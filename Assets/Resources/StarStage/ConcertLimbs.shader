Shader "PianoTeacher3D/ConcertLimbs"
{
    Properties
    {
        [MainTexture] _BaseMap ("角色底图", 2D) = "white" {}
        [MainColor] _BaseColor ("角色色调", Color) = (1,1,1,1)
        [Normal] _BumpMap ("法线贴图", 2D) = "bump" {}
        _BumpScale ("法线强度", Float) = 1
        _Cull ("剔除模式", Float) = 2
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Cull [_Cull]
        Pass
        {
            Name "ConcertLimbs"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST, _BaseColor;
                float _BumpScale;
                float _Cull;
            CBUFFER_END

            struct Attributes
            {
                float4 position : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                float2 uv : TEXCOORD0;
                float2 bindPosition : TEXCOORD1;
            };
            struct Varyings
            {
                float4 clip : SV_POSITION;
                float3 world : TEXCOORD0;
                float3 normal : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float2 bindPosition : TEXCOORD3;
                half fog : TEXCOORD4;
                float4 tangent : TEXCOORD5;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.world = TransformObjectToWorld(v.position.xyz);
                o.clip = TransformWorldToHClip(o.world);
                o.normal = TransformObjectToWorldNormal(v.normal);
                o.tangent = float4(TransformObjectToWorldDir(v.tangent.xyz), v.tangent.w * GetOddNegativeScale());
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.bindPosition = v.bindPosition;
                o.fog = ComputeFogFactor(o.clip.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half3 source = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb;
                half3 skin = SRGBToLinear(half3(0.73, 0.55, 0.46));
                // 2026-11-18 精修：底图本身是皮肤的地方（手、前臂：红明显高于蓝）保留真实纹理细节，
                // 衬衫布料（偏蓝）处才换成肤色；靠近肘部（bindPosition.y = 前臂进度）渐变回纯肤色，
                // 与程序化上臂的接缝保持不可见。腿脚（bindPosition.x 低于 0.155）照旧直接用底图。
                half skinness = smoothstep(0.03h, 0.12h, source.r - source.b);
                half nearElbow = smoothstep(0.24h, 0.42h, i.bindPosition.y);
                half detail = skinness * nearElbow;
                bool limb = i.bindPosition.x >= 0.155;
                half3 albedo = limb ? lerp(skin, source, detail) : source;

                // 法线贴图：只在保留真实皮肤纹理的地方生效，布料替换区保持平滑
                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv), _BumpScale);
                normalTS = normalize(lerp(half3(0, 0, 1), normalTS, limb ? detail : 1.0h));
                float3 bitangent = i.tangent.w * cross(i.normal, i.tangent.xyz);
                float3 normalWS = TransformTangentToWorld(normalTS, half3x3(i.tangent.xyz, bitangent, i.normal));

                InputData input = (InputData)0;
                input.positionWS = i.world;
                input.positionCS = i.clip;
                input.normalWS = NormalizeNormalPerPixel(normalWS);
                input.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.world);
                input.shadowCoord = TransformWorldToShadowCoord(i.world);
                input.bakedGI = SampleSH(input.normalWS);
                input.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.clip);
                input.shadowMask = 1;
                input.vertexLighting = VertexLighting(i.world, input.normalWS);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo * _BaseColor.rgb;
                surface.metallic = 0;
                surface.smoothness = 0.35;
                surface.normalTS = half3(0,0,1);
                surface.alpha = 1;
                surface.occlusion = 1;
                half4 color = UniversalFragmentPBR(input, surface);
                color.rgb = MixFog(color.rgb, i.fog);
                return color;
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
}

