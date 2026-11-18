using UnityEngine;

namespace Fengling.Environment
{
    /// <summary>
    /// 程序化海岸地形：沙滩 → 缓坡 → 深海。
    ///
    /// 为什么不用 Unity Terrain：
    ///   1. 我们只需要一个高度函数，Terrain 的雕花工具用不上
    ///   2. Terrain 在 HDRP 下要多配一套材质/绘制，反而麻烦
    ///   3. 程序化 mesh 面数可控（128×128 = 32k 三角形），低端机吃得消
    ///
    /// 高度模型（沿 Z 轴从陆地走向海洋）：
    ///   t = (z - shoreZ) / slopeLength      ← 0 = 岸线起点，1 = 最深处
    ///   h = lerp(landHeight, -seaDepth, smoothstep(t)) + 噪声
    ///
    /// 水面在 y = 0，所以 h = 0 的位置就是水陆交界线。
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [AddComponentMenu("Fengling/Environment/Coast Terrain")]
    public sealed class CoastTerrain : MonoBehaviour
    {
        [Header("范围")]
        [Tooltip("地形平面尺寸（米）")]
        public Vector2 size = new Vector2(200f, 200f);

        [Tooltip("网格分辨率，128 约 1.6 万顶点 / 3.2 万三角形")]
        [Range(16, 256)] public int resolution = 128;

        [Header("剖面")]
        [Tooltip("岸线起点在世界 Z 坐标的哪个位置（从这里开始向海里倾斜）")]
        public float shoreZ = 0f;

        [Tooltip("陆地最高点（相对水面，米）")]
        public float landHeight = 3f;

        [Tooltip("最深处（相对水面，米，填正数）")]
        public float seaDepth = 8f;

        [Tooltip("从岸线到最深处的水平距离（米）")]
        public float slopeLength = 60f;

        [Header("噪声")]
        [Tooltip("起伏幅度（米）。陆地起伏大，海底基本平坦")]
        [Range(0f, 3f)] public float noiseAmplitude = 0.6f;

        [Tooltip("噪声频率，越大越碎")]
        [Range(0.005f, 0.2f)] public float noiseScale = 0.04f;

        [Header("生成")]
        [Tooltip("Awake 时自动生成。关掉可以手动调 Generate()")]
        public bool generateOnAwake = true;

        private Mesh _mesh;

        /// <summary>
        /// 核心：给一个世界坐标，返回该处地面高度。
        /// 泡沫和深度色都靠这个函数的地形版本，务必和 mesh 用同一个实现。
        /// </summary>
        public float SampleHeight(float worldX, float worldZ)
        {
            float t = Mathf.Clamp01((worldZ - shoreZ) / Mathf.Max(1f, slopeLength));
            float smooth = t * t * (3f - 2f * t);              // smoothstep
            float baseHeight = Mathf.Lerp(landHeight, -seaDepth, smooth);

            // 陆地起伏明显，越往深海越平（真实海底就是这样）
            float damp = Mathf.Lerp(1f, 0.15f, smooth);
            float n = Mathf.PerlinNoise(worldX * noiseScale + 137.31f,
                                        worldZ * noiseScale + 71.97f) - 0.5f;

            return baseHeight + n * noiseAmplitude * damp;
        }

        private void Awake()
        {
            if (generateOnAwake) Generate();
        }

        /// <summary>生成网格。改了参数之后手动调用。</summary>
        public void Generate()
        {
            if (_mesh == null)
            {
                _mesh = new Mesh { name = "CoastTerrain (generated)" };
                _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }
            _mesh.Clear();

            int res = Mathf.Max(2, resolution);
            int vertCount = res * res;
            var vertices = new Vector3[vertCount];
            var uvs = new Vector2[vertCount];

            float halfX = size.x * 0.5f;
            float halfZ = size.y * 0.5f;
            float stepX = size.x / (res - 1);
            float stepZ = size.y / (res - 1);

            for (int zi = 0; zi < res; zi++)
            {
                float localZ = -halfZ + zi * stepZ;
                for (int xi = 0; xi < res; xi++)
                {
                    float localX = -halfX + xi * stepX;
                    int i = zi * res + xi;

                    // 注意：这里用的是世界坐标（物体假定在原点）
                    float worldX = transform.position.x + localX;
                    float worldZ = transform.position.z + localZ;

                    vertices[i] = new Vector3(localX, SampleHeight(worldX, worldZ), localZ);
                    uvs[i] = new Vector2((localX + halfX) / size.x, (localZ + halfZ) / size.y);
                }
            }

            var triangles = new int[(res - 1) * (res - 1) * 6];
            int ti = 0;
            for (int zi = 0; zi < res - 1; zi++)
            {
                for (int xi = 0; xi < res - 1; xi++)
                {
                    int bl = zi * res + xi;
                    int br = bl + 1;
                    int tl = bl + res;
                    int tr = tl + 1;

                    triangles[ti++] = bl; triangles[ti++] = tl; triangles[ti++] = tr;
                    triangles[ti++] = bl; triangles[ti++] = tr; triangles[ti++] = br;
                }
            }

            _mesh.vertices = vertices;
            _mesh.uv = uvs;
            _mesh.triangles = triangles;
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();

            var mf = GetComponent<MeshFilter>();
            if (mf != null) mf.sharedMesh = _mesh;
        }

        private void OnDrawGizmosSelected()
        {
            // 水面线（y=0）在岸线位置画一条提示
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.5f);
            float halfX = size.x * 0.5f;
            Gizmos.DrawLine(new Vector3(transform.position.x - halfX, 0f, shoreZ),
                            new Vector3(transform.position.x + halfX, 0f, shoreZ));
        }

        /// <summary>
        /// 把高度函数烘焙成一张纹理，供水面 shader 采样。
        ///
        /// 为什么要这么做：水面需要知道"这个像素下面是多深"，
        /// 常规做法是读深度缓冲（_CameraDepthTexture），但那是 HDRP 内部格式，
        /// 自包含 shader 里访问容易踩坑。
        /// 改成烘焙地形高度图后，shader 只要采样一张普通 2D 纹理，
        /// 完全不依赖渲染管线的深度，跨管线也可用。
        ///
        /// 纹理编码：R 通道 = 归一化高度，0 = 最低点，1 = 最高点。
        /// </summary>
        /// <param name="res">纹理分辨率，256 对本场景（200m）约 0.8m/像素，足够泡沫用</param>
        public Texture2D BuildHeightMap(int res = 256)
        {
            res = Mathf.Clamp(res, 16, 1024);

            // 高度范围：陆地最高 + 噪声余量，到深海最低 - 噪声余量
            float minH = -seaDepth - noiseAmplitude;
            float maxH = landHeight + noiseAmplitude;
            float range = Mathf.Max(0.001f, maxH - minH);

            var tex = new Texture2D(res, res, TextureFormat.RFloat, false, true)
            {
                name = "CoastTerrainHeightMap",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            var pixels = new Color[res * res];
            float halfX = size.x * 0.5f;
            float halfZ = size.y * 0.5f;

            for (int zi = 0; zi < res; zi++)
            {
                float localZ = -halfZ + (zi + 0.5f) / res * size.y;
                for (int xi = 0; xi < res; xi++)
                {
                    float localX = -halfX + (xi + 0.5f) / res * size.x;
                    float h = SampleHeight(transform.position.x + localX,
                                           transform.position.z + localZ);
                    float normalized = Mathf.InverseLerp(minH, maxH, h);
                    pixels[zi * res + xi] = new Color(normalized, 0f, 0f, 1f);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(false, false);

            _heightMapRange = new Vector2(minH, maxH);
            return tex;
        }

        private Vector2 _heightMapRange = new Vector2(-9f, 4f);

        /// <summary>高度图对应的实际高度范围（min, max），shader 用来还原真实高度。</summary>
        public Vector2 HeightMapRange => _heightMapRange;
    }
}
