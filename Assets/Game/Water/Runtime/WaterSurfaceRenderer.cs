using UnityEngine;

namespace Fengling.Water
{
    /// <summary>
    /// 水面渲染器：生成网格 + 创建材质 + 每帧把参数喂给 shader。
    ///
    /// 关键职责是**让 GPU 和 CPU 用同一组波参数**。
    /// CPU 的 GerstnerWaterSimulation 负责浮力/采样，GPU 的 vertex shader 负责画面；
    /// 两边算的 ω、相位、振幅任何一项不一致，玩家就会浮在水面之上或之下。
    ///
    /// 所以这里不自己保存波参数，一律从 WaterModule 的 WaterSettings 读。
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [AddComponentMenu("Fengling/Water/Water Surface Renderer")]
    public sealed class WaterSurfaceRenderer : MonoBehaviour
    {
        [Header("数据源")]
        public WaterModule water;

        [Header("网格")]
        [Tooltip("每边分段数。128 段 = 16,641 顶点，对 200m 水面每格 1.5m")]
        [Range(16, 256)] public int segments = 128;

        [Header("地形高度图（用于水深、泡沫）")]
        public Texture2D terrainHeightMap;
        [Tooltip("(中心X, 中心Z, 尺寸X, 尺寸Z)")]
        public Vector4 heightMapBounds = new Vector4(0f, 0f, 200f, 200f);
        [Tooltip("高度图对应的真实高度范围 (min, max)")]
        public Vector2 heightRange = new Vector2(-9f, 4f);

        [Header("外观")]
        public Texture2D normalMap1;
        public Texture2D normalMap2;
        public Color shallowColor = new Color(0.05f, 0.42f, 0.45f);
        public Color deepColor = new Color(0.01f, 0.06f, 0.12f);
        [Range(0.5f, 30f)] public float absorptionDistance = 5f;
        [Range(0.01f, 0.5f)] public float roughness = 0.06f;
        [Range(0f, 0.2f)] public float f0 = 0.02f;
        [Range(0f, 2f)] public float normalStrength = 1f;
        [Range(0f, 3f)] public float sunIntensity = 2f;

        [Header("开关（阶段 7 品质档会用）")]
        public bool useFoam = true;
        public bool useReflectionCube = true;

        [Header("泡沫")]
        public Color foamColor = Color.white;
        [Range(0.05f, 3f)] public float foamDistance = 0.6f;
        [Range(0f, 2f)] public float foamStrength = 1f;

        private Mesh _mesh;
        private Material _material;
        private Camera _camera;
        private static readonly Vector4[] WaveDirs = new Vector4[16];
        private static readonly Vector4[] WaveParams = new Vector4[16];

        private void Awake()
        {
            if (water == null) water = FindFirstObjectByType<WaterModule>();

            _mesh = WaterMeshBuilder.Build(
                water != null ? water.Settings.size : new Vector2(200f, 200f),
                segments, "WaterSurface");
            GetComponent<MeshFilter>().sharedMesh = _mesh;

            var shader = Shader.Find("Fengling/Water Surface");
            if (shader == null)
            {
                Debug.LogError("[Water] 找不到 shader 'Fengling/Water Surface'，水面不会渲染");
                return;
            }

            _material = new Material(shader) { name = "WaterSurface (instance)" };
            var mr = GetComponent<MeshRenderer>();
            mr.sharedMaterial = _material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            PushStaticProperties();
        }

        private void Update()
        {
            if (_material == null || water == null) return;
            UpdateTier();
            PushDynamicProperties();
        }

        private WaterQualityTier _appliedTier = (WaterQualityTier)(-1);

        /// <summary>
        /// 按档位调参数。只在档位变化时执行一次，不对每帧造成负担。
        /// 数值依据《水面复刻手册》第 8 章的性能预算。
        /// </summary>
        private void UpdateTier()
        {
            var tier = water.Settings.tier;
            if (tier == _appliedTier) return;
            _appliedTier = tier;

            switch (tier)
            {
                case WaterQualityTier.Low:
                    useFoam = false;
                    useReflectionCube = false;
                    normalStrength = 0.7f;
                    absorptionDistance = 4f;
                    roughness = 0.10f;
                    foamDistance = 0.35f;
                    break;

                case WaterQualityTier.Medium:
                    useFoam = true;
                    useReflectionCube = true;
                    normalStrength = 1.0f;
                    absorptionDistance = 5f;
                    roughness = 0.07f;
                    foamDistance = 0.6f;
                    break;

                default: // High
                    useFoam = true;
                    useReflectionCube = true;
                    normalStrength = 1.2f;
                    absorptionDistance = 5f;
                    roughness = 0.06f;
                    foamDistance = 0.9f;
                    break;
            }

            PushStaticProperties();
        }

        private void PushStaticProperties()
        {
            if (_material == null || water == null) return;

            var s = water.Settings;
            _material.SetVector("_HeightMapBounds", heightMapBounds);
            _material.SetVector("_HeightRange", new Vector4(heightRange.x, heightRange.y, 0f, 0f));
            if (terrainHeightMap != null) _material.SetTexture("_HeightMap", terrainHeightMap);
            if (normalMap1 != null) _material.SetTexture("_NormalMap1", normalMap1);
            if (normalMap2 != null) _material.SetTexture("_NormalMap2", normalMap2);

            _material.SetColor("_ShallowColor", shallowColor);
            _material.SetColor("_DeepColor", deepColor);
            _material.SetFloat("_AbsorptionDistance", absorptionDistance);
            _material.SetFloat("_Roughness", roughness);
            _material.SetFloat("_F0", f0);
            _material.SetFloat("_NormalStrength", normalStrength);
            _material.SetFloat("_SunIntensity", sunIntensity);
            _material.SetColor("_FoamColor", foamColor);
            _material.SetFloat("_FoamDistance", foamDistance);
            _material.SetFloat("_FoamStrength", foamStrength);
            _material.SetFloat("_UseFoam", useFoam ? 1f : 0f);
            _material.SetFloat("_UseReflectionCube", useReflectionCube ? 1f : 0f);
            _material.SetFloat("_WaterLevel", s.baseLevel);
        }

        /// <summary>
        /// 每帧把波参数和太阳方向推给 shader。
        /// 波形数据一律从 WaterModule.FillWaveData 拿 —— 渲染层不自己读 settings，
        /// 否则 Gerstner 和 Spectral 两种实现会出现两份数据源。
        /// </summary>
        private void PushDynamicProperties()
        {
            int count = water.FillWaveData(WaveDirs, WaveParams);

            float maxHeight = 0f;
            for (int i = 0; i < count; i++)
                maxHeight += Mathf.Abs(WaveParams[i].x);

            _material.SetInt("_WaveCount", count);
            _material.SetVectorArray("_WaveDirs", WaveDirs);
            _material.SetVectorArray("_WaveParams", WaveParams);
            // 振幅已在模拟器里乘过 amplitudeScale，这里必须固定 1，否则会重复放大
            _material.SetFloat("_AmplitudeScale", 1f);
            _material.SetFloat("_WaterTime", water.WaterTime);
            _material.SetFloat("_MaxWaveHeight", Mathf.Max(0.05f, maxHeight * 0.6f));
            _material.SetVector("_SunDirection", GetSunDirection());

            UpdateUnderwater();
        }

        /// <summary>
        /// 相机在水下时给 shader 一个标记，水面整体偏青（模拟水体吸收）。
        /// 顺便把相机背景色也调成深水色，避免看到"天空中"的水下视角。
        /// </summary>
        private void UpdateUnderwater()
        {
            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return;

            var camPos = _camera.transform.position;
            var sample = water.Sample(new Vector2(camPos.x, camPos.z));
            bool underwater = sample.Inside && camPos.y < sample.Height;

            _material.SetFloat("_CameraUnderwater", underwater ? 1f : 0f);
        }

        private static Vector4 GetSunDirection()
        {
            var sun = RenderSettings.sun;
            if (sun != null)
            {
                var forward = sun.transform.forward;
                var dir = -(Vector3)forward;
                return new Vector4(dir.x, dir.y, dir.z, 0f);
            }
            return new Vector4(0.3f, 0.8f, 0.5f, 0f);
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            if (_material != null) Destroy(_material);
        }
    }
}
