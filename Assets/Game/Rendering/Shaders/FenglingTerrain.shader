Shader "Fengling/Terrain"
{
    // 地形专用 shader。
    // 关键：Unity Terrain 按 patch 实例化绘制，顶点里带 per-instance 的 patch 偏移，
    // 必须用 TerrainInstancing 重建顶点位置（照抄 URP 的 TerrainLitInput.hlsl）。
    Properties
    {
        _SunColorTint ("Sun Tint", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "FenglingForward" "RenderType" = "Opaque" "Queue" = "Geometry" }
        ZWrite On
        ZTest LEqual
        Cull Off

        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap
            #include "UnityCG.cginc"

            // ---- 地形实例化数据（照抄 URP）
            CBUFFER_START(_Terrain)
            float4 _TerrainHeightmapRecipSize;   // zw = 1/(w-1), 1/(h-1)
            float4 _TerrainHeightmapScale;       // xz = 世界尺寸，y = 高度
            UNITY_INSTANCING_BUFFER_START(Terrain)
            UNITY_DEFINE_INSTANCED_PROP(float4, _TerrainPatchInstanceData)
            UNITY_INSTANCING_BUFFER_END(Terrain)
            CBUFFER_END

            sampler2D _TerrainHeightmapTexture;
            sampler2D _TerrainNormalmapTexture;

            #define UnpackHeightmap(h) ((h) * 2.0 - 1.0)

            float4 _SunColorTint;
            float3 _FenglingSunDir;
            float3 _FenglingSunColor;
            float3 _FenglingAmbientColor;
            float _FenglingAmbientScale;
            float _FenglingCloudShadowStrength;
            float _FenglingCloudShadowScale;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
            };

            void TerrainInstancing(inout float4 positionOS, inout float3 normal, inout float2 uv)
            {
            #ifdef UNITY_INSTANCING_ENABLED
                float2 patchVertex = positionOS.xy;
                float4 instanceData = UNITY_ACCESS_INSTANCED_PROP(Terrain, _TerrainPatchInstanceData);

                float2 sampleCoords = (patchVertex.xy + instanceData.xy) * instanceData.z;
                float height = UnpackHeightmap(_TerrainHeightmapTexture.Load(int3(sampleCoords, 0)).r);

                positionOS.xz = sampleCoords * _TerrainHeightmapScale.xz;
                positionOS.y = height * _TerrainHeightmapScale.y;

                normal = _TerrainNormalmapTexture.Load(int3(sampleCoords, 0)).rgb * 2 - 1;
                uv = sampleCoords * _TerrainHeightmapRecipSize.zw;
            #endif
            }

            v2f vert(appdata_base v)
            {
                v2f o;
                float4 posOS = v.vertex;
                float3 normalOS = v.normal;
                float2 uv = 0;

                TerrainInstancing(posOS, normalOS, uv);

                o.pos = UnityObjectToClipPos(posOS);
                o.worldPos = mul(unity_ObjectToWorld, posOS).xyz;
                o.worldNormal = UnityObjectToWorldNormal(normalOS);
                return o;
            }

            // 低频噪声：打破分带硬边
            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float4 frag(v2f i) : SV_Target
            {
                float h = i.worldPos.y;

                float jitter = (Hash21(floor(i.worldPos.xz * 0.05)) - 0.5) * 14.0;
                h += jitter;

                float3 sand  = float3(0.788, 0.702, 0.576);
                float3 grass = float3(0.498, 0.627, 0.353);
                float3 grass2= float3(0.588, 0.702, 0.420);
                float3 forest= float3(0.247, 0.357, 0.227);
                float3 rock  = float3(0.431, 0.416, 0.478);

                float3 albedo = sand;
                albedo = lerp(albedo, grass,  smoothstep(10.0, 30.0, h));
                albedo = lerp(albedo, grass2, smoothstep(30.0, 60.0, h) * 0.6);
                albedo = lerp(albedo, forest, smoothstep(80.0, 130.0, h));
                albedo = lerp(albedo, rock,   smoothstep(180.0, 260.0, h));

                float slope = 1.0 - saturate(dot(normalize(i.worldNormal), float3(0, 1, 0)));
                albedo = lerp(albedo, rock, smoothstep(0.35, 0.7, slope));

                // 简易光照：太阳 + 环境
                float3 N = normalize(i.worldNormal);
                float ndl = saturate(dot(N, normalize(-_FenglingSunDir)));
                float3 lit = albedo * (_FenglingSunColor.rgb * ndl + _FenglingAmbientColor.rgb * _FenglingAmbientScale);

                return float4(lit * _SunColorTint.rgb, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
