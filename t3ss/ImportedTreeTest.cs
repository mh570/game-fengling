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

            // 2) ShapeMesh：用我们生成的凹凸球（替换工程自带的蛋形）
            Mesh shapeMesh = null;
            {
                // ★ 只替换形状网格，其余流程完全不变
                // ★ 扁椭圆 + 3 个 1/3 半径的突出球
                var gen = LobeShapeTest.BuildLobedShape(
                    seed: 22,
                    bigLobes: 3, bigH: 0.33f, bigW: 0.45f,   // 突出 1/3，窄一点才鼓
                    smallLobes: 0, smallH: 0.0f, smallW: 0.30f,
                    baseR: 1.0f, lon: 48, lat: 28,
                    ellipsoidScale: new Vector3(1.15f, 0.82f, 1.15f));   // 扁椭圆（宽>高）
                // 缩放到和工程形状网格相同尺寸
                Mesh orig = null;
                foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
                    if (mf.sharedMesh != null && (mf.name.Contains("树叶") || mf.sharedMesh.name.Contains("树叶")))
                    { orig = mf.sharedMesh; break; }
                if (orig != null)
                {
                    float os = Mathf.Max(orig.bounds.size.x, orig.bounds.size.y, orig.bounds.size.z);
                    float gs = Mathf.Max(gen.bounds.size.x, gen.bounds.size.y, gen.bounds.size.z);
                    float k = os / Mathf.Max(0.0001f, gs);
                    var vs = gen.vertices;
                    for (int i = 0; i < vs.Length; i++) vs[i] *= k;
                    gen.vertices = vs;
                    gen.RecalculateNormals();
                    gen.RecalculateBounds();
                    // 中心对齐到原形状
                    var shift = orig.bounds.center - gen.bounds.center;
                    for (int i = 0; i < vs.Length; i++) vs[i] += shift;
                    gen.vertices = vs;
                    gen.RecalculateBounds();
                    Debug.Log("[Imp] 形状替换：原 " + orig.vertexCount + " 顶点 → 生成 " + gen.vertexCount +
                              " 顶点，缩放 " + k.ToString("F3"));
                }
                shapeMesh = gen;
                // 把生成的形状塞进工程原本的「树叶」MeshFilter（渲染位置/变换都沿用）
                if (orig != null)
                    foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
                        if (mf.sharedMesh == orig) { mf.sharedMesh = gen; break; }
            }

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

            // ★ 去虚线：底衬改用纯色 Unlit（对应工程的 Unlit.shadergraph）
            //    ToonLit 会让 3 个球明暗不同 → 交界出现虚线
            var flatShader = Shader.Find("Custom/FlatColor");
            if (flatShader == null) Debug.LogError("[Imp] ★ 找不到 Custom/FlatColor");
            var shapeMatForShape = new Material(flatShader);
            shapeMatForShape.SetColor("_Color", new Color(0.250f, 0.320f, 0.195f));   // 和叶片中调接近 → 空隙不显黑

            // 只保留"树叶"形状网格 + 树干
            foreach (var mr in inst.GetComponentsInChildren<MeshRenderer>())
            {
                // 只渲染"树叶"形状网格 + "树干"，其余（地砖/地面/墙壁/石头）不显示
                bool keep = mr.name.Contains("树叶") || mr.name.Contains("树干");
                mr.enabled = keep;
                if (mr.name.Contains("树叶") && shapeMatForShape != null)
                    mr.sharedMaterial = shapeMatForShape;
                if (mr.name.Contains("树干"))
                {
                    // ★ 无条件换纯色（工程 树干.mat 也是 Unlit.shadergraph）
                    //   旧逻辑「仅当 shader 不支持才换」放过了 URP/Lit → 树干一直是灰矩形
                    var tm = new Material(Shader.Find("Custom/FlatColor"));
                    tm.SetColor("_Color", new Color(0.29f, 0.21f, 0.25f));   // → sRGB #4A3540
                    mr.sharedMaterial = tm;
                    Debug.Log("[Imp] 树干 → FlatColor");
                }
            }


            // 他们的 shadergraph 在 Unity6 编译失败 → 用我们的 HLSL 版本，贴图沿用他们的
            var hlslLeaf = Shader.Find("Custom/LeafImported");
            if (hlslLeaf != null)
            {
                var newLeafMat = new Material(hlslLeaf) { name = "MAT_LeafHLSL" };
                newLeafMat.enableInstancing = true;
                var texLeaf = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Imported/Tex/树叶.png");
                var texLeaf2 = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Imported/Tex/树叶2.png");
                Debug.Log("[Imp] 树叶.png=" + (texLeaf != null) + " 树叶2.png=" + (texLeaf2 != null));
                if (texLeaf != null) newLeafMat.SetTexture("_MainTex", texLeaf);
                newLeafMat.SetColor("_Color", Color.white);
                newLeafMat.SetVector("_SunDir", new Vector4(0.35f, 0.85f, 0.40f, 0f));
                newLeafMat.SetFloat("_LightOffsetScale", 0.396f);
                newLeafMat.SetColor("_GradDark",  new Color(0.333f, 0.384f, 0.299f));   // → sRGB #64805A（视频暗部）
                newLeafMat.SetColor("_GradMid1",  new Color(0.429f, 0.494f, 0.374f));
                newLeafMat.SetColor("_GradMid2",  new Color(0.559f, 0.644f, 0.487f));
                newLeafMat.SetColor("_GradLight", new Color(0.686f, 0.790f, 0.660f));   // → sRGB #8CA676（视频亮部）
                // 用他们的 树叶.mat 里能读到的颜色（如果可读）
                if (leafMat != null && leafMat.HasProperty("_Color")) newLeafMat.SetColor("_ColorA", leafMat.GetColor("_Color"));
                leafMat = newLeafMat;
                leafMat2 = newLeafMat;   // matIndex 随机 0~1，必须两个槽位都有材质
                Debug.Log("[Imp] 已换成 HLSL 叶片材质");
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
                dl.LeafDenesity = 0.443f;   // 工程值
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