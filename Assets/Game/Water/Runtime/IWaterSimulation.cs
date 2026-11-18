using UnityEngine;
using Fengling.Water.Abstractions;

namespace Fengling.Water
{
    /// <summary>
    /// 水面模拟的内部实现契约（模块内私有的"发动机"）。
    ///
    /// 与 IWaterSurface 的区别：
    ///   IWaterSurface  = 给外部用的插头（稳定，不能随便改）
    ///   IWaterSimulation = 模块内部可替换的发动机（Gerstner / FFT / 噪声）
    ///
    /// 外部永远看不到这个接口。
    /// </summary>
    public interface IWaterSimulation
    {
        /// <summary>采样水面高度与法线。</summary>
        WaterSample Sample(Vector2 worldXZ, float time);
    }
}
