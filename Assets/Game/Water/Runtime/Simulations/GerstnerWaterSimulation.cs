using UnityEngine;
using Fengling.Water.Abstractions;

namespace Fengling.Water
{
    /// <summary>
    /// Gerstner 波叠加（高度 + 解析法线，纯 CPU）。
    ///
    /// 为什么用解析法线而不是有限差分：
    ///   有限差分每个采样点要额外算 2 次高度，代价 ×3，而且精度受步长影响。
    ///   Gerstner 的导数可以直接解析写出来，几乎免费。
    ///
    /// 注意：这里只算高度和法线，不做水平位移（XZ）。
    ///   水平位移会让"采样点"和"顶点"不再一一对应，CPU 采样会变复杂。
    ///   第一版先不做，等视觉 shader 需要再补——届时 CPU 采样要改成迭代求逆。
    /// </summary>
    public sealed class GerstnerWaterSimulation : IWaterSimulation
    {
        private struct Wave
        {
            public Vector2 Dir;      // 已归一化
            public float Amplitude;
            public float Wavenumber; // k = 2π/λ
            public float Phase;      // 初始相位
            public float Omega;      // 角频率
        }

        private readonly Wave[] _waves;
        private readonly float _level;
        private readonly Bounds _bounds;

        public GerstnerWaterSimulation(WaterSettings settings)
        {
            _level = settings.baseLevel;
            _bounds = settings.WorldBounds;

            int n = Mathf.Min(settings.waves?.Length ?? 0, 8);
            _waves = new Wave[n];
            for (int i = 0; i < n; i++)
            {
                var w = settings.waves[i];
                float k = 2f * Mathf.PI / Mathf.Max(0.01f, w.wavelength);
                // 深水色散关系：ω = sqrt(g·k)，乘 speed 作为美术可调的倍率
                float omega = Mathf.Sqrt(9.81f * k) * Mathf.Max(0f, w.speed);
                _waves[i] = new Wave
                {
                    Dir = w.direction.sqrMagnitude < 1e-4f ? Vector2.right : w.direction.normalized,
                    Amplitude = w.amplitude * settings.amplitudeScale,
                    Wavenumber = k,
                    Phase = i * 1.37f,     // 固定错相，避免所有波同时到峰
                    Omega = omega,
                };
            }
        }

        public WaterSample Sample(Vector2 worldXZ, float time)
        {
            if (!_bounds.Contains(new Vector3(worldXZ.x, _level, worldXZ.y)))
                return new WaterSample(_level, Vector3.up, false);

            float height = _level;
            float dhdx = 0f;
            float dhdz = 0f;

            for (int i = 0; i < _waves.Length; i++)
            {
                ref readonly Wave w = ref _waves[i];
                float phase = w.Wavenumber * Vector2.Dot(w.Dir, worldXZ) - w.Omega * time + w.Phase;
                float s = Mathf.Sin(phase);
                float c = Mathf.Cos(phase);

                height += w.Amplitude * s;
                // 解析导数：d/dx [A·sin(k·(d·p) - ωt)] = A·k·d.x·cos(phase)
                float ak = w.Amplitude * w.Wavenumber;
                dhdx += ak * w.Dir.x * c;
                dhdz += ak * w.Dir.y * c;
            }

            // 高度场 y = h(x,z) 的法线 = normalize(-∂h/∂x, 1, -∂h/∂z)
            var normal = new Vector3(-dhdx, 1f, -dhdz).normalized;
            return new WaterSample(height, normal, true);
        }

        /// <summary>
        /// 导出手上正在用的波（振幅已乘过 amplitudeScale，是最终值）。
        /// shader 侧不要再乘一次，否则画面和浮力会对不上。
        /// </summary>
        public int FillWaveData(Vector4[] dirs, Vector4[] parameters, float amplitudeScale)
        {
            int n = Mathf.Min(_waves.Length, Mathf.Min(dirs.Length, parameters.Length));
            for (int i = 0; i < n; i++)
            {
                ref readonly Wave w = ref _waves[i];
                dirs[i] = new Vector4(w.Dir.x, w.Dir.y, 0f, 0f);
                parameters[i] = new Vector4(w.Amplitude, w.Wavenumber, w.Omega, w.Phase);
            }
            return n;
        }
    }
}
