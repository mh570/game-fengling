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
        public WaterKind kind = WaterKind.Gerstner;

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
