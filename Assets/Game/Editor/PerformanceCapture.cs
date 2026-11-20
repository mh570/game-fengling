using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Fengling.EditorTools
{
    /// <summary>720p 性能基线：固定 RT 渲染，统计 CPU 提交帧时间。</summary>
    public static class PerformanceCapture
    {
        private const string PendingKey = "Perf.Pending";
        private const string StartKey = "Perf.Start";
        private const int Width = 1280;
        private const int Height = 720;
        private const int Frames = 90;

        private static readonly List<string> Lines = new List<string>();

        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/RpgLab.unity");
            SessionState.SetBool(PendingKey, true);
            SessionState.SetFloat(StartKey, 0f);
            Lines.Clear();
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.isPlaying = true;
        }

        [InitializeOnLoadMethod]
        private static void Reattach()
        {
            if (!SessionState.GetBool(PendingKey, false)) return;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            if (!SessionState.GetBool(PendingKey, false) || !EditorApplication.isPlaying) return;
            float now = (float)EditorApplication.timeSinceStartup;
            float start = SessionState.GetFloat(StartKey, 0f);
            if (start <= 0f)
            {
                SessionState.SetFloat(StartKey, now);
                return;
            }
            if (now - start < 7f) return;

            var tod = UnityEngine.Object.FindFirstObjectByType<Time.TimeOfDaySystem>();
            var weather = UnityEngine.Object.FindFirstObjectByType<Time.WeatherSystem>();
            var cam = Camera.main;
            if (tod == null || weather == null || cam == null) { EditorApplication.Exit(1); return; }

            var rts = cam.GetComponent<Player.RtsCameraController>();
            if (rts != null) rts.enabled = false;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;

            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            cam.targetTexture = rt;
            var prevActive = RenderTexture.active;

            Measure("day_clear", 12f, 0.05f, 0.05f, 0.05f, 0f, tod, weather, cam, rt);
            Measure("night_clear", 22f, 0.05f, 0.05f, 0.05f, 0f, tod, weather, cam, rt);
            Measure("overcast", 12f, 0.90f, 0.35f, 0.50f, 0f, tod, weather, cam, rt);
            Measure("rain", 12f, 0.80f, 0.35f, 0.40f, 0.90f, tod, weather, cam, rt);

            RenderTexture.active = prevActive;
            cam.targetTexture = null;
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);

            Directory.CreateDirectory("D:/item/game");
            File.WriteAllLines("D:/item/game/p9_perf.csv", Lines);
            foreach (var line in Lines) Debug.Log("[Perf] " + line);

            SessionState.SetBool(PendingKey, false);
            EditorApplication.update -= Tick;
            EditorApplication.Exit(0);
        }

        private static void Measure(string name, float hour, float cloud, float haze, float fog, float rain,
                                    Time.TimeOfDaySystem tod, Time.WeatherSystem weather, Camera cam, RenderTexture rt)
        {
            weather.cloudCover = cloud;
            weather.haze = haze;
            weather.fog = fog;
            weather.rain = rain;
            weather.snow = 0f;
            tod.paused = true;
            tod.SetTime(hour);

            // 预热，避免第一帧 shader 编译/RT 初始化污染。
            for (int i = 0; i < 10; i++) cam.Render();

            var times = new List<double>(Frames);
            var sw = new Stopwatch();
            var px = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            for (int i = 0; i < Frames; i++)
            {
                sw.Restart();
                cam.Render();
                RenderTexture.active = rt;
                px.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                sw.Stop();
                times.Add(sw.Elapsed.TotalMilliseconds);
            }
            UnityEngine.Object.DestroyImmediate(px);

            times.Sort();
            double sum = 0;
            foreach (var t in times) sum += t;
            double avg = sum / times.Count;
            double p50 = times[times.Count / 2];
            double p95 = times[Mathf.Clamp((int)(times.Count * 0.95), 0, times.Count - 1)];
            Lines.Add($"{name},avg_ms={avg:F2},p50_ms={p50:F2},p95_ms={p95:F2},fps_p50={1000.0 / p50:F1}");
        }
    }
}
