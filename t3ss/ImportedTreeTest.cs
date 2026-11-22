using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace T3ssDemo
{
    /// <summary>直接使用从百度网盘工程导入的树（model.fbx + Leaf.mesh + DrawLeaf.cs）。</summary>
    public static class ImportedTreeTest
    {
        [MenuItem("T3SS/导入树测试")]
        public static void Run()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 1) 导入的树模型
            var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Imported/Model/model.fbx");
            if (model == null) { Debug.LogError("[Imp] model.fbx 加载失败"); EditorApplication.Exit(1); return; }

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.transform.position = Vector3.zero;
            inst.transform.rotation = Quaternion.identity;
            Debug.Log("[Imp] model.fbx 实例化: " + inst.name);

            // 列出子物体和网格
            foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
                Debug.Log("[Imp]   MeshFilter: " + mf.name + " verts=" + (mf.sharedMesh != null ? mf.sharedMesh.vertexCount : 0));

            // 2) 找 ShapeMesh（顶点多的那个）和树干
            Mesh shapeMesh = null;
            foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                if (mf.name.Contains("树叶") || mf.sharedMesh.name.Contains("树叶"))
                { shapeMesh = mf.sharedMesh; break; }
            }
            if (shapeMesh == null)
                foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
                    if (mf.sharedMesh != null && shapeMesh == null) shapeMesh = mf.sharedMesh;
            Debug.Log("[Imp] ShapeMesh 顶点数: " + (shapeMesh != null ? shapeMesh.vertexCount : 0));

            // 3) Leaf.mesh
            var leafMesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Imported/Model/Leaf.mesh");
            if (leafMesh == null)
            {
                // 可能是 fbx 里的子网格
                var guids = AssetDatabase.FindAssets("Leaf t:Mesh", new[] { "Assets/Imported" });
                if (guids.Length > 0) leafMesh = AssetDatabase.LoadAssetAtPath<Mesh>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }
            Debug.Log("[Imp] LeafMesh: " + (leafMesh != null ? leafMesh.name + " verts=" + leafMesh.vertexCount : "未找到"));

            // 4) 材质
            var leafMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Imported/Material/树叶.mat");
            var leafMat2 = AssetDatabase.LoadAssetAtPath<Material>("Assets/Imported/Material/树叶 2.mat");
            var shapeMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Imported/Material/树叶Shape.mat");
            var trunkMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Imported/Material/树干.mat");
            Debug.Log("[Imp] 材质: 树叶=" + (leafMat != null) + " 树叶2=" + (leafMat2 != null) +
                      " Shape=" + (shapeMat != null) + " 树干=" + (trunkMat != null));

            // 只保留"树叶"形状网格可见，隐藏地砖/地面/墙壁/树干/石头
            foreach (var mr in inst.GetComponentsInChildren<MeshRenderer>())
            {
                bool isLeafShape = mr.name.Contains("树叶");
                mr.enabled = isLeafShape;
            }

            // 5) 挂 DrawLeaf
            DrawLeaf dl = null;
            if (shapeMesh != null && leafMesh != null && leafMat != null)
            {
                dl = inst.AddComponent<DrawLeaf>();
                dl.ShapeMesh = shapeMesh;
                dl.LeafMesh = leafMesh;
                dl.mats = new System.Collections.Generic.List<Material>();
                if (leafMat != null) dl.mats.Add(leafMat);
                if (leafMat2 != null) dl.mats.Add(leafMat2);
                dl.LeafDenesity = 0.443f;
                dl.LeafSize = 1.449f;
                dl.LeafOffset = 0.356f;
                dl.LightOffset = 0.396f;
                Debug.Log("[Imp] DrawLeaf 已挂载，材质数=" + dl.mats.Count);
            }
            else Debug.LogError("[Imp] 缺 ShapeMesh/LeafMesh/材质");

            // 6) 地面 + 光 + 相机
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = new Vector3(3, 1, 3);
            var gm = new Material(Shader.Find("Custom/ToonLit"));
            gm.SetColor("_BaseColor", new Color(0.52f, 0.64f, 0.55f));
            ground.GetComponent<MeshRenderer>().sharedMaterial = gm;

            var lg = new GameObject("Sun").AddComponent<Light>();
            lg.type = LightType.Directional;
            lg.intensity = 1.4f;
            lg.color = new Color(1f, 0.96f, 0.86f);
            lg.shadows = LightShadows.Soft;
            lg.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.40f, 0.46f, 0.44f);

            // 自动取景到"树叶"的包围盒
            Bounds tb = new Bounds(Vector3.zero, Vector3.one);
            bool hasB = false;
            foreach (var mr in inst.GetComponentsInChildren<MeshRenderer>())
            {
                if (!mr.name.Contains("树叶") || mr.GetComponent<MeshFilter>() == null) continue;
                var m = mr.GetComponent<MeshFilter>().sharedMesh;
                if (m == null) continue;
                var wb = mr.transform.TransformPoint(m.bounds.center);
                tb = new Bounds(wb, m.bounds.size);
                hasB = true;
                Debug.Log("[Imp] 树叶包围盒: center=" + wb + " size=" + m.bounds.size);
                break;
            }

            var camGo = new GameObject("Cam");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            float frameSize = hasB ? Mathf.Max(tb.extents.x, tb.extents.y) * 1.25f : 4.5f;
            cam.orthographicSize = frameSize;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.70f, 0.78f, 0.80f);
            Vector3 lookAt = hasB ? tb.center : new Vector3(0f, 2.5f, 0f);
            cam.transform.position = lookAt + new Vector3(0f, tb.extents.y * 0.3f, -frameSize * 3.2f);
            cam.transform.LookAt(lookAt);
            cam.farClipPlane = 100f;

            EditorSceneManager.MarkSceneDirty(scene);
            Directory.CreateDirectory("Assets/Scenes/Test");
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/Test/ImportedTree.unity");

            // 7) 渲染（DrawLeaf 用 DrawMeshInstanced，只在 Update 跑 → 手动调一次）
            var rt = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            for (int i = 0; i < 3; i++)
            {
                if (dl != null) dl.InitLeaf();
                // 反射调 DrawLeafs
                var mi = typeof(DrawLeaf).GetMethod("DrawLeafs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (mi != null && dl != null) mi.Invoke(dl, null);
                cam.Render();
            }
            RenderTexture.active = rt;
            var shot = new Texture2D(512, 512, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
            shot.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;

            Directory.CreateDirectory("Shots");
            File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), "Shots/imported_tree.png"), shot.EncodeToPNG());
            Object.DestroyImmediate(shot);
            rt.Release();
            Debug.Log("[Imp] 已保存 Shots/imported_tree.png");
            EditorApplication.Exit(0);
        }
    }
}