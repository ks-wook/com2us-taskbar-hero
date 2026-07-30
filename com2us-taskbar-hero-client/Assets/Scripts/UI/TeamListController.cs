using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TaskbarHero.Client.Managers;
using TaskbarHero.Common.Dto;
using TaskbarHero.Common.MasterData;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 파티 편성 전용 씬(TeamListScene)의 컨트롤러.
    /// 좌측 좁은 영역에 계정이 보유한 캐릭터를 초상화와 함께 나열하고, 우측 넓은 영역에 현재 파티(자리 1~3)를
    /// 카드로 보여준다. 모든 조작(추가·제외·자리 교체)은 그 즉시 <b>파티 전체 스냅샷</b>을
    /// <c>POST /api/game/party/arrange</c> 로 보낸다(세이브 데이터 기획서 5.5 — 이동 절차가 아니라 최종 상태).
    ///
    /// 조작 규칙
    /// <list type="bullet">
    /// <item>좌측 캐릭터 클릭 — 선택된 자리가 있으면 그 자리에 배치(있던 캐릭터는 미편성), 없으면 첫 빈 자리에 추가</item>
    /// <item>우측 카드 클릭 — 선택/선택 해제, 이미 다른 자리를 선택한 상태면 두 자리를 교체(빈 자리면 이동)</item>
    /// <item>카드의 [제외] — 그 자리 캐릭터를 파티에서 뺀다(마지막 1명은 서버가 거부하므로 미리 차단)</item>
    /// <item>좌측 하단 [캐릭터 추가] — 생성 비용을 안내하고 CreateCharacterScene으로 이동(복귀는 이 씬)</item>
    /// </list>
    ///
    /// 계층은 코드로 구성한다(<see cref="Construct"/>). 씬 생성·아트 배선은 에디터 도구
    /// <c>TaskbarHero/UI/편성 씬(TeamListScene) 생성</c>이 담당한다.
    /// </summary>
    public class TeamListController : MonoBehaviour
    {
        private const int MaxSlots = 3;
        private const float CanvasRefWidth = 1080f;
        private const float CanvasRefHeight = 1920f;

        // 좌우 분할: 좌측은 좁은 목록, 우측은 넓은 편성 영역.
        // 조작 안내는 머리말(제목 아래), 상태 문구는 화면 맨 아래 띠에 두어 패널 프레임 아트와 겹치지 않게 한다.
        private const float LeftWidth = 360f;
        private const float Margin = 24f;
        private const float HeaderHeight = 124f;  // 제목 + 조작 안내 줄
        private const float FooterHeight = 60f;   // 상태 문구 줄
        private const float PanelPad = 46f;       // 패널 프레임 아트 안쪽 여백(9-slice 나무 테두리를 피한다)
        private const float RowHeight = 96f;
        private const float RowGap = 10f;
        private const float RowPortraitSize = 76f;
        private const float AddButtonHeight = 72f;
        private const float CardGap = 16f;

        // 파티 자리 카드 = 슬롯 아트(party_slot.png 191×319)를 원본 비율로 놓는다.
        private const float CardAspect = 191f / 319f;
        // 슬롯 아트 안쪽 창(석재 액자 안의 어두운 영역)의 비율 좌표. 창 위쪽이 아치형이라
        // 사각형 초상화가 모서리를 넘지 않도록 실제 창보다 살짝 좁게 잡는다.
        private const float SlotWindowXMin = 0.175f;
        private const float SlotWindowXMax = 0.845f;
        private const float SlotWindowYMin = 0.135f;
        private const float SlotWindowYMax = 0.830f;

        // 패널 아트(ui_bg)는 9-slice 경계가 커서(230·250px) 그대로 쓰면 테두리가 패널을 먹는다.
        // pixelsPerUnitMultiplier로 테두리를 축소한다(HUD 배경과 같은 방식).
        private const float PanelPixelsPerUnitMultiplier = 3f;

        // 초상화 렌더 설정. 슬롯마다 화면 밖 격리 위치에 캐릭터를 두고 전용 카메라로 렌더한다(인벤토리와 동일 방식).
        // orthographicSize가 작을수록 확대 — 전투 초상화(PortraitCameraRig, 0.27/aim 0.48)와 같은 '얼굴 위주' 프레이밍을 쓴다.
        private const int PortraitLayer = 28;      // 인벤토리와 공유(동시 표시되지 않음), 전투 초상(29~31)과 비겹침
        // 카드 초상화 RT는 슬롯 아트 '안쪽 창'과 같은 비율로 만든다(늘어남·잘림 없이 창을 정확히 채우도록).
        private const int CardPortraitWidth = 288;
        private const int CardPortraitHeight = 492;
        private const float CardPortraitOrtho = 0.33f;   // 값이 크면 캐릭터가 작게 보인다(창 대비 여유를 둔 얼굴 클로즈업)
        private static readonly Vector2 CardPortraitAim = new Vector2(0f, 0.48f);
        private const int RowPortraitPixels = 96;
        private const float RowPortraitOrtho = 0.32f;
        private static readonly Vector2 RowPortraitAim = new Vector2(0f, 0.48f);
        // 파티 카드 초상화는 배경을 <b>투명</b>하게 렌더해 슬롯 아트(party_slot) 위에 캐릭터만 겹쳐 보이게 한다.
        private static readonly Color CardPortraitBg = new Color(0f, 0f, 0f, 0f);
        // 좌측 목록 초상화는 작은 칸이라 배경을 채워(어두운 남색) 썸네일처럼 보이게 둔다.
        private static readonly Color RowPortraitBg = new Color(0.169f, 0.176f, 0.247f, 1f);
        private const int MaxRowPortraits = 6;     // 좌측 목록 초상화 상한(카메라 수 제한). 초과 행은 이름만 표시

        private static readonly Color BgColor = new Color(0.09f, 0.10f, 0.14f, 1f);
        private static readonly Color PanelColor = new Color(0.15f, 0.17f, 0.26f, 1f);
        private static readonly Color RowColor = new Color(0.20f, 0.23f, 0.34f, 1f);
        private static readonly Color RowDimColor = new Color(0.14f, 0.15f, 0.21f, 1f);
        private static readonly Color SlotEmptyColor = new Color(0.12f, 0.13f, 0.19f, 1f);
        private static readonly Color TitleColor = new Color(0.92f, 0.90f, 0.82f, 1f);
        private static readonly Color SubColor = new Color(0.68f, 0.70f, 0.78f, 1f);
        private static readonly Color CardSelectedColor = new Color(0.33f, 0.30f, 0.18f, 1f); // 선택된 자리(아트 없을 때의 폴백)
        private static readonly Color CardSelectedTint = new Color(1f, 0.86f, 0.52f, 1f);     // 선택된 자리의 슬롯 아트 색(금색)
        private static readonly Color SelectedLabelColor = new Color(1f, 0.86f, 0.35f, 1f);

        [SerializeField] private RectTransform _ownedList;   // 좌측: 보유 캐릭터 행이 쌓이는 컨테이너
        [SerializeField] private RectTransform _partyArea;   // 우측: 파티 자리 카드가 놓이는 컨테이너
        [SerializeField] private Text _statusText;
        [SerializeField] private Text _helpText;
        [SerializeField] private Button _backButton;
        [SerializeField] private Button _addCharacterButton;

        [Header("UI 아트(에디터 빌더가 배선: Assets/Art/UI · Assets/Art/Icon)")]
        [Tooltip("좌·우 패널 배경 프레임(Assets/Art/UI/ui_bg, 9-slice). 없으면 단색 패널.")]
        [SerializeField] private Sprite _panelSprite;
        [Tooltip("파티 자리 슬롯 아트(Assets/Art/UI/TeamList/party_slot). 석재 액자 + 빈 자리 실루엣. 없으면 단색 카드.")]
        [SerializeField] private Sprite _slotSprite;
        [Tooltip("버튼 배경(Assets/Art/UI/pixel_rpg_button, 9-slice). 없으면 단색 버튼.")]
        [SerializeField] private Sprite _buttonSprite;
        [Tooltip("제목 옆 편성 아이콘(Assets/Art/Icon/편성.png). 없으면 아이콘 없이 제목만 표시.")]
        [SerializeField] private Sprite _titleIcon;

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private PortraitSlot[] _cardPortraits;   // 우측 카드 3개용(고정 RT 크기)
        private PortraitSlot[] _rowPortraits;    // 좌측 목록용(고정 RT 크기)
        private int _selectedSlot;               // 선택된 파티 자리(0 = 선택 없음)
        private bool _busy;

        /// <summary>초상화 렌더러 1기(전용 카메라·RT를 얹은 화면 밖 오브젝트)와 현재 표시 중인 외형 키.</summary>
        private sealed class PortraitSlot
        {
            public CharacterPortrait portrait;
            public GameObject stage;
            public int key = int.MinValue;   // 표시 중인 외형(직업+성별) 키. 같으면 다시 심지 않는다
        }

        /// <summary>에디터 씬 빌더가 계층을 정적으로 굽기 위해 호출한다.</summary>
        public void EditorConstruct() => Construct();

        /// <summary>씬에 구워진 계층이 없거나 옛 버전이면(참조 누락) 다시 구성하고, 고정 버튼의 클릭 핸들러를 연결한다.</summary>
        private void Awake()
        {
            if (_ownedList == null || _partyArea == null || _addCharacterButton == null || _helpText == null)
            {
                RebuildHierarchy();
            }
            WireRuntime();
        }

        /// <summary>
        /// 씬에 구워진 고정 버튼(뒤로 · 캐릭터 추가)의 클릭 핸들러를 런타임에 연결한다.
        /// <c>onClick.AddListener</c>는 비영구(non-persistent) 리스너라 씬·프리팹에 직렬화되지 않으므로,
        /// 에디터 빌드 때 <see cref="Construct"/>에서 붙인 리스너는 실행 시 남아 있지 않는다 —
        /// 그래서 매 실행마다 여기서 다시 붙인다(중복 방지로 먼저 모두 제거).
        /// </summary>
        private void WireRuntime()
        {
            if (_backButton != null)
            {
                _backButton.onClick.RemoveAllListeners();
                _backButton.onClick.AddListener(OnBack);
            }
            if (_addCharacterButton != null)
            {
                _addCharacterButton.onClick.RemoveAllListeners();
                _addCharacterButton.onClick.AddListener(OnAddCharacter);
            }
        }

        private void Start()
        {
            Refresh();
        }

        /// <summary>파괴 시 화면 밖 초상화 스테이지(카메라·RT·캐릭터 인스턴스)를 함께 정리한다.</summary>
        private void OnDestroy()
        {
            ReleasePortraits(_cardPortraits);
            ReleasePortraits(_rowPortraits);
            _cardPortraits = null;
            _rowPortraits = null;
        }

        /// <summary>초상화 스테이지 배열을 파괴한다.</summary>
        private static void ReleasePortraits(PortraitSlot[] slots)
        {
            if (slots == null)
            {
                return;
            }
            foreach (var s in slots)
            {
                if (s != null && s.stage != null) Destroy(s.stage);
            }
        }

        // ---- 계층 구성 ----

        /// <summary>구워진(옛 버전) 계층을 지우고 <see cref="Construct"/>로 처음부터 다시 만든다.</summary>
        private void RebuildHierarchy()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Destroy(transform.GetChild(i).gameObject);
            }
            _spawned.Clear();
            Construct();
        }

        /// <summary>캔버스·좌우 패널·제목·뒤로/캐릭터 추가 버튼을 만든다.</summary>
        private void Construct()
        {
            var canvas = gameObject.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = gameObject.AddComponent<Canvas>();
            }
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = UiSortingOrder.Panel;

            var scaler = gameObject.GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                scaler = gameObject.AddComponent<CanvasScaler>();
            }
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(CanvasRefWidth, CanvasRefHeight);
            scaler.matchWidthOrHeight = 0.5f;

            if (gameObject.GetComponent<GraphicRaycaster>() == null)
            {
                gameObject.AddComponent<GraphicRaycaster>();
            }

            // 전체 배경
            var bg = NewImage("Background", transform, BgColor);
            Stretch(bg.rectTransform);

            BuildHeader();
            BuildOwnedPanel();
            BuildPartyPanel();
            BuildFooter();
        }

        /// <summary>제목(아이콘 + '파티 편성')·좌측 상단 '뒤로' 버튼·조작 안내 줄을 만든다.</summary>
        private void BuildHeader()
        {
            var title = NewText("Title", transform, "파티 편성", 44, FontStyle.Bold, TitleColor, TextAnchor.MiddleCenter);
            title.anchorMin = title.anchorMax = new Vector2(0.5f, 1f);
            title.pivot = new Vector2(0.5f, 1f);
            title.anchoredPosition = new Vector2(0f, -Margin);
            title.sizeDelta = new Vector2(CanvasRefWidth, 60f);

            if (_titleIcon != null)
            {
                var icon = NewImage("TitleIcon", transform, Color.white);
                icon.sprite = _titleIcon;
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                var irt = icon.rectTransform;
                irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 1f);
                irt.pivot = new Vector2(1f, 1f);
                irt.anchoredPosition = new Vector2(-130f, -Margin - 4f);
                irt.sizeDelta = new Vector2(52f, 52f);
            }

            var back = NewImage("BackButton", transform, RowColor).rectTransform;
            ApplyButtonSprite(back.GetComponent<Image>());
            back.anchorMin = back.anchorMax = new Vector2(0f, 1f);
            back.pivot = new Vector2(0f, 1f);
            back.anchoredPosition = new Vector2(Margin, -Margin);
            back.sizeDelta = new Vector2(160f, 60f);
            var backLabel = NewText("Label", back, "뒤로", 26, FontStyle.Bold, TitleColor, TextAnchor.MiddleCenter);
            Stretch(backLabel);
            _backButton = back.gameObject.AddComponent<Button>();
            _backButton.onClick.AddListener(OnBack);

            // 조작 안내(고정 문구) — 제목 아래 줄. 패널 프레임 아트와 겹치지 않도록 패널 밖에 둔다.
            var help = NewText("Help", transform,
                "자리를 눌러 선택 → 다른 자리를 누르면 교체 · [제외]로 파티에서 뺀다 · 좌측 목록을 눌러 배치",
                20, FontStyle.Normal, SubColor, TextAnchor.MiddleCenter);
            help.anchorMin = new Vector2(0f, 1f);
            help.anchorMax = new Vector2(1f, 1f);
            help.pivot = new Vector2(0.5f, 1f);
            help.offsetMin = new Vector2(Margin, -(Margin + HeaderHeight - 24f));
            help.offsetMax = new Vector2(-Margin, -(Margin + 68f));
            _helpText = help.GetComponent<Text>();
        }

        /// <summary>화면 맨 아래 상태 문구 줄(저장 결과·안내)을 만든다.</summary>
        private void BuildFooter()
        {
            var status = NewText("Status", transform, string.Empty, 22, FontStyle.Normal, SubColor, TextAnchor.MiddleCenter);
            status.anchorMin = new Vector2(0f, 0f);
            status.anchorMax = new Vector2(1f, 0f);
            status.pivot = new Vector2(0.5f, 0f);
            status.offsetMin = new Vector2(Margin, 12f);
            status.offsetMax = new Vector2(-Margin, 12f + FooterHeight - 24f);
            _statusText = status.GetComponent<Text>();
        }

        /// <summary>좌측 '보유 캐릭터' 패널(목록 컨테이너 + 하단 '캐릭터 추가' 버튼)을 만든다.</summary>
        private void BuildOwnedPanel()
        {
            var panel = NewImage("OwnedPanel", transform, PanelColor);
            ApplyPanelSprite(panel);
            var left = panel.rectTransform;
            left.anchorMin = new Vector2(0f, 0f);
            left.anchorMax = new Vector2(0f, 1f);
            left.pivot = new Vector2(0f, 0.5f);
            left.offsetMin = new Vector2(Margin, Margin + FooterHeight);
            left.offsetMax = new Vector2(Margin + LeftWidth, -(Margin + HeaderHeight));

            var leftTitle = NewText("OwnedTitle", left, "보유 캐릭터", 26, FontStyle.Bold, TitleColor, TextAnchor.MiddleCenter);
            leftTitle.anchorMin = new Vector2(0f, 1f);
            leftTitle.anchorMax = new Vector2(1f, 1f);
            leftTitle.pivot = new Vector2(0.5f, 1f);
            leftTitle.offsetMin = new Vector2(PanelPad, -(PanelPad + 38f));
            leftTitle.offsetMax = new Vector2(-PanelPad, -PanelPad);

            _ownedList = NewRect("OwnedList", left);
            _ownedList.anchorMin = new Vector2(0f, 0f);
            _ownedList.anchorMax = new Vector2(1f, 1f);
            _ownedList.offsetMin = new Vector2(PanelPad, PanelPad + AddButtonHeight + 14f);
            _ownedList.offsetMax = new Vector2(-PanelPad, -(PanelPad + 46f));

            // 하단 '캐릭터 추가' 버튼(옛 편성 팝업의 '+' 자리를 대신한다).
            var add = NewImage("AddCharacterButton", left, new Color(0.25f, 0.55f, 0.35f, 1f)).rectTransform;
            ApplyButtonSprite(add.GetComponent<Image>());
            add.anchorMin = new Vector2(0f, 0f);
            add.anchorMax = new Vector2(1f, 0f);
            add.pivot = new Vector2(0.5f, 0f);
            add.offsetMin = new Vector2(PanelPad, PanelPad);
            add.offsetMax = new Vector2(-PanelPad, PanelPad + AddButtonHeight);
            var addLabel = NewText("Label", add, "＋ 캐릭터 추가", 24, FontStyle.Bold, TitleColor, TextAnchor.MiddleCenter);
            Stretch(addLabel);
            _addCharacterButton = add.gameObject.AddComponent<Button>();
            _addCharacterButton.onClick.AddListener(OnAddCharacter);
        }

        /// <summary>우측 '파티 구성원' 패널(카드 컨테이너 + 조작 안내 + 상태 문구)을 만든다.</summary>
        private void BuildPartyPanel()
        {
            var panel = NewImage("PartyPanel", transform, PanelColor);
            ApplyPanelSprite(panel);
            var right = panel.rectTransform;
            right.anchorMin = new Vector2(0f, 0f);
            right.anchorMax = new Vector2(1f, 1f);
            right.pivot = new Vector2(0.5f, 0.5f);
            right.offsetMin = new Vector2(Margin + LeftWidth + Margin, Margin + FooterHeight);
            right.offsetMax = new Vector2(-Margin, -(Margin + HeaderHeight));

            var rightTitle = NewText("PartyTitle", right, "파티 구성원", 26, FontStyle.Bold, TitleColor, TextAnchor.MiddleCenter);
            rightTitle.anchorMin = new Vector2(0f, 1f);
            rightTitle.anchorMax = new Vector2(1f, 1f);
            rightTitle.pivot = new Vector2(0.5f, 1f);
            rightTitle.offsetMin = new Vector2(PanelPad, -(PanelPad + 38f));
            rightTitle.offsetMax = new Vector2(-PanelPad, -PanelPad);

            _partyArea = NewRect("PartyArea", right);
            _partyArea.anchorMin = new Vector2(0f, 0f);
            _partyArea.anchorMax = new Vector2(1f, 1f);
            _partyArea.offsetMin = new Vector2(PanelPad, PanelPad);
            _partyArea.offsetMax = new Vector2(-PanelPad, -(PanelPad + 46f));
        }

        // ---- 갱신 ----

        /// <summary>세션의 보유 캐릭터로 좌측 목록과 우측 파티 카드를 다시 그린다(초상화 렌더러는 재사용).</summary>
        public void Refresh()
        {
            foreach (var go in _spawned)
            {
                if (go != null) Destroy(go);
            }
            _spawned.Clear();
            SetStatus(string.Empty); // 이전 상태 문구를 지운다(저장 결과 문구는 호출자가 다시 채운다)

            var chars = Session.GameData != null ? Session.GameData.characters : null;
            if (chars == null || chars.Count == 0)
            {
                HideAllPortraits();
                SetStatus("보유한 캐릭터가 없습니다. [캐릭터 추가]로 생성하세요.");
                return;
            }

            EnsurePortraits();
            BuildOwnedRows(chars);
            BuildPartySlots(chars);
        }

        /// <summary>좌측: 보유 캐릭터 행(초상화 + 직업·레벨). 이미 편성된 캐릭터는 흐리게 하고 클릭을 막는다.</summary>
        private void BuildOwnedRows(List<CharacterDto> chars)
        {
            float y = 0f;
            int portraitIndex = 0;
            foreach (var c in chars)
            {
                if (c == null) continue;
                bool inParty = c.slot >= 1 && c.slot <= MaxSlots;

                var row = NewImage($"Owned_{c.characterId}", _ownedList, inParty ? RowDimColor : RowColor).rectTransform;
                row.anchorMin = new Vector2(0f, 1f);
                row.anchorMax = new Vector2(1f, 1f);
                row.pivot = new Vector2(0.5f, 1f);
                row.offsetMin = new Vector2(0f, 0f);
                row.offsetMax = new Vector2(0f, 0f);
                row.anchoredPosition = new Vector2(0f, y);
                row.sizeDelta = new Vector2(0f, RowHeight);
                _spawned.Add(row.gameObject);

                // 초상화(좌측 정사각). 렌더러 수가 상한을 넘으면 초상화 없이 텍스트만 표시한다.
                float textLeft = 14f;
                if (portraitIndex < MaxRowPortraits)
                {
                    var render = NewRawImage("Portrait", row);
                    var prt = render.rectTransform;
                    prt.anchorMin = new Vector2(0f, 0.5f);
                    prt.anchorMax = new Vector2(0f, 0.5f);
                    prt.pivot = new Vector2(0f, 0.5f);
                    prt.anchoredPosition = new Vector2(10f, 0f);
                    prt.sizeDelta = new Vector2(RowPortraitSize, RowPortraitSize);
                    render.raycastTarget = false;
                    AssignPortrait(_rowPortraits, portraitIndex, render, c);
                    textLeft = 10f + RowPortraitSize + 12f;
                    portraitIndex++;
                }

                var name = NewText("Name", row, ClassNameOf(c.classCode), 24, FontStyle.Bold,
                    inParty ? SubColor : TitleColor, TextAnchor.LowerLeft);
                name.anchorMin = new Vector2(0f, 0.5f);
                name.anchorMax = new Vector2(1f, 1f);
                name.offsetMin = new Vector2(textLeft, 0f);
                name.offsetMax = new Vector2(-14f, -8f);

                string sub = inParty ? $"Lv.{c.level} · 편성 {c.slot}번" : $"Lv.{c.level} · 미편성";
                var lv = NewText("Sub", row, sub, 20, FontStyle.Normal, SubColor, TextAnchor.UpperLeft);
                lv.anchorMin = new Vector2(0f, 0f);
                lv.anchorMax = new Vector2(1f, 0.5f);
                lv.offsetMin = new Vector2(textLeft, 8f);
                lv.offsetMax = new Vector2(-14f, 0f);

                var btn = row.gameObject.AddComponent<Button>();
                int id = c.characterId;
                btn.interactable = !inParty;
                btn.onClick.AddListener(() => OnOwnedClicked(id));

                y -= RowHeight + RowGap;
            }

            // 남는 렌더러는 캐릭터를 비워 불필요한 렌더를 막는다.
            for (int i = portraitIndex; _rowPortraits != null && i < _rowPortraits.Length; i++)
            {
                ClearPortrait(_rowPortraits, i);
            }
        }

        /// <summary>
        /// 우측: 파티 자리 3칸을 <b>왼쪽부터 차례로(1·2·3번)</b> 세로로 배치한다.
        /// 칸마다 위에서 아래로 자리 번호 → 슬롯 아트(안쪽 창에 얼굴 초상화) → 직업 → 레벨 → [제외] 순으로 쌓는다.
        /// 슬롯 아트는 <see cref="AspectRatioFitter"/>로 원본 비율(191:319)을 유지하므로 창 비율이 바뀌어도 액자가 늘어나지 않고,
        /// 캐릭터가 없는 자리는 아트에 그려진 <b>물음표 실루엣</b>이 그대로 빈 자리 표시가 된다.
        /// </summary>
        private void BuildPartySlots(List<CharacterDto> chars)
        {
            var bySlot = new Dictionary<int, CharacterDto>();
            foreach (var c in chars)
            {
                if (c != null && c.slot >= 1 && c.slot <= MaxSlots) bySlot[c.slot] = c;
            }

            for (int slot = 1; slot <= MaxSlots; slot++)
            {
                bool filled = bySlot.TryGetValue(slot, out var c);
                bool selected = _selectedSlot == slot;
                int index = slot - 1;

                // 칸(컬럼) — 좌→우 3등분. 카드는 이 칸 안에서 비율을 지켜 놓인다.
                var column = NewRect($"Column_{slot}", _partyArea);
                column.anchorMin = new Vector2(index / (float)MaxSlots, 0f);
                column.anchorMax = new Vector2((index + 1) / (float)MaxSlots, 1f);
                column.offsetMin = new Vector2(slot == 1 ? 0f : CardGap * 0.5f, 0f);
                column.offsetMax = new Vector2(slot == MaxSlots ? 0f : -CardGap * 0.5f, 0f);
                _spawned.Add(column.gameObject);

                // 자리 번호 + 선택 상태(칸 최상단)
                string slotLabelText = selected ? $"{slot}번 자리 · 선택됨" : $"{slot}번 자리";
                var slotLabel = NewText("SlotNo", column, slotLabelText, 22, FontStyle.Bold,
                    selected ? SelectedLabelColor : SubColor, TextAnchor.MiddleCenter);
                PlaceFraction(slotLabel, 0f, 0.93f, 1f, 1f, 8f);

                // 슬롯 아트(액자). 원본 비율을 유지하며 칸 상단 영역에 맞춰 놓인다.
                // 선택된 자리는 아트를 금색으로 물들여 강조한다(아트가 없으면 단색 카드로 폴백).
                Color cardColor = _slotSprite != null
                    ? (selected ? CardSelectedTint : Color.white)
                    : (selected ? CardSelectedColor : (filled ? RowColor : SlotEmptyColor));
                // 밴드(카드가 놓일 영역) — AspectRatioFitter는 Fit 모드에서 대상의 앵커를 부모 전체로 덮어쓰므로,
                // 카드를 칸의 특정 구간에만 놓으려면 그 구간 크기의 부모(밴드)를 한 겹 둬야 한다.
                var band = NewRect($"CardBand_{slot}", column);
                PlaceFraction(band, 0f, 0.35f, 1f, 0.92f, 0f);   // 아래(0.35 미만)는 직업·레벨·[제외] 자리

                var cardImg = NewImage($"Slot_{slot}", band, cardColor);
                if (_slotSprite != null)
                {
                    cardImg.sprite = _slotSprite;
                    cardImg.type = Image.Type.Simple;
                }
                var card = cardImg.rectTransform;
                var cardFitter = cardImg.gameObject.AddComponent<AspectRatioFitter>();
                cardFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                cardFitter.aspectRatio = CardAspect;

                if (filled)
                {
                    // 아트 안쪽 창에 얼굴 초상화를 채운다(RT가 창과 같은 비율이라 늘어남·잘림 없음).
                    // 초상화 배경이 불투명해 아트의 '빈 자리 실루엣'을 그대로 덮는다.
                    var window = NewRect("Window", card);
                    PlaceFraction(window, SlotWindowXMin, SlotWindowYMin, SlotWindowXMax, SlotWindowYMax, 0f);

                    var render = NewRawImage("Portrait", window);
                    render.raycastTarget = false;
                    var fitter = render.gameObject.AddComponent<AspectRatioFitter>();
                    fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                    fitter.aspectRatio = CardPortraitWidth / (float)CardPortraitHeight;
                    AssignPortrait(_cardPortraits, index, render, c);
                }
                else
                {
                    // 빈 자리는 아트의 물음표 실루엣이 그대로 보이게 둔다(덧그리는 문구 없음).
                    ClearPortrait(_cardPortraits, index);
                }

                BuildCardInfo(column, slot, filled ? c : null);

                var btn = cardImg.gameObject.AddComponent<Button>();
                int captured = slot;
                btn.onClick.AddListener(() => OnSlotClicked(captured));
            }
        }

        /// <summary>슬롯 아트 아래쪽(칸 하단)에 직업·레벨 + [제외] 버튼, 또는 빈 자리 배치 안내를 만든다.</summary>
        private void BuildCardInfo(RectTransform column, int slot, CharacterDto c)
        {
            if (c == null)
            {
                var guide = NewText("Guide", column, "좌측 보유 캐릭터를\n눌러 배치하세요", 22, FontStyle.Normal,
                    SubColor, TextAnchor.MiddleCenter);
                PlaceFraction(guide, 0f, 0.10f, 1f, 0.30f, 10f);
                return;
            }

            var name = NewText("Name", column, ClassNameOf(c.classCode), 32, FontStyle.Bold, TitleColor, TextAnchor.MiddleCenter);
            PlaceFraction(name, 0f, 0.25f, 1f, 0.34f, 8f);

            var lv = NewText("Level", column, $"Lv.{c.level}", 24, FontStyle.Normal, SubColor, TextAnchor.MiddleCenter);
            PlaceFraction(lv, 0f, 0.18f, 1f, 0.25f, 8f);

            // [제외] — 카드 아래 별도 버튼이라 카드 클릭(선택/교체)과 겹치지 않는다.
            var exclude = NewImage("ExcludeButton", column, new Color(0.45f, 0.24f, 0.26f, 1f)).rectTransform;
            ApplyButtonSprite(exclude.GetComponent<Image>());
            PlaceFraction(exclude, 0.12f, 0.035f, 0.88f, 0.16f, 0f);
            var excludeLabel = NewText("Label", exclude, "제외", 24, FontStyle.Bold, TitleColor, TextAnchor.MiddleCenter);
            Stretch(excludeLabel);
            var btn = exclude.gameObject.AddComponent<Button>();
            int captured = slot;
            btn.onClick.AddListener(() => OnExcludeClicked(captured));
        }

        // ---- 초상화 렌더러 ----

        /// <summary>카드·목록용 초상화 렌더러를 1회 생성한다(런타임 전용). 이미 있으면 그대로 재사용한다.</summary>
        private void EnsurePortraits()
        {
            if (!Application.isPlaying)
            {
                return;
            }
            if (_cardPortraits == null)
            {
                _cardPortraits = CreatePortraits(MaxSlots, 0f, CardPortraitWidth, CardPortraitHeight,
                    CardPortraitOrtho, CardPortraitAim, CardPortraitBg);
            }
            if (_rowPortraits == null)
            {
                _rowPortraits = CreatePortraits(MaxRowPortraits, 60f, RowPortraitPixels, RowPortraitPixels,
                    RowPortraitOrtho, RowPortraitAim, RowPortraitBg);
            }
        }

        /// <summary>화면 밖 격리 위치에 초상화 렌더러 <paramref name="count"/>기를 만든다(서로 다른 x로 떨어뜨려 카메라가 겹쳐 보지 않게).</summary>
        private static PortraitSlot[] CreatePortraits(int count, float yOffset, int rtWidth, int rtHeight,
            float ortho, Vector2 aim, Color background)
        {
            var slots = new PortraitSlot[count];
            for (int i = 0; i < count; i++)
            {
                var origin = new Vector3(700f + i * 40f, 700f + yOffset, 0f);
                var stage = new GameObject($"TeamListPortraitStage_{yOffset:0}_{i}");
                stage.transform.position = origin;
                var p = stage.AddComponent<CharacterPortrait>();
                p.Initialize(null, PortraitLayer, rtWidth, rtHeight, ortho, aim, background, origin);
                slots[i] = new PortraitSlot { portrait = p, stage = stage };
            }
            return slots;
        }

        /// <summary>지정 렌더러를 새 RawImage에 연결하고, 외형(직업·성별)이 바뀐 경우에만 캐릭터 프리팹을 다시 심는다.</summary>
        private static void AssignPortrait(PortraitSlot[] slots, int index, RawImage target, CharacterDto c)
        {
            if (slots == null || index < 0 || index >= slots.Length || slots[index] == null || c == null)
            {
                return;
            }
            var slot = slots[index];
            slot.stage.SetActive(true);
            slot.portrait.Retarget(target);

            int key = c.classCode * 10 + c.gender;
            if (slot.key != key)
            {
                slot.portrait.SetCharacter(CharacterPrefabDatabase.PrefabOf(c.classCode, c.gender));
                slot.key = key;
            }
            target.color = Color.white;
        }

        /// <summary>지정 렌더러를 비우고 끈다(빈 자리·남는 목록 칸).</summary>
        private static void ClearPortrait(PortraitSlot[] slots, int index)
        {
            if (slots == null || index < 0 || index >= slots.Length || slots[index] == null)
            {
                return;
            }
            var slot = slots[index];
            slot.portrait.Retarget(null);
            if (slot.key != -1)
            {
                slot.portrait.SetCharacter(null);
                slot.key = -1;
            }
            slot.stage.SetActive(false);
        }

        /// <summary>모든 초상화 렌더러를 비운다(보유 캐릭터가 없을 때).</summary>
        private void HideAllPortraits()
        {
            for (int i = 0; _cardPortraits != null && i < _cardPortraits.Length; i++) ClearPortrait(_cardPortraits, i);
            for (int i = 0; _rowPortraits != null && i < _rowPortraits.Length; i++) ClearPortrait(_rowPortraits, i);
        }

        // ---- 편성 조작 ----

        /// <summary>
        /// 좌측 캐릭터 클릭 → 선택된 자리가 있으면 그 자리에 배치(있던 캐릭터는 미편성으로 빠진다),
        /// 선택이 없으면 첫 빈 자리에 추가하고 파티 전체 스냅샷을 저장한다(5.5 스냅샷 규약).
        /// </summary>
        private void OnOwnedClicked(int characterId)
        {
            if (_busy)
            {
                return;
            }

            var chars = Session.GameData != null ? Session.GameData.characters : null;
            if (chars == null)
            {
                return;
            }

            var members = CurrentMembers(chars);
            foreach (var m in members)
            {
                if (m.characterId == characterId)
                {
                    SetStatus("이미 편성된 캐릭터입니다.");
                    return;
                }
            }

            int target = _selectedSlot != 0 ? _selectedSlot : FirstFreeSlot(members);
            if (target == 0)
            {
                SetStatus($"파티는 최대 {MaxSlots}명입니다. 바꿀 자리를 눌러 선택하거나 [제외]하세요.");
                return;
            }

            int replaced = OccupantOf(members, target);
            RemoveSlot(members, target);
            members.Add(new PartyMemberDto { characterId = characterId, slot = target });
            _selectedSlot = 0;
            SaveParty(members, replaced != 0
                ? $"{target}번 자리를 교체했습니다."
                : $"{target}번 자리에 추가했습니다.");
        }

        /// <summary>
        /// 파티 자리 카드 클릭 → 선택이 없으면 그 자리를 선택하고, 같은 자리면 선택 해제,
        /// 다른 자리면 두 자리를 교체한다(대상이 빈 자리면 이동). 교체·이동은 즉시 저장한다.
        /// </summary>
        private void OnSlotClicked(int slot)
        {
            if (_busy)
            {
                return;
            }

            var chars = Session.GameData != null ? Session.GameData.characters : null;
            if (chars == null)
            {
                return;
            }
            var members = CurrentMembers(chars);

            if (_selectedSlot == slot)
            {
                _selectedSlot = 0;
                Refresh();
                SetStatus("선택을 해제했습니다.");
                return;
            }

            if (_selectedSlot == 0)
            {
                if (OccupantOf(members, slot) == 0)
                {
                    SetStatus("빈 자리입니다. 좌측 보유 캐릭터를 눌러 배치하세요.");
                    return;
                }
                _selectedSlot = slot;
                Refresh();
                SetStatus($"{slot}번 자리를 선택했습니다. 옮길 자리를 누르세요.");
                return;
            }

            int from = _selectedSlot;
            int fromChar = OccupantOf(members, from);
            int toChar = OccupantOf(members, slot);
            if (fromChar == 0)
            {
                // 선택 뒤 서버 응답으로 편성이 바뀐 경우(방어) — 선택만 해제한다.
                _selectedSlot = 0;
                Refresh();
                return;
            }

            RemoveSlot(members, from);
            RemoveSlot(members, slot);
            members.Add(new PartyMemberDto { characterId = fromChar, slot = slot });
            if (toChar != 0)
            {
                members.Add(new PartyMemberDto { characterId = toChar, slot = from });
            }
            _selectedSlot = 0;
            SaveParty(members, toChar != 0
                ? $"{from}번 ↔ {slot}번 자리를 교체했습니다."
                : $"{from}번 → {slot}번 자리로 옮겼습니다.");
        }

        /// <summary>[제외] → 그 자리 캐릭터를 파티에서 뺀다(미편성). 마지막 1명은 서버가 거부하므로 미리 차단한다.</summary>
        private void OnExcludeClicked(int slot)
        {
            if (_busy)
            {
                return;
            }

            var chars = Session.GameData != null ? Session.GameData.characters : null;
            if (chars == null)
            {
                return;
            }

            var members = CurrentMembers(chars);
            if (OccupantOf(members, slot) == 0)
            {
                return;
            }
            if (members.Count <= 1)
            {
                SetStatus("파티는 최소 1명이어야 합니다.");
                return;
            }

            RemoveSlot(members, slot);
            _selectedSlot = 0;
            SaveParty(members, $"{slot}번 자리의 캐릭터를 제외했습니다.");
        }

        /// <summary>현재 편성된 멤버를 자리 순으로 모은다(서버로 보낼 스냅샷의 기반).</summary>
        private static List<PartyMemberDto> CurrentMembers(List<CharacterDto> chars)
        {
            var members = new List<PartyMemberDto>();
            for (int slot = 1; slot <= MaxSlots; slot++)
            {
                foreach (var c in chars)
                {
                    if (c != null && c.slot == slot)
                    {
                        members.Add(new PartyMemberDto { characterId = c.characterId, slot = slot });
                        break;
                    }
                }
            }
            return members;
        }

        /// <summary>스냅샷에서 지정 자리의 캐릭터 ID(비어 있으면 0).</summary>
        private static int OccupantOf(List<PartyMemberDto> members, int slot)
        {
            foreach (var m in members)
            {
                if (m != null && m.slot == slot)
                {
                    return m.characterId;
                }
            }
            return 0;
        }

        /// <summary>스냅샷에서 지정 자리를 비운다(그 캐릭터는 미편성이 된다).</summary>
        private static void RemoveSlot(List<PartyMemberDto> members, int slot)
        {
            for (int i = members.Count - 1; i >= 0; i--)
            {
                if (members[i] != null && members[i].slot == slot)
                {
                    members.RemoveAt(i);
                }
            }
        }

        /// <summary>비어 있는 가장 앞 자리 번호(1~3). 빈 자리가 없으면 0.</summary>
        private static int FirstFreeSlot(List<PartyMemberDto> members)
        {
            for (int slot = 1; slot <= MaxSlots; slot++)
            {
                if (OccupantOf(members, slot) == 0)
                {
                    return slot;
                }
            }
            return 0;
        }

        /// <summary>파티 전체 스냅샷을 서버에 저장한다(POST /api/game/party/arrange). 응답의 캐릭터 목록으로 화면을 다시 그린다.</summary>
        private void SaveParty(List<PartyMemberDto> members, string okMessage)
        {
            if (!Session.IsLoggedIn)
            {
                SetStatus("로그인 상태가 아니어서 저장할 수 없습니다.");
                return;
            }
            if (NetworkManager.Instance == null)
            {
                // NetworkManager는 TitleScene에서 생성돼 DontDestroyOnLoad로 유지된다.
                // 이 씬을 단독 실행하면 없으므로(개발 중 직접 Play) 저장할 수 없다.
                SetStatus("네트워크 매니저가 없습니다(TitleScene부터 실행해 주세요).");
                return;
            }

            _busy = true;
            SetStatus("저장 중...");
            var req = new ArrangePartyRequest
            {
                userId = Session.UserId,
                token = Session.Token,
                data = new ArrangePartyData { members = members },
            };
            Debug.Log($"[TeamList] 편성 저장 요청 — 멤버 {members.Count}명");
            NetworkManager.Instance.PostToGame<ArrangePartyResponse>(
                "/api/game/party/arrange", req,
                resp =>
                {
                    _busy = false;
                    var data = resp != null ? resp.data : null;
                    if (data != null && data.characters != null)
                    {
                        Session.ApplyCharacters(data.characters);
                    }
                    Refresh();
                    SetStatus(okMessage);
                },
                err =>
                {
                    _busy = false;
                    // Refresh()가 상태 문구를 초기화하므로 성공 경로와 같이 '갱신 → 문구' 순서를 지킨다.
                    Refresh();
                    SetStatus(ErrorMessages.ToKorean(err));
                });
        }

        // ---- 캐릭터 추가 ----

        /// <summary>
        /// [캐릭터 추가] → 다음 캐릭터 생성 비용을 모달로 안내하고, 골드가 부족하면 차단한다(요청·이동 없음).
        /// 충분하면 확인 시 CreateCharacterScene으로 이동한다(생성·차감은 그 씬에서 서버 권위로 처리).
        /// </summary>
        private void OnAddCharacter()
        {
            if (_busy)
            {
                return;
            }

            MasterDataManager.EnsureLoaded();
            var db = MasterDataManager.Db;
            int existing = Session.GameData != null && Session.GameData.characters != null
                ? Session.GameData.characters.Count : 0;

            // 직업당 1명이므로 모든 직업을 보유하면 더 생성할 수 없다(생성 씬에서도 '선택불가'로 막힌다).
            int classCount = db != null && db.Classes != null ? db.Classes.Count : 0;
            if (classCount > 0 && existing >= classCount)
            {
                SetStatus("더 생성할 수 있는 직업이 없습니다.");
                return;
            }

            int nextSlot = existing + 1;
            long cost = db != null ? db.CharacterCreateCostOf(nextSlot) : 0L;
            long gold = CurrentGold();

            if (cost > 0 && gold < cost)
            {
                if (ModalManager.Instance != null)
                {
                    ModalManager.Instance.ShowConfirm("캐릭터 추가",
                        $"골드가 부족합니다.\n필요 골드: {GoldFormat.Highlight(cost)}\n보유 골드: {GoldFormat.Highlight(gold)}");
                }
                SetStatus("골드가 부족해 캐릭터를 추가할 수 없습니다.");
                Debug.Log($"[TeamList] 캐릭터 추가 차단(골드 부족): 필요 {cost}, 보유 {gold}");
                return;
            }

            string message = cost > 0
                ? $"캐릭터 추가에 골드 {GoldFormat.Highlight(cost)}이 필요합니다.\n(보유 {GoldFormat.Highlight(gold)})\n생성 화면으로 이동할까요?"
                : "새 캐릭터를 생성합니다.\n생성 화면으로 이동할까요?";
            if (ModalManager.Instance != null)
            {
                ModalManager.Instance.ShowConfirmCancel("캐릭터 추가", message, GoToCreateScene);
            }
            else
            {
                GoToCreateScene();
            }
        }

        /// <summary>계정 보유 골드(재화 타입 1).</summary>
        private static long CurrentGold()
        {
            var currencies = Session.GameData != null ? Session.GameData.currencies : null;
            if (currencies != null)
            {
                foreach (var cur in currencies)
                {
                    if (cur != null && cur.currencyType == 1)
                    {
                        return cur.amount;
                    }
                }
            }
            return 0;
        }

        /// <summary>캐릭터 생성 씬으로 이동한다(생성·뒤로가기 후 이 편성 씬으로 복귀).</summary>
        private void GoToCreateScene()
        {
            Session.CreateCharacterFromGame = true;             // 생성 씬에 '뒤로가기' 노출
            Session.CreateCharacterReturnScene = "TeamListScene"; // 복귀 지점을 이 씬으로
            Debug.Log("[TeamList] 캐릭터 추가 → CreateCharacterScene 이동");
            if (SceneManager.Instance != null)
            {
                SceneManager.Instance.LoadScene("CreateCharacterScene");
            }
        }

        /// <summary>게임 화면(GameScene)으로 돌아간다. 씬 매니저가 없으면(이 씬 단독 실행) Unity 씬 로더로 직접 전환한다.</summary>
        private void OnBack()
        {
            Debug.Log("[TeamList] 뒤로 → GameScene 복귀");
            if (SceneManager.Instance != null)
            {
                SceneManager.Instance.LoadScene("GameScene");
                return;
            }
            UnityEngine.SceneManagement.SceneManager.LoadScene("GameScene");
        }

        private void SetStatus(string text)
        {
            if (_statusText != null)
            {
                _statusText.text = text ?? string.Empty;
            }
        }

        /// <summary>class_master의 직업 이름(없으면 코드 표기).</summary>
        private static string ClassNameOf(int classCode)
        {
            MasterDataManager.EnsureLoaded();
            var db = MasterDataManager.Db;
            if (db != null && db.Classes.TryGetValue(classCode, out ClassMaster cls) && cls != null
                && !string.IsNullOrEmpty(cls.name))
            {
                return cls.name;
            }
            return $"직업 {classCode}";
        }

        // ---- UI 생성 헬퍼 ----

        /// <summary>패널 배경에 프레임 아트(ui_bg 9-slice)를 적용한다(없으면 단색 유지).
        /// 9-slice라 창 비율이 바뀌어 패널이 늘어나도 나무 테두리 모양이 유지된다.</summary>
        private void ApplyPanelSprite(Image img)
        {
            if (img == null || _panelSprite == null) return;
            img.sprite = _panelSprite;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = PanelPixelsPerUnitMultiplier;
            img.color = Color.white;
        }

        /// <summary>버튼 배경에 공용 버튼 아트(pixel_rpg_button 9-slice)를 적용한다(없으면 단색 유지).</summary>
        private void ApplyButtonSprite(Image img)
        {
            if (img == null || _buttonSprite == null) return;
            img.sprite = _buttonSprite;
            img.type = Image.Type.Sliced;
            img.color = Color.white;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static Image NewImage(string name, Transform parent, Color color)
        {
            var rect = NewRect(name, parent);
            var img = rect.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        /// <summary>초상화 렌더 대상 RawImage. 렌더러가 배정되기 전에는 투명하게 둔다(흰 사각형 방지).</summary>
        private static RawImage NewRawImage(string name, Transform parent)
        {
            var rect = NewRect(name, parent);
            var img = rect.gameObject.AddComponent<RawImage>();
            img.color = new Color(1f, 1f, 1f, 0f);
            return img;
        }

        private static RectTransform NewText(string name, Transform parent, string content, int fontSize,
            FontStyle style, Color color, TextAnchor anchor)
        {
            var rect = NewRect(name, parent);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = content;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.alignment = anchor;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return rect;
        }

        /// <summary>부모 안에서 비율(0~1) 좌표로 배치한다. 카드 크기가 창 비율에 따라 달라져도 내부 배치 비율이 유지된다.
        /// <paramref name="inset"/>은 좌우 여백(픽셀)이다.</summary>
        private static void PlaceFraction(RectTransform rect, float xMin, float yMin, float xMax, float yMax, float inset)
        {
            rect.anchorMin = new Vector2(xMin, yMin);
            rect.anchorMax = new Vector2(xMax, yMax);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(inset, 0f);
            rect.offsetMax = new Vector2(-inset, 0f);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
