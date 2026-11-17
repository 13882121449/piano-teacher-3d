Shader "PianoTeacher3D/ConcertHeadReference"
{
    Properties
    {
        [MainTexture] _BaseMap ("参考三视图", 2D) = "white" {}
        _HeadForward ("头部朝向", Vector) = (0,0,1,0)
        [MainColor] _BaseColor ("色调", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Name "ConcertFace"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST, _BaseColor, _HeadForward;
            CBUFFER_END

            struct Attributes
            {
                float4 position : POSITION;
                float3 normal : NORMAL;
                float2 weights : TEXCOORD0;
                float4 projection : TEXCOORD1;
            };
            struct Varyings
            {
                float4 clip : SV_POSITION;
                float3 normal : TEXCOORD0;
                float4 projection : TEXCOORD1;
                float2 weights : TEXCOORD2;
                half fog : TEXCOORD3;
                float3 world : TEXCOORD4;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.clip = TransformObjectToHClip(v.position.xyz);
                o.normal = TransformObjectToWorldNormal(v.normal);
                o.world = TransformObjectToWorld(v.position.xyz);
                o.projection = v.projection;
                o.weights = v.weights;
                o.fog = ComputeFogFactor(o.clip.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float y = (258.0 - i.projection.y - 0.5) / 256.0;
                half4 front = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, float2((i.projection.x - 321.5) / 512.0, y));
                half4 side = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, float2((i.projection.z - 639.5) / 512.0, y));
                half4 rear = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, float2((i.projection.w - 983.5) / 512.0, y));
                half3 weights = half3(i.weights.x, 1 - i.weights.x - i.weights.y, i.weights.y);
                half3 sampled = half3(front.a, side.a, rear.a);
                float facing = dot(GetWorldSpaceNormalizeViewDir(i.world), TransformObjectToWorldDir(_HeadForward.xyz));
                float profile = 1.0 - smoothstep(0.08, 0.53, abs(facing));
                weights = lerp(weights, half3(0, 1, 0), profile * smoothstep(0.3, 0.91, side.a));
                half3 regular = weights * sampled;
                half3 secondary = sampled * half3(0.04, 0.17, 0.04);
                half3 blend = regular + (1 - saturate(dot(regular, 1.0))) * secondary;
                half sum = dot(blend, 1.0);
                half3 color = (front.rgb * blend.x + side.rgb * blend.y + rear.rgb * blend.z) / max(sum, 0.001);
                half3 skin = SRGBToLinear(half3(0.73, 0.55, 0.46));
                half3 hair = SRGBToLinear(half3(0.19, 0.15, 0.13));
                half3 extra = lerp(hair, skin, smoothstep(83.0, 108.0, i.projection.y) * (1 - i.weights.y));
                color = lerp(extra, color, saturate(sum * 10.0));
                color = lerp(color, skin, smoothstep(110.5, 120.5, i.projection.y));
                half3 normal = normalize(i.normal);
                Light key = GetMainLight();
                half3 light = SampleSH(normal) * 0.29 + 0.72;
                light += key.color * saturate(dot(normal, key.direction)) * 0.24;
                return half4(MixFog(color * light * _BaseColor.rgb, i.fog), 1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
