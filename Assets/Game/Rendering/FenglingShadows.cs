using UnityEngine;
using UnityEngine.Rendering;

namespace Fengling.Rendering
{
    /// <summary>
    /// 方向光阴影（第一版：单级）。
    ///
    /// 流程：以相机为中心、沿光线方向拟合一个正交投影，
    /// 把场景的 ShadowCaster pass 画进深度图，再把矩阵和图交给 shader。
    ///
    /// 级联（4 级）在这个类的基础上扩展：把视锥体按距离切成 N 段，
    /// 每段算一次矩阵，渲进图集的不同 tile。先保证单级正确。
    /// </summary>
    public sealed class FenglingShadows
    {
        private const string ShadowCasterTag = "ShadowCaster";

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

            _shadowMap = new RenderTexture(resolution, resolution, 24, RenderTextureFormat.Shadowmap)
            {
                name = "FenglingShadowMap",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            _shadowMap.Create();
        }

        public void Render(ScriptableRenderContext context, Camera camera, CullingResults culling,
                           Vector3 lightDirection, float distance, int resolution)
        {
            Resolution = Mathf.Clamp(resolution, 256, 4096);
            EnsureTexture(Resolution);

            // 以相机位置为中心，沿光线方向拉远，构造正交投影
            var center = camera.transform.position;
            var lightRot = Quaternion.LookRotation(-lightDirection.normalized, Vector3.up);
            var lightPos = center - lightDirection.normalized * (distance * 0.5f);

            _viewMatrix = Matrix4x4.TRS(lightPos, lightRot, Vector3.one).inverse;

            float half = distance * 0.5f;
            _projMatrix = Matrix4x4.Ortho(-half, half, -half, half, 0.01f, distance * 2f);

            var cmd = CommandBufferPool.Get("Fengling.Shadows");
            cmd.SetRenderTarget(_shadowMap);
            cmd.ClearRenderTarget(true, true, Color.black);
            cmd.SetViewProjectionMatrices(_viewMatrix, _projMatrix);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);

            var drawing = new DrawingSettings(new ShaderTagId(ShadowCasterTag), new SortingSettings(camera));
            var filtering = new FilteringSettings(RenderQueueRange.all);
            context.DrawRenderers(culling, ref drawing, ref filtering);

            // 恢复相机状态：矩阵 + 渲染目标
            // 少了 SetRenderTarget，后面的不透明/透明会全部画进阴影图里，屏幕上只剩清屏色
            var restore = CommandBufferPool.Get("Fengling.ShadowRestore");
            restore.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            context.ExecuteCommandBuffer(restore);
            CommandBufferPool.Release(restore);
            context.SetupCameraProperties(camera);

            // 传给 shader
            var cmd2 = CommandBufferPool.Get("Fengling.ShadowGlobals");
            cmd2.SetGlobalTexture("_FenglingShadowMap", _shadowMap);
            cmd2.SetGlobalMatrix("_FenglingShadowView", _viewMatrix);
            cmd2.SetGlobalMatrix("_FenglingShadowProj", _projMatrix);
            cmd2.SetGlobalFloat("_FenglingShadowDistance", distance);
            cmd2.SetGlobalFloat("_FenglingShadowEnabled", 1f);
            context.ExecuteCommandBuffer(cmd2);
            CommandBufferPool.Release(cmd2);
        }

        public void Disable()
        {
            // 关闭时必须显式复位：否则上一次会话/帧留下的 1 会让 Lit shader
            // 继续采样已经失效的阴影图，表现为地形整片发黑。
            Shader.SetGlobalFloat("_FenglingShadowEnabled", 0f);
            Shader.SetGlobalTexture("_FenglingShadowMap", Texture2D.blackTexture);
        }
    }
}
