using System.Collections.Generic;
using UnityEngine;

namespace Fengling.TerrainSystem
{
    /// <summary>
    /// 把高度图切成块生成网格。
    ///
    /// 分两种模式：
    ///   - **同分辨率（LOD0）**：相邻块共享完全相同的边缘顶点（因为采样函数是确定的），
    ///     天然水密，不需要边框，不会有洞。
    ///   - **有 LOD 差异**：需要用边框扩展（Sebastian Lague 的做法）给高低级块之间缝合。
    ///
    /// 我们目前只用 LOD0，所以走第一种，简单且不会出错。
    /// </summary>
    public static class TerrainChunkBuilder
    {
        public struct Chunk
        {
            public Mesh mesh;
            public Vector3 position;
        }

        public static List<Chunk> Build(Vector2 origin, int gridSize, int chunkCount, float cellSize,
                                        float heightCompression)
        {
            var result = new List<Chunk>();
            if (!TerrainMeshBuilder.LoadRaw()) return result;

            int cells = gridSize / chunkCount;

            // 区域高度范围（决定颜色分带）
            float rMin = float.MaxValue, rMax = float.MinValue;
            for (int j = 0; j <= gridSize; j += 2)
            {
                for (int i = 0; i <= gridSize; i += 2)
                {
                    float h = TerrainMeshBuilder.SampleHeightSmooth(origin.x + i * cellSize, origin.y + j * cellSize);
                    if (h < rMin) rMin = h;
                    if (h > rMax) rMax = h;
                }
            }
            float mean = (rMin + rMax) * 0.5f;
            float lo = mean + (rMin - mean) * heightCompression;
            float hi = mean + (rMax - mean) * heightCompression;
            Debug.Log(string.Format("[Chunk] 分带 {0:F1} ~ {1:F1} m（原始 {2:F1} ~ {3:F1}）", lo, hi, rMin, rMax));

            for (int cz = 0; cz < chunkCount; cz++)
            {
                for (int cx = 0; cx < chunkCount; cx++)
                {
                    var mesh = BuildChunk(origin, cx, cz, cells, cellSize, mean, heightCompression, lo, hi);
                    if (mesh == null) continue;

                    float centerX = origin.x + (cx * cells + cells * 0.5f) * cellSize;
                    float centerZ = origin.y + (cz * cells + cells * 0.5f) * cellSize;

                    result.Add(new Chunk
                    {
                        mesh = mesh,
                        position = new Vector3(centerX, 0f, centerZ),
                    });
                }
            }

            Debug.Log(string.Format("[Chunk] 生成 {0} 块，每块 {1}x{1} 格", result.Count, cells));
            return result;
        }

        static Mesh BuildChunk(Vector2 origin, int cx, int cz, int cells, float cellSize,
                               float mean, float compression, float lo, float hi)
        {
            int n = cells + 1;                 // 每边顶点数
            int quadCount = cells * cells;

            // 平面着色：每格 6 个顶点
            int vertCount = quadCount * 6;
            if (vertCount > 65000)
            {
                Debug.LogError("[Chunk] 顶点超 65k: " + vertCount + "（请增大 ChunkCount）");
                return null;
            }

            var vertices = new Vector3[vertCount];
            var normals = new Vector3[vertCount];
            var colors = new Color[vertCount];
            var triangles = new int[vertCount];

            // 先算格点世界坐标（块内共享角点，块间也共享，因为采样确定）
            var pts = new Vector3[n * n];
            for (int j = 0; j < n; j++)
            {
                for (int i = 0; i < n; i++)
                {
                    float wx = origin.x + (cx * cells + i) * cellSize;
                    float wz = origin.y + (cz * cells + j) * cellSize;
                    float h = TerrainMeshBuilder.SampleHeightSmooth(wx, wz);
                    // 只压平陆地，保留海平面以下（否则海滩/海会被压没）
                    if (h > 0f) h = mean + (h - mean) * compression;
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

                    EmitTri(vertices, normals, colors, triangles, ref vi, p00, p11, p10, lo, hi);
                    EmitTri(vertices, normals, colors, triangles, ref vi, p00, p01, p11, lo, hi);
                }
            }

            var mesh = new Mesh();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.colors = colors;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        static void EmitTri(Vector3[] vs, Vector3[] ns, Color[] cs, int[] ts, ref int vi,
                            Vector3 a, Vector3 b, Vector3 c, float lo, float hi)
        {
            Vector3 nrm = Vector3.Cross(b - a, c - a).normalized;
            if (nrm.y < 0f) nrm = -nrm;   // 保证朝上

            float slope = 1f - Mathf.Abs(nrm.y);

            int start = vi;
            for (int k = 0; k < 3; k++)
            {
                Vector3 p = k == 0 ? a : (k == 1 ? b : c);
                vs[vi] = p;
                ns[vi] = nrm;
                cs[vi] = TerrainColors.Compute(p.x, p.z, p.y, slope, lo, hi);
                ts[vi] = vi;
                vi++;
            }
        }
    }
}
