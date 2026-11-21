using UnityEngine;
using Fengling.Environment;

namespace Fengling.Player
{
    /// <summary>
    /// 很淡的贴地 contact shadow，补清晨/黄昏真实 shadow 太弱时的角色可读性。
    /// 不替代真实阴影，只负责“脚底不悬空”。
    /// </summary>
    [AddComponentMenu("Fengling/Player/Player Blob Shadow")]
    public sealed class PlayerBlobShadow : MonoBehaviour, ITerrainConsumer
    {
        public Transform target;
        public CoastTerrain terrain;

        /// <summary>由 TerrainSceneBootstrap 注入，避免自己 Find。</summary>
        public void ApplyTerrain(CoastTerrain value)
        {
            terrain = value;
        }
        public float diameter = 1.6f;
        public float groundOffset = 0.03f;
        public float maxHeight = 2.5f;
        public Color color = new Color(0f, 0f, 0f, 0.38f);

        private MeshRenderer _renderer;
        private Material _material;
        private float _baseAlpha;

        private void Awake()
        {
            _renderer = GetComponent<MeshRenderer>();
            if (_renderer != null)
            {
                _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _renderer.receiveShadows = false;
                _material = _renderer.material;
            }
            if (terrain == null)
                terrain = Object.FindFirstObjectByType<CoastTerrain>();
            _baseAlpha = color.a;
        }

        private void LateUpdate()
        {
            if (target == null || terrain == null || _material == null) return;

            float ground = terrain.SampleHeight(target.position.x, target.position.z);
            float height = target.position.y - ground;
            float visible = Mathf.Clamp01(1f - Mathf.InverseLerp(maxHeight * 0.6f, maxHeight, height));

            // 真实太阳/月影已经存在时，blob 只保留很弱的一层，避免黑球叠加。
            float realShadow = Mathf.Max(
                Shader.GetGlobalFloat("_FenglingShadowStrength"),
                Shader.GetGlobalFloat("_FenglingMoonShadowStrength"));
            visible *= Mathf.Clamp01(1f - realShadow * 1.1f);

            Vector3 p = target.position;
            p.y = ground + groundOffset;
            transform.position = p;
            transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            transform.localScale = new Vector3(diameter, diameter, 1f);

            Color c = color;
            c.a = _baseAlpha * visible;
            _material.SetColor("_Color", c);
            _renderer.enabled = visible > 0.01f;
        }
    }
}
