using System.IO;
using UnityEditor;
using UnityEngine;

namespace Fengling.EditorTools
{
    /// <summary>
    /// 程序化生成一张带云的天空 cubemap。
    ///
    /// 为什么不用 HDRP 的体积云：
    ///   体积云在 2 核 + 核显上是灾难级开销，而且默认参数出来一片灰。
    ///   烘焙成 cubemap 后：零运行时开销，还能直接当环境反射用（水面反射就是它）。
    ///
    /// 组成：天顶→地平线的蓝白渐变 + 平面投影的 fbm 云层 + 太阳光晕。
    /// </summary>
    public static class SkyCubemapGenerator
    {
        public static Cubemap GetOrCreate(string assetPath, int size, Vector3 sunDirection)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Cubemap>(assetPath);
            if (existing != null) return existing;

            var cube = Generate(size, sunDirection);
            cube.name = Path.GetFileNameWithoutExtension(assetPath);

            Directory.CreateDirectory(Path.GetDirectoryName(assetPath) ?? "Assets");
            AssetDatabase.CreateAsset(cube, assetPath);
            return AssetDatabase.LoadAssetAtPath<Cubemap>(assetPath);
        }

        public static Cubemap Generate(int size, Vector3 sunDir)
        {
            size = Mathf.Clamp(size, 32, 1024);
            sunDir = sunDir.normalized;

            var cube = new Cubemap(size, TextureFormat.RGBAHalf, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
            };

            var pixels = new Color[size * size];

            for (int face = 0; face < 6; face++)
            {
                var cubemapFace = (CubemapFace)face;

                for (int y = 0; y < size; y++)
                {
                    float v = (y + 0.5f) / size * 2f - 1f;
                    for (int x = 0; x < size; x++)
                    {
                        float u = (x + 0.5f) / size * 2f - 1f;
                        Vector3 dir = FaceDirection(cubemapFace, u, v).normalized;
                        pixels[y * size + x] = SkyColor(dir, sunDir);
                    }
                }

                cube.SetPixels(pixels, cubemapFace);
            }

            cube.Apply(true, false);
            return cube;
        }

        private static Vector3 FaceDirection(CubemapFace face, float u, float v)
        {
            switch (face)
            {
                case CubemapFace.PositiveX: return new Vector3(1f, -v, -u);
                case CubemapFace.NegativeX: return new Vector3(-1f, -v, u);
                case CubemapFace.PositiveY: return new Vector3(u, 1f, v);
                case CubemapFace.NegativeY: return new Vector3(u, -1f, -v);
                case CubemapFace.PositiveZ: return new Vector3(u, -v, 1f);
                default: return new Vector3(-u, -v, -1f);
            }
        }

        private static Color SkyColor(Vector3 dir, Vector3 sunDir)
        {
            // 天顶偏蓝，地平线偏白 —— 真实天空就是这样
            var zenith = new Color(0.16f, 0.34f, 0.62f);
            var horizon = new Color(0.78f, 0.85f, 0.90f);

            float up = Mathf.Clamp01(dir.y);
            var sky = Color.Lerp(horizon, zenith, Mathf.Pow(up, 0.55f));

            // 地面以下：淡淡的雾色，避免反射采样到纯黑
            if (dir.y < 0f)
                sky = Color.Lerp(horizon * 0.9f, new Color(0.12f, 0.16f, 0.18f), Mathf.Clamp01(-dir.y * 2f));

            // ---- 云：把方向投影到水平面，用 fbm 生成
            if (dir.y > 0.015f)
            {
                Vector2 p = new Vector2(dir.x, dir.z) / Mathf.Max(dir.y, 0.08f);
                float cloud = Fbm(p * 0.9f + new Vector2(11.3f, 7.7f), 5);

                // 只在中高空出现，地平线附近淡出
                float band = Mathf.SmoothStep(0f, 0.09f, dir.y) *
                             (1f - Mathf.SmoothStep(0.55f, 0.95f, dir.y));

                float mask = Mathf.SmoothStep(0.50f, 0.72f, cloud) * band;
                var cloudColor = Color.Lerp(new Color(0.72f, 0.76f, 0.82f), Color.white,
                                             Mathf.SmoothStep(0.55f, 0.85f, cloud));
                sky = Color.Lerp(sky, cloudColor, mask * 0.92f);
            }

            // ---- 太阳光晕
            float sunDot = Mathf.Clamp01(Vector3.Dot(dir, sunDir));
            float glow = Mathf.Pow(sunDot, 90f) * 4f + Mathf.Pow(sunDot, 8f) * 0.25f;
            sky += new Color(1f, 0.92f, 0.78f) * glow;

            return sky;
        }

        private static float Fbm(Vector2 p, int octaves)
        {
            float value = 0f;
            float amplitude = 0.5f;
            for (int i = 0; i < octaves; i++)
            {
                value += ValueNoise(p) * amplitude;
                p *= 2.02f;
                amplitude *= 0.5f;
            }
            return value;
        }

        private static float ValueNoise(Vector2 p)
        {
            Vector2 i = new Vector2(Mathf.Floor(p.x), Mathf.Floor(p.y));
            Vector2 f = new Vector2(p.x - i.x, p.y - i.y);
            f = new Vector2(f.x * f.x * (3f - 2f * f.x), f.y * f.y * (3f - 2f * f.y));

            float a = Hash(i);
            float b = Hash(i + new Vector2(1f, 0f));
            float c = Hash(i + new Vector2(0f, 1f));
            float d = Hash(i + new Vector2(1f, 1f));

            return Mathf.Lerp(Mathf.Lerp(a, b, f.x), Mathf.Lerp(c, d, f.x), f.y);
        }

        private static float Hash(Vector2 p)
        {
            float v = Mathf.Sin(Vector2.Dot(p, new Vector2(127.1f, 311.7f))) * 43758.5453f;
            return v - Mathf.Floor(v);
        }
    }
}
