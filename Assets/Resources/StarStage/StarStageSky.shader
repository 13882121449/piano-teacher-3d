Shader "PianoTeacher3D/StarStageSky"
{
    Properties
    {
        _Effects ("水波强度", Range(0,1)) = 1
        _StageSequence ("曲目时间与光强", Vector) = (-1,1,1,0)
        _WaterTint ("曲段水色", Vector) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Cull Front
        ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Effects;
                float4 _StageSequence, _WaterTint;
            CBUFFER_END
            struct Varyings { float4 clip : SV_POSITION; float3 world : TEXCOORD0; };
            Varyings Vert(float4 vertex : POSITION)
            {
                Varyings o;
                o.world = TransformObjectToWorld(vertex.xyz);
                o.clip = TransformWorldToHClip(o.world);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 direction = normalize(i.world - _WorldSpaceCameraPos);
                float height = direction.y;
                float3 water = lerp(float3(0.003,0.022,0.031), float3(0.012,0.119,0.164),
                    smoothstep(-0.48, 0.57, height));
                // 水面以上的光斑随波前移动，海洋生物由场景网格独立渲染。
                float3 sun = normalize(float3(-0.16,0.71,-0.37));
                float halo = pow(saturate(dot(sun, direction)), 9);
                float2 p = direction.xz / max(height, 0.13) * 5.0;
                float t = (_StageSequence.x >= 0.0 ? _StageSequence.x : _Time.y) * 0.34;
                float wave = sin(p.x * 2.1 + sin(p.y * 1.4 + t) + 0.22 * sin(p.y * 0.53)) +
                    cos(p.y * 3.2 + sin(p.x * 1.7 - t) + 0.43 * cos(p.x * 0.62));
                float facets = exp(-abs(wave) * 4.2) * smoothstep(0.04, 0.49, height);
                water += (halo * float3(0.026,0.156,0.204) + facets * float3(0.022,0.125,0.170) * _Effects) *
                    _StageSequence.y;
                return half4(water * _WaterTint.rgb, 1);
            }
            ENDHLSL
        }
    }
}

