using System.Collections.Generic;
using UnityEngine;

namespace Fengling.Environment
{
    /// <summary>
    /// 鱼群：用 Boids（Reynolds 1987）的三条规则驱动。
    ///
    /// 参考：GitHub 的 boids 实现普遍就这三条
    ///   Separation 分离 —— 离邻居太近就往外推
    ///   Alignment  对齐 —— 速度向邻居平均速度靠拢
    ///   Cohesion   聚合 —— 向邻居中心靠近
    /// 加上两条约束：不出水面、不钻海底。
    /// </summary>
    [AddComponentMenu("Fengling/Environment/Fish School")]
    public sealed class FishSchool : MonoBehaviour
    {
        [Header("鱼群")]
        public Transform[] fish;
        public float maxSpeed = 2.5f;
        public float maxForce = 3f;

        [Header("Boids 权重")]
        public float separationWeight = 1.6f;
        public float alignmentWeight = 0.8f;
        public float cohesionWeight = 0.7f;
        public float wanderWeight = 0.6f;

        [Header("邻域")]
        public float neighborRadius = 4f;
        public float separationRadius = 1.5f;

        [Header("水域约束")]
        public float waterLevel = 0f;
        public float minDepthBelowSurface = 0.6f;
        public float seabedMargin = 0.4f;
        public Vector2 areaSize = new Vector2(60f, 40f);
        public Vector2 areaCenter = Vector2.zero;

        private Vector3[] _velocities;
        private float[] _wanderPhase;

        private void Start()
        {
            if (fish == null || fish.Length == 0) return;
            _velocities = new Vector3[fish.Length];
            _wanderPhase = new float[fish.Length];
            for (int i = 0; i < fish.Length; i++)
            {
                _velocities[i] = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)).normalized * maxSpeed * 0.5f;
                _wanderPhase[i] = Random.Range(0f, 100f);
            }
        }

        private void Update()
        {
            if (fish == null || _velocities == null) return;
            float dt = Time.deltaTime;

            for (int i = 0; i < fish.Length; i++)
            {
                var self = fish[i];
                if (self == null) continue;

                Vector3 pos = self.position;
                Vector3 vel = _velocities[i];

                Vector3 sep = Vector3.zero, ali = Vector3.zero, coh = Vector3.zero;
                int neighbors = 0;

                for (int j = 0; j < fish.Length; j++)
                {
                    if (i == j || fish[j] == null) continue;
                    Vector3 diff = pos - fish[j].position;
                    float d = diff.magnitude;

                    if (d < separationRadius && d > 1e-4f)
                        sep += diff.normalized / d;

                    if (d < neighborRadius)
                    {
                        ali += _velocities[j];
                        coh += fish[j].position;
                        neighbors++;
                    }
                }

                Vector3 accel = Vector3.zero;

                if (sep.sqrMagnitude > 1e-6f) accel += sep.normalized * separationWeight;
                if (neighbors > 0)
                {
                    ali = (ali / neighbors).normalized;
                    coh = ((coh / neighbors) - pos).normalized;
                    accel += ali * alignmentWeight + coh * cohesionWeight;
                }

                // 徘徊：用相位正弦制造轻微转向，避免完全直线
                _wanderPhase[i] += dt * 0.7f;
                accel += new Vector3(Mathf.Sin(_wanderPhase[i]), 0f, Mathf.Cos(_wanderPhase[i] * 0.8f)) * wanderWeight;

                // 不出水面
                float surfaceAllowed = waterLevel - minDepthBelowSurface;
                if (pos.y > surfaceAllowed) accel += Vector3.down * (pos.y - surfaceAllowed) * 6f;

                // 不钻海底（用射线或简单下限）
                if (pos.y < waterLevel - 6f) accel += Vector3.up * (waterLevel - 6f - pos.y) * 6f;

                // 不出水域
                Vector2 local = new Vector2(pos.x - areaCenter.x, pos.z - areaCenter.y);
                if (Mathf.Abs(local.x) > areaSize.x * 0.5f)
                    accel += Vector3.right * -Mathf.Sign(local.x) * (Mathf.Abs(local.x) - areaSize.x * 0.5f) * 0.6f;
                if (Mathf.Abs(local.y) > areaSize.y * 0.5f)
                    accel += Vector3.forward * -Mathf.Sign(local.y) * (Mathf.Abs(local.y) - areaSize.y * 0.5f) * 0.6f;

                accel = Vector3.ClampMagnitude(accel, maxForce);
                vel += accel * dt;
                vel.y *= 0.9f;                       // 鱼基本水平游
                vel = Vector3.ClampMagnitude(vel, maxSpeed);

                pos += vel * dt;
                _velocities[i] = vel;

                self.position = pos;
                if (vel.sqrMagnitude > 1e-4f)
                    self.rotation = Quaternion.Slerp(self.rotation,
                        Quaternion.LookRotation(vel, Vector3.up), dt * 4f);
            }
        }
    }
}
