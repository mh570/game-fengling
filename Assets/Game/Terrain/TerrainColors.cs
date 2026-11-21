using UnityEngine;

namespace Fengling.TerrainSystem
{
    /// <summary>
    /// 地形顶点色：高度分带 + 坡度变岩 + 像素抖动。
    /// 调色板取自参考视频分析（有限、低饱和、偏冷绿）。
    /// </summary>
    public static class TerrainColors
    {
        static readonly Color Sand   = new Color(0.788f, 0.702f, 0.576f);
        static readonly Color Grass  = new Color(0.498f, 0.627f, 0.353f);
        static readonly Color Grass2 = new Color(0.588f, 0.702f, 0.420f);
        static readonly Color Forest = new Color(0.247f, 0.357f, 0.227f);
        static readonly Color Rock   = new Color(0.431f, 0.416f, 0.478f);
        static readonly Color Deep   = new Color(0.310f, 0.400f, 0.290f);

        /// <summary>抖动强度（0=无抖动）</summary>
        public static float DitherStrength = 0.10f;

        public static Color Compute(float worldX, float worldZ, float height, float slope,
                                    float regionMin, float regionMax)
        {
            float top = Mathf.Max(regionMax, regionMin + 8f);
            float t = Mathf.Clamp01((height - regionMin) / Mathf.Max(1f, top - regionMin));

            Color c = Sand;
            c = Color.Lerp(c, Grass,  Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.06f, 0.18f, t)));
            c = Color.Lerp(c, Grass2, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.35f, t)) * 0.65f);
            c = Color.Lerp(c, Forest, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.62f, t)));
            c = Color.Lerp(c, Rock,   Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.72f, 0.95f, t)));

            c = Color.Lerp(c, Rock, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.75f, slope)));

            if (height < 0f)
                c = Color.Lerp(c, Deep, Mathf.InverseLerp(0f, -15f, height));

            float d = Dither(worldX, worldZ);
            c *= Mathf.Lerp(1f - DitherStrength, 1f + DitherStrength, d);

            return c;
        }

        static float Dither(float x, float z)
        {
            int ix = Mathf.FloorToInt(x);
            int iz = Mathf.FloorToInt(z);
            int h = (ix * 73856093) ^ (iz * 19349663);
            h = (h ^ (h >> 13)) & 0x7fffffff;
            return (h % 16) / 15f;
        }
    }
}
