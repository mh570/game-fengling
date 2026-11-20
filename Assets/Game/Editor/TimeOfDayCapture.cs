using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fengling.EditorTools
{
    /// <summary>Phase 2 验收：同机位抓 6/12/18/0 点。</summary>
    public static class TimeOfDayCapture
    {
        private const string ScenePath = "Assets/Scenes/RpgLab.unity";
        private const string PendingKey = "TOD.Pending";
        private const string StartKey = "TOD.Start";
        private const string StageKey = "TOD.Stage";
        private const string StageStartKey = "TOD.StageStart";

        private static readonly float[] Hours = { 6f, 12f, 18f, 0f };
        private static readonly string[] Names = { "06", "12", "18", "00" };

        public static void Run()
        {
            EditorSceneManager.OpenScene(ScenePath);
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
            if (start <= 0f)
            {
                SessionState.SetFloat(StartKey, now);
                return;
            }
            if (now - start < 6f) return;

            var cam = Camera.main;
            var tod = Object.FindFirstObjectByType<Time.TimeOfDaySystem>();
            if (cam == null || tod == null) { EditorApplication.Exit(1); return; }

            var rts = cam.GetComponent<Player.RtsCameraController>();
            if (rts != null)
            {
                rts.pitch = 45f;
                rts.distance = 24f;
                rts.FocusOnPlayer();
            }

            int stage = SessionState.GetInt(StageKey, 0);
            if (stage >= Hours.Length)
            {
                SessionState.SetBool(PendingKey, false);
                EditorApplication.update -= Tick;
                Debug.Log("[TOD] captured 06/12/18/00");
                EditorApplication.Exit(0);
                return;
            }

            float stageStart = SessionState.GetFloat(StageStartKey, 0f);
            if (stageStart <= 0f)
            {
                tod.paused = true;
                tod.SetTime(Hours[stage]);
                SessionState.SetFloat(StageStartKey, now);
                return;
            }

            if (now - stageStart < 1.2f) return;
            Render(cam, $"D:/item/game/p9_time_{Names[stage]}.png");
            SessionState.SetInt(StageKey, stage + 1);
            SessionState.SetFloat(StageStartKey, 0f);
        }

        private static void Render(Camera cam, string path)
        {
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            var prev = cam.targetTexture;
            var prevActive = RenderTexture.active;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            tex.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? "D:/item/game");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            cam.targetTexture = prev;
            RenderTexture.active = prevActive;
            rt.Release();
            Object.DestroyImmediate(rt);
        }
    }
}
