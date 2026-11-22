using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace T3ssDemo
{
    /// <summary>
    /// t3ssel8r 完整像素化管线：
    ///   ① 场景渲染到 640×360（降采样 → 解决"网格问题"）
    ///   ② 法线描边（可选，已在 RendererFeature 里）
    ///   ③ OKLab 量化（解决"颜色问题"）
    ///   ④ 最近邻放大到 1920×1080
    ///
    /// 树的参数完全照抄 e2a6e4f（已验证正确）。
    /// </summary>
    public static class PixelatedTreeTest
    {
        [MenuItem("T3SS/像素化树(完整管线)")]
        public static void Run()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ================= 树（照抄 e2a6e4f）=================
            var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Imported/Model/model.fbx");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.transform.position = Vector3.zero;
            inst.transform.rotation = Quaternion.identity;

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
            // 注意：不设置 LifhtOffsetDenesity（保持默认 0，与 e2a6e4f 一致）

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

            Directory.CreateDirectory("Shots/Pixelated");

            // ================= 管线参数 =================
            int LOW_W = 640, LOW_H = 360;      // 内部分辨率
            int HIGH_W = 1920, HIGH_H = 1080;  // 输出分辨率
            float[] quantSteps = { 0f, 4f, 6f, 8f, 12f };   // 0 = 不量化

            var mi = typeof(DrawLeaf).GetMethod("DrawLeafs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var quantShader = Shader.Find("Custom/OKLabQuantize");
            Debug.Log("[PX] OKLabQuantize shader=" + (quantShader != null));
            var quantMat = new Material(quantShader);
            quantMat.SetFloat("_Dither", 0f);
            quantMat.SetFloat("_SaturationBoost", 1.0f);

            // ① 渲染到 640×360
            var lowRT = new RenderTexture(LOW_W, LOW_H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            lowRT.filterMode = FilterMode.Point;
            cam.targetTexture = lowRT;
            dl.InitLeaf();
            for (int f = 0; f < 3; f++) { if (mi != null) mi.Invoke(dl, null); cam.Render(); }

            // 保存原始低分辨率
            SaveRT(lowRT, LOW_W, LOW_H, "Shots/Pixelated/01_lowres_raw.png");
            Debug.Log("[PX] ① 低分辨率渲染完成 " + LOW_W + "x" + LOW_H);

            // ② 逐个量化等级处理 + 放大
            for (int i = 0; i < quantSteps.Length; i++)
            {
                float steps = quantSteps[i];
                var srcRT = lowRT;

                if (steps > 0f)
                {
                    quantMat.SetFloat("_Steps", steps);
                    var qRT = RenderTexture.GetTemporary(LOW_W, LOW_H, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                    qRT.filterMode = FilterMode.Point;
                    Graphics.Blit(lowRT, qRT, quantMat);
                    SaveRT(qRT, LOW_W, LOW_H, "Shots/Pixelated/02_quant_" + (int)steps + "_low.png");
                    // 放大到 1920×1080（点采样）
                    var upRT = RenderTexture.GetTemporary(HIGH_W, HIGH_H, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                    upRT.filterMode = FilterMode.Point;
                    Graphics.Blit(qRT, upRT);
                    SaveRT(upRT, HIGH_W, HIGH_H, "Shots/Pixelated/03_quant_" + (int)steps + "_1080p.png");
                    RenderTexture.ReleaseTemporary(qRT);
                    RenderTexture.ReleaseTemporary(upRT);
                    Debug.Log("[PX] 量化 steps=" + steps + " 完成");
                }
                else
                {
                    // 不量化，只放大（对照组）
                    var upRT = RenderTexture.GetTemporary(HIGH_W, HIGH_H, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                    upRT.filterMode = FilterMode.Point;
                    Graphics.Blit(lowRT, upRT);
                    SaveRT(upRT, HIGH_W, HIGH_H, "Shots/Pixelated/03_noquant_1080p.png");
                    RenderTexture.ReleaseTemporary(upRT);
                    Debug.Log("[PX] 对照组（不量化）完成");
                }
            }

            cam.targetTexture = null;
            lowRT.Release();
            EditorApplication.Exit(0);
        }

        static void SaveRT(RenderTexture rt, int w, int h, string path)
        {
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false);   // linear = false（sRGB）
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            var dir = Path.GetDirectoryName(Path.Combine(Directory.GetCurrentDirectory(), path));
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), path), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
    }
}
