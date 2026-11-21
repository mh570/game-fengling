using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace T3ssDemo
{
    /// <summary>
    /// GodRay 面片生成器。
    /// 做法（视频 03 的描述）：绘制「平行于光线、垂直于视角」的薄片面片，叠加。
    /// 我们把面片放在树冠之间的空地上，从地面往上延伸，加法叠加成光柱。
    /// </summary>
    public static class GodRayBuilder
    {
        public struct Options
        {
            public int ShaftCount;      // 光柱数量
            public float AreaRadius;    // 撒布半径
            public float Height;        // 光柱高度
            public float Width;         // 光柱宽度
            public int Seed;
        }

        public static Mesh Build(Options opt, out int quadCount)
        {
            var rng = new System.Random(opt.Seed);
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var colors = new List<Color>();
            var tris = new List<int>();
            int b = 0;

            quadCount = opt.ShaftCount;
            for (int i = 0; i < opt.ShaftCount; i++)
            {
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                float rad = Mathf.Sqrt((float)rng.NextDouble()) * opt.AreaRadius;
                var basePos = new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad);

                float w = opt.Width * (0.6f + (float)rng.NextDouble() * 0.9f);
                float h = opt.Height * (0.55f + (float)rng.NextDouble() * 0.7f);
                float bright = 0.35f + (float)rng.NextDouble() * 0.65f;

                // 面片的局部坐标：x = 横向（shader 里换成相机右方向），y = 高度
                verts.Add(new Vector3(-w * 0.5f, 0f, 0f));
                verts.Add(new Vector3( w * 0.5f, 0f, 0f));
                verts.Add(new Vector3(-w * 0.5f, h,  0f));
                verts.Add(new Vector3( w * 0.5f, h,  0f));

                uvs.Add(new Vector2(0f, 0f));
                uvs.Add(new Vector2(1f, 0f));
                uvs.Add(new Vector2(0f, 1f));
                uvs.Add(new Vector2(1f, 1f));

                var c = new Color(bright, bright, bright, 1f);
                colors.Add(c); colors.Add(c); colors.Add(c); colors.Add(c);

                tris.Add(b + 0); tris.Add(b + 2); tris.Add(b + 1);
                tris.Add(b + 2); tris.Add(b + 3); tris.Add(b + 1);
                b += 4;
            }

            var mesh = new Mesh();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            // 注意：x/y 是 shader 里用的偏移量，真正的世界位置靠后续 Transform 平移
            // 这里用一个技巧：顶点存 (横向, 高度, 0)，shader 里 offsets 后加上物体位置
            // 为简化，我们把每根光柱做成独立 GameObject（位置精度更好）
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>生成一批光柱 GameObject（每根一个），返回根节点。</summary>
        public static GameObject Spawn(Options opt, Material mat, Transform parent, out int count)
        {
            var root = new GameObject("GodRays");
            if (parent != null) root.transform.SetParent(parent, false);

            var rng = new System.Random(opt.Seed);
            count = opt.ShaftCount;
            for (int i = 0; i < opt.ShaftCount; i++)
            {
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                float rad = Mathf.Sqrt((float)rng.NextDouble()) * opt.AreaRadius;
                var basePos = new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad);

                float w = opt.Width * (0.6f + (float)rng.NextDouble() * 0.9f);
                float h = opt.Height * (0.55f + (float)rng.NextDouble() * 0.7f);
                float bright = 0.35f + (float)rng.NextDouble() * 0.65f;

                var mesh = new Mesh();
                mesh.SetVertices(new List<Vector3>
                {
                    new Vector3(-w * 0.5f, 0f, 0f),
                    new Vector3( w * 0.5f, 0f, 0f),
                    new Vector3(-w * 0.5f, h, 0f),
                    new Vector3( w * 0.5f, h, 0f),
                });
                mesh.SetUVs(0, new List<Vector2>
                {
                    new Vector2(0f, 0f), new Vector2(1f, 0f),
                    new Vector2(0f, 1f), new Vector2(1f, 1f),
                });
                var c = new Color(bright, bright, bright, 1f);
                mesh.SetColors(new List<Color> { c, c, c, c });
                mesh.SetTriangles(new List<int> { 0, 2, 1, 2, 3, 1 }, 0);
                mesh.RecalculateBounds();

                var go = new GameObject("Shaft_" + i);
                go.transform.SetParent(root.transform, false);
                go.transform.position = basePos;
                go.transform.rotation = Quaternion.Euler(-28f, (float)rng.NextDouble() * 360f, 0f);   // 沿光线倾斜
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            }
            return root;
        }
    }
}
