using UnityEngine;

namespace Fengling.TerrainSystem
{
    /// <summary>
    /// 地形网格生成器。
    ///
    /// **移植自**：Sebastian Lague, Procedural-Landmass-Generation, Proc Gen E19
    /// https://github.com/SebLague/Procedural-Landmass-Generation
    ///
    /// 核心价值（我们之前自己写的版本缺这些）：
    ///   1. **边缘顶点缝合**（border vertices，用负索引单独存）——块与块之间的接缝靠它消除
    ///   2. **LOD 简化增量**（meshSimplificationIncrement）——按级别跳采样
    ///   3. 平面着色（每个三角形独立顶点）
    ///
    /// 我们加的：顶点色（高度分带 + 坡度 + 抖动）。
    /// </summary>
    public static class TerrainMeshGenerator
    {
        public static MeshData Generate(float[,] heightMap, MeshSettings settings, int levelOfDetail,
                                        float regionMin, float regionMax)
        {
            int increment = (levelOfDetail == 0) ? 1 : levelOfDetail * 2;

            int borderedSize = heightMap.GetLength(0);
            int meshSize = borderedSize - 2 * increment;
            int meshSizeUnsimplified = borderedSize - 2;

            float topLeftX = (meshSizeUnsimplified - 1) / -2f;
            float topLeftZ = (meshSizeUnsimplified - 1) / 2f;

            int verticesPerLine = (meshSize - 1) / increment + 1;

            var meshData = new MeshData(verticesPerLine, settings.useFlatShading);

            var vertexIndicesMap = new int[borderedSize, borderedSize];
            int meshVertexIndex = 0;
            int borderVertexIndex = -1;

            for (int y = 0; y < borderedSize; y += increment)
            {
                for (int x = 0; x < borderedSize; x += increment)
                {
                    bool isBorderVertex = y == 0 || y == borderedSize - 1 || x == 0 || x == borderedSize - 1;
                    if (isBorderVertex)
                    {
                        vertexIndicesMap[x, y] = borderVertexIndex;
                        borderVertexIndex--;
                    }
                    else
                    {
                        vertexIndicesMap[x, y] = meshVertexIndex;
                        meshVertexIndex++;
                    }
                }
            }

            for (int y = 0; y < borderedSize; y += increment)
            {
                for (int x = 0; x < borderedSize; x += increment)
                {
                    int vertexIndex = vertexIndicesMap[x, y];
                    var percent = new Vector2((x - increment) / (float)meshSize, (y - increment) / (float)meshSize);
                    float height = heightMap[x, y];
                    var vertexPosition = new Vector3(
                        (topLeftX + percent.x * meshSizeUnsimplified) * settings.meshScale,
                        height,
                        (topLeftZ - percent.y * meshSizeUnsimplified) * settings.meshScale);

                    meshData.AddVertex(vertexPosition, percent, vertexIndex);

                    if (x < borderedSize - 1 && y < borderedSize - 1)
                    {
                        int a = vertexIndicesMap[x, y];
                        int b = vertexIndicesMap[x + increment, y];
                        int c = vertexIndicesMap[x, y + increment];
                        int d = vertexIndicesMap[x + increment, y + increment];
                        meshData.AddTriangle(a, d, c);
                        meshData.AddTriangle(d, a, b);
                    }
                    vertexIndex++;
                }
            }

            meshData.ProcessMesh();

            // 顶点色：按高度/坡度算（我们的风格化分带）
            meshData.ApplyVertexColors(regionMin, regionMax);

            return meshData;
        }
    }

    [System.Serializable]
    public struct MeshSettings
    {
        public float meshScale;
        public bool useFlatShading;
    }

    public class MeshData
    {
        Vector3[] vertices;
        int[] triangles;
        Vector2[] uvs;
        Vector3[] bakedNormals;
        Color[] colors;

        Vector3[] borderVertices;
        int[] borderTriangles;

        int triangleIndex;
        int borderTriangleIndex;

        bool useFlatShading;

        public MeshData(int verticesPerLine, bool useFlatShading)
        {
            this.useFlatShading = useFlatShading;
            vertices = new Vector3[verticesPerLine * verticesPerLine];
            uvs = new Vector2[verticesPerLine * verticesPerLine];
            triangles = new int[(verticesPerLine - 1) * (verticesPerLine - 1) * 6];
            borderVertices = new Vector3[verticesPerLine * 4 + 4];
            borderTriangles = new int[24 * verticesPerLine];
        }

        public void AddVertex(Vector3 vertexPosition, Vector2 uv, int vertexIndex)
        {
            if (vertexIndex < 0)
            {
                borderVertices[-vertexIndex - 1] = vertexPosition;
            }
            else
            {
                vertices[vertexIndex] = vertexPosition;
                uvs[vertexIndex] = uv;
            }
        }

        public void AddTriangle(int a, int b, int c)
        {
            if (a < 0 || b < 0 || c < 0)
            {
                borderTriangles[borderTriangleIndex] = a;
                borderTriangles[borderTriangleIndex + 1] = b;
                borderTriangles[borderTriangleIndex + 2] = c;
                borderTriangleIndex += 3;
            }
            else
            {
                triangles[triangleIndex] = a;
                triangles[triangleIndex + 1] = b;
                triangles[triangleIndex + 2] = c;
                triangleIndex += 3;
            }
        }

        Vector3[] CalculateNormals()
        {
            var vertexNormals = new Vector3[vertices.Length];
            int triangleCount = triangles.Length / 3;
            for (int i = 0; i < triangleCount; i++)
            {
                int ti = i * 3;
                int a = triangles[ti], b = triangles[ti + 1], c = triangles[ti + 2];
                Vector3 n = SurfaceNormalFromIndices(a, b, c);
                vertexNormals[a] += n;
                vertexNormals[b] += n;
                vertexNormals[c] += n;
            }

            int btCount = borderTriangles.Length / 3;
            for (int i = 0; i < btCount; i++)
            {
                int ti = i * 3;
                int a = borderTriangles[ti], b = borderTriangles[ti + 1], c = borderTriangles[ti + 2];
                if (a == 0 && b == 0 && c == 0) continue;
                Vector3 n = SurfaceNormalFromIndices(a, b, c);
                if (a >= 0) vertexNormals[a] += n;
                if (b >= 0) vertexNormals[b] += n;
                if (c >= 0) vertexNormals[c] += n;
            }

            for (int i = 0; i < vertexNormals.Length; i++) vertexNormals[i].Normalize();
            return vertexNormals;
        }

        Vector3 SurfaceNormalFromIndices(int indexA, int indexB, int indexC)
        {
            Vector3 pa = (indexA < 0) ? borderVertices[-indexA - 1] : vertices[indexA];
            Vector3 pb = (indexB < 0) ? borderVertices[-indexB - 1] : vertices[indexB];
            Vector3 pc = (indexC < 0) ? borderVertices[-indexC - 1] : vertices[indexC];
            return Vector3.Cross(pb - pa, pc - pa).normalized;
        }

        public void ProcessMesh()
        {
            if (useFlatShading) FlatShading();
            else BakeNormals();
        }

        void BakeNormals() { bakedNormals = CalculateNormals(); }

        void FlatShading()
        {
            var flatVerts = new Vector3[triangles.Length];
            var flatUvs = new Vector2[triangles.Length];
            for (int i = 0; i < triangles.Length; i++)
            {
                flatVerts[i] = vertices[triangles[i]];
                flatUvs[i] = uvs[triangles[i]];
                triangles[i] = i;
            }
            vertices = flatVerts;
            uvs = flatUvs;
        }

        /// <summary>顶点色：高度分带 + 坡度变岩 + 像素抖动</summary>
        public void ApplyVertexColors(float regionMin, float regionMax)
        {
            colors = new Color[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 p = vertices[i];

                // 坡度：用网格法线近似（此时已 ProcessMesh，平面着色下用三角形法线）
                float slope = 0f;
                if (bakedNormals != null && i < bakedNormals.Length)
                    slope = 1f - Mathf.Abs(Vector3.Dot(bakedNormals[i], Vector3.up));

                colors[i] = TerrainColors.Compute(p.x, p.z, p.y, slope, regionMin, regionMax);
            }
        }

        public Mesh CreateMesh()
        {
            var mesh = new Mesh();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.uv = uvs;
            if (useFlatShading) mesh.RecalculateNormals();
            else mesh.normals = bakedNormals;
            if (colors != null) mesh.colors = colors;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
