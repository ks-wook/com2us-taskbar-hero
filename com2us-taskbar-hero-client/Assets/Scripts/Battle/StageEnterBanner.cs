using UnityEngine;
using UnityEngine.UI;
using TaskbarHero.Client.Managers;

namespace TaskbarHero.Client.Battle
{
    /// <summary>
    /// 스테이지 입장 시 화면 중단(전투 레인 위쪽)에 "지역 · 스테이지"를 잠깐 띄웠다가 서서히 사라지는 배너.
    /// 페이드 인 → 유지 → 페이드 아웃 후 자동 파괴한다. 슬로우모션(timeScale)과 무관하게 unscaled 시간으로 재생한다.
    /// <para>
    /// 정적 계층(Canvas·배경 바·지역/스테이지 텍스트)은 <b>프리팹으로 baked</b>되어(에디터 빌더 <c>StageEnterBannerBuilder</c>)
    /// 인스펙터에서 손쉽게 수정할 수 있다. 런타임에는 <see cref="DungeonBattleFlow"/>가 프리팹을 Instantiate해
    /// <see cref="Play"/>로 문구를 채우고 재생한다. 프리팹이 배선되지 않았을 때를 대비해 코드로 계층을 생성하는
    /// 폴백(<see cref="Show"/>·<see cref="Construct"/>)도 유지한다.
    /// </para>
    /// <para>
    /// 보스러시 라운드 전환은 문구 대신 <b>아트 조합</b>으로 띄운다(<see cref="PlayRoundArt"/>) —
    /// 'ROUND' 명판(<c>boss_rush_round.png</c>) <b>중앙 하단</b>에 라운드 숫자(<c>digit_N.png</c>)를 걸친다.
    /// 아트는 <see cref="BossRushBattleFlow"/>가 들고 있다가 넘겨준다(배너 프리팹은 스테이지 입장용 그대로).
    /// </para>
    /// </summary>
    public class StageEnterBanner : MonoBehaviour
    {
        // 지역명(act 1~5). 스테이지 선택 UI의 지역 구성과 일치.
        private static readonly string[] RegionNames = { "평원", "얼음", "화산", "사막", "묘지" };

        [Header("연출 타이밍 (인스펙터에서 수정 가능)")]
        [Tooltip("페이드 인 시간(초).")]
        [SerializeField] private float fadeIn = 0.35f;
        [Tooltip("완전 표시 유지 시간(초).")]
        [SerializeField] private float hold = 1.5f;
        [Tooltip("페이드 아웃 시간(초).")]
        [SerializeField] private float fadeOut = 0.9f;
        [Tooltip("페이드 아웃 시 위로 떠오르는 거리(px).")]
        [SerializeField] private float riseDistance = 40f;

        [Header("구성 참조 (에디터 빌더가 배선 — 직접 수정 불필요)")]
        [SerializeField] private CanvasGroup _cg;
        [SerializeField] private RectTransform _content;
        [SerializeField] private Image _bar;
        [SerializeField] private Text _regionText;
        [SerializeField] private Text _stageText;

        // ── 보스러시 라운드 아트 표기 ──
        // 아트(boss_rush_round·digit_N)는 Multiple로 임포트되어 서브 스프라이트가 **투명 여백을 잘라낸
        // 그림 영역**이다(실측: 명판 730×329 / 숫자 95~129×154). 그래서 rect 크기 = 보이는 크기이고,
        // 여백 보정 없이 스프라이트 비율만 지키면 된다.
        private const float PlaqueLetterCenterRatio = 0.537f;   // 명판 위 → 'ROUND' 글자 중심(실측 230~357px / 329px)

        private const float RoundPlaqueWidth = 360f;   // 명판 표시 가로(px)
        private const float RoundDigitHeight = 88f;    // 숫자 표시 세로(px) — 명판 글자보다 조금 크게
        /// <summary>숫자가 명판 아래끝 <b>안쪽으로 물리는</b> 깊이(px). 숫자를 명판 하단 가운데에 걸쳐 두면
        /// 두 아트가 한 덩어리로 읽힌다 — 완전히 띄우면 따로 떠 있는 것처럼 보인다.</summary>
        private const float RoundDigitOverlap = 18f;
        private const float RoundRowTop = 6f;          // 컨텐츠 상단 → 명판 위

        private RectTransform _roundRow;
        private Image _roundPlaque;
        private Image _roundDigit;

        private float _baseY;
        private float _t;
        private bool _playing;

        private bool AlreadyBuilt => _content != null;

        private void Awake()
        {
            if (!AlreadyBuilt)
            {
                Construct(); // 폴백(프리팹 없이 붙은 경우)
            }
        }

        /// <summary>에디터 빌드 전용: 정적 계층을 생성하고 참조를 배선한다(프리팹 저장용).</summary>
        public void EditorConstruct()
        {
            Construct();
        }

        /// <summary>지정 스테이지 좌표로 입장 배너를 코드로 생성해 띄운다(프리팹 미배선 시 폴백).</summary>
        public static void Show(int act, int difficulty, int stage)
        {
            var go = new GameObject("StageEnterBanner");
            var banner = go.AddComponent<StageEnterBanner>(); // Awake에서 Construct()
            banner.Play(act, difficulty, stage);
        }

        /// <summary>임의 문구(윗줄·아랫줄)로 입장 배너를 재생한다 — 보스러시 라운드 전환이
        /// <c>ROUND 3</c> / <c>5 ROUNDS</c> 처럼 스테이지 좌표가 아닌 표기를 쓰기 위해 쓰는 진입점이다.
        /// 연출(페이드·떠오름)·사운드는 스테이지 입장과 같다.</summary>
        public void PlayText(string topLine, string bottomLine, SoundId sfx = SoundId.StageEnter)
        {
            SoundManager.Sfx(sfx);
            if (!AlreadyBuilt)
            {
                Construct();
            }
            SetRoundArtMode(false);
            if (_regionText != null)
            {
                _regionText.text = topLine;
            }
            if (_stageText != null)
            {
                _stageText.text = bottomLine;
            }
            BeginPlay();
        }

        /// <summary>이미 생성된(프리팹) 배너에 스테이지 문구를 채우고 페이드 연출을 시작한다.</summary>
        public void Play(int act, int difficulty, int stage)
        {
            SoundManager.Sfx(SoundId.StageEnter);
            if (!AlreadyBuilt)
            {
                Construct();
            }

            SetRoundArtMode(false);

            string region = act >= 1 && act <= RegionNames.Length ? RegionNames[act - 1] : $"{act}지역";
            if (_regionText != null)
            {
                _regionText.text = $"{region} 지역";
            }
            if (_stageText != null)
            {
                _stageText.text = $"STAGE {act}-{stage}";
            }

            BeginPlay();
        }

        /// <summary>보스러시 라운드 진입 배너를 <b>아트 조합</b>으로 재생한다 — 'ROUND' 명판 중앙 하단에
        /// 라운드 숫자 아트를 걸쳐 띄운다(문구는 쓰지 않는다).
        /// 아트가 하나라도 없으면 기존 문구 표기(<see cref="PlayText"/>)로 폴백한다.
        /// <paramref name="sfx"/>로 배너음을 고른다 — 도전 첫 라운드는 보스러시 시작음, 이후 라운드는 입장음이다.</summary>
        public void PlayRoundArt(Sprite plaque, Sprite digit, string fallbackTopLine,
                                 SoundId sfx = SoundId.StageEnter)
        {
            if (plaque == null || digit == null)
            {
                PlayText(fallbackTopLine, string.Empty, sfx);
                return;
            }

            SoundManager.Sfx(sfx);
            if (!AlreadyBuilt)
            {
                Construct();
            }
            EnsureRoundRow();
            SetRoundArtMode(true);
            LayoutRoundArt(plaque, digit);
            BeginPlay();
        }

        /// <summary>명판·숫자를 담는 행(row)이 없으면 만든다 — 이미 저장된 배너 프리팹에도 붙도록 런타임에 생성한다.</summary>
        private void EnsureRoundRow()
        {
            if (_roundRow != null || _content == null)
            {
                return;
            }

            _roundRow = NewRect("RoundArt", _content);
            _roundRow.anchorMin = _roundRow.anchorMax = new Vector2(0.5f, 1f);
            _roundRow.pivot = new Vector2(0.5f, 1f);
            _roundRow.sizeDelta = new Vector2(900f, 300f);
            _roundRow.anchoredPosition = new Vector2(0f, -RoundRowTop);

            _roundPlaque = NewImage("Plaque", _roundRow, Color.white);
            _roundPlaque.preserveAspect = true;
            _roundDigit = NewImage("Digit", _roundRow, Color.white);
            _roundDigit.preserveAspect = true;
        }

        /// <summary>명판·숫자 아트의 크기와 자리를 잡는다 — 명판을 가운데 두고, 숫자를 그 <b>아래끝 가운데</b>에
        /// <see cref="RoundDigitOverlap"/>만큼 물려 놓는다(숫자가 명판을 살짝 덮는다).</summary>
        private void LayoutRoundArt(Sprite plaque, Sprite digit)
        {
            float plaqueW = RoundPlaqueWidth;
            float plaqueH = plaqueW * (plaque.rect.height / plaque.rect.width);
            float digitH = RoundDigitHeight;
            float digitW = digitH * (digit.rect.width / digit.rect.height);

            _roundRow.anchoredPosition = new Vector2(0f, -RoundRowTop);

            var prt = _roundPlaque.rectTransform;
            _roundPlaque.sprite = plaque;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 1f);
            prt.pivot = new Vector2(0.5f, 1f);
            prt.sizeDelta = new Vector2(plaqueW, plaqueH);
            prt.anchoredPosition = Vector2.zero;

            // 숫자는 명판 아래끝에서 위로 RoundDigitOverlap만큼 겹치게 시작한다(가로는 명판 중앙).
            var drt = _roundDigit.rectTransform;
            _roundDigit.sprite = digit;
            drt.anchorMin = drt.anchorMax = new Vector2(0.5f, 1f);
            drt.pivot = new Vector2(0.5f, 1f);
            drt.sizeDelta = new Vector2(digitW, digitH);
            drt.anchoredPosition = new Vector2(0f, -(plaqueH - RoundDigitOverlap));

            // 숫자가 명판 프레임 위에 오도록(겹치는 부분이 가려지지 않게) 그리기 순서를 뒤로 보낸다.
            drt.SetAsLastSibling();
        }

        /// <summary>라운드 아트 표기와 문구 표기를 전환한다 — 아트 모드에서는 명판이 자체 프레임을 갖고 있고
        /// 문구를 쓰지 않으므로 <b>반투명 배경 바와 두 줄 문구를 모두 감춘다</b>.</summary>
        private void SetRoundArtMode(bool artMode)
        {
            if (_bar == null && _content != null)
            {
                var bar = _content.Find("Bar");   // 프리팹이 참조 없이 저장된 경우 대비
                if (bar != null)
                {
                    _bar = bar.GetComponent<Image>();
                }
            }
            if (_bar != null)
            {
                _bar.gameObject.SetActive(!artMode);
            }
            if (_regionText != null)
            {
                _regionText.gameObject.SetActive(!artMode);
            }
            if (_stageText != null)
            {
                _stageText.gameObject.SetActive(!artMode);
            }
            if (_roundRow != null)
            {
                _roundRow.gameObject.SetActive(artMode);
            }
        }

        /// <summary>문구가 채워진 배너의 페이드 연출을 처음부터 시작한다(Play·PlayText 공용).</summary>
        private void BeginPlay()
        {
            _baseY = _content != null ? _content.anchoredPosition.y : -560f;
            _t = 0f;
            _playing = true;
            if (_cg != null)
            {
                _cg.alpha = 0f;
            }
        }

        private void Update()
        {
            if (!_playing)
            {
                return;
            }

            _t += Time.unscaledDeltaTime;

            float alpha;
            float rise = 0f;
            if (_t < fadeIn)
            {
                alpha = _t / fadeIn;
            }
            else if (_t < fadeIn + hold)
            {
                alpha = 1f;
            }
            else
            {
                float f = (_t - fadeIn - hold) / fadeOut; // 0→1
                if (f >= 1f)
                {
                    Destroy(gameObject);
                    return;
                }
                alpha = 1f - f;
                rise = f * riseDistance;
            }

            if (_cg != null)
            {
                _cg.alpha = alpha;
            }
            if (_content != null)
            {
                _content.anchoredPosition = new Vector2(0f, _baseY + rise);
            }
        }

        // ── 계층 생성 ──

        /// <summary>Canvas·배경 바·지역/스테이지 텍스트 계층을 생성하고 직렬화 참조를 채운다.</summary>
        private void Construct()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvas = gameObject.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = gameObject.AddComponent<Canvas>();
            }
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = UiSortingOrder.BattleEnterBanner; // HUD·진행 바 위, 기능 패널 아래

            var scaler = gameObject.GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                scaler = gameObject.AddComponent<CanvasScaler>();
            }
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            // 현재 씬 규격(GameScene은 높이 1440 기준)으로 즉시 맞춘다 — 주기 스윕을 기다리면 첫 표시 때 잠깐 크게 그려진다.
            GameViewLayout.ApplyCurrentScaler(scaler);

            if (gameObject.GetComponent<GraphicRaycaster>() == null)
            {
                gameObject.AddComponent<GraphicRaycaster>().enabled = false; // 입력은 막지 않음
            }

            _cg = gameObject.GetComponent<CanvasGroup>();
            if (_cg == null)
            {
                _cg = gameObject.AddComponent<CanvasGroup>();
            }
            _cg.interactable = false;
            _cg.blocksRaycasts = false;
            _cg.alpha = 0f;

            // 중앙 컨테이너(상단 앵커 기준으로 아래로 내려 전투 레인에 가깝게 노출)
            _content = NewRect("Content", transform);
            _content.anchorMin = _content.anchorMax = new Vector2(0.5f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.sizeDelta = new Vector2(900f, 200f);
            _content.anchoredPosition = new Vector2(0f, -560f);

            // 반투명 배경 바
            _bar = NewImage("Bar", _content, new Color(0f, 0f, 0f, 0.5f));
            var brt = _bar.rectTransform;
            brt.anchorMin = new Vector2(0.5f, 0.5f);
            brt.anchorMax = new Vector2(0.5f, 0.5f);
            brt.pivot = new Vector2(0.5f, 0.5f);
            brt.sizeDelta = new Vector2(560f, 180f);
            brt.anchoredPosition = new Vector2(0f, -90f);

            _regionText = NewText("Region", _content, font, "지역", 60, TextAnchor.MiddleCenter);
            _regionText.color = new Color(1f, 0.92f, 0.5f);
            _regionText.fontStyle = FontStyle.Bold;
            var rrt = _regionText.rectTransform;
            rrt.anchorMin = rrt.anchorMax = new Vector2(0.5f, 1f);
            rrt.pivot = new Vector2(0.5f, 1f);
            rrt.sizeDelta = new Vector2(760f, 76f);
            rrt.anchoredPosition = new Vector2(0f, -30f);

            _stageText = NewText("Stage", _content, font, "STAGE", 44, TextAnchor.MiddleCenter);
            _stageText.color = Color.white;
            var srt = _stageText.rectTransform;
            srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 1f);
            srt.pivot = new Vector2(0.5f, 1f);
            srt.sizeDelta = new Vector2(760f, 60f);
            srt.anchoredPosition = new Vector2(0f, -108f);
        }

        // ── UI 헬퍼 ──

        private static RectTransform NewRect(string name, Transform parent)
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
            img.raycastTarget = false;
            return img;
        }

        private static Text NewText(string name, Transform parent, Font font, string content, int size, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = font;
            t.text = content;
            t.fontSize = size;
            t.alignment = anchor;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }
    }
}
