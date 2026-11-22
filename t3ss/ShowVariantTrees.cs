using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace T3ssDemo
{
    /// <summary>把 8 个形状变体渲染成完整的树（带叶片）。</summary>
    public static class ShowVariantTrees
    {
        [MenuItem("T3SS/看变体树")]
        public static void Run()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var tmp = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Imported/Model/model.fbx");
            Mesh baseMesh = null;
            if (tmp != null)
                foreach (var mf in tmp.GetComponentsInChildren<MeshFilter>())
                    if (mf.sharedMesh != null && mf.sharedMesh.name.Contains("树叶")) { baseMesh = mf.sharedMesh; break; }
            if (baseMesh == null) { Debug.LogError("[VT] 找不到模板网格"); EditorApplication.Exit(1); return; }

            var leafShader = Shader.Find("Custom/LeafImported");
            var leafMesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Imported/Model/Leaf.mesh");
            var leafTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Imported/Tex/树叶.png");
            var flatShader = Shader.Find("Custom/FlatColor");
            Debug.Log("[VT] shader=" + (leafShader != null) + " leaf=" + (leafMesh != null) + " tex=" + (leafTex != null) + " flat=" + (flatShader != null));

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = new Vector3(40, 1, 40);
            var gmat = new Material(Shader.Find("Custom/ToonLit"));
            gmat.SetColor("_BaseColor", new Color(0.42f, 0.55f, 0.32f));
            gmat.SetColor("_Color2", new Color(0.40f, 0.52f, 0.30f));
            gmat.SetColor("_Color3", new Color(0.36f, 0.48f, 0.27f));
            ground.GetComponent<MeshRenderer>().sharedMaterial = gmat;

            var lg = new GameObject("Sun").AddComponent<Light>();
            lg.type = LightType.Directional;
            lg.intensity = 1.3f;
            lg.color = new Color(1f, 0.96f, 0.86f);
            lg.shadows = LightShadows.Soft;
            lg.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.62f, 0.55f);

            var camGo = new GameObject("Cam");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.70f, 0.78f, 0.80f);
            cam.farClipPlane = 300f;
            var rt = new RenderTexture(256, 320, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            Directory.CreateDirectory("Shots/VariantTrees");

            var mi = typeof(DrawLeaf).GetMethod("DrawLeafs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var cfgs = new (int seed, float smin, float smax, float jit, float bend)[]
            {
                (1, 0.85f, 1.15f, 0.02f, 0.00f),
                (2, 0.80f, 1.20f, 0.04f, 0.05f),
                (3, 0.90f, 1.10f, 0.03f, -0.06f),
                (4, 0.75f, 1.25f, 0.05f, 0.08f),
                (5, 0.88f, 1.12f, 0.01f, 0.00f),
                (6, 0.82f, 1.18f, 0.045f, -0.04f),
                (7, 0.92f, 1.08f, 0.025f, 0.03f),
                (8, 0.78f, 1.22f, 0.035f, -0.07f),
            };

            for (int i = 0; i < cfgs.Length; i++)
            {
                var c = cfgs[i];
                var mesh = ShapeVariants.MakeVariant(baseMesh, c.seed, c.smin, c.smax, c.jit, c.bend);
                Directory.CreateDirectory("Assets/Generated");
                string path = "Assets/Generated/VT_" + i + ".asset";
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(mesh, path);
                var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);

                var go = new GameObject("VT" + i);
                go.AddComponent<MeshFilter>().sharedMesh = saved;
                var smr = go.AddComponent<MeshRenderer>();
                var smat = new Material(flatShader);
                smat.SetColor("_Color", new Color(0.250f, 0.320f, 0.195f));
                smr.sharedMaterial = smat;

                var dl = go.AddComponent<DrawLeaf>();
                dl.ShapeMesh = saved;
                dl.LeafMesh = leafMesh;
                var lm = new Material(leafShader);
                lm.enableInstancing = true;
                if (leafTex != null) lm.SetTexture("_MainTex", leafTex);
                lm.SetColor("_Color", Color.white);
                lm.SetVector("_SunDir", new Vector4(0.35f, 0.85f, 0.40f, 0f));
                lm.SetFloat("_LightOffsetScale", 0.396f);
                lm.SetColor("_GradDark",  new Color(0.500f, 0.576f, 0.449f));
                lm.SetColor("_GradMid1",  new Color(0.644f, 0.741f, 0.561f));
                lm.SetColor("_GradMid2",  new Color(0.839f, 0.966f, 0.731f));
                lm.SetColor("_GradLight", new Color(1.000f, 1.100f, 0.900f));
                dl.mats = new List<Material> { lm, lm };
                dl.LeafDenesity = 0.443f;
                dl.LeafSize = 1.449f;
                dl.LeafOffset = 0.356f;
                dl.LightOffset = 0.396f;
                dl.LifhtOffsetDenesity = 0.588f;

                var b = saved.bounds;
                float ext = Mathf.Max(b.extents.x, b.extents.y) * 1.6f;
                cam.orthographicSize = ext;
                cam.transform.position = b.center + new Vector3(0, ext * 0.15f, -ext * 3.2f);
                cam.transform.LookAt(b.center);

                dl.InitLeaf();
                for (int f = 0; f < 3; f++) { if (mi != null) mi.Invoke(dl, null); cam.Render(); }
                RenderTexture.active = rt;
                var shot = new Texture2D(256, 320, TextureFormat.RGB24, false);
                shot.ReadPixels(new Rect(0, 0, 256, 320), 0, 0);
                shot.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), "Shots/VariantTrees/v" + i + ".png"), shot.EncodeToPNG());
                Object.DestroyImmediate(shot);
                Object.DestroyImmediate(go);
                Debug.Log("[VT] 树 " + i + " bbox=" + b.size);
            }

            cam.targetTexture = null;
            rt.Release();
            EditorApplication.Exit(0);
        }
    }
}
