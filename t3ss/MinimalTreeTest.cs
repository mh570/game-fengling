using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace T3ssDemo
{
    /// <summary>
    /// 最小化：严格按 DrawLeaf.cs 的逻辑——形状网格只当顶点源（不渲染），
    /// 叶子用工程的 LeafSize=1.449 让它互相重叠成实心树冠。
    /// </summary>
    public static class MinimalTreeTest
    {
        [MenuItem("T3SS/最小化树")]
        public static void Run()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Imported/Model/model.fbx");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);

            // ① 所有网格都不渲染（形状只当骨架）
                        foreach (var mr in inst.GetComponentsInChildren<MeshRenderer>())
            {
                // 只渲染树干；树叶形状网格只当顶点源，不渲染
                bool isTrunk = mr.name.Contains("树干");
                mr.enabled = isTrunk;
                if (isTrunk)
                {
                    var tm = new Material(Shader.Find("Unlit/Color"));   // 工程 树干.mat 也是同一个非光照 shader
                    tm.SetColor("_Color", new Color(0.29f, 0.21f, 0.25f));
                    mr.sharedMaterial = tm;
                }
            }

            // 找 树叶 形状网格
            Mesh shapeMesh = null;
            foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
                if (mf.name.Contains("树叶") && mf.sharedMesh != null) { shapeMesh = mf.sharedMesh; break; }
            Debug.Log("[Min] 形状顶点=" + (shapeMesh != null ? shapeMesh.vertexCount : 0));

            // ② DrawLeaf：工程原参数
            var leafMesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Imported/Model/Leaf.mesh");
            var leafTex  = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Imported/Tex/树叶.png");
            var lm = new Material(Shader.Find("Custom/LeafImported"));
            lm.enableInstancing = true;
            if (leafTex != null) lm.SetTexture("_MainTex", leafTex);
            lm.SetColor("_Color", new Color(0.494f, 0.937f, 0.384f));   // 工程 树叶.mat 原值
            lm.SetVector("_SunDir", new Vector4(0.35f, 0.85f, 0.40f, 0f));
            lm.SetFloat("_LightOffsetScale", 0.396f);
            lm.SetColor("_GradDark",  new Color(0.100f, 0.130f, 0.090f));
            lm.SetColor("_GradMid1",  new Color(0.180f, 0.220f, 0.150f));
            lm.SetColor("_GradMid2",  new Color(0.260f, 0.310f, 0.220f));
            lm.SetColor("_GradLight", new Color(0.337f, 0.393f, 0.290f));

            var dl = inst.AddComponent<DrawLeaf>();
            dl.ShapeMesh = shapeMesh;
            dl.LeafMesh = leafMesh;
            dl.mats = new List<Material> { lm, lm };
            dl.LeafDenesity = 0.443f;   // 工程值
            dl.LeafSize     = 1.449f;   // 工程值
            dl.LeafOffset   = 0.356f;   // 工程值
            dl.LightOffset  = 0.396f;   // 工程值
            dl.LifhtOffsetDenesity = 0.588f;   // 工程值

            // 地面（只需一个中性色，让画面能看）
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = new Vector3(10, 1, 10);
            ground.GetComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Custom/ToonLit"));

            // 光（叶片 shader 是 UNLIT，光照只影响地面）
            var lg = new GameObject("Sun").AddComponent<Light>();
            lg.type = LightType.Directional;
            lg.intensity = 1.2f;
            lg.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.62f, 0.55f);

            // 取景
            var b = shapeMesh.bounds;
            Vector3 wc = inst.transform.TransformPoint(b.center);
            float ext = Mathf.Max(b.extents.x, b.extents.y) * 1.5f;
            var camGo = new GameObject("Cam");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = ext;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.70f, 0.78f, 0.80f);
            cam.transform.position = wc + new Vector3(0, ext * 0.1f, -ext * 3.2f);
            cam.transform.LookAt(wc);
            cam.farClipPlane = 300f;

            EditorSceneManager.SaveScene(scene, "Assets/Scenes/Test/MinimalTree.unity");

            var rt = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            dl.InitLeaf();
            var mi = typeof(DrawLeaf).GetMethod("DrawLeafs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            for (int i = 0; i < 3; i++) { if (mi != null) mi.Invoke(dl, null); cam.Render(); }
            RenderTexture.active = rt;
            var shot = new Texture2D(512, 512, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
            shot.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), "Shots/minimal_tree.png"), shot.EncodeToPNG());
            Object.DestroyImmediate(shot);
            rt.Release();
            Debug.Log("[Min] 已保存 Shots/minimal_tree.png");
            EditorApplication.Exit(0);
        }
    }
}