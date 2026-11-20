using UnityEngine;

namespace Fengling.Time
{
    /// <summary>
    /// 天气状态：目前只做视觉层，优先驱动云量/阴晴、日月遮挡和阴影强度。
    /// 后续雨/雾/风扩展到这里，不散落到 TimeOfDaySystem。
    /// </summary>
    [AddComponentMenu("Fengling/Time/Weather System")]
    public sealed class WeatherSystem : MonoBehaviour
    {
        public static WeatherSystem Instance { get; private set; }

        [Header("云 / 阴晴")]
        [Range(0f, 1f)] public float cloudCover = 0.25f;
        [Range(0f, 1f)] public float haze = 0.1f;
        [Range(0f, 1f)] public float fog = 0f;
        [Range(0f, 1f)] public float rain = 0f;
        [Range(0f, 1f)] public float wind = 0.2f;

        public float SunVisibility => Mathf.Lerp(1f, 0.22f, cloudCover);
        public float MoonVisibility => Mathf.Lerp(1f, 0.1f, cloudCover);
        public float ShadowVisibility => Mathf.Lerp(1f, 0.18f, cloudCover);
        public float CloudOpacity => Mathf.Clamp01(cloudCover * 0.9f + haze * 0.15f);

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
