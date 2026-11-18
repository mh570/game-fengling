using UnityEngine;
using Fengling.Water.Abstractions;

namespace Fengling.Water
{
    public enum WaterKind
    {
        /// <summary>纯平面。作为对照组：用来验证"差异是不是水造成的"。</summary>
        Flat = 0,

        /// <summary>Gerstner 波叠加。第一版实现。</summary>
        Gerstner = 1,

        /// <summary>频谱合成：Phillips 谱生成 N 个波再叠加（阶段 6）。</summary>
        Spectral = 2,
    }

    /// <summary>
    /// 画质档位。
    ///
    /// 存在的理由：这台机器（2 核 + 核显）全开只有个位数帧率，
    /// 而全关又看不出水的效果。三档让"能跑"和"好看"可以分开。
    /// </summary>
    public enum WaterQualityTier
    {
        Low = 0,
        Medium = 1,
        High = 2,
    }

    /// <summary>单个 Gerstner 波。</summary>
    [System.Serializable]
    public class WaveSettings
    {
        [Tooltip("传播方向（XZ 平面，会归一化）")]
        public Vector2 direction = Vector2.right;

        [Tooltip("振幅（米）")]
        [Range(0f, 3f)] public float amplitude = 0.5f;

        [Tooltip("波长（米）")]
        [Range(1f, 200f)] public float wavelength = 32f;

        [Tooltip("相位速度倍率，1 = 按重力波速")]
        [Range(0f, 3f)] public float speed = 1f;
    }

    /// <summary>
    /// 水模块的全部可调参数。
    ///
    /// 刻意做成普通 [Serializable] 类而不是 ScriptableObject：
    /// 测试阶段要在运行时随便改、随便复制，SO 反而碍事。
    /// 等参数稳定了再提取成资产。
    /// </summary>
    [System.Serializable]
    public class WaterSettings
    {
        [Header("基础")]
        public WaterKind kind = WaterKind.Spectral;   // 16 波频谱合成，浪形不规则

        [Tooltip("画质档位：Low 优先帧率，High 优先效果")]
        public WaterQualityTier tier = WaterQualityTier.High;

        [Tooltip("水域尺寸（米，正方形）")]
        public Vector2 size = new Vector2(200f, 200f);

        [Tooltip("水面静止高度（世界 Y）")]
        public float baseLevel = 0f;

        [Tooltip("时间缩放，0 = 定格，用于对照")]
        [Range(0f, 3f)] public float timeScale = 1f;

        [Tooltip("整体振幅倍率（运行时调参用）")]
        [Range(0f, 3f)] public float amplitudeScale = 1f;

        [Header("Gerstner 波（最多 8 个）")]
        public WaveSettings[] waves = DefaultWaves();

        [Header("频谱合成（Spectral）")]
        [Tooltip("生成多少个波。越多越像真海，但顶点着色器越贵")]
        [Range(4, 16)] public int spectralWaveCount = 12;

        [Tooltip("风速 km/h：决定峰值波长和能量集中度")]
        [Range(1f, 60f)] public float windSpeed = 12f;

        [Tooltip("风向（度，XZ 平面）")]
        [Range(0f, 360f)] public float windDirectionDegrees = 30f;

        [Tooltip("波向绕风向的散布范围（度）")]
        [Range(0f, 180f)] public float windDirectionSpread = 130f;   // 方向充分散开，避免规则条纹

        [Tooltip("最短波长（米）")]
        public float spectralMinWavelength = 4f;

        [Tooltip("最长波长（米）")]
        public float spectralMaxWavelength = 48f;

        [Tooltip("所有波振幅之和（米），控制整体浪高")]
        [Range(0.1f, 6f)] public float spectralTotalAmplitude = 1.6f;

        [Tooltip("随机种子：同一个种子生成同一片海")]
        public int spectralSeed = 20261118;

        /// <summary>
        /// 默认四波：波长几何级数（32/16/8/4），方向错开，速度略递进。
        /// 这套是手册里推荐的起点。
        /// </summary>
        public static WaveSettings[] DefaultWaves()
        {
            return new[]
            {
                new WaveSettings { direction = new Vector2( 1f,  0f), amplitude = 0.50f, wavelength = 32f, speed = 1.0f },
                new WaveSettings { direction = new Vector2( 0.7f, 0.7f), amplitude = 0.30f, wavelength = 16f, speed = 1.2f },
                new WaveSettings { direction = new Vector2(-0.5f, 0.85f), amplitude = 0.15f, wavelength = 8f,  speed = 1.5f },
                new WaveSettings { direction = new Vector2( 0.9f,-0.4f), amplitude = 0.08f, wavelength = 4f,  speed = 1.8f },
            };
        }

        public float HalfSize => size.x * 0.5f;

        public Bounds WorldBounds => new Bounds(
            new Vector3(0f, baseLevel, 0f),
            new Vector3(size.x, 100f, size.y));
    }
}
