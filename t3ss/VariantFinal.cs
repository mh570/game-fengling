using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace T3ssDemo
{
    /// <summary>
    /// 完全照抄 e2a6e4f 的配置，只把形状网格换成本地变体。
    /// 变形只用「平滑」的方式（各轴缩放 + 低频弯曲），不做逐顶点随机抖动。
    /// </summary>
    public static class VariantFinal
    {
        static Mesh LoadProjectShape()
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Imported/Model/model.fbx");
            if (go != null)
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                    if (mf.sharedMesh != null && mf.sharedMesh.name.Contains("树叶"))
                        return mf.sharedMesh;
            return null;
        }

        /// <summary>平滑变形：各轴缩放 + 低频弯曲，无逐顶点随机。</summary>
        public static Mesh SmoothVariant(Mesh src, int seed, float scaleLo, float scaleHi, float bend)
        {
            var rng = new System.Random(seed);
            var vs = src.vertices;
            var ns = src.normals;
            var b = src.bounds;
            var c = b.center;

            float sx = Mathf.Lerp(scaleLo, scaleHi, (float)rng.NextDouble());
            float sy = Mathf.Lerp(scaleLo, scaleHi, (float)rng.NextDouble());
            float sz = Mathf.Lerp(scaleLo, scaleHi, (float)rng.NextDouble());
            float rotY = (float)rng.NextDouble() * 360f;
            float bendSign = rng.NextDouble() > 0.5 ? 1f : -1f;
            var rot = Quaternion.Euler(0f, rotY, 0f);

            // 非均匀缩放的逆转置（用于修正法线）
            var inv = Matrix4x4.Scale(new Vector3(1f / sx, 1f / sy, 1f / sz));
            var nrmMat = inv.transpose;

            var outV = new Vector3[vs.Length];
            var outN = new Vector3[ns.Length];
            for (int i = 0; i < vs.Length; i++)
            {
                var p = vs[i] - c;
                p = new Vector3(p.x * sx, p.y * sy, p.z * sz);
                // 低频弯曲：越靠上越偏，用 t² 平滑
                float t = Mathf.InverseLerp(b.min.y, b.max.y, vs[i].y);
                p.x += bendSign * bend * t * t * b.extents.x;
                p = rot * p;
                outV[i] = p + c;

                var n = nrmMat.MultiplyVector(ns[i]).normalized;
                outN[i] = (rot * n).normalized;
            }

            var mesh = new Mesh();
            mesh.indexFormat = src.indexFormat;
            mesh.SetVertices(outV);
            mesh.SetNormals(outN);
            mesh.SetUVs(0, src.uv);
            mesh.SetUVs(1, src.uv2);
            mesh.SetTriangles(src.triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        [MenuItem("T3SS/最终变体树")]
        public static void Run()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var baseMesh = LoadProjectShape();
            if (baseMesh == null) { Debug.LogError("[VF] 找不到 树叶"); EditorApplication.Exit(1); return; }

            var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Imported/Model/model.fbx");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.transform.position = Vector3.zero;

            // 只保留 树叶 + 树干（照抄 e2a6e4f）
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

            // 找 树叶 MeshFilter 和 树干
            MeshFilter shapeMF = null; MeshFilter trunkMF = null;
            var trunkRenderers = new List<MeshRenderer>();
            foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                if (mf.name.Contains("树叶") && shapeMF == null) shapeMF = mf;
                if (mf.name.Contains("树干") && trunkMF == null) trunkMF = mf;
            }
            Debug.Log("[VF] 树叶MF=" + (shapeMF != null) + " 树干MF=" + (trunkMF != null));

            var leafShader = Shader.Find("Custom/LeafImported");
            var leafMesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Imported/Model/Leaf.mesh");
            var leafTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Imported/Tex/树叶.png");

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = new Vector3(30, 1, 30);
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
            cam.farClipPlane = 100f;
            var rt = new RenderTexture(256, 320, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            Directory.CreateDirectory("Shots/VariantFinal");

            var mi = typeof(DrawLeaf).GetMethod("DrawLeafs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            // 8 个变体（缩放范围温和 0.90~1.12）
            var cfgs = new (int seed, float lo, float hi, float bend)[]
            {
                (11, 0.98f, 1.02f, 0.00f),   // 几乎原始
                (22, 0.90f, 1.10f, 0.03f),
                (33, 0.95f, 1.12f, -0.04f),
                (44, 0.88f, 1.08f, 0.05f),
                (55, 1.00f, 1.08f, 0.00f),
                (66, 0.92f, 1.06f, -0.03f),
                (77, 0.96f, 1.04f, 0.02f),
                (88, 0.90f, 1.12f, -0.05f),
            };

            for (int i = 0; i < cfgs.Length; i++)
            {
                var c = cfgs[i];
                var shape = SmoothVariant(baseMesh, c.seed, c.lo, c.hi, c.bend);
                shapeMF.sharedMesh = shape;

                // 移除旧的 DrawLeaf，重加（InitLeaf 只能调一次）
                var oldDl = inst.GetComponent<DrawLeaf>();
                if (oldDl != null) Object.DestroyImmediate(oldDl);

                var dl = inst.AddComponent<DrawLeaf>();
                dl.ShapeMesh = shape;
                dl.LeafMesh = leafMesh;
                var lm = new Material(leafShader);
                lm.enableInstancing = true;
                if (leafTex != null) lm.SetTexture("_MainTex", leafTex);
                // ★ 照抄 e2a6e4f
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
                dl.LifhtOffsetDenesity = 0.588f;

                // ★ 照抄 e2a6e4f 的取景
                var tb = shape.bounds;
                float frameSize = Mathf.Max(tb.extents.x, tb.extents.y) * 1.25f;
                cam.orthographicSize = frameSize;
                Vector3 lookAt = tb.center;
                cam.transform.position = lookAt + new Vector3(0f, tb.extents.y * 0.3f, -frameSize * 3.2f);
                cam.transform.LookAt(lookAt);

                dl.InitLeaf();
                for (int f = 0; f < 3; f++) { if (mi != null) mi.Invoke(dl, null); cam.Render(); }
                RenderTexture.active = rt;
                var shot = new Texture2D(256, 320, TextureFormat.RGB24, false);
                shot.ReadPixels(new Rect(0, 0, 256, 320), 0, 0);
                shot.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), "Shots/VariantFinal/f" + i + ".png"), shot.EncodeToPNG());
                Object.DestroyImmediate(shot);
                Debug.Log("[VF] 变体 " + i + " bbox=" + tb.size + " 缩放(" + c.lo + "~" + c.hi + ")");
            }

            cam.targetTexture = null;
            rt.Release();
            EditorApplication.Exit(0);
        }
    }
}
