using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fengling.EditorTools
{
    /// <summary>
    /// 无人值守截图：打开 WaterLab → 进 Play → 等稳定 → 相机渲到 RenderTexture 存 PNG → 退出。
    ///
    /// 为什么不用 ScreenCapture.CaptureScreenshot：
    ///   编辑器里没有 Game 窗口时它会静默失败（日志说截了，文件没落盘）。
    ///   直接把相机渲到 RT 再 EncodeToPNG，和窗口布局无关。
    ///
    /// 域重载会清静态字段，状态一律放 SessionState。
    /// </summary>
    public static class WaterLabAutoCapture
    {
        private const string ScenePath = "Assets/Scenes/WaterLab.unity";
        private const string OutPath = "D:/item/game/v2_capture.png";

        private const string PendingKey = "Fengling.Capture.Pending";
        private const string StartKey = "Fengling.Capture.Start";
        private const string ShotKey = "Fengling.Capture.Shot";
        private const string ShotTimeKey = "Fengling.Capture.ShotTime";

        private const int Width = 1280;
        private const int Height = 720;

        private const float WaitBeforeShot = 35f;   // 2 核机器 shader 首帧编译慢
        private const float ExitDelay = 6f;

        public static void Capture()
        {
            if (!File.Exists(ScenePath))
            {
                Debug.LogError("[AutoCapture] 场景不存在，先跑 WaterLabSceneBuilder");
                EditorApplication.Exit(1);
                return;
            }

            if (File.Exists(OutPath)) File.Delete(OutPath);

            EditorSceneManager.OpenScene(ScenePath);

            SessionState.SetBool(PendingKey, true);
            SessionState.SetBool(ShotKey, false);
            SessionState.SetFloat(StartKey, 0f);

            Debug.Log("[AutoCapture] 进入 Play");
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
            float start = SessionState.GetFloat(StartKey, 0f);
            if (start <= 0f)
            {
                SessionState.SetFloat(StartKey, (float)EditorApplication.timeSinceStartup);
                return;
            }
            if (!EditorApplication.isPlaying) return;

            float now = (float)EditorApplication.timeSinceStartup;

            if (!SessionState.GetBool(ShotKey, false))
            {
                if (now - start < WaitBeforeShot) return;

                SessionState.SetBool(ShotKey, true);
                SessionState.SetFloat(ShotTimeKey, now);

                bool ok = CaptureCamera(OutPath);
                Debug.Log($"[AutoCapture] 截图 {(ok ? "成功" : "失败")} t={now - start:0.0}s");
                return;
            }

            if (now - SessionState.GetFloat(ShotTimeKey, now) > ExitDelay)
                Finish();
        }

        private static void Finish()
        {
            EditorApplication.update -= Tick;
            SessionState.SetBool(PendingKey, false);
            Debug.Log("[AutoCapture] 退出");
            EditorApplication.Exit(0);
        }

        private static bool CaptureCamera(string path)
        {
            var cam = Camera.main;
            if (cam == null)
            {
                Debug.LogError("[AutoCapture] 没有 MainCamera");
                return false;
            }

            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            var prevTarget = cam.targetTexture;
            var prevActive = RenderTexture.active;

            try
            {
                cam.targetTexture = rt;
                cam.Render();

                RenderTexture.active = rt;
                var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                tex.Apply();

                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError("[AutoCapture] 截图异常: " + e);
                return false;
            }
            finally
            {
                cam.targetTexture = prevTarget;
                RenderTexture.active = prevActive;
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }
    }
}
