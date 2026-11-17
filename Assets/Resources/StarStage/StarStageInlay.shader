Shader "PianoTeacher3D/StarStageInlay"
{
    SubShader
    {
        Tags { "Queue"="Transparent+2" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.uv * 2.0 - 1.0;
                float radius = length(p);
                float angle = atan2(p.y, p.x) * 0.159154943;
                float aa = max(0.001, fwidth(radius) * 0.72);
                float outer = 1.0 - smoothstep(0.002, 0.002 + aa, abs(radius - 0.944));
                float inner = 1.0 - smoothstep(0.0015, 0.0015 + aa, abs(radius - 0.895));
                float dash = smoothstep(0.16, 0.30, abs(frac(angle * 12.0) - 0.5));
                float diamond = abs(frac(angle * 12.0 + 0.5) - 0.5) * 3.05 + abs(radius - 0.919) * 48.0;
                float accents = 1.0 - smoothstep(0.88, 1.06, diamond);
                float time = _Time.y;
                float wave = sin(p.x * 23 + p.y * 10 + 1.3 * sin(p.y * 14 - time * 0.34));
                float crossing = sin(p.y * 23 - p.x * 12 + sin(p.x * 12 + time * 0.29));
                float caustic = pow(saturate(1 - abs(wave + 0.88 * crossing)), 6);
                caustic *= (1 - smoothstep(0.70, 0.89, radius)) * (0.34 + 0.66 * saturate(p.x + 0.45));
                float trim = max(outer * 0.36, max(inner * dash * 0.25, accents * 0.42));
                return half4(0.30, 0.55, 0.65, max(trim, caustic * 0.075));
            }
            ENDHLSL
        }
    }
}
