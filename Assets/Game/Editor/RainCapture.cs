using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fengling.EditorTools
{
    /// <summary>临时验收：12:00 雨天，检查雨粒子、水面雨纹和光照衰减。</summary>
    public static class RainCapture
    {
        private const string PendingKey = "Rain.Pending";
        private const string StartKey = "Rain.Start";

        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/RpgLab.unity");
            SessionState.SetBool(PendingKey, true);
            SessionState.SetFloat(StartKey, 0f);
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
            if (now - start < 8f) return;

            var tod = Object.FindFirstObjectByType<Time.TimeOfDaySystem>();
            var weather = Object.FindFirstObjectByType<Time.WeatherSystem>();
            var cam = Camera.main;
            if (tod == null || weather == null || cam == null) { EditorApplication.Exit(1); return; }

            tod.paused = true;
            tod.SetTime(12f);
            weather.cloudCover = 0.75f;
            weather.haze = 0.35f;
            weather.fog = 0.35f;
            weather.rain = 0.9f;
            var meshRain = Object.FindFirstObjectByType<Time.MeshRain>();
            if (meshRain != null) meshRain.Refresh();

            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            var prev = cam.targetTexture;
            var prevActive = RenderTexture.active;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            tex.Apply();
            Directory.CreateDirectory("D:/item/game");
            File.WriteAllBytes("D:/item/game/p9_weather_rain.png", tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            cam.targetTexture = prev;
            RenderTexture.active = prevActive;
            rt.Release();
            Object.DestroyImmediate(rt);

            SessionState.SetBool(PendingKey, false);
            EditorApplication.update -= Tick;
            Debug.Log("[RainQA] captured");
            EditorApplication.Exit(0);
        }
    }
}
