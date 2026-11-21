using Fengling.Environment;
using UnityEngine;

namespace Fengling.Time
{
    /// <summary>
    /// 环境系统的唯一装配点。
    ///
    /// 职责：
    ///   1. 创建全局唯一的 EnvironmentContext
    ///   2. 把它注入 TimeOfDaySystem / WeatherSystem（写入方）
    ///   3. 把它注入所有 IEnvironmentConsumer（读取方）
    ///
    /// 为什么放在 Fengling.Time：
    ///   Time 已经引用 Environment（写数据），而装配需要 Time 的两个系统；
    ///   若放到 Environment 会造成 Time ↔ Environment 循环引用。
    ///
    /// 全项目的 Find 集中在这里，共 3 处，是预算内的唯一允许点。
    /// </summary>
    [AddComponentMenu("Fengling/Environment/Environment Bootstrap")]
    [DefaultExecutionOrder(-500)]
    public sealed class EnvironmentBootstrap : MonoBehaviour
    {
        private EnvironmentContext _context;

        public EnvironmentContext Context
        {
            get { return _context; }
        }

        private void Awake()
        {
            _context = new EnvironmentContext();
            _context.Reset();

            // ---- 写入方
            TimeOfDaySystem time = FindFirstObjectByType<TimeOfDaySystem>();
            WeatherSystem weather = FindFirstObjectByType<WeatherSystem>();

            if (time != null) time.Initialize(_context);
            if (weather != null) weather.Initialize(_context);

            // 注入 WeatherSystem 引用，让 TimeOfDaySystem 不必自己 Find。
            // Bootstrap 的执行顺序是 -500，早于 TimeOfDaySystem.Awake，所以能生效。
            if (time != null && time.weather == null) time.weather = weather;

            // ---- 读取方
            int injected = 0;
            MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (MonoBehaviour mb in behaviours)
            {
                IEnvironmentConsumer consumer = mb as IEnvironmentConsumer;
                if (consumer == null) continue;

                consumer.ApplyEnvironment(_context);
                injected++;
            }

            Debug.Log("[Env] 上下文已建立：time=" + (time != null) +
                      " weather=" + (weather != null) +
                      " 消费者=" + injected);
        }
    }
}
