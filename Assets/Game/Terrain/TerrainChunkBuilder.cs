using System.Collections.Generic;
using UnityEngine;

namespace Fengling.TerrainSystem
{
    /// <summary>
    /// 把 Skye 高度图切成带"边框"的块，交给 TerrainMeshGenerator 生成网格。
    ///
    /// 为什么要有边框：TerrainMeshGenerator 需要 (块大小 + 2) 的高度图，
    /// 多出来的一圈用来算缝合法线，块与块之间才不会出现裂缝。
    /// </summary>
    public static class TerrainChunkBuilder
    {
        public struct Chunk
        {
            public Mesh mesh;
            public Vector3 position;   // 该块的世界中心
            public int lod;
        }

        /// <summary>生成所有块</summary>
        public static List<Chunk> Build(Vector2 origin, int gridSize, int chunkCount, float cellSize,
                                        float heightCompression, int lod)
        {
            var result = new List<Chunk>();
            if (!TerrainMeshBuilder.LoadRaw()) return result;

            int cells = gridSize / chunkCount;
            var settings = new MeshSettings { meshScale = cellSize, useFlatShading = true };

            // 区域高度范围（分带用）
            float rMin = float.MaxValue, rMax = float.MinValue;
            for (int j = 0; j <= gridSize; j += 4)
            {
                for (int i = 0; i <= gridSize; i += 4)
                {
                    float h = TerrainMeshBuilder.SampleHeightSmooth(origin.x + i * cellSize, origin.y + j * cellSize);
                    if (h < rMin) rMin = h;
                    if (h > rMax) rMax = h;
                }
            }
            float mean = (rMin + rMax) * 0.5f;
            float lo = mean + (rMin - mean) * heightCompression;
            float hi = mean + (rMax - mean) * heightCompression;
            Debug.Log(string.Format("[Chunk] 分带范围 {0:F1} ~ {1:F1} m（原始 {2:F1} ~ {3:F1}，压平 {4:P0}）",
                lo, hi, rMin, rMax, heightCompression));

            int increment = (lod == 0) ? 1 : lod * 2;
            int borderedSize = cells + 2 * increment + 1;

            for (int cz = 0; cz < chunkCount; cz++)
            {
                for (int cx = 0; cx < chunkCount; cx++)
                {
                    var heights = new float[borderedSize, borderedSize];
                    for (int j = 0; j < borderedSize; j++)
                    {
                        for (int i = 0; i < borderedSize; i++)
                        {
                            // 边框往外扩一圈：块内坐标 (i-1, j-1) 相对块起点
                            float wx = origin.x + (cx * cells + (i - increment)) * cellSize;
                            float wz = origin.y + (cz * cells + (j - increment)) * cellSize;
                            float h = TerrainMeshBuilder.SampleHeightSmooth(wx, wz);
                            h = mean + (h - mean) * heightCompression;
                            heights[i, j] = h;
                        }
                    }

                    var md = TerrainMeshGenerator.Generate(heights, settings, lod, lo, hi);
                    var mesh = md.CreateMesh();
                    mesh.name = string.Format("TerrainChunk_{0}_{1}_LOD{2}", cx, cz, lod);

                    // 块中心的世界坐标
                    float centerX = origin.x + (cx * cells + cells * 0.5f) * cellSize;
                    float centerZ = origin.y + (cz * cells + cells * 0.5f) * cellSize;

                    result.Add(new Chunk
                    {
                        mesh = mesh,
                        position = new Vector3(centerX, 0f, centerZ),
                        lod = lod,
                    });
                }
            }

            Debug.Log(string.Format("[Chunk] 生成 {0} 块，{1}x{1} 格，LOD{2}", result.Count, cells, lod));
            return result;
        }
    }
}
