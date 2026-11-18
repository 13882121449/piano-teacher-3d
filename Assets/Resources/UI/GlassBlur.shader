// 毛玻璃界面的背景模糊（双重 Kawase）。由 GlassBlurFeature 通过 Blitter 调用：
// Pass 0 = 降采样（中心 ×4 + 四角），Pass 1 = 升采样（8 点帐篷）。
// _GlassSourceTexel.xy = 1 / 源纹理尺寸，_GlassOffset = 以源纹素为单位的采样偏移。
Shader "Hidden/PianoTeacher3D/GlassBlur"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        SAMPLER(sampler_GlassLinearClamp);
        float4 _GlassSourceTexel;
        float _GlassOffset;

        half3 GlassTap(float2 uv)
        {
            return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_GlassLinearClamp, uv).rgb;
        }

        half4 FragDown(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uv = input.texcoord;
            float2 o = _GlassSourceTexel.xy * _GlassOffset;
            half3 c = GlassTap(uv) * 4.0h;
            c += GlassTap(uv + float2(-o.x, -o.y));
            c += GlassTap(uv + float2( o.x, -o.y));
            c += GlassTap(uv + float2(-o.x,  o.y));
            c += GlassTap(uv + float2( o.x,  o.y));
            return half4(c * 0.125h, 1.0h);
        }

        half4 FragUp(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uv = input.texcoord;
            float2 o = _GlassSourceTexel.xy * _GlassOffset;
            half3 c = GlassTap(uv + float2(-o.x * 2.0, 0.0));
            c += GlassTap(uv + float2(-o.x,  o.y)) * 2.0h;
            c += GlassTap(uv + float2( 0.0,  o.y * 2.0));
            c += GlassTap(uv + float2( o.x,  o.y)) * 2.0h;
            c += GlassTap(uv + float2( o.x * 2.0, 0.0));
            c += GlassTap(uv + float2( o.x, -o.y)) * 2.0h;
            c += GlassTap(uv + float2( 0.0, -o.y * 2.0));
            c += GlassTap(uv + float2(-o.x, -o.y)) * 2.0h;
            return half4(c / 12.0h, 1.0h);
        }
        ENDHLSL

        Pass
        {
            Name "GlassDown"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDown
            ENDHLSL
        }

        Pass
        {
            Name "GlassUp"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragUp
            ENDHLSL
        }
    }
}
