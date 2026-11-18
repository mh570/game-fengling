using UnityEngine;
using Fengling.Water.Abstractions;

namespace Fengling.Water
{
    /// <summary>
    /// 频谱合成的水面：用 Phillips 谱一次性生成 N 个波，再按 Gerstner 公式叠加。
    ///
    /// 为什么这么做，而不是写一个 CPU 的 FFT：
    ///   1. 真正的 FFT 海洋需要 GPU 端每帧多次逆变换，CPU 版本在 2 核机器上没意义
    ///   2. "用频谱采样得到一组波，再直接求和"是对逆 FFT 的逐点求值，
    ///      数学上等价于 IFFT 在采样点上的结果，只是省掉了 FFT 的加速结构
    ///   3. 复用已经验证过的 Gerstner 采样代码，CPU/GPU 一致性不打折
    ///
    /// 真正的 GPU FFT（三波段、patch 平铺、无重复感）建议直接用
    /// Unity HDRP Water System，不要自己造。
    /// </summary>
    public sealed class SpectralWaterSimulation : IWaterSimulation
    {
        private struct Wave
        {
            public Vector2 Dir;
            public float Amplitude;
            public float Wavenumber;
            public float Omega;
            public float Phase;
        }

        private readonly Wave[] _waves;
        private readonly float _level;
        private readonly Bounds _bounds;

        public SpectralWaterSimulation(WaterSettings settings)
        {
            _level = settings.baseLevel;
            _bounds = settings.WorldBounds;

            int count = Mathf.Clamp(settings.spectralWaveCount, 4, 16);
            var rng = new System.Random(settings.spectralSeed);

            float windRad = settings.windDirectionDegrees * Mathf.Deg2Rad;
            var windDir = new Vector2(Mathf.Cos(windRad), Mathf.Sin(windRad));
            float windSpeed = Mathf.Max(0.1f, settings.windSpeed);
            float L = windSpeed * windSpeed / 9.81f;      // 峰值波长（深水色散）

            _waves = new Wave[count];
            float rawTotal = 0f;

            for (int i = 0; i < count; i++)
            {
                // 波长按幂律分布：长波少、短波多，和真实海浪的能量分布一致
                float u = (float)rng.NextDouble();
                float lambda = Mathf.Lerp(settings.spectralMaxWavelength, settings.spectralMinWavelength,
                                          Mathf.Pow(u, 1.8f));
                float k = 2f * Mathf.PI / Mathf.Max(0.01f, lambda);

                // Phillips 谱（省略方向项，方向单独处理）
                float kL = Mathf.Max(1e-4f, k * L);
                float spectrum = Mathf.Exp(-1f / (kL * kL)) / Mathf.Pow(k, 4f);
                float amp = Mathf.Sqrt(Mathf.Max(0f, spectrum));

                // 方向：绕风向随机散开
                float spread = settings.windDirectionSpread * Mathf.Deg2Rad *
                               ((float)rng.NextDouble() * 2f - 1f);
                float ang = windRad + spread;

                _waves[i] = new Wave
                {
                    Dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)),
                    Amplitude = amp,
                    Wavenumber = k,
                    Omega = Mathf.Sqrt(9.81f * k),
                    Phase = (float)rng.NextDouble() * Mathf.PI * 2f,
                };
                rawTotal += amp;
            }

            // 归一化：让总振幅等于美术给的目标值
            float scale = rawTotal > 1e-6f ? (settings.spectralTotalAmplitude * settings.amplitudeScale) / rawTotal : 0f;
            for (int i = 0; i < _waves.Length; i++)
                _waves[i].Amplitude *= scale;
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
                float theta = w.Wavenumber * Vector2.Dot(w.Dir, worldXZ) - w.Omega * time + w.Phase;
                float s = Mathf.Sin(theta);
                float c = Mathf.Cos(theta);

                height += w.Amplitude * s;
                float ak = w.Amplitude * w.Wavenumber;
                dhdx += ak * w.Dir.x * c;
                dhdz += ak * w.Dir.y * c;
            }

            var normal = new Vector3(-dhdx, 1f, -dhdz).normalized;
            return new WaterSample(height, normal, true);
        }

        public int FillWaveData(Vector4[] dirs, Vector4[] parameters, float amplitudeScale)
        {
            int n = Mathf.Min(_waves.Length, dirs.Length);
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
