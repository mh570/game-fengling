Shader "Fengling/Outline"
{
    Properties
    {
        _LineColor ("Line Color", Color) = (1, 1, 1, 1)
        _DepthThreshold ("Depth Threshold", Range(0.001, 0.5)) = 0.06
        _NormalThreshold ("Normal Threshold", Range(0.01, 1)) = 0.25
        _LineWidth ("Line Width (px)", Range(0.5, 4)) = 1.5
        _DashScale ("Dash Scale", Float) = 90
        _DashDensity ("Dash Density", Range(0, 1)) = 0.55
        _Opacity ("Opacity", Range(0, 1)) = 0.85
    }

    SubShader
    {
        Tags { "RenderPipeline" = "FenglingForward" "RenderType" = "Opaque" }
        ZWrite Off ZTest Always Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            sampler2D _FenglingSceneDepth;   // 我们的线性深度（View 空间 Z）
            float4 _FenglingSceneDepth_TexelSize;
            float4 _LineColor;
            float _DepthThreshold;
            float _NormalThreshold;
            float _LineWidth;
            float _DashScale;
            float _DashDensity;
            float _Opacity;

            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(uint vid : SV_VertexID)
            {
                v2f o;
                float2 uv = float2((vid << 1) & 2, vid & 2);
                o.pos = float4(uv * 2.0 - 1.0, 0.0, 1.0);
                o.uv = uv;
                return o;
            }

            float Depth(float2 uv) { return tex2D(_FenglingSceneDepth, uv).r; }

            float4 frag(v2f i) : SV_Target
            {
                float2 t = _FenglingSceneDepth_TexelSize.xy * _LineWidth;

                // Sobel：四邻域深度差
                float d0 = Depth(i.uv);
                float dl = Depth(i.uv - float2(t.x, 0));
                float dr = Depth(i.uv + float2(t.x, 0));
                float du = Depth(i.uv + float2(0, t.y));
                float dd = Depth(i.uv - float2(0, t.y));

                float gx = abs(dr - dl);
                float gy = abs(du - dd);

                // 连续性过滤：排除"天空/水面接缝"这类远距离跳变。
                // 水面几何与背景的深度差极大，不过滤会被当成边缘（那几条竖白线）。
                float minD = min(min(min(dl, dr), min(du, dd)), d0);
                float maxD = max(max(max(dl, dr), max(du, dd)), d0);
                float spread = maxD / max(0.001, minD);
                bool valid = (minD > 0.01) && (spread < 30.0);

                float edge = valid ? max(gx, gy) : 0.0;

                // ---- 用深度梯度重建法线（不需要额外的法线 pass，避开绘制时序问题）
                // 原理：深度的横向/纵向梯度近似表面的倾斜方向。
                // 对"边缘检测"用途足够精确；需要物理精确法线时再换逆投影叉积。
                float3 nC = normalize(float3(dl - dr, dd - du, 2.0 * t.x * max(0.001, d0)));
                float3 nL = normalize(float3(Depth(i.uv - float2(2.0 * t.x, 0)) - d0,
                                             Depth(i.uv - float2(t.x, t.y)) - Depth(i.uv - float2(t.x, -t.y)),
                                             2.0 * t.x * max(0.001, dl)));
                float3 nR = normalize(float3(Depth(i.uv + float2(t.x, t.y)) - Depth(i.uv + float2(t.x, -t.y)),
                                             d0 - Depth(i.uv + float2(2.0 * t.x, 0)),
                                             2.0 * t.x * max(0.001, dr)));

                // 法线差：抓同深度、不同朝向的交界（路面贴地、石头接草）
                float normalEdge = max(distance(nC, nL), distance(nC, nR));
                normalEdge = max(normalEdge, distance(nC, normalize(float3(
                    Depth(i.uv + float2(t.x, t.y)) - Depth(i.uv - float2(t.x, t.y)),
                    d0 - Depth(i.uv + float2(0, 2.0 * t.y)),
                    2.0 * t.x * max(0.001, d0)))));

                float edgeTotal = max(edge, valid ? normalEdge * _NormalThreshold * 10.0 : 0.0);

                // 相对阈值：远处深度差本来就大，用深度归一化避免远处满屏描边
                float rel = edgeTotal / max(0.001, d0 * _DepthThreshold * 8.0);
                // 注意：不能叫 line —— HLSL 里它是几何着色器的保留关键字
                float outlineMask = saturate(rel);

                // 虚线：屏幕坐标 + 噪声产生断续
                float2 p = i.uv * _DashScale;
                float n = frac(sin(dot(floor(p), float2(12.9898, 78.233))) * 43758.5453);
                float dash = step(1.0 - _DashDensity, n);

                float a = outlineMask * dash * _Opacity;
                return float4(_LineColor.rgb, a);

            }
            ENDHLSL
        }
    }
    Fallback Off
}
