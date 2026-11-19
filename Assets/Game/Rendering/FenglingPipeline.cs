using UnityEngine;
using UnityEngine.Rendering;

namespace Fengling.Rendering
{
    /// <summary>
    /// 最小 SRP：清屏 + 不透明 + 透明，两段绘制。
    /// 阴影与天空盒在后续步骤加，先保证"能画出东西"。
    /// </summary>
    public sealed class FenglingPipeline : RenderPipeline
    {
        private readonly FenglingPipelineSettings _settings;
        private readonly FenglingShadows _shadows = new FenglingShadows();

        private static readonly ShaderTagId[] OpaqueTags =
        {
            new ShaderTagId("FenglingForward"),
            new ShaderTagId("SRPDefaultUnlit"),
        };

        private static readonly ShaderTagId[] TransparentTags =
        {
            new ShaderTagId("SRPDefaultUnlit"),
        };

        public FenglingPipeline(FenglingPipelineSettings settings)
        {
            _settings = settings;
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

            var cmd = CommandBufferPool.Get("Fengling.Clear");

            // 清屏：先用纯色天空，程序化天空盒后面替换这里
            var clearColor = camera.clearFlags == CameraClearFlags.SolidColor
                ? camera.backgroundColor
                : _settings.skyColor;

            cmd.ClearRenderTarget(
                camera.clearFlags != CameraClearFlags.SolidColor,
                camera.clearFlags != CameraClearFlags.Nothing,
                clearColor);

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
                                _settings.shadowDistance, _settings.shadowResolution);
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

            // ---- 天空盒（先跳过，第二阶段换成程序化）
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

            context.Submit();
        }
    }
}
