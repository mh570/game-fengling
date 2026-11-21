using Fengling.Water.Abstractions;
using UnityEngine;

namespace Fengling.Water
{
    /// <summary>
    /// 水体装配点：找一次 WaterModule，注入给场景里的水面消费者。
    ///
    /// 与 EnvironmentBootstrap 分开：水的装配是场景级引用，和环境数据是两回事。
    ///
    /// 注意 WaterSurfaceRenderer 需要的是具体的 WaterModule（要读 Settings / FillWaveData），
    /// 不是 IWaterSurface 抽象，所以这里直接按具体类型注入。
    /// </summary>
    [AddComponentMenu("Fengling/Water/Water Bootstrap")]
    [DefaultExecutionOrder(-450)]
    public sealed class WaterSceneBootstrap : MonoBehaviour
    {
        private void Awake()
        {
            WaterModule module = FindFirstObjectByType<WaterModule>();
            if (module == null)
            {
                Debug.LogWarning("[Water] 场景里没有 WaterModule，水面消费者将保持未注入");
                return;
            }

            int injected = 0;

            // 同程序集：直接按具体类型注入
            foreach (WaterSurfaceRenderer renderer in FindObjectsByType<WaterSurfaceRenderer>(FindObjectsSortMode.None))
            {
                renderer.SetWaterModule(module);
                injected++;
            }

            foreach (WaterLabHUD hud in FindObjectsByType<WaterLabHUD>(FindObjectsSortMode.None))
            {
                hud.SetWaterModule(module);
                injected++;
            }

            // 跨程序集：走 IWaterConsumer 契约（Player 只依赖 Abstractions）
            IWaterProvider provider = module as IWaterProvider;
            if (provider != null)
            {
                foreach (MonoBehaviour mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                {
                    IWaterConsumer consumer = mb as IWaterConsumer;
                    if (consumer == null) continue;

                    consumer.ApplyWater(provider.Water);
                    injected++;
                }
            }

            Debug.Log("[Water] 已注入 " + injected + " 个消费者");
        }
    }
}
