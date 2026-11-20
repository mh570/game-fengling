using UnityEngine;

namespace Fengling.Time
{
    /// <summary>
    /// Mesh 雨：不依赖 ParticleSystem，用一批相机朝向的细长 quad + 顶点着色器下落。
    /// 透明 Mesh 已在探针中验证可渲染。
    /// </summary>
    [AddComponentMenu("Fengling/Time/Mesh Rain")]
    public sealed class MeshRain : MonoBehaviour
    {
        public WeatherSystem weather;
        public int count = 4000;
        public float area = 60f;

        private Mesh _mesh;
        private MeshRenderer _renderer;
        private Material _material;
        private Camera _camera;

        private void Awake()
        {
            if (weather == null) weather = FindFirstObjectByType<WeatherSystem>();

            BuildMesh();
            var shader = Shader.Find("Fengling/Mesh Rain");
            if (shader == null) { enabled = false; return; }
            _material = new Material(shader) { name = "MeshRain (instance)" };
            var mr = gameObject.AddComponent<MeshFilter>();
            mr.sharedMesh = _mesh;
            _renderer = gameObject.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = _material;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _camera = Camera.main;
        }

        private void BuildMesh()
        {
            var verts = new Vector3[count * 4];
            var uvs = new Vector2[count * 4];
            var colors = new Color[count * 4];
            var tris = new int[count * 6];
            var rng = new System.Random(12345);

            for (int i = 0; i < count; i++)
            {
                float x = ((float)rng.NextDouble() - 0.5f) * area;
                float z = ((float)rng.NextDouble() - 0.5f) * area;
                float seed = (float)rng.NextDouble();
                int v = i * 4;
                for (int k = 0; k < 4; k++)
                {
                    float dx = (k == 1 || k == 2) ? 1f : 0f;
                    float dy = (k >= 2) ? 1f : 0f;
                    verts[v + k] = new Vector3(x, 0f, z);
                    uvs[v + k] = new Vector2(dx, dy);
                    colors[v + k] = new Color(seed, seed, seed, 1f);
                }
                tris[i * 6 + 0] = v + 0;
                tris[i * 6 + 1] = v + 1;
                tris[i * 6 + 2] = v + 2;
                tris[i * 6 + 3] = v + 0;
                tris[i * 6 + 4] = v + 2;
                tris[i * 6 + 5] = v + 3;
            }

            _mesh = new Mesh { name = "MeshRain" };
            _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            _mesh.vertices = verts;
            _mesh.uv = uvs;
            _mesh.colors = colors;
            _mesh.triangles = tris;
            _mesh.bounds = new Bounds(Vector3.zero, new Vector3(area, 40f, area));
        }

        private void Update()
        {
            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return;
            var p = _camera.transform.position;
            var f = _camera.transform.forward;
            transform.position = new Vector3(p.x + f.x * 15f, p.y - 10f, p.z + f.z * 15f);
            Refresh();
        }

        public void Refresh()
        {
            if (_material != null && weather != null && _renderer != null)
            {
                _material.SetFloat("_Rain", weather.rain);
                _renderer.enabled = weather.rain > 0.01f;
            }
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
            if (_mesh != null) Destroy(_mesh);
        }
    }
}
