using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using TaskbarHero.Client.Managers;
using TaskbarHero.Common.Dto;

namespace TaskbarHero.Client.UI.Gacha
{
    /// <summary>
    /// 뽑기 결과 연출 오버레이(가챠 기획서 §5.2 응답의 <c>results</c>를 재생만 한다 — 추첨은 전부 서버가 확정하고
    /// 클라이언트는 결과를 받아 보여줄 뿐이다).
    /// <para>순서 — ① 결과 중 <b>최고 등급</b>에 해당하는 연출 영상(<c>Assets/Art/UI/Gacha/{3,4,5}성연출.mp4</c>)을
    /// 재생하고(3등급 미만이면 생략) ② 별빛 배경(<c>gacha_result_bg</c>, 위에 9-slice 액자 테두리
    /// <c>window_frame_hollow</c>를 얹는다) 위에 결과 칸을 <b>스케일 0에서 원래 크기로</b> 왼쪽부터 하나씩 띄운다.
    /// 아무 곳이나 누르면 영상을 건너뛰고 결과로 넘어간다(연출은 매 뽑기마다 반복되므로 스킵이 필수다).</para>
    /// <para>제목은 문구가 아니라 <b>명판 이미지</b>(<c>Assets/Art/UI/Gacha/gacha_result_ui.png</c>)이며, 결과 화면이
    /// 열리는 순간 작은 크기에서 <b>원본 크기까지 커지며</b> 등장한다(<see cref="TitlePopIn"/>). 아트가 배선돼 있지
    /// 않을 때만 예전처럼 텍스트 제목("뽑기 결과"·"N연 뽑기 결과")으로 폴백한다.</para>
    /// <para>결과 칸은 공용 아이템 슬롯 프리팹(<c>ItemSlot</c>)을 써서 아이콘·수량·hover 상세를 그대로 재사용하고,
    /// 프레임만 가챠 전용 아트(<c>gacha_result_slot</c>)로 교체한다. 천장(<c>isPity</c>)·10연 보장
    /// (<c>isGuaranteed</c>)으로 확정된 칸은 슬롯 아래에 배지를 달아 왜 그 등급이 나왔는지 알려 준다.</para>
    /// 자기 캔버스(<see cref="UiSortingOrder.RewardOverPanel"/>)를 가지므로 가챠 패널 위에 그려진다.
    /// </summary>
    public class GachaResultOverlay : MonoBehaviour
    {
        // 결과 칸 격자: 10연이 5열 × 2행으로 떨어지도록 잡는다(1연은 첫 칸 하나만 쓴다).
        private const int Columns = 5;
        private const float SlotSize = 128f;
        private const float SlotGapX = 26f;
        private const float SlotGapY = 34f;
        private const float BadgeHeight = 30f;

        // 별빛 배경(gacha_result_bg 718×558)을 원본 비율 그대로 확대해 쓴다.
        private const float BackgroundWidth = 940f;
        private const float BackgroundHeight = 730f;

        // 창 테두리(액자). 배경 아트가 테두리 없는 그림이라 경계가 그냥 잘려 보이므로 9-slice 프레임을 얹는다.
        private const float FrameThickness = 40f;  // 낮출수록 테두리가 얇아진다(pixelsPerUnitMultiplier로 환산)
        private const float FrameOutset = 10f;     // 배경 rect보다 이만큼 밖으로 키워 배경을 개구부에 꽉 채운다

        // 결과 칸이 하나씩 등장하는 연출.
        private const float RevealStagger = 0.07f;   // 칸 사이 시차
        private const float RevealPopSeconds = 0.16f; // 한 칸이 커지는 시간
        private const float RevealStartScale = 0f;   // 아예 안 보이는 상태에서 원래 크기까지 커진다
        private const float VideoStartTimeout = 1.5f; // 재생이 시작되지 않으면 연출을 건너뛴다

        // 제목 명판(gacha_result_ui 595×266) — 창 위쪽 테두리에 걸치도록 얹어 결과 칸을 가리지 않는다.
        // 크기는 원본 비율을 유지한 값이며, 이것이 등장 연출이 끝나는 "원본 크기"다.
        private static readonly Vector2 TitleImageSize = new Vector2(480f, 215f);
        private const float TitlePopSeconds = 0.32f;    // 작은 크기 → 원본 크기까지 걸리는 시간
        private const float TitlePopStartScale = 0.25f; // 등장 시작 크기(원본 대비)
        // 원본 크기를 살짝 넘겼다가 제자리로 돌아오는 back-out 이징 계수(0이면 오버슈트 없음).
        private const float TitlePopOvershoot = 1.4f;

        // 4·5등급 칸 뒤에서 도는 글로우(원본 800×800 방사형). 칸보다 크게 깔아 빛이 밖으로 번지게 한다.
        private const float GlowSizeScale = 1.9f;
        private const float GlowFps = 20f;

        [Header("리소스 (에디터 빌더가 배선)")]
        [Tooltip("결과 배경(Assets/Art/UI/Gacha/gacha_result_bg.png).")]
        [SerializeField] private Sprite _resultBackground;
        [Tooltip("결과 칸 프레임(Assets/Art/UI/Gacha/gacha_result_slot.png).")]
        [SerializeField] private Sprite _slotFrame;
        [Tooltip("제목 명판 이미지(Assets/Art/UI/Gacha/gacha_result_ui.png). 없으면 제목 텍스트로 폴백한다.")]
        [SerializeField] private Sprite _titleImageSprite;
        [Tooltip("결과 창 테두리(Assets/Art/UI/Trade/01_Frames_Panels/window_frame_hollow.png, 9-slice). " +
                 "가운데가 비어 있어 별빛 배경 위에 액자처럼 얹힌다. 없으면 테두리 없이 배경만 보인다(기존 동작).")]
        [SerializeField] private Sprite _windowFrame;
        [Tooltip("확인 버튼 배경(Assets/Art/UI/pixel_rpg_button.png, 9-slice).")]
        [SerializeField] private Sprite _buttonSprite;
        [Tooltip("공용 아이템 슬롯 프리팹(Assets/Prefabs/UI/ItemSlot).")]
        [SerializeField] private GameObject _itemSlotPrefab;

        [Header("등급 슬롯 글로우 (Assets/Art/Effect/UI — 4·5등급 칸 뒤에서 순환 재생)")]
        [Tooltip("4성(영웅) 글로우 프레임(FourStarSlotGlow_01~).")]
        [SerializeField] private Sprite[] _glowGrade4;
        [Tooltip("5성(전설) 글로우 프레임(FiveStarSlotGlow_01~).")]
        [SerializeField] private Sprite[] _glowGrade5;

        [Header("등급 연출 영상 (Assets/Art/UI/Gacha)")]
        [Tooltip("3성(희귀) 연출 — 결과 최고 등급이 3일 때 재생.")]
        [SerializeField] private VideoClip _clipGrade3;
        [Tooltip("4성(영웅) 연출 — 결과 최고 등급이 4일 때 재생.")]
        [SerializeField] private VideoClip _clipGrade4;
        [Tooltip("5성(전설) 연출 — 결과 최고 등급이 5일 때 재생.")]
        [SerializeField] private VideoClip _clipGrade5;

        [Header("구성 참조 (에디터 빌더가 배선)")]
        [SerializeField] private GameObject _videoRoot;
        [SerializeField] private RawImage _videoImage;
        [SerializeField] private GameObject _resultRoot;
        [SerializeField] private RectTransform _slotArea;
        [SerializeField] private Image _titleImage;
        [SerializeField] private Text _titleText;
        [SerializeField] private Button _confirmButton;
        [SerializeField] private Button _skipButton;

        private Font _font;
        private VideoPlayer _player;
        private RenderTexture _videoTexture;
        private Coroutine _routine;
        private bool _skipRequested;
        private readonly List<GameObject> _slots = new List<GameObject>();

        /// <summary>패널 프리팹에 구워 두는 정적 계층을 만든다(에디터 빌드 전용 진입점).</summary>
        public void EditorConstruct() => Construct();

        /// <summary>정적 계층이 없으면(프리팹 미배선) 실행 시점에 만든다.</summary>
        private void Awake()
        {
            if (_resultRoot == null)
            {
                Construct();
            }
            WireRuntime();
            gameObject.SetActive(false); // 뽑기 결과가 있을 때만 노출
        }

        /// <summary>재생용으로 만든 RenderTexture를 정리한다(에셋이 아니라 런타임 생성물이다).</summary>
        private void OnDestroy()
        {
            ReleaseVideoTexture();
        }

        // ── 정적 계층 구성 ──

        /// <summary>딤 · 영상 화면 · 결과 배경 · 결과 칸 영역 · 확인 버튼을 만든다.</summary>
        private void Construct()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var self = (RectTransform)transform;
            Stretch(self);

            // 패널 캔버스 안의 자식 캔버스로 두고 정렬만 덮어써, 가챠 패널 위·공용 모달 아래에 그린다.
            var canvas = gameObject.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = gameObject.AddComponent<Canvas>();
            }
            canvas.overrideSorting = true;
            canvas.sortingOrder = UiSortingOrder.RewardOverPanel;
            if (gameObject.GetComponent<GraphicRaycaster>() == null)
            {
                gameObject.AddComponent<GraphicRaycaster>();
            }

            // 딤 = 연출 중 아무 곳이나 눌러 건너뛰는 판. 결과가 보이는 동안에도 남아 뒤 패널 클릭을 막는다.
            var dim = NewImage("Dim", self, new Color(0f, 0f, 0f, 0.72f));
            Stretch(dim.rectTransform);
            _skipButton = dim.gameObject.AddComponent<Button>();
            _skipButton.transition = Selectable.Transition.None;

            BuildVideoScreen(self);
            BuildResultScreen(self);
        }

        /// <summary>등급 연출 영상을 그리는 전체 화면(RawImage + VideoPlayer). 기본 숨김.</summary>
        private void BuildVideoScreen(RectTransform parent)
        {
            var root = NewChild("VideoRoot", parent);
            Stretch(root);
            _videoRoot = root.gameObject;

            var raw = new GameObject("VideoImage", typeof(RectTransform), typeof(RawImage));
            raw.transform.SetParent(root, false);
            _videoImage = raw.GetComponent<RawImage>();
            _videoImage.raycastTarget = false; // 클릭은 딤(스킵)이 받는다
            _videoImage.color = Color.white;
            Stretch(_videoImage.rectTransform);

            var hint = NewText("SkipHint", root, "화면을 누르면 연출을 건너뜁니다", 24, TextAnchor.LowerCenter);
            hint.color = new Color(1f, 1f, 1f, 0.65f);
            var hrt = hint.rectTransform;
            hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 0f);
            hrt.pivot = new Vector2(0.5f, 0f);
            hrt.anchoredPosition = new Vector2(0f, 40f);
            hrt.sizeDelta = new Vector2(700f, 40f);

            _videoRoot.SetActive(false);
        }

        /// <summary>별빛 배경 · 제목 · 결과 칸 영역 · 확인 버튼. 기본 숨김.</summary>
        private void BuildResultScreen(RectTransform parent)
        {
            var root = NewChild("ResultRoot", parent);
            Stretch(root);
            _resultRoot = root.gameObject;

            var bg = NewImage("ResultBackground", root, new Color(0.14f, 0.16f, 0.34f, 0.96f));
            ApplySimple(bg, _resultBackground);
            var brt = bg.rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
            brt.pivot = new Vector2(0.5f, 0.5f);
            brt.anchoredPosition = new Vector2(0f, 40f);
            brt.sizeDelta = new Vector2(BackgroundWidth, BackgroundHeight);
            bg.raycastTarget = false; // 배경을 눌러도 딤(스킵)이 받는다

            // 테두리(액자) — 배경 다음 형제로 만들어 배경 위, 확인 버튼 아래에 그려진다.
            BuildWindowFrame(root, brt);

            // 제목 명판(이미지). 액자 <b>다음</b> 형제로 만들어야 위쪽 테두리 위에 그려진다
            // (배경의 자식으로 두면 액자가 명판을 가로질러 잘린 것처럼 보인다).
            BuildTitlePlate(root, brt);

            _titleText = NewText("Title", brt, "뽑기 결과", 46, TextAnchor.MiddleCenter);
            _titleText.fontStyle = FontStyle.Bold;
            _titleText.color = new Color(1f, 0.94f, 0.72f);
            var trt = _titleText.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.anchoredPosition = new Vector2(0f, -46f);
            trt.sizeDelta = new Vector2(BackgroundWidth - 80f, 60f);

            // 결과 칸은 요청마다 개수가 달라(1연 1칸·10연 10칸) 런타임에 만든다. 이 영역은 그 부모다.
            _slotArea = NewChild("SlotArea", brt);
            _slotArea.anchorMin = _slotArea.anchorMax = new Vector2(0.5f, 0.5f);
            _slotArea.pivot = new Vector2(0.5f, 0.5f);
            _slotArea.anchoredPosition = new Vector2(0f, -10f);
            _slotArea.sizeDelta = new Vector2(BackgroundWidth - 120f, BackgroundHeight - 200f);

            var confirm = NewImage("ConfirmButton", root, new Color(0.26f, 0.30f, 0.48f, 1f));
            ApplySliced(confirm, _buttonSprite);
            var crt = confirm.rectTransform;
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            crt.anchoredPosition = new Vector2(0f, -(BackgroundHeight * 0.5f) + 40f - 30f);
            crt.sizeDelta = new Vector2(320f, 96f);
            var ct = NewText("Label", crt, "확인", 34, TextAnchor.MiddleCenter);
            ct.fontStyle = FontStyle.Bold;
            Stretch(ct.rectTransform);
            _confirmButton = confirm.gameObject.AddComponent<Button>();

            _resultRoot.SetActive(false);
        }

        /// <summary>
        /// 결과 창 테두리(액자)를 배경 위에 얹는다. 별빛 배경 아트에 테두리가 없어 창 경계가 화면에서 그냥
        /// 잘려 보이므로, <b>가운데가 빈 9-slice 프레임</b>으로 경계를 만든다(<see cref="StagePanelController"/>의
        /// 지역 창과 같은 아트·같은 방식이라 화면 톤이 일관된다).
        /// <para>가운데는 그리지 않고(<c>fillCenter = false</c>) 클릭도 받지 않는 순수 장식이며, 배경 rect보다
        /// <see cref="FrameOutset"/>만큼 크게 잡아 배경이 액자 개구부에 꽉 차 보이게 한다. 두께가
        /// <see cref="FrameThickness"/>가 되도록 <c>pixelsPerUnitMultiplier</c>를 계산한다(낮출수록 두꺼워진다).</para>
        /// 아트가 배선되지 않았으면 아무것도 만들지 않는다(테두리 없이 배경만 = 기존 동작).
        /// </summary>
        private void BuildWindowFrame(RectTransform parent, RectTransform backgroundRect)
        {
            if (_windowFrame == null)
            {
                return;
            }

            var frame = NewImage("WindowFrame", parent, Color.white);
            frame.sprite = _windowFrame;
            frame.type = Image.Type.Sliced;
            frame.fillCenter = false;   // 배경·제목·결과 칸을 덮지 않는다
            float srcBorder = Mathf.Max(_windowFrame.border.x, 1f); // 원본 9-slice 경계(20px)
            frame.pixelsPerUnitMultiplier = srcBorder / FrameThickness;
            frame.raycastTarget = false; // 클릭은 딤(스킵)·확인 버튼이 받는다

            var rt = frame.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = backgroundRect.anchoredPosition;
            rt.sizeDelta = backgroundRect.sizeDelta + new Vector2(FrameOutset * 2f, FrameOutset * 2f);
        }

        /// <summary>
        /// 제목 명판(<c>gacha_result_ui</c>)을 결과 창 <b>위쪽 테두리에 걸치도록</b> 얹는다 — 제목 텍스트가 있던
        /// 창 안쪽에 이 크기로 넣으면 결과 칸 영역을 침범하므로, 절반이 창 밖으로 올라오게 놓아 칸을 가리지 않는다.
        /// 피벗은 가운데라 등장 연출(<see cref="TitlePopIn"/>)이 명판 중심을 기준으로 커진다.
        /// <para>아트가 배선되지 않았으면 만들지 않는다 — 그 경우 <see cref="Show"/>가 제목 텍스트를 노출한다.</para>
        /// </summary>
        private void BuildTitlePlate(RectTransform parent, RectTransform backgroundRect)
        {
            if (_titleImageSprite == null)
            {
                return;
            }

            var plate = NewImage("TitlePlate", parent, Color.white);
            ApplySimple(plate, _titleImageSprite);
            plate.preserveAspect = true;
            plate.raycastTarget = false; // 클릭은 딤(스킵)·확인 버튼이 받는다
            _titleImage = plate;

            var rt = plate.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = TitleImageSize;
            // 창 위쪽 테두리 선에 명판 중심을 맞춘다(절반은 창 밖, 절반은 창 안).
            rt.anchoredPosition = backgroundRect.anchoredPosition + new Vector2(0f, BackgroundHeight * 0.5f);
        }

        /// <summary>
        /// 제목 명판을 <see cref="TitlePopStartScale"/>(작은 크기) → 원본 크기(1)로 키운다.
        /// 원본 크기를 살짝 넘겼다가 제자리로 돌아오는 back-out 이징이라 "팡 나타났다"로 읽히며,
        /// 결과 칸 등장과 같은 unscaled 시간을 쓴다. 연출을 건너뛰면(<see cref="_skipRequested"/>) 즉시 원본 크기로 맞춘다.
        /// </summary>
        private IEnumerator TitlePopIn()
        {
            var rt = _titleImage.rectTransform;
            rt.localScale = new Vector3(TitlePopStartScale, TitlePopStartScale, 1f);
            for (float t = 0f; t < TitlePopSeconds; t += Time.unscaledDeltaTime)
            {
                if (_skipRequested)
                {
                    break;
                }
                float k = Mathf.Clamp01(t / TitlePopSeconds);
                // easeOutBack: k=1에서 정확히 1로 끝나고, 그 직전에 1을 조금 넘어선다.
                float u = k - 1f;
                float ease = 1f + (TitlePopOvershoot + 1f) * u * u * u + TitlePopOvershoot * u * u;
                float scale = Mathf.LerpUnclamped(TitlePopStartScale, 1f, ease);
                rt.localScale = new Vector3(scale, scale, 1f);
                yield return null;
            }
            rt.localScale = Vector3.one; // 원본 크기로 마무리
        }

        /// <summary>버튼 리스너를 실행 시점에 다시 연결한다(프리팹에 직렬화되지 않는 비영구 리스너).</summary>
        private void WireRuntime()
        {
            if (_confirmButton != null)
            {
                _confirmButton.onClick.RemoveAllListeners();
                _confirmButton.onClick.AddListener(Hide);
            }
            if (_skipButton != null)
            {
                // 스킵 판(딤)은 상황에 따라 다른 소리를 직접 내므로 전역 클릭음에서 제외한다.
                UiClickSound.Suppress(_skipButton);
                _skipButton.onClick.RemoveAllListeners();
                _skipButton.onClick.AddListener(OnSkipClick);
            }
        }

        // ── 표시 ──

        /// <summary>
        /// 뽑기 결과를 연출과 함께 표시한다. 최고 등급에 맞는 영상을 먼저 재생하고(3등급 미만이면 생략)
        /// 결과 칸을 순차로 띄운다. 이미 표시 중이면 진행 중인 연출을 끊고 새 결과로 갈아탄다.
        /// </summary>
        public void Show(GachaPullResultData data)
        {
            if (data == null || data.results == null || data.results.Count == 0)
            {
                return;
            }

            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }

            gameObject.SetActive(true);
            _skipRequested = false;
            BuildSlots(data.results);
            bool multi = data.pullType == (int)TaskbarHero.Common.GachaPullType.Multi;
            // 제목은 명판 이미지가 정본이고(등장 연출은 결과 화면이 열릴 때 재생한다), 아트가 배선되지 않았을
            // 때만 텍스트로 폴백한다. 연차는 결과 칸 수로 드러나므로 명판에는 회차 수를 쓰지 않는다.
            bool useImage = _titleImage != null;
            if (_titleImage != null)
            {
                _titleImage.gameObject.SetActive(true);
                _titleImage.rectTransform.localScale = new Vector3(TitlePopStartScale, TitlePopStartScale, 1f);
            }
            if (_titleText != null)
            {
                _titleText.gameObject.SetActive(!useImage);
                if (!useImage)
                {
                    _titleText.text = multi ? $"{data.results.Count}연 뽑기 결과" : "뽑기 결과";
                }
            }
            // 뽑기 시전음(사운드 정의서 §7). 비용 차감음은 시전음과 겹치므로 재생하지 않는다.
            SoundManager.Sfx(multi ? SoundId.GachaPullMulti : SoundId.GachaPullSingle);
            _routine = StartCoroutine(PlayRoutine(BestGrade(data.results)));
        }

        /// <summary>오버레이를 닫는다(연출 중이면 중단하고 영상도 멈춘다).</summary>
        public void Hide()
        {
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }
            StopVideo();
            if (_videoRoot != null) _videoRoot.SetActive(false);
            if (_resultRoot != null) _resultRoot.SetActive(false);
            gameObject.SetActive(false);
        }

        /// <summary>딤 클릭: 영상 재생 중이면 건너뛰고, 결과가 다 드러났으면 닫는다.</summary>
        private void OnSkipClick()
        {
            if (_routine != null)
            {
                _skipRequested = true; // 진행 중인 연출(영상·순차 등장)을 즉시 끝낸다
                // 재생 중인 등급 연출음(최대 4초)도 함께 끊는다 — 남으면 다음 슬롯 공개음과 겹친다(§7).
                SoundManager.StopAllSfx();
                SoundManager.Sfx(SoundId.UiClick);
                return;
            }
            Hide();
        }

        /// <summary>
        /// 연출 순서를 재생한다 — 등급 영상(있으면) → 결과 칸 순차 등장.
        /// <b>등급 연출음은 묶음 안 최고 등급 하나만</b> 영상 시작에 맞춰 울린다(사운드 정의서 §7 —
        /// 10연에서 같은 등급이 여러 번 나와도 겹쳐 울리지 않는다).
        /// </summary>
        private IEnumerator PlayRoutine(int bestGrade)
        {
            var clip = ClipFor(bestGrade);
            if (clip != null)
            {
                SoundManager.Sfx(GradeSoundFor(bestGrade));
                yield return PlayVideo(clip);
            }

            _skipRequested = false; // 영상 스킵이 결과 등장까지 삼키지 않도록 초기화
            if (_resultRoot != null) _resultRoot.SetActive(true);
            SoundManager.Sfx(SoundId.UiPanelOpen); // 결과 요약판 표시(§8 공용음 매핑)
            if (_titleImage != null)
            {
                // 제목 명판이 커지는 동안 결과 칸도 함께 드러나기 시작한다(대기 없이 병행 재생).
                StartCoroutine(TitlePopIn());
            }
            yield return RevealSlots();
            _routine = null;
        }

        /// <summary>등급별 결과 연출음(3·4·5등급만 있고 그 미만은 슬롯 공개음으로 끝난다).</summary>
        private static SoundId GradeSoundFor(int grade)
        {
            switch (grade)
            {
                case 5:
                    return SoundId.GachaGrade5;
                case 4:
                    return SoundId.GachaGrade4;
                case 3:
                    return SoundId.GachaGrade3;
                default:
                    return SoundId.None;
            }
        }

        /// <summary>등급 연출 영상을 준비·재생하고 끝날 때(또는 스킵 시)까지 기다린다.</summary>
        private IEnumerator PlayVideo(VideoClip clip)
        {
            EnsurePlayer();
            if (_player == null)
            {
                yield break;
            }

            _player.clip = clip;
            EnsureVideoTexture((int)clip.width, (int)clip.height);
            _player.targetTexture = _videoTexture;
            if (_videoImage != null)
            {
                _videoImage.texture = _videoTexture;
            }

            _player.Prepare();
            float waited = 0f;
            while (!_player.isPrepared && waited < VideoStartTimeout && !_skipRequested)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            if (_skipRequested || !_player.isPrepared)
            {
                StopVideo();
                yield break; // 준비가 안 되면(코덱 미지원 등) 연출을 건너뛰고 결과로 넘어간다
            }

            if (_videoRoot != null) _videoRoot.SetActive(true);
            _player.Play();

            waited = 0f;
            while (!_player.isPlaying && waited < VideoStartTimeout && !_skipRequested)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            while (_player.isPlaying && !_skipRequested)
            {
                yield return null;
            }

            StopVideo();
            if (_videoRoot != null) _videoRoot.SetActive(false);
        }

        /// <summary>결과 칸을 왼쪽부터 하나씩 키워 띄운다(스킵하면 즉시 전부 표시).</summary>
        private IEnumerator RevealSlots()
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i] != null)
                {
                    _slots[i].transform.localScale = new Vector3(RevealStartScale, RevealStartScale, 1f);
                }
            }

            for (int i = 0; i < _slots.Count; i++)
            {
                if (_skipRequested)
                {
                    break;
                }
                var slot = _slots[i];
                if (slot == null)
                {
                    continue;
                }
                SoundManager.Sfx(SoundId.GachaSlotReveal); // 칸 1개 공개(등급 무관 공통음)
                for (float t = 0f; t < RevealPopSeconds; t += Time.unscaledDeltaTime)
                {
                    if (_skipRequested)
                    {
                        break;
                    }
                    float k = Mathf.Clamp01(t / RevealPopSeconds);
                    float scale = Mathf.Lerp(RevealStartScale, 1f, 1f - (1f - k) * (1f - k));
                    slot.transform.localScale = new Vector3(scale, scale, 1f);
                    yield return null;
                }
                slot.transform.localScale = Vector3.one;
                if (!_skipRequested && RevealStagger > 0f)
                {
                    yield return new WaitForSecondsRealtime(RevealStagger);
                }
            }

            foreach (var slot in _slots)
            {
                if (slot != null)
                {
                    slot.transform.localScale = Vector3.one; // 스킵으로 끊겼어도 전부 원래 크기로
                }
            }
            _skipRequested = false;
        }

        // ── 결과 칸 ──

        /// <summary>회차별 결과로 칸을 다시 만든다(5열 격자, 왼쪽 위부터 seq 순서).</summary>
        private void BuildSlots(List<GachaResultItemDto> results)
        {
            foreach (var slot in _slots)
            {
                if (slot != null) Destroy(slot);
            }
            _slots.Clear();
            if (_slotArea == null)
            {
                return;
            }

            int count = results.Count;
            int columns = Mathf.Min(Columns, count);
            int rows = Mathf.CeilToInt(count / (float)Columns);
            float cellW = SlotSize + SlotGapX;
            float cellH = SlotSize + BadgeHeight + SlotGapY;
            float originX = -(columns - 1) * cellW * 0.5f;
            float originY = (rows - 1) * cellH * 0.5f;

            for (int i = 0; i < count; i++)
            {
                var result = results[i];
                if (result == null)
                {
                    continue;
                }
                int row = i / Columns;
                int col = i % Columns;
                // 마지막 줄이 덜 찼으면 그 줄만 가운데로 모은다(예: 7개면 아래 줄 2개가 가운데).
                int inRow = Mathf.Min(Columns, count - row * Columns);
                float rowOriginX = -(inRow - 1) * cellW * 0.5f;
                var pos = new Vector2(rowOriginX + col * cellW, originY - row * cellH);
                _slots.Add(BuildSlot(result, pos));
            }
        }

        /// <summary>결과 칸 하나(아이템 슬롯 + 천장·보장 배지).</summary>
        private GameObject BuildSlot(GachaResultItemDto result, Vector2 pos)
        {
            var cell = NewChild("ResultSlot", _slotArea);
            cell.anchorMin = cell.anchorMax = new Vector2(0.5f, 0.5f);
            cell.pivot = new Vector2(0.5f, 0.5f);
            cell.anchoredPosition = pos;
            cell.sizeDelta = new Vector2(SlotSize, SlotSize + BadgeHeight);
            // 만드는 즉시 감춘다 — 등장 연출(RevealSlots)이 스케일 0에서 키우므로, 한 프레임이라도
            // 원래 크기로 보였다 사라지면 깜빡임이 된다. 연출이 끝나면 전부 원래 크기로 복원된다.
            cell.localScale = Vector3.zero;

            // 글로우를 먼저 만들어(첫 자식 = 가장 뒤) 아이템 칸 뒤에서 빛이 돌게 한다.
            BuildGlow(cell, result.grade);

            if (_itemSlotPrefab != null)
            {
                var go = Instantiate(_itemSlotPrefab, cell);
                var srt = (RectTransform)go.transform;
                srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 1f);
                srt.pivot = new Vector2(0.5f, 1f);
                srt.anchoredPosition = Vector2.zero;
                srt.sizeDelta = new Vector2(SlotSize, SlotSize);
                var view = go.GetComponent<ItemSlotView>();
                if (view != null)
                {
                    view.SetFrameSprite(_slotFrame);
                    view.Setup(result.itemCode, result.quantity);
                }
            }
            else
            {
                // 폴백: 공용 슬롯 프리팹이 배선되지 않았으면 프레임 + 등급 색만 표시한다.
                var frame = NewImage("Frame", cell, GradeColors.RewardSlotBackground(result.grade));
                ApplySimple(frame, _slotFrame);
                var frt = frame.rectTransform;
                frt.anchorMin = frt.anchorMax = new Vector2(0.5f, 1f);
                frt.pivot = new Vector2(0.5f, 1f);
                frt.anchoredPosition = Vector2.zero;
                frt.sizeDelta = new Vector2(SlotSize, SlotSize);
            }

            string badge = result.isPity ? "천장" : result.isGuaranteed ? "보장" : null;
            var label = NewText("Badge", cell, badge ?? ItemDisplayName(result.itemCode), badge != null ? 22 : 20,
                TextAnchor.UpperCenter);
            label.color = badge != null ? new Color(1f, 0.86f, 0.42f) : GradeColors.Name(result.grade);
            label.fontStyle = badge != null ? FontStyle.Bold : FontStyle.Normal;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            var lrt = label.rectTransform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0f);
            lrt.pivot = new Vector2(0.5f, 0f);
            lrt.anchoredPosition = new Vector2(0f, 0f);
            lrt.sizeDelta = new Vector2(SlotSize + SlotGapX - 4f, BadgeHeight);

            return cell.gameObject;
        }

        /// <summary>
        /// 4·5등급 칸 뒤에 방사형 글로우를 깐다(그 미만 등급은 붙이지 않는다 — 등급이 곧 연출의 크기다).
        /// 프레임이 배선되지 않았으면 아무것도 만들지 않는다.
        /// </summary>
        private void BuildGlow(RectTransform cell, int grade)
        {
            var frames = grade >= 5 ? _glowGrade5 : grade == 4 ? _glowGrade4 : null;
            if (frames == null || frames.Length == 0)
            {
                return;
            }

            var img = NewImage("Glow", cell, Color.white);
            img.sprite = frames[0];
            img.type = Image.Type.Simple;
            img.raycastTarget = false; // 클릭은 딤(스킵)·아이템 칸이 받는다
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, -SlotSize * 0.5f); // 아이템 칸 중심과 겹치게
            rt.sizeDelta = new Vector2(SlotSize * GlowSizeScale, SlotSize * GlowSizeScale);

            img.gameObject.AddComponent<SpriteSequenceAnimator>().Configure(frames, GlowFps, true);
        }

        // ── 영상 재생 자원 ──

        /// <summary>VideoPlayer를 준비한다(런타임에 1회 부착. 오디오는 트랙이 있으면 그대로 재생한다).</summary>
        private void EnsurePlayer()
        {
            if (_player != null)
            {
                return;
            }
            _player = gameObject.GetComponent<VideoPlayer>();
            if (_player == null)
            {
                _player = gameObject.AddComponent<VideoPlayer>();
            }
            _player.playOnAwake = false;
            _player.isLooping = false;
            _player.waitForFirstFrame = true;
            _player.renderMode = VideoRenderMode.RenderTexture;
            _player.audioOutputMode = VideoAudioOutputMode.Direct;
            _player.skipOnDrop = true;
        }

        /// <summary>영상 크기에 맞는 RenderTexture를 확보한다(크기가 다르면 새로 만든다).</summary>
        private void EnsureVideoTexture(int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                return;
            }
            if (_videoTexture != null && _videoTexture.width == width && _videoTexture.height == height)
            {
                return;
            }
            ReleaseVideoTexture();
            _videoTexture = new RenderTexture(width, height, 0) { name = "GachaVideoRT" };
        }

        /// <summary>RenderTexture를 해제한다(에셋이 아니므로 직접 파괴해야 한다).</summary>
        private void ReleaseVideoTexture()
        {
            if (_videoTexture == null)
            {
                return;
            }
            if (_player != null)
            {
                _player.targetTexture = null;
            }
            if (_videoImage != null)
            {
                _videoImage.texture = null;
            }
            _videoTexture.Release();
            Destroy(_videoTexture);
            _videoTexture = null;
        }

        /// <summary>재생을 멈춘다(오버레이를 닫거나 연출을 건너뛸 때).</summary>
        private void StopVideo()
        {
            if (_player != null && _player.isPlaying)
            {
                _player.Stop();
            }
        }

        /// <summary>결과 최고 등급에 해당하는 연출 영상(3·4·5등급). 그 미만이면 null(연출 생략).</summary>
        private VideoClip ClipFor(int grade)
        {
            switch (grade)
            {
                case 5:
                    return _clipGrade5;
                case 4:
                    return _clipGrade4;
                case 3:
                    return _clipGrade3;
                default:
                    return null;
            }
        }

        /// <summary>결과 회차 중 가장 높은 등급(연출 선택 기준).</summary>
        private static int BestGrade(List<GachaResultItemDto> results)
        {
            int best = 0;
            foreach (var r in results)
            {
                if (r != null && r.grade > best)
                {
                    best = r.grade;
                }
            }
            return best;
        }

        /// <summary>아이템 표시 이름(번들 마스터에 없으면 코드로 대체).</summary>
        private static string ItemDisplayName(int itemCode)
        {
            MasterDataManager.EnsureLoaded();
            var db = MasterDataManager.Db;
            if (db != null && db.Items.TryGetValue(itemCode, out var def) && def != null)
            {
                return def.name;
            }
            return $"아이템 {itemCode}";
        }

        // ── UI 헬퍼 ──

        private static RectTransform NewChild(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static Image NewImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            return img;
        }

        private Text NewText(string name, Transform parent, string content, int size, TextAnchor anchor)
        {
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = _font;
            t.text = content;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = Color.white;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        private static void ApplySimple(Image img, Sprite sprite)
        {
            if (img == null || sprite == null) return;
            img.sprite = sprite;
            img.type = Image.Type.Simple;
            img.color = Color.white;
        }

        private static void ApplySliced(Image img, Sprite sprite)
        {
            if (img == null || sprite == null) return;
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
            img.color = Color.white;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
