using UnityEngine;
using UnityEngine.Rendering;

namespace Fengling.Rendering
{
    /// <summary>
    /// 边缘亮线后处理（阶段 1：深度差 Sobel）。
    ///
    /// 参考：
    ///   tantaneity/unity-urp-outline-postprocess（Sobel + 深度/法线）
    ///   Ronja 教程 019 OutlinesPostprocessed（深度权重 + 法线权重分开）
    ///
    /// 阶段 1 只用深度：能抓到轮廓（水岸、悬崖、树与地），
    /// 同深度的两个平面交界（路面贴在地上）需要法线，放阶段 2。
    ///
    /// 为什么能直接采样深度：管线里已经生成了 _FenglingSceneDepth（线性深度 RT）。
    /// </summary>
    public sealed class FenglingOutline
    {
        private Material _material;
        private bool _shaderMissing;

        public bool Enabled = true;
        public Color LineColor = Color.white;
        [Range(0.001f, 0.5f)] public float DepthThreshold = 0.06f;
        [Range(0.5f, 4f)] public float LineWidth = 1.5f;
        public float DashScale = 90f;
        [Range(0f, 1f)] public float DashDensity = 0.55f;
        [Range(0f, 1f)] public float Opacity = 0.85f;

        public void Render(ScriptableRenderContext context, Camera camera,
                           RenderTargetIdentifier colorTarget, RenderTargetIdentifier depthTarget)
        {
            if (!Enabled) return;

            if (_material == null && !_shaderMissing)
            {
                var shader = Shader.Find("Fengling/Outline");
                if (shader == null)
                {
                    _shaderMissing = true;
                    Debug.LogWarning("[Outline] 找不到 shader 'Fengling/Outline'");
                    return;
                }
                _material = new Material(shader) { name = "FenglingOutline (instance)" };
            }
            if (_material == null) return;

            _material.SetColor("_LineColor", LineColor);
            _material.SetFloat("_DepthThreshold", DepthThreshold);
            _material.SetFloat("_LineWidth", LineWidth);
            _material.SetFloat("_DashScale", DashScale);
            _material.SetFloat("_DashDensity", DashDensity);
            _material.SetFloat("_Opacity", Opacity);

            var cmd = CommandBufferPool.Get("Fengling.Outline");
            cmd.SetRenderTarget(colorTarget, depthTarget);
            cmd.DrawProcedural(Matrix4x4.identity, _material, 0, MeshTopology.Triangles, 3);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}
