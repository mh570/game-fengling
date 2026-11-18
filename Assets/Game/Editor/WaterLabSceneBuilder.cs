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

        [MenuItem("Fengling/Build Water Lab Scene")]
        public static void Build()
        {
            // 1. 新场景（自带主相机 + 平行光）
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // 2. 水域
            var waterGo = new GameObject("Water");
            waterGo.AddComponent<Water.WaterModule>();
            waterGo.AddComponent<Water.WaterFieldGizmo>();
            waterGo.AddComponent<Water.WaterLabHUD>();

            // 3. 小岛地面（比水面高 0.5 m，走出边缘就会掉进水里）
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Island";
            ground.transform.position = new Vector3(0f, 0.5f, 0f);
            ground.transform.localScale = new Vector3(3f, 1f, 3f);   // 30 m × 30 m

            // 4. 玩家
            var player = new GameObject("Player");
            player.transform.position = new Vector3(0f, 2f, -8f);

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
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!scenes.Exists(s => s.path == ScenePath))
                scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();

            Debug.Log($"[Fengling] 水测试场景已生成：{ScenePath}\n" +
                      "直接按 Play。快捷键：2=Gerstner  1=Flat  [ ]=振幅  P=定格  F1=隐藏面板");
        }
    }
}
