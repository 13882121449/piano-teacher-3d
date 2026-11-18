// 竖琴钢琴的琴弦（2026-11-18）：uv.x = 弦号（0 = A0），uv.y = 沿弦位置（0 底端 / 1 顶端）。
// 振幅来自全局数组 _PT3D_HarpAmp[88]（HarpStringVibration 每帧写入）：
// 弦中段按 sin(πt) 横向摆动，同时按振幅发出青色辉光（配合泛光读出「这根弦在响」）。
Shader "PianoTeacher3D/HarpString"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.80, 0.84, 0.88, 1)
        _GlowColor ("Glow Color", Color) = (0.45, 0.95, 1.0, 1)
        _GlowIntensity ("Glow Intensity", Range(0, 8)) = 3.2
        _Displacement ("Max Displacement (m)", Range(0, 0.01)) = 0.0035
        _Gloss ("Gloss", Range(4, 256)) = 90
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _GlowColor;
            half _GlowIntensity;
            float _Displacement;
            half _Gloss;
        CBUFFER_END

        float _PT3D_HarpAmp[88];

        float3 DisplaceString(float3 positionOS, float2 uv, out float amp)
        {
            int index = clamp((int)round(uv.x), 0, 87);
            amp = _PT3D_HarpAmp[index];
            float envelope = sin(saturate(uv.y) * PI);
            // 视觉频率取 8–15 Hz：60 fps 下能读出往复摆动而不是闪烁
            float frequency = lerp(8.0, 15.0, index / 87.0);
            float wave = sin(_Time.y * frequency * 6.2831853 + index * 1.7);
            positionOS.x += amp * envelope * wave * _Displacement;
            return positionOS;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float amp : TEXCOORD2;
                float fog : TEXCOORD3;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                float amp;
                float3 positionOS = DisplaceString(input.positionOS.xyz, input.uv, amp);
                o.positionWS = TransformObjectToWorld(positionOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.amp = amp;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalWS);
                float3 v = normalize(GetWorldSpaceViewDir(input.positionWS));
                Light light = GetMainLight();
                float ndl = saturate(dot(n, light.direction)) * 0.65 + 0.35;
                float3 h = normalize(light.direction + v);
                float spec = pow(saturate(dot(n, h)), _Gloss) * 1.4;
                float3 ambient = SampleSH(n);
                float3 color = _BaseColor.rgb * (ambient + light.color * ndl) + light.color * spec;
                color += _GlowColor.rgb * (_GlowIntensity * input.amp);
                color = MixFog(color, input.fog);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            float4 vert(Attributes input) : SV_POSITION
            {
                float amp;
                return TransformObjectToHClip(DisplaceString(input.positionOS.xyz, input.uv, amp));
            }

            half frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
