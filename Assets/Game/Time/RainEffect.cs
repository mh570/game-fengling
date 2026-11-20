using UnityEngine;

namespace Fengling.Time
{
    /// <summary>
    /// 简单程序化雨：跟随相机的 Box 发射器 + 拉伸粒子。
    /// 不依赖预制体/贴图，方便先验证天气链路。
    /// </summary>
    [AddComponentMenu("Fengling/Time/Rain Effect")]
    public sealed class RainEffect : MonoBehaviour
    {
        public WeatherSystem weather;
        public int maxParticles = 1600;
        public float area = 36f;
        public float fallSpeed = 24f;

        private ParticleSystem _ps;
        private ParticleSystemRenderer _renderer;
        private Camera _camera;

        private void Awake()
        {
            if (weather == null) weather = FindFirstObjectByType<WeatherSystem>();

            var go = new GameObject("RainParticles");
            go.transform.SetParent(transform, false);
            _ps = go.AddComponent<ParticleSystem>();
            _renderer = go.GetComponent<ParticleSystemRenderer>();

            var main = _ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = 1.4f;
            main.startSpeed = 0f;
            main.startSize = 0.035f;
            main.startColor = new Color(0.62f, 0.70f, 0.82f, 0.55f);
            main.maxParticles = maxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = _ps.emission;
            emission.rateOverTime = 0f;

            var shape = _ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(area, 1f, area);
            shape.position = new Vector3(0f, 10f, 0f);

            var velocity = _ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.y = -fallSpeed;

            _renderer.renderMode = ParticleSystemRenderMode.Stretch;
            _renderer.lengthScale = 5f;
            _renderer.velocityScale = 0.12f;
            _renderer.sharedMaterial = new Material(Shader.Find("Fengling/Rain"))
            {
                name = "Rain (instance)",
            };

            _camera = Camera.main;
            _ps.Play();
        }

        private void Update()
        {
            if (weather == null || _ps == null) return;
            if (_camera == null) _camera = Camera.main;
            if (_camera != null)
            {
                var p = _camera.transform.position;
                transform.position = new Vector3(p.x, p.y + 8f, p.z);
            }

            var emission = _ps.emission;
            emission.rateOverTime = weather.rain * 1600f;
            _renderer.enabled = weather.rain > 0.01f;
        }

        private void OnDestroy()
        {
            if (_renderer != null && _renderer.sharedMaterial != null)
                Destroy(_renderer.sharedMaterial);
        }
    }
}
