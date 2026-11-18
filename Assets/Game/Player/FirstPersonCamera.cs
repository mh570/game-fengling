using UnityEngine;

namespace Fengling.Player
{
    /// <summary>
    /// 第一人称相机：贴在角色眼睛高度，跟随角色朝向，鼠标上下看由自己处理。
    ///
    /// 左右转向交给 PlayerMotor（它已经在转角色），这里只负责：
    ///   1. 位置 = 角色 + 眼高
    ///   2. 俯仰角 = 鼠标 Y
    /// 避免两边同时转造成转速翻倍。
    /// </summary>
    [AddComponentMenu("Fengling/Player/First Person Camera")]
    public sealed class FirstPersonCamera : MonoBehaviour
    {
        public Transform target;

        [Tooltip("眼睛相对角色脚底的高度")]
        public float eyeHeight = 1.62f;

        [Range(0.5f, 10f)] public float mouseSensitivity = 2.5f;

        [Tooltip("俯仰限制，防止翻过头")]
        public float pitchLimit = 85f;

        private float _pitch;

        private void LateUpdate()
        {
            if (target == null)
            {
                var go = GameObject.Find("Player");
                if (go != null) target = go.transform;
                if (target == null) return;
            }

            if (Cursor.lockState == CursorLockMode.Locked)
            {
                _pitch = Mathf.Clamp(_pitch - Input.GetAxisRaw("Mouse Y") * mouseSensitivity, -pitchLimit, pitchLimit);
            }

            transform.position = target.position + Vector3.up * eyeHeight;
            transform.rotation = target.rotation * Quaternion.Euler(_pitch, 0f, 0f);
        }
    }
}
