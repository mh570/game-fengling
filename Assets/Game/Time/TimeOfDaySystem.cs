using UnityEngine;

namespace Fengling.Time
{
    /// <summary>
    /// 24h 昼夜核心：时间推进、太阳/月亮方位、天空参数、环境光。
    /// 只负责“时间 → 方向/颜色”，不直接改水面/阴影级联。
    /// </summary>
    [AddComponentMenu("Fengling/Time/Time Of Day System")]
    public sealed class TimeOfDaySystem : MonoBehaviour
    {
        [Header("时钟")]
        [Range(0f, 24f)] public float timeOfDay = 8f;
        public int day = 1;
        [Range(0, 3)] public int season = 1; // 0春 1夏 2秋 3冬
        public int year = 1;
        public int calendarYear = 2026;
        [Range(1, 12)] public int calendarMonth = 11;
        [Range(1, 31)] public int calendarDay = 20;
        [Range(-180f, 180f)] public float longitude = 0f;
        public bool paused = false;
        [Tooltip("1 现实秒 = 多少游戏分钟；1 = 24 现实分钟一天")]
        [Range(0.01f, 60f)] public float gameMinutesPerSecond = 1f;
        [Range(0f, 20f)] public float speedMultiplier = 1f;

        [Header("太阳/月亮")]
        public Light sun;
        public Light moon;
        [Range(-60f, 60f)] public float latitude = 35f;
        public float sunMaxIntensity = 8f;
        public float moonMaxIntensity = 3f;
        [Tooltip("月相校准：0 = 由日期推算；正数把月相向后推")]
        public float moonPhaseOffsetDays = 0f;
        public float MoonIlluminatedFraction { get; private set; }
        public float MoonAgeDays { get; private set; }
        public float MoonPhaseAngle { get; private set; }

        [Header("天空")]
        public Material skyMaterial;
        public WeatherSystem weather;
        public bool driveAmbient = true;
        public float dayAmbient = 1.2f;
        public float nightAmbient = 0.18f;

        [Header("调试 UI")]
        public bool showHud = true;

        private Material _skyInstance;

        private static readonly int SunDirectionId = Shader.PropertyToID("_SunDirection");
        private static readonly int SunColorId = Shader.PropertyToID("_SunColor");
        private static readonly int MoonDirectionId = Shader.PropertyToID("_MoonDirection");
        private static readonly int MoonColorId = Shader.PropertyToID("_MoonColor");
        private static readonly int NightFactorId = Shader.PropertyToID("_NightFactor");
        private static readonly int StarIntensityId = Shader.PropertyToID("_StarIntensity");
        private static readonly int ZenithColorId = Shader.PropertyToID("_ZenithColor");
        private static readonly int HorizonColorId = Shader.PropertyToID("_HorizonColor");
        private static readonly int CloudColorId = Shader.PropertyToID("_CloudColor");
        private static readonly int CloudDarkId = Shader.PropertyToID("_CloudDark");

        private void Awake()
        {
            if (sun == null) sun = RenderSettings.sun;
            if (moon == null) CreateMoon();
            if (weather == null) weather = FindFirstObjectByType<WeatherSystem>();
            if (skyMaterial == null) skyMaterial = RenderSettings.skybox;
            if (skyMaterial != null)
            {
                _skyInstance = new Material(skyMaterial) { name = "FenglingSky_Runtime" };
                RenderSettings.skybox = _skyInstance;
            }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
        }

        private void Update()
        {
            if (!paused)
            {
                timeOfDay += UnityEngine.Time.unscaledDeltaTime * gameMinutesPerSecond / 60f * Mathf.Max(0f, speedMultiplier);
                if (timeOfDay >= 24f)
                {
                    timeOfDay -= 24f;
                    day++;
                    calendarDay++;
                    int daysInMonth = System.DateTime.DaysInMonth(calendarYear, calendarMonth);
                    if (calendarDay > daysInMonth)
                    {
                        calendarDay = 1;
                        calendarMonth++;
                        if (calendarMonth > 12)
                        {
                            calendarMonth = 1;
                            calendarYear++;
                        }
                    }
                    if (day > 30) { day = 1; season = (season + 1) % 4; }
                }
            }
            ApplyLighting();
        }

        public void SetTime(float hour)
        {
            timeOfDay = Mathf.Repeat(hour, 24f);
            ApplyLighting();
        }

        public void SetPaused(bool value) => paused = value;
        public void SetSpeed(float value) => speedMultiplier = Mathf.Max(0f, value);

        public void AdvanceHours(float hours)
        {
            timeOfDay = Mathf.Repeat(timeOfDay + hours, 24f);
            ApplyLighting();
        }

        private void ApplyLighting()
        {
            // 真实日期/时间 -> J2000 天数 -> 太阳/月亮黄经 -> 地平坐标。
            var utc = new System.DateTime(calendarYear, calendarMonth, calendarDay, 0, 0, 0, System.DateTimeKind.Utc)
                .AddHours(timeOfDay);
            double days = AstronomyMath.DaysSinceJ2000(utc);
            double sunLon = AstronomyMath.SunEclipticLongitude(days);
            double moonLon = AstronomyMath.MoonEclipticLongitude(days);
            double moonLat = AstronomyMath.MoonEclipticLatitude(days);
            double elongation = ((moonLon - sunLon) % 360.0 + 360.0) % 360.0;

            Vector3 sunDir = AstronomyMath.HorizonDirection(sunLon, 0.0, days, latitude, longitude);
            Vector3 moonDir = AstronomyMath.HorizonDirection(moonLon, moonLat, days, latitude, longitude);

            float dayFactor = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.08f, 0.18f, sunDir.y));
            float cloudCover = weather != null ? weather.cloudCover : 0f;
            float sunVisibility = weather != null ? weather.SunVisibility : 1f;
            float moonVisibility = weather != null ? weather.MoonVisibility : 1f;
            float shadowVisibility = weather != null ? weather.ShadowVisibility : 1f;
            float atmosphere = weather != null ? weather.Atmosphere : 0f;
            Color fogColor = Color.Lerp(new Color(0.025f, 0.035f, 0.06f),
                new Color(0.58f, 0.64f, 0.70f), dayFactor);
            fogColor = Color.Lerp(fogColor, new Color(0.42f, 0.47f, 0.52f), atmosphere * 0.55f);
            Shader.SetGlobalColor("_FenglingFogColor", fogColor);
            Shader.SetGlobalFloat("_FenglingFogDensity", weather != null ? weather.FogDensity : 0f);
            Shader.SetGlobalFloat("_FenglingWaterCloud", cloudCover);
            Shader.SetGlobalFloat("_FenglingWaterAtmosphere", atmosphere);
            Shader.SetGlobalFloat("_FenglingRain", weather != null ? weather.rain : 0f);
            float volumeDensity = Mathf.Max(0.01f,
                (weather != null ? weather.fog * 0.02f + weather.haze * 0.005f : 0f));
            Shader.SetGlobalFloat("_FenglingVolumetricDensity", volumeDensity);
            // 月亮：朔望月 -> 相位角 -> 照亮比例；月亮位置由太阳黄经 + 相位角推进。
            MoonPhaseAngle = (float)(elongation * Mathf.Deg2Rad);
            MoonIlluminatedFraction = (float)AstronomyMath.MoonIlluminatedFraction(MoonPhaseAngle);
            MoonAgeDays = (float)AstronomyMath.MoonAgeDays(sunLon, moonLon);
            float moonFactor = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.08f, 0.18f, moonDir.y));

            // 天空太阳保持真实高度；光照/阴影方向限制最低仰角，避免黎明/黄昏影子无限长。
            Vector3 lightDir = sunDir;
            float minLightY = Mathf.Sin(12f * Mathf.Deg2Rad);
            if (lightDir.y < minLightY)
            {
                lightDir.y = minLightY;
                lightDir.Normalize();
            }

            if (sun != null)
            {
                sun.transform.rotation = Quaternion.LookRotation(-lightDir, Vector3.up);
                sun.intensity = sunMaxIntensity * dayFactor * sunVisibility;
                sun.color = Color.Lerp(Color.Lerp(new Color(1f, 0.42f, 0.18f), new Color(1f, 0.95f, 0.86f),
                    Mathf.InverseLerp(0.02f, 0.45f, sunDir.y)), fogColor, atmosphere * 0.3f);
            }

            if (moon != null)
            {
                moon.transform.rotation = Quaternion.LookRotation(-moonDir, Vector3.up);
                moon.intensity = moonMaxIntensity * moonFactor * MoonIlluminatedFraction * moonVisibility;
                moon.color = Color.Lerp(new Color(0.52f, 0.66f, 1f), fogColor, atmosphere * 0.35f);

                // 月亮光照参数直接写全局，避免阴影类查找 Moon Light 失败时月亮完全不亮。
                Shader.SetGlobalVector("_FenglingMoonDir", new Vector4(moonDir.x, moonDir.y, moonDir.z, 0f));
                Shader.SetGlobalColor("_FenglingMoonColor", moon.color * (moon.intensity * 0.12f));
            }

            // 低太阳时淡化阴影，避免一条拖到屏幕外的长矛。
            Shader.SetGlobalFloat("_FenglingShadowStrength",
                Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.03f, 0.24f, lightDir.y)) * shadowVisibility);

            if (_skyInstance != null)
            {
                _skyInstance.SetVector(SunDirectionId, new Vector4(sunDir.x, sunDir.y, sunDir.z, 0f));
                _skyInstance.SetVector(MoonDirectionId, new Vector4(moonDir.x, moonDir.y, moonDir.z, 0f));
                _skyInstance.SetColor(SunColorId, sun != null ? sun.color : Color.white);
                _skyInstance.SetColor(MoonColorId, new Color(0.72f, 0.82f, 1f));
                _skyInstance.SetFloat("_MoonPhaseAngle", MoonPhaseAngle);
                _skyInstance.SetFloat("_MoonSize", 2000f);
                _skyInstance.SetFloat("_MoonDiskSize", 0.03f);
                _skyInstance.SetFloat("_MoonDiskBrightness", 0.4f);
                _skyInstance.SetFloat(NightFactorId, 1f - dayFactor);
                _skyInstance.SetFloat(StarIntensityId, Mathf.SmoothStep(0f, 1f, 1f - dayFactor));

                Color dayZenith = new Color(0.16f, 0.34f, 0.62f);
                Color nightZenith = new Color(0.005f, 0.012f, 0.035f);
                Color dayHorizon = new Color(0.78f, 0.85f, 0.90f);
                Color nightHorizon = new Color(0.035f, 0.055f, 0.12f);
                _skyInstance.SetColor(ZenithColorId, Color.Lerp(nightZenith, dayZenith, dayFactor));
                Color horizon = Color.Lerp(nightHorizon, dayHorizon, dayFactor);
                horizon = Color.Lerp(horizon, fogColor, atmosphere * 0.6f);
                _skyInstance.SetColor(HorizonColorId, horizon);
                _skyInstance.SetColor(CloudColorId, Color.Lerp(new Color(0.10f, 0.12f, 0.18f), Color.white, dayFactor));
                _skyInstance.SetColor(CloudDarkId, Color.Lerp(new Color(0.03f, 0.04f, 0.08f),
                    new Color(0.62f, 0.66f, 0.72f), dayFactor));
                _skyInstance.SetFloat("_CloudCoverage", Mathf.Lerp(0.12f, 0.95f, cloudCover));
                _skyInstance.SetFloat("_CloudOpacity", weather != null ? weather.CloudOpacity : 0f);
                _skyInstance.SetFloat("_CloudFarCoverage", Mathf.Lerp(0.62f, 0.34f, cloudCover));
                _skyInstance.SetFloat("_CloudFarOpacity", Mathf.Lerp(0.22f, 0.55f, cloudCover));
                _skyInstance.SetFloat("_CloudNearCoverage", Mathf.Lerp(0.70f, 0.42f, cloudCover));
                _skyInstance.SetFloat("_CloudNearOpacity", Mathf.Lerp(0.20f, 0.75f, cloudCover));
                var cloudWind = new Vector4(0.8f, 0.25f, 0f, 0f);
                _skyInstance.SetVector("_CloudWind", cloudWind);

                Shader.SetGlobalFloat("_FenglingCloudShadowStrength", cloudCover * 0.9f);
                Shader.SetGlobalFloat("_FenglingCloudShadowScale", 0.07f);
                Shader.SetGlobalVector("_FenglingCloudWind", cloudWind);
            }

            if (driveAmbient)
            {
                // 自建 SRP 下 Skybox 环境光不会自动刷新 SH，用 Flat Ambient 明确驱动。
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                Color dayAmb = new Color(0.62f, 0.68f, 0.75f);
                Color nightAmb = new Color(0.055f, 0.07f, 0.13f);
                float ambT = Mathf.Max(dayFactor, 0.22f);
                RenderSettings.ambientLight = Color.Lerp(nightAmb, dayAmb, ambT);
                RenderSettings.ambientIntensity = 1f;

                // 日出/日落前后用 twilight 抬环境光，避免低太阳时地面死黑。
                float twilight = 1f - Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(0.02f, 0.30f, Mathf.Abs(sunDir.y)));
                float ambientT = Mathf.Max(dayFactor, twilight * 0.8f);

                // FenglingLit 的 SH 兜底：白天保留 SH，夜晚/黎明用全局色保底。
                Shader.SetGlobalFloat("_FenglingAmbientScale", Mathf.Lerp(0.08f, 1f, ambientT));
                Shader.SetGlobalColor("_FenglingAmbientColor",
                    Color.Lerp(new Color(0.07f, 0.08f, 0.12f),
                        new Color(0.18f, 0.19f, 0.18f), ambientT));
            }
        }

        private void CreateMoon()
        {
            var go = new GameObject("Moon Light");
            go.transform.SetParent(transform, false);
            moon = go.AddComponent<Light>();
            moon.type = LightType.Directional;
            moon.color = new Color(0.52f, 0.66f, 1f);
            moon.intensity = 0f;
            moon.shadows = LightShadows.Soft;
        }

        private void OnGUI()
        {
            if (!showHud) return;
            GUI.Box(new Rect(12f, 12f, 360f, 82f), "Time of Day");
            GUI.Label(new Rect(24f, 36f, 330f, 22f), $"{calendarYear}-{calendarMonth:00}-{calendarDay:00}  {Mathf.FloorToInt(timeOfDay):00}:{Mathf.FloorToInt((timeOfDay % 1f) * 60f):00}  Moon {MoonIlluminatedFraction * 100f:0}%");

            float newTime = GUI.HorizontalSlider(new Rect(24f, 58f, 250f, 18f), timeOfDay, 0f, 24f);
            if (Mathf.Abs(newTime - timeOfDay) > 0.001f)
                SetTime(newTime);

            if (GUI.Button(new Rect(284f, 34f, 74f, 22f), paused ? "Resume" : "Pause"))
                paused = !paused;
            if (GUI.Button(new Rect(284f, 60f, 74f, 22f), $"{speedMultiplier:0.#}x"))
                speedMultiplier = speedMultiplier >= 8f ? 1f : speedMultiplier * 2f;
        }
    }
}
