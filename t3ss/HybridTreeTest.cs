using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace T3ssDemo
{
    /// <summary>测试：他们的 DrawLeaf 技术 + 我们的 3 球形状。</summary>
    public static class HybridTreeTest
    {
        [MenuItem("T3SS/混合树测试")]
        public static void Run()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 1) 形状网格（我们的 3 个错位锯齿球）
            var opt = new TreeGenerator.Options { Height = 5f, LeafCount = 1, LeafSize = 0.3f, Seed = 2026 };
            var shapeMesh = TreeGenerator.BuildShapeOnly(opt);
            Directory.CreateDirectory("Assets/Generated");
            AssetDatabase.DeleteAsset("Assets/Generated/HybridShape.asset");
            AssetDatabase.CreateAsset(shapeMesh, "Assets/Generated/HybridShape.asset");
            var shape = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Generated/HybridShape.asset");
            Debug.Log("[Hyb] 形状顶点=" + shape.vertexCount);

            var shapeGo = new GameObject("Shape");
            shapeGo.AddComponent<MeshFilter>().sharedMesh = shape;
            var smr = shapeGo.AddComponent<MeshRenderer>();
            smr.enabled = false;   // 形状只当发射器，不渲染（避免坏材质）

            // 2) 叶片网格 + 材质（他们的 Leaf.mesh + 我们的 HLSL shader）
            var leafMesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Imported/Model/Leaf.mesh");
            if (leafMesh == null) { Debug.LogError("[Hyb] Leaf.mesh 缺"); EditorApplication.Exit(1); return; }
            var leafShader = Shader.Find("Custom/LeafImported");
            var mat = new Material(leafShader) { name = "MAT_HybridLeaf" };
            mat.enableInstancing = true;
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Imported/Tex/树叶.png");
            if (tex != null) mat.SetTexture("_MainTex", tex);

            // 3) 挂 DrawLeaf
            var dl = shapeGo.AddComponent<DrawLeaf>();
            dl.ShapeMesh = shape;
            dl.LeafMesh = leafMesh;
            dl.mats = new System.Collections.Generic.List<Material> { mat, mat };
            dl.LeafDenesity = 0.60f;
            dl.LeafSize = 1.5f;
            dl.LeafOffset = 0.15f;
            dl.LightOffset = 0.396f;

            // 4) 地面/光/相机
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = new Vector3(3, 1, 3);
            var gm = new Material(Shader.Find("Custom/ToonLit"));
            gm.SetColor("_BaseColor", new Color(0.52f, 0.64f, 0.55f));
            gm.SetColor("_Color2", new Color(0.48f, 0.60f, 0.52f));
            gm.SetColor("_Color3", new Color(0.44f, 0.56f, 0.48f));
            ground.GetComponent<MeshRenderer>().sharedMaterial = gm;

            var lg = new GameObject("Sun").AddComponent<Light>();
            lg.type = LightType.Directional;
            lg.intensity = 1.4f;
            lg.color = new Color(1f, 0.96f, 0.86f);
            lg.shadows = LightShadows.Soft;
            lg.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.40f, 0.46f, 0.44f);

            var camGo = new GameObject("Cam");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.orthographicSize = 4.2f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.70f, 0.78f, 0.80f);
            cam.transform.position = new Vector3(0f, 3.2f, -14f);
            cam.transform.LookAt(new Vector3(0f, 3.4f, 0f));
            cam.farClipPlane = 100f;

            Directory.CreateDirectory("Assets/Scenes/Test");
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/Test/HybridTree.unity");

            // 5) 渲染
            var rt = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            var mi = typeof(DrawLeaf).GetMethod("DrawLeafs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            for (int i = 0; i < 3; i++)
            {
                dl.InitLeaf();
                if (mi != null) mi.Invoke(dl, null);
                cam.Render();
            }
            RenderTexture.active = rt;
            var shot = new Texture2D(512, 512, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
            shot.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), "Shots/hybrid_tree.png"), shot.EncodeToPNG());
            Object.DestroyImmediate(shot);
            rt.Release();
            Debug.Log("[Hyb] 已保存 Shots/hybrid_tree.png");
            EditorApplication.Exit(0);
        }
    }
}