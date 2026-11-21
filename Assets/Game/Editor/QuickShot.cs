using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fengling.EditorTools
{
    /// <summary>
    /// 快速截图：打开场景、设时间、可选清雾、显式渲染、存 PNG。
    ///
    /// 参数必须存 SessionState：进 Play 会触 domain reload，静态字段会清零。
    /// 参考之前的坑：相机选择要排除反射相机（targetTexture != null）。
    /// </summary>
    public static class QuickShot
    {
        private const string PendingKey = "QuickShot.Pending";
        private const string StartKey = "QuickShot.Start";
        private const string SceneKey = "QuickShot.Scene";
        private const string HourKey = "QuickShot.Hour";
        private const string NameKey = "QuickShot.Name";
        private const string FogKey = "QuickShot.ClearFog";
        private const string SkyKey = "QuickShot.Sky";

        public static void RunLake() { Shot("Assets/Scenes/Lake.unity", 12f, "lake_12h.png", false); }
        public static void RunLakeNoFog() { Shot("Assets/Scenes/Lake.unity", 12f, "lake_12h_nofog.png", true); }
        public static void RunLake18() { Shot("Assets/Scenes/Lake.unity", 18f, "lake_18h.png", false); }
        public static void RunRpgLab() { Shot("Assets/Scenes/RpgLab.unity", 12f, "rgplab_12h.png", false); }
        public static void RunIsland() { Shot("Assets/Scenes/Island.unity", 10f, "island_10h.png", false); }
        public static void RunTerrain200() { Shot("Assets/Scenes/Terrain200.unity", 10f, "terrain200_10h.png", true); }
        public static void RunLakeSky() { Shot("Assets/Scenes/Lake.unity", 12f, "lake_sky.png", false); SessionState.SetBool(SkyKey, true); }

        private static void Shot(string scene, float hour, string outName, bool clearFog)
        {
            SessionState.SetString(SceneKey, scene);
            SessionState.SetFloat(HourKey, hour);
            SessionState.SetString(NameKey, outName);
            SessionState.SetBool(FogKey, clearFog);

            EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
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
            if (!SessionState.GetBool(PendingKey, false)) return;
            if (!EditorApplication.isPlaying) return;

            double now = EditorApplication.timeSinceStartup;
            float start = SessionState.GetFloat(StartKey, 0f);
            if (start <= 0f) { SessionState.SetFloat(StartKey, (float)now); return; }
            if (now - start < 2.0) return;

            SessionState.SetBool(PendingKey, false);
            EditorApplication.update -= Tick;
            Capture();
        }

        private static void Capture()
        {
            float hour = SessionState.GetFloat(HourKey, 12f);
            string outName = SessionState.GetString(NameKey, "shot.png");
            bool clearFog = SessionState.GetBool(FogKey, false);

            var weather = Object.FindFirstObjectByType<Fengling.Time.WeatherSystem>();
            if (clearFog && weather != null)
            {
                weather.fog = 0f;
                weather.haze = 0f;
                weather.cloudCover = 0.05f;
                Debug.Log("[Shot] 已清雾/降云量");
            }

            var tod = Object.FindFirstObjectByType<Fengling.Time.TimeOfDaySystem>();
            if (tod != null) tod.SetTime(hour);

            Camera cam = null;
            foreach (Camera c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (c == null || c.cameraType != CameraType.Game || c.targetTexture != null) continue;
                cam = c;
                break;
            }
            if (cam == null) { Debug.LogError("[Shot] 没有可用相机"); EditorApplication.Exit(1); return; }

            if (SessionState.GetBool(SkyKey, false))
            {
                var rts = Object.FindFirstObjectByType<Fengling.Player.RtsCameraController>();
                if (rts != null) rts.enabled = false;
                cam.transform.rotation = Quaternion.Euler(-18f, 45f, 0f);
                Debug.Log("[Shot] 已切换到仰视机位");
            }
            Debug.Log("[Shot] 相机 " + cam.name + " pos=" + cam.transform.position.ToString("F1") +
                      " hour=" + hour + " clearFog=" + clearFog +
                      " weatherFog=" + (weather != null ? weather.fog : -1f));

            const int w = 1280, h = 720;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();

            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "images"));
            Directory.CreateDirectory(dir);
            string full = Path.Combine(dir, outName);
            File.WriteAllBytes(full, tex.EncodeToPNG());

            // 采样四角 + 中心，判断画面是否有内容
            Color mid = tex.GetPixel(w / 2, h / 2);
            Color top = tex.GetPixel(w / 2, h - 20);
            Color bottom = tex.GetPixel(w / 2, 20);
            Debug.Log(string.Format("[Shot] 采样 上=({0},{1},{2}) 中=({3},{4},{5}) 下=({6},{7},{8})",
                Mathf.RoundToInt(top.r * 255), Mathf.RoundToInt(top.g * 255), Mathf.RoundToInt(top.b * 255),
                Mathf.RoundToInt(mid.r * 255), Mathf.RoundToInt(mid.g * 255), Mathf.RoundToInt(mid.b * 255),
                Mathf.RoundToInt(bottom.r * 255), Mathf.RoundToInt(bottom.g * 255), Mathf.RoundToInt(bottom.b * 255)));

            cam.targetTexture = null;
            RenderTexture.active = null;
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);

            Debug.Log("[Shot] 已保存 " + full);
            EditorApplication.isPlaying = false;
            EditorApplication.Exit(0);
        }
    }
}
