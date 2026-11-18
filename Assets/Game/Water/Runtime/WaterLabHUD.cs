using UnityEngine;

namespace Fengling.Water
{
    /// <summary>
    /// 水测试台（运行时调参面板）。
    ///
    /// 核心目的：**不重编译就能调水**。
    /// 改振幅、切实现、定格时间，全部运行中完成，改完立刻看效果。
    ///
    /// 快捷键：
    ///   1 / 2     切换 Flat / Gerstner
    ///   [ ]       振幅 -/+
    ///   P         定格/恢复时间
    ///   F1        隐藏/显示面板
    /// </summary>
    [AddComponentMenu("Fengling/Water/Water Lab HUD")]
    public sealed class WaterLabHUD : MonoBehaviour
    {
        [SerializeField] private WaterModule water;
        [SerializeField] private Transform player;

        private bool _visible = true;
        private WaterSurfaceRenderer _renderer;
        private float _fps;

        private void Awake()
        {
            if (water == null) water = FindFirstObjectByType<WaterModule>();
            _renderer = FindFirstObjectByType<WaterSurfaceRenderer>();
            if (player == null)
            {
                var go = GameObject.Find("Player");
                if (go != null) player = go.transform;
            }
        }

        private void Update()
        {
            if (_fps > 0f) _fps = Mathf.Lerp(_fps, 1f / Mathf.Max(1e-5f, Time.unscaledDeltaTime), 0.1f);
            else _fps = 1f / Mathf.Max(1e-5f, Time.unscaledDeltaTime);

            if (water == null) return;

            if (Input.GetKeyDown(KeyCode.Alpha1)) water.SetKind(WaterKind.Flat);
            if (Input.GetKeyDown(KeyCode.Alpha2)) water.SetKind(WaterKind.Gerstner);
            if (Input.GetKeyDown(KeyCode.Alpha3)) water.Settings.tier = WaterQualityTier.Low;
            if (Input.GetKeyDown(KeyCode.Alpha4)) water.Settings.tier = WaterQualityTier.Medium;
            if (Input.GetKeyDown(KeyCode.Alpha5)) water.Settings.tier = WaterQualityTier.High;
            if (Input.GetKeyDown(KeyCode.Alpha6)) water.SetKind(WaterKind.Spectral);
            if (Input.GetKeyDown(KeyCode.LeftBracket)) water.SetAmplitudeScale(water.Settings.amplitudeScale - 0.1f);
            if (Input.GetKeyDown(KeyCode.RightBracket)) water.SetAmplitudeScale(water.Settings.amplitudeScale + 0.1f);
            if (Input.GetKeyDown(KeyCode.P))
                water.SetTimeScale(water.Settings.timeScale > 0f ? 0f : 1f);
            if (Input.GetKeyDown(KeyCode.F1)) _visible = !_visible;
            if (Input.GetKeyDown(KeyCode.V) && _renderer != null)
                _renderer.stylized = _renderer.stylized > 0.5f ? 0f : 1f;
        }

        private void OnGUI()
        {
            if (!_visible || water == null) return;

            var s = water.Settings;
            var style = new GUIStyle(GUI.skin.label) { fontSize = 14 };
            var box = new GUIStyle(GUI.skin.box);

            GUILayout.BeginArea(new Rect(12f, 12f, 360f, 260f), box);
            GUILayout.Label("WATER LAB", style);
            GUILayout.Label($"FPS            {_fps:0}", style);
            GUILayout.Label($"实现           {s.kind}", style);
            GUILayout.Label($"画质档         {s.tier}", style);
            if (_renderer != null)
                GUILayout.Label($"风格           {(_renderer.stylized > 0.5f ? "风格化" : "写实")}  [V] 切换", style);
            GUILayout.Label($"振幅倍率       {s.amplitudeScale:0.00}", style);
            GUILayout.Label($"时间缩放       {s.timeScale:0.00}", style);
            GUILayout.Label($"水面时间       {water.WaterTime:0.00} s", style);

            if (player != null)
            {
                var p = player.position;
                var sample = water.Sample(new Vector2(p.x, p.z));
                float delta = p.y - sample.Height;

                GUILayout.Space(6f);
                GUILayout.Label($"玩家位置       ({p.x:0.0}, {p.y:0.0}, {p.z:0.0})", style);
                GUILayout.Label($"该点水面高度   {sample.Height:0.000}", style);
                GUILayout.Label($"相对水面       {delta:+0.000;-0.000} m", style);
                GUILayout.Label($"水面法线       ({sample.Normal.x:0.00}, {sample.Normal.y:0.00}, {sample.Normal.z:0.00})", style);
            }

            GUILayout.Space(6f);
            GUILayout.Label("[1]Flat [2]Gerstner [6]Spectral  [3/4/5]画质档  [V]写实/风格化  [ ]振幅  [P]定格  [F1]隐藏", style);
            GUILayout.EndArea();
        }
    }
}
