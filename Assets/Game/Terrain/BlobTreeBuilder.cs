using System.Collections.Generic;
using UnityEngine;

namespace Fengling.TerrainSystem
{
    /// <summary>
    /// 程序化生成"视频风格"的树。
    ///
    /// 依据（来自参考视频帧分析）：
    ///   - 树干：细、直、深棕
    ///   - 树冠：3~6 个圆润团块堆叠，不是一个整球
    ///   - 颜色：底部深绿 → 顶部亮绿
    ///   - 点缀：红色浆果
    ///   - 影子：深绿色有角度的暗斑（用扁平投影体）
    ///
    /// 为什么不用资产：glTF 与自建 SRP 不兼容；而视频的树本来就是团块低模，
    /// 代码生成反而更贴近，还能控制风格。
    /// </summary>
    public static class BlobTreeBuilder
    {
        public struct Part
        {
            public Mesh mesh;
            public Vector3 offset;
            public Color color;
        }

        /// <summary>生成一棵树的所有部件（树干 + 若干树冠团块 + 浆果）</summary>
        public static List<Part> Build(int seed, float height)
        {
            var rng = new System.Random(seed);
            var parts = new List<Part>();

            float trunkH = height * 0.45f;
            float trunkR = Mathf.Max(0.06f, height * 0.022f);

            // ---- 树干
            parts.Add(new Part
            {
                mesh = BuildCylinder(trunkR, trunkH, 6),
                offset = Vector3.zero,
                color = new Color(0.26f, 0.18f, 0.13f),
            });

            // ---- 树冠：3~6 个团块
            int blobs = 3 + rng.Next(4);
            float canopyBase = trunkH * 0.85f;
            float canopyH = height - canopyBase;

            float cR = height * 0.30f;
            for (int i = 0; i < blobs; i++)
            {
                float t = blobs == 1 ? 0.5f : i / (float)(blobs - 1);

                // 沿一条稍微弯曲的轴堆叠，越往上越小
                float y = canopyBase + t * canopyH * 0.75f;
                float radius = cR * Mathf.Lerp(1.0f, 0.55f, t);

                // 横向随机偏移，形成不规则轮廓
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                float off = cR * 0.35f * (float)rng.NextDouble();
                var center = new Vector3(Mathf.Cos(ang) * off, y, Mathf.Sin(ang) * off);

                // 颜色：底部深绿、顶部亮绿
                float lum = Mathf.Lerp(0.0f, 1.0f, t);
                var col = Color.Lerp(
                    new Color(0.18f, 0.32f, 0.16f),
                    new Color(0.52f, 0.72f, 0.34f), lum);

                parts.Add(new Part
                {
                    mesh = BuildBlob(radius, 7, 5),
                    offset = center,
                    color = col,
                });
            }

            // ---- 浆果（小红点，用极小的球）
            int berries = 3 + rng.Next(5);
            for (int i = 0; i < berries; i++)
            {
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                float t = (float)rng.NextDouble();
                float y = canopyBase + t * canopyH * 0.7f;
                float r = cR * 0.95f;
                parts.Add(new Part
                {
                    mesh = BuildBlob(height * 0.022f, 4, 3),
                    offset = new Vector3(Mathf.Cos(ang) * r, y, Mathf.Sin(ang) * r),
                    color = new Color(0.72f, 0.14f, 0.16f),
                });
            }

            return parts;
        }

        /// <summary>把一棵树合并成一个网格（顶点着色，便于一次绘制）</summary>
        public static Mesh Merge(List<Part> parts)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var colors = new List<Color>();
            var tris = new List<int>();

            foreach (var p in parts)
            {
                int baseIdx = verts.Count;
                var mv = p.mesh.vertices;
                var mn = p.mesh.normals;
                var mt = p.mesh.triangles;

                for (int i = 0; i < mv.Length; i++)
                {
                    verts.Add(mv[i] + p.offset);
                    normals.Add(mn[i]);
                    colors.Add(p.color);
                }
                for (int i = 0; i < mt.Length; i++) tris.Add(baseIdx + mt[i]);
            }

            var m = new Mesh();
            m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(verts);
            m.SetNormals(normals);
            m.SetColors(colors);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        // ---------- 基础体 ----------

        static Mesh BuildCylinder(float radius, float height, int sides)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var tris = new List<int>();

            for (int i = 0; i < sides; i++)
            {
                float a = i / (float)sides * Mathf.PI * 2f;
                float x = Mathf.Cos(a) * radius;
                float z = Mathf.Sin(a) * radius;
                verts.Add(new Vector3(x, 0f, z));
                verts.Add(new Vector3(x, height, z));
                normals.Add(new Vector3(x, 0f, z).normalized);
                normals.Add(new Vector3(x, 0f, z).normalized);
            }
            for (int i = 0; i < sides; i++)
            {
                int a = i * 2;
                int b = ((i + 1) % sides) * 2;
                tris.Add(a); tris.Add(b); tris.Add(a + 1);
                tris.Add(b); tris.Add(b + 1); tris.Add(a + 1);
            }

            var m = new Mesh();
            m.SetVertices(verts);
            m.SetNormals(normals);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>低多边形球（团块）：经纬分段很少，保留棱面</summary>
        static Mesh BuildBlob(float radius, int lon, int lat)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var tris = new List<int>();

            for (int j = 0; j <= lat; j++)
            {
                float v = j / (float)lat;
                float phi = v * Mathf.PI;
                for (int i = 0; i <= lon; i++)
                {
                    float u = i / (float)lon;
                    float theta = u * Mathf.PI * 2f;
                    var n = new Vector3(
                        Mathf.Sin(phi) * Mathf.Cos(theta),
                        Mathf.Cos(phi),
                        Mathf.Sin(phi) * Mathf.Sin(theta));
                    verts.Add(n * radius);
                    normals.Add(n);
                }
            }
            for (int j = 0; j < lat; j++)
            {
                for (int i = 0; i < lon; i++)
                {
                    int a = j * (lon + 1) + i;
                    int b = a + lon + 1;
                    tris.Add(a); tris.Add(b); tris.Add(a + 1);
                    tris.Add(a + 1); tris.Add(b); tris.Add(b + 1);
                }
            }

            var m = new Mesh();
            m.SetVertices(verts);
            m.SetNormals(normals);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}
