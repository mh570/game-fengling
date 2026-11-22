Shader "Custom/LeafImported"
{
    // 忠实复刻 Leaf.shadergraph（UniversalUnlitSubTarget = UNLIT）
    // 节点结构: dot(_normal, SUN_DIR) → Remap → SampleGradient → * _Color * texture → Unlit
    // DrawLeaf.cs 通过 MaterialPropertyBlock 传 _normal / _speedOffset / _lightOffset

    Properties
    {
        _MainTex ("Leaf Mask", 2D) = "white" {}
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        _Color ("Color", Color) = (0.494, 0.937, 0.384, 1)

        // 渐变（UNLIT 下用手工渐变，等价 SampleGradient）
        _GradDark  ("Gradient 0.0", Color) = (0.230, 0.330, 0.170, 1)
        _GradMid1  ("Gradient 0.33", Color) = (0.400, 0.520, 0.290, 1)
        _GradMid2  ("Gradient 0.66", Color) = (0.580, 0.700, 0.430, 1)
        _GradLight ("Gradient 1.0", Color) = (0.760, 0.880, 0.590, 1)

        _SunDir ("Sun Direction", Vector) = (0.35, 0.85, 0.40, 0)
        _LightOffsetScale ("Light Offset Scale", Float) = 0.396
        _WindStrength ("Wind", Float) = 0.06
    }

    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite On

        Pass
        {
            Name "Unlit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            sampler2D _MainTex;

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Cutoff;
                half4 _Color;
                half4 _GradDark, _GradMid1, _GradMid2, _GradLight;
                float4 _SunDir;
                float _LightOffsetScale;
                float _WindStrength;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _normal)
                UNITY_DEFINE_INSTANCED_PROP(float, _speedOffset)
                UNITY_DEFINE_INSTANCED_PROP(float, _lightOffset)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float2 uv          : TEXCOORD1;
                float  lightOff    : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);

                float3 sphereN  = UNITY_ACCESS_INSTANCED_PROP(Props, _normal).xyz;
                float  lightOff = UNITY_ACCESS_INSTANCED_PROP(Props, _lightOffset);
                float  speedOff = UNITY_ACCESS_INSTANCED_PROP(Props, _speedOffset);

                float4 instancePos = float4(
                    UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23, 1);

                // 柱面 billboard
                float3 toCam = _WorldSpaceCameraPos - instancePos.xyz;
                toCam.y = 0;
                float len = length(toCam);
                toCam = len > 1e-4 ? toCam / len : float3(0, 0, 1);
                float3 rightV = normalize(cross(float3(0, 1, 0), toCam));
                float3 upV = float3(0, 1, 0);

                float sx = length(float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20));
                float sy = length(float3(UNITY_MATRIX_M._m01, UNITY_MATRIX_M._m11, UNITY_MATRIX_M._m21));
                float3 localOff = IN.positionOS.xyz * float3(sx, sy, sx);

                float sway = sin(_Time.y * 2.0 + speedOff) * _WindStrength;
                localOff.x += sway * (IN.positionOS.y + 0.5);

                float3 posWS = instancePos.xyz + rightV * localOff.x + upV * localOff.y + toCam * localOff.z;

                OUT.positionHCS = TransformWorldToHClip(posWS);
                OUT.normalWS = sphereN;
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.lightOff = lightOff;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 mask = tex2D(_MainTex, IN.uv);
                clip(mask.a - _Cutoff);

                // ---- UNLIT：手动算 N·L（太阳方向硬编码），不吃实时光
                float3 N = normalize(IN.normalWS);
                float3 L = normalize(_SunDir.xyz);
                float ndl = dot(N, L);

                // Remap: -1..1 → 0..1
                float t = saturate(ndl * 0.5 + 0.5);

                // 每片叶子的随机光照偏移
                t = saturate(t + (IN.lightOff - 0.5) * _LightOffsetScale);

                // SampleGradient（4 段）
                float3 g;
                if (t < 0.3333)       g = lerp(_GradDark.rgb,  _GradMid1.rgb,  t / 0.3333);
                else if (t < 0.6667)  g = lerp(_GradMid1.rgb,  _GradMid2.rgb,  (t - 0.3333) / 0.3334);
                else                  g = lerp(_GradMid2.rgb,  _GradLight.rgb, (t - 0.6667) / 0.3333);

                // * _Color * 遮罩
                float3 col = g * _Color.rgb;

                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
