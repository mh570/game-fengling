using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fengling.EditorTools
{
    /// <summary>水面颜色探针：红浅水、蓝深水、关反射，等一帧应用材质后抓图。</summary>
    public static class WaterDiagnoseCapture
    {
        private const string PendingKey = "WaterDiag.Pending";
        private const string StartKey = "WaterDiag.Start";
        private const string StageKey = "WaterDiag.Stage";
        private const string StageStartKey = "WaterDiag.StageStart";

        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Lake.unity");
            SessionState.SetBool(PendingKey, true);
            SessionState.SetFloat(StartKey, 0f);
            SessionState.SetInt(StageKey, 0);
            SessionState.SetFloat(StageStartKey, 0f);
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
            if (start <= 0f) { SessionState.SetFloat(StartKey, now); return; }
            if (now - start < 7f) return;

            var tod = Object.FindFirstObjectByType<Time.TimeOfDaySystem>();
            var cam = Camera.main;
            if (tod == null || cam == null) { EditorApplication.Exit(1); return; }

            int stage = SessionState.GetInt(StageKey, 0);
            if (stage == 0)
            {
                tod.paused = true;
                tod.SetTime(12f);
                foreach (var w in Object.FindObjectsByType<Water.WaterSurfaceRenderer>(FindObjectsSortMode.None))
                {
                    w.shallowColor = Color.red;
                    w.deepColor = Color.blue;
                    w.reflectionStrength = 0f;
                    w.stylized = 0f;
                    w.sunIntensity = 0f;
                }
                SessionState.SetInt(StageKey, 1);
                SessionState.SetFloat(StageStartKey, now);
                return;
            }

            if (now - SessionState.GetFloat(StageStartKey, 0f) < 0.8f) return;

            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            var prev = cam.targetTexture;
            var prevActive = RenderTexture.active;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            tex.Apply();
            File.WriteAllBytes("D:/item/game/p9_water_probe.png", tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            cam.targetTexture = prev;
            RenderTexture.active = prevActive;
            rt.Release();
            Object.DestroyImmediate(rt);

            SessionState.SetBool(PendingKey, false);
            EditorApplication.update -= Tick;
            Debug.Log("[WaterDiag] captured");
            EditorApplication.Exit(0);
        }
    }
}
