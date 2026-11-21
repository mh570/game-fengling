using UnityEngine;
using Fengling.Water.Abstractions;

namespace Fengling.Player
{
    /// <summary>
    /// 最简单的角色移动：WASD + 鼠标 + 跳跃 + 重力。
    ///
    /// 刻意保持尽量简单 —— 这个角色的唯一职责是"把玩家带到水边、走进水里"，
    /// 用来观察水的表现。它不是主角控制器，不要往这里堆动画、状态机、技能。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [AddComponentMenu("Fengling/Player/Player Motor")]
    public sealed class PlayerMotor : MonoBehaviour
    {
        /// <summary>是否接受移动输入。自由观察模式下为 false，角色原地不动。</summary>
        public bool acceptMoveInput = true;

        [Header("移动")]
        [SerializeField] private float walkSpeed = 4.5f;
        [SerializeField] private float runSpeed = 8f;
        [SerializeField] private float jumpHeight = 1.2f;
        [SerializeField] private float gravity = -20f;

        [Header("视角")]
        [SerializeField] private float mouseSensitivity = 2.5f;
        [SerializeField] private Transform cameraPivot;

        [Header("转向")]
        [Tooltip("移动方向以这个相机为基准（W = 屏幕向上）。由 PlayerSceneBootstrap 注入")]
        public Transform cameraTransform;
        [Tooltip("转身平滑速度，越大转得越快")]
        public float turnSpeed = 12f;

        private CharacterController _cc;
        private Vector3 _velocity;
        private float _pitch;
        private bool _cursorLocked;
        private Vector3 _spawnPosition;
        private Quaternion _spawnRotation;

        public Vector3 Velocity => _velocity;
        public bool IsGrounded => _cc != null && _cc.isGrounded;

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            // 第三人称相机自己跟随，不再自动挂到 Camera.main（否则会打架）

            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;
            LockCursor(true);
        }

        private void Update()
        {
            HandleCursor();
            if (_cursorLocked) HandleLook();
            HandleMove();
        }

        /// <summary>
        /// 掉出世界的保险：没有碰撞体、或者穿模掉下去时，把人拉回出生点，
        /// 而不是让他一直自由落体到 Y=-15374（第一次跑就踩了这个坑）。
        /// </summary>
        private void LateUpdate()
        {
            if (transform.position.y < -100f)
            {
                Debug.LogWarning("[Player] 掉出世界，已重置到出生点");
                _cc.enabled = false;
                transform.SetPositionAndRotation(_spawnPosition, _spawnRotation);
                _cc.enabled = true;
                _velocity = Vector3.zero;
            }
        }

        private void HandleCursor()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) LockCursor(false);
            if (Input.GetMouseButtonDown(0) && !_cursorLocked) LockCursor(true);
        }

        private void LockCursor(bool locked)
        {
            _cursorLocked = locked;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        private void HandleLook()
        {
            float my = Input.GetAxisRaw("Mouse Y") * mouseSensitivity;

            // 不再用鼠标旋转角色：角色朝向由移动方向决定（RTS 手感）。
            // 否则"面朝相机时按 W 会倒着走"，而且鼠标和移动方向会互相打架。
            if (cameraPivot != null && acceptMoveInput)
            {
                _pitch = Mathf.Clamp(_pitch - my, -85f, 85f);
                cameraPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
            }
        }

        private void HandleMove()
        {
            float h = acceptMoveInput ? Input.GetAxisRaw("Horizontal") : 0f;
            float v = acceptMoveInput ? Input.GetAxisRaw("Vertical") : 0f;
            var input = new Vector3(h, 0f, v);
            if (input.sqrMagnitude > 1f) input.Normalize();

            bool running = acceptMoveInput && Input.GetKey(KeyCode.LeftShift);
            float speed = running ? runSpeed : walkSpeed;

            // 相机相对移动：W = 屏幕向上，D = 屏幕向右
            Vector3 fwd = Vector3.forward;
            Vector3 right = Vector3.right;
            if (cameraTransform != null)
            {
                fwd = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up);
                if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
                fwd.Normalize();
                right = Vector3.Cross(Vector3.up, fwd);
            }

            Vector3 dir = fwd * input.z + right * input.x;
            if (dir.sqrMagnitude > 1f) dir.Normalize();
            var planar = dir * speed;

            // 朝移动方向转身（平滑），这样能看出角色正面朝哪
            if (dir.sqrMagnitude > 0.0001f)
            {
                Quaternion target = Quaternion.LookRotation(dir, Vector3.up);
                transform.rotation = Quaternion.Slerp(
                    transform.rotation, target, 1f - Mathf.Exp(-turnSpeed * Time.deltaTime));
            }

            if (_cc.isGrounded && _velocity.y < 0f) _velocity.y = -2f; // 贴地
            if (acceptMoveInput && _cc.isGrounded && Input.GetButtonDown("Jump"))
                _velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);

            _velocity.y += gravity * Time.deltaTime;

            _cc.Move((planar + _velocity) * Time.deltaTime);
        }
    }
}
