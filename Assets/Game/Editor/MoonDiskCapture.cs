using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fengling.EditorTools
{
    /// <summary>临时验收：22:00 把相机对准月亮，检查月相圆盘。</summary>
    public static class MoonDiskCapture
    {
        private const string PendingKey = "MoonDisk.Pending";
        private const string StartKey = "MoonDisk.Start";

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
            if (now - start < 6f) return;

            var tod = Object.FindFirstObjectByType<Time.TimeOfDaySystem>();
            var cam = Camera.main;
            if (tod == null || cam == null || tod.moon == null) { EditorApplication.Exit(1); return; }

            tod.paused = true;
            tod.SetTime(22f);

            var rts = cam.GetComponent<Player.RtsCameraController>();
            if (rts != null) rts.enabled = false;
            cam.transform.position = new Vector3(0f, 5f, 0f);
            cam.transform.rotation = Quaternion.LookRotation(-tod.moon.transform.forward, Vector3.up);

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
            File.WriteAllBytes("D:/item/game/p9_moon_disk.png", tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            cam.targetTexture = prev;
            RenderTexture.active = prevActive;
            rt.Release();
            Object.DestroyImmediate(rt);

            SessionState.SetBool(PendingKey, false);
            EditorApplication.update -= Tick;
            Debug.Log($"[MoonDisk] captured phase={tod.MoonIlluminatedFraction:0.00}");
            EditorApplication.Exit(0);
        }
    }
}
