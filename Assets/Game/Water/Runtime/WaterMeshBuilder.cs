using UnityEngine;

namespace Fengling.Water
{
    /// <summary>
    /// 生成水面网格。
    ///
    /// 为什么需要足够密的网格：波形的顶点位移在 vertex shader 里做，
    /// 网格太稀就变成大三角形在抖。200m 的水面用 128 段，每格 1.5m，
    /// 对 32m 波长的浪够用（每个波长约 21 个顶点）。
    /// </summary>
    public static class WaterMeshBuilder
    {
        public static Mesh Build(Vector2 size, int segments, string meshName = "WaterSurface")
        {
            segments = Mathf.Clamp(segments, 1, 512);
            int res = segments + 1;
            int vertCount = res * res;

            var vertices = new Vector3[vertCount];
            var uvs = new Vector2[vertCount];

            float halfX = size.x * 0.5f;
            float halfZ = size.y * 0.5f;
            float stepX = size.x / segments;
            float stepZ = size.y / segments;

            for (int zi = 0; zi < res; zi++)
            {
                float z = -halfZ + zi * stepZ;
                for (int xi = 0; xi < res; xi++)
                {
                    float x = -halfX + xi * stepX;
                    int i = zi * res + xi;
                    vertices[i] = new Vector3(x, 0f, z);
                    uvs[i] = new Vector2((float)xi / segments, (float)zi / segments);
                }
            }

            var triangles = new int[segments * segments * 6];
            int ti = 0;
            for (int zi = 0; zi < segments; zi++)
            {
                for (int xi = 0; xi < segments; xi++)
                {
                    int bl = zi * res + xi;
                    int br = bl + 1;
                    int tl = bl + res;
                    int tr = tl + 1;

                    triangles[ti++] = bl; triangles[ti++] = tl; triangles[ti++] = tr;
                    triangles[ti++] = bl; triangles[ti++] = tr; triangles[ti++] = br;
                }
            }

            var mesh = new Mesh { name = meshName };
            mesh.indexFormat = vertCount > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;

            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
