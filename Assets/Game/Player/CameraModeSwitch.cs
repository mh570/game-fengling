using UnityEngine;

namespace Fengling.Player
{
    /// <summary>
    /// 相机模式切换：第三人称 / 俯视，按 C 切换。
    /// 两个相机组件挂在同一个 Camera 上，只启用其中一个。
    /// </summary>
    [AddComponentMenu("Fengling/Player/Camera Mode Switch")]
    public sealed class CameraModeSwitch : MonoBehaviour
    {
        public ThirdPersonCamera thirdPerson;
        public TopDownCamera topDown;
        public KeyCode toggleKey = KeyCode.C;

        private void Start()
        {
            ApplyMode(true);
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey)) ApplyMode(thirdPerson == null || !thirdPerson.enabled);
        }

        private void ApplyMode(bool useThirdPerson)
        {
            if (thirdPerson != null) thirdPerson.enabled = useThirdPerson;
            if (topDown != null) topDown.enabled = !useThirdPerson;
        }
    }
}
