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

        [Header("天空")]
        public Material skyMaterial;
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
            // 太阳赤纬：按季节/日期近似
            float dayOfYear = season * 90f + day;
            float declination = 23.44f * Mathf.Sin((dayOfYear - 81f) * Mathf.Deg2Rad * 360f / 365f);
            float hourAngle = (timeOfDay - 12f) * 15f;

            float lat = latitude * Mathf.Deg2Rad;
            float dec = declination * Mathf.Deg2Rad;
            float ha = hourAngle * Mathf.Deg2Rad;

            float sinAlt = Mathf.Sin(lat) * Mathf.Sin(dec) +
                           Mathf.Cos(lat) * Mathf.Cos(dec) * Mathf.Cos(ha);
            sinAlt = Mathf.Clamp(sinAlt, -1f, 1f);
            float altitude = Mathf.Asin(sinAlt);

            float cosAz = (Mathf.Sin(dec) - Mathf.Sin(altitude) * Mathf.Sin(lat)) /
                          (Mathf.Cos(altitude) * Mathf.Cos(lat) + 1e-5f);
            float azimuth = Mathf.Acos(Mathf.Clamp(cosAz, -1f, 1f));
            if (Mathf.Sin(ha) > 0f) azimuth = Mathf.PI * 2f - azimuth;

            Vector3 sunDir = new Vector3(
                Mathf.Cos(altitude) * Mathf.Sin(azimuth),
                Mathf.Sin(altitude),
                Mathf.Cos(altitude) * Mathf.Cos(azimuth)).normalized;

            float dayFactor = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.08f, 0.18f, sunDir.y));
            // 月亮：朔望月 -> 相位角 -> 照亮比例；月亮位置由太阳黄经 + 相位角推进。
            const float SynodicMonth = 29.53059f;
            float daySerial = year * 365f + season * 90f + day;
            float daysSinceNewMoon = daySerial + timeOfDay / 24f - 0.26f + moonPhaseOffsetDays;
            MoonAgeDays = Mathf.Repeat(daysSinceNewMoon, SynodicMonth);
            float phaseAngle = MoonAgeDays / SynodicMonth * Mathf.PI * 2f;
            MoonIlluminatedFraction = (1f - Mathf.Cos(phaseAngle)) * 0.5f;

            float moonDeclination = declination + 5.14f * Mathf.Sin(phaseAngle);
            float moonHourAngle = hourAngle + phaseAngle * Mathf.Rad2Deg;
            float moonDecRad = moonDeclination * Mathf.Deg2Rad;
            float moonHaRad = moonHourAngle * Mathf.Deg2Rad;
            float sinMoonAlt = Mathf.Sin(lat) * Mathf.Sin(moonDecRad) +
                               Mathf.Cos(lat) * Mathf.Cos(moonDecRad) * Mathf.Cos(moonHaRad);
            sinMoonAlt = Mathf.Clamp(sinMoonAlt, -1f, 1f);
            float moonAltitude = Mathf.Asin(sinMoonAlt);
            float cosMoonAz = (Mathf.Sin(moonDecRad) - Mathf.Sin(moonAltitude) * Mathf.Sin(lat)) /
                              (Mathf.Cos(moonAltitude) * Mathf.Cos(lat) + 1e-5f);
            float moonAzimuth = Mathf.Acos(Mathf.Clamp(cosMoonAz, -1f, 1f));
            if (Mathf.Sin(moonHaRad) > 0f) moonAzimuth = Mathf.PI * 2f - moonAzimuth;
            Vector3 moonDir = new Vector3(
                Mathf.Cos(moonAltitude) * Mathf.Sin(moonAzimuth),
                Mathf.Sin(moonAltitude),
                Mathf.Cos(moonAltitude) * Mathf.Cos(moonAzimuth)).normalized;

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
                sun.intensity = sunMaxIntensity * dayFactor;
                sun.color = Color.Lerp(new Color(1f, 0.42f, 0.18f), new Color(1f, 0.95f, 0.86f),
                    Mathf.InverseLerp(0.02f, 0.45f, sunDir.y));
            }

            if (moon != null)
            {
                moon.transform.rotation = Quaternion.LookRotation(-moonDir, Vector3.up);
                moon.intensity = moonMaxIntensity * moonFactor * MoonIlluminatedFraction;
                moon.color = new Color(0.52f, 0.66f, 1f);

                // 月亮光照参数直接写全局，避免阴影类查找 Moon Light 失败时月亮完全不亮。
                Shader.SetGlobalVector("_FenglingMoonDir", new Vector4(moonDir.x, moonDir.y, moonDir.z, 0f));
                Shader.SetGlobalColor("_FenglingMoonColor", moon.color * (moon.intensity * 0.12f));
            }

            // 低太阳时淡化阴影，避免一条拖到屏幕外的长矛。
            Shader.SetGlobalFloat("_FenglingShadowStrength",
                Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.03f, 0.24f, lightDir.y)));

            if (_skyInstance != null)
            {
                _skyInstance.SetVector(SunDirectionId, new Vector4(sunDir.x, sunDir.y, sunDir.z, 0f));
                _skyInstance.SetVector(MoonDirectionId, new Vector4(moonDir.x, moonDir.y, moonDir.z, 0f));
                _skyInstance.SetColor(SunColorId, sun != null ? sun.color : Color.white);
                _skyInstance.SetColor(MoonColorId, new Color(0.72f, 0.82f, 1f));
                _skyInstance.SetFloat(NightFactorId, 1f - dayFactor);
                _skyInstance.SetFloat(StarIntensityId, Mathf.SmoothStep(0f, 1f, 1f - dayFactor));

                Color dayZenith = new Color(0.16f, 0.34f, 0.62f);
                Color nightZenith = new Color(0.005f, 0.012f, 0.035f);
                Color dayHorizon = new Color(0.78f, 0.85f, 0.90f);
                Color nightHorizon = new Color(0.035f, 0.055f, 0.12f);
                _skyInstance.SetColor(ZenithColorId, Color.Lerp(nightZenith, dayZenith, dayFactor));
                _skyInstance.SetColor(HorizonColorId, Color.Lerp(nightHorizon, dayHorizon, dayFactor));
                _skyInstance.SetColor(CloudColorId, Color.Lerp(new Color(0.10f, 0.12f, 0.18f), Color.white, dayFactor));
                _skyInstance.SetColor(CloudDarkId, Color.Lerp(new Color(0.03f, 0.04f, 0.08f),
                    new Color(0.62f, 0.66f, 0.72f), dayFactor));
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
            GUI.Label(new Rect(24f, 36f, 300f, 22f), $"{Mathf.FloorToInt(timeOfDay):00}:{Mathf.FloorToInt((timeOfDay % 1f) * 60f):00}  Day {day}  S{season}  Moon {MoonIlluminatedFraction * 100f:0}%");

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
