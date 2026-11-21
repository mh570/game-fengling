using UnityEngine;

namespace Fengling.Environment
{
    /// <summary>
    /// 环境状态的纯数据快照。
    ///
    /// 设计约束（2026-11-21 决定）：
    ///   - 只有一个环境状态（全局唯一），不按相机分
    ///   - 消费者**主动读**（拉模式），不订阅事件
    ///   - 只放数据，不放行为 —— 派生量在写入方算好
    /// </summary>
    public struct EnvironmentState
    {
        // ---- 时间
        public int Day;
        public float Hour;                       // 0..24

        // ---- 太阳
        public Vector3 SunDirection;             // 指向太阳的单位向量
        public Color SunColor;
        public float SunIntensity;

        // ---- 月亮
        public Vector3 MoonDirection;
        public Color MoonColor;
        public float MoonIntensity;
        public float MoonIlluminatedFraction;    // 0..1

        // ---- 天气原始参数
        public float CloudCover;
        public float Haze;
        public float Fog;
        public float Rain;
        public float Snow;
        public float Wind;

        // ---- 天气派生量（由 WeatherSystem 算好，消费者直接用）
        public float SunVisibility;
        public float MoonVisibility;
        public float ShadowVisibility;
        public float FogDensity;

        // ---- 环境光
        public float AmbientIntensity;
    }

    /// <summary>
    /// 环境上下文：场景内唯一的"环境真相"持有者。
    ///
    /// 谁来写：
    ///   - TimeOfDaySystem  → SetCelestial（时间 / 日月）
    ///   - WeatherSystem    → SetWeather（天气与派生量）
    ///
    /// 谁来读：
    ///   - 渲染管线 / 天空 / 阴影 / 水面 / 雨雪 / 玩家浮力
    ///
    /// 为什么分成两个写入口而不是一个 SetState：
    ///   两个写入方各自负责不同字段，用一个整体 SetState 会互相覆盖。
    /// </summary>
    public sealed class EnvironmentContext
    {
        private EnvironmentState _state;

        /// <summary>当前状态快照。消费者每帧读它。</summary>
        public EnvironmentState State
        {
            get { return _state; }
        }

        /// <summary>每次写入递增。消费者可用它判断"值有没有变"，避免每帧重复推 shader。</summary>
        public int Revision { get; private set; }

        /// <summary>时间 / 日月写入。</summary>
        public void SetCelestial(
            int day, float hour,
            Vector3 sunDirection, Color sunColor, float sunIntensity,
            Vector3 moonDirection, Color moonColor, float moonIntensity, float moonIlluminatedFraction,
            float ambientIntensity)
        {
            _state.Day = day;
            _state.Hour = hour;
            _state.SunDirection = sunDirection;
            _state.SunColor = sunColor;
            _state.SunIntensity = sunIntensity;
            _state.MoonDirection = moonDirection;
            _state.MoonColor = moonColor;
            _state.MoonIntensity = moonIntensity;
            _state.MoonIlluminatedFraction = moonIlluminatedFraction;
            _state.AmbientIntensity = ambientIntensity;

            Revision++;
        }

        /// <summary>天气写入（含派生量）。</summary>
        public void SetWeather(
            float cloudCover, float haze, float fog, float rain, float snow, float wind,
            float sunVisibility, float moonVisibility, float shadowVisibility, float fogDensity)
        {
            _state.CloudCover = cloudCover;
            _state.Haze = haze;
            _state.Fog = fog;
            _state.Rain = rain;
            _state.Snow = snow;
            _state.Wind = wind;
            _state.SunVisibility = sunVisibility;
            _state.MoonVisibility = moonVisibility;
            _state.ShadowVisibility = shadowVisibility;
            _state.FogDensity = fogDensity;

            Revision++;
        }

        /// <summary>测试/工具用：重置为默认值。</summary>
        public void Reset()
        {
            _state = default(EnvironmentState);
            _state.SunColor = Color.white;
            _state.MoonColor = new Color(0.75f, 0.82f, 1f);
            _state.SunVisibility = 1f;
            _state.MoonVisibility = 1f;
            _state.ShadowVisibility = 1f;
            Revision++;
        }
    }
}
