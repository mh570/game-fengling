using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace T3ssDemo
{
    /// <summary>
    /// 严格按 t3ssel8r / Ghislain GIRARDOT 的做法生成树：
    ///   1. 树干：锥形圆柱 + 2~3 根分枝
    ///   2. 树冠"壳"：3 个错位的球（提供体积与遮挡）
    ///   3. 叶片卡片：在壳表面撒几百张带 alpha 贴图的小方片，XYZ 全随机旋转
    ///   4. 法线：球形重映射（从树冠中心往外）
    /// </summary>
    public static class TreeGenerator
    {
        // ---------- 叶片贴图（代码生成）----------
        public static Texture2D MakeLeafTexture(int size = 32, int seed = 7)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = new Color(1, 1, 1, 0);

            var rng = new System.Random(seed);
            int leaves = 14 + rng.Next(6);   // 更多圆，填满方形
            for (int k = 0; k < leaves; k++)
            {
                float cx = size * 0.5f + (float)(rng.NextDouble() - 0.5) * size * 0.50f;
                float cy = size * 0.5f + (float)(rng.NextDouble() - 0.5) * size * 0.50f;
                float r = size * (0.14f + (float)rng.NextDouble() * 0.10f);   // 更大的圆
                float shade = 0.72f + (float)rng.NextDouble() * 0.28f;

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        if (d <= r)
                        {
                            // 纯白：颜色全靠顶点色/光照，卡片本身不能再有明暗
                            // （否则每张卡片都是一个亮点 → 满树白点）
                            int idx = y * size + x;
                            px[idx] = new Color(1f, 1f, 1f, 1f);
                        }
                    }
                }
            }

            tex.SetPixels(px);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.Apply();
            return tex;
        }

        // ---------- 生成一棵树 ----------
        public struct Options
        {
            public float Height;
            public int LeafCount;
            public float LeafSize;
            public int Seed;
        }

        /// <summary>只生成形状网格（3 个错位锯齿球），供 DrawLeaf.cs 用。</summary>
        public static Mesh BuildShapeOnly(Options opt)
        {
            var rng = new System.Random(opt.Seed);
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var uv2s = new List<Vector4>();
            var colors = new List<Color>();
            var tris = new List<int>();
            int triBase = 0;

            float H = opt.Height;
            float trunkRatio = 0.55f;
            float trunkH = H * trunkRatio;
            float canopyR = H * 0.42f;
            float canopyBase = trunkH * 1.15f;

            var shellCenters = new List<Vector4>();
            shellCenters.Add(new Vector4(0f, canopyBase + canopyR * 0.45f, 0f, canopyR));
            for (int k = 0; k < 2; k++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float d = canopyR * (1.00f + (float)rng.NextDouble() * 0.40f);   // 拉到球外，显出多凸起
                float rr = canopyR * (0.48f + (float)rng.NextDouble() * 0.14f);   // 缩小副球
                shellCenters.Add(new Vector4(Mathf.Cos(a) * d,
                    canopyBase + canopyR * (0.20f + (float)rng.NextDouble() * 0.45f),
                    Mathf.Sin(a) * d, rr));
            }

            int shIdx = 0;
            foreach (var sc in shellCenters)
                AddSphere(verts, normals, uvs, uv2s, colors, tris, ref triBase,
                    new Vector3(sc.x, sc.y, sc.z), sc.w, 28, 20, Color.white, 0.18f, opt.Seed * 31 + shIdx++);

            var mesh = new Mesh();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, uv2s);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
        public static Mesh Build(Options opt, out int shellVerts, out int leafVerts)
        {
            var rng = new System.Random(opt.Seed);
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var uv2s = new List<Vector4>();   // xyz = 卡片中心（billboard 用）
            var colors = new List<Color>();
            var tris = new List<int>();
            int triBase = 0;

            float H = opt.Height;
            // 形状变体：树干比例、树冠比例、粗细都按种子变化
            float trunkRatio = 0.45f + (float)rng.NextDouble() * 0.20f;   // 0.45~0.65
            float trunkH = H * trunkRatio;
            float trunkR = Mathf.Max(0.13f, H * (0.080f + (float)rng.NextDouble() * 0.030f));   // 更粗

            // ---- 1. 树干（锥形圆柱）
            AddCylinder(verts, normals, uvs, uv2s, colors, tris, ref triBase,
                Vector3.zero, trunkH, trunkR, trunkR * 0.55f, 8,
                new Color(0.067f, 0.036f, 0.050f, 1f));

            // ---- 2. 分枝（2~3 根，斜向上）
            int branches = 2 + rng.Next(2);
            for (int b = 0; b < branches; b++)
            {
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                float baseY = trunkH * (0.55f + (float)rng.NextDouble() * 0.25f);
                float len = trunkH * (0.35f + (float)rng.NextDouble() * 0.2f);
                var start = new Vector3(Mathf.Cos(ang) * trunkR * 0.4f, baseY, Mathf.Sin(ang) * trunkR * 0.4f);
                var dir = new Vector3(Mathf.Cos(ang) * 0.55f, 2.2f, Mathf.Sin(ang) * 0.55f).normalized;   // 分枝更竖直
                var end = start + dir * len;
                AddCylinderBetween(verts, normals, uvs, uv2s, colors, tris, ref triBase, start, end,
                    trunkR * 0.45f, trunkR * 0.28f, 6, new Color(0.067f, 0.036f, 0.050f, 1f));
            }

            shellVerts = verts.Count;

            // ---- 3. 树冠"壳"：3 个错位球（提供体积），位置随种子变化
            float canopyR = H * (0.34f + (float)rng.NextDouble() * 0.14f);   // 0.34~0.48
            float canopyBase = trunkH * (1.30f + (float)rng.NextDouble() * 0.15f);   // 大幅抬高，露出树干
            var shellCenters = new List<Vector4>();   // xyz + radius
            shellCenters.Add(new Vector4(0f, canopyBase + canopyR * 0.45f, 0f, canopyR));
            for (int k = 0; k < 2; k++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float d = canopyR * (0.20f + (float)rng.NextDouble() * 0.35f);
                float rr = canopyR * (0.60f + (float)rng.NextDouble() * 0.20f);
                shellCenters.Add(new Vector4(
                    Mathf.Cos(a) * d,
                    canopyBase + canopyR * (0.18f + (float)rng.NextDouble() * 0.50f),
                    Mathf.Sin(a) * d,
                    rr));
            }

            var shellCol = Color.white;   // 壳与叶片同色：缝隙不再露出暗斑
            int shIdx = 0;
            foreach (var sc in shellCenters)
                AddSphere(verts, normals, uvs, uv2s, colors, tris, ref triBase,
                    new Vector3(sc.x, sc.y, sc.z), sc.w, 14, 10, shellCol, 0.18f, opt.Seed * 31 + shIdx++);

            // 整体树冠中心（用于球形法线）
            var canopyCenter = new Vector3(0f, canopyBase + canopyR * 0.45f, 0f);

            // ---- 4. 叶片：遍历「形状网格顶点」（照 DrawLeaf.cs 的做法）
            // DrawLeaf.cs: for (i < ShapeMesh.vertices.Length) if (density >= random) 长一片叶子
            // 叶片数 = 形状网格顶点数 × 密度，天然均匀、天然贴合形状
            float leafSize = Mathf.Max(0.05f, opt.LeafSize);
            float density = Mathf.Clamp01(opt.LeafCount / 500f);   // 用 LeafCount 参数当"密度×500"用
            int lon = 14, lat = 10;                                 // 和 AddSphere 的细分一致
            int placedLeaves = 0;

            foreach (var sc in shellCenters)
            {
              var center = new Vector3(sc.x, sc.y, sc.z);
              float radius = sc.w;

              for (int j = 0; j <= lat; j++)
              {
                float vv2 = j / (float)lat;
                float phiA = vv2 * Mathf.PI;
                for (int i2 = 0; i2 <= lon; i2++)
                {
                  if ((float)rng.NextDouble() > density) continue;   // 密度过滤

                  float uu2 = i2 / (float)lon;
                  float thetaA = uu2 * Mathf.PI * 2f;
                  var dir = new Vector3(
                      Mathf.Sin(phiA) * Mathf.Cos(thetaA),
                      Mathf.Cos(phiA),
                      Mathf.Sin(phiA) * Mathf.Sin(thetaA));

                  // 位置 = 顶点位置 + 沿法线偏移（DrawLeaf: pos + normal * LeafOffset）
                  var pos = center + dir * (radius * 1.05f);
                  float minY = canopyBase * 1.02f;
                  if (pos.y < minY) pos.y = minY + (float)rng.NextDouble() * canopyR * 0.25f;

                  // 朝向：只绕自身 Z 轴随机（DrawLeaf: Quaternion.Euler(0,0,random)）
                  var rot = Quaternion.Euler(0f, 0f, (float)rng.NextDouble() * 360f);

                  Color lcol = Color.white;   // 颜色全交给渐变，顶点色不再压暗
                  float s = leafSize * (0.8f + (float)rng.NextDouble() * 0.5f);
                  AddQuad(verts, normals, uvs, uv2s, colors, tris, ref triBase, pos, rot, s);
                  placedLeaves++;
                }
              }
            }
            leafVerts = verts.Count - shellVerts;

            // ---- 5. 球形法线重映射（只对树冠区域）
            var arrN = normals.ToArray();
            var arrV = verts.ToArray();
            for (int i = 0; i < arrV.Length; i++)
            {
                if (i < shellVerts) continue;   // 树干/分枝保留原法线；树冠（壳+卡片）全部球形化
                var d = arrV[i] - canopyCenter;
                if (d.sqrMagnitude < 1e-6f) continue;
                // 全球形法线：整团连续法线场，像水彩
                arrN[i] = Vector3.Slerp(arrN[i].normalized, d.normalized, 1.0f).normalized;
            }

            var mesh = new Mesh();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(new List<Vector3>(arrN));
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, uv2s);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // ---------------- 基础几何 ----------------

        static void AddQuad(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<Vector4> uv2, List<Color> c,
                            List<int> t, ref int b, Vector3 pos, Quaternion rot, float s)
        {
            float h = s * 0.5f;
            var corners = new[]
            {
                new Vector3(-h, -h, 0), new Vector3(h, -h, 0),
                new Vector3(-h, h, 0),  new Vector3(h, h, 0),
            };
            var uvc = new[] { new Vector2(0,0), new Vector2(1,0), new Vector2(0,1), new Vector2(1,1) };
            var normal = rot * Vector3.forward;

            for (int i = 0; i < 4; i++)
            {
                v.Add(pos + rot * corners[i]);
                n.Add(normal);
                uv.Add(uvc[i]);
                uv2.Add(new Vector4(pos.x, pos.y, pos.z, 0));   // 卡片中心
                c.Add(Color.white);
            }
            t.Add(b + 0); t.Add(b + 2); t.Add(b + 1);
            t.Add(b + 2); t.Add(b + 3); t.Add(b + 1);
            b += 4;
        }

        static void AddCylinder(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<Vector4> uv2, List<Color> c,
                                List<int> t, ref int b, Vector3 basePos, float height,
                                float rBottom, float rTop, int sides, Color col)
        {
            AddCylinderBetween(v, n, uv, uv2, c, t, ref b, basePos, basePos + Vector3.up * height,
                rBottom, rTop, sides, col);
        }

        static void AddCylinderBetween(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<Vector4> uv2, List<Color> c,
                                       List<int> t, ref int b, Vector3 a, Vector3 bb,
                                       float rA, float rB, int sides, Color col)
        {
            var axis = (bb - a).normalized;
            var up = Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.95f ? Vector3.right : Vector3.up;
            var right = Vector3.Cross(axis, up).normalized;
            var fwd = Vector3.Cross(right, axis).normalized;

            // 平滑法线圆柱（t3ssel8r 的做法：纯色光滑柱体）
            int start = b;
            for (int i = 0; i < sides; i++)
            {
                float ang = i / (float)sides * Mathf.PI * 2f;
                var dir = right * Mathf.Cos(ang) + fwd * Mathf.Sin(ang);
                v.Add(a + dir * rA); n.Add(dir); uv.Add(new Vector2(i / (float)sides, 0)); uv2.Add(new Vector4((a + dir * rA).x, (a + dir * rA).y, (a + dir * rA).z, 0)); c.Add(new Color(col.r, col.g, col.b, 0f));
                v.Add(bb + dir * rB); n.Add(dir); uv.Add(new Vector2(i / (float)sides, 1)); uv2.Add(new Vector4((bb + dir * rB).x, (bb + dir * rB).y, (bb + dir * rB).z, 0)); c.Add(new Color(col.r, col.g, col.b, 0f));
            }
            for (int i = 0; i < sides; i++)
            {
                int i0 = start + i * 2;
                int i1 = start + ((i + 1) % sides) * 2;
                t.Add(i0); t.Add(i1); t.Add(i0 + 1);
                t.Add(i1); t.Add(i1 + 1); t.Add(i0 + 1);
            }
            b += sides * 2;        }

        static void AddSphere(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<Vector4> uv2, List<Color> c,
                              List<int> t, ref int b, Vector3 center, float radius,
                              int lon, int lat, Color col, float jag = 0f, int seed = 0)
        {
            int start = b;
            var jagRng = new System.Random(seed);
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

                    // 锯齿化：每个顶点随机内缩/外扩，形成不规则的团
                    float r = radius;
                    if (jag > 0f)
                    {
                        float jitter = 1f + ((float)jagRng.NextDouble() - 0.5f) * 2f * jag;
                        r *= jitter;
                    }

                    v.Add(center + dir * r);
                    n.Add(dir);
                    uv.Add(new Vector2(uu, vv));
                    uv2.Add(new Vector4((center + dir * r).x, (center + dir * r).y, (center + dir * r).z, 0));
                    c.Add(new Color(1f, 1f, 1f, 1f));
                }
            }
            for (int j = 0; j < lat; j++)
            {
                for (int i = 0; i < lon; i++)
                {
                    int a = start + j * (lon + 1) + i;
                    int bb2 = a + lon + 1;
                    t.Add(a); t.Add(bb2); t.Add(a + 1);
                    t.Add(a + 1); t.Add(bb2); t.Add(bb2 + 1);
                }
            }
            b += (lat + 1) * (lon + 1);
        }
    }
}
