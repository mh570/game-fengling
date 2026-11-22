using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace T3ssDemo
{
    /// <summary>
    /// 凹凸球形状生成器 v2 —— 两级凸起。
    /// 参考 SIGGRAPH 2014《Stylized Trees and Landscapes》：
    ///   树冠 = 3 个 blobby（大团） + 表面多个小球（sub-canopy）
    /// 单一连续表面 → 法线连续 → 有叶团层次且无接缝。
    /// </summary>
    public static class LobeShapeTest
    {
        /// <summary>
        /// ★ 工程文件的做法：N 个独立椭球（3~5 个）重叠成树冠。
        /// 每个球是完整闭合椭球面，法线用各自的球心 → 每个球有自己的明暗。
        /// </summary>
        public static Mesh BuildEllipsoidCluster(int seed, int count,
                                                 float minR, float maxR,
                                                 float spreadXZ, float spreadY,
                                                 float flatY, int lon, int lat,
                                                 float centerY)
        {
            var rng = new System.Random(seed);
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var uv2 = new List<Vector4>();
            var colors = new List<Color>();
            var tris = new List<int>();

            int vbase = 0;
            for (int b = 0; b < count; b++)
            {
                // 球心位置：第一个居中，其余随机散布
                Vector3 c;
                float r;
                if (b == 0)
                {
                    c = new Vector3(0f, centerY, 0f);
                    r = maxR * 1.0f;
                }
                else
                {
                    float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                    float d = spreadXZ * (0.35f + (float)rng.NextDouble() * 0.65f);
                    c = new Vector3(Mathf.Cos(a) * d,
                                    centerY + ((float)rng.NextDouble() - 0.35f) * spreadY,
                                    Mathf.Sin(a) * d);
                    r = Mathf.Lerp(minR, maxR, (float)rng.NextDouble());
                }

                // 每个球轻微压扁
                float fy = flatY * (0.9f + (float)rng.NextDouble() * 0.2f);

                for (int j = 0; j <= lat; j++)
                {
                    float vv = j / (float)lat;
                    float phi = vv * Mathf.PI;
                    for (int i = 0; i <= lon; i++)
                    {
                        float uu = i / (float)lon;
                        float theta = uu * Mathf.PI * 2f;
                        var dir = new Vector3(
                            Mathf.Sin(phi) * Mathf.Cos(theta),
                            Mathf.Cos(phi),
                            Mathf.Sin(phi) * Mathf.Sin(theta));
                        var pos = c + new Vector3(dir.x * r, dir.y * r * fy, dir.z * r);
                        verts.Add(pos);
                        // ★ 每个球用自己的球心算法线
                        var nrm = new Vector3(dir.x, dir.y / Mathf.Max(0.01f, fy), dir.z).normalized;
                        normals.Add(nrm);
                        uvs.Add(new Vector2(uu, vv));
                        uv2.Add(new Vector4(pos.x, pos.y, pos.z, 0));
                        colors.Add(Color.white);
                    }
                }
                for (int j = 0; j < lat; j++)
                    for (int i = 0; i < lon; i++)
                    {
                        int a0 = vbase + j * (lon + 1) + i;
                        int a1 = a0 + lon + 1;
                        tris.Add(a0); tris.Add(a1); tris.Add(a0 + 1);
                        tris.Add(a0 + 1); tris.Add(a1); tris.Add(a1 + 1);
                    }
                vbase += (lat + 1) * (lon + 1);
            }

            var mesh = new Mesh();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, uv2);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Mesh BuildLobedShape(int seed,
                                           int bigLobes, float bigH, float bigW,
                                           int smallLobes, float smallH, float smallW,
                                           float baseR, int lon, int lat)
        {
            // 默认扁椭圆（宽 > 高）
            return BuildLobedShape(seed, bigLobes, bigH, bigW, smallLobes, smallH, smallW,
                                   baseR, lon, lat, new Vector3(1.15f, 0.85f, 1.15f));
        }

        /// <summary>
        /// 扁椭圆 + 若干突出球。
        /// ellipsoidScale: 基础椭圆的各轴缩放（Y 小于 XZ 就是扁的）
        /// 突出球用高斯叠加，高度 = baseR * bigH（大约 1/3 半径）
        /// </summary>
        public static Mesh BuildLobedShape(int seed,
                                           int bigLobes, float bigH, float bigW,
                                           int smallLobes, float smallH, float smallW,
                                           float baseR, int lon, int lat,
                                           Vector3 ellipsoidScale)
        {
            var rng = new System.Random(seed);
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var uv2 = new List<Vector4>();
            var colors = new List<Color>();
            var tris = new List<int>();
            var lobeCenters = new List<Vector3>();   // 每个凸起的中心（用于逐凸起法线）

            var lobes = new List<Vector3>();
            var lobeH = new List<float>();
            var lobeW = new List<float>();

            void AddLobe(int n, float h, float w)
            {
                for (int k = 0; k < n; k++)
                {
                    float theta = (float)rng.NextDouble() * Mathf.PI * 2f;
                    float phi = Mathf.Acos(2f * (float)rng.NextDouble() - 1f);
                    lobes.Add(new Vector3(
                        Mathf.Sin(phi) * Mathf.Cos(theta),
                        Mathf.Cos(phi),
                        Mathf.Sin(phi) * Mathf.Sin(theta)));
                    lobeH.Add(h * (0.75f + (float)rng.NextDouble() * 0.5f));
                    lobeW.Add(w * (0.8f + (float)rng.NextDouble() * 0.4f));
                }
            }
            AddLobe(bigLobes, bigH, bigW);          // 主团
            AddLobe(smallLobes, smallH, smallW);    // 表面小球

            // 每个凸起的中心（椭球空间内，稍微往里收一点）
            foreach (var ld in lobes)
                lobeCenters.Add(new Vector3(ld.x * baseR * ellipsoidScale.x * 0.75f,
                                            ld.y * baseR * ellipsoidScale.y * 0.75f,
                                            ld.z * baseR * ellipsoidScale.z * 0.75f));
            // 最外层兜底（没找到任何凸起时用整体中心）
            lobeCenters.Add(Vector3.zero);

            int start = 0;
            var normalsPerLobe = new List<Vector3>();
            for (int j = 0; j <= lat; j++)
            {
                float vv = j / (float)lat;
                float phiA = vv * Mathf.PI;
                for (int i = 0; i <= lon; i++)
                {
                    float uu = i / (float)lon;
                    float thetaA = uu * Mathf.PI * 2f;
                    var dir = new Vector3(
                        Mathf.Sin(phiA) * Mathf.Cos(thetaA),
                        Mathf.Cos(phiA),
                        Mathf.Sin(phiA) * Mathf.Sin(thetaA));

                    float r = baseR;
                    for (int k = 0; k < lobes.Count; k++)
                    {
                        float ang = Mathf.Acos(Mathf.Clamp(Vector3.Dot(dir, lobes[k]), -1f, 1f));
                        r += baseR * lobeH[k] * Mathf.Exp(-(ang * ang) / (lobeW[k] * lobeW[k]));
                    }

                    var pos = dir * r;
                    // 扁椭圆：把球压成椭球（Y 轴压扁）
                    pos = new Vector3(pos.x * ellipsoidScale.x,
                                      pos.y * ellipsoidScale.y,
                                      pos.z * ellipsoidScale.z);
                    verts.Add(pos);

                    // ★ 逐凸起法线：找最近的凸起中心，法线从那个中心往外
                    //   这样每个叶团有自己的明暗 → 有层次
                    //   （几何是单一连续网格 → 没有接缝）
                    int nearest = lobeCenters.Count - 1;
                    float bestD = float.MaxValue;
                    for (int k = 0; k < lobeCenters.Count; k++)
                    {
                        float d = (pos - lobeCenters[k]).sqrMagnitude;
                        if (d < bestD) { bestD = d; nearest = k; }
                    }
                    var nrm = (pos - lobeCenters[nearest]);
                    normalsPerLobe.Add(nrm.sqrMagnitude > 1e-8f ? nrm.normalized : dir);

                    uvs.Add(new Vector2(uu, vv));
                    uv2.Add(new Vector4(pos.x, pos.y, pos.z, 0));
                    colors.Add(Color.white);
                }
            }

            for (int j = 0; j < lat; j++)
                for (int i = 0; i < lon; i++)
                {
                    int a0 = start + j * (lon + 1) + i;
                    int a1 = a0 + lon + 1;
                    tris.Add(a0); tris.Add(a1); tris.Add(a0 + 1);
                    tris.Add(a0 + 1); tris.Add(a1); tris.Add(a1 + 1);
                }

            var mesh = new Mesh();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(normalsPerLobe);   // ★ 逐凸起法线（不是 RecalculateNormals）
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, uv2);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        [MenuItem("T3SS/叶团树变体")]
        public static void Run()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var leafShader = Shader.Find("Custom/LeafImported");
            var leafMesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Imported/Model/Leaf.mesh");
            var leafTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Imported/Tex/树叶.png");
            Debug.Log("[Lobe] shader=" + (leafShader != null) + " leafMesh=" + (leafMesh != null) + " tex=" + (leafTex != null));

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
            cam.farClipPlane = 300f;
            var rt = new RenderTexture(256, 320, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            Directory.CreateDirectory("Shots/LobeTree");

            var mi = typeof(DrawLeaf).GetMethod("DrawLeafs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var flatShader = Shader.Find("Custom/FlatColor");

            // 6 个变体：大团 / 小球
            var variants = new (int seed, int big, float bigH, float bigW, int small, float smallH, float smallW)[]
            {
                (11, 3, 0.42f, 0.70f, 4, 0.12f, 0.30f),
                (22, 3, 0.50f, 0.65f, 6, 0.15f, 0.28f),
                (33, 4, 0.45f, 0.72f, 5, 0.10f, 0.32f),
                (44, 2, 0.55f, 0.80f, 6, 0.14f, 0.26f),
                (55, 3, 0.38f, 0.60f, 8, 0.16f, 0.24f),
                (66, 4, 0.48f, 0.68f, 3, 0.12f, 0.34f),
            };

            for (int vi = 0; vi < variants.Length; vi++)
            {
                var v = variants[vi];
                var mesh = BuildLobedShape(v.seed, v.big, v.bigH, v.bigW, v.small, v.smallH, v.smallW,
                                           1.0f, 44, 26);
                string path = "Assets/Generated/LobeTree_" + vi + ".asset";
                AssetDatabase.DeleteAsset(path);
                Directory.CreateDirectory("Assets/Generated");
                AssetDatabase.CreateAsset(mesh, path);
                var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);

                var go = new GameObject("LobeTree_" + vi);
                go.transform.position = Vector3.zero;
                go.AddComponent<MeshFilter>().sharedMesh = saved;

                // 底衬：纯色补色（和叶片中调接近）
                var smr = go.AddComponent<MeshRenderer>();
                var smat = new Material(flatShader);
                smat.SetColor("_Color", new Color(0.250f, 0.320f, 0.195f));
                smr.sharedMaterial = smat;

                // DrawLeaf
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

                var bnd = saved.bounds;
                float ext = Mathf.Max(bnd.extents.x, bnd.extents.y) * 1.5f;
                cam.orthographicSize = ext;
                cam.transform.position = bnd.center + new Vector3(0, ext * 0.1f, -ext * 3.2f);
                cam.transform.LookAt(bnd.center);

                dl.InitLeaf();
                for (int f = 0; f < 3; f++) { if (mi != null) mi.Invoke(dl, null); cam.Render(); }

                RenderTexture.active = rt;
                var shot = new Texture2D(256, 320, TextureFormat.RGB24, false);
                shot.ReadPixels(new Rect(0, 0, 256, 320), 0, 0);
                shot.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), "Shots/LobeTree/t" + vi + ".png"), shot.EncodeToPNG());
                Object.DestroyImmediate(shot);
                Object.DestroyImmediate(go);
                Debug.Log("[Lobe] 树 " + vi + ": 大团" + v.big + "(h" + v.bigH + " w" + v.bigW + ") 小球" + v.small + "(h" + v.smallH + " w" + v.smallW + ")");
            }

            cam.targetTexture = null;
            rt.Release();
            EditorApplication.Exit(0);
        }
    }
}
