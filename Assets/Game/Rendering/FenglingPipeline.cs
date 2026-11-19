using UnityEngine;
using UnityEngine.Rendering;

namespace Fengling.Rendering
{
    /// <summary>
    /// 最小 SRP：场景先渲到 HDR RT，再做曝光/Bloom/ACES，最后输出到相机目标。
    /// 水面/天空/阴影的顺序保持不变；后处理是这条管线的一等公民。
    /// </summary>
    public sealed class FenglingPipeline : RenderPipeline
    {
        private readonly FenglingPipelineSettings _settings;
        private readonly FenglingShadows _shadows = new FenglingShadows();
        private readonly Material _postMaterial;

        private static readonly ShaderTagId[] OpaqueTags =
        {
            new ShaderTagId("FenglingForward"),
            new ShaderTagId("SRPDefaultUnlit"),
        };

        private static readonly ShaderTagId[] TransparentTags =
        {
            new ShaderTagId("SRPDefaultUnlit"),
        };

        public FenglingPipeline(FenglingPipelineSettings settings, Material postProcessTemplate)
        {
            _settings = settings;

            if (postProcessTemplate != null)
            {
                _postMaterial = new Material(postProcessTemplate) { name = "FenglingPostProcess (instance)" };
            }
            else
            {
                var shader = Shader.Find("Fengling/PostProcess");
                if (shader != null)
                    _postMaterial = new Material(shader) { name = "FenglingPostProcess (instance)" };
            }
        }

        protected override void Render(ScriptableRenderContext context, Camera[] cameras)
        {
            foreach (var camera in cameras)
            {
                if (camera == null) continue;
                if (camera.cameraType == CameraType.Reflection) continue;
                RenderCamera(context, camera);
            }
        }

        private void RenderCamera(ScriptableRenderContext context, Camera camera)
        {
            if (!camera.TryGetCullingParameters(out var cullingParams)) return;

            context.SetupCameraProperties(camera);

            int width = Mathf.Max(1, camera.pixelWidth);
            int height = Mathf.Max(1, camera.pixelHeight);

            var colorRT = RenderTexture.GetTemporary(
                width, height, 24, RenderTextureFormat.DefaultHDR, RenderTextureReadWrite.Linear);
            colorRT.name = "FenglingColor";
            var colorTarget = (RenderTargetIdentifier)colorRT;

            var cmd = CommandBufferPool.Get("Fengling.Setup");
            cmd.SetRenderTarget(colorTarget);

            // SolidColor/Skybox 清颜色；Depth 只清深度；Nothing 都不清。
            bool clearColor = camera.clearFlags == CameraClearFlags.SolidColor ||
                              camera.clearFlags == CameraClearFlags.Skybox;
            bool clearDepth = camera.clearFlags != CameraClearFlags.Nothing;
            var clearColorValue = camera.clearFlags == CameraClearFlags.SolidColor
                ? camera.backgroundColor
                : _settings.skyColor;

            cmd.ClearRenderTarget(clearDepth, clearColor, clearColorValue);

            // 主光全局参数：自建管线没有 HDRP 的 LightLoop，统一用全局变量传给 shader
            var sun = RenderSettings.sun;
            Vector4 sunDir = sun != null ? (Vector4)(-sun.transform.forward) : new Vector4(0f, 1f, 0f, 0f);
            // 曝光归一：Light intensity 9 直接当颜色会过曝，自建管线没有 tonemap
            Color sunColor = sun != null ? sun.color * (sun.intensity * 0.12f) : Color.white;
            cmd.SetGlobalVector("_FenglingSunDir", sunDir);
            cmd.SetGlobalColor("_FenglingSunColor", sunColor);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);

            var culling = context.Cull(ref cullingParams);

            // 阴影：在不透明之前画进深度图
            var sunForShadow = RenderSettings.sun;
            if (_settings.shadowsEnabled && sunForShadow != null)
            {
                _shadows.Render(context, camera, culling, sunForShadow.transform.forward,
                                _settings.shadowDistance, _settings.shadowResolution, colorTarget);
            }
            else
            {
                // 必须复位全局开关；否则 shader 可能沿用上一帧/上一次会话的 1。
                _shadows.Disable();
            }

            // ---- 不透明
            var opaqueSettings = new DrawingSettings(OpaqueTags[0], new SortingSettings(camera))
            {
                perObjectData = PerObjectData.None,
            };
            for (int i = 1; i < OpaqueTags.Length; i++)
                opaqueSettings.SetShaderPassName(i, OpaqueTags[i]);

            var opaqueFilter = new FilteringSettings(RenderQueueRange.opaque);
            context.DrawRenderers(culling, ref opaqueSettings, ref opaqueFilter);

            // ---- 天空盒
            if (camera.clearFlags == CameraClearFlags.Skybox)
                context.DrawSkybox(camera);

            // ---- 透明
            var transparentSorting = new SortingSettings(camera) { criteria = SortingCriteria.CommonTransparent };
            var transparentSettings = new DrawingSettings(TransparentTags[0], transparentSorting)
            {
                perObjectData = PerObjectData.None,
            };
            var transparentFilter = new FilteringSettings(RenderQueueRange.transparent);
            context.DrawRenderers(culling, ref transparentSettings, ref transparentFilter);

            PostProcess(context, camera, colorRT, width, height);
            RenderTexture.ReleaseTemporary(colorRT);
        }

        private void PostProcess(ScriptableRenderContext context, Camera camera, RenderTexture colorRT,
                                 int width, int height)
        {
            var target = camera.targetTexture != null
                ? (RenderTargetIdentifier)camera.targetTexture
                : BuiltinRenderTextureType.CameraTarget;

            if (_postMaterial == null)
            {
                var direct = CommandBufferPool.Get("Fengling.Blit");
                direct.Blit(colorRT, target);
                context.ExecuteCommandBuffer(direct);
                context.Submit();
                CommandBufferPool.Release(direct);
                return;
            }

            int bloomWidth = Mathf.Max(1, width / 2);
            int bloomHeight = Mathf.Max(1, height / 2);
            var bright = RenderTexture.GetTemporary(
                bloomWidth, bloomHeight, 0, RenderTextureFormat.DefaultHDR, RenderTextureReadWrite.Linear);
            var blur = RenderTexture.GetTemporary(
                bloomWidth, bloomHeight, 0, RenderTextureFormat.DefaultHDR, RenderTextureReadWrite.Linear);

            var cmd = CommandBufferPool.Get("Fengling.PostProcess");
            cmd.SetGlobalFloat("_FenglingExposure", _settings.exposure);
            cmd.SetGlobalFloat("_FenglingBloomIntensity", _settings.bloomIntensity);
            cmd.SetGlobalFloat("_FenglingBloomThreshold", _settings.bloomThreshold);
            cmd.SetGlobalFloat("_FenglingContrast", _settings.contrast);
            cmd.SetGlobalFloat("_FenglingSaturation", _settings.saturation);
            cmd.SetGlobalVector("_FenglingBlurTexel", new Vector4(1f / bloomWidth, 1f / bloomHeight, 0f, 0f));

            // 1. 提亮：全分辨率 → 半分辨率
            cmd.Blit(colorRT, bright, _postMaterial, 1);

            // 2. 横向模糊
            cmd.SetGlobalVector("_FenglingBlurDir", new Vector4(1f, 0f, 0f, 0f));
            cmd.Blit(bright, blur, _postMaterial, 2);

            // 3. 纵向模糊
            cmd.SetGlobalVector("_FenglingBlurDir", new Vector4(0f, 1f, 0f, 0f));
            cmd.Blit(blur, bright, _postMaterial, 2);

            // 4. 合成：场景 + Bloom → 屏幕
            cmd.SetGlobalTexture("_BloomTex", bright);
            cmd.Blit(colorRT, target, _postMaterial, 0);

            context.ExecuteCommandBuffer(cmd);
            context.Submit();
            CommandBufferPool.Release(cmd);

            RenderTexture.ReleaseTemporary(bright);
            RenderTexture.ReleaseTemporary(blur);
        }
    }
}
