using UnityEngine;

namespace Fengling.TerrainSystem
{
    /// <summary>
    /// 地形改造：在真实高度图上手工"刻"出河流和湖泊。
    ///
    /// 为什么需要：真实 DEM（2.6km，2.54m/像素）解析不出小溪和小湖，
    /// 但参考视频里有水。所以要在采样后叠加人工地形。
    ///
    /// 做法：挖沟（降低高度）+ 水面平面（在 0m 处放一张水）
    /// </summary>
    public static class TerrainCarver
    {
        public struct River
        {
            public Vector2[] points;   // 折线（世界坐标 XZ）
            public float width;        // 半宽（米）
            public float depth;        // 下挖深度（米）
            public float falloff;      // 边缘过渡宽度（米）
        }

        public struct Lake
        {
            public Vector2 center;
            public float radius;
            public float depth;
            public float falloff;
        }

        public static River[] Rivers = System.Array.Empty<River>();
        public static Lake[] Lakes = System.Array.Empty<Lake>();

        // ---- 岛屿造型 ----
        /// <summary>是否把地形"捏"成一座岛（四周环海）</summary>
        public static bool IslandMode = true;
        public static Vector2 IslandCenter;
        public static float IslandRadius = 88f;   // 岛半径（米）
        public static float IslandTop = 13f;      // 岛心高度
        public static float SeaDepth = -7f;       // 外海深度

        /// <summary>
        /// 把一片海岸地形捏成岛：
        ///   - 中心保留真实地形起伏（缩放到 ±2m，作为细节）
        ///   - 叠加径向衰减，边缘降到海面以下
        ///   - 沙滩自然出现在 0m 等值线上
        /// </summary>
        public static float MakeIsland(float worldX, float worldZ, float detailH)
        {
            if (!IslandMode) return detailH;

            float r = Vector2.Distance(new Vector2(worldX, worldZ), IslandCenter) / Mathf.Max(1f, IslandRadius);

            // 中心 1 → 边缘 0。注意 Unity 的 Mathf.SmoothStep(from,to,t) 是在 from~to 间插值，
            // 不是 smoothstep(edge0,edge1,x)，所以要先 InverseLerp 再 SmoothStep(0,1,...)。
            float x = Mathf.InverseLerp(0.55f, 1.0f, r);
            float mask = 1f - Mathf.SmoothStep(0f, 1f, x);

            // 基础高度：海平面以下 → 岛心
            float baseH = Mathf.Lerp(SeaDepth, IslandTop, mask);

            // 真实地形起伏作为细节（只在岛内生效）
            float detail = (detailH - 8f) * 0.16f * mask;

            return baseH + detail;
        }

        /// <summary>对某个采样点应用所有改造，返回修改后的高度</summary>
        public static float Apply(float worldX, float worldZ, float height)
        {
            // 先把地形捏成岛（四周环海），再挖河湖
            float h = TerrainShape.Enabled ? TerrainShape.Sample(worldX, worldZ) : height;

            // 湖泊：圆形下挖
            for (int i = 0; i < Lakes.Length; i++)
            {
                var lake = Lakes[i];
                float d = Vector2.Distance(new Vector2(worldX, worldZ), lake.center);
                if (d > lake.radius + lake.falloff) continue;

                float t = Mathf.InverseLerp(lake.radius + lake.falloff, lake.radius, d);
                t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
                float target = -lake.depth;
                h = Mathf.Lerp(h, Mathf.Min(h, target), t);
            }

            // 河流：折线附近下挖
            for (int i = 0; i < Rivers.Length; i++)
            {
                var river = Rivers[i];
                if (river.points == null || river.points.Length < 2) continue;

                float bestDist = float.MaxValue;
                for (int k = 0; k < river.points.Length - 1; k++)
                {
                    float d = DistanceToSegment(new Vector2(worldX, worldZ), river.points[k], river.points[k + 1]);
                    if (d < bestDist) bestDist = d;
                }

                if (bestDist > river.width + river.falloff) continue;

                float t = Mathf.InverseLerp(river.width + river.falloff, river.width, bestDist);
                t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
                float target = -river.depth;
                h = Mathf.Lerp(h, Mathf.Min(h, target), t);
            }

            return h;
        }

        static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 0.0001f) return Vector2.Distance(p, a);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>在区域里自动规划一条入海的小河 + 一个湖</summary>
        public static void PlanDefault(Vector2 origin, float size)
        {
            // 从高处（区域右上）流向海边（区域左下）
            // 注意：这里用相对坐标，实际会换算到世界坐标
            float x0 = origin.x, z0 = origin.y;

            Rivers = new[]
            {
                new River
                {
                    points = new[]
                    {
                        new Vector2(x0 + size * 0.24f, z0 + size * 0.62f),
                        new Vector2(x0 + size * 0.27f, z0 + size * 0.52f),
                        new Vector2(x0 + size * 0.33f, z0 + size * 0.42f),
                        new Vector2(x0 + size * 0.38f, z0 + size * 0.32f),
                        new Vector2(x0 + size * 0.42f, z0 + size * 0.22f),
                        new Vector2(x0 + size * 0.45f, z0 + size * 0.04f),
                    },
                    width = 1.2f,
                    depth = 0.8f,
                    falloff = 3f,
                },
            };

            Lakes = new[]
            {
                new Lake
                {
                    center = new Vector2(x0 + size * 0.24f, z0 + size * 0.62f),
                    radius = 16f,
                    depth = 2.0f,
                    falloff = 8f,
                },
            };

            Debug.Log("[Carver] 已规划 1 条河 + 1 个湖");
        }
    }
}
