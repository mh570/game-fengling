using UnityEngine;
using UnityEngine.Rendering;

namespace Fengling.Rendering
{
    /// <summary>
    /// 方向光 4 级级联阴影：4 张独立 RFloat 线性深度图。
    /// 级联比例/裁剪球按 Catlike/URP 的 ComputeDirectionalShadowMatricesAndCullingPrimitives 路径。
    /// </summary>
    public sealed class FenglingShadows
    {
        public const int CascadeCount = 4;
        private static readonly Vector3 CascadeRatios = new Vector3(0.1f, 0.25f, 0.5f);

        private readonly RenderTexture[] _maps = new RenderTexture[CascadeCount];
        private readonly Matrix4x4[] _views = new Matrix4x4[CascadeCount];
        private readonly Matrix4x4[] _vps = new Matrix4x4[CascadeCount];
        private readonly Vector4[] _spheres = new Vector4[CascadeCount];

        public int Resolution = 1024;

        private void EnsureMap(int index, int size)
        {
            if (_maps[index] != null && _maps[index].width == size) return;
            if (_maps[index] != null)
            {
                _maps[index].Release();
                Object.DestroyImmediate(_maps[index]);
            }
            _maps[index] = new RenderTexture(size, size, 24, RenderTextureFormat.RFloat)
            {
                name = "FenglingShadowCascade" + index,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            _maps[index].Create();
        }

        public void Render(ScriptableRenderContext context, Camera camera, CullingResults culling,
                           Vector3 lightDirection, float distance, int resolution,
                           RenderTargetIdentifier restoreTarget, RenderTargetIdentifier restoreDepthTarget)
        {
            Resolution = Mathf.Clamp(resolution, 256, 2048);
            for (int i = 0; i < CascadeCount; i++) EnsureMap(i, Resolution);

            int lightIndex = -1;
            var visible = culling.visibleLights;
            var sun = RenderSettings.sun;
            for (int i = 0; i < visible.Length; i++)
            {
                if (visible[i].lightType == LightType.Directional &&
                    (sun == null || visible[i].light == sun))
                {
                    lightIndex = i;
                    break;
                }
            }
            if (lightIndex < 0)
            {
                Disable();
                return;
            }

            for (int i = 0; i < CascadeCount; i++)
            {
                culling.ComputeDirectionalShadowMatricesAndCullingPrimitives(
                    lightIndex, i, CascadeCount, CascadeRatios, Resolution, 0.1f,
                    out Matrix4x4 view, out Matrix4x4 proj, out ShadowSplitData splitData);

                _views[i] = view;
                _vps[i] = proj * view;
                _spheres[i] = splitData.cullingSphere;

                var setup = CommandBufferPool.Get("Fengling.ShadowCascade" + i);
                setup.SetRenderTarget(_maps[i]);
                setup.ClearRenderTarget(true, true, new Color(1000f, 0f, 0f, 1f));
                setup.SetViewProjectionMatrices(view, proj);
                context.ExecuteCommandBuffer(setup);
                CommandBufferPool.Release(setup);

                var drawing = new DrawingSettings(new ShaderTagId("DepthOnly"), new SortingSettings(camera))
                {
                    perObjectData = PerObjectData.None,
                };
                var filtering = new FilteringSettings(RenderQueueRange.opaque);
                context.DrawRenderers(culling, ref drawing, ref filtering);
            }

            var restore = CommandBufferPool.Get("Fengling.ShadowRestore");
            restore.SetRenderTarget(restoreTarget, restoreDepthTarget);
            context.ExecuteCommandBuffer(restore);
            CommandBufferPool.Release(restore);
            context.SetupCameraProperties(camera);

            for (int i = 0; i < CascadeCount; i++)
            {
                Shader.SetGlobalTexture("_FenglingShadowMap" + i, _maps[i]);
                Shader.SetGlobalMatrix("_FenglingShadowView" + i, _views[i]);
                Shader.SetGlobalMatrix("_FenglingShadowVP" + i, _vps[i]);
            }
            Shader.SetGlobalVectorArray("_FenglingShadowSpheres", _spheres);
            Shader.SetGlobalFloat("_FenglingShadowTexel", 1f / Resolution);
            Shader.SetGlobalFloat("_FenglingShadowEnabled", 1f);
        }

        public void Disable()
        {
            Shader.SetGlobalFloat("_FenglingShadowEnabled", 0f);
        }
    }
}
