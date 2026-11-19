// iOS「液态玻璃」（Liquid Glass）风格 UI 材质（uGUI Image 用，2026-11-18）。
//
// 背景：GlassBlurFeature 写入的全局模糊纹理 _PT3D_GlassBlurTex（按屏幕坐标采样背后的 3D 画面）。
// 玻璃贴图（UITheme.GetGlassSprite）除形状 alpha 外还编码了边缘信息：
//   R = 离边缘的接近度（0 = 内部，1 = 最外沿），G/B = 边缘外法线（打包到 0..1）。
// 着色：
//   ① 玻璃体：背景轻度提亮、提饱和，再按 _TintStrength 叠一点 Image.color 色调——保持通透；
//   ② 折射：越靠近边缘，采样位置沿法线向内偏移越多，像厚玻璃边缘的透镜放大；
//   ③ 边缘高光：边缘带的镜面高光，朝左上（主光）最亮，右下有一道较弱的回光，外沿再加一条细亮线。
// Image.color 的 RGB 是色调，alpha 是整体不透明度（CanvasGroup 淡入淡出照常）。
// 模糊纹理未就绪时退回 Image.color 纯色。模板 / RectMask2D 裁剪与 UI/Default 一致。
Shader "PianoTeacher3D/UI/FrostedGlass"
{
    Properties
    {
        [PerRendererData] _MainTex ("Glass Sprite", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _TintStrength ("Tint Strength", Range(0, 1)) = 0.62
        _Saturation ("Saturation", Range(0, 2.5)) = 1.25
        _Brightness ("Brightness", Range(0.5, 1.6)) = 0.72
        _Lift ("Brightness Lift", Range(-0.2, 0.3)) = -0.02
        _RimIntensity ("Rim Highlight", Range(0, 2)) = 0.55
        _RimColor ("Rim Color", Color) = (0.94, 0.87, 0.72, 1)
        _Frost ("Frost (white lift)", Range(0, 0.3)) = 0.0
        _Refraction ("Edge Refraction (px)", Range(0, 40)) = 5
        _Grain ("Grain", Range(0, 0.06)) = 0.012
        _TopSheen ("Top Specular Sheen", Range(0, 0.3)) = 0.045

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
                float4 mask : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;
            float _UIMaskSoftnessX;
            float _UIMaskSoftnessY;

            sampler2D _PT3D_GlassBlurTex;
            float _PT3D_GlassBlurReady;

            half _TintStrength;
            half _Saturation;
            half _Brightness;
            half _Lift;
            half _RimIntensity;
            fixed4 _RimColor;
            half _Frost;
            half _Refraction;
            half _Grain;
            half _TopSheen;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                float4 vPosition = UnityObjectToClipPos(v.vertex);
                OUT.worldPosition = v.vertex;
                OUT.vertex = vPosition;

                float2 pixelSize = vPosition.w;
                pixelSize /= float2(1, 1) * abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
                float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);
                OUT.mask = float4(v.vertex.xy * 2 - clampedRect.xy - clampedRect.zw,
                    0.25 / (0.25 * half2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize.xy)));

                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                OUT.screenPos = ComputeScreenPos(vPosition);
                return OUT;
            }

            half3 SampleBehind(float2 uv)
            {
                half3 c = tex2D(_PT3D_GlassBlurTex, uv).rgb;
                half luma = dot(c, half3(0.2126h, 0.7152h, 0.0722h));
                c = lerp(luma.xxx, c, _Saturation);
                return max(c * _Brightness + _Lift, 0.0h);
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                half4 data = tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd;
                half shape = data.a;
                // 普通圆角贴图（RGB 全白）没有边缘编码：G、B 同时接近 1 在玻璃贴图里不会出现，
                // 遇到时按「无边缘信息」处理，退化为平滑玻璃，避免整块被当成高光边缘。
                bool encoded = !(data.g > 0.97h && data.b > 0.97h);
                half edge = encoded ? saturate(data.r) : 0.0h;
                half2 normal = encoded ? data.gb * 2.0h - 1.0h : half2(0.0h, 0.0h);
                half normalLength = length(normal);
                normal = normalLength > 1e-3h ? normal / normalLength : half2(0.0h, 0.0h);

                float2 uv = IN.screenPos.xy / max(IN.screenPos.w, 1e-5);
                // 折射：边缘带内沿法线向内偏移采样，越靠外沿偏得越多
                float2 pixel = 1.0 / max(_ScreenParams.xy, 1.0);
                float2 refracted = uv - normal * (edge * edge) * _Refraction * pixel;
                half3 behind = SampleBehind(refracted);

                half3 glass = lerp(behind, IN.color.rgb, _TintStrength);
                glass = lerp(glass, half3(1.0h, 1.0h, 1.0h), _Frost);

                // 顶部极淡高光掠影：9-slice 面板中心 UV.y=0.5 不受影响，仅在顶倒角/上部渐现受光层次
                half topGlow = smoothstep(0.55h, 0.98h, saturate(IN.texcoord.y)) * _TopSheen;
                glass += _RimColor.rgb * topGlow;

                // 边缘高光：1.3~2.0px 光学切割微光（主光来自左上，香槟金暖调）
                half2 light = half2(-0.55h, 0.83h);
                half facing = saturate(dot(normal, light));
                half lip = smoothstep(0.55h, 0.96h, edge);
                half rim = lip * (0.28h + 0.72h * pow(facing, 1.5h));
                glass += _RimColor.rgb * (rim * _RimIntensity);

                // 细颗粒：避免大面积平滑渐变在 8 位缓冲里出现色带
                half n = frac(sin(dot(IN.vertex.xy, float2(12.9898, 78.233))) * 43758.5453);
                glass += (n - 0.5h) * _Grain;

                // 限制在可显示范围：界面不参与 HDR 泛光（截图把画布转进相机渲染时也不会被 Bloom 晕成一片白）
                glass = saturate(glass);

                bool ready = _PT3D_GlassBlurReady > 0.5;
                half4 color = half4(ready ? glass : IN.color.rgb, shape * IN.color.a);

                #ifdef UNITY_UI_CLIP_RECT
                half2 m = saturate((_ClipRect.zw - _ClipRect.xy - abs(IN.mask.xy)) * IN.mask.zw);
                color.a *= m.x * m.y;
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDCG
        }
    }
}
