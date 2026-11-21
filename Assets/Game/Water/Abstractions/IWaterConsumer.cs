namespace Fengling.Water.Abstractions
{
    /// <summary>
    /// 水面消费者：需要水面数据的组件实现它，由 WaterSceneBootstrap 统一注入。
    ///
    /// 用接口而不是直接找 IWaterProvider 的原因：
    ///   原来的扫描式查找（遍历所有 MonoBehaviour 找 IWaterProvider）本身也是一次 Find，
    ///   而且依赖"谁先 Awake"的隐式顺序。改成注入后顺序由 Bootstrap 的执行顺序决定。
    /// </summary>
    public interface IWaterConsumer
    {
        void ApplyWater(IWaterSurface water);
    }
}
