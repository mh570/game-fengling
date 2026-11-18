using UnityEngine;
using UnityEngine.Rendering;

namespace Fengling.Rendering
{
    /// <summary>
    /// 管线配置：目前只有天空色和阴影开关，后续逐步补充。
    /// </summary>
    [System.Serializable]
    public sealed class FenglingPipelineSettings
    {
        public Color skyColor = new Color(0.42f, 0.62f, 0.85f, 1f);

        [Tooltip("阴影分辨率（完整阴影阶段使用）")]
        public int shadowResolution = 2048;

        [Tooltip("阴影距离")]
        public float shadowDistance = 80f;
    }
}
