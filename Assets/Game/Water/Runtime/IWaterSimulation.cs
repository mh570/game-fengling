using UnityEngine;
using Fengling.Water.Abstractions;

namespace Fengling.Water
{
    /// <summary>
    /// 水面模拟的内部实现契约（模块内私有的"发动机"）。
    ///
    /// 与 IWaterSurface 的区别：
    ///   IWaterSurface  = 给外部用的插头（稳定，不能随便改）
    ///   IWaterSimulation = 模块内部可替换的发动机（Gerstner / 频谱合成 / 平面）
    ///
    /// 外部永远看不到这个接口。
    /// </summary>
    public interface IWaterSimulation
    {
        /// <summary>采样水面高度与法线。</summary>
        WaterSample Sample(Vector2 worldXZ, float time);

        /// <summary>
        /// 把 GPU 需要的波形数据填进数组（方向 xyz、参数 amp/k/omega/phase）。
        ///
        /// 为什么放在接口上：CPU 采样和 GPU 顶点位移必须用**同一组波**，
        /// 否则玩家看到的画面和浮力会错位（人浮在水面上方或下方）。
        /// 让模拟器自己导出它实际在用的波，就不会有两份数据源。
        ///
        /// 返回实际写入的波数量。
        /// </summary>
        int FillWaveData(Vector4[] dirs, Vector4[] parameters, float amplitudeScale);
    }
}
