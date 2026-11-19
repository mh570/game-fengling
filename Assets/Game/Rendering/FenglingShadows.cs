using UnityEngine;
using UnityEngine.Rendering;

namespace Fengling.Rendering
{
    /// <summary>
    /// 方向光阴影：RFloat 线性深度阴影图。
    /// 不再依赖平台 Shadowmap 深度格式，和场景线性深度用同一套 DepthOnly pass。
    /// </summary>
    public sealed class FenglingShadows
    {
        private RenderTexture _shadowMap;
        private Matrix4x4 _viewMatrix;
        private Matrix4x4 _projMatrix;

        public int Resolution = 2048;

        private void EnsureTexture(int resolution)
        {
            if (_shadowMap != null && _shadowMap.width == resolution) return;
            if (_shadowMap != null)
            {
                _shadowMap.Release();
                Object.DestroyImmediate(_shadowMap);
            }

            _shadowMap = new RenderTexture(resolution, resolution, 24, RenderTextureFormat.RFloat)
            {
                name = "FenglingShadowLinear",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            _shadowMap.Create();
        }

        public void Render(ScriptableRenderContext context, Camera camera, CullingResults culling,
                           Vector3 lightDirection, float distance, int resolution,
                           RenderTargetIdentifier restoreTarget, RenderTargetIdentifier restoreDepthTarget)
        {
            Resolution = Mathf.Clamp(resolution, 256, 4096);
            EnsureTexture(Resolution);

            var center = camera.transform.position;
            var lightRot = Quaternion.LookRotation(-lightDirection.normalized, Vector3.up);
            var lightPos = center - lightDirection.normalized * (distance * 0.5f);
            _viewMatrix = Matrix4x4.TRS(lightPos, lightRot, Vector3.one).inverse;

            float half = distance * 0.5f;
            _projMatrix = Matrix4x4.Ortho(-half, half, -half, half, 0.01f, distance * 2f);

            var cmd = CommandBufferPool.Get("Fengling.Shadows");
            cmd.SetRenderTarget(_shadowMap);
            cmd.ClearRenderTarget(true, true, new Color(1000f, 0f, 0f, 1f));
            cmd.SetViewProjectionMatrices(_viewMatrix, _projMatrix);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);

            var drawing = new DrawingSettings(new ShaderTagId("DepthOnly"), new SortingSettings(camera))
            {
                perObjectData = PerObjectData.None,
            };
            var filtering = new FilteringSettings(RenderQueueRange.opaque);
            context.DrawRenderers(culling, ref drawing, ref filtering);

            var restore = CommandBufferPool.Get("Fengling.ShadowRestore");
            restore.SetRenderTarget(restoreTarget, restoreDepthTarget);
            context.ExecuteCommandBuffer(restore);
            CommandBufferPool.Release(restore);
            context.SetupCameraProperties(camera);

            var globals = CommandBufferPool.Get("Fengling.ShadowGlobals");
            globals.SetGlobalTexture("_FenglingShadowMap", _shadowMap);
            globals.SetGlobalMatrix("_FenglingShadowView", _viewMatrix);
            globals.SetGlobalMatrix("_FenglingShadowProj", _projMatrix);
            globals.SetGlobalFloat("_FenglingShadowDistance", distance);
            globals.SetGlobalFloat("_FenglingShadowEnabled", 1f);
            context.ExecuteCommandBuffer(globals);
            CommandBufferPool.Release(globals);
        }

        public void Disable()
        {
            Shader.SetGlobalFloat("_FenglingShadowEnabled", 0f);
            Shader.SetGlobalTexture("_FenglingShadowMap", Texture2D.blackTexture);
        }
    }
}
