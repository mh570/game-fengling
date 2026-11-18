using UnityEngine;
using Fengling.Water.Abstractions;

namespace Fengling.Water
{
    /// <summary>
    /// 水模块的唯一入口（门面 / Facade）。
    ///
    /// 场景里只需要挂这一个组件。它对外：
    ///   - 实现 IWaterProvider（让外部能找到水面）
    ///   - 实现 IWaterSurface（提供采样）
    /// 对内：
    ///   - 持有 IWaterSimulation（可替换的发动机）
    ///   - 管理时间推进
    ///
    /// 依赖方向：
    ///   WaterModule ──> IWaterSimulation（内部）
    ///   WaterModule ──> IWaterSurface（对外，Abstractions 程序集）
    ///   谁都不依赖玩家。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Fengling/Water/Water Module")]
    public sealed class WaterModule : MonoBehaviour, IWaterProvider, IWaterSurface
    {
        [SerializeField]
        private WaterSettings _settings = new WaterSettings();

        private IWaterSimulation _simulation;
        private float _waterTime;

        // ---- IWaterProvider ----
        public IWaterSurface Water => this;

        // ---- IWaterSurface ----
        public float WaterTime => _waterTime;

        public Bounds WorldBounds => _settings.WorldBounds;

        /// <summary>暴露出去供调试面板读写（测试阶段允许）。</summary>
        public WaterSettings Settings => _settings;

        private void Awake()
        {
            Rebuild();
        }

        private void Update()
        {
            // 水面时间自己走，外部不要传时间进来 —— 保证所有调用方看到的波形一致
            _waterTime += Time.deltaTime * _settings.timeScale;
        }

        /// <summary>按当前设置重建模拟。改参数后调用。</summary>
        public void Rebuild()
        {
            _simulation = _settings.kind switch
            {
                WaterKind.Flat => new FlatWaterSimulation(_settings),
                _ => new GerstnerWaterSimulation(_settings),
            };
        }

        public WaterSample Sample(Vector2 worldXZ)
        {
            return _simulation?.Sample(worldXZ, _waterTime) ?? WaterSample.None;
        }

        // ---- 运行时调参（给测试台用） ----

        public void SetKind(WaterKind kind)
        {
            _settings.kind = kind;
            Rebuild();
        }

        public void SetAmplitudeScale(float scale)
        {
            _settings.amplitudeScale = Mathf.Clamp(scale, 0f, 3f);
            Rebuild();
        }

        public void SetTimeScale(float scale)
        {
            _settings.timeScale = Mathf.Clamp(scale, 0f, 3f);
        }

        private void OnDrawGizmos()
        {
            // 水域边界
            Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.6f);
            Gizmos.DrawWireCube(
                new Vector3(0f, _settings.baseLevel, 0f),
                new Vector3(_settings.size.x, 0.1f, _settings.size.y));
        }
    }
}
