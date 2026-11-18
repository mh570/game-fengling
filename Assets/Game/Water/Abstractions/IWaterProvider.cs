using UnityEngine;

namespace Fengling.Water.Abstractions
{
    /// <summary>
    /// 水模块的唯一"出口"标记。
    ///
    /// 外部（玩家、道具）不引用 Water 程序集，而是在场景里找这个标记，
    /// 从而拿到 IWaterSurface。这样编译期就不存在 Player → Water 的依赖。
    /// </summary>
    public interface IWaterProvider
    {
        IWaterSurface Water { get; }
    }
}
