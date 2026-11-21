using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fengling.EditorTools
{
    /// <summary>
    /// 相机/输入模式诊断：Play 后打印相机组件构成和模式状态。
    /// 用法：-executeMethod Fengling.EditorTools.CameraModeDiag.Run
    /// </summary>
    public static class CameraModeDiag
    {
        private const string PendingKey = "CameraModeDiag.Pending";
        private const string StartKey = "CameraModeDiag.Start";

        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Lake.unity", OpenSceneMode.Single);
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
            if (now - start < 1.5) return;

            EditorApplication.update -= Tick;
            SessionState.SetBool(PendingKey, false);
            Report();
        }

        private static void Report()
        {
            // 批处理模式下不会自动出帧，必须显式渲染一次，否则管线根本不跑，
            // 所有 [SRP]/[Shadows] 日志都不会出现。
            var gameCam = Object.FindFirstObjectByType<Camera>();
            if (gameCam != null)
            {
                var rt = new RenderTexture(640, 360, 24, RenderTextureFormat.ARGB32);
                gameCam.targetTexture = rt;
                gameCam.Render();
                gameCam.targetTexture = null;
                rt.Release();
                Object.DestroyImmediate(rt);
                Debug.Log("[CamDiag] 已强制渲染一帧");
            }

            var sb = new StringBuilder();
            sb.AppendLine("[CamDiag] ===== 相机组件 =====");

            Camera[] cams = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
            foreach (Camera c in cams)
            {
                sb.AppendLine("[CamDiag] Camera: " + c.name +
                              " type=" + c.cameraType +
                              " enabled=" + c.enabled +
                              " depth=" + c.depth +
                              " tex=" + (c.targetTexture != null) +
                              " pos=" + c.transform.position.ToString("F1"));
            }

            sb.AppendLine("[CamDiag] ===== 相机脚本 =====");
            MonoBehaviour[] all = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (MonoBehaviour mb in all)
            {
                if (mb == null) continue;
                string n = mb.GetType().Name;
                if (n.Contains("Camera") || n.Contains("Rts") || n.Contains("Motor"))
                {
                    sb.AppendLine("[CamDiag] " + n + " enabled=" + mb.enabled +
                                  " on=" + mb.gameObject.name);
                }
            }

            var rts = Object.FindFirstObjectByType<Fengling.Player.RtsCameraController>();
            if (rts != null)
            {
                sb.AppendLine("[CamDiag] RtsCameraController.IsFollowing=" + rts.IsFollowing +
                              " focusTarget=" + (rts.focusTarget != null ? rts.focusTarget.name : "null") +
                              " motor=" + (rts.playerMotor != null ? rts.playerMotor.name : "null"));
            }
            else sb.AppendLine("[CamDiag] 场景里没有 RtsCameraController");

            var motor = Object.FindFirstObjectByType<Fengling.Player.PlayerMotor>();
            if (motor != null)
                sb.AppendLine("[CamDiag] PlayerMotor.acceptMoveInput=" + motor.acceptMoveInput);
            else sb.AppendLine("[CamDiag] 场景里没有 PlayerMotor");

            var sun = RenderSettings.sun;
            sb.AppendLine("[CamDiag] RenderSettings.sun=" +
                          (sun != null ? sun.name + " shadows=" + sun.shadows : "null"));

            Debug.Log(sb.ToString());

            EditorApplication.isPlaying = false;
            EditorApplication.Exit(0);
        }
    }
}
