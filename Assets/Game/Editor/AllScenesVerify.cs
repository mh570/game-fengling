using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fengling.EditorTools
{
    /// <summary>
    /// 全场景注入验证：逐个打开场景、进 Play、检查消费者是否都被注入。
    ///
    /// 为什么需要：删掉兜底 Find 后，场景缺 Bootstrap 会**静默失效**（引用为 null，不报错）。
    /// 只验证两个代表场景不够，必须全跑。
    ///
    /// 跨场景跑 Play 状态机，用 SessionState 保存进度（进出 Play 都会触发 domain reload）。
    /// 用法：-executeMethod Fengling.EditorTools.AllScenesVerify.Run
    /// 退出码 0 = 全部通过。
    /// </summary>
    public static class AllScenesVerify
    {
        private const string PendingKey = "AllScenesVerify.Pending";
        private const string IndexKey = "AllScenesVerify.Index";
        private const string PhaseKey = "AllScenesVerify.Phase";
        private const string StartKey = "AllScenesVerify.Start";
        private const string ResultKey = "AllScenesVerify.Results";

        private static readonly List<string> Failures = new List<string>();

        public static void Run()
        {
            var scenes = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Scene"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/")) continue;
                scenes.Add(path);
            }
            scenes.Sort();

            StringBuilder sb = new StringBuilder();
            foreach (string s in scenes) sb.Append(s).Append(';');
            SessionState.SetString(ResultKey, sb.ToString());

            SessionState.SetInt(IndexKey, 0);
            SessionState.SetInt(PhaseKey, 0);
            SessionState.SetFloat(StartKey, 0f);
            SessionState.SetBool(PendingKey, true);

            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Debug.Log("[AllVerify] 共 " + scenes.Count + " 个场景待验证");
        }

        [InitializeOnLoadMethod]
        private static void Reattach()
        {
            if (!SessionState.GetBool(PendingKey, false)) return;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        private static string[] Scenes()
        {
            string all = SessionState.GetString(ResultKey, "");
            return all.Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries);
        }

        private static void Tick()
        {
            if (!SessionState.GetBool(PendingKey, false)) return;

            string[] scenes = Scenes();
            if (scenes.Length == 0) { EditorApplication.Exit(1); return; }

            int index = SessionState.GetInt(IndexKey, 0);
            int phase = SessionState.GetInt(PhaseKey, 0);

            if (index >= scenes.Length) { Finish(); return; }

            if (phase == 0)
            {
                EditorSceneManager.OpenScene(scenes[index], OpenSceneMode.Single);
                SessionState.SetFloat(StartKey, 0f);
                SessionState.SetInt(PhaseKey, 1);
                EditorApplication.isPlaying = true;
                return;
            }

            if (phase == 1)
            {
                if (!EditorApplication.isPlaying) return;

                double now = EditorApplication.timeSinceStartup;
                float start = SessionState.GetFloat(StartKey, 0f);
                if (start <= 0f) { SessionState.SetFloat(StartKey, (float)now); return; }
                if (now - start < 1.2) return;

                VerifyScene(scenes[index]);
                EditorApplication.isPlaying = false;
                SessionState.SetInt(PhaseKey, 2);
                return;
            }

            // phase 2：等 Play 完全退出再开下一个场景
            if (EditorApplication.isPlaying) return;
            SessionState.SetInt(IndexKey, index + 1);
            SessionState.SetInt(PhaseKey, 0);
        }

        private static void VerifyScene(string scenePath)
        {
            int issues = 0;

            // 水面渲染器必须拿到 WaterModule
            var surface = Object.FindFirstObjectByType<Fengling.Water.WaterSurfaceRenderer>();
            if (surface != null && surface.water == null)
            {
                Failures.Add(scenePath + " : WaterSurfaceRenderer.water 未注入");
                issues++;
            }

            // 地形消费者
            var rts = Object.FindFirstObjectByType<Fengling.Player.RtsCameraController>();
            if (rts != null && rts.terrain == null)
            {
                Failures.Add(scenePath + " : RtsCameraController.terrain 未注入");
                issues++;
            }

            var blob = Object.FindFirstObjectByType<Fengling.Player.PlayerBlobShadow>();
            if (blob != null && blob.terrain == null)
            {
                Failures.Add(scenePath + " : PlayerBlobShadow.terrain 未注入");
                issues++;
            }

            Debug.Log("[AllVerify] " + scenePath + " -> " + (issues == 0 ? "OK" : issues + " 个问题"));
        }

        private static void Finish()
        {
            SessionState.SetBool(PendingKey, false);
            EditorApplication.update -= Tick;

            if (Failures.Count == 0)
            {
                Debug.Log("[AllVerify] ===== 全部场景通过 =====");
            }
            else
            {
                Debug.LogError("[AllVerify] ===== 失败 " + Failures.Count + " 项 =====");
                foreach (string f in Failures) Debug.LogError("[AllVerify] FAIL: " + f);
            }

            EditorApplication.Exit(Failures.Count == 0 ? 0 : 1);
        }
    }
}
