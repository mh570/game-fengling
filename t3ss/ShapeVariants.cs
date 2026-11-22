using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace T3ssDemo
{
    /// <summary>
    /// 以工程自带的 树叶 网格为模板，变形生成多种树形。
    /// 保留工程的原始风格（多个球 + 每球独立法线），只改变比例和表面细节。
    /// </summary>
    public static class ShapeVariants
    {
        static Mesh LoadProjectShape()
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Imported/Model/model.fbx");
            if (go != null)
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                    if (mf.sharedMesh != null && mf.sharedMesh.name.Contains("树叶"))
                        return mf.sharedMesh;
            var all = AssetDatabase.LoadAllAssetsAtPath("Assets/Imported/Model/model.fbx");
            foreach (var o in all)
            {
                var m = o as Mesh;
                if (m != null && m.name.Contains("树叶")) return m;
            }
            return null;
        }

        /// <summary>用模板网格生成一个变体。</summary>
        public static Mesh MakeVariant(Mesh src, int seed,
                                       float scaleMin, float scaleMax,
                                       float jitter, float bendY)
        {
            var rng = new System.Random(seed);
            var vs = src.vertices;
            var ns = src.normals;
            var b = src.bounds;

            // ① 各轴随机缩放
            float sx = Mathf.Lerp(scaleMin, scaleMax, (float)rng.NextDouble());
            float sy = Mathf.Lerp(scaleMin, scaleMax, (float)rng.NextDouble());
            float sz = Mathf.Lerp(scaleMin, scaleMax, (float)rng.NextDouble());
            // ② 绕 Y 随机旋转
            float rotY = (float)rng.NextDouble() * 360f;
            var rot = Quaternion.Euler(0f, rotY, 0f);
            // ③ 轻微弯曲（顶部往一边偏）
            float bendSign = rng.NextDouble() > 0.5 ? 1f : -1f;

            var outV = new Vector3[vs.Length];
            var outN = new Vector3[ns.Length];
            var center = b.center;
            for (int i = 0; i < vs.Length; i++)
            {
                var p = vs[i] - center;                       // 相对中心
                p = new Vector3(p.x * sx, p.y * sy, p.z * sz); // 缩放
                // 弯曲：越靠上越往 +X 偏
                float t = Mathf.InverseLerp(b.min.y, b.max.y, vs[i].y);
                p.x += bendSign * bendY * t * t * b.extents.x;
                // 沿法线抖动（表面起伏）
                float j = ((float)rng.NextDouble() - 0.5f) * 2f * jitter;
                p += ns[i] * (j * b.extents.y * 0.25f);
                p = rot * p;
                outV[i] = p + center;
                outN[i] = rot * ns[i];
            }

            var mesh = new Mesh();
            mesh.indexFormat = src.indexFormat;
            mesh.SetVertices(outV);
            mesh.SetNormals(outN);
            mesh.SetUVs(0, src.uv);
            mesh.SetUVs(1, src.uv2);
            mesh.SetColors(new List<Color>(src.colors.Length == vs.Length ? src.colors : new Color[vs.Length]));
            mesh.SetTriangles(src.triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        [MenuItem("T3SS/生成形状变体")]
        public static void Run()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var src = LoadProjectShape();
            if (src == null) { Debug.LogError("[Var] 找不到工程 树叶 网格"); EditorApplication.Exit(1); return; }
            Debug.Log("[Var] 模板网格: " + src.vertexCount + " 顶点, bbox=" + src.bounds.size);

            var lg = new GameObject("Sun").AddComponent<Light>();
            lg.type = LightType.Directional;
            lg.intensity = 1.05f;
            lg.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.55f);

            var camGo = new GameObject("Cam");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.78f, 0.86f, 0.90f);
            cam.farClipPlane = 300f;
            var rt = new RenderTexture(256, 320, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            Directory.CreateDirectory("Shots/ShapeVariants");

            var gray = new Material(Shader.Find("Custom/FlatColor"));
            gray.SetColor("_Color", new Color(0.55f, 0.55f, 0.55f));

            // 8 个变体：不同的缩放范围/抖动/弯曲
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
                var mesh = MakeVariant(src, c.seed, c.smin, c.smax, c.jit, c.bend);
                var go = new GameObject("V" + i);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = gray;

                var b = mesh.bounds;
                float ext = Mathf.Max(b.extents.x, b.extents.y) * 1.3f;
                cam.orthographicSize = ext;
                cam.transform.position = b.center + new Vector3(0, ext * 0.1f, -ext * 3f);
                cam.transform.LookAt(b.center);
                cam.Render();
                RenderTexture.active = rt;
                var shot = new Texture2D(256, 320, TextureFormat.RGB24, false);
                shot.ReadPixels(new Rect(0, 0, 256, 320), 0, 0);
                shot.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), "Shots/ShapeVariants/s" + i + ".png"), shot.EncodeToPNG());
                Object.DestroyImmediate(shot);
                Object.DestroyImmediate(go);
                Debug.Log("[Var] 变体 " + i + " bbox=" + b.size + " 缩放(" + c.smin + "~" + c.smax + ") 抖动" + c.jit);
            }

            cam.targetTexture = null;
            rt.Release();
            EditorApplication.Exit(0);
        }
    }
}
