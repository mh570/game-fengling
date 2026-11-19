using UnityEngine;
using Fengling.Rendering;

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

        [Tooltip("运行时重建水面网格。瀑布 sheet 用 Quad 时必须关掉，否则会被替换成水平网格")]
        public bool buildMesh = true;

        [Header("地形高度图（用于水深、泡沫）")]
        public Texture2D terrainHeightMap;
        [Tooltip("(中心X, 中心Z, 尺寸X, 尺寸Z)")]
        public Vector4 heightMapBounds = new Vector4(0f, 0f, 200f, 200f);
        [Tooltip("高度图对应的真实高度范围 (min, max)")]
        public Vector2 heightRange = new Vector2(-9f, 4f);

        [Header("材质")]
        [Tooltip("引用 Fengling/Water Surface 的材质资产。为空时回退到 Shader.Find（仅编辑器可靠）")]
        public Material waterMaterialTemplate;

        [Header("外观")]
        public Texture2D normalMap1;
        public Texture2D normalMap2;
        public Color shallowColor = new Color(0.42f, 0.76f, 0.80f);
        public Color deepColor = new Color(0.04f, 0.22f, 0.36f);
        [Range(0.5f, 30f)] public float absorptionDistance = 6f;

        [Tooltip("水深超过这个值就完全不透明")]
        [Range(0.5f, 40f)] public float opaqueAtDepth = 9f;
        [Range(0.01f, 0.5f)] public float roughness = 0.06f;
        [Range(0f, 0.2f)] public float f0 = 0.02f;
        [Range(0f, 2f)] public float normalStrength = 1f;
        [Range(0f, 0.1f)] public float refractionStrength = 0.015f;
        [Header("Normal Tiling")]
        [Tooltip("每 10m 的法线重复次数；正交俯视下太高会产生横向条纹")]
        [Range(0.5f, 24f)] public float normalTiling1 = 8f;
        [Range(0.5f, 48f)] public float normalTiling2 = 24f;

        [Header("远处 / 海平线")]
        public Color horizonColor = new Color(0.65f, 0.78f, 0.90f);
        [Range(10f, 500f)] public float horizonFadeStart = 30f;
        [Range(50f, 1000f)] public float horizonFadeRange = 120f;
        public Color shoreColor = new Color(0.66f, 0.60f, 0.46f);

        [Tooltip("预留：空间浪高扰动。当前未使用，避免 CPU 浮力采样与 GPU 波形不一致")]
        [Range(0f, 1f)] public float waveIrregularity = 0.35f;
        [Range(0f, 3f)] public float sunIntensity = 2f;

        [Header("风格：0 = 写实，1 = 风格化（奶蓝奶白）")]
        [Range(0f, 1f)] public float stylized = 0f;   // 默认写实，按 V 切换
        public Color stylizedShallow = new Color(0.72f, 0.90f, 0.92f);
        public Color stylizedDeep = new Color(0.10f, 0.38f, 0.52f);
        [Range(0f, 1f)] public float stylizedOpacity = 0.65f;

        [Header("开关（阶段 7 品质档会用）")]
        [Tooltip("关闭后不再按 Quality Tier 覆盖 manual 参数")]
        public bool autoTier = true;
        public bool useFoam = true;
        public bool useReflectionCube = true;

        [Header("流向 / 垂直流")]
        [Tooltip("水面流动方向（XZ），小河用")]
        public Vector2 flowDirection = new Vector2(1f, 0f);
        [Tooltip("流动速度，0 = 静止")]
        [Range(0f, 3f)] public float flowSpeed = 0f;
        [Tooltip("1 = 瀑布模式（垂直流，跳过波动）")]
        [Range(0f, 1f)] public float verticalFlow = 0f;

        [Header("泡沫")]
        public Color foamColor = Color.white;
        [Range(0.05f, 3f)] public float foamDistance = 1.0f;
        [Range(0f, 2f)] public float foamStrength = 0.85f;

        [Tooltip("泡沫边缘的噪声扭曲量（米），越大越碎")]
        [Range(0f, 10f)] public float foamWarp = 2.5f;

        private Mesh _mesh;
        private Material _material;
        private Camera _camera;
        private static readonly Vector4[] WaveDirs = new Vector4[16];
        private static readonly Vector4[] WaveParams = new Vector4[16];

        private void OnEnable()
        {
            FenglingSceneTextures.Ready += BindSceneTextures;
        }

        private void OnDisable()
        {
            FenglingSceneTextures.Ready -= BindSceneTextures;
        }

        private void Awake()
        {
            if (water == null) water = FindFirstObjectByType<WaterModule>();

            if (buildMesh)
            {
                _mesh = WaterMeshBuilder.Build(
                    water != null ? water.Settings.size : new Vector2(200f, 200f),
                    segments, "WaterSurface");
                GetComponent<MeshFilter>().sharedMesh = _mesh;
            }

            var shader = waterMaterialTemplate != null && waterMaterialTemplate.shader != null
                ? waterMaterialTemplate.shader
                : Shader.Find("Fengling/Water Surface");
            if (shader == null)
            {
                Debug.LogError("[Water] 找不到 shader 'Fengling/Water Surface'，水面不会渲染");
                return;
            }

            _material = waterMaterialTemplate != null
                ? new Material(waterMaterialTemplate) { name = "WaterSurface (instance)" }
                : new Material(shader) { name = "WaterSurface (instance)" };
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
            if (!autoTier) return;
            var tier = water.Settings.tier;
            if (tier == _appliedTier) return;
            _appliedTier = tier;

            switch (tier)
            {
                case WaterQualityTier.Low:
                    useFoam = false;
                    useReflectionCube = false;
                    normalStrength = 0.7f;
                    absorptionDistance = 6f;
                    roughness = 0.10f;
                    foamDistance = 0.5f;
                    break;

                case WaterQualityTier.Medium:
                    useFoam = true;
                    useReflectionCube = true;
                    normalStrength = 1.0f;
                    absorptionDistance = 6f;
                    roughness = 0.07f;
                    foamDistance = 1.0f;
                    break;

                default: // High
                    useFoam = true;
                    useReflectionCube = true;
                    normalStrength = 1.2f;
                    absorptionDistance = 6f;
                    roughness = 0.06f;
                    foamDistance = 1.6f;
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
            _material.SetFloat("_RefractionStrength", refractionStrength);
            _material.SetFloat("_NormalTiling1", normalTiling1);
            _material.SetFloat("_NormalTiling2", normalTiling2);
            _material.SetFloat("_SunIntensity", sunIntensity);
            _material.SetColor("_FoamColor", foamColor);
            _material.SetFloat("_FadeDistance", opaqueAtDepth);
            _material.SetFloat("_FoamWarp", foamWarp);
            _material.SetFloat("_FoamDistance", foamDistance);
            _material.SetFloat("_FoamStrength", foamStrength);
            _material.SetFloat("_Stylized", 0f);   // 只做写实
            _material.SetColor("_HorizonColor", horizonColor);
            _material.SetFloat("_HorizonFadeStart", horizonFadeStart);
            _material.SetFloat("_HorizonFadeRange", horizonFadeRange);
            _material.SetColor("_ShoreColor", shoreColor);

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

            _material.SetFloat("_AmplitudeScale", 1f);
            _material.SetFloat("_NormalStrength", normalStrength);
            _material.SetFloat("_RefractionStrength", refractionStrength);
            _material.SetFloat("_NormalTiling1", normalTiling1);
            _material.SetFloat("_NormalTiling2", normalTiling2);
            _material.SetColor("_ShallowColor", shallowColor);
            _material.SetColor("_DeepColor", deepColor);
            _material.SetFloat("_FadeDistance", opaqueAtDepth);
            _material.SetFloat("_FoamDistance", foamDistance);
            _material.SetFloat("_FoamStrength", foamStrength);
            _material.SetFloat("_UseFoam", useFoam ? 1f : 0f);

            // 管线拷贝的深度/场景颜色：显式绑到材质，避免全局 sampler 在某些平台失效。
            var sceneDepth = Shader.GetGlobalTexture("_FenglingSceneDepth");
            if (sceneDepth != null)
            {
                _material.SetTexture("_FenglingSceneDepth", sceneDepth);
                _material.SetFloat("_FenglingSceneDepthAvailable",
                    Shader.GetGlobalFloat("_FenglingSceneDepthAvailable"));
            }
            else
            {
                _material.SetFloat("_FenglingSceneDepthAvailable", 0f);
            }

            var sceneColor = Shader.GetGlobalTexture("_FenglingSceneColor");
            if (sceneColor != null)
            {
                _material.SetTexture("_FenglingSceneColor", sceneColor);
                _material.SetFloat("_FenglingSceneColorAvailable",
                    Shader.GetGlobalFloat("_FenglingSceneColorAvailable"));
            }
            else
            {
                _material.SetFloat("_FenglingSceneColorAvailable", 0f);
            }

            _material.SetVector("_FlowDir", new Vector4(flowDirection.x, flowDirection.y, 0f, 0f));
            _material.SetFloat("_FlowSpeed", flowSpeed);
            _material.SetFloat("_Vertical", verticalFlow);

            _material.SetFloat("_WaterTime", water.WaterTime);
            _material.SetVector("_SunDirection", GetSunDirection());

            // 波数组用全局数组传递：运行时 new Material 的数组属性默认只有 1 个元素，
            // SetVectorArray(16) 会被 Unity cap 到 1，导致 GPU 只拿到第 1 个波。
            Shader.SetGlobalInt("_FenglingWaveCount", count);
            Shader.SetGlobalVectorArray("_FenglingWaveDirs", WaveDirs);
            Shader.SetGlobalVectorArray("_FenglingWaveParams", WaveParams);

            UpdateUnderwater();
        }

        /// <summary>
        /// 管线在透明 pass 之前调用：把当前帧的深度/场景颜色显式绑到材质。
        /// 不能只在 Update 里读全局，否则读到的是上一帧已释放的 RT。
        /// </summary>
        public void BindSceneTextures(Texture depth, Texture color)
        {
            if (_material == null) return;
            if (depth != null)
            {
                _material.SetTexture("_FenglingSceneDepth", depth);
                _material.SetFloat("_FenglingSceneDepthAvailable", 1f);
            }
            if (color != null)
            {
                _material.SetTexture("_FenglingSceneColor", color);
                _material.SetFloat("_FenglingSceneColorAvailable", 1f);
            }
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
