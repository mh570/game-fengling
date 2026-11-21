using UnityEngine;

namespace Fengling.TerrainSystem
{
    /// <summary>
    /// 地形顶点色。
    ///
    /// 因为地形是**平的**（参考视频也如此），所以这里用**绝对高度**分色，
    /// 不用"区域内相对高度"——那样平地会被判成岩石，整片变灰。
    ///
    /// 规则：
    ///   水面下   → 深色水底
    ///   0~0.9m   → 沙滩
    ///   0.9m 以上 → 草地（用低频噪声做深浅变化）
    ///   坡度陡   → 岩石
    /// </summary>
    public static class TerrainColors
    {
        static readonly Color Sand   = new Color(0.82f, 0.76f, 0.60f);
        static readonly Color Sand2  = new Color(0.74f, 0.67f, 0.52f);
        static readonly Color Grass  = new Color(0.44f, 0.56f, 0.30f);   // 暗绿
        static readonly Color Grass2 = new Color(0.54f, 0.68f, 0.34f);   // 亮一档
        static readonly Color Forest = new Color(0.40f, 0.52f, 0.28f);   // 最深档（不能再暗，否则发黑）
        static readonly Color Rock   = new Color(0.46f, 0.44f, 0.48f);
        static readonly Color Deep   = new Color(0.26f, 0.36f, 0.26f);

        /// <summary>抖动强度</summary>
        public static float DitherStrength = 0.05f;

        public static Color Compute(float worldX, float worldZ, float height, float slope,
                                    float regionMin, float regionMax)
        {
            Color c;

            if (height < -0.15f)
            {
                // 水底
                c = Color.Lerp(Deep, Sand, Mathf.InverseLerp(-6f, -0.15f, height));
            }
            else if (height < 0.9f)
            {
                // 沙滩
                c = Color.Lerp(Sand2, Sand, Mathf.InverseLerp(-0.15f, 0.9f, height));
            }
            else
            {
                // 草地：低频大斑块（视频里最明显的特征——深浅交错的水彩感）
                float big = Mathf.PerlinNoise(worldX * 0.018f + 7.1f, worldZ * 0.018f + 2.9f);
                float mid = Mathf.PerlinNoise(worldX * 0.055f + 1.7f, worldZ * 0.055f + 5.3f);
                float patch = big * 0.72f + mid * 0.28f;

                c = Color.Lerp(Forest, Grass2, Mathf.SmoothStep(0f, 1f, patch));
                // 最亮的区域再提一档，拉开层次
                if (patch > 0.66f) c = Color.Lerp(c, new Color(0.60f, 0.74f, 0.38f), (patch - 0.66f) * 2.0f);
            }

            // 陡坡露岩
            c = Color.Lerp(c, Rock, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.40f, 0.75f, slope)));

            // 水线白边：视频里最醒目的特征（岸边一圈白）
            float waterline = 1f - Mathf.Clamp01(Mathf.Abs(height + 0.05f) / 0.30f);
            c = Color.Lerp(c, new Color(0.96f, 0.97f, 0.94f), waterline * 0.80f);

            // 像素抖动
            float d = Dither(worldX, worldZ);
            c *= Mathf.Lerp(1f - DitherStrength, 1f + DitherStrength, d);

            return c;
        }

        static float Dither(float x, float z)
        {
            int ix = Mathf.FloorToInt(x * 0.5f);
            int iz = Mathf.FloorToInt(z * 0.5f);
            int h = (ix * 73856093) ^ (iz * 19349663);
            h = (h ^ (h >> 13)) & 0x7fffffff;
            return (h % 16) / 15f;
        }
    }
}
