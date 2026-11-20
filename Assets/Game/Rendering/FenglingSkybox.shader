// 程序化天空盒：渐变 + fbm 云 + 太阳光晕。
// 用于自建 SRP（FenglingPipeline）的 DrawSkybox。
// 和之前烘焙的 cubemap 不同，这个是实时的，参数可调。

Shader "Fengling/Skybox"
{
    Properties
    {
        _ZenithColor ("Zenith Color", Color) = (0.16, 0.34, 0.62, 1)
        _HorizonColor ("Horizon Color", Color) = (0.78, 0.85, 0.90, 1)
        _CloudColor ("Cloud Color", Color) = (1, 1, 1, 1)
        _CloudDark ("Cloud Shadow Color", Color) = (0.62, 0.66, 0.72, 1)
        _CloudCoverage ("Cloud Coverage", Range(0, 1)) = 0.45
        _CloudScale ("Cloud Scale", Float) = 0.9
        _CloudSpeed ("Cloud Speed", Float) = 0.01
        _SunDirection ("Sun Direction", Vector) = (0.3, 0.8, 0.5, 0)
        _SunColor ("Sun Color", Color) = (1, 0.92, 0.78, 1)
        _SunSize ("Sun Glow", Range(1, 500)) = 90
        _MoonDirection ("Moon Direction", Vector) = (-0.3, -0.8, -0.5, 0)
        _MoonColor ("Moon Color", Color) = (0.72, 0.82, 1, 1)
        _MoonSize ("Moon Glow", Range(1, 500)) = 120
        _MoonPhaseAngle ("Moon Elongation", Float) = 3.14159
        _MoonDiskSize ("Moon Disk Size", Range(0.002, 0.08)) = 0.012
        _MoonDiskBrightness ("Moon Disk Brightness", Float) = 1.6
        _NightFactor ("Night Factor", Range(0, 1)) = 0
        _StarIntensity ("Star Intensity", Range(0, 1)) = 0
        _Exposure ("Exposure", Float) = 1
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _ZenithColor;
            float4 _HorizonColor;
            float4 _CloudColor;
            float4 _CloudDark;
            float _CloudCoverage;
            float _CloudScale;
            float _CloudSpeed;
            float4 _SunDirection;
            float4 _SunColor;
            float _SunSize;
            float4 _MoonDirection;
            float4 _MoonColor;
            float _MoonSize;
            float _MoonPhaseAngle;
            float _MoonDiskSize;
            float _MoonDiskBrightness;
            float _NightFactor;
            float _StarIntensity;
            float _Exposure;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float fbm(float2 p)
            {
                float v = 0;
                float amp = 0.5;
                for (int i = 0; i < 5; i++)
                {
                    v += vnoise(p) * amp;
                    p *= 2.03;
                    amp *= 0.5;
                }
                return v;
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 dir = normalize(i.dir);

                // 天顶 -> 地平线渐变
                float up = saturate(dir.y);
                float3 sky = lerp(_HorizonColor.rgb, _ZenithColor.rgb, pow(up, 0.55));

                // 地平线以下：略暗的雾色，避免反射采到纯黑
                if (dir.y < 0)
                    sky = lerp(_HorizonColor.rgb * 0.85, _HorizonColor.rgb * 0.45, saturate(-dir.y * 2));

                // 云：平面投影 + fbm
                if (dir.y > 0.02)
                {
                    float2 p = dir.xz / max(dir.y, 0.08) * _CloudScale;
                    p += _Time.y * _CloudSpeed;
                    float c = fbm(p);
                    float band = smoothstep(0.0, 0.10, dir.y) * (1.0 - smoothstep(0.55, 0.95, dir.y));
                    float mask = smoothstep(_CloudCoverage, _CloudCoverage + 0.22, c) * band;

                    float shade = smoothstep(0.45, 0.85, c);
                    float3 cloudCol = lerp(_CloudDark.rgb, _CloudColor.rgb, shade);
                    sky = lerp(sky, cloudCol, mask * 0.92);
                }

                // 太阳光晕
                float sunDot = saturate(dot(dir, normalize(_SunDirection.xyz)));
                float glow = pow(sunDot, _SunSize) * 3.0 + pow(sunDot, 8.0) * 0.2;
                sky += _SunColor.rgb * glow;

                // 月亮盘：亮面朝向太阳，按相位角切出月相。
                float3 moonDir = normalize(_MoonDirection.xyz);
                float moonCos = dot(dir, moonDir);
                float disk = smoothstep(cos(_MoonDiskSize), cos(_MoonDiskSize * 0.92), moonCos);
                if (disk > 0.001)
                {
                    float3 right = cross(float3(0, 1, 0), moonDir);
                    if (dot(right, right) < 1e-4) right = cross(float3(1, 0, 0), moonDir);
                    right = normalize(right);
                    float3 up2 = cross(moonDir, right);
                    float2 p = float2(dot(dir, right), dot(dir, up2)) / max(0.0001, _MoonDiskSize);

                    float3 sunTangentVec = normalize(_SunDirection.xyz - moonDir * dot(_SunDirection.xyz, moonDir));
                    float2 sunTangent = float2(dot(sunTangentVec, right), dot(sunTangentVec, up2));
                    sunTangent = normalize(sunTangent + float2(1e-5, 1e-5));

                    // 球面法线 + 太阳方向点乘：终止线自然是椭圆弧，不是直线切分。
                    float3 moonNormal = float3(p, sqrt(saturate(1.0 - dot(p, p))));
                    float sinElong = sin(_MoonPhaseAngle);
                    float3 moonLightDir = normalize(float3(
                        sunTangent.x * sinElong,
                        sunTangent.y * sinElong,
                        -cos(_MoonPhaseAngle)));
                    float ndl = saturate(dot(moonNormal, moonLightDir));
                    float lit = smoothstep(0.0, 0.08, ndl);

                    // 球面法线：月盘边缘更暗，避免贴纸感。
                    float r2 = saturate(dot(p, p));
                    float mu = sqrt(saturate(1.0 - r2));
                    float limb = 0.55 + 0.45 * mu;
                    float maria = 0.45 + 0.55 * fbm(p * 5.0 + 0.37);
                    float craters = smoothstep(0.985, 1.0, hash21(floor(p * 42.0 + 7.3)));
                    maria *= 1.0 - craters * 0.35;
                    float3 diskColor = _MoonColor.rgb * _MoonDiskBrightness * limb * maria *
                                       (0.03 + lit * 1.0);
                    // 地照：暗面保留一点冷光。
                    diskColor += float3(0.025, 0.035, 0.055) * (1.0 - lit) * mu;
                    sky = lerp(sky, diskColor, disk * saturate(_NightFactor * 1.2 + 0.25));
                }

                // 月亮光晕：只在夜间出现
                float moonDot = saturate(dot(dir, moonDir));
                float moonGlow = pow(moonDot, _MoonSize) * 0.6 + pow(moonDot, 20.0) * 0.08;
                sky += _MoonColor.rgb * moonGlow * _NightFactor * (1.0 - disk);

                // 星空：方向球面网格 + 稀疏 hash
                if (_StarIntensity > 0.001 && dir.y > 0.02)
                {
                    float2 sphere = float2(atan2(dir.z, dir.x), asin(clamp(dir.y, -1.0, 1.0)));
                    float2 cell = floor(sphere * 140.0);
                    float h = hash21(cell);
                    float star = step(0.9975, h) * pow(saturate(dir.y), 1.5);
                    sky += star * _StarIntensity * float3(0.8, 0.9, 1.0);
                }

                return float4(sky * _Exposure, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
