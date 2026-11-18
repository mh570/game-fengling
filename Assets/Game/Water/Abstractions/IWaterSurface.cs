using UnityEngine;

namespace Fengling.Water.Abstractions
{
    /// <summary>
    /// 一次水面采样结果。
    /// </summary>
    public readonly struct WaterSample
    {
        /// <summary>水面在该点的世界高度（Y）。</summary>
        public readonly float Height;

        /// <summary>水面法线（世界空间，已归一化）。</summary>
        public readonly Vector3 Normal;

        /// <summary>该点是否在水域范围内。</summary>
        public readonly bool Inside;

        public WaterSample(float height, Vector3 normal, bool inside)
        {
            Height = height;
            Normal = normal;
            Inside = inside;
        }

        public static readonly WaterSample None = new WaterSample(float.NegativeInfinity, Vector3.up, false);
    }

    /// <summary>
    /// 水面查询契约（唯一对外接口）。
    ///
    /// 这是整个水模块的"插座"：
    ///   - 玩家、船、鱼漂、粒子，都只认这个接口
    ///   - 水面内部是 Gerstner 还是 FFT、是 CPU 还是 GPU，外部完全不知道
    ///   - 换实现不需要改任何调用方代码
    ///
    /// 设计约束：
    ///   1. 纯查询，无副作用（不能在这里改状态）
    ///   2. 无 Unity 生命周期依赖，可以在任何线程/任何时刻调（当前实现是主线程）
    ///   3. 坐标系一律用世界空间
    /// </summary>
    public interface IWaterSurface
    {
        /// <summary>按世界坐标 XZ 采样水面。</summary>
        WaterSample Sample(Vector2 worldXZ);

        /// <summary>水面动画的当前时间（秒），由水模块自己管理，外部不要传时间进来。</summary>
        float WaterTime { get; }

        /// <summary>水域的世界包围盒，用于快速剔除。</summary>
        Bounds WorldBounds { get; }
    }
}
