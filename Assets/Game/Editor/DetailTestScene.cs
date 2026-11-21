using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Fengling.TerrainSystem;

namespace Fengling.EditorTools
{
    /// <summary>
    /// 30×30 米技术测试场景。
    ///
    /// 目的是验证视频级别的"密度"和"风格"，不是做大世界：
    ///   1. 平地（30×30m，几乎无高差）
    ///   2. 一个水塘 + 岸边白线
    ///   3. 草、花铺满（billboard 面片）
    ///   4. 像素化渲染（PixelRenderScale 0.5）
    ///   5. 相机取景对齐视频（视野高度 ~14m，俯角 50°）
    /// </summary>
    public static class DetailTestScene
    {
        const string ScenePath = "Assets/Scenes/Detail30.unity";
        const int Cells = 30;          // 30x30 米
        const float CellSize = 1f;
        const int Chunks = 1;          // 一块就够

        [MenuItem("Fengling/Terrain/30x30 技术测试场景")]
        public static void Run()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---- 地面：30x30 平地 + 水塘
            TerrainShape.Enabled = false;   // 不用半岛造型
            TerrainCarver.IslandMode = false;

            // 水塘：中心偏左，半径 6m
            var center = new Vector2(0f, 0f);
            float size = Cells * CellSize;
            TerrainCarver.IslandCenter = center;
            TerrainCarver.Lakes = new[]
            {
                new TerrainCarver.Lake
                {
                    center = new Vector2(center.x - 4f, center.y + 2f),
                    radius = 6f,
                    depth = 1.6f,
                    falloff = 3.5f,
                },
            };
            TerrainCarver.Rivers = System.Array.Empty<TerrainCarver.River>();

            var chunks = BuildFlatGround(size);
            if (chunks == null) { Debug.LogError("[Detail] 地面生成失败"); return; }

            var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Rendering/Materials/Mat_TerrainColor.mat");
            if (mat == null)
            {
                var shader = Shader.Find("Fengling/TerrainColor");
                if (shader == null) { Debug.LogError("[Detail] 缺 shader"); return; }
                mat = new Material(shader) { name = "Mat_TerrainColor" };
                AssetDatabase.CreateAsset(mat, "Assets/Game/Rendering/Materials/Mat_TerrainColor.mat");
            }

            // 网格存成资产
            if (!AssetDatabase.IsValidFolder("Assets/Game/Terrain/Generated"))
                AssetDatabase.CreateFolder("Assets/Game/Terrain", "Generated");
            const string meshPath = "Assets/Game/Terrain/Generated/Detail30.asset";
            AssetDatabase.DeleteAsset(meshPath);
            AssetDatabase.CreateAsset(chunks, meshPath);
            var savedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);

            var ground = new GameObject("Ground30");
            ground.transform.position = Vector3.zero;
            ground.AddComponent<MeshFilter>().sharedMesh = savedMesh;
            var mr = ground.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.receiveShadows = true;
            ground.AddComponent<MeshCollider>().sharedMesh = savedMesh;

            // ---- 水面（y = 0）
            var water = GameObject.CreatePrimitive(PrimitiveType.Plane);
            water.name = "Water";
            water.transform.position = new Vector3(-4f, 0.02f, 2f);
            water.transform.localScale = new Vector3(1.7f, 1f, 1.7f);   // 约 17m，略大于池塘
            Object.DestroyImmediate(water.GetComponent<Collider>());
            var wmat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Rendering/Materials/Mat_Water.mat");
            if (wmat == null)
            {
                var ws = Shader.Find("Fengling/Water");
                if (ws != null)
                {
                    wmat = new Material(ws) { name = "Mat_Water" };
                    wmat.SetColor("_ShallowColor", new Color(0.58f, 0.82f, 0.80f));
                    wmat.SetColor("_DeepColor", new Color(0.12f, 0.34f, 0.42f));
                    AssetDatabase.CreateAsset(wmat, "Assets/Game/Rendering/Materials/Mat_Water.mat");
                }
            }
            water.GetComponent<MeshRenderer>().sharedMaterial = wmat;

            // ---- 花草：撒点（技术测试重点）
            ScatterVegetation(center, size);

            // GPU Instancing 草场（上万株）
            var grass = new GameObject("GrassField");
            var gf = grass.AddComponent<TerrainSystem.GrassField>();
            gf.Center = center;
            gf.Size = size;
            gf.BaseHeight = 1.2f;
            gf.Count = 12000;

            // ---- 灯光
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.intensity = 1.25f;
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.Euler(52f, -30f, 0f);
            RenderSettings.sun = sun;

            var tod = new GameObject("TimeOfDay").AddComponent<Fengling.Time.TimeOfDaySystem>();
            tod.sun = sun;
            tod.timeOfDay = 10f;

            // ---- 相机（视频取景：视野高度 14m，俯角 50°）
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.62f, 0.72f, 0.78f);
            cam.fieldOfView = 45f;
            cam.farClipPlane = 300f;
            camGo.transform.position = new Vector3(0f, 14f, -9f);
            camGo.transform.rotation = Quaternion.Euler(50f, 0f, 0f);

            new GameObject("EnvironmentBootstrap").AddComponent<Fengling.Time.EnvironmentBootstrap>();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
            Debug.Log("[Detail] 已创建 " + ScenePath);
        }

        static Mesh BuildFlatGround(float size)
        {
            int n = Cells + 1;
            int quads = Cells * Cells;
            var verts = new Vector3[quads * 6];
            var normals = new Vector3[quads * 6];
            var colors = new Color[quads * 6];
            var tris = new int[quads * 6];

            var pts = new Vector3[n * n];
            for (int j = 0; j < n; j++)
            {
                for (int i = 0; i < n; i++)
                {
                    float wx = -size * 0.5f + i * CellSize;
                    float wz = -size * 0.5f + j * CellSize;
                    float h = TerrainCarver.Apply(wx, wz, 1.2f);   // 基准 1.2m，水塘处下挖
                    pts[j * n + i] = new Vector3(wx, h, wz);
                }
            }

            int vi = 0;
            for (int j = 0; j < Cells; j++)
            {
                for (int i = 0; i < Cells; i++)
                {
                    var p00 = pts[j * n + i];
                    var p10 = pts[j * n + i + 1];
                    var p01 = pts[(j + 1) * n + i];
                    var p11 = pts[(j + 1) * n + i + 1];
                    EmitTri(verts, normals, colors, tris, ref vi, p00, p11, p10);
                    EmitTri(verts, normals, colors, tris, ref vi, p00, p01, p11);
                }
            }

            var mesh = new Mesh();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = verts;
            mesh.normals = normals;
            mesh.colors = colors;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }

        static void EmitTri(Vector3[] vs, Vector3[] ns, Color[] cs, int[] ts, ref int vi,
                            Vector3 a, Vector3 b, Vector3 c)
        {
            var nrm = Vector3.Cross(b - a, c - a).normalized;
            if (nrm.y < 0f) nrm = -nrm;
            float slope = 1f - Mathf.Abs(nrm.y);
            for (int k = 0; k < 3; k++)
            {
                var p = k == 0 ? a : (k == 1 ? b : c);
                vs[vi] = p;
                ns[vi] = nrm;
                cs[vi] = TerrainColors.Compute(p.x, p.z, p.y, slope, 0f, 2f);
                ts[vi] = vi;
                vi++;
            }
        }

        /// <summary>撒草和花（billboard 小面片 + 简单颜色），验证密度</summary>
        static void ScatterVegetation(Vector2 center, float size)
        {
            var root = new GameObject("Vegetation");
            var rng = new System.Random(20261121);
            int placed = 0;

            // 花：成簇
            for (int cluster = 0; cluster < 22; cluster++)
            {
                float cx = center.x + (float)(rng.NextDouble() - 0.5) * size * 0.85f;
                float cz = center.y + (float)(rng.NextDouble() - 0.5) * size * 0.85f;
                float h = TerrainCarver.Apply(cx, cz, 1.2f);
                if (h < 0.35f) continue;   // 水里不放

                int perCluster = 12 + rng.Next(14);
                for (int k = 0; k < perCluster; k++)
                {
                    float px = cx + (float)(rng.NextDouble() - 0.5) * 1.6f;
                    float pz = cz + (float)(rng.NextDouble() - 0.5) * 1.6f;
                    var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    go.name = "Flower";
                    Object.DestroyImmediate(go.GetComponent<Collider>());
                    go.transform.SetParent(root.transform, false);
                    go.transform.position = new Vector3(px, TerrainCarver.Apply(px, pz, 1.2f) + 0.16f, pz);
                    go.transform.rotation = Quaternion.Euler(70f, (float)rng.NextDouble() * 360f, 0f);
                    go.transform.localScale = Vector3.one * 0.55f;

                    var col = cluster % 3 == 0
                        ? new Color(0.95f, 0.90f, 0.55f)
                        : (cluster % 3 == 1 ? new Color(0.72f, 0.55f, 0.90f) : new Color(0.98f, 0.98f, 0.92f));
                    ApplyFlatColor(go, col);
                    placed++;
                }
            }

            // 草：稀疏小片（先用面片占位，验证密度观感）
            for (int i = 0; i < 260; i++)
            {
                float px = center.x + (float)(rng.NextDouble() - 0.5) * size * 0.9f;
                float pz = center.y + (float)(rng.NextDouble() - 0.5) * size * 0.9f;
                float h = TerrainCarver.Apply(px, pz, 1.2f);
                if (h < 0.3f) continue;

                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = "Grass";
                Object.DestroyImmediate(go.GetComponent<Collider>());
                go.transform.SetParent(root.transform, false);
                go.transform.position = new Vector3(px, h + 0.2f, pz);
                go.transform.rotation = Quaternion.Euler(75f, (float)rng.NextDouble() * 360f, 0f);
                go.transform.localScale = new Vector3(0.35f, 0.55f, 1f);
                ApplyFlatColor(go, new Color(0.36f, 0.55f, 0.24f));
                placed++;
            }

            Debug.Log("[Detail] 撒了 " + placed + " 个植被面片");
        }

        static Material _flatMat;
        static void ApplyFlatColor(GameObject go, Color c)
        {
            if (_flatMat == null)
            {
                var shader = Shader.Find("Fengling/TerrainColor");
                _flatMat = new Material(shader) { name = "Mat_Veg" };
                _flatMat.SetColor("_Tint", Color.white);
            }
            var mat = new Material(_flatMat);
            mat.SetColor("_Tint", c);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
