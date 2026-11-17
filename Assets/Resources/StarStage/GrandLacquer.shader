Shader "PianoTeacher3D/GrandLacquer"
{
    Properties
    {
        _BaseColor ("琴漆底色", Color) = (0.055,0.065,0.09,1)
    }

    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            // 两块虚拟柔光板模拟舞台反光。反射向量随网格法线和观察方向变化。
            float Softbox(float3 reflection, float3 axis, float3 horizontal, float2 spread)
            {
                float3 vertical = cross(axis, horizontal);
                float2 position = float2(dot(reflection, horizontal), dot(reflection, vertical)) / spread;
                float falloff = dot(position, position);
                return exp(-falloff) * smoothstep(0.45, 0.92, dot(reflection, axis));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normal = normalize(input.normalWS);
                float3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float3 reflection = reflect(-view, normal);
                float facing = saturate(dot(normal, view));
                float edge = pow(1.0 - facing, 3.0);
                float fresnel = 0.15 + 0.62 * edge;
                float3 coolAxis = normalize(float3(0.45, 0.72, -0.53));
                float3 warmAxis = normalize(float3(-0.49, -0.42, -0.76));
                float cool = Softbox(reflection, coolAxis, normalize(cross(float3(0,1,0), coolAxis)), float2(0.85, 0.36));
                float warm = Softbox(reflection, warmAxis, normalize(cross(float3(0,1,0), warmAxis)), float2(0.77, 0.28));
                float rim = pow(1.0 - facing, 4.0) * smoothstep(-0.65, 0.65, reflection.y);
                Light light = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                float diffuse = saturate(dot(normal, light.direction)) * light.shadowAttenuation;
                float side = smoothstep(-0.9, 0.8, normal.x * 0.5 + normal.y * 0.7 - normal.z * 0.2);
                float3 color = _BaseColor.rgb * (0.45 + 0.52 * side + 0.35 * diffuse * light.color);
                color *= lerp(float3(1.0, 1.0, 1.0), float3(0.52, 0.86, 1.08), edge);
                color += fresnel * (cool * float3(0.30, 0.66, 0.96) + warm * float3(0.46, 0.42, 0.31));
                color += rim * float3(0.025, 0.16, 0.24);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
}
