using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fengling.EditorTools
{
    /// <summary>夜间月影 QA。</summary>
    public static class MoonShadowCapture
    {
        private const string PendingKey = "MoonShadow.Pending";
        private const string StartKey = "MoonShadow.Start";

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

            weather.cloudCover = 0.05f;
            weather.haze = 0.05f;
            weather.fog = 0.05f;
            tod.paused = true;
            tod.SetTime(22.0f);

            cam.transform.position = new Vector3(0f, 12f, -9f);
            var player = GameObject.FindWithTag("Player");
            if (player == null) player = GameObject.Find("Player");
            if (player != null)
            {
                cam.transform.position = player.transform.position + new Vector3(3f, 5f, -6f);
                cam.transform.LookAt(player.transform.position + Vector3.up * 1.2f);
            }
            else
            {
                cam.transform.rotation = Quaternion.Euler(45f, 0f, 0f);
            }
            var rts = cam.GetComponent<Player.RtsCameraController>();
            if (rts != null) rts.enabled = false;

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
            File.WriteAllBytes("D:/item/game/p9_moon_shadow.png", tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            cam.targetTexture = prev;
            RenderTexture.active = prevActive;
            rt.Release();
            Object.DestroyImmediate(rt);

            SessionState.SetBool(PendingKey, false);
            EditorApplication.update -= Tick;
            Debug.Log("[MoonShadowQA] captured");
            EditorApplication.Exit(0);
        }
    }
}
