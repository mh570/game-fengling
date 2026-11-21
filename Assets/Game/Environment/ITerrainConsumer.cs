namespace Fengling.Environment
{
    /// <summary>
    /// 地形消费者：由 TerrainSceneBootstrap 统一注入 CoastTerrain。
    ///
    /// 为什么单独一个接口而不并进 IEnvironmentConsumer：
    ///   地形引用是"场景装配"问题，不是"环境数据"问题；
    ///   混在一起会让 EnvironmentContext 承担它不该承担的职责。
    /// </summary>
    public interface ITerrainConsumer
    {
        void ApplyTerrain(CoastTerrain terrain);
    }
}
