using UnityEngine;

namespace Fengling.Player
{
    /// <summary>
    /// 正交俯视相机（等距/RTS 视角）。
    ///
    /// 和第三人称的区别不是参数，是投影方式：
    ///   透视 → 远处变小，画面有消失点
    ///   正交 → 远近同大，才有"等距"的感觉
    ///
    /// 操作：WASD/方向键平移，滚轮缩放，中键拖拽也能平移。
    /// </summary>
    [AddComponentMenu("Fengling/Player/Top Down Camera")]
    public sealed class TopDownCamera : MonoBehaviour
    {
        [Header("视角")]
        [Tooltip("俯角：45 度是经典等距，越大越接近正俯视")]
        [Range(20f, 89f)] public float pitch = 52f;

        [Tooltip("水平旋转（等距一般是 45 度）")]
        public float yaw = 45f;

        [Header("正交视野")]
        [Tooltip("视野高度（米）。越大看到的范围越广")]
        [Range(5f, 300f)] public float orthoSize = 45f;
        [Range(5f, 300f)] public float minOrthoSize = 12f;
        [Range(5f, 500f)] public float maxOrthoSize = 120f;

        [Header("平移")]
        public float panSpeed = 25f;
        [Tooltip("相机聚焦的世界坐标点（地面）")]
        public Vector3 focus = new Vector3(0f, 0f, 30f);

        [Header("边界（可选）")]
        public bool clampFocus = true;
        public Vector2 focusLimit = new Vector2(100f, 100f);

        private Camera _cam;
        private Vector3 _lastMouse;

        private void OnEnable()
        {
            var cam = GetComponent<Camera>();
            if (cam != null) cam.orthographic = true;
        }

        private void Awake()
        {
            _cam = GetComponent<Camera>();
            if (_cam == null) _cam = gameObject.AddComponent<Camera>();
            _cam.orthographic = true;
            Apply();
        }

        private void Update()
        {
            // 平移
            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");
            if (Mathf.Abs(h) > 0.01f || Mathf.Abs(v) > 0.01f)
            {
                // 用相机朝向把输入转成世界方向（保持"上=屏幕上方"）
                var forward = transform.forward;
                forward.y = 0f;
                forward.Normalize();
                var right = transform.right;
                right.y = 0f;
                right.Normalize();

                focus += (right * h + forward * v) * (panSpeed * orthoSize / 45f * Time.deltaTime);
            }

            // 中键拖拽
            if (Input.GetMouseButtonDown(2)) _lastMouse = Input.mousePosition;
            if (Input.GetMouseButton(2))
            {
                var delta = Input.mousePosition - _lastMouse;
                _lastMouse = Input.mousePosition;
                float scale = orthoSize * 2f / Mathf.Max(1f, Screen.height);
                var right = transform.right; right.y = 0f; right.Normalize();
                var forward = transform.forward; forward.y = 0f; forward.Normalize();
                focus += (-right * delta.x - forward * delta.y) * scale;
            }

            // 缩放
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f)
                orthoSize = Mathf.Clamp(orthoSize * (1f - scroll * 0.1f), minOrthoSize, maxOrthoSize);

            if (clampFocus)
            {
                focus.x = Mathf.Clamp(focus.x, -focusLimit.x, focusLimit.x);
                focus.z = Mathf.Clamp(focus.z, -focusLimit.y, focusLimit.y);
            }

            Apply();
        }

        private void Apply()
        {
            if (_cam == null) return;
            _cam.orthographic = true;
            _cam.orthographicSize = orthoSize;

            var rot = Quaternion.Euler(pitch, yaw, 0f);
            transform.rotation = rot;

            // 从焦点往相机方向退一段距离（正交下距离只影响裁剪，不影响大小）
            float distance = 200f;
            transform.position = focus - rot * Vector3.forward * distance;
        }
    }
}
