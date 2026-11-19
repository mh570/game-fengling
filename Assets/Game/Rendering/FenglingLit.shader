// 极简 forward Lit：漫反射 + 主光 + SH 环境光 + 轻微高光。
// 走自建管线 FenglingPipeline 的 "FenglingForward" 通道。
// 目的：替掉 HDRP/Lit，让地形/石头/鱼在新管线下不变粉。

Shader "Fengling/Lit"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.75, 0.72, 0.65, 1)
        _BaseMap ("Base Map", 2D) = "white" {}
        _Smoothness ("Smoothness", Range(0, 1)) = 0.15
        _Metallic ("Metallic", Range(0, 1)) = 0
        _AmbientBoost ("Ambient Boost", Range(0, 3)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Name "FenglingForward"
            Tags { "LightMode" = "FenglingForward" }
            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            float4 _BaseColor;
            sampler2D _BaseMap;
            float4 _BaseMap_ST;
            float _Smoothness;
            float _Metallic;
            float _AmbientBoost;

            // 由 FenglingPipeline 每帧写入
            float4 _FenglingSunDir;
            float4 _FenglingSunColor;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.pos = UnityWorldToClipPos(o.worldPos);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 N = normalize(i.worldNormal);
                float3 L = normalize(_FenglingSunDir.xyz);
                float3 V = normalize(_WorldSpaceCameraPos - i.worldPos);

                float3 albedo = _BaseColor.rgb * tex2D(_BaseMap, i.uv).rgb;

                // 漫反射
                float ndl = saturate(dot(N, L));
                float3 diffuse = albedo * _FenglingSunColor.rgb * ndl;

                // 环境光（球谐，来自天空盒）
                float3 ambient = ShadeSH9(float4(N, 1.0)) * albedo * _AmbientBoost;

                // Blinn-Phong 高光
                float3 H = normalize(L + V);
                float spec = pow(saturate(dot(N, H)), exp2(lerp(1.0, 10.0, _Smoothness)));
                float3 specular = _FenglingSunColor.rgb * spec * _Smoothness * (1.0 - _Metallic);

                float3 color = diffuse + ambient + specular;
                return float4(color, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
