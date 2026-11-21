using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fengling.EditorTools
{
    /// <summary>
    /// 给所有场景装上三个 Bootstrap（环境 / 水体 / 地形）。
    ///
    /// 为什么需要：决定删除消费者的兜底 Find 后，场景里必须有 Bootstrap 注入，
    /// 否则运行时引用为 null。所以先把所有场景补齐，再删兜底。
    ///
    /// 幂等：已有就不重复加。只保存真正有改动的场景。
    /// 用法：-executeMethod Fengling.EditorTools.SceneBootstrapsInstall.Run
    /// </summary>
    public static class SceneBootstrapsInstall
    {
        public static void Run()
        {
            string[] guids = AssetDatabase.FindAssets("t:Scene");
            var changed = new List<string>();
            var skipped = new List<string>();

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                // 包内场景（HDRP 场景模板等）不能打开
                if (!path.StartsWith("Assets/")) continue;

                Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

                bool dirty = false;
                dirty |= Ensure<Fengling.Time.EnvironmentBootstrap>("EnvironmentBootstrap");
                dirty |= Ensure<Fengling.Water.WaterSceneBootstrap>("WaterSceneBootstrap");
                dirty |= Ensure<Fengling.Environment.TerrainSceneBootstrap>("TerrainSceneBootstrap");

                if (dirty)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    changed.Add(path);
                }
                else
                {
                    skipped.Add(path);
                }
            }

            Debug.Log("[Install] 已更新 " + changed.Count + " 个场景，跳过 " + skipped.Count + " 个（已存在）");
            foreach (string p in changed) Debug.Log("[Install] + " + p);

            EditorApplication.Exit(0);
        }

        private static bool Ensure<T>(string name) where T : Component
        {
            if (Object.FindFirstObjectByType<T>() != null) return false;

            var go = new GameObject(name);
            go.AddComponent<T>();
            return true;
        }
    }
}
