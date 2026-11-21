using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Fengling.TerrainSystem;

namespace Fengling.EditorTools
{
    /// <summary>
    /// 生成 200m 地形网格场景。
    /// 网格生成用 TerrainChunkBuilder（移植自 Sebastian Lague 的分块+缝合实现）。
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
            var region = TerrainMeshBuilder.FindBestRegion(settings.GridSize * settings.CellSize);
            settings.WorldOrigin = region;

            TerrainCarver.PlanDefault(settings.WorldOrigin, settings.GridSize * settings.CellSize);

            var chunks = TerrainChunkBuilder.Build(
                settings.WorldOrigin, settings.GridSize, settings.ChunkCount,
                settings.CellSize, TerrainMeshBuilder.HeightCompression);

            if (chunks.Count == 0)
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

            if (!AssetDatabase.IsValidFolder("Assets/Game/Terrain/Generated"))
                AssetDatabase.CreateFolder("Assets/Game/Terrain", "Generated");

            var root = new GameObject("Terrain200");
            for (int i = 0; i < chunks.Count; i++)
            {
                string meshPath = string.Format("Assets/Game/Terrain/Generated/TerrainChunk_{0}.asset", i);
                AssetDatabase.DeleteAsset(meshPath);
                AssetDatabase.CreateAsset(chunks[i].mesh, meshPath);
                var savedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);

                var go = new GameObject("Chunk_" + i);
                go.transform.SetParent(root.transform, false);
                // 顶点已经是世界坐标，不能再加偏移
                go.transform.position = Vector3.zero;

                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = savedMesh;

                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                mr.receiveShadows = true;
            }

            // 区域高度（相机用）
            float minH = float.MaxValue, maxH = float.MinValue;
            for (int z = 0; z <= 200; z += 5)
            {
                for (int x = 0; x <= 200; x += 5)
                {
                    float h = TerrainMeshBuilder.SampleHeight(region.x + x, region.y + z);
                    if (h < minH) minH = h;
                    if (h > maxH) maxH = h;
                }
            }
            float centerH = TerrainMeshBuilder.SampleHeight(region.x + 100f, region.y + 100f);
            Debug.Log(string.Format("[Terrain] 区域高度 {0:F1} ~ {1:F1} m", minH, maxH));

            // 水面：在 0m 处放一张大平面（盖住海和刻出来的河/湖）
            var waterGo = new GameObject("WaterPlane");
            waterGo.transform.position = new Vector3(region.x + 100f, 0f, region.y + 100f);
            waterGo.transform.localScale = new Vector3(2000f, 1f, 2000f);
            var wmf = waterGo.AddComponent<MeshFilter>();
            wmf.sharedMesh = BuildQuadMesh();
            var wmr = waterGo.AddComponent<MeshRenderer>();
            var wmat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Rendering/Materials/Mat_SimpleWater.mat");
            if (wmat == null)
            {
                var ws = Shader.Find("Fengling/TerrainColor");
                if (ws != null)
                {
                    wmat = new Material(ws) { name = "Mat_SimpleWater" };
                    wmat.SetColor("_Tint", new Color(0.30f, 0.52f, 0.62f));
                    AssetDatabase.CreateAsset(wmat, "Assets/Game/Rendering/Materials/Mat_SimpleWater.mat");
                }
            }
            wmr.sharedMaterial = wmat;

            // 相机：俯视，接近参考视频的取景
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.62f, 0.72f, 0.78f);
            cam.fieldOfView = 50f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 1200f;
            camGo.transform.position = new Vector3(region.x + 100f, centerH + 85f, region.y - 30f);
            camGo.transform.rotation = Quaternion.Euler(50f, 0f, 0f);

            // 太阳
            var sunGo = new GameObject("Directional Light");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.intensity = 1.1f;
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
                ScenePath, chunks.Count, settings.GridSize, settings.GridSize * settings.CellSize));
        }
        static Mesh BuildQuadMesh()
        {
            var m = new Mesh();
            m.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f),
                new Vector3(-0.5f, 0f, 0.5f),  new Vector3(0.5f, 0f, 0.5f),
            };
            m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            m.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            m.RecalculateBounds();
            return m;
        }
    }
}
