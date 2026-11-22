Shader "Custom/LeafImported"
{
    // 用 HLSL 重写他们的 Leaf.shadergraph（Unity 6 下 shader graph 编译失败）
    // 节点结构：Camera→Cross→Matrix = billboard；normal = 球面法线；Dot(N,L)→Sample Gradient→BaseColor
    // DrawLeaf.cs 通过 MaterialPropertyBlock 传 _normal / _speedOffset / _lightOffset 数组

    Properties
    {
        _MainTex ("Leaf Texture", 2D) = "white" {}
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.6
        _ColorA ("Gradient Dark", Color) = (0.13, 0.22, 0.10, 1)
        _ColorB ("Gradient Mid",  Color) = (0.38, 0.50, 0.33, 1)
        _ColorC ("Gradient Light",Color) = (0.54, 0.66, 0.43, 1)
        _LightOffsetScale ("Light Offset Scale", Float) = 0.396
        _WindStrength ("Wind", Float) = 0.08
    }

    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite On

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            sampler2D _MainTex;

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Cutoff;
                half4 _ColorA;
                half4 _ColorB;
                half4 _ColorC;
                float _LightOffsetScale;
                float _WindStrength;
            CBUFFER_END

            // DrawLeaf.cs 通过 MaterialPropertyBlock 传入的每实例数据
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

                // 球面法线（来自 MaterialPropertyBlock）
                float3 sphereN = UNITY_ACCESS_INSTANCED_PROP(Props, _normal).xyz;
                float  lightOff = UNITY_ACCESS_INSTANCED_PROP(Props, _lightOffset);
                float  speedOff = UNITY_ACCESS_INSTANCED_PROP(Props, _speedOffset);

                // 实例矩阵的平移 = 这片叶子的位置
                float4 instancePos = float4(
                    UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23, 1);

                // 柱面 billboard：围绕实例位置，按相机方向重建
                float3 toCam = _WorldSpaceCameraPos - instancePos.xyz;
                toCam.y = 0;
                float len = length(toCam);
                toCam = len > 1e-4 ? toCam / len : float3(0, 0, 1);
                float3 rightV = normalize(cross(float3(0, 1, 0), toCam));
                float3 upV = float3(0, 1, 0);

                // 用实例矩阵的缩放 + 局部偏移
                float sx = length(float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20));
                float sy = length(float3(UNITY_MATRIX_M._m01, UNITY_MATRIX_M._m11, UNITY_MATRIX_M._m21));
                float3 localOff = IN.positionOS.xyz * float3(sx, sy, sx);

                // 风：按 speedOffset 相位摆动
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
                half4 tex = tex2D(_MainTex, IN.uv);
                clip(tex.a - _Cutoff);

                float3 N = normalize(IN.normalWS);
                Light mainLight = GetMainLight();
                float ndl = dot(N, mainLight.direction);
                float q = saturate(ndl * 0.5 + 0.5);

                // 每片叶子的光照偏移（他们的 _lightOffset × LightOffset 0.396）
                q = saturate(q + (IN.lightOff - 0.5) * _LightOffsetScale);

                float3 cA = _ColorA.rgb, cB = _ColorB.rgb, cC = _ColorC.rgb;
                float3 col = q < 0.5 ? lerp(cA, cB, q * 2.0) : lerp(cB, cC, (q - 0.5) * 2.0);

                float3 ambient = SampleSH(N) * 0.12;
                col += ambient;

                // 贴图只当形状遮罩用：透明区的 RGB 是品红，不能用
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
