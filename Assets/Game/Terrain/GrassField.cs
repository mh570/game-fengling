using System.Collections.Generic;
using UnityEngine;

namespace Fengling.TerrainSystem
{
    /// <summary>
    /// GPU Instancing 草场：一次 draw call 画上万株草。
    ///
    /// 为什么必须这么做：视频里 30×30 米有几千到上万株草，
    /// 用 GameObject 一株一个会卡死。这里用 Graphics.RenderMeshInstanced 批量画。
    /// </summary>
    [ExecuteAlways]
    public class GrassField : MonoBehaviour
    {
        [Header("区域")]
        public Vector2 Center = Vector2.zero;
        public float Size = 30f;
        public float BaseHeight = 1.2f;

        [Header("密度")]
        public int Count = 12000;

        [Header("外观")]
        public float MinHeight = 0.22f;
        public float MaxHeight = 0.55f;
        public float MinWidth = 0.10f;
        public float MaxWidth = 0.20f;

        [Header("生存条件")]
        public float MinGroundHeight = 0.35f;   // 低于这个高度（水面/沙滩）不长草
        public float MaxSlope = 0.55f;

        public Material GrassMaterial;

        private Mesh _quad;
        private readonly List<Matrix4x4> _matrices = new List<Matrix4x4>();
        private RenderParams _rp;

        void Start()
        {
            Build();
        }

        public void Build()
        {
            if (GrassMaterial == null)
            {
                var sh = Shader.Find("Fengling/Grass");
                if (sh == null) { Debug.LogWarning("[Grass] 找不到 Fengling/Grass"); return; }
                GrassMaterial = new Material(sh) { name = "Mat_Grass" };
                GrassMaterial.enableInstancing = true;   // RenderMeshInstanced 必需
            }

            _quad = BuildBladeMesh();

            var rng = new System.Random(20261121);
            _matrices.Clear();

            int tries = Count * 3;
            for (int i = 0; i < tries && _matrices.Count < Count; i++)
            {
                float x = Center.x + (float)(rng.NextDouble() - 0.5) * Size;
                float z = Center.y + (float)(rng.NextDouble() - 0.5) * Size;

                // 用射线打真实地面高度（静态 TerrainCarver 配置在场景重载后会丢，不能依赖它）
                float h;
                if (!TrySampleGround(x, z, out h)) continue;
                if (h < MinGroundHeight) continue;

                float hx, hz;
                if (!TrySampleGround(x + 0.5f, z, out hx)) continue;
                if (!TrySampleGround(x, z + 0.5f, out hz)) continue;
                float slope = Mathf.Max(Mathf.Abs(hx - h), Mathf.Abs(hz - h));
                if (slope > MaxSlope) continue;

                float w = Mathf.Lerp(MinWidth, MaxWidth, (float)rng.NextDouble());
                float bh = Mathf.Lerp(MinHeight, MaxHeight, (float)rng.NextDouble());
                float yaw = (float)rng.NextDouble() * 360f;

                var pos = new Vector3(x, h, z);
                var rot = Quaternion.Euler(0f, yaw, 0f);
                var scale = new Vector3(w, bh, w);
                _matrices.Add(Matrix4x4.TRS(pos, rot, scale));
            }

            _rp = new RenderParams(GrassMaterial)
            {
                worldBounds = new Bounds(new Vector3(Center.x, BaseHeight, Center.y), Vector3.one * (Size + 10f)),
                shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
                receiveShadows = true,
            };

            Debug.Log("[Grass] 撒了 " + _matrices.Count + " 株草（一次 instance 批次）");
        }

        void Update()
        {
            if (_quad == null || _matrices.Count == 0) return;

            // 分块提交，避免单次过大
            const int batch = 1023;
            for (int i = 0; i < _matrices.Count; i += batch)
            {
                int n = Mathf.Min(batch, _matrices.Count - i);
                var slice = new Matrix4x4[n];
                _matrices.CopyTo(i, slice, 0, n);
                Graphics.RenderMeshInstanced(_rp, _quad, 0, slice);
            }
        }

        /// <summary>从上方射线打地面，返回真实高度（不依赖静态配置）</summary>
        static bool TrySampleGround(float x, float z, out float height)
        {
            const float maxY = 200f;
            if (Physics.Raycast(new Vector3(x, maxY, z), Vector3.down, out var hit, maxY * 2f))
            {
                height = hit.point.y;
                return true;
            }
            height = 0f;
            return false;
        }

        /// <summary>一根草的网格：底面在 y=0，顶在 y=1 的十字面片</summary>
        static Mesh BuildBladeMesh()
        {
            var m = new Mesh();
            m.vertices = new[]
            {
                new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f),
                new Vector3(-0.5f, 1f, 0f), new Vector3(0.5f, 1f, 0f),
                new Vector3(0f, 0f, -0.5f), new Vector3(0f, 0f, 0.5f),
                new Vector3(0f, 1f, -0.5f), new Vector3(0f, 1f, 0.5f),
            };
            m.uv = new[]
            {
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1),
            };
            m.triangles = new[] { 0, 2, 1, 2, 3, 1, 4, 6, 5, 6, 7, 5 };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
