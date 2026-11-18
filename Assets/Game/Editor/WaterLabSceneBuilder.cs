using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fengling.EditorTools
{
    /// <summary>
    /// 一键生成水测试场景。
    ///
    /// 为什么要有这个：手搭测试场景要拖 6 个组件、设 4 个参数，
    /// 每次都容易漏。用脚本生成，保证每次都是一致的起点。
    ///
    /// 菜单：Fengling / Build Water Lab Scene
    /// </summary>
    public static class WaterLabSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/WaterLab.unity";
        private const string SandMatPath = "Assets/Game/Environment/Terrain/CoastSand.mat";

        [MenuItem("Fengling/Build Water Lab Scene")]
        public static void Build()
        {
            // 1. 新场景（自带主相机 + 平行光）
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // 2. 海岸地形
            var terrainGo = new GameObject("CoastTerrain");
            terrainGo.AddComponent<MeshFilter>();
            var mr = terrainGo.AddComponent<MeshRenderer>();
            var terrain = terrainGo.AddComponent<Environment.CoastTerrain>();

            mr.sharedMaterial = GetOrCreateSandMaterial();
            terrain.Generate();

            // 3. 水域
            var waterGo = new GameObject("Water");
            waterGo.AddComponent<Water.WaterModule>();
            waterGo.AddComponent<Water.WaterFieldGizmo>();
            waterGo.AddComponent<Water.WaterLabHUD>();

            // 4. 玩家（站在陆地上）
            var player = new GameObject("Player");
            player.transform.position = new Vector3(0f, terrain.landHeight + 1.5f, terrain.shoreZ - 20f);

            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0f, 0.9f, 0f);

            player.AddComponent<Player.PlayerMotor>();
            player.AddComponent<Player.PlayerBuoyancy>();

            // 5. 相机挂到玩家头上
            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                cam = camGo.AddComponent<Camera>();
            }
            cam.transform.SetParent(player.transform, false);
            cam.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            cam.transform.localRotation = Quaternion.identity;

            // 6. 保存
            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            // 7. 加进 Build Settings
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!scenes.Exists(s => s.path == ScenePath))
                scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();

            Debug.Log($"[Fengling] 水测试场景已生成：{ScenePath}\n" +
                      "按 Play。快捷键：2=Gerstner  1=Flat  [ ]=振幅  P=定格  F1=隐藏面板\n" +
                      "Scene 视图里能看到蓝色线框 —— 那是水面的真实波形。");
        }

        private static Material GetOrCreateSandMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(SandMatPath);
            if (existing != null) return existing;

            var shader = Shader.Find("HDRP/Lit");
            if (shader == null)
            {
                Debug.LogWarning("[Fengling] 找不到 HDRP/Lit，地形会用默认材质（可能是粉红色）");
                return null;
            }

            var mat = new Material(shader) { name = "CoastSand" };
            mat.SetColor("_BaseColor", new Color(0.76f, 0.68f, 0.50f, 1f));
            mat.SetFloat("_Smoothness", 0.05f);
            mat.SetFloat("_Metallic", 0f);

            Directory.CreateDirectory(Path.GetDirectoryName(SandMatPath) ?? "Assets");
            AssetDatabase.CreateAsset(mat, SandMatPath);
            return mat;
        }
    }
}
