using System.IO;
using System.Collections.Generic;
using UnityEngine;

namespace Fengling.TerrainSystem
{
    /// <summary>
    /// 从 RAW 高度图生成分块地形网格。
    ///
    /// 设计依据（对标参考视频 + 业界做法）：
    ///   - 分块网格 + 顶点色（Polaris / Tiny Terrain 的标准做法，不用 splatmap）
    ///   - 平面着色（每个三角形一个法线，保留锐利棱角）
    ///   - 顶点色 = 高度分带 + 坡度变岩 + 像素抖动（视频里草地是两色抖动的）
    ///
    /// 约束：
    ///   - Unity 单 mesh 顶点上限 65535
    ///   - 平面着色每个四边形要 6 个顶点（不共享）
    ///   - 所以每块最多约 100x100 格（=60000 顶点）
    /// </summary>
    public static class TerrainMeshBuilder
    {
        public const string RawPath = "Assets/Resources/Terrain/skye_2km6.raw";
        const int RawRes = 1025;
        const float RawMinH = -50f;
        const float RawMaxH = 478.6f;
        const float RawSpanMeters = 2600f;   // 整张高度图覆盖 2.6km

        public struct Settings
        {
            public int GridSize;        // 总格数（200 → 200m×200m @1m）
            public int ChunkCount;      // 分块数（2 → 2×2）
            public float CellSize;      // 每格米数
            public float HeightScale;   // 高度缩放
            public Vector2 WorldOrigin; // 世界坐标原点（区域左下角）
        }

        public static Settings Default => new Settings
        {
            GridSize = 200,
            ChunkCount = 2,
            CellSize = 1f,
            HeightScale = 1f,
            WorldOrigin = new Vector2(-100f, -100f),
        };

        private static float[] _heights;   // 原始高度（米）
        private static int _res;
        private static Vector2 _rawMin;    // 高度图在世界里的最小角
        private static float _regionMin, _regionMax;

        /// <summary>高度平滑半径（格）。去掉 1m 采样造成的尖刺和垂直面。</summary>
        public static int SmoothRadius = 4;

        /// <summary>高度压平系数：0=完全压平，1=原始起伏。参考视频的地形很平缓，所以取小值。</summary>
        public static float HeightCompression = 0.35f;

        private static float _regionMean;

        /// <summary>平滑采样：对邻域取平均，消除尖刺</summary>
        public static float SampleHeightSmooth(float wx, float wz)
        {
            float sum = 0f;
            int n = 0;
            for (int j = -SmoothRadius; j <= SmoothRadius; j++)
            {
                for (int i = -SmoothRadius; i <= SmoothRadius; i++)
                {
                    sum += SampleHeight(wx + i, wz + j);
                    n++;
                }
            }
            return sum / n;
        }

        /// <summary>加载 RAW（只加载一次）</summary>
        public static bool LoadRaw()
        {
            if (_heights != null) return true;

            if (!File.Exists(RawPath))
            {
                Debug.LogError("[Terrain] 找不到 RAW: " + RawPath);
                return false;
            }

            byte[] raw = File.ReadAllBytes(RawPath);
            if (raw.Length != RawRes * RawRes * 2)
            {
                Debug.LogError("[Terrain] RAW 大小不对: " + raw.Length);
                return false;
            }

            _res = RawRes;
            _heights = new float[_res * _res];
            for (int i = 0; i < _heights.Length; i++)
            {
                ushort v = (ushort)(raw[i * 2] | (raw[i * 2 + 1] << 8));
                float t = v / 65535f;
                _heights[i] = Mathf.Lerp(RawMinH, RawMaxH, t);
            }

            // 高度图居中在世界原点，覆盖 2.6km
            _rawMin = new Vector2(-RawSpanMeters * 0.5f, -RawSpanMeters * 0.5f);
            return true;
        }

        /// <summary>双线性采样世界坐标处的高度（米）</summary>
        public static float SampleHeight(float worldX, float worldZ)
        {
            if (_heights == null && !LoadRaw()) return 0f;

            float u = (worldX - _rawMin.x) / RawSpanMeters * (_res - 1);
            float v = (worldZ - _rawMin.y) / RawSpanMeters * (_res - 1);

            if (u < 0 || v < 0 || u >= _res - 1 || v >= _res - 1)
                return RawMinH;

            int x0 = Mathf.FloorToInt(u), z0 = Mathf.FloorToInt(v);
            float fx = u - x0, fz = v - z0;
            int x1 = Mathf.Min(x0 + 1, _res - 1), z1 = Mathf.Min(z0 + 1, _res - 1);

            float h00 = _heights[z0 * _res + x0];
            float h10 = _heights[z0 * _res + x1];
            float h01 = _heights[z1 * _res + x0];
            float h11 = _heights[z1 * _res + x1];

            return Mathf.Lerp(Mathf.Lerp(h00, h10, fx), Mathf.Lerp(h01, h11, fx), fz);
        }

        // ---------- 顶点色 ----------

        // 调色板（取自视频分析：有限、低饱和、偏冷绿）
        static readonly Color Sand   = new Color(0.788f, 0.702f, 0.576f);
        static readonly Color Grass  = new Color(0.498f, 0.627f, 0.353f);
        static readonly Color Grass2 = new Color(0.588f, 0.702f, 0.420f);
        static readonly Color Forest = new Color(0.247f, 0.357f, 0.227f);
        static readonly Color Rock   = new Color(0.431f, 0.416f, 0.478f);
        static readonly Color Deep   = new Color(0.310f, 0.400f, 0.290f);

        /// <summary>按高度 + 坡度 + 抖动算顶点色</summary>
        public static Color ComputeColor(float worldX, float worldZ, float height, float slope, float regionMin, float regionMax)
        {
            // 高度分带：按"区域内的相对高度"算，而不是绝对海拔。
            // 否则拿到一片 50m 高的地，全部会判成沙滩。
            float top = Mathf.Max(regionMax, regionMin + 8f);
            float t01 = Mathf.Clamp01((height - regionMin) / Mathf.Max(1f, top - regionMin));

            Color c = Sand;
            c = Color.Lerp(c, Grass,  Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.06f, 0.18f, t01)));
            c = Color.Lerp(c, Grass2, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.35f, t01)) * 0.65f);
            c = Color.Lerp(c, Forest, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.62f, t01)));
            c = Color.Lerp(c, Rock,   Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.72f, 0.95f, t01)));

            // 坡度变岩（陡的地方露岩）
            c = Color.Lerp(c, Rock, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.75f, slope)));

            // 水下压暗
            if (height < 0f)
                c = Color.Lerp(c, Deep, Mathf.InverseLerp(0f, -20f, height));

            // 像素抖动：两色交替，模拟视频里草地的 dithering
            float dither = Dither(worldX, worldZ);
            c *= Mathf.Lerp(0.90f, 1.10f, dither);

            return c;
        }

        // 4x4 Bayer 抖动的简化版：按 1m 格子做两色交替
        static float Dither(float x, float z)
        {
            int ix = Mathf.FloorToInt(x);
            int iz = Mathf.FloorToInt(z);
            int h = (ix * 73856093) ^ (iz * 19349663);
            h = (h ^ (h >> 13)) & 0x7fffffff;
            return (h % 16) / 15f;
        }

        // ---------- 自动选区 ----------

        /// <summary>
        /// 在世界里找一块"最有看头"的 200m 区域：要有海滩（低处）也要有坡（高处）。
        /// 打分规则：低处越接近 0m 越好，高处越高越好，都满足才算好区域。
        /// </summary>
        public static Vector2 FindBestRegion(float sizeMeters, int step = 8)
        {
            if (!LoadRaw()) return new Vector2(-100f, -100f);

            float half = RawSpanMeters * 0.5f - sizeMeters * 0.5f - 10f;
            float halfSize = sizeMeters * 0.5f;
            Vector2 best = new Vector2(-halfSize, -halfSize);   // 返回左下角，与 Build 的约定一致
            float bestScore = float.MinValue;

            for (float cz = -half; cz <= half; cz += 40f)
            {
                for (float cx = -half; cx <= half; cx += 40f)
                {
                    float lo = float.MaxValue, hi = float.MinValue;
                    float sum = 0f, sum2 = 0f;
                    int n = 0;
                    for (int j = -2; j <= 2; j++)
                    {
                        for (int i = -2; i <= 2; i++)
                        {
                            float h = SampleHeight(cx + i * sizeMeters * 0.2f, cz + j * sizeMeters * 0.2f);
                            if (h < lo) lo = h;
                            if (h > hi) hi = h;
                            sum += h;
                            sum2 += h * h;
                            n++;
                        }
                    }
                    float mean = sum / n;
                    float variance = Mathf.Max(0f, sum2 / n - mean * mean);
                    float std = Mathf.Sqrt(variance);

                    // 硬性要求：要有海岸线（低处到 0m 附近），也要有缓丘（高处 40~120m），
                    // 起伏不能太大（排除悬崖），也不能太小（排除纯平地/纯浅海）
                    if (lo > 3f || lo < -12f) continue;   // 最低处要在海岸线附近
                    if (hi < 40f) continue;              // 要有坡
                    if (hi > 130f) continue;             // 别太高
                    if (std < 8f || std > 22f) continue; // 起伏适中

                    float seaCloseness = 1f - Mathf.Abs(lo - 2f) / 12f;
                    float relif = Mathf.Min(hi, 80f) / 80f;
                    float flatness = 1f - Mathf.Clamp01(std / 22f);   // 越平越好
                    float score = seaCloseness * 1.5f + relif + flatness * 3f;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = new Vector2(cx - halfSize, cz - halfSize);   // 中心 → 左下角
                    }
                }
            }

            Debug.Log(string.Format("[Terrain] 选区 ({0:F0}, {1:F0}) 得分 {2:F2}", best.x, best.y, bestScore));
            return best;
        }

        // ---------- 网格生成 ----------

        /// <summary>生成所有块的网格（每个元素是一块的 Mesh）</summary>
        public static List<Mesh> Build(Settings s, out List<Vector3> origins)
        {
            var meshes = new List<Mesh>();
            origins = new List<Vector3>();

            if (!LoadRaw()) return meshes;

            // 先算这块区域的真实高度范围，供分带使用
            _regionMin = float.MaxValue; _regionMax = float.MinValue;
            for (int j = 0; j <= s.GridSize; j += 4)
            {
                for (int i = 0; i <= s.GridSize; i += 4)
                {
                    float h = SampleHeight(s.WorldOrigin.x + i * s.CellSize, s.WorldOrigin.y + j * s.CellSize);
                    if (h < _regionMin) _regionMin = h;
                    if (h > _regionMax) _regionMax = h;
                }
            }
                        Debug.Log(string.Format("[Terrain] Build origin=({0},{1}) grid={2} cell={3}",
                s.WorldOrigin.x, s.WorldOrigin.y, s.GridSize, s.CellSize));
            Debug.Log(string.Format("[Terrain] 颜色分带范围 {0:F1} ~ {1:F1} m", _regionMin, _regionMax));
            _regionMean = (_regionMin + _regionMax) * 0.5f;

            int chunkCells = s.GridSize / s.ChunkCount;
            if (chunkCells * s.ChunkCount != s.GridSize)
            {
                Debug.LogError("[Terrain] GridSize 必须能被 ChunkCount 整除");
                return meshes;
            }

            for (int cz = 0; cz < s.ChunkCount; cz++)
            {
                for (int cx = 0; cx < s.ChunkCount; cx++)
                {
                    var mesh = BuildChunk(s, cx, cz, chunkCells);
                    if (mesh != null)
                    {
                        meshes.Add(mesh);
                        origins.Add(new Vector3(
                            s.WorldOrigin.x + cx * chunkCells * s.CellSize, 0f,
                            s.WorldOrigin.y + cz * chunkCells * s.CellSize));
                    }
                }
            }

            Debug.Log(string.Format("[Terrain] 生成 {0} 块，每块 {1}x{1} 格，共 {2} 三角形",
                meshes.Count, chunkCells, meshes.Count * chunkCells * chunkCells * 2));
            return meshes;
        }

        static Mesh BuildChunk(Settings s, int cx, int cz, int cells)
        {
            int vertsPerChunk = cells * cells * 6;   // 每格 2 三角形 = 6 顶点（平面着色不共享）
            if (vertsPerChunk > 65000)
            {
                Debug.LogError("[Terrain] 块太大，顶点超限: " + vertsPerChunk);
                return null;
            }

            var vertices = new Vector3[vertsPerChunk];
            var normals = new Vector3[vertsPerChunk];
            var colors = new Color[vertsPerChunk];
            var triangles = new int[vertsPerChunk];

            // 先算这块所有格点的位置/高度（共享角点高度，保证块内无缝）
            int n = cells + 1;
            var pts = new Vector3[n * n];
            for (int j = 0; j < n; j++)
            {
                for (int i = 0; i < n; i++)
                {
                    float wx = s.WorldOrigin.x + (cx * cells + i) * s.CellSize;
                    float wz = s.WorldOrigin.y + (cz * cells + j) * s.CellSize;
                    float h = SampleHeightSmooth(wx, wz);
                    h = _regionMean + (h - _regionMean) * HeightCompression;  // 压平
                    h *= s.HeightScale;
                    pts[j * n + i] = new Vector3(wx, h, wz);
                }
            }

            int vi = 0;
            for (int j = 0; j < cells; j++)
            {
                for (int i = 0; i < cells; i++)
                {
                    Vector3 p00 = pts[j * n + i];
                    Vector3 p10 = pts[j * n + i + 1];
                    Vector3 p01 = pts[(j + 1) * n + i];
                    Vector3 p11 = pts[(j + 1) * n + i + 1];

                    EmitQuad(vertices, normals, colors, triangles, ref vi, p00, p10, p01, p11);
                }
            }

            var mesh = new Mesh { name = string.Format("TerrainChunk_{0}_{1}", cx, cz) };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.colors = colors;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        static void EmitQuad(Vector3[] vs, Vector3[] ns, Color[] cs, int[] ts, ref int vi,
                             Vector3 p00, Vector3 p10, Vector3 p01, Vector3 p11)
        {
            // 两个三角形，各自算法线（平面着色）
            EmitTri(vs, ns, cs, ts, ref vi, p00, p10, p11);
            EmitTri(vs, ns, cs, ts, ref vi, p00, p11, p01);
        }

        static void EmitTri(Vector3[] vs, Vector3[] ns, Color[] cs, int[] ts, ref int vi,
                            Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 nrm = Vector3.Cross(b - a, c - a).normalized;

            // 坡度：1 - |N·up|，用来变岩
            float slope = 1f - Mathf.Abs(Vector3.Dot(nrm, Vector3.up));

            int start = vi;
            for (int k = 0; k < 3; k++)
            {
                Vector3 p = k == 0 ? a : (k == 1 ? b : c);
                vs[vi] = p;
                ns[vi] = nrm;
                cs[vi] = ComputeColor(p.x, p.z, p.y, slope, _regionMin, _regionMax);
                ts[vi] = vi;
                vi++;
            }
            // 保证正面朝向（Unity 顺时针为正面，这里按叉积结果判断）
            if (Vector3.Dot(nrm, Vector3.up) < 0f)
            {
                int t = ts[start + 1]; ts[start + 1] = ts[start + 2]; ts[start + 2] = t;
            }
        }
    }
}
