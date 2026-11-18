// 海底粒子（2026-11-18）：气泡与海雪共用，形状在片元里按 UV 算，不需要贴图。
// _Mode = 0：气泡——透明球体，亮边 + 左上高光点，中心近乎透明；
// _Mode = 1：海雪——柔和的小光点。颜色与透明度来自粒子顶点色（ParticleSystem 的 Color over Lifetime）。
Shader "PianoTeacher3D/OceanParticle"
{
    Properties
    {
        _Tint ("Tint", Color) = (0.75, 0.95, 1.0, 1)
        _Mode ("Mode (0 bubble, 1 snow)", Float) = 0
        _Intensity ("Intensity", Range(0, 4)) = 1.2
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
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
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                half _Mode;
                half _Intensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float fog : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.color = input.color;
                o.uv = input.uv;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 p = input.uv * 2.0 - 1.0;
                float r = length(p);
                if (r > 1.0) discard;
                half alpha;
                half3 color;
                if (_Mode < 0.5)
                {
                    float rim = smoothstep(0.62, 0.95, r) * (1.0 - smoothstep(0.95, 1.0, r));
                    float spot = 1.0 - smoothstep(0.0, 0.28, length(p - float2(-0.35, 0.38)));
                    float body = 0.10 * (1.0 - r);
                    alpha = saturate(rim * 0.75 + spot * 0.9 + body);
                    color = _Tint.rgb * (0.65 + rim * 0.6) + spot;
                }
                else
                {
                    float soft = 1.0 - smoothstep(0.0, 1.0, r);
                    alpha = soft * soft;
                    color = _Tint.rgb;
                }
                color *= _Intensity * input.color.rgb;
                color = MixFog(color, input.fog);
                return half4(color, alpha * input.color.a);
            }
            ENDHLSL
        }
    }
}
