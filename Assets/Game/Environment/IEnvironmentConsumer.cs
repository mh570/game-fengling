namespace Fengling.Environment
{
    /// <summary>
    /// 环境消费者：需要环境数据的组件实现它，由 EnvironmentBootstrap 统一注入。
    ///
    /// 为什么用接口 + 集中注入，而不是各自 Find：
    ///   全项目 36 处 FindFirstObjectByType 是最大的耦合来源。
    ///   改成"Bootstrap 找一次，注入给所有人"后，消费者不再关心谁提供数据。
    ///
    /// 实现方约定：只保存引用，不要在 ApplyEnvironment 里做重活。
    /// 数据每帧变化，读的时机由消费者自己决定（拉模式）。
    /// </summary>
    public interface IEnvironmentConsumer
    {
        void ApplyEnvironment(EnvironmentContext context);
    }
}
