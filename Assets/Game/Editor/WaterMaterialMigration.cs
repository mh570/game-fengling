using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fengling.EditorTools
{
    /// <summary>
    /// 把 WaterLab 场景里遗留的 HDRP/Lit 内嵌材质迁移到外部 Fengling/Lit 材质。
    ///
    /// 背景：
    ///   MaterialConverter 只遍历 AssetDatabase 里的 .mat 资产，
    ///   而 WaterLab 的 PropFish/PropRock/PropPlant 是直接嵌在 .unity 里的 Material，
    ///   所以管线切换后它们仍是 HDRP/Lit，运行时显示为洋红。
    ///
    /// 这个工具按对象名前缀替换材质，保留场景里的手工摆放和相机设置。
    /// </summary>
    public static class WaterMaterialMigration
    {
        private const string ScenePath = "Assets/Scenes/WaterLab.unity";

        [MenuItem("Fengling/Setup/Migrate WaterLab Materials", false, 42)]
        public static void MigrateWaterLab()
        {
            if (System.IO.File.Exists(ScenePath) == false)
            {
                Debug.LogError("[Migrate] 场景不存在: " + ScenePath);
                return;
            }

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var fish = LitMaterialProvider.Make("PropFish", new Color(0.95f, 0.62f, 0.25f), 0.45f);
            var rock = LitMaterialProvider.Make("PropRock", new Color(0.32f, 0.32f, 0.30f), 0.15f);
            var plant = LitMaterialProvider.Make("PropPlant", new Color(0.18f, 0.42f, 0.26f), 0.30f);
            var player = LitMaterialProvider.Make("PlayerBody", new Color(0.72f, 0.32f, 0.24f), 0.20f);

            var replacements = new Dictionary<Renderer, Material>();
            int scanned = 0;
            int changed = 0;

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    scanned++;
                    var target = TargetFor(r.name, fish, rock, plant, player);
                    if (target == null) continue;

                    var current = r.sharedMaterial;
                    bool legacy = current == null || current.shader == null ||
                                  current.shader.name == "HDRP/Lit" ||
                                  current.shader.name == "HDRP/Unlit" ||
                                  current.shader.name == "Standard" ||
                                  current.shader.name.StartsWith("Universal Render Pipeline/");

                    if (!legacy) continue;
                    if (current == target) continue;

                    replacements[r] = target;
                }
            }

            foreach (var pair in replacements)
            {
                Undo.RecordObject(pair.Key, "Migrate water lab material");
                pair.Key.sharedMaterial = pair.Value;
                EditorUtility.SetDirty(pair.Key);
                changed++;
            }

            var waterTemplate = WaterMaterialProvider.GetOrCreate();
            int waterChanged = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var sm in root.GetComponentsInChildren<Water.WaterSurfaceRenderer>(true))
                {
                    if (sm.waterMaterialTemplate == waterTemplate) continue;
                    Undo.RecordObject(sm, "Assign water material template");
                    sm.waterMaterialTemplate = waterTemplate;
                    EditorUtility.SetDirty(sm);
                    waterChanged++;
                }
            }

            // 自建管线不消费 HDRP Volume，旧场景里的 Global Volume 只会误导。
            int removedVolume = 0;
            var globalVolume = GameObject.Find("Global Volume");
            if (globalVolume != null)
            {
                Undo.DestroyObjectImmediate(globalVolume);
                removedVolume = 1;
            }

            // 把 200m 水面扩到 1000m，避免低机位看到水面网格边缘的黑线。
            int resizedWater = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var wm in root.GetComponentsInChildren<Water.WaterModule>(true))
                {
                    var size = wm.Settings.size;
                    if (size.x >= 1000f && size.y >= 1000f) continue;
                    Undo.RecordObject(wm, "Resize WaterLab water");
                    wm.Settings.size = new Vector2(1000f, 1000f);
                    EditorUtility.SetDirty(wm);
                    resizedWater++;
                }
            }

            // 自建管线使用程序化天空盒 + Skybox 环境光；曝光/ACES/Bloom 由后处理负责。
            SkyboxSetup.Apply();

            if (changed > 0 || waterChanged > 0 || removedVolume > 0 || resizedWater > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
            }

            Debug.Log($"[Migrate] WaterLab 扫描 {scanned} 个 Renderer，替换 {changed} 个材质，" +
                      $"绑定 {waterChanged} 个水面材质模板，移除 {removedVolume} 个 HDRP Global Volume，" +
                      $"扩展 {resizedWater} 个水面。");
            LogRemainingLegacy(scene);
        }

        private static Material TargetFor(string name, Material fish, Material rock, Material plant, Material player)
        {
            if (name.StartsWith("Fish")) return fish;
            if (name.StartsWith("Rock")) return rock;
            if (name.StartsWith("Plant")) return plant;
            if (name == "Player") return player;
            return null;
        }

        private static void LogRemainingLegacy(Scene scene)
        {
            var remaining = new List<string>();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    var mat = r.sharedMaterial;
                    var shaderName = mat != null && mat.shader != null ? mat.shader.name : "<null>";
                    if (shaderName == "HDRP/Lit" || shaderName == "HDRP/Unlit" ||
                        shaderName == "Standard" || shaderName.StartsWith("Universal Render Pipeline/"))
                    {
                        remaining.Add(r.name + " | " + mat.name + " | " + shaderName);
                    }
                }
            }

            if (remaining.Count == 0)
            {
                Debug.Log("[Migrate] 没有剩余 HDRP/URP/Standard 材质。");
                return;
            }

            Debug.LogWarning("[Migrate] 仍有遗留材质:\n" + string.Join("\n", remaining));
        }
    }
}
