using UnityEngine;

namespace Fengling.Player
{
    /// <summary>
    /// 第三人称俯视相机（RPG 标准机位）。
    ///
    /// 之前是第一人称：相机挂在角色头上，看到的是"贴水面看远方"。
    /// 但这是个俯视角 RPG，需要从后上方 40–60° 看下去，
    /// 才能同时看到角色、沙滩和整片水面。
    /// </summary>
    [AddComponentMenu("Fengling/Player/Third Person Camera")]
    public sealed class ThirdPersonCamera : MonoBehaviour
    {
        [Tooltip("跟随目标，留空则自动找名为 Player 的物体")]
        public Transform target;

        [Tooltip("相对目标的偏移：X 右、Y 上、Z 前。相机放在后方所以 Z 为负")]
        public Vector3 offset = new Vector3(0f, 7.5f, -9f);

        [Tooltip("看到的目标点相对角色脚底的高度")]
        public float lookAtHeight = 1.2f;

        [Tooltip("跟随平滑，0 = 硬跟随")]
        [Range(0f, 20f)] public float followSharpness = 6f;

        [Tooltip("是否用角色朝向决定相机方位。关掉则保持世界固定方向（等距视角）")]
        public bool followYaw = false;

        private void LateUpdate()
        {
            if (target == null)
            {
                var go = GameObject.Find("Player");
                if (go != null) target = go.transform;
                if (target == null) return;
            }

            var desiredOffset = followYaw ? target.rotation * offset : offset;
            var desiredPos = target.position + desiredOffset;

            transform.position = followSharpness <= 0f
                ? desiredPos
                : Vector3.Lerp(transform.position, desiredPos, 1f - Mathf.Exp(-followSharpness * Time.deltaTime));

            transform.LookAt(target.position + Vector3.up * lookAtHeight);
        }
    }
}
