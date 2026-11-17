Shader "PianoTeacher3D/StageRibbon"
{
    Properties
    {
        [HDR] _Tone ("光色", Color) = (0.15,0.9,1,1)
        _Effects ("动效强度", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent+4" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Tone;
                float _Effects;
            CBUFFER_END
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
                float t = input.uv.x;
                float v = input.uv.y;
                float ends = smoothstep(0.0, 0.22, t) * (1.0 - smoothstep(0.74, 1.0, t));
                float crease = 0.47 + 0.23 * sin(t * 17.0 - _Time.y * 1.15);
                float highlight = exp(-abs(v - crease) * 28.0);
                float outer = exp(-abs(v - 0.79) * 48.0) * 0.28;
                float sheet = sin(v * 3.14159) * (0.18 + 0.11 * sin(t * 32.0 + v * 5.0 - _Time.y));
                float wave = 0.63 + 0.37 * sin(t * 15.0 - _Time.y * 1.7);
                float intensity = ends * (highlight * (0.48 + 0.47 * wave) + outer + sheet);
                float energy = intensity * (0.58 + 0.42 * _Effects);
                float3 color = lerp(_Tone.rgb, float3(1.4, 1.18, 0.92), highlight * wave * 0.36);
                return half4(color * energy, saturate(energy * 0.38));
            }
            ENDHLSL
        }
    }
}
