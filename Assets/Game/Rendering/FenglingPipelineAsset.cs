using UnityEngine;
using UnityEngine.Rendering;

namespace Fengling.Rendering
{
    /// <summary>
    /// 管线资产：挂到 Project Settings → Graphics → Scriptable Render Pipeline Settings。
    /// </summary>
    [CreateAssetMenu(menuName = "Fengling/Render Pipeline", fileName = "FenglingPipeline")]
    public sealed class FenglingPipelineAsset : RenderPipelineAsset
    {
        [SerializeField] private FenglingPipelineSettings settings = new FenglingPipelineSettings();

        protected override RenderPipeline CreatePipeline()
        {
            return new FenglingPipeline(settings);
        }
    }
}
