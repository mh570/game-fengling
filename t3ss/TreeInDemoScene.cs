using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace T3ssDemo
{
    /// <summary>
    /// 把代码生成的树放进仓库自带的 ShaderTest 场景（管线已配好），
    /// 用场景原有的相机渲染，这样描边和像素化都会生效。
    /// </summary>
    public static class TreeInDemoScene
    {
        [MenuItem("T3SS/树放进Demo场景")]
        public static void Run()
        {
            // 打开仓库自带场景（管线已配）
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Test/ShaderTest.unity", OpenSceneMode.Single);

            // 找相机
            Camera cam = null;
            foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (c.cameraType == CameraType.Game && c.targetTexture == null) { cam = c; break; }
            }
            if (cam == null) cam = Object.FindFirstObjectByType<Camera>();
            if (cam == null) { Debug.LogError("[Demo] 没相机"); EditorApplication.Exit(1); return; }
            Debug.Log("[Demo] 相机=" + cam.name + " 正交=" + cam.orthographic + " size=" + cam.orthographicSize);

            // ---- 叶片贴图
            Directory.CreateDirectory("Assets/Textures");
            var leafTex = TreeGenerator.MakeLeafTexture(32, 7);
            File.WriteAllBytes("Assets/Textures/LeafCluster.png", leafTex.EncodeToPNG());
            AssetDatabase.ImportAsset("Assets/Textures/LeafCluster.png", ImportAssetOptions.ForceSynchronousImport);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/LeafCluster.png");

            // ---- foliage 材质
            var shader = Shader.Find("Custom/ToonFoliage");
            if (shader == null) { Debug.LogError("[Demo] 缺 Custom/ToonFoliage"); EditorApplication.Exit(1); return; }
            var mat = new Material(shader) { name = "MAT_Foliage_Demo" };
            if (tex != null) mat.SetTexture("_MainTex", tex);
            mat.SetFloat("_Cutoff", 0.4f);
            mat.SetFloat("_Cuts", 3f);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetColor("_HighlightColor", new Color(0.70f, 0.86f, 0.46f));
            mat.SetColor("_ShadowColor", new Color(0.16f, 0.28f, 0.16f));

            // ---- 在相机前方摆一圈树
            var root = new GameObject("TestTrees");
            float camDist = 14f;
            var rng = new System.Random(999);
            for (int i = 0; i < 30; i++)
            {
                var opt = new TreeGenerator.Options
                {
                    Height = 3.6f + (float)rng.NextDouble() * 1.2f,
                    LeafCount = 1400,
                    LeafSize = 0.48f,
                    Seed = 300 + i,
                };
                var mesh = TreeGenerator.Build(opt, out int sv, out int lv);

                Directory.CreateDirectory("Assets/Generated");
                string path = "Assets/Generated/DemoTree_" + i + ".asset";
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(mesh, path);
                var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);

                var go = new GameObject("DemoTree_" + i);
                go.transform.SetParent(root.transform, false);

                // 沿相机前方摆成弧形
                // 围着原点摆（demo 物体也在原点附近）
                // 丛状分布：3 个圆环
                int ring = i / 10;
                float ang = (i % 10) / 10f * Mathf.PI * 2f + ring * 0.5f;
                float rad = 6f + ring * 4.5f;
                go.transform.position = new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad);
                go.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);

                go.AddComponent<MeshFilter>().sharedMesh = saved;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }

            // ---- 暖光 + 亮环境
            foreach (var lg in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (lg.type == LightType.Directional)
                {
                    lg.color = new Color(1.0f, 0.95f, 0.82f);
                    lg.intensity = 1.6f;
                    lg.shadowStrength = 0.55f;
                }
            }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.52f, 0.58f, 0.52f);

            // ---- 草（手动 DrawMeshInstanced）
            var spawner = Object.FindFirstObjectByType<GrassSpawner>();
            if (spawner != null && spawner.grassMesh != null && spawner.grassMaterial != null)
            {
                var gb = new Bounds(Vector3.zero, new Vector3(16f, 4f, 16f));
                int gcount = 35000;
                var gmats = new Matrix4x4[gcount];
                var grng = new System.Random(777);
                int gplaced = 0;
                for (int gi = 0; gi < gcount * 3 && gplaced < gcount; gi++)
                {
                    float gx = (float)(grng.NextDouble() - 0.5) * gb.size.x;
                    float gz = (float)(grng.NextDouble() - 0.5) * gb.size.z;
                    if (!Physics.Raycast(new Vector3(gx, 50f, gz), Vector3.down, out var gh, 200f)) continue;
                    if (gh.normal.y < 0.9f || Mathf.Abs(gh.point.y) > 0.15f) continue;
                    float hh = Mathf.Lerp(spawner.minHeight, spawner.maxHeight, (float)grng.NextDouble());
                    gmats[gplaced] = Matrix4x4.TRS(new Vector3(gx, gh.point.y, gz),
                        Quaternion.Euler(0f, (float)grng.NextDouble() * 360f, 0f),
                        new Vector3(hh * 0.75f, hh, hh * 0.75f));
                    gplaced++;
                }
                var mpb = new MaterialPropertyBlock();
                for (int gi = 0; gi < gplaced; gi += 1023)
                {
                    int n = Mathf.Min(1023, gplaced - gi);
                    var slice = new Matrix4x4[n];
                    System.Array.Copy(gmats, gi, slice, 0, n);
                    Graphics.DrawMeshInstanced(spawner.grassMesh, 0, spawner.grassMaterial, slice, n, mpb,
                        UnityEngine.Rendering.ShadowCastingMode.Off, false);
                }
                Debug.Log("[Demo] 草 " + gplaced + " 株");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/Test/ShaderTest_Trees.unity");

            // ---- 渲染（场景自己的相机 + 已配好的 URP 管线）
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            var oldT = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.Render();
            RenderTexture.active = rt;
            var shot = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            shot.Apply();
            RenderTexture.active = null;
            cam.targetTexture = oldT;

            Directory.CreateDirectory("Shots");
            File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), "Shots/tree_in_demo.png"), shot.EncodeToPNG());
            Object.DestroyImmediate(shot);
            rt.Release();

            Debug.Log("[Demo] 已保存 Shots/tree_in_demo.png");
            EditorApplication.Exit(0);
        }
    }
}
