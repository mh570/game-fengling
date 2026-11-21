Shader "Fengling/Volumetric Clouds"
{
    Properties
    {
        _CloudColor ("Cloud Color", Color) = (1, 1, 1, 1)
        _CloudShadeColor ("Cloud Shade Color", Color) = (0.35, 0.40, 0.50, 1)
        _Coverage ("Coverage", Range(0, 1)) = 0.45
        _Density ("Density", Range(0, 1)) = 0.6
        _Altitude ("Layer Altitude", Float) = 600
        _Thickness ("Layer Thickness", Float) = 600
        _Scale ("Noise Scale", Float) = 0.0008
        _RaySteps ("Ray Steps", Range(8, 96)) = 40
        _LightSteps ("Light Steps", Range(1, 8)) = 4
        _Wind ("Wind (xy)", Vector) = (0.8, 0.25, 0, 0)
        _Speed ("Scroll Speed", Float) = 0.02
    }

    SubShader
    {
        Tags { "RenderPipeline" = "FenglingForward" "RenderType" = "Opaque" "Queue" = "Geometry" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "VolumetricClouds"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            // 预乘 alpha：颜色已按透射率衰减，直接叠加
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            #include "UnityCG.cginc"

            sampler2D _DepthTex;
            float3 _CameraPos;
            float4x4 _CameraInvViewProj;
            float3 _SunDirection;
            float3 _SunColor;

            float4 _CloudColor;
            float4 _CloudShadeColor;
            float _Coverage;
            float _Density;
            float _Altitude;
            float _Thickness;
            float _Scale;
            float _RaySteps;
            float _LightSteps;
            float4 _Wind;
            float _Speed;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            // 全屏三角形：DrawProcedural 三顶点，不读顶点缓冲
            v2f vert(uint vid : SV_VertexID)
            {
                v2f o;
                float2 uv = float2((vid << 1) & 2, vid & 2);
                o.pos = float4(uv * 2.0 - 1.0, 0.0, 1.0);
                o.uv = uv;
                return o;
            }

            // ---- 值噪声 + fbm（3D，用于云的体积密度）
            float hash13(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float vnoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float n000 = hash13(i + float3(0, 0, 0));
                float n100 = hash13(i + float3(1, 0, 0));
                float n010 = hash13(i + float3(0, 1, 0));
                float n110 = hash13(i + float3(1, 1, 0));
                float n001 = hash13(i + float3(0, 0, 1));
                float n101 = hash13(i + float3(1, 0, 1));
                float n011 = hash13(i + float3(0, 1, 1));
                float n111 = hash13(i + float3(1, 1, 1));

                float x00 = lerp(n000, n100, f.x);
                float x10 = lerp(n010, n110, f.x);
                float x01 = lerp(n001, n101, f.x);
                float x11 = lerp(n011, n111, f.x);

                return lerp(lerp(x00, x10, f.y), lerp(x01, x11, f.y), f.z);
            }

            float fbm(float3 p)
            {
                float v = 0.0;
                float a = 0.5;
                for (int i = 0; i < 4; i++)
                {
                    v += a * vnoise(p);
                    p *= 2.02;
                    a *= 0.5;
                }
                return v;
            }

            // ---- 云密度：高度分层 + fbm + 覆盖度
            float DensityAt(float3 wp)
            {
                float h = (wp.y - _Altitude) / max(1.0, _Thickness);
                if (h < 0.0 || h > 1.0) return 0.0;

                // 上下边缘淡出，中间最厚
                float profile = smoothstep(0.0, 0.20, h) * (1.0 - smoothstep(0.65, 1.0, h));

                float3 windOffset = float3(_Wind.x, 0.0, _Wind.y) * (_Time.y * _Speed * 1000.0);
                float n = fbm((wp + windOffset) * _Scale);

                float d = saturate((n - _Coverage) / max(0.001, 1.0 - _Coverage));
                return d * profile * _Density;
            }

            // ---- 朝太阳方向采样，得到该点被上方云遮住多少
            float SunTransmittance(float3 wp)
            {
                float stepLen = _Thickness / max(1.0, _LightSteps);
                float3 dir = normalize(_SunDirection);
                float t = 0.0;
                float3 p = wp;
                for (int i = 0; i < 8; i++)
                {
                    if (i >= (int)_LightSteps) break;
                    p += dir * stepLen;
                    t += DensityAt(p) * stepLen;
                }
                return exp(-t * 0.02);
            }

            float4 frag(v2f i) : SV_Target
            {
                // 由 UV + 深度重建世界方向
                float2 ndc = i.uv * 2.0 - 1.0;
                float4 nearH = mul(_CameraInvViewProj, float4(ndc, 0.0, 1.0));
                float4 farH = mul(_CameraInvViewProj, float4(ndc, 1.0, 1.0));
                float3 nearW = nearH.xyz / nearH.w;
                float3 farW = farH.xyz / farH.w;
                float3 rayDir = normalize(farW - nearW);

                // 与云层水平板求交（y ∈ [altitude, altitude+thickness]）
                float y0 = _Altitude;
                float y1 = _Altitude + _Thickness;

                float t0, t1;
                if (abs(rayDir.y) < 0.0001)
                {
                    if (_CameraPos.y < y0 || _CameraPos.y > y1) return float4(0, 0, 0, 0);
                    t0 = 0.0;
                    t1 = 4000.0;
                }
                else
                {
                    float ta = (y0 - _CameraPos.y) / rayDir.y;
                    float tb = (y1 - _CameraPos.y) / rayDir.y;
                    t0 = min(ta, tb);
                    t1 = max(ta, tb);
                }

                t0 = max(t0, 0.0);
                if (t1 <= t0) return float4(0, 0, 0, 0);

                float maxDist = 6000.0;
                t1 = min(t1, maxDist);
                if (t1 <= t0) return float4(0, 0, 0, 0);

                int steps = (int)_RaySteps;
                float stepLen = (t1 - t0) / steps;

                float transmittance = 1.0;
                float3 scattered = float3(0, 0, 0);
                float3 sun = normalize(_SunDirection);

                for (int s = 0; s < 96; s++)
                {
                    if (s >= steps) break;

                    // 抖动起点，减少条带
                    float jitter = hash13(float3(i.uv * 1024.0, (float)s + _Time.y));
                    float t = t0 + (s + jitter) * stepLen;
                    float3 wp = _CameraPos + rayDir * t;

                    float density = DensityAt(wp);
                    if (density > 0.0001)
                    {
                        float sunT = SunTransmittance(wp);
                        // 简单双瓣相位：前向散射 + 各向同性
                        float cosA = dot(rayDir, sun);
                        float phase = 0.6 + 0.4 * pow(saturate(cosA), 8.0);

                        float3 lit = _CloudColor.rgb * sunT * phase;
                        float3 shade = _CloudShadeColor.rgb * 0.25;
                        float3 sampleCol = lerp(shade, lit, sunT) * _SunColor;

                        float extinction = density * stepLen * 0.00025;
                        float a = 1.0 - exp(-extinction);

                        scattered += transmittance * a * sampleCol;
                        transmittance *= (1.0 - a);

                        if (transmittance < 0.02) break;
                    }
                }

                float alpha = saturate(1.0 - transmittance);
                return float4(scattered, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
