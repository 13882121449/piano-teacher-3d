Shader "PianoTeacher3D/UnderwaterSun"
{
    Properties
    {
        _Effects ("散射强度", Range(0,1)) = 1
        _Sun ("光源", Vector) = (1.15,6.6,-1.35,0)
        _SunTarget ("琴台中心", Vector) = (0,-0.12,-0.33,0)
        _StageSequence ("曲目时间与光强", Vector) = (-1,1,1,0)
        _WaterTint ("曲段水色", Vector) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+35" "RenderType"="Transparent" }
        Pass
        {
            Cull Front
            ZWrite Off
            ZTest Always
            Blend One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Effects;
                float4 _Sun, _SunTarget, _StageSequence, _WaterTint;
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
                float3 fromCamera = i.world - _WorldSpaceCameraPos;
                float maxT = length(fromCamera);
                float3 ray = fromCamera / max(0.001, maxT);
                float2 uv = i.clip.xy / _ScaledScreenParams.xy;
                float depth = SampleSceneDepth(uv);
                #if UNITY_REVERSED_Z
                    bool hasScene = depth > 0.00001;
                #else
                    bool hasScene = depth < 0.99999;
                    depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, depth);
                #endif
                if (hasScene)
                {
                    float3 surface = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                    maxT = min(maxT, distance(surface, _WorldSpaceCameraPos));
                }

                // 沿视线积累散射，遇到实物深度时结束。光束边缘由水面折光的宽度控制。
                float3 axis = normalize(_SunTarget.xyz - _Sun.xyz);
                float lightLength = distance(_SunTarget.xyz, _Sun.xyz);
                float3 across = normalize(cross(axis, float3(0,0,1)));
                float3 other = cross(axis, across);
                float illumination = 0;
                int samples = _Effects > 0.8 ? 24 : 12;
                float dt = maxT / samples;
                float time = _StageSequence.x >= 0.0 ? _StageSequence.x : _Time.y;
                [loop] for (int s = 0; s < samples; s++)
                {
                    float3 p = _WorldSpaceCameraPos + ray * ((s + 0.5) * dt) - _Sun.xyz;
                    float distanceAlong = dot(p, axis);
                    float2 offset = float2(dot(p, across), dot(p, other));
                    float scale = 0.36 + max(0, distanceAlong) * 0.128;
                    float2 q = offset / scale;
                    float shafts = exp(-dot(q, q) * 1.8) * 0.55;
                    float2 narrow = q - float2(-0.39, 0.28);
                    shafts += exp(-dot(narrow, narrow) * 12.0) * 0.75;
                    narrow = q - float2(0.43, -0.25);
                    shafts += exp(-dot(narrow, narrow) * 17.0) * 0.35;
                    float folds = 0.47 + 0.53 * pow(saturate(0.5 + 0.5 * sin(q.x * 17 + q.y * 8 + time * 0.40)), 2);
                    float falloff = smoothstep(0, 0.9, distanceAlong) * (1.0 - smoothstep(lightLength - 0.8, lightLength + 0.5, distanceAlong));
                    illumination += shafts * folds * falloff * dt;
                }
                float alpha = min(0.62, illumination * 0.28 * _StageSequence.y) * _Effects;
                return half4(float3(0.10,0.66,0.86) * _WaterTint.rgb * alpha, alpha * 0.37);
            }
            ENDHLSL
        }
    }
}
