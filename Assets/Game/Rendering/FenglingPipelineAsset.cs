using UnityEngine;
using UnityEngine.Rendering;

namespace Fengling.Rendering
{
    /// <summary>
    /// 管线资产。
    ///
    /// 必须重写 defaultMaterial / defaultShader / renderPipelineShaderTag：
    ///   - 不重写 defaultMaterial，材质丢失的渲染器会被画成洋红（Unity 的错误材质）
    ///   - 不重写 shaderTag，打包时该管线的变体会被剥离
    /// </summary>
    [CreateAssetMenu(menuName = "Fengling/Render Pipeline", fileName = "FenglingPipeline")]
    public sealed class FenglingPipelineAsset : RenderPipelineAsset<FenglingPipeline>
    {
        [SerializeField] private FenglingPipelineSettings settings = new FenglingPipelineSettings();

        private Material _defaultMaterial;

        protected override RenderPipeline CreatePipeline()
        {
            return new FenglingPipeline(settings);
        }

        public override string renderPipelineShaderTag => "FenglingForward";

        public override Shader defaultShader
        {
            get
            {
                var s = Shader.Find("Fengling/Lit");
                if (s != null) return s;
                return Shader.Find("Hidden/InternalErrorShader");
            }
        }

        public override Material defaultMaterial
        {
            get
            {
                if (_defaultMaterial != null) return _defaultMaterial;

                var shader = Shader.Find("Fengling/Lit");
                if (shader == null) return null;

                _defaultMaterial = new Material(shader) { name = "FenglingDefaultMaterial", hideFlags = HideFlags.HideAndDontSave };
                return _defaultMaterial;
            }
        }
    }
}
