using UnityEngine;

namespace Fengling.Environment
{
    /// <summary>
    /// 地形装配点：找一次 CoastTerrain，注入给所有 ITerrainConsumer。
    ///
    /// 与 EnvironmentBootstrap 分开的原因：
    ///   环境数据（时间/天气）每帧变化；地形引用是静态装配。
    ///   混在一个类里会让"谁负责什么"变模糊。
    /// </summary>
    [AddComponentMenu("Fengling/Environment/Terrain Bootstrap")]
    [DefaultExecutionOrder(-450)]
    public sealed class TerrainSceneBootstrap : MonoBehaviour
    {
        private void Awake()
        {
            CoastTerrain terrain = FindFirstObjectByType<CoastTerrain>();
            if (terrain == null)
            {
                Debug.LogWarning("[Terrain] 场景里没有 CoastTerrain，地形消费者将保持未注入");
                return;
            }

            int injected = 0;
            MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (MonoBehaviour mb in behaviours)
            {
                ITerrainConsumer consumer = mb as ITerrainConsumer;
                if (consumer == null) continue;

                consumer.ApplyTerrain(terrain);
                injected++;
            }

            Debug.Log("[Terrain] 已注入 " + injected + " 个消费者");
        }
    }
}
