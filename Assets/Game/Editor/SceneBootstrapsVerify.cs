using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fengling.EditorTools
{
    /// <summary>
    /// 场景装配验证：把三个 Bootstrap 装进 RpgLab，跑起来后断言注入真的生效。
    ///
    /// 断言的是"引用非空且指向场景里那个实例"，而不只是"没报错"。
    /// 用法：-executeMethod Fengling.EditorTools.SceneBootstrapsVerify.Run
    /// </summary>
    public static class SceneBootstrapsVerify
    {
        private const string ScenePath = "Assets/Scenes/RpgLab.unity";
        private const string PendingKey = "SceneBootstrapsVerify.Pending";
        private const string StartKey = "SceneBootstrapsVerify.Start";

        private static readonly List<string> Failures = new List<string>();

        public static void Run()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            bool dirty = false;
            dirty |= Ensure<Fengling.Time.EnvironmentBootstrap>("EnvironmentBootstrap");
            dirty |= Ensure<Fengling.Water.WaterSceneBootstrap>("WaterSceneBootstrap");
            dirty |= Ensure<Fengling.Environment.TerrainSceneBootstrap>("TerrainSceneBootstrap");

            if (dirty)
            {
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
                Debug.Log("[SceneVerify] 已安装 Bootstrap 并保存场景");
            }

            SessionState.SetBool(PendingKey, true);
            SessionState.SetFloat(StartKey, 0f);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.isPlaying = true;
        }

        private static bool Ensure<T>(string name) where T : Component
        {
            if (Object.FindFirstObjectByType<T>() != null) return false;

            var go = new GameObject(name);
            go.AddComponent<T>();
            return true;
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

            EditorApplication.update -= Tick;
            SessionState.SetBool(PendingKey, false);
            Verify();
        }

        private static void Verify()
        {
            Failures.Clear();

            var module = Object.FindFirstObjectByType<Fengling.Water.WaterModule>();
            var terrain = Object.FindFirstObjectByType<Fengling.Environment.CoastTerrain>();

            Check("场景有 WaterModule", module != null);
            Check("场景有 CoastTerrain", terrain != null);

            // 水面渲染器
            var renderer = Object.FindFirstObjectByType<Fengling.Water.WaterSurfaceRenderer>();
            Check("场景有 WaterSurfaceRenderer", renderer != null);
            if (renderer != null)
            {
                Check("WaterSurfaceRenderer.water 已注入", renderer.water != null);
                Check("注入的是同一个 WaterModule", ReferenceEquals(renderer.water, module));
            }

            // 地形消费者
            var rts = Object.FindFirstObjectByType<Fengling.Player.RtsCameraController>();
            if (rts != null)
            {
                Check("RtsCameraController.terrain 已注入", rts.terrain != null);
                Check("注入的是同一个 CoastTerrain", ReferenceEquals(rts.terrain, terrain));
            }
            else
            {
                Debug.Log("[SceneVerify] 场景无 RtsCameraController，跳过");
            }

            var blob = Object.FindFirstObjectByType<Fengling.Player.PlayerBlobShadow>();
            if (blob != null) Check("PlayerBlobShadow.terrain 已注入", blob.terrain != null);

            // 玩家浮力：_water 是私有的，用反射读
            var buoyancy = Object.FindFirstObjectByType<Fengling.Player.PlayerBuoyancy>();
            if (buoyancy != null)
            {
                FieldInfo f = typeof(Fengling.Player.PlayerBuoyancy)
                    .GetField("_water", BindingFlags.NonPublic | BindingFlags.Instance);
                object value = f != null ? f.GetValue(buoyancy) : null;
                Check("PlayerBuoyancy._water 已注入", value != null);
            }
            else
            {
                Debug.Log("[SceneVerify] 场景无 PlayerBuoyancy，跳过");
            }

            if (Failures.Count == 0) Debug.Log("[SceneVerify] ===== 全部通过 =====");
            else
            {
                Debug.LogError("[SceneVerify] ===== 失败 " + Failures.Count + " 项 =====");
                foreach (string f in Failures) Debug.LogError("[SceneVerify] FAIL: " + f);
            }

            EditorApplication.isPlaying = false;
            EditorApplication.Exit(Failures.Count == 0 ? 0 : 1);
        }

        private static void Check(string label, bool ok)
        {
            if (ok) Debug.Log("[SceneVerify] OK: " + label);
            else Failures.Add(label);
        }
    }
}
