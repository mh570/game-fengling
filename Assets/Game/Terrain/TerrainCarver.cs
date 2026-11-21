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

        /// <summary>对某个采样点应用所有改造，返回修改后的高度</summary>
        public static float Apply(float worldX, float worldZ, float height)
        {
            float h = height;

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
                        new Vector2(x0 + size * 0.80f, z0 + size * 0.90f),
                        new Vector2(x0 + size * 0.68f, z0 + size * 0.72f),
                        new Vector2(x0 + size * 0.58f, z0 + size * 0.55f),
                        new Vector2(x0 + size * 0.42f, z0 + size * 0.40f),
                        new Vector2(x0 + size * 0.30f, z0 + size * 0.25f),
                        new Vector2(x0 + size * 0.18f, z0 + size * 0.12f),
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
                    center = new Vector2(x0 + size * 0.35f, z0 + size * 0.70f),
                    radius = 12f,
                    depth = 1.4f,
                    falloff = 8f,
                },
            };

            Debug.Log("[Carver] 已规划 1 条河 + 1 个湖");
        }
    }
}
