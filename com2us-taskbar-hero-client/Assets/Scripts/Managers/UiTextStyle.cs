using UnityEngine;
using UnityEngine.UI;
// UnityEngine.SceneManagement를 using으로 열지 않는다 — 이 네임스페이스에 동명의 SceneManager(씬 전환 매니저)가 있어
// 이름이 가려진다. 씬 로드 이벤트는 정규화 이름(UnityEngine.SceneManagement.SceneManager)으로 참조한다.

namespace TaskbarHero.Client.Managers
{
    /// <summary>
    /// 텍스트 렌더링 전역 정책.
    ///
    /// <para><b>왜 필요한가</b> — 모든 텍스트가 빌트인 동적 폰트(<c>LegacyRuntime.ttf</c> = Arial 계열)를 쓴다.
    /// 이 폰트는 글리프를 그레이스케일 안티에일리어싱으로 굽기 때문에 획 주변에 반투명 회색이 1~2px 퍼지고,
    /// 경계가 딱 떨어지는 픽셀아트(배경·UI 프레임·아이콘) 옆에서 유독 흐릿해 보인다.</para>
    ///
    /// <para><b>무엇을 하는가</b> — 모든 <see cref="Text"/>에 <c>TaskbarHero/UI Text Sharpen</c> 셰이더 머티리얼을
    /// 붙여 글리프 알파를 두 단계로 손본다 — <see cref="SharpenAmount"/>(이웃과 비교하는 언샤프 마스킹으로
    /// 획 바깥 halo를 걷어내고 획을 또렷하게) + <see cref="AlphaBoost"/>(남은 알파를 진하게).
    /// 폰트·글자 크기·레이아웃을 바꾸지 않으므로 UI 배치가 그대로이고, <see cref="InkFloor"/>가
    /// 잉크 있는 픽셀의 알파 하한을 보장하므로 작은 글자의 얇은 획도 사라지지 않는다.</para>
    ///
    /// <para><b>왜 자동 스캔인가</b> — 텍스트가 40여 곳의 컨트롤러에서 런타임에 생성되므로 생성 지점마다
    /// 배선하면 새 코드에서 빠지기 쉽다. 여기서 주기적으로 훑어 적용하며, 호출부가 커스텀 머티리얼을
    /// 직접 지정한 텍스트는 의도를 존중해 건드리지 않는다. 새 코드에서 즉시 적용하고 싶으면
    /// <see cref="Apply(Text)"/>를 호출한다.</para>
    /// </summary>
    public static class UiTextStyle
    {
        /// <summary>선명화 셰이더 이름. 코드에서만 참조하므로 Graphics Settings의 Always Included Shaders에 등록되어 있어야 한다.</summary>
        private const string ShaderName = "TaskbarHero/UI Text Sharpen";

        /// <summary>
        /// 글리프 알파 배율. 1보다 크면 획이 진해져 반투명 테두리가 좁아 보인다. 1이면 배율 없음.
        /// <para><b>알파 하한 컷은 절대 쓰지 않는다</b> — 이것이 이 방식의 안전 조건이다.
        /// 하한 컷(그 값 미만을 투명 처리)을 쓰면 작은 글자(래스터 9~15px)의 한글 얇은 획이 사라져
        /// 글자가 깨진다. 2026-08-03에 하한 0.35로 적용했다가 배율 0.563 캔버스의 패널 텍스트가
        /// 읽을 수 없게 되어 되돌렸다(자세한 경위: docs/ui/텍스트-선명도.md).
        /// 선명도가 부족하면 하한 대신 <see cref="SharpenAmount"/>를 올린다.</para>
        /// </summary>
        public const float AlphaBoost = 1.35f;

        /// <summary>
        /// 언샤프 마스킹 강도(공간 대비). 글리프 알파를 십자 이웃 4탭 평균과 비교해 그 차이를 이 배수로 증폭한다.
        /// <para>0이면 끄고 배율만 적용(예전 동작). 올릴수록 획 바깥의 반투명 halo가 걷혀 경계가 또렷해진다.
        /// 알파 값만 보는 하한 컷과 달리 <b>주변 문맥</b>으로 halo와 얇은 획을 구분하므로,
        /// 획의 심(국소 최댓값)은 오히려 밝아진다 — 얇은 획이 지워지지 않는 이유다.</para>
        /// </summary>
        public const float SharpenAmount = 1.6f;

        /// <summary>이웃 샘플 거리(화면 픽셀 기준). 폰트 아틀라스의 글리프 간 패딩을 넘어 옆 글자를 읽지 않도록 1 이하로 둔다.</summary>
        public const float SharpenRadius = 0.7f;

        /// <summary>
        /// 잉크 보호 하한. 언샤프가 알파를 내릴 때 <b>원본 알파 × 이 비율</b> 아래로는 내리지 않는다.
        /// 획이 있던 픽셀이 투명해지지 않게 하는 안전장치다.
        /// <para>셰이더가 이 하한을 <b>획 쪽 픽셀에만</b> 적용한다(이웃 대비 밝기가 높을수록 강하게 보호).
        /// 이웃보다 한참 어두운 순수 halo에는 하한이 거의 걸리지 않아 깨끗이 걷힌다 —
        /// 하한을 모든 픽셀에 똑같이 걸면 halo까지 보호해 선명해지지 않는다(측정으로 확인).</para>
        /// </summary>
        public const float InkFloor = 0.35f;

        /// <summary>새로 생성된 텍스트를 잡기 위한 스캔 주기(초).</summary>
        private const float ScanInterval = 0.4f;

        private static Material _material;
        private static float _nextScanTime;
        private static bool _shaderMissingLogged;

        /// <summary>선명화 머티리얼(지연 생성). 셰이더가 빌드에 포함되지 않았으면 null을 돌려준다.</summary>
        public static Material Material
        {
            get
            {
                if (_material != null)
                {
                    return _material;
                }

                var shader = Shader.Find(ShaderName);
                if (shader == null)
                {
                    if (!_shaderMissingLogged)
                    {
                        _shaderMissingLogged = true;
                        Debug.LogWarning($"[UiTextStyle] 셰이더를 찾지 못해 선명화를 건너뜁니다: {ShaderName} "
                                         + "(Project Settings > Graphics > Always Included Shaders 등록 필요)");
                    }
                    return null;
                }

                _material = new Material(shader) { name = "UITextSharpen (runtime)" };
                _material.SetFloat("_SharpenAmount", SharpenAmount);
                _material.SetFloat("_SharpenRadius", SharpenRadius);
                _material.SetFloat("_InkFloor", InkFloor);
                _material.SetFloat("_SharpenLow", 0f); // 하한 컷 없음 — 얇은 획을 지우지 않는다
                _material.SetFloat("_SharpenHigh", 1f / Mathf.Max(1f, AlphaBoost));
                return _material;
            }
        }

        /// <summary>플레이 시작 시 스캔 훅을 건다(씬 로드 직후 1회 + 이후 주기 스캔).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            // 도메인 리로드가 꺼져 있으면 정적 상태가 남아 있으므로 중복 등록을 먼저 해제한다.
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
            Canvas.willRenderCanvases -= Tick;
            Canvas.willRenderCanvases += Tick;

            _material = null; // 이전 플레이 세션에서 파괴된 머티리얼 참조 정리(도메인 리로드 비활성 대비)
            _nextScanTime = 0f;
            ApplyAll();
        }

        /// <summary>씬이 로드되면 그 씬의 텍스트에 즉시 적용한다(첫 프레임부터 선명하게).</summary>
        private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene,
                                         UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            ApplyAll();
        }

        /// <summary>캔버스 렌더 직전마다 호출되며, 스캔 주기가 지났을 때만 실제로 훑는다.</summary>
        private static void Tick()
        {
            if (Time.unscaledTime < _nextScanTime)
            {
                return;
            }
            _nextScanTime = Time.unscaledTime + ScanInterval;
            ApplyAll();
        }

        /// <summary>씬의 모든 uGUI 텍스트(비활성 포함)에 전역 텍스트 정책을 적용한다.</summary>
        public static void ApplyAll()
        {
            foreach (var text in Object.FindObjectsByType<Text>(FindObjectsInactive.Include))
            {
                Apply(text);
            }
        }

        /// <summary>
        /// 텍스트 하나에 선명화 머티리얼을 적용하고, 줄 높이 때문에 글자가 사라지지 않는지 확인한다.
        /// 이미 적용된 텍스트와, 호출부가 의도적으로 지정한 커스텀 머티리얼은 그대로 둔다.
        /// </summary>
        public static void Apply(Text text)
        {
            if (text == null)
            {
                return;
            }

            EnsureLineNotCulled(text);

            var material = Material;
            if (material == null)
            {
                return;
            }

            Material current = text.material;
            if (current == material)
            {
                return; // 이미 적용됨
            }
            if (current != null && current != text.defaultMaterial)
            {
                return; // 커스텀 머티리얼(외곽선·마스크 등)은 존중
            }

            text.material = material;
        }

        /// <summary>
        /// <b>한 줄조차 들어가지 않는</b> 텍스트를 구제한다.
        ///
        /// <para>uGUI <see cref="Text"/>는 <see cref="VerticalWrapMode.Truncate"/>일 때 줄 높이가 rect 높이를 넘으면
        /// 그 줄을 <b>통째로 렌더링하지 않는다</b>(부분 잘림이 아니라 아예 사라진다). rect 높이를 글자 크기에 딱 맞춰
        /// 잡아 둔 곳은 폰트·글자 크기가 조금만 바뀌어도 글자가 사라지므로, 한 줄조차 안 들어갈 때만
        /// <see cref="VerticalWrapMode.Overflow"/>로 바꿔 살짝 넘치더라도 보이게 한다.
        /// 여러 줄을 의도적으로 잘라내는 텍스트(rect가 한 줄보다 충분히 큰 경우)는 Truncate를 그대로 유지한다.</para>
        /// </summary>
        private static void EnsureLineNotCulled(Text text)
        {
            if (text.verticalOverflow != VerticalWrapMode.Truncate)
            {
                return;
            }

            Rect rect = text.rectTransform.rect;
            if (rect.height <= 1f)
            {
                return; // 레이아웃 계산 전 — 다음 스캔에서 다시 본다
            }

            // 한 글자(=반드시 한 줄)로 줄 높이를 재서 rect와 비교한다.
            // preferredHeight와 같은 경로를 쓰되, 캔버스 배율로 나눠 로컬 단위로 환산한다.
            float pixelsPerUnit = text.canvas != null && text.canvas.scaleFactor > 0f ? text.canvas.scaleFactor : 1f;
            var settings = text.GetGenerationSettings(new Vector2(rect.width, 0f));
            float oneLineHeight = text.cachedTextGeneratorForLayout.GetPreferredHeight("가", settings) / pixelsPerUnit;

            if (oneLineHeight > rect.height + 0.01f)
            {
                text.verticalOverflow = VerticalWrapMode.Overflow;
            }
        }
    }
}
