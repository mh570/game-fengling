using UnityEngine;

namespace Fengling.Water
{
    /// <summary>
    /// 在 Scene 视图里把水面高度场画成线框。
    ///
    /// 这是"测试水"最关键的工具：**不用写 shader 也能看见波浪**。
    /// 先确认波形对不对，再去做视觉，顺序不能反。
    ///
    /// 挂在 WaterModule 同一个物体上，选中它就能看到。
    /// </summary>
    [RequireComponent(typeof(WaterModule))]
    [AddComponentMenu("Fengling/Water/Water Field Gizmo")]
    public sealed class WaterFieldGizmo : MonoBehaviour
    {
        [Tooltip("每个方向的采样数。32 已经是 32×32=1024 次采样，够看形状了。")]
        [Range(4, 64)] public int resolution = 24;

        [Tooltip("线框颜色")]
        public Color color = new Color(0.25f, 0.85f, 1f, 0.85f);

        private WaterModule _water;

        private void Awake()
        {
            _water = GetComponent<WaterModule>();
        }

        private void OnDrawGizmos()
        {
            if (!Application.isPlaying) return;
            if (_water == null) _water = GetComponent<WaterModule>();
            if (_water == null) return;

            var s = _water.Settings;
            float half = s.size.x * 0.5f;
            float step = s.size.x / (resolution - 1);

            Gizmos.color = color;

            // 横向线
            for (int z = 0; z < resolution; z++)
            {
                float wz = -half + z * step;
                Vector3 prev = default;
                for (int x = 0; x < resolution; x++)
                {
                    float wx = -half + x * step;
                    var sample = _water.Sample(new Vector2(wx, wz));
                    var p = new Vector3(wx, sample.Height, wz);
                    if (x > 0) Gizmos.DrawLine(prev, p);
                    prev = p;
                }
            }

            // 纵向线
            for (int x = 0; x < resolution; x++)
            {
                float wx = -half + x * step;
                Vector3 prev = default;
                for (int z = 0; z < resolution; z++)
                {
                    float wz = -half + z * step;
                    var sample = _water.Sample(new Vector2(wx, wz));
                    var p = new Vector3(wx, sample.Height, wz);
                    if (z > 0) Gizmos.DrawLine(prev, p);
                    prev = p;
                }
            }
        }
    }
}
