using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace T3ssDemo
{
    /// <summary>用 TreeGenerator 生成树并渲染测试。</summary>
    public static class TreeSceneTest
    {
        [MenuItem("T3SS/生成树场景")]
        public static void Run()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---- 叶片贴图（代码生成）
            var leafTex = TreeGenerator.MakeLeafTexture(32, 7);
            Directory.CreateDirectory("Assets/Textures");
            File.WriteAllBytes("Assets/Textures/LeafCluster.png", leafTex.EncodeToPNG());
            AssetDatabase.ImportAsset("Assets/Textures/LeafCluster.png", ImportAssetOptions.ForceSynchronousImport);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/LeafCluster.png");
            if (tex != null)
            {
                var imp = (TextureImporter)AssetImporter.GetAtPath("Assets/Textures/LeafCluster.png");
                if (imp != null)
                {
                    imp.textureType = TextureImporterType.Default;
                    imp.alphaIsTransparency = true;
                    imp.filterMode = FilterMode.Point;
                    imp.mipmapEnabled = false;
                    imp.SaveAndReimport();
                }
                tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/LeafCluster.png");
            }

            // ---- foliage 材质
            var shader = Shader.Find("Custom/ToonFoliage");
            if (shader == null) { Debug.LogError("[Tree] 找不到 Custom/ToonFoliage"); EditorApplication.Exit(1); return; }
            var mat = new Material(shader) { name = "MAT_Foliage" };
            if (tex != null) mat.SetTexture("_MainTex", tex);
            mat.SetFloat("_Cutoff", 0.4f);
            mat.SetFloat("_Cuts", 3f);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetColor("_HighlightColor", new Color(0.66f, 0.84f, 0.44f));
            mat.SetColor("_ShadowColor", new Color(0.14f, 0.26f, 0.14f));

            // ---- 地面
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(8, 1, 8);
            var toon = Shader.Find("Custom/ToonLit");
            if (toon != null)
            {
                var gm = new Material(toon) { name = "MAT_Ground" };
                gm.SetColor("_BaseColor", new Color(0.45f, 0.62f, 0.30f));
                gm.SetColor("_HighlightColor", new Color(0.58f, 0.76f, 0.38f));
                gm.SetColor("_ShadowColor", new Color(0.26f, 0.40f, 0.22f));
                ground.GetComponent<MeshRenderer>().sharedMaterial = gm;
            }

            // ---- 生成 5 棵树
            var rng = new System.Random(4242);
            for (int i = 0; i < 5; i++)
            {
                var opt = new TreeGenerator.Options
                {
                    Height = 4.0f + (float)rng.NextDouble() * 1.5f,
                    LeafCount = 900,
                    LeafSize = 0.50f,
                    Seed = 100 + i,
                };
                var mesh = TreeGenerator.Build(opt, out int shellV, out int leafV);
                string path = "Assets/Generated/Tree_" + i + ".asset";
                Directory.CreateDirectory("Assets/Generated");
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(mesh, path);
                var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);

                var go = new GameObject("Tree_" + i);
                float ang = i / 5f * Mathf.PI * 2f;
                float rad = 5.5f + (float)rng.NextDouble() * 3f;
                go.transform.position = new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad);
                go.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                go.AddComponent<MeshFilter>().sharedMesh = saved;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

                Debug.Log("[Tree] Tree_" + i + " 壳顶点=" + shellV + " 叶片顶点=" + leafV);
            }

            // ---- 灯光 + 环境
            var lightGo = new GameObject("Sun");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.5f;
            light.color = new Color(1f, 0.96f, 0.88f);
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.65f;
            lightGo.transform.rotation = Quaternion.Euler(52f, -35f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.50f, 0.48f);

            // ---- 相机（正交等距）
            var pivot = new GameObject("CameraPivot");
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(pivot.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 9f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.62f, 0.72f, 0.78f);
            cam.farClipPlane = 200f;
            pivot.transform.position = Vector3.zero;
            pivot.transform.rotation = Quaternion.Euler(28f, 45f, 0f);
            camGo.transform.localPosition = new Vector3(0f, 0f, -30f);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/Test/TreeGen.unity");

            // ---- 渲染
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            cam.Render();
            RenderTexture.active = rt;
            var shot = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            shot.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;

            Directory.CreateDirectory("Shots");
            File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), "Shots/tree_gen.png"), shot.EncodeToPNG());
            Object.DestroyImmediate(shot);
            rt.Release();

            Debug.Log("[Tree] 已保存 Shots/tree_gen.png");
            EditorApplication.Exit(0);
        }
    }
}
