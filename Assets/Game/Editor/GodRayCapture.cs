using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fengling.EditorTools
{
    /// <summary>GodRay 验收：加一排高柱作为遮挡，低机位朝太阳抓图。</summary>
    public static class GodRayCapture
    {
        private const string PendingKey = "GodRay.Pending";
        private const string StartKey = "GodRay.Start";

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
            if (now - start < 7f) return;

            var tod = Object.FindFirstObjectByType<Time.TimeOfDaySystem>();
            var weather = Object.FindFirstObjectByType<Time.WeatherSystem>();
            var cam = Camera.main;
            if (tod == null || weather == null || cam == null) { EditorApplication.Exit(1); return; }

            tod.paused = true;
            tod.SetTime(8.2f);
            weather.cloudCover = 0.25f;
            weather.haze = 0.35f;
            weather.fog = 0.55f;

            var rts = cam.GetComponent<Player.RtsCameraController>();
            if (rts != null) rts.enabled = false;

            // 一点低机位，朝太阳方向看。
            Vector3 sunDir = -tod.sun.transform.forward;
            Vector3 flatSun = new Vector3(sunDir.x, 0f, sunDir.z).normalized;
            cam.transform.position = new Vector3(0f, 5f, -10f);
            cam.transform.rotation = Quaternion.LookRotation(flatSun, Vector3.up);

            // 在相机与太阳之间摆一排高柱，制造遮挡缝隙。
            var mat = new Material(Shader.Find("Fengling/Lit")) { color = new Color(0.35f, 0.30f, 0.24f) };
            for (int i = 0; i < 12; i++)
            {
                var pillar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pillar.name = "GodRayPillar_" + i;
                float x = (i - 5.5f) * 2.2f;
                pillar.transform.position = new Vector3(x, 4f, 8f + Mathf.Abs(i - 5.5f) * 2.5f);
                pillar.transform.localScale = new Vector3(1.1f, 8f, 1.1f);
                pillar.GetComponent<Renderer>().sharedMaterial = mat;
            }

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
            File.WriteAllBytes("D:/item/game/p9_godray_lab.png", tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            cam.targetTexture = prev;
            RenderTexture.active = prevActive;
            rt.Release();
            Object.DestroyImmediate(rt);

            SessionState.SetBool(PendingKey, false);
            EditorApplication.update -= Tick;
            Debug.Log("[GodRayQA] captured");
            EditorApplication.Exit(0);
        }
    }
}
