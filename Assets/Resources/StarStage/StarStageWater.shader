Shader "PianoTeacher3D/StarStageWater"
{
    Properties
    {
        _BaseColor ("底色", Color) = (0.018,0.075,0.19,0.76)
        _Reflection ("倒影模式", Float) = 0
        _WaterY ("倒影平面高度", Float) = 0
        _Effects ("动效强度", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _Reflection;
                float _WaterY;
                float _Effects;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 world : TEXCOORD0; float3 normal : TEXCOORD1; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.world = TransformObjectToWorld(input.positionOS.xyz);
                output.normal = TransformObjectToWorldNormal(input.normalOS);
                if (_Reflection > 0.5)
                {
                    output.world.y = 2.0 * _WaterY - output.world.y - 0.006;
                    output.world.x += sin(output.world.y * 125.0 + output.world.z * 9.0) * 0.006;
                }
                output.positionCS = TransformWorldToHClip(output.world);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                if (_Reflection > 0.5)
                {
                    float light = 0.45 + 0.55 * abs(input.normal.y);
                    float fade = exp(2.0 * min(0.0, input.world.y - _WaterY));
                    return half4(_BaseColor.rgb * light, fade * _BaseColor.a);
                }

                float3 view = normalize(_WorldSpaceCameraPos - input.world);
                float grazing = pow(1.0 - saturate(view.y), 4);
                float2 p = input.world.xz - float2(0.0, -0.32);
                float time = _Time.y;
                float radius = length(p);
                float phase = frac(time * 0.37);
                float ringDistance = abs(radius - (1.81 + phase * 2.1));
                float ring = (1 - smoothstep(0.012, 0.045 + 0.05 * phase, ringDistance));
                ring *= smoothstep(0.0, 0.18, phase) * (1 - phase);

                // 椭长的水光随相位缓慢滑动，远水仅留色带以减少闪烁。
                float2 waterAxes = float2(dot(p, float2(-0.32, 0.95)), dot(p, float2(0.95, 0.32)));
                float waviness = sin(waterAxes.x * 6.3 + waterAxes.y * 2.1);
                float wave = sin(waterAxes.y * 11.2 + waviness * 1.7 + time * 0.62);
                float crossing = sin(waterAxes.x * 7.3 - waterAxes.y * 2.9 - time * 0.4);
                float gleam = pow(saturate(1.0 - abs(wave + crossing * 0.86)), 5);
                float patches = sin(p.x * 0.81 + p.y * 0.62 + time * 0.17) *
                    sin(p.y * 1.06 - p.x * 0.22 - time * 0.19);
                gleam *= (1 - smoothstep(2.5, 8.0, radius)) * (0.10 + 0.90 * smoothstep(-0.42, 0.55, patches));
                float shore = exp(-max(0.0, radius - 1.78) * 1.75);
                float3 color = lerp(_BaseColor.rgb, float3(0.023, 0.13, 0.21), grazing);
                color += shore * float3(0.017, 0.055, 0.089);
                color += _Effects * (gleam * float3(0.024, 0.082, 0.115) + ring * float3(0.025, 0.056, 0.095));
                float horizonBlend = 1 - smoothstep(8.0, 24.0, radius);
                return half4(color, lerp(_BaseColor.a - 0.20, _BaseColor.a, grazing) * horizonBlend);
            }
            ENDHLSL
        }
    }
}
