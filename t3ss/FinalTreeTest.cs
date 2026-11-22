using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace T3ssDemo
{
    /// <summary>
    /// 最终版：按「多个独立椭球」的逻辑处理形状。
    ///
    /// 关键：不能整体缩放（会把每个球压扁），要「重排椭球中心」——
    /// 每个椭球保持原形，只改变它们在空间中的位置，从而改变整体轮廓。
    ///
    /// 这对应 SIGGRAPH 2014 的做法：blob 的 size 和 position 抖动。
    ///
    /// 管线：
    ///   ① 树叶网格 → k-means 聚类成 N 个椭球 → 重排中心（伞状）
    ///   ② 渲染到 640×360（降采样，解决网格问题）
    ///   ③ OKLab 量化 8 级（解决颜色问题）
    ///   ④ 最近邻放大到 1920×1080
    /// </summary>
    public static class FinalTreeTest
    {
        // ---------- k-means 聚类 ----------
        static int[] ClusterVertices(Vector3[] verts, int k, int iterations, int seed)
        {
            var rng = new System.Random(seed);
            int n = verts.Length;
            var assign = new int[n];

            // 初始化：随机选 k 个顶点当中心
            var centers = new Vector3[k];
            for (int i = 0; i < k; i++) centers[i] = verts[rng.Next(n)];

            for (int it = 0; it < iterations; it++)
            {
                // 分配
                for (int i = 0; i < n; i++)
                {
                    int best = 0; float bestD = float.MaxValue;
                    for (int c = 0; c < k; c++)
                    {
                        float d = (verts[i] - centers[c]).sqrMagnitude;
                        if (d < bestD) { bestD = d; best = c; }
                    }
                    assign[i] = best;
                }
                // 更新中心
                var sum = new Vector3[k];
                var cnt = new int[k];
                for (int i = 0; i < n; i++) { sum[assign[i]] += verts[i]; cnt[assign[i]]++; }
                for (int c = 0; c < k; c++)
                    if (cnt[c] > 0) centers[c] = sum[c] / cnt[c];
            }
            return assign;
        }

        /// <summary>
        /// 重排椭球中心：每个顶点跟着它所属椭球的质心位移。
        /// 椭球本身形状不变。
        /// </summary>
        static Mesh RepositionLobes(Mesh src, int k, float spreadXZ, float squashY, int seed)
        {
            var verts = src.vertices;
            var normals = src.normals;
            var assign = ClusterVertices(verts, k, 24, seed);

            // 每簇质心
            var centroid = new Vector3[k];
            var cnt = new int[k];
            for (int i = 0; i < verts.Length; i++) { centroid[assign[i]] += verts[i]; cnt[assign[i]]++; }
            for (int c = 0; c < k; c++) if (cnt[c] > 0) centroid[c] /= cnt[c];

            // 整体中心（用于相对变换）
            var total = Vector3.zero;
            for (int c = 0; c < k; c++) total += centroid[c];
            total /= k;

            // 每个质心的位移量
            var delta = new Vector3[k];
            for (int c = 0; c < k; c++)
            {
                var rel = centroid[c] - total;
                // X/Z 散开，Y 压扁
                var target = new Vector3(rel.x * spreadXZ, rel.y * squashY, rel.z * spreadXZ);
                delta[c] = target - rel;
            }

            // 应用位移（顶点位置 + 保持法线不变，因为椭球没变形）
            var outV = new Vector3[verts.Length];
            for (int i = 0; i < verts.Length; i++)
                outV[i] = verts[i] + delta[assign[i]];

            var mesh = new Mesh();
            mesh.indexFormat = src.indexFormat;
            mesh.SetVertices(outV);
            mesh.SetNormals(normals);              // ★ 法线不变（椭球没变形）
            mesh.SetUVs(0, src.uv);
            mesh.SetUVs(1, src.uv2);
            mesh.SetTriangles(src.triangles, 0);
            mesh.RecalculateBounds();

            string info = "";
            for (int c = 0; c < k; c++) info += " 簇" + c + "(" + cnt[c] + "顶点," + delta[c].ToString("F2") + ")";
            Debug.Log("[Final] 重排椭球 k=" + k + info);
            return mesh;
        }

        [MenuItem("T3SS/最终树(伞状+像素化+量化)")]
        public static void Run()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ===== 树 =====
            var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Imported/Model/model.fbx");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.transform.position = Vector3.zero;

            Mesh origShape = null;
            foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
                if (mf.sharedMesh != null && (mf.name.Contains("树叶") || mf.sharedMesh.name.Contains("树叶")))
                { origShape = mf.sharedMesh; break; }

            // ★ 重排椭球：X/Z ×1.25（散开），Y ×0.68（压扁）→ 伞状
            var shapeMesh = RepositionLobes(origShape, 5, 1.25f, 0.68f, 42);

            // 塞回 MeshFilter
            foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
                if (mf.sharedMesh == origShape) { mf.sharedMesh = shapeMesh; break; }

            foreach (var mr in inst.GetComponentsInChildren<MeshRenderer>())
            {
                bool keep = mr.name.Contains("树叶") || mr.name.Contains("树干");
                mr.enabled = keep;
                if (mr.name.Contains("树叶"))
                {
                    var sm = new Material(Shader.Find("Custom/FlatColor"));
                    sm.SetColor("_Color", new Color(0.250f, 0.320f, 0.195f));
                    mr.sharedMaterial = sm;
                }
                if (mr.name.Contains("树干"))
                {
                    var tm = new Material(Shader.Find("Custom/FlatColor"));
                    tm.SetColor("_Color", new Color(0.29f, 0.21f, 0.25f));
                    mr.sharedMaterial = tm;
                }
            }

            var leafShader = Shader.Find("Custom/LeafImported");
            var leafMesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Imported/Model/Leaf.mesh");
            var leafTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Imported/Tex/树叶.png");

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = new Vector3(3, 1, 3);
            var gmat = new Material(Shader.Find("Custom/ToonLit"));
            gmat.SetColor("_BaseColor", new Color(0.52f, 0.64f, 0.55f));
            ground.GetComponent<MeshRenderer>().sharedMaterial = gmat;

            var lg = new GameObject("Sun").AddComponent<Light>();
            lg.type = LightType.Directional;
            lg.intensity = 1.4f;
            lg.color = new Color(1f, 0.96f, 0.86f);
            lg.shadows = LightShadows.Soft;
            lg.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.40f, 0.46f, 0.44f);

            // ★ 叶片：LeafSize 0.30（用户决定），密度 1.0（补偿覆盖）
            var dl = inst.AddComponent<DrawLeaf>();
            dl.ShapeMesh = shapeMesh;
            dl.LeafMesh = leafMesh;
            var lm = new Material(leafShader);
            lm.enableInstancing = true;
            if (leafTex != null) lm.SetTexture("_MainTex", leafTex);
            lm.SetColor("_Color", Color.white);
            lm.SetVector("_SunDir", new Vector4(0.35f, 0.85f, 0.40f, 0f));
            lm.SetFloat("_LightOffsetScale", 0.396f);
            lm.SetColor("_GradDark",  new Color(0.333f, 0.384f, 0.299f));
            lm.SetColor("_GradMid1",  new Color(0.429f, 0.494f, 0.374f));
            lm.SetColor("_GradMid2",  new Color(0.559f, 0.644f, 0.487f));
            lm.SetColor("_GradLight", new Color(0.686f, 0.790f, 0.660f));
            dl.mats = new List<Material> { lm, lm };
            dl.LeafDenesity = 1.0f;      // ★ 100% 顶点（用户决定：密度提高补偿小叶子）
            dl.LeafSize = 0.30f;         // ★ 缩小 5 倍（用户决定）
            dl.LeafOffset = 0.10f;       // 相应缩小
            dl.LightOffset = 0.396f;

            var camGo = new GameObject("Cam");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.70f, 0.78f, 0.80f);
            cam.farClipPlane = 100f;

            var tb = shapeMesh.bounds;
            float frameSize = Mathf.Max(tb.extents.x, tb.extents.y) * 1.25f;
            cam.orthographicSize = frameSize;
            Vector3 lookAt = tb.center;
            cam.transform.position = lookAt + new Vector3(0f, tb.extents.y * 0.3f, -frameSize * 3.2f);
            cam.transform.LookAt(lookAt);

            Directory.CreateDirectory("Shots/Final");

            // ===== 管线 =====
            const int LOW_W = 640, LOW_H = 360;
            const int HIGH_W = 1920, HIGH_H = 1080;
            const float QUANT_STEPS = 8f;   // ★ 用户选 8 级

            var mi = typeof(DrawLeaf).GetMethod("DrawLeafs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var quantShader = Shader.Find("Custom/OKLabQuantize");
            var quantMat = new Material(quantShader);
            quantMat.SetFloat("_Steps", QUANT_STEPS);
            quantMat.SetFloat("_Dither", 0f);
            quantMat.SetFloat("_SaturationBoost", 1.0f);

            var lowRT = new RenderTexture(LOW_W, LOW_H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            lowRT.filterMode = FilterMode.Point;
            cam.targetTexture = lowRT;
            dl.InitLeaf();
            for (int f = 0; f < 4; f++) { if (mi != null) mi.Invoke(dl, null); cam.Render(); }

            SaveRT(lowRT, LOW_W, LOW_H, "Shots/Final/01_lowres.png");

            // 量化
            var qRT = RenderTexture.GetTemporary(LOW_W, LOW_H, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            qRT.filterMode = FilterMode.Point;
            Graphics.Blit(lowRT, qRT, quantMat);
            SaveRT(qRT, LOW_W, LOW_H, "Shots/Final/02_quant8_low.png");

            // 放大
            var upRT = RenderTexture.GetTemporary(HIGH_W, HIGH_H, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            upRT.filterMode = FilterMode.Point;
            Graphics.Blit(qRT, upRT);
            SaveRT(upRT, HIGH_W, HIGH_H, "Shots/Final/03_final_1080p.png");

            // 对照组：不量化只放大
            var up2 = RenderTexture.GetTemporary(HIGH_W, HIGH_H, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            up2.filterMode = FilterMode.Point;
            Graphics.Blit(lowRT, up2);
            SaveRT(up2, HIGH_W, HIGH_H, "Shots/Final/03_noquant_1080p.png");

            cam.targetTexture = null;
            lowRT.Release();
            RenderTexture.ReleaseTemporary(qRT);
            RenderTexture.ReleaseTemporary(upRT);
            RenderTexture.ReleaseTemporary(up2);
            Debug.Log("[Final] 完成");
            EditorApplication.Exit(0);
        }

        static void SaveRT(RenderTexture rt, int w, int h, string path)
        {
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            var full = Path.Combine(Directory.GetCurrentDirectory(), path);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllBytes(full, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
    }
}
