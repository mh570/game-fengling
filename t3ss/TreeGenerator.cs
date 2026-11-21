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
            int leaves = 6 + rng.Next(4);
            for (int k = 0; k < leaves; k++)
            {
                float cx = size * 0.5f + (float)(rng.NextDouble() - 0.5) * size * 0.62f;
                float cy = size * 0.5f + (float)(rng.NextDouble() - 0.5) * size * 0.62f;
                float r = size * (0.10f + (float)rng.NextDouble() * 0.09f);
                float shade = 0.72f + (float)rng.NextDouble() * 0.28f;

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        if (d <= r)
                        {
                            // 边缘略暗，做出层次
                            float edge = Mathf.Clamp01(1f - (d / r) * 0.45f);
                            var c = new Color(shade * edge, shade * edge, shade * edge, 1f);
                            int idx = y * size + x;
                            if (px[idx].a < 0.5f || c.r < px[idx].r) px[idx] = c;
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

        public static Mesh Build(Options opt, out int shellVerts, out int leafVerts)
        {
            var rng = new System.Random(opt.Seed);
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var colors = new List<Color>();
            var tris = new List<int>();
            int triBase = 0;

            float H = opt.Height;
            // 形状变体：树干比例、树冠比例、粗细都按种子变化
            float trunkRatio = 0.45f + (float)rng.NextDouble() * 0.20f;   // 0.45~0.65
            float trunkH = H * trunkRatio;
            float trunkR = Mathf.Max(0.08f, H * (0.045f + (float)rng.NextDouble() * 0.018f));   // 加粗

            // ---- 1. 树干（锥形圆柱）
            AddCylinder(verts, normals, uvs, colors, tris, ref triBase,
                Vector3.zero, trunkH, trunkR, trunkR * 0.55f, 8,
                new Color(0.29f, 0.20f, 0.25f, 1f));

            // ---- 2. 分枝（2~3 根，斜向上）
            int branches = 2 + rng.Next(2);
            for (int b = 0; b < branches; b++)
            {
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                float baseY = trunkH * (0.55f + (float)rng.NextDouble() * 0.25f);
                float len = trunkH * (0.35f + (float)rng.NextDouble() * 0.2f);
                var start = new Vector3(Mathf.Cos(ang) * trunkR * 0.4f, baseY, Mathf.Sin(ang) * trunkR * 0.4f);
                var dir = new Vector3(Mathf.Cos(ang), 1.1f, Mathf.Sin(ang)).normalized;
                var end = start + dir * len;
                AddCylinderBetween(verts, normals, uvs, colors, tris, ref triBase, start, end,
                    trunkR * 0.45f, trunkR * 0.28f, 6, new Color(0.29f, 0.20f, 0.25f, 1f));
            }

            shellVerts = verts.Count;

            // ---- 3. 树冠"壳"：3 个错位球（提供体积），位置随种子变化
            float canopyR = H * (0.34f + (float)rng.NextDouble() * 0.14f);   // 0.34~0.48
            float canopyBase = trunkH * (1.08f + (float)rng.NextDouble() * 0.12f);   // 抬高，露出树干
            var shellCenters = new List<Vector4>();   // xyz + radius
            shellCenters.Add(new Vector4(0f, canopyBase + canopyR * 0.45f, 0f, canopyR * 0.60f));
            for (int k = 0; k < 2; k++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float d = canopyR * (0.20f + (float)rng.NextDouble() * 0.35f);
                float rr = canopyR * (0.33f + (float)rng.NextDouble() * 0.14f);
                shellCenters.Add(new Vector4(
                    Mathf.Cos(a) * d,
                    canopyBase + canopyR * (0.18f + (float)rng.NextDouble() * 0.50f),
                    Mathf.Sin(a) * d,
                    rr));
            }

            var shellCol = new Color(0.10f, 0.16f, 0.09f, 1f);   // 壳压暗，靠卡片表现树叶
            foreach (var sc in shellCenters)
                AddSphere(verts, normals, uvs, colors, tris, ref triBase,
                    new Vector3(sc.x, sc.y, sc.z), sc.w, 8, 6, shellCol);

            // 整体树冠中心（用于球形法线）
            var canopyCenter = new Vector3(0f, canopyBase + canopyR * 0.45f, 0f);

            // ---- 4. 叶片卡片：撒在壳表面
            var leafCenters = shellCenters;
            int leafCount = Mathf.Max(50, opt.LeafCount);
            float leafSize = Mathf.Max(0.05f, opt.LeafSize);

            for (int i = 0; i < leafCount; i++)
            {
                // 随机挑一个壳球，随机球面方向
                var sc = leafCenters[rng.Next(leafCenters.Count)];
                var center = new Vector3(sc.x, sc.y, sc.z);
                float radius = sc.w;

                float theta = (float)rng.NextDouble() * Mathf.PI * 2f;
                float phi = Mathf.Acos(2f * (float)rng.NextDouble() - 1f);
                var dir = new Vector3(
                    Mathf.Sin(phi) * Mathf.Cos(theta),
                    Mathf.Cos(phi),
                    Mathf.Sin(phi) * Mathf.Sin(theta));

                // 位置：球面上 + 随机内外偏移
                var pos = center + dir * (canopyR * (0.35f + (float)rng.NextDouble() * 0.75f));
                // 垂直向上偏置：叶片不下垂到树干以下
                float minY = canopyBase * 0.92f;
                if (pos.y < minY) pos.y = minY + (float)rng.NextDouble() * canopyR * 0.25f;

                // 朝向：完全随机
                var rot = Quaternion.Euler(
                    (float)rng.NextDouble() * 360f,
                    (float)rng.NextDouble() * 360f,
                    (float)rng.NextDouble() * 360f);

                // 颜色：越靠外越暗（模拟内部遮挡）
                // 每张卡片独立颜色：亮度 + 色相都随机，打破单色
                float tint = 0.82f + (float)rng.NextDouble() * 0.36f;
                float hueShift = (float)rng.NextDouble() * 0.12f - 0.06f;
                var lcol = new Color(
                    Mathf.Clamp01(0.55f * tint + hueShift),
                    Mathf.Clamp01(0.75f * tint),
                    Mathf.Clamp01(0.42f * tint - hueShift),
                    1f);

                float s = leafSize * (0.8f + (float)rng.NextDouble() * 0.5f);
                AddQuad(verts, normals, uvs, colors, tris, ref triBase, pos, rot, s);
            }
            leafVerts = verts.Count - shellVerts;

            // ---- 5. 球形法线重映射（只对树冠区域）
            var arrN = normals.ToArray();
            var arrV = verts.ToArray();
            for (int i = 0; i < arrV.Length; i++)
            {
                if (arrV[i].y < canopyBase * 0.9f) continue;
                var d = arrV[i] - canopyCenter;
                if (d.sqrMagnitude < 1e-6f) continue;
                arrN[i] = Vector3.Slerp(arrN[i].normalized, d.normalized, 0.35f).normalized;
            }

            var mesh = new Mesh();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(new List<Vector3>(arrN));
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // ---------------- 基础几何 ----------------

        static void AddQuad(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<Color> c,
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
                c.Add(Color.white);
            }
            t.Add(b + 0); t.Add(b + 2); t.Add(b + 1);
            t.Add(b + 2); t.Add(b + 3); t.Add(b + 1);
            b += 4;
        }

        static void AddCylinder(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<Color> c,
                                List<int> t, ref int b, Vector3 basePos, float height,
                                float rBottom, float rTop, int sides, Color col)
        {
            AddCylinderBetween(v, n, uv, c, t, ref b, basePos, basePos + Vector3.up * height,
                rBottom, rTop, sides, col);
        }

        static void AddCylinderBetween(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<Color> c,
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
                v.Add(a + dir * rA); n.Add(dir); uv.Add(new Vector2(i / (float)sides, 0)); c.Add(col);
                v.Add(bb + dir * rB); n.Add(dir); uv.Add(new Vector2(i / (float)sides, 1)); c.Add(col);
            }
            for (int i = 0; i < sides; i++)
            {
                int i0 = start + i * 2;
                int i1 = start + ((i + 1) % sides) * 2;
                t.Add(i0); t.Add(i1); t.Add(i0 + 1);
                t.Add(i1); t.Add(i1 + 1); t.Add(i0 + 1);
            }
            b += sides * 2;        }

        static void AddSphere(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<Color> c,
                              List<int> t, ref int b, Vector3 center, float radius,
                              int lon, int lat, Color col)
        {
            int start = b;
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
                    v.Add(center + dir * radius);
                    n.Add(dir);
                    uv.Add(new Vector2(uu, vv));
                    c.Add(col);
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
