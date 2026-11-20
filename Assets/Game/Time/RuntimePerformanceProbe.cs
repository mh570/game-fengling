using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Fengling.Time
{
    /// <summary>
    /// 仅 -benchmark 启动时运行：四组天气/时段，采集 FrameTiming，写 CSV 后退出。
    /// </summary>
    public sealed class RuntimePerformanceProbe : MonoBehaviour
    {
        private struct Scenario
        {
            public string Name;
            public float Hour;
            public float Cloud;
            public float Haze;
            public float Fog;
            public float Rain;
        }

        private static readonly Scenario[] Scenarios =
        {
            new Scenario { Name = "day_clear", Hour = 12f, Cloud = 0.05f, Haze = 0.05f, Fog = 0.05f, Rain = 0f },
            new Scenario { Name = "night_clear", Hour = 22f, Cloud = 0.05f, Haze = 0.05f, Fog = 0.05f, Rain = 0f },
            new Scenario { Name = "overcast", Hour = 12f, Cloud = 0.90f, Haze = 0.35f, Fog = 0.50f, Rain = 0f },
            new Scenario { Name = "rain", Hour = 12f, Cloud = 0.80f, Haze = 0.35f, Fog = 0.40f, Rain = 0.90f },
        };

        private const int WarmupFrames = 60;
        private const int MeasureFrames = 180;

        private TimeOfDaySystem _tod;
        private WeatherSystem _weather;
        private int _scenario;
        private int _frame;
        private readonly List<float> _cpu = new List<float>();
        private readonly List<float> _gpu = new List<float>();
        private readonly FrameTiming[] _timings = new FrameTiming[1];
        private readonly List<string> _lines = new List<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var args = Environment.GetCommandLineArgs();
            bool benchmark = false;
            foreach (var a in args)
                if (a == "-benchmark") benchmark = true;
            if (!benchmark) return;
            var go = new GameObject("RuntimePerformanceProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<RuntimePerformanceProbe>();
        }

        private void Start()
        {
            Screen.SetResolution(1280, 720, false);
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            _tod = FindFirstObjectByType<TimeOfDaySystem>();
            _weather = FindFirstObjectByType<WeatherSystem>();
            ApplyScenario(0);
        }

        private void ApplyScenario(int index)
        {
            if (index >= Scenarios.Length)
            {
                Finish();
                return;
            }
            var s = Scenarios[index];
            if (_weather != null)
            {
                _weather.cloudCover = s.Cloud;
                _weather.haze = s.Haze;
                _weather.fog = s.Fog;
                _weather.rain = s.Rain;
                _weather.snow = 0f;
            }
            if (_tod != null)
            {
                _tod.paused = true;
                _tod.SetTime(s.Hour);
            }
            _sceneFrame = 0;
            _cpu.Clear();
            _gpu.Clear();
        }

        private int _sceneFrame;

        private void Update()
        {
            if (_scenario >= Scenarios.Length) return;
            FrameTimingManager.CaptureFrameTimings();
            uint count = FrameTimingManager.GetLatestTimings(1, _timings);
            float cpu = UnityEngine.Time.unscaledDeltaTime * 1000f;
            float gpu = 0f;
            if (count > 0)
            {
                cpu = (float)_timings[0].cpuFrameTime;
                gpu = (float)_timings[0].gpuFrameTime;
            }

            _sceneFrame++;
            if (_sceneFrame > WarmupFrames)
            {
                _cpu.Add(cpu);
                _gpu.Add(gpu);
            }
            if (_sceneFrame >= WarmupFrames + MeasureFrames)
            {
                FlushScenario();
                _scenario++;
                ApplyScenario(_scenario);
                if (_scenario >= Scenarios.Length) Finish();
            }
        }

        private void FlushScenario()
        {
            var s = Scenarios[_scenario];
            _cpu.Sort();
            _gpu.Sort();
            float cpuP50 = Percentile(_cpu, 0.5f);
            float cpuP95 = Percentile(_cpu, 0.95f);
            float gpuP50 = Percentile(_gpu, 0.5f);
            float gpuP95 = Percentile(_gpu, 0.95f);
            _lines.Add($"{s.Name},cpu_p50={cpuP50:F2},cpu_p95={cpuP95:F2},gpu_p50={gpuP50:F2},gpu_p95={gpuP95:F2}");
            Debug.Log($"[PlayerPerf] {_lines[_lines.Count - 1]}");
        }

        private static float Percentile(List<float> list, float p)
        {
            if (list.Count == 0) return 0f;
            int i = Mathf.Clamp(Mathf.RoundToInt((list.Count - 1) * p), 0, list.Count - 1);
            return list[i];
        }

        private void Finish()
        {
            try
            {
                Directory.CreateDirectory("D:/item/game");
                File.WriteAllLines("D:/item/game/p9_player_perf.csv", _lines);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PlayerPerf] write failed: " + e.Message);
            }
            Application.Quit();
        }
    }
}
