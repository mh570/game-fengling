using UnityEngine;

namespace Fengling.Player
{
    /// <summary>
    /// 玩家装配点：把 RtsCameraController 和 PlayerMotor 接起来。
    ///
    /// 为什么需要：
    ///   自由观察模式下 WASD 应该归相机、角色不动；
    ///   聚焦模式下 WASD 归角色、相机跟随。
    ///   这需要相机知道角色、角色知道当前模式，两者必须接线。
    ///
    /// 与 TerrainSceneBootstrap 分开：这是"玩家输入归属"，不是场景环境。
    /// </summary>
    [AddComponentMenu("Fengling/Player/Player Bootstrap")]
    [DefaultExecutionOrder(-440)]
    public sealed class PlayerSceneBootstrap : MonoBehaviour
    {
        private void Awake()
        {
            RtsCameraController rts = FindFirstObjectByType<RtsCameraController>();
            PlayerMotor motor = FindFirstObjectByType<PlayerMotor>();

            if (rts != null && motor != null)
            {
                rts.playerMotor = motor;

                // 聚焦目标缺失时自动指向角色，否则空格永远聚焦不到人
                if (rts.focusTarget == null)
                    rts.focusTarget = motor.transform;
            }

            Debug.Log("[Player] 接线: rts=" + (rts != null) +
                      " motor=" + (motor != null) +
                      " focusTarget=" + (rts != null && rts.focusTarget != null));
        }
    }
}
