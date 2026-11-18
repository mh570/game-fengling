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
        [Header("移动")]
        [SerializeField] private float walkSpeed = 4.5f;
        [SerializeField] private float runSpeed = 8f;
        [SerializeField] private float jumpHeight = 1.2f;
        [SerializeField] private float gravity = -20f;

        [Header("视角")]
        [SerializeField] private float mouseSensitivity = 2.5f;
        [SerializeField] private Transform cameraPivot;

        private CharacterController _cc;
        private Vector3 _velocity;
        private float _pitch;
        private bool _cursorLocked;

        public Vector3 Velocity => _velocity;
        public bool IsGrounded => _cc != null && _cc.isGrounded;

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            if (cameraPivot == null && Camera.main != null)
                cameraPivot = Camera.main.transform;
            LockCursor(true);
        }

        private void Update()
        {
            HandleCursor();
            if (_cursorLocked) HandleLook();
            HandleMove();
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
            float mx = Input.GetAxisRaw("Mouse X") * mouseSensitivity;
            float my = Input.GetAxisRaw("Mouse Y") * mouseSensitivity;

            transform.Rotate(Vector3.up, mx, Space.Self);

            if (cameraPivot != null)
            {
                _pitch = Mathf.Clamp(_pitch - my, -85f, 85f);
                cameraPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
            }
        }

        private void HandleMove()
        {
            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");
            var input = new Vector3(h, 0f, v);
            if (input.sqrMagnitude > 1f) input.Normalize();

            bool running = Input.GetKey(KeyCode.LeftShift);
            float speed = running ? runSpeed : walkSpeed;
            var planar = transform.TransformDirection(input) * speed;

            if (_cc.isGrounded && _velocity.y < 0f) _velocity.y = -2f; // 贴地
            if (_cc.isGrounded && Input.GetButtonDown("Jump"))
                _velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);

            _velocity.y += gravity * Time.deltaTime;

            _cc.Move((planar + _velocity) * Time.deltaTime);
        }
    }
}
