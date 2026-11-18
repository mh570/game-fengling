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

        [Header("泡沫")]
        public Color foamColor = Color.white;
        [Range(0.05f, 3f)] public float foamDistance = 0.6f;
        [Range(0f, 2f)] public float foamStrength = 1f;

        private Mesh _mesh;
        private Material _material;
        private static readonly Vector4[] WaveDirs = new Vector4[8];
        private static readonly Vector4[] WaveParams = new Vector4[8];

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
            PushDynamicProperties();
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
            _material.SetFloat("_WaterLevel", s.baseLevel);
        }

        /// <summary>
        /// 每帧把波参数和太阳方向推给 shader。
        /// ω 的算法必须和 GerstnerWaterSimulation 完全一致：ω = sqrt(g·k)·speed
        /// </summary>
        private void PushDynamicProperties()
        {
            var s = water.Settings;

            int count = Mathf.Min(s.waves?.Length ?? 0, 8);
            for (int i = 0; i < count; i++)
            {
                var w = s.waves[i];
                float k = 2f * Mathf.PI / Mathf.Max(0.01f, w.wavelength);
                float omega = Mathf.Sqrt(9.81f * k) * Mathf.Max(0f, w.speed);
                var dir = w.direction.sqrMagnitude < 1e-4f ? Vector2.right : w.direction.normalized;

                WaveDirs[i] = new Vector4(dir.x, dir.y, 0f, 0f);
                WaveParams[i] = new Vector4(w.amplitude, k, omega, i * 1.37f);  // 相位也必须和 CPU 一致
            }

            _material.SetInt("_WaveCount", count);
            _material.SetVectorArray("_WaveDirs", WaveDirs);
            _material.SetVectorArray("_WaveParams", WaveParams);
            _material.SetFloat("_AmplitudeScale", s.amplitudeScale);
            _material.SetFloat("_WaterTime", water.WaterTime);
            _material.SetVector("_SunDirection", GetSunDirection());
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
