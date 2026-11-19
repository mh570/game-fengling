// 自建 SRP 的后处理：曝光 → Bloom → ACES → 对比度/饱和度。
// 三个 pass 共用这个 shader：
//   0 Composite：场景 + Bloom → 最终画面
//   1 Bright：提取高光
//   2 Blur：单向高斯模糊（水平/垂直各一次）
Shader "Fengling/PostProcess"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _BloomTex ("Bloom", 2D) = "black" {}
    }

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "Composite"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _BloomTex;
            float _FenglingExposure;
            float _FenglingBloomIntensity;
            float _FenglingContrast;
            float _FenglingSaturation;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float3 ACESFilm(float3 x)
            {
                return saturate((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14));
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 scene = tex2D(_MainTex, i.uv).rgb;
                float3 bloom = tex2D(_BloomTex, i.uv).rgb;

                scene += bloom * _FenglingBloomIntensity;
                scene *= exp2(_FenglingExposure);
                scene = ACESFilm(scene);

                float luma = dot(scene, float3(0.2126, 0.7152, 0.0722));
                scene = lerp(luma.xxx, scene, _FenglingSaturation);
                scene = saturate((scene - 0.5) * _FenglingContrast + 0.5);

                return float4(scene, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Bright"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _FenglingBloomThreshold;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 c = tex2D(_MainTex, i.uv).rgb;
                float brightness = max(c.r, max(c.g, c.b));
                float contribution = max(0.0, brightness - _FenglingBloomThreshold);
                c *= contribution / max(brightness, 1e-4);
                return float4(c, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Blur"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _FenglingBlurTexel;
            float4 _FenglingBlurDir;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 offset = _FenglingBlurTexel.xy * _FenglingBlurDir.xy;
                float3 c = tex2D(_MainTex, i.uv).rgb * 0.227027;
                c += tex2D(_MainTex, i.uv + offset * 1.384615).rgb * 0.316216;
                c += tex2D(_MainTex, i.uv - offset * 1.384615).rgb * 0.316216;
                c += tex2D(_MainTex, i.uv + offset * 3.230769).rgb * 0.070270;
                c += tex2D(_MainTex, i.uv - offset * 3.230769).rgb * 0.070270;
                return float4(c, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
