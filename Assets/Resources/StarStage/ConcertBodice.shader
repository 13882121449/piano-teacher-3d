Shader "PianoTeacher3D/ConcertBodice"
{
    Properties
    {
        [MainTexture] _BaseMap ("角色底图", 2D) = "white" {}
        [MainColor] _BaseColor ("角色色调", Color) = (1,1,1,1)
        _Satin ("银蓝缎面", Color) = (0.36,0.49,0.61,1)
        _Fit ("躯干比例与朝向", Vector) = (0.4,0.17,1,0.96)
        _Face ("脸部材质", Float) = 0
        _Cull ("剔除模式", Float) = 2
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Cull [_Cull]
        Pass
        {
            Name "ConcertBodice"
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
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST, _BaseColor, _Satin, _Fit;
                float _Face, _Cull;
            CBUFFER_END

            struct Attributes
            {
                float4 position : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                float3 bindPosition : TEXCOORD2;
            };
            struct Varyings
            {
                float4 clip : SV_POSITION;
                float3 world : TEXCOORD0;
                float3 normal : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float3 bindPosition : TEXCOORD3;
                half fog : TEXCOORD4;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.world = TransformObjectToWorld(v.position.xyz);
                o.clip = TransformWorldToHClip(o.world);
                o.normal = TransformObjectToWorldNormal(v.normal);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.bindPosition = v.bindPosition;
                o.fog = ComputeFogFactor(o.clip.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 p = i.bindPosition;
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb;
                half smoothness = 0.33;
                half metallic = 0;
                if (_Face > 0.5)
                {
                    // 头顶与后脑改为棕色，使用原贴图的明度保留发丝纹理。
                    float fromPart = abs(i.uv.x - 0.5);
                    float hairline = 0.88 - 0.182 * smoothstep(0.061, 0.24, fromPart)
                        - 0.142 * smoothstep(0.28, 0.49, fromPart);
                    float crown = smoothstep(hairline - 0.016, hairline + 0.016, i.uv.y);
                    albedo = lerp(albedo, albedo * half3(0.24, 0.19, 0.17), crown);
                    smoothness = lerp(0.33, 0.52, crown);
                }
                else if (p.y + _Fit.w > 0.12)
                {
                    float row = p.y / _Fit.x;
                    float side = abs(p.x) / _Fit.y;
                    float back = smoothstep(-0.02, 0.02, -p.z * _Fit.z);
                    float neckline = lerp(0.70 + 0.20 * side, 0.26 + 1.36 * side * side, back);
                    float inner = 1 - smoothstep(0.73, 0.79, side);
                    float bodice = inner * (1 - smoothstep(neckline - 0.005, neckline + 0.005, row));
                    float strap = (1 - smoothstep(0.041, 0.059, abs(side - 0.62))) *
                        (1 - smoothstep(1.02, 1.075, row));
                    float waist = 1 - smoothstep(0.94, 1.13, side);
                    float cloth = lerp(waist, max(bodice, strap), smoothstep(0.33, 0.66, row));

                    // 人体沿用裸露部位底图，上衣覆盖区域换成相近肤色，再按领口和肩带裁出缎面。
                    float exposed = smoothstep(0.005, 0.022, albedo.r - albedo.g) *
                        smoothstep(0.004, 0.02, albedo.g - albedo.b);
                    half3 skin = lerp(SRGBToLinear(half3(0.74, 0.55, 0.44)), albedo, exposed);
                    float drape = sin(row * 30 + side * side * 8) * exp(-abs(row - 0.74) * 6) * (1 - back);
                    albedo = lerp(skin, _Satin.rgb * (1 + drape * 0.085), cloth);
                    smoothness = lerp(0.33, 0.67, cloth);
                    metallic = 0.07 * cloth;
                }

                InputData input = (InputData)0;
                input.positionWS = i.world;
                input.positionCS = i.clip;
                input.normalWS = NormalizeNormalPerPixel(i.normal);
                input.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.world);
                input.shadowCoord = TransformWorldToShadowCoord(i.world);
                input.bakedGI = SampleSH(input.normalWS);
                input.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.clip);
                input.shadowMask = 1;
                input.vertexLighting = VertexLighting(i.world, input.normalWS);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo * _BaseColor.rgb;
                surface.metallic = metallic;
                surface.smoothness = smoothness;
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
