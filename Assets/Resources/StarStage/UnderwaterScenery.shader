Shader "PianoTeacher3D/UnderwaterScenery"
{
    Properties
    {
        _Effects ("动效强度", Range(0,1)) = 1
        _Sediment ("海床与礁石", Float) = 0
        _Glow ("局部发光", Float) = 0
        _Shine ("表面反光", Float) = 0.26
        _BaseTint ("表面基色", Color) = (1,1,1,1)
        _UseVertexColor ("使用顶点颜色", Float) = 1
        _StageSurface ("舞台材质", Float) = 0
        _Whale ("鲸体变形与远景", Float) = 0
        _Sun ("光源", Vector) = (1.15,6.6,-1.35,0)
        _SunTarget ("琴台中心", Vector) = (0,-0.12,-0.33,0)
        _StageSequence ("曲目时间、光强与水雾", Vector) = (-1,1,1,0)
        _WaterTint ("曲段水色", Vector) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Effects, _Sediment, _Glow, _Shine, _UseVertexColor, _StageSurface, _Whale;
                float4 _Sun, _SunTarget, _BaseTint, _StageSequence, _WaterTint;
            CBUFFER_END

            // 2026-11-18 琴台镜面倒影（StagePlanarReflection 写入；强度 0 = 关闭）
            TEXTURE2D(_PT3D_PlanarReflection);
            SAMPLER(sampler_PT3D_PlanarReflection);
            float _PT3D_PlanarReflectionStrength;

            struct Attributes { float4 vertex : POSITION; float3 normal : NORMAL; float4 color : COLOR; float2 motion : TEXCOORD0; };
            struct Varyings
            {
                float4 clip : SV_POSITION;
                float3 world : TEXCOORD0;
                half3 normal : TEXCOORD1;
                half3 color : TEXCOORD2;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 p = v.vertex.xyz;
                float t = _StageSequence.x >= 0.0 ? _StageSequence.x : _Time.y;
                float weight = v.motion.x;
                // 躯干变形用位置求连续波形，眼睛、唇线和鳍根跟随同一片皮肤。
                if (_Whale > 0.5)
                {
                    float tail = smoothstep(0.6, 4.4, -p.x);
                    float phase = t * 1.38 + p.x * 0.71;
                    p.y += sin(phase) * lerp(0.058, 0.31, tail) * _Effects;
                    if (v.motion.y == 6)
                    {
                        float flap = sin(t * 1.38 - 1.15) * 0.18 * weight * weight * _Effects;
                        p.y += flap;
                        p.z -= sign(p.z) * flap * 0.34;
                    }
                }
                else if (v.motion.y == 2)
                    p += float3(sin(t * 1.16 + p.y * 0.66 + p.z * 0.31), 0,
                        cos(t * 0.88 + p.y * 0.47 + p.x * 0.36)) * weight * weight * 0.43 * _Effects;
                else if (v.motion.y >= 3 && v.motion.y < 4)
                {
                    float phase = frac(v.motion.y) * 6.2831853;
                    p.z += weight * sin(t * 9.8 + phase + p.x * 1.4) * 0.28 * _Effects;
                    p.y += sin(t * 1.7 + phase) * 0.045 * _Effects;
                }
                else if (v.motion.y == 4)
                    p.xz += weight * weight * float2(sin(t * 2.2 - weight * 3.8 + p.x * 19.0),
                        cos(t * 1.7 - weight * 4.4 + p.z * 16.0)) * 0.12 * _Effects;
                else if (v.motion.y == 5)
                    p += float3(sin(t * 0.63 + weight), sin(t * 0.43 + weight * 1.6),
                        cos(t * 0.54 + weight * 0.8)) * float3(0.47, 0.27, 0.38) * _Effects;
                o.world = TransformObjectToWorld(p);
                o.clip = TransformWorldToHClip(o.world);
                o.normal = TransformObjectToWorldNormal(v.normal);
                o.color = lerp(_BaseTint.rgb, v.color.rgb, _UseVertexColor);
                return o;
            }

            float Hash(float2 p) { return frac(sin(dot(p, float2(41.23, 47.91))) * 43913.461); }

            float Noise(float2 p)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(cell), Hash(cell + float2(1,0)), f.x),
                    lerp(Hash(cell + float2(0,1)), Hash(cell + 1), f.x), f.y);
            }

            // 世界坐标中的两层缓慢波前相交。远处降低频率能量，避免闪烁。
            float Caustics(float2 p, float t)
            {
                p = float2(p.x * 0.93 - p.y * 0.37, p.y * 0.93 + p.x * 0.37);
                float2 warp = float2(sin(p.y * 2.1 + t * 0.39), sin(p.x * 2.9 - t * 0.32));
                p += 0.25 * warp;
                float a = sin(p.x * 7.1 - t * 0.39 + sin(p.y * 4.2 + t * 0.43));
                float b = sin(p.y * 6.0 + t * 0.44 + cos(p.x * 4.6 - t * 0.31));
                float edge = abs(a + b);
                return exp(-edge * 12.0) * (0.55 + 0.45 * Noise(p * 2.1 + t * 0.05));
            }

            half4 Frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                float3 viewDelta = _WorldSpaceCameraPos - i.world;
                float distanceToEye = length(viewDelta);
                float3 view = viewDelta / max(0.001, distanceToEye);
                float3 normal = normalize(i.normal) * IS_FRONT_VFACE(face, 1, -1);
                float3 toSun = normalize(_Sun.xyz - i.world);
                float diffuse = saturate(dot(normal, toSun));
                float reflection = pow(saturate(dot(normal, normalize(toSun + view))), 30.0);
                float elevation = saturate(normal.y * 0.5 + 0.5);
                float grain = Noise(i.world.xz * 16.1 + i.world.y * 5.6);
                float broad = Noise(float2(i.world.x + 0.41 * i.world.y, i.world.z) * 2.8);
                float specks = Noise(i.world.xz * 57.0 + i.world.y * 27.0);
                float bands = Noise(float2(i.world.y * 14.0, (i.world.x + i.world.z) * 2.7));
                float surfaceDetail = lerp(0.74 + 0.28 * grain, 0.43 + 0.44 * broad + 0.21 * grain + 0.13 * bands, _Sediment);
                float3 axis = normalize(_SunTarget.xyz - _Sun.xyz);
                float3 fromSun = i.world - _Sun.xyz;
                float along = dot(fromSun, axis);
                float shaft = exp(-dot(fromSun - axis * along, fromSun - axis * along) * 0.50);
                float shadow = MainLightRealtimeShadow(TransformWorldToShadowCoord(i.world));
                float coolLight = (0.28 + 0.38 * shadow) * diffuse * (0.57 + 0.80 * shaft * _StageSequence.y);
                float3 color = i.color * surfaceDetail * (0.15 + 0.17 * elevation + coolLight * float3(0.62,1.12,1.23));
                color += reflection * _Shine * (0.16 + 0.84 * shaft) * float3(0.08,0.32,0.43);
                float time = _StageSequence.x >= 0.0 ? _StageSequence.x : _Time.y;
                float caustic = Caustics((i.world.xz + i.world.y * axis.xz * 0.9), time);
                color += caustic * shaft * diffuse * shadow * float3(0.044,0.29,0.37) *
                    lerp(0.53,1,_Sediment) * _StageSequence.y;
                color += pow(saturate(specks), 7) * _Sediment * diffuse * float3(0.030,0.051,0.054) *
                    (1.0 - smoothstep(3.0, 11.0, distanceToEye));
                float2 toCenter = i.world.xz - _SunTarget.xz;
                float pool = exp(-dot(toCenter, toCenter) * 1.55);
                float glint = 0.05 + 0.24 * pow(1.0 - saturate(dot(normal, view)), 4);
                float3 environment = GlossyEnvironmentReflection(reflect(-view, normal), i.world, 0.27, 1);
                color += _StageSurface * (float3(0.052,0.114,0.124) * pool * diffuse * shadow +
                    environment * glint * 0.22);
                if (_StageSurface > 0.5 && _PT3D_PlanarReflectionStrength > 0.001)
                {
                    // 屏幕坐标取样（反射相机在投影里左右翻过一次，u 翻回来），水纹轻微扰动
                    float2 suv = GetNormalizedScreenSpaceUV(i.clip);
                    float ripple = Noise(i.world.xz * 3.1 + time * 0.12) - 0.5;
                    suv += normal.xz * 0.015 + float2(ripple, -ripple) * 0.006;
                    suv.x = 1.0 - suv.x;
                    float3 mirrored = SAMPLE_TEXTURE2D(_PT3D_PlanarReflection, sampler_PT3D_PlanarReflection, saturate(suv)).rgb;
                    float fresnel = 0.22 + 0.78 * pow(1.0 - saturate(dot(normal, view)), 3.0);
                    color = lerp(color, color * 0.55 + mirrored * 0.9, saturate(fresnel * _PT3D_PlanarReflectionStrength));
                }
                color += i.color * _Glow;
                float fog = max(1.0 - exp(-distanceToEye * 0.078 * _StageSequence.z), smoothstep(13.0, 19.5, distanceToEye));
                float3 water = lerp(float3(0.003,0.022,0.031), float3(0.012,0.119,0.164),
                    smoothstep(-0.48, 0.57, -view.y)) * _WaterTint.rgb;
                water = lerp(water, float3(0.037,0.077,0.104), _Whale * 0.68 * (1 - smoothstep(13.0, 19.5, distanceToEye)));
                color = lerp(color, water, fog);
                return half4(color, 1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
}
