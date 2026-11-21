using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Fengling.TerrainSystem;

namespace Fengling.EditorTools
{
    /// <summary>
    /// 用 TerrainMeshBuilder 生成 200m 地形网格场景（替代 Unity Terrain）。
    /// </summary>
    public static class TerrainSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/Terrain200.unity";
        const string MatPath = "Assets/Game/Rendering/Materials/Mat_TerrainColor.mat";

        [MenuItem("Fengling/Terrain/生成 200m 网格地形")]
        public static void Run()
        {
            // 必须先建场景：NewScene 会销毁之前创建的运行时对象（包括 Mesh）
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var settings = TerrainMeshBuilder.Default;
            // 自动挑一块有海滩也有坡的区域，别选到纯山或纯平地
            var region = TerrainMeshBuilder.FindBestRegion(settings.GridSize * settings.CellSize);
            settings.WorldOrigin = region;

            var meshes = TerrainMeshBuilder.Build(settings, out var origins);
            if (meshes.Count == 0)
            {
                Debug.LogError("[Terrain] 没生成任何网格");
                return;
            }

            // 材质
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            if (mat == null)
            {
                var shader = Shader.Find("Fengling/TerrainColor");
                if (shader == null)
                {
                    Debug.LogError("[Terrain] 找不到 shader Fengling/TerrainColor");
                    return;
                }
                mat = new Material(shader) { name = "Mat_TerrainColor" };
                AssetDatabase.CreateAsset(mat, MatPath);
            }


            // 网格资产目录（必须用 AssetDatabase 建，不能用 Directory）
            if (!AssetDatabase.IsValidFolder("Assets/Game/Terrain/Generated"))
                AssetDatabase.CreateFolder("Assets/Game/Terrain", "Generated");

            var root = new GameObject("Terrain200");
            for (int i = 0; i < meshes.Count; i++)
            {
                // 运行时创建的 Mesh 必须存成资产，否则场景保存后引用会丢失
                string meshPath = string.Format("Assets/Game/Terrain/Generated/TerrainChunk_{0}.asset", i);
                AssetDatabase.DeleteAsset(meshPath);
                AssetDatabase.CreateAsset(meshes[i], meshPath);
                var savedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);

                var go = new GameObject("Chunk_" + i);
                go.transform.SetParent(root.transform, false);

                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = savedMesh;

                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                mr.receiveShadows = true;

                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = savedMesh;
            }

            // 先算这块区域的高度范围，好放相机
            float minH = float.MaxValue, maxH = float.MinValue;
            for (int z = -100; z <= 100; z += 5)
            {
                for (int x = -100; x <= 100; x += 5)
                {
                    float h = TerrainMeshBuilder.SampleHeight(region.x + x, region.y + z);
                    if (h < minH) minH = h;
                    if (h > maxH) maxH = h;
                }
            }
            float centerH = TerrainMeshBuilder.SampleHeight(region.x + 100f, region.y + 100f);
            Debug.Log(string.Format("[Terrain] 区域高度 {0:F1} ~ {1:F1} m，中心 {2:F1} m", minH, maxH, centerH));

            // 相机（俯视，参考视频约 50~60°，视野高度 12~15m）
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.62f, 0.72f, 0.78f);
            cam.fieldOfView = 35f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 600f;
            camGo.transform.position = new Vector3(region.x + 100f, centerH + 40f, region.y + 30f);
            camGo.transform.rotation = Quaternion.Euler(40f, 0f, 0f);

            // 太阳
            var sunGo = new GameObject("Directional Light");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.intensity = 1.4f;
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
            RenderSettings.sun = sun;

            var tod = new GameObject("TimeOfDay").AddComponent<Fengling.Time.TimeOfDaySystem>();
            tod.sun = sun;
            tod.timeOfDay = 10f;

            new GameObject("EnvironmentBootstrap").AddComponent<Fengling.Time.EnvironmentBootstrap>();
            new GameObject("TerrainSceneBootstrap").AddComponent<Fengling.Environment.TerrainSceneBootstrap>();
            new GameObject("PlayerSceneBootstrap").AddComponent<Fengling.Player.PlayerSceneBootstrap>();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            Debug.Log(string.Format("[Terrain] 已创建 {0}：{1} 块，{2}x{2} 格，{3}m",
                ScenePath, meshes.Count, settings.GridSize, settings.GridSize * settings.CellSize));
        }
    }
}
