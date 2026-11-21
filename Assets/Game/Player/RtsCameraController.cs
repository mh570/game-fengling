using UnityEngine;
using Fengling.Environment;

namespace Fengling.Player
{
    /// <summary>
    /// Manor Lords / RTS 式自由相机：
    /// 目标点 + 透视 Rig，WASD 平移，滚轮缩放，中键/QE 旋转，
    /// Space 聚焦玩家，地形/水面/边界约束。
    ///
    /// 结构参考 RTSCameraController-Cinemachine 与 Stratecam：
    /// 目标点、缩放非线性、缩放越大移动越快、地面检测、边界、平滑。
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [AddComponentMenu("Fengling/Player/RTS Camera Controller")]
    public sealed class RtsCameraController : MonoBehaviour, ITerrainConsumer
    {
        [Header("目标")]
        public Transform focusTarget;

        [Tooltip("聚焦时由相机把 WASD 让给角色；由 PlayerSceneBootstrap 接线")]
        public PlayerMotor playerMotor;

        /// <summary>是否处于聚焦操控模式（WASD 归角色，相机跟随）。</summary>
        public bool IsFollowing
        {
            get { return _focused; }
        }

        public bool startFocused = false;
        public Vector3 focusOffset = new Vector3(0f, 1f, 0f);

        [Header("环绕")]
        public float yaw = 45f;
        [Range(20f, 85f)] public float pitch = 55f;
        public float minPitch = 35f;
        public float maxPitch = 75f;
        public float rotateKeySpeed = 90f;
        public float mouseRotateSensitivity = 4f;

        [Header("缩放")]
        public float distance = 35f;
        public float minDistance = 15f;
        public float maxDistance = 120f;
        [Range(0.01f, 0.5f)] public float zoomStep = 0.12f;

        [Header("平移")]
        public float panSpeed = 30f;
        [Range(0f, 30f)] public float smoothing = 12f;
        public bool edgeScroll = false;
        public float edgeSize = 12f;

        [Header("边界 / 地面")]
        public CoastTerrain terrain;

        /// <summary>由 TerrainSceneBootstrap 注入，避免自己 Find。</summary>
        public void ApplyTerrain(CoastTerrain value)
        {
            terrain = value;
        }
        public Vector2 boundsHalf = new Vector2(95f, 95f);
        public bool clampToBounds = true;
        public float groundClearance = 1.2f;
        public float waterClearance = 1f;

        [Header("输入")]
        public KeyCode focusKey = KeyCode.F;
        public KeyCode rotateLeft = KeyCode.Q;
        public KeyCode rotateRight = KeyCode.E;

        private Camera _camera;
        private Vector3 _focus;
        private Vector3 _positionVelocity;
        private bool _focused;
        private Vector3 _lastMouse;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _camera.orthographic = false;
            _camera.fieldOfView = 45f;
            _camera.nearClipPlane = 0.3f;
            _camera.farClipPlane = 1000f;

            _focused = startFocused && focusTarget != null;
            _focus = _focused ? FocusPoint() : new Vector3(0f, 0f, 20f);
            ApplyImmediate();
        }

        private void OnGUI()
        {
            string mode = _focused ? "聚焦操控（WASD 移动角色）" : "自由观察（WASD 平移相机）";
            var style = new GUIStyle(GUI.skin.label) { fontSize = 18 };
            style.normal.textColor = _focused ? new Color(0.5f, 1f, 0.5f) : Color.white;
            GUI.Label(new Rect(12f, 10f, 520f, 28f), "模式：" + mode + "     [F] 切换", style);
        }

        private void Update()
        {
            if (Input.GetKeyDown(focusKey))
            {
                _focused = !_focused;
                if (_focused && focusTarget == null)
                    _focused = false;
            }

            // Q/E 旋转
            float yawInput = 0f;
            if (Input.GetKey(rotateLeft)) yawInput -= 1f;
            if (Input.GetKey(rotateRight)) yawInput += 1f;
            yaw += yawInput * rotateKeySpeed * Time.unscaledDeltaTime;

            // 中键拖拽旋转 / 俯角
            if (Input.GetMouseButtonDown(2))
                _lastMouse = Input.mousePosition;
            if (Input.GetMouseButton(2))
            {
                Vector3 delta = Input.mousePosition - _lastMouse;
                _lastMouse = Input.mousePosition;
                yaw += delta.x * mouseRotateSensitivity * 0.1f;
                pitch -= delta.y * mouseRotateSensitivity * 0.1f;
            }

            // WASD / 方向键平移。
            // 聚焦模式下 WASD 归角色，相机只跟随，不能再平移，否则两边同时动。
            Vector2 move = _focused
                ? Vector2.zero
                : new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            if (edgeScroll && Application.isFocused)
            {
                Vector3 mouse = Input.mousePosition;
                if (mouse.x >= 0f && mouse.y >= 0f && mouse.x <= Screen.width && mouse.y <= Screen.height)
                {
                    if (mouse.x < edgeSize) move.x -= 1f;
                    if (mouse.x > Screen.width - edgeSize) move.x += 1f;
                    if (mouse.y < edgeSize) move.y -= 1f;
                    if (mouse.y > Screen.height - edgeSize) move.y += 1f;
                }
            }

            // 把输入归属告诉角色：聚焦时角色能走，观察时角色原地不动
            if (playerMotor != null)
                playerMotor.acceptMoveInput = _focused;

            if (move.sqrMagnitude > 0.001f)
            {
                _focused = false;
                Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                Vector3 right = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
                float speed = panSpeed * Mathf.Lerp(0.7f, 1.8f, Mathf.InverseLerp(minDistance, maxDistance, distance));
                _focus += (right * move.x + forward * move.y) * speed * Time.unscaledDeltaTime;
            }

            // 滚轮缩放：越远越快，避免近处一格跨太远
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                float factor = Mathf.Lerp(0.75f, 1.25f, Mathf.InverseLerp(minDistance, maxDistance, distance));
                distance = Mathf.Clamp(distance * (1f - scroll * zoomStep * factor), minDistance, maxDistance);
            }

            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

            if (_focused && focusTarget != null)
                _focus = FocusPoint();

            if (clampToBounds)
            {
                _focus.x = Mathf.Clamp(_focus.x, -boundsHalf.x, boundsHalf.x);
                _focus.z = Mathf.Clamp(_focus.z, -boundsHalf.y, boundsHalf.y);
            }

            // 目标点直接响应输入；相机位置由 SmoothDamp 平滑
            // （目标点再 SmoothDamp 一次会和位置平滑叠加成双重延迟）。

            Quaternion desiredRotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 desiredPosition = _focus - desiredRotation * Vector3.forward * distance;

            // 地面/水面 clearance：相机不能穿地、不能潜到水下
            float ground = terrain != null ? terrain.SampleHeight(desiredPosition.x, desiredPosition.z) : 0f;
            float water = Shader.GetGlobalFloat("_FenglingWaterLevel");
            float minY = Mathf.Max(ground + groundClearance, water + waterClearance);
            if (desiredPosition.y < minY)
                desiredPosition.y = minY;

            transform.position = Vector3.SmoothDamp(
                transform.position, desiredPosition, ref _positionVelocity,
                Mathf.Max(0.001f, 1f / Mathf.Max(0.01f, smoothing)), Mathf.Infinity, Time.unscaledDeltaTime);
            float t = 1f - Mathf.Exp(-Mathf.Max(0.01f, smoothing) * Time.unscaledDeltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, t);
        }

        private Vector3 FocusPoint()
        {
            return focusTarget != null ? focusTarget.position + focusOffset : _focus;
        }

        private void ApplyImmediate()
        {
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            transform.rotation = rot;
            transform.position = _focus - rot * Vector3.forward * distance;
        }

        /// <summary>外部调用：聚焦玩家。</summary>
        public void FocusOnPlayer()
        {
            if (focusTarget == null) return;
            _focused = true;
            _focus = FocusPoint();
        }

        /// <summary>外部调用：聚焦世界点（双击地面等以后接）。</summary>
        public void FocusOn(Vector3 worldPoint)
        {
            _focused = false;
            _focus = worldPoint;
        }

        private void OnValidate()
        {
            minDistance = Mathf.Max(1f, minDistance);
            maxDistance = Mathf.Max(minDistance, maxDistance);
            distance = Mathf.Clamp(distance, minDistance, maxDistance);
            minPitch = Mathf.Clamp(minPitch, 5f, 85f);
            maxPitch = Mathf.Clamp(maxPitch, minPitch, 89f);
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
            smoothing = Mathf.Max(0.01f, smoothing);
        }
    }
}
