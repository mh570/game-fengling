using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fengling.EditorTools
{
    /// <summary>
    /// Shader 审计：找出所有"材质用了非自建管线 shader"的 Renderer。
    ///
    /// 为什么需要：自建 SRP 下，内置 Standard 等 shader 会渲染成洋红错误材质。
    /// 项目有 MaterialConverter，但没有强制机制，新加对象很容易漏。
    ///
    /// 本工具**只报告，不修改**（2026-11-21 决定 D2）——
    /// 有些对象可能故意用特殊 shader，自动替换会改错。
    ///
    /// 用法：
    ///   菜单 Fengling/审计 Shader
    ///   或批处理 -executeMethod Fengling.EditorTools.ShaderAudit.Run
    /// </summary>
    public static class ShaderAudit
    {
        /// <summary>自建管线的 shader 前缀。</summary>
        private const string OwnPrefix = "Fengling/";

        /// <summary>
        /// 在自建 SRP 下仍然正常工作的 shader（不参与光照，不依赖内置管线）。
        /// 这些不算失控。
        /// </summary>
        private static readonly HashSet<string> Neutral = new HashSet<string>
        {
            "Sprites/Default",
            "Sprites/NoFog",
            "UI/Default",
            "TextMeshPro/Distance Field",
            "TextMeshPro/Mobile/Distance Field",
            "TextMeshPro/Sprite",
            "Particles/Standard Unlit",
            "Hidden/InternalErrorShader",
        };

        /// <summary>
        /// 在 Awake 里自建材质的组件。这类 Renderer 在编辑期材质槽是空的，
        /// 属于设计如此，不能算失控。
        /// </summary>
        private static readonly HashSet<string> RuntimeMaterialized = new HashSet<string>
        {
            "WaterSurfaceRenderer",
        };

        private sealed class Finding
        {
            public string Asset;
            public string Object;
            public string Path;
            public string Shader;
        }

        [MenuItem("Fengling/审计 Shader")]
        public static void Run()
        {
            var findings = new List<Finding>();
            var shaderCounts = new Dictionary<string, int>();

            string[] sceneGuids = AssetDatabase.FindAssets("t:Scene");
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab");

            Debug.Log("[Audit] 开始：场景 " + sceneGuids.Length + " 个，Prefab " + prefabGuids.Length + " 个");

            // 场景：用 Additive 打开，避免影响当前编辑中的场景
            foreach (string guid in sceneGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                // FindAssets 会返回包内的场景（HDRP 的场景模板等），包内场景不能打开
                if (!path.StartsWith("Assets/")) continue;

                Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                try
                {
                    foreach (GameObject root in scene.GetRootGameObjects())
                    {
                        ScanScene(root, path, findings, shaderCounts);
                    }
                }
                finally
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }

            // Prefab
            foreach (string guid in prefabGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/")) continue;

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;
                ScanPrefab(prefab, path, findings, shaderCounts);
            }

            Report(findings, shaderCounts);
        }

        private static void ScanScene(GameObject root, string path, List<Finding> findings,
                                       Dictionary<string, int> shaderCounts)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in renderers)
            {
                ScanRenderer(r, path, BuildHierarchy(r.transform), path, findings, shaderCounts);
            }
        }

        private static void ScanPrefab(GameObject prefab, string path, List<Finding> findings,
                                        Dictionary<string, int> shaderCounts)
        {
            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in renderers)
            {
                ScanRenderer(r, prefab.name, BuildHierarchy(r.transform), path, findings, shaderCounts);
            }
        }

        private static string BuildHierarchy(Transform t)
        {
            var parts = new List<string>();
            while (t != null)
            {
                parts.Add(t.name);
                t = t.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static void ScanRenderer(Renderer renderer, string assetLabel, string objectPath, string assetPath,
                                          List<Finding> findings, Dictionary<string, int> shaderCounts)
        {
            // 运行时自建材质的对象（水面等）在编辑期材质槽为空，跳过
            foreach (MonoBehaviour mb in renderer.GetComponents<MonoBehaviour>())
            {
                if (mb != null && RuntimeMaterialized.Contains(mb.GetType().Name))
                {
                    return;
                }
            }

            Material[] materials = renderer.sharedMaterials;
            if (materials == null || materials.Length == 0) return;

            foreach (Material m in materials)
            {
                if (m == null)
                {
                    findings.Add(new Finding { Asset = assetLabel, Object = objectPath, Path = assetPath, Shader = "<null material>" });
                    continue;
                }

                if (m.shader == null)
                {
                    findings.Add(new Finding { Asset = assetLabel, Object = objectPath, Path = assetPath, Shader = "<null shader>" });
                    continue;
                }

                string shaderName = m.shader.name;
                if (shaderName.StartsWith(OwnPrefix)) continue;
                if (Neutral.Contains(shaderName)) continue;

                findings.Add(new Finding { Asset = assetLabel, Object = objectPath, Path = assetPath, Shader = shaderName });

                if (!shaderCounts.ContainsKey(shaderName)) shaderCounts[shaderName] = 0;
                shaderCounts[shaderName]++;
            }
        }

        private static void Report(List<Finding> findings, Dictionary<string, int> shaderCounts)
        {
            Debug.Log("[Audit] ===== 结果 =====");
            Debug.Log("[Audit] 失控材质引用: " + findings.Count + " 处");

            if (findings.Count == 0)
            {
                Debug.Log("[Audit] 没有发现失控材质");
                return;
            }

            Debug.Log("[Audit] 按 shader 汇总：");
            foreach (var pair in shaderCounts)
            {
                Debug.Log("    " + pair.Key + "  x" + pair.Value);
            }

            Debug.Log("[Audit] 明细（前 40 条）：");
            int limit = Mathf.Min(findings.Count, 40);
            for (int i = 0; i < limit; i++)
            {
                Finding f = findings[i];
                Debug.Log("    " + f.Shader + "  @  " + f.Asset + "  ::  " + f.Object);
            }

            // 同时落盘一份完整清单，方便逐个处理
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "_shader_audit.txt"));
            var sb = new StringBuilder();
            sb.AppendLine("shader\tasset\tobject\tpath");
            foreach (Finding f in findings)
            {
                sb.Append(f.Shader).Append('\t')
                  .Append(f.Asset).Append('\t')
                  .Append(f.Object).Append('\t')
                  .Append(f.Path).AppendLine();
            }

            File.WriteAllText(path, sb.ToString());
            Debug.Log("[Audit] 完整清单已写入: " + path);
        }
    }
}
