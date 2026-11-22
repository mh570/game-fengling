using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace T3ssDemo
{
    /// <summary>
    /// 按 t3ssel8r 的正确顺序：
    ///   阶段①：3D 渲染 —— 独立球体各自明暗（色块来源）
    ///   阶段②：像素化 —— 降采样 + OKLab 量化 + 最近邻放大
    ///
    /// 关键：阶段①必须先正确，否则阶段②只是把错误放大成马赛克。
    /// </summary>
    public static class TwoStageTest
    {
        [MenuItem("T3SS/两阶段(先色块后像素化)")]
        public static void Run()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ============ 阶段①：3D 场景（照抄 e2a6e4f，球体保持原样）============
            var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Imported/Model/model.fbx");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.transform.position = Vector3.zero;

            Mesh shapeMesh = null;
            foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
                if (mf.sharedMesh != null && (mf.name.Contains("树叶") || mf.sharedMesh.name.Contains("树叶")))
                { shapeMesh = mf.sharedMesh; break; }

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

            // ★ 独立球体：用工程原始网格（球体各自独立、各自法线）
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
            dl.LeafDenesity = 0.443f;
            dl.LeafSize = 1.449f;
            dl.LeafOffset = 0.356f;
            dl.LightOffset = 0.396f;
            dl.LifhtOffsetDenesity = 0.588f;   // 工程材质值 → 每叶有明暗变化

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

            Directory.CreateDirectory("Shots/TwoStage");

            var mi = typeof(DrawLeaf).GetMethod("DrawLeafs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            // ---------- 阶段①：高分辨率 3D 渲染（看清每个球的色块）----------
            const int HI = 1024;
            var hiRT = new RenderTexture(HI, HI, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            cam.targetTexture = hiRT;
            dl.InitLeaf();
            for (int f = 0; f < 4; f++) { if (mi != null) mi.Invoke(dl, null); cam.Render(); }
            SaveRT(hiRT, HI, HI, "Shots/TwoStage/01_stage1_3d_1024.png");
            Debug.Log("[2S] 阶段① 3D 渲染完成 1024×1024");

            // ---------- 阶段②：像素化（降采样 + 量化 + 放大）----------
            var quantShader = Shader.Find("Custom/OKLabQuantize");
            var quantMat = new Material(quantShader);

            // 从 1024 降到 256（4×降采样，等效于 512 屏幕下的 640×360 比例）
            int[] lowSizes = { 512, 256, 192 };
            foreach (int low in lowSizes)
            {
                var lo = RenderTexture.GetTemporary(low, low, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                lo.filterMode = FilterMode.Point;
                Graphics.Blit(hiRT, lo);
                SaveRT(lo, low, low, "Shots/TwoStage/02_down_" + low + ".png");

                // 量化 8 级
                var q = RenderTexture.GetTemporary(low, low, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                q.filterMode = FilterMode.Point;
                quantMat.SetFloat("_Steps", 8f);
                quantMat.SetFloat("_Dither", 0f);
                quantMat.SetFloat("_SaturationBoost", 1.0f);
                Graphics.Blit(lo, q, quantMat);

                // 放大回 1024
                var up = RenderTexture.GetTemporary(HI, HI, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                up.filterMode = FilterMode.Point;
                Graphics.Blit(q, up);
                SaveRT(up, HI, HI, "Shots/TwoStage/03_pixel_" + low + "_quant8.png");

                RenderTexture.ReleaseTemporary(lo);
                RenderTexture.ReleaseTemporary(q);
                RenderTexture.ReleaseTemporary(up);
                Debug.Log("[2S] 阶段② 像素化 low=" + low + " 完成");
            }

            cam.targetTexture = null;
            hiRT.Release();
            Debug.Log("[2S] 全部完成");
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
