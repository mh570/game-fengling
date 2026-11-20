using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fengling.EditorTools
{
    /// <summary>Phase 5 QA：六套 Preset 用 RTS 相机 + 时间系统各抓一张。</summary>
    public static class PresetQaCapture
    {
        private const string PendingKey = "P9.Preset.Pending";
        private const string StartKey = "P9.Preset.Start";
        private const string NameKey = "P9.Preset.Name";

        public static void CaptureRiver() => Run("River");
        public static void CaptureWaterfall() => Run("Waterfall");
        public static void CaptureLake() => Run("Lake");
        public static void CapturePond() => Run("Pond");
        public static void CaptureSwamp() => Run("Swamp");
        public static void CaptureEstuary() => Run("Estuary");

        private static void Run(string name)
        {
            EditorSceneManager.OpenScene("Assets/Scenes/" + name + ".unity");
            SessionState.SetBool(PendingKey, true);
            SessionState.SetFloat(StartKey, 0f);
            SessionState.SetString(NameKey, name);
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

            string name = SessionState.GetString(NameKey, "River");
            var cam = Camera.main;
            if (cam == null) { EditorApplication.Exit(1); return; }

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
            File.WriteAllBytes("D:/item/game/p9_preset_" + name + ".png", tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            cam.targetTexture = prev;
            RenderTexture.active = prevActive;
            rt.Release();
            Object.DestroyImmediate(rt);

            SessionState.SetBool(PendingKey, false);
            EditorApplication.update -= Tick;
            Debug.Log("[PresetQA] captured " + name);
            EditorApplication.Exit(0);
        }
    }
}
