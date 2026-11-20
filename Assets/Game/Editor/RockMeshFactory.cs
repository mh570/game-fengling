using UnityEditor;
using UnityEngine;

namespace Fengling.EditorTools
{
    /// <summary>生成/复用低多边形岩石 mesh，替换球体 Rock。</summary>
    public static class RockMeshFactory
    {
        private const string Path = "Assets/Game/Environment/PropRockMesh.asset";

        public static Mesh GetOrCreate()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(Path);
            if (existing != null) return existing;

            var temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var source = temp.GetComponent<MeshFilter>().sharedMesh;
            var verts = source.vertices;
            var tris = source.triangles;
            Object.DestroyImmediate(temp);

            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 dir = verts[i].normalized;
                float n = Mathf.PerlinNoise(dir.x * 3.1f + 7.3f, dir.z * 3.1f + 2.1f) - 0.5f;
                float radial = 1f + n * 0.55f;
                Vector3 p = dir * radial;
                p.y *= 0.55f;
                verts[i] = p;
            }

            var mesh = new Mesh { name = "PropRockMesh" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, Path);
            AssetDatabase.SaveAssets();
            Debug.Log("[RockMesh] created " + Path);
            return mesh;
        }
    }
}
