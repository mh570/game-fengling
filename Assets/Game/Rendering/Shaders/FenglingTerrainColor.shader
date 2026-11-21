Shader "Fengling/TerrainColor"
{
    // 地形顶点色 shader。
    // 几何是平面着色的（网格里每个三角形的顶点法线一致），所以这里不用再处理法线平滑。
    // 颜色完全来自顶点色（分带 + 坡度 + 抖动），不需要贴图。
    Properties
    {
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _MainTex ("Ground Tex", 2D) = "white" {}
        _TexScale ("Tex Scale (per meter)", Float) = 0.5
        _TexStrength ("Tex Strength", Range(0, 1)) = 0.65
    }

    SubShader
    {
        Tags { "RenderPipeline" = "FenglingForward" "RenderType" = "Opaque" "Queue" = "Geometry" }
        ZWrite On
        ZTest LEqual
        Cull Off

        Pass
        {
            Tags { "LightMode" = "FenglingForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            float4 _Tint;
            sampler2D _MainTex;
            float _TexScale;
            float _TexStrength;
            float3 _FenglingSunDir;
            float3 _FenglingSunColor;
            float3 _FenglingAmbientColor;
            float _FenglingAmbientScale;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 normal : TEXCOORD0;
                float4 color : COLOR;
                float3 worldPos : TEXCOORD2;
            };

            v2f vert(appdata_full v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.color = v.color;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 N = normalize(i.normal);
                float ndl = saturate(dot(N, normalize(-_FenglingSunDir)));

                // 半兰伯特 + 环境：压低对比，参考视频是高环境光低对比
                float wrap = ndl * 0.45 + 0.55;   // 压低对比，避免暗部发黑

                // 贴图调制：叠加草地纹理细节（沙地和草地都受益）
                float2 uv = i.worldPos.xz * _TexScale;
                float3 tex = tex2D(_MainTex, uv).rgb;
                float3 baseCol = i.color.rgb * lerp(float3(1,1,1), tex * 2.0, _TexStrength);

                float3 lit = baseCol *
                             (_FenglingSunColor.rgb * wrap * 0.50 +
                              _FenglingAmbientColor.rgb * _FenglingAmbientScale * 0.30);

                // 稍微提饱和，抵消过曝后的发灰
                float l = dot(lit, float3(0.299, 0.587, 0.114));
                lit = lerp(float3(l, l, l), lit, 1.35);
                lit = saturate(lit);

                return float4(lit * _Tint.rgb, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
