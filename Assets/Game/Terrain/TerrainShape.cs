using UnityEngine;

namespace Fengling.TerrainSystem
{
    /// <summary>
    /// 半岛地形造型。
    ///
    /// 设计（对照视频）：
    ///   - 半岛从北边伸入海里，东/南/西三面环海
    ///   - 北侧一座小山（约 28m），中部与南部是平地（2~4m）
    ///   - 海岸线自动形成沙滩（0~1.5m）
    ///   - 中部挖一个湖，一条小溪从湖流向南边的海
    ///
    /// 为什么不用真实 DEM：真实海岸落差 100m+，压平后仍然像山区，
    /// 而参考视频里是"手绘游戏的缓坡地形"。所以这里用程序化造型。
    /// </summary>
    public static class TerrainShape
    {
        public static bool Enabled = true;

        /// <summary>半岛中心（世界坐标）</summary>
        public static Vector2 Center;
        /// <summary>半岛半宽/半长（米）</summary>
        public static float HalfSize = 100f;

        /// <summary>平地基准高度</summary>
        public static float FlatHeight = 2.6f;
        /// <summary>小山高度（相对平地）</summary>
        public static float HillHeight = 26f;
        /// <summary>小山位置（归一化 -1..1）</summary>
        public static Vector2 HillPos = new Vector2(-0.25f, 0.55f);
        /// <summary>小山半径（归一化）</summary>
        public static float HillRadius = 0.55f;

        /// <summary>海面以下的深度</summary>
        public static float SeaDepth = 8f;
        /// <summary>噪声起伏幅度</summary>
        public static float NoiseAmp = 0.35f;   // 平地：只留极轻微起伏

        /// <summary>计算某点的半岛高度（世界坐标）</summary>
        public static float Sample(float worldX, float worldZ)
        {
            if (!Enabled) return 0f;

            float nx = (worldX - Center.x) / HalfSize;   // -1..1
            float nz = (worldZ - Center.y) / HalfSize;

            // ---- 海岸线：陆地铺满画面，只在南侧（底部）入海
            // 用噪声让海岸线不规则（不是直线）
            float coastZ = -0.28f + (Mathf.PerlinNoise(worldX * 0.018f + 4.7f, 3.3f) - 0.5f) * 0.30f;
            float land = Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(coastZ - 0.16f, coastZ + 0.12f, nz));

            // 东西边缘也稍微收一点，避免地块被裁成断崖
            float edgeFade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.0f, 0.86f, Mathf.Abs(nx)));
            land *= edgeFade;

            // ---- 小山（高斯）
            float dx = (nx - HillPos.x) / HillRadius;
            float dz = (nz - HillPos.y) / HillRadius;
            float hill = HillHeight * Mathf.Exp(-(dx * dx + dz * dz));

            // ---- 缓坡起伏（低频噪声）
            float noise = (Mathf.PerlinNoise(worldX * 0.035f + 11.3f, worldZ * 0.035f + 5.7f) - 0.5f)
                          * 2f * NoiseAmp;
            float noise2 = (Mathf.PerlinNoise(worldX * 0.09f + 3.1f, worldZ * 0.09f + 8.9f) - 0.5f)
                          * 2f * NoiseAmp * 0.45f;

            // ---- 合成：陆地高度
            float landH = FlatHeight + hill + noise + noise2;

            // ---- 海：陆地外缘降到海面以下
            float h = Mathf.Lerp(-SeaDepth, landH, land);

            return h;
        }
    }
}
