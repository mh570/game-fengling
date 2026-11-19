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

        [Tooltip("阴影开关。采样实现未完成前保持关闭，否则地形会整片发黑")]
        public bool shadowsEnabled = false;

        [Tooltip("阴影分辨率（完整阴影阶段使用）")]
        public int shadowResolution = 2048;

        [Tooltip("阴影距离")]
        public float shadowDistance = 80f;

        [Header("Post Processing")]
        [Tooltip("曝光（EV）。0 = 原始亮度，1 = 提亮一档")]
        [Range(-5f, 5f)] public float exposure = 0.0f;

        [Tooltip("Bloom 强度")]
        [Range(0f, 2f)] public float bloomIntensity = 0.15f;

        [Tooltip("Bloom 高光阈值")]
        [Range(0f, 4f)] public float bloomThreshold = 1.2f;

        [Tooltip("对比度")]
        [Range(0f, 2f)] public float contrast = 1.05f;

        [Tooltip("饱和度")]
        [Range(0f, 2f)] public float saturation = 1.0f;
    }
}
