// 下落音符（2026-11-18）：圆角发光条。uv = 条内归一化坐标，uv2.x = 条的物理高度 / 宽度（保持圆角为正圆），
// uv2.y = 「已落到琴键」的程度（0–1，落键时亮度提高）；颜色来自顶点色（左手青、右手金）。
Shader "PianoTeacher3D/FallingNote"
{
    Properties
    {
        _Glow ("Glow", Range(0, 4)) = 1.4
        _Opacity ("Opacity", Range(0, 1)) = 0.82
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Glow;
                half _Opacity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 shape : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.color = input.color;
                o.uv = input.uv;
                o.shape = input.uv2;
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // 以宽度为单位的圆角矩形距离场：半径 = 半宽
                float aspect = max(1.0, input.shape.x);
                float2 p = float2((input.uv.x - 0.5) * 2.0, (input.uv.y - 0.5) * 2.0 * aspect);
                float2 q = abs(p) - float2(0.0, aspect - 1.0);
                float d = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - 1.0;   // <0 在内部
                float body = 1.0 - smoothstep(-0.10, 0.02, d);
                float rim = (1.0 - smoothstep(0.0, 0.25, abs(d + 0.12))) * body;
                float landing = input.shape.y;
                half3 color = input.color.rgb * (0.75 + 0.35 * rim) * _Glow * (1.0 + landing * 0.8);
                color += rim * 0.35;
                half alpha = body * _Opacity * input.color.a;
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
