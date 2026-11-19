using UnityEngine;

namespace Fengling.Player
{
    /// <summary>
    /// 相机模式切换：第一人称 / 俯视，按 C 切换。
    /// 两个相机组件挂在同一个 Camera 上，只启用其中一个。
    /// </summary>
    [AddComponentMenu("Fengling/Player/Camera Mode Switch")]
    public sealed class CameraModeSwitch : MonoBehaviour
    {
        public FirstPersonCamera firstPerson;
        public TopDownCamera topDown;
        public KeyCode toggleKey = KeyCode.C;
        [Tooltip("启动时用第一人称；默认 false = 俯视 RPG 机位")]
        public bool startInFirstPerson = false;

        private void Start()
        {
            ApplyMode(startInFirstPerson);
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey))
                ApplyMode(firstPerson != null && !firstPerson.enabled);
        }

        private void ApplyMode(bool useFirstPerson)
        {
            if (firstPerson != null) firstPerson.enabled = useFirstPerson;
            if (topDown != null) topDown.enabled = !useFirstPerson;
        }
    }
}
