using UnityEngine;
using Fengling.Water.Abstractions;

namespace Fengling.Water
{
    /// <summary>
    /// 对照组：完全是平面。
    ///
    /// 存在的意义不是"凑数"，而是**控制变量**：
    /// 当玩家在水上跳、卡住、抖动时，先切到 Flat 看问题还在不在。
    /// 还在 → 是玩家/物理的问题；没了 → 是波形的问题。
    /// </summary>
    public sealed class FlatWaterSimulation : IWaterSimulation
    {
        private readonly float _level;
        private readonly Bounds _bounds;

        public FlatWaterSimulation(WaterSettings settings)
        {
            _level = settings.baseLevel;
            _bounds = settings.WorldBounds;
        }

        public WaterSample Sample(Vector2 worldXZ, float time)
        {
            bool inside = _bounds.Contains(new Vector3(worldXZ.x, _level, worldXZ.y));
            return new WaterSample(_level, Vector3.up, inside);
        }

        /// <summary>平面没有波，返回 0 —— shader 会退化成静止水面。</summary>
        public int FillWaveData(Vector4[] dirs, Vector4[] parameters, float amplitudeScale) => 0;
    }
}
