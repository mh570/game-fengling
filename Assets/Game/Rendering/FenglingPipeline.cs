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
        private Light _fillLight;
        private Camera _reflectionCamera;
        private RenderTexture _reflectionRT;

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
                width, height, 0, RenderTextureFormat.DefaultHDR, RenderTextureReadWrite.Linear);
            colorRT.name = "FenglingColor";
            var depthRT = RenderTexture.GetTemporary(
                width, height, 24, RenderTextureFormat.Depth, RenderTextureReadWrite.Linear);
            depthRT.name = "FenglingDepth";

            var colorTarget = (RenderTargetIdentifier)colorRT;
            var depthTarget = (RenderTargetIdentifier)depthRT;

            var cmd = CommandBufferPool.Get("Fengling.Setup");
            cmd.SetRenderTarget(colorTarget, depthTarget);

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

            if (_fillLight == null)
            {
                foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                {
                    if (light != null && light.type == LightType.Directional && light.name == "Fill Light")
                    {
                        _fillLight = light;
                        break;
                    }
                }
            }
            if (_fillLight != null)
            {
                var fillDir = -_fillLight.transform.forward;
                cmd.SetGlobalVector("_FenglingFillDir", new Vector4(fillDir.x, fillDir.y, fillDir.z, 0f));
                cmd.SetGlobalColor("_FenglingFillColor",
                    _fillLight.color * (_fillLight.intensity * 0.08f));
            }
            else
            {
                cmd.SetGlobalColor("_FenglingFillColor", Color.black);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);

            var culling = context.Cull(ref cullingParams);

            // ---- 平面反射（半分辨率）
            float waterLevel = Shader.GetGlobalFloat("_FenglingWaterLevel");
            if (camera.cameraType != CameraType.Reflection &&
                camera.transform.position.y > waterLevel + 0.01f)
            {
                RenderReflection(context, camera, waterLevel, colorTarget, depthTarget, width, height);
            }
            else
            {
                FenglingReflection.Publish(null, Matrix4x4.identity);
            }

            // 阴影：在不透明之前画进深度图
            var sunForShadow = RenderSettings.sun;
            if (_settings.shadowsEnabled && sunForShadow != null)
            {
                _shadows.Render(context, camera, culling, sunForShadow.transform.forward,
                                _settings.shadowDistance, _settings.shadowResolution,
                                colorTarget, depthTarget);
            }
            else
            {
                // 必须复位全局开关；否则 shader 可能沿用上一帧/上一次会话的 1。
                _shadows.Disable();
            }

            // Shadow pass 会切换 RT/矩阵；显式重新绑定主相机目标和属性，
            // 避免后续不透明/透明继续画进 shadow map。
            context.SetupCameraProperties(camera);
            var rebind = CommandBufferPool.Get("Fengling.MainTargetRebind");
            rebind.SetRenderTarget(colorTarget, depthTarget);
            context.ExecuteCommandBuffer(rebind);
            CommandBufferPool.Release(rebind);

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

            // ---- 场景颜色 / 线性深度拷贝（全分辨率）
            // 水面在透明队列里，必须先拷贝不透明+天空的颜色，才能做折射。
            var sceneColorRT = RenderTexture.GetTemporary(
                width, height, 0, RenderTextureFormat.DefaultHDR, RenderTextureReadWrite.Linear);
            sceneColorRT.name = "FenglingSceneColor";
            var linearDepthRT = RenderTexture.GetTemporary(
                width, height, 24, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
            linearDepthRT.name = "FenglingLinearDepth";

            var copyCmd = CommandBufferPool.Get("Fengling.SceneColor");
            copyCmd.Blit(colorRT, sceneColorRT);
            context.ExecuteCommandBuffer(copyCmd);
            CommandBufferPool.Release(copyCmd);

            var depthCmd = CommandBufferPool.Get("Fengling.LinearDepth");
            depthCmd.SetRenderTarget(linearDepthRT);
            depthCmd.ClearRenderTarget(true, true, new Color(1000f, 0f, 0f, 1f));
            context.ExecuteCommandBuffer(depthCmd);
            CommandBufferPool.Release(depthCmd);

            var depthDrawing = new DrawingSettings(new ShaderTagId("DepthOnly"), new SortingSettings(camera))
            {
                perObjectData = PerObjectData.None,
            };
            var depthFilter = new FilteringSettings(RenderQueueRange.opaque);
            context.DrawRenderers(culling, ref depthDrawing, ref depthFilter);
            context.Submit();

            var restoreCmd = CommandBufferPool.Get("Fengling.DepthRestore");
            restoreCmd.SetRenderTarget(colorTarget, depthTarget);
            restoreCmd.SetGlobalTexture("_FenglingSceneDepth", linearDepthRT);
            restoreCmd.SetGlobalTexture("_FenglingSceneColor", sceneColorRT);
            restoreCmd.SetGlobalFloat("_FenglingSceneDepthAvailable", 1f);
            restoreCmd.SetGlobalFloat("_FenglingSceneColorAvailable", 1f);
            context.ExecuteCommandBuffer(restoreCmd);
            CommandBufferPool.Release(restoreCmd);

            // 材质属性绑定必须在管线内、透明 pass 之前完成；
            // 在 WaterSurfaceRenderer.Update 里读全局会拿到上一帧已释放的 RT。
            FenglingSceneTextures.Publish(linearDepthRT, sceneColorRT);

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
            RenderTexture.ReleaseTemporary(depthRT);
            RenderTexture.ReleaseTemporary(sceneColorRT);
            RenderTexture.ReleaseTemporary(linearDepthRT);
        }

        private void RenderReflection(ScriptableRenderContext context, Camera camera, float waterLevel,
                                      RenderTargetIdentifier mainColor, RenderTargetIdentifier mainDepth,
                                      int width, int height)
        {
            int rw = Mathf.Max(1, width / 2);
            int rh = Mathf.Max(1, height / 2);
            if (_reflectionRT == null || _reflectionRT.width != rw || _reflectionRT.height != rh)
            {
                if (_reflectionRT != null)
                {
                    _reflectionRT.Release();
                    Object.DestroyImmediate(_reflectionRT);
                }
                _reflectionRT = new RenderTexture(rw, rh, 24, RenderTextureFormat.DefaultHDR)
                {
                    name = "FenglingReflectionHalf",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
                _reflectionRT.Create();
            }

            EnsureReflectionCamera();
            var t = camera.transform;
            Vector3 pos = t.position;
            pos.y = 2f * waterLevel - pos.y;
            Vector3 forward = t.forward;
            forward.y = -forward.y;
            Vector3 up = t.up;
            up.y = -up.y;

            var cam = _reflectionCamera;
            cam.CopyFrom(camera);
            cam.enabled = true;
            cam.cameraType = CameraType.Reflection;
            cam.targetTexture = _reflectionRT;
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(forward, up));
            cam.allowHDR = true;
            cam.allowMSAA = false;
            cam.clearFlags = CameraClearFlags.Skybox;

            var clipPlane = CameraSpacePlane(cam, new Vector3(0f, waterLevel, 0f), Vector3.up, 1f);
            cam.projectionMatrix = cam.CalculateObliqueMatrix(clipPlane);

            if (!cam.TryGetCullingParameters(out var reflectionParams))
            {
                cam.enabled = false;
                FenglingReflection.Publish(null, Matrix4x4.identity);
                context.SetupCameraProperties(camera);
                return;
            }

            var reflectionCulling = context.Cull(ref reflectionParams);
            context.SetupCameraProperties(cam);

            var clear = CommandBufferPool.Get("Fengling.ReflectionClear");
            clear.SetRenderTarget(_reflectionRT);
            clear.ClearRenderTarget(true, true, _settings.skyColor);
            context.ExecuteCommandBuffer(clear);
            CommandBufferPool.Release(clear);

            var drawing = new DrawingSettings(OpaqueTags[0], new SortingSettings(cam))
            {
                perObjectData = PerObjectData.None,
            };
            for (int i = 1; i < OpaqueTags.Length; i++)
                drawing.SetShaderPassName(i, OpaqueTags[i]);

            var filter = new FilteringSettings(RenderQueueRange.opaque);
            GL.invertCulling = true;
            context.DrawRenderers(reflectionCulling, ref drawing, ref filter);
            // 俯视反射不画天空盒：云会被投成大片白斑。
            // 反射只保留岸线/地形/大物体，天空用清屏色兜底。
            GL.invertCulling = false;
            context.Submit();

            context.SetupCameraProperties(camera);
            var restore = CommandBufferPool.Get("Fengling.ReflectionRestore");
            restore.SetRenderTarget(mainColor, mainDepth);
            context.ExecuteCommandBuffer(restore);
            CommandBufferPool.Release(restore);
            cam.enabled = false;

            var vp = cam.projectionMatrix * cam.worldToCameraMatrix;
            Shader.SetGlobalTexture("_FenglingReflectionTex", _reflectionRT);
            Shader.SetGlobalMatrix("_FenglingReflectionVP", vp);
            Shader.SetGlobalFloat("_FenglingReflectionEnabled", 1f);
            FenglingReflection.Publish(_reflectionRT, vp);
        }

        private void EnsureReflectionCamera()
        {
            if (_reflectionCamera != null) return;
            var go = new GameObject("FenglingReflectionCamera")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            _reflectionCamera = go.AddComponent<Camera>();
            _reflectionCamera.enabled = false;
        }

        private static Vector4 CameraSpacePlane(Camera cam, Vector3 pos, Vector3 normal, float sideSign)
        {
            Vector3 offsetPos = pos + normal * sideSign * cam.nearClipPlane;
            Matrix4x4 m = cam.worldToCameraMatrix;
            Vector3 cpos = m.MultiplyPoint(offsetPos);
            Vector3 cnormal = m.MultiplyVector(normal).normalized * sideSign;
            return new Vector4(cnormal.x, cnormal.y, cnormal.z, -Vector3.Dot(cpos, cnormal));
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
