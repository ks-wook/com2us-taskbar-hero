// uGUI 텍스트 선명화 셰이더.
//
// 빌트인 동적 폰트(LegacyRuntime.ttf)는 글리프를 그레이스케일 안티에일리어싱으로 래스터화하므로,
// 하드 엣지 픽셀아트 옆에 놓이면 반투명 테두리가 넓게 퍼져 흐릿하게 보인다.
// 이 셰이더는 UI/Default와 동일하게 동작하되 글리프 알파를 두 단계로 손본다.
//
//  1) **언샤프 마스킹(공간 대비, _SharpenAmount)** — 십자 이웃 4탭의 평균과 비교해
//     "이웃보다 밝은 픽셀(획 속·얇은 획의 심)"은 더 밝게, "이웃보다 어두운 픽셀(획 바깥 halo)"은
//     더 어둡게 만든다. 알파 값만 보는 하한 컷과 달리 **주변 문맥으로 halo와 얇은 획을 구분**하므로,
//     halo를 깎으면서도 얇은 획은 오히려 진해진다(획의 심은 국소 최댓값이라 항상 밝아지는 쪽).
//  2) **알파 배율(_SharpenHigh)** — 남은 알파를 1/_SharpenHigh배로 올려 획을 조금 더 진하게.
//
// 안전장치 두 개로 "글자가 사라지는" 사고를 구조적으로 막는다.
//  - **_InkFloor** — 잉크가 있던 픽셀의 알파를 원본의 _InkFloor배 아래로는 절대 내리지 않는다.
//    언샤프가 아무리 세도 획이 투명해질 수 없다(0.6이면 최악의 경우도 원본 알파의 60%는 남는다).
//  - **_SharpenLow(하한 컷)는 0으로 둘 것.** 0보다 크면 그 값 미만의 알파가 문맥과 무관하게 전부
//    투명해지는데, 작은 글자(래스터 9~15px)의 한글 얇은 획은 커버리지가 낮아 그대로 지워진다 —
//    2026-08-03에 하한 0.35로 적용했다가 패널 텍스트가 "인빈도리"처럼 깨져 읽을 수 없게 됐다.
//    선명도가 더 필요하면 하한을 올리지 말고 _SharpenAmount를 올린다.
Shader "TaskbarHero/UI Text Sharpen"
{
    Properties
    {
        [PerRendererData] _MainTex ("Font Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        // 공간 대비(언샤프) — 0이면 끔. 올릴수록 획 경계가 또렷해진다.
        _SharpenAmount ("Unsharp Amount", Range(0,3)) = 1.6
        // 이웃 샘플 거리(화면 픽셀). 폰트 아틀라스의 글리프 간 패딩을 넘어 옆 글자를 읽지 않도록 1 이하로 둔다.
        _SharpenRadius ("Unsharp Radius (screen px)", Range(0.25,1)) = 0.7
        // 잉크 보호 하한 — 획 쪽 픽셀은 원본 알파의 이 비율 아래로 내리지 않는다(획 소실 방지).
        _InkFloor ("Ink Floor (keep ratio)", Range(0.1,1)) = 0.35

        // 하한은 0 고정 권장(위 주석 참고). 상한을 낮출수록 획이 진해진다(1/상한 = 알파 배율).
        _SharpenLow ("Alpha Remap Low (0 권장)", Range(0,0.2)) = 0
        _SharpenHigh ("Alpha Remap High", Range(0.3,1)) = 0.74

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
            #pragma target 3.0 // fwidth(화면 미분) 사용 — GLES2에서는 확장이 필요하다

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;
            float _SharpenAmount;
            float _SharpenRadius;
            float _InkFloor;
            float _SharpenLow;
            float _SharpenHigh;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                half4 tex = tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd;
                float ink = tex.a;

                // 1) 언샤프 마스킹 — 십자 이웃 4탭 평균과의 차이를 증폭한다.
                //    획 바깥(이웃이 더 밝음)은 어두워져 halo가 걷히고, 획 속·얇은 획의 심(국소 최댓값)은
                //    밝아진다. 알파 하한 컷과 달리 문맥을 보므로 얇은 획을 지우지 않는다.
                float alpha = ink;
                if (_SharpenAmount > 0.0)
                {
                    // 이웃 샘플 거리는 화면 1픽셀에 해당하는 uv 변화(fwidth)로 잡는다.
                    // _MainTex_TexelSize를 쓰지 않는 이유: uGUI Text는 폰트 텍스처를 머티리얼이 아니라
                    // CanvasRenderer의 텍스처 오버라이드로 넘기므로 텍셀 크기가 채워지지 않을 수 있다.
                    // 화면 기준이라 텍스트가 확대·축소돼도 선명화 폭이 항상 1픽셀 규모로 유지된다.
                    float2 t = fwidth(IN.texcoord) * _SharpenRadius;
                    float n = (tex2D(_MainTex, IN.texcoord + float2( t.x, 0)).a
                             + tex2D(_MainTex, IN.texcoord + float2(-t.x, 0)).a
                             + tex2D(_MainTex, IN.texcoord + float2(0,  t.y)).a
                             + tex2D(_MainTex, IN.texcoord + float2(0, -t.y)).a) * 0.25;
                    alpha = ink + _SharpenAmount * (ink - n);

                    // 잉크 보호 — 단, **획일 때만** 보호한다.
                    // ink >= n 인 픽셀은 이웃보다 밝으므로 획의 심(얇은 획도 여기 들어온다) → 알파가 오르는 쪽이라
                    // 보호가 필요 없다. ink < n 인 픽셀은 획 바깥의 halo이므로 과감히 깎아야 선명해진다.
                    // 그래서 하한은 "획 쪽에 가까운 정도"(ink/n)에 비례해 풀어 준다 —
                    // 이웃과 비슷한 밝기(얇은 획이 걸릴 수 있는 구간)는 _InkFloor로 지켜지고,
                    // 이웃보다 한참 어두운 순수 halo는 하한이 0에 가까워져 깨끗이 걷힌다.
                    float strokeness = n > 0.0001 ? saturate(ink / n) : 1.0;
                    float floorRatio = lerp(0.0, _InkFloor, strokeness * strokeness);
                    alpha = max(alpha, ink * floorRatio);
                }

                // 2) 글리프 알파 재매핑 — 획을 진하게 만들어 반투명 테두리를 좁힌다.
                // 하한은 안전을 위해 0~0.2로 묶는다(그 이상이면 작은 글자의 얇은 획이 잘려 나간다).
                float lo = clamp(_SharpenLow, 0.0, min(0.2, _SharpenHigh - 0.001));
                tex.a = saturate((alpha - lo) / max(_SharpenHigh - lo, 0.001));

                half4 color = IN.color * tex;

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
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
