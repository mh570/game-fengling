using UnityEngine;
using Fengling.Water.Abstractions;

namespace Fengling.Player
{
    /// <summary>
    /// 让角色在水里浮起来。
    ///
    /// 这个组件的存在有两个目的：
    ///   1. 玩家走进水里时不会直接沉底，能站在水面上观察波形
    ///   2. 它是 IWaterSurface 的**第一个消费者** —— 用来验证接口够不够用
    ///
    /// 依赖：只引用 Fengling.Water.Abstractions，不引用 Water 实现。
    /// 它永远不知道水面是 Gerstner 还是 FFT，也不知道有没有 shader。
    /// </summary>
    [AddComponentMenu("Fengling/Player/Player Buoyancy")]
    public sealed class PlayerBuoyancy : MonoBehaviour, IWaterConsumer
    {
        [Tooltip("浮力强度：越大越快被推回水面")]
        [SerializeField] private float buoyancy = 12f;

        [Tooltip("垂直阻尼，防止在水面上下弹跳")]
        [SerializeField] private float damping = 4f;

        [Tooltip("身体中心相对脚底的高度")]
        [SerializeField] private float bodyCenterOffset = 0.9f;

        [Tooltip("头部出水高度：浮到此高度以上就不再加力")]
        [SerializeField] private float rideHeight = 0.4f;

        private IWaterSurface _water;
        private CharacterController _cc;
        private Vector3 _verticalVelocity;

        /// <summary>最近一次采样结果，给调试面板读。</summary>
        public WaterSample LastSample { get; private set; }

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            ResolveWater();
        }

        private void Start()
        {
            if (_water == null) ResolveWater();
        }

        /// <summary>由 WaterSceneBootstrap 注入，不再自己扫描场景。</summary>
        public void ApplyWater(IWaterSurface water)
        {
            _water = water;
        }

        /// <summary>
        /// 兜底：只有未被 Bootstrap 注入时才执行。
        /// 保留旧行为，避免没装 WaterSceneBootstrap 的场景（如 Preset）失效。
        /// </summary>
        private void ResolveWater()
        {
            var providers = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (var mb in providers)
            {
                if (mb is IWaterProvider p)
                {
                    _water = p.Water;
                    return;
                }
            }
            _water = null;
        }

        private void Update()
        {
            if (_water == null) return;

            var feet = transform.position;
            var center = feet + Vector3.up * bodyCenterOffset;
            var sample = _water.Sample(new Vector2(center.x, center.z));
            LastSample = sample;

            if (!sample.Inside) return;

            // 水面 - 身体中心 = 浸没深度
            float submersion = sample.Height - center.y;
            if (submersion <= 0f) return; // 完全在水面之上

            // 越深推力越大，超过 rideHeight 后逐渐减弱
            float push = Mathf.Clamp01((submersion - rideHeight) / Mathf.Max(0.01f, bodyCenterOffset));
            _verticalVelocity.y += (push * buoyancy - _verticalVelocity.y * damping) * Time.deltaTime;

            if (_cc != null)
                _cc.Move(_verticalVelocity * Time.deltaTime);
            else
                transform.position += _verticalVelocity * Time.deltaTime;
        }
    }
}
