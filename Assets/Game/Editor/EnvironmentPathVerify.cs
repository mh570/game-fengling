using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fengling.EditorTools
{
    /// <summary>
    /// 环境链路验证：把 EnvironmentBootstrap 装进 RpgLab，跑起来后断言
    /// "新链路（Context）读到的值 == 旧链路（直接引用）的值"。
    ///
    /// 为什么需要：改造后画面没变化只能证明"没改坏"，
    /// 不能证明"新链路真的在工作"。必须有等值断言。
    ///
    /// 用法：-executeMethod Fengling.EditorTools.EnvironmentPathVerify.Run
    /// 退出码 0 = 全部通过，1 = 有断言失败。
    /// </summary>
    public static class EnvironmentPathVerify
    {
        private const string ScenePath = "Assets/Scenes/RpgLab.unity";
        private const string PendingKey = "EnvPathVerify.Pending";
        private const string StartKey = "EnvPathVerify.Start";

        private static readonly List<string> Failures = new List<string>();

        public static void Run()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            // 装 Bootstrap（幂等）
            var existing = Object.FindFirstObjectByType<Fengling.Time.EnvironmentBootstrap>();
            if (existing == null)
            {
                var go = new GameObject("EnvironmentBootstrap");
                go.AddComponent<Fengling.Time.EnvironmentBootstrap>();
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
                Debug.Log("[EnvVerify] 已添加 EnvironmentBootstrap 并保存场景");
            }
            else
            {
                Debug.Log("[EnvVerify] 场景已有 EnvironmentBootstrap");
            }

            SessionState.SetBool(PendingKey, true);
            SessionState.SetFloat(StartKey, 0f);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.isPlaying = true;
        }

        [InitializeOnLoadMethod]
        private static void Reattach()
        {
            if (!SessionState.GetBool(PendingKey, false)) return;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            if (!SessionState.GetBool(PendingKey, false)) return;
            if (!EditorApplication.isPlaying) return;

            double now = EditorApplication.timeSinceStartup;
            float start = SessionState.GetFloat(StartKey, 0f);
            if (start <= 0f)
            {
                SessionState.SetFloat(StartKey, (float)now);
                return;
            }
            if (now - start < 2.0) return;

            EditorApplication.update -= Tick;
            SessionState.SetBool(PendingKey, false);
            Verify();
        }

        private static void Verify()
        {
            Failures.Clear();

            var bootstrap = Object.FindFirstObjectByType<Fengling.Time.EnvironmentBootstrap>();
            var time = Object.FindFirstObjectByType<Fengling.Time.TimeOfDaySystem>();
            var weather = Object.FindFirstObjectByType<Fengling.Time.WeatherSystem>();

            Check("Bootstrap 存在", bootstrap != null);
            Check("TimeOfDaySystem 存在", time != null);
            Check("WeatherSystem 存在", weather != null);

            if (bootstrap == null || time == null || weather == null)
            {
                Finish();
                return;
            }

            var ctx = bootstrap.Context;
            Check("Context 已创建", ctx != null);
            if (ctx == null) { Finish(); return; }

            Check("Time 已注入 context（Revision > 0）", ctx.Revision > 0);
            Check("Time 已拿到 weather 引用", time.weather != null);

            // 等值断言：新链路 vs 旧链路
            var s = ctx.State;
            CheckFloat("云量", s.CloudCover, weather.cloudCover);
            CheckFloat("雨量", s.Rain, weather.rain);
            CheckFloat("雪量", s.Snow, weather.snow);
            CheckFloat("风力", s.Wind, weather.wind);
            CheckFloat("太阳可见度", s.SunVisibility, weather.SunVisibility);
            CheckFloat("阴影可见度", s.ShadowVisibility, weather.ShadowVisibility);

            CheckNear("时间", s.Hour, time.timeOfDay, 0.5f);
            Check("太阳方向非零", s.SunDirection.sqrMagnitude > 0.9f);
            Check("太阳强度 >= 0", s.SunIntensity >= 0f);

            Finish();
        }

        private static void Finish()
        {
            if (Failures.Count == 0)
            {
                Debug.Log("[EnvVerify] ===== 全部通过 =====");
            }
            else
            {
                Debug.LogError("[EnvVerify] ===== 失败 " + Failures.Count + " 项 =====");
                foreach (string f in Failures) Debug.LogError("[EnvVerify] FAIL: " + f);
            }

            EditorApplication.isPlaying = false;
            EditorApplication.Exit(Failures.Count == 0 ? 0 : 1);
        }

        private static void Check(string label, bool ok)
        {
            if (ok) Debug.Log("[EnvVerify] OK: " + label);
            else Failures.Add(label);
        }

        private static void CheckFloat(string label, float a, float b)
        {
            bool ok = Mathf.Abs(a - b) < 0.001f;
            if (ok) Debug.Log("[EnvVerify] OK: " + label + " = " + a);
            else Failures.Add(label + "：context=" + a + " 旧路径=" + b);
        }

        private static void CheckNear(string label, float a, float b, float tolerance)
        {
            bool ok = Mathf.Abs(a - b) < tolerance;
            if (ok) Debug.Log("[EnvVerify] OK: " + label + " = " + a);
            else Failures.Add(label + "：context=" + a + " 旧路径=" + b);
        }
    }
}
