using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TaskbarHero.Client.Managers;
using TaskbarHero.Common.Dto;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// GameScene 상시 HUD. <b>화면 하단 가로 중앙</b>에 기능 버튼(뽑기·거래소·출석부·메일·편성·스테이지·가방·환경설정)을
    /// 한 줄로 노출하고, ESC 메뉴(타이틀로 돌아가기)를 코드로 구성한다.
    /// 아이콘 줄은 던전 배경 띠보다 아래(화면 최하단)에 놓여 배경 아트에 묻히지 않는다 —
    /// 배경 띠를 위로 띄우는 쪽은 <c>DungeonBattleBuilder</c>가 GameScene을 구울 때 처리한다.
    /// 다만 <b>뒷배경 프레임은 아이콘 줄보다 위로 더 커서 던전 띠에 얹힌다</b>(<see cref="UiBackTopExtend"/>) —
    /// 그래서 HUD 캔버스를 전투 오버레이보다 위(<see cref="UiSortingOrder.Hud"/>)에 둔다.
    /// <para>
    /// 하단 바는 <b>항상 펼쳐진 상태로 상시 노출</b>된다 — 접기/펼치기와 그 토글 버튼(우측 상단)은 두지 않는다.
    /// 아이콘을 누르면 잠깐 커졌다 돌아오는 클릭 피드백(<see cref="ButtonPunchScale"/>)만 재생된다.
    /// </para>
    /// <para>
    /// 화면 구성 외에 <b>GameScene에 머무는 동안 도는 주기 작업</b>도 이 컴포넌트가 소유한다 —
    /// 미수령 메일 레드닷 조회(<see cref="MailNotifyLoop"/>)와 접속 시각 갱신
    /// (<see cref="HeartbeatLoop"/>, 오프라인 정산 기준 시각). 씬을 벗어나면 함께 사라져 자동으로 멈춘다.
    /// </para>
    /// </summary>
    public class GameSceneHudController : MonoBehaviour
    {
        [Header("메뉴 버튼 아이콘 (에디터 빌더가 배선: Assets/Art/Icon, 메일은 Assets/Art/UI/Mail, 출석부는 Assets/Art/UI/Attendance)")]
        [SerializeField] private Sprite mailIcon;       // 메일(우편함)
        [SerializeField] private Sprite partyIcon;      // 편성
        [SerializeField] private Sprite stageIcon;      // 스테이지
        [SerializeField] private Sprite inventoryIcon;  // 가방
        [SerializeField] private Sprite attendanceIcon; // 출석부
        [Tooltip("거래소 아이콘(Assets/Art/UI/Trade/거래소.png).")]
        [SerializeField] private Sprite tradeIcon;      // 거래소
        [Tooltip("뽑기 아이콘(Assets/Art/Icon/뽑기.png).")]
        [SerializeField] private Sprite gachaIcon;      // 뽑기(가챠)
        [Tooltip("환경설정 아이콘(Assets/Art/Icon/환경설정.png).")]
        [SerializeField] private Sprite settingsIcon;   // 환경설정
        [Tooltip("하단 아이콘 줄 뒷배경 프레임(Assets/Art/UI/ui_bg_3.png, 9-slice). 없으면 배경 없이 아이콘만 표시.")]
        [SerializeField] private Sprite uiBackgroundSprite;

        [Header("적용 중인 버프 표시 (우측 상단)")]
        [Tooltip("적용 중인 버프 아이콘(Assets/Art/Icon/적용중인버프.png). 활성 버프가 있을 때만 노출된다.")]
        [SerializeField] private Sprite activeBuffIcon;
        [Tooltip("버프 상세 툴팁 배경(Assets/Art/UI/item_detail_bg.png). 없으면 단색 배경으로 표시.")]
        [SerializeField] private Sprite buffTooltipBackground;

        [Header("ESC 메뉴 리소스 (에디터 빌더가 배선)")]
        [Tooltip("ESC 메뉴 창 배경 프레임(Assets/Art/UI/ui_bg_2.png — 가방·스킬·룬 창과 같은 공용 프레임). 없으면 단색 패널.")]
        [SerializeField] private Sprite escMenuFrameSprite;
        [Tooltip("ESC 메뉴 버튼 배경(Assets/Art/UI/System/system_slot.png, 9-slice). 없으면 단색 버튼.")]
        [SerializeField] private Sprite systemSlotSprite;

        [Header("알림(레드닷)")]
        [Tooltip("미수령 보상 메일 확인을 위한 우편함 재조회 주기(초). 0 이하면 진입 시 1회만 조회한다.")]
        [SerializeField] private float mailPollIntervalSeconds = 60f;

        [Header("접속 시각 갱신(heartbeat)")]
        [Tooltip("접속 시각 갱신(/api/game/update-last-active) 주기(초). 기획서 규약은 5분(300초)이며, " +
                 "이 값이 곧 오프라인 정산 시작점의 정밀도가 된다. 0 이하면 보내지 않는다.")]
        [SerializeField] private float heartbeatIntervalSeconds = 300f;

        [Header("접속 시 자동 표시")]
        [Tooltip("접속 시 오늘자 출석 보상이 아직 남아 있으면 출석부 패널을 자동으로 연다.")]
        [SerializeField] private bool autoOpenAttendance = true;

        // 하단 버튼 줄: 바 안에서 오른쪽 끝부터 왼쪽으로 한 칸씩. 바는 전투 화면 밴드
        // (GameViewLayout.GameWidth = 1440) 안에 놓이므로 칸 수 × 간격이 그 폭을 넘지 않아야 한다 —
        // 뽑기를 더해 8칸이 되면서 종전 간격(180)으로는 1468이 되어 넘치므로 간격·버튼 폭을 함께 줄였다
        // (7 × 164 + 152 + 48 = 1348 ≤ 1440).
        private const int MenuSlotCount = 8;
        private const float MenuSlotStep = 164f;
        private const float MenuButtonWidth = 152f;
        // 아이콘(104) + 라벨(40)을 여백 없이 붙인 높이. 이 둘 사이·위아래에 빈 칸을 두지 않는다.
        private const float MenuButtonHeight = MenuIconSize + MenuLabelHeight;
        private const float MenuIconSize = 104f;
        private const float MenuLabelHeight = 40f;
        private const float MenuRowY = 30f;       // 화면 하단에서 아이콘 줄을 띄우는 높이

        // 하단 UI 뒷배경(ui_bg_3) 크기 계산용.
        // 아이콘 줄 위아래 여백은 두지 않는다(0) — 바 높이를 아이콘이 차지하는 만큼으로만 잡기 위함이다.
        private const float UiBackPadding = 0f;           // 아이콘 줄과 배경 프레임 안쪽 여백
        private const float UiBackSidePadding = 24f;      // 좌우 여백
        // 프레임을 아이콘 줄 위로 더 키우는 높이 — 프레임 상단 장식이 아이콘을 덮지 않을 만큼만 남긴다.
        // 하단 UI가 던전 위에 얹히는 것이 의도이며, 그러려면 HUD 캔버스가 전투 오버레이보다 위여야 한다
        // (UiSortingOrder.Hud).
        private const float UiBackTopExtend = 24f;
        // ui_bg_3(1024×434)의 9-slice 테두리는 상 92 / 하 53px(GameSceneHudBuilder.UiBackgroundBorder)로
        // 합 145 < 바 높이(252.26)라 원본 픽셀 크기(배율 1)로 그려도 들어간다 — 픽셀아트를 축소하지 않는다.
        private const float UiBackPixelsPerUnitMultiplier = 1f;
        // 프레임 안쪽(비어 있는 구멍)에 깔 짙은 파랑 바닥. 색은 패널 프레임(ui_bg_2) 내부색과 같은 톤이라
        // 하단 바와 인벤토리·스킬 패널의 배경이 같은 계열로 읽힌다.
        private static readonly Color UiBackFillColor = new Color32(23, 36, 50, 255);
        // 좌우는 9-slice 테두리(L 60 · R 60, 배율 1이므로 픽셀 = UI 단위)에서 Bleed만큼 물려 이음선을 없앤다.
        private static readonly Vector4 UiBackFrameBorder = new Vector4(60f, 53f, 60f, 92f); // L,B,R,T
        private const float UiBackFillBleed = 2f;
        // 위아래는 테두리 두께로 계산하지 않고 플레이 모드에서 직접 늘려 확정한 값을 쓴다 —
        // 바닥이 비어 보였으므로 아래는 바 밑단까지(0) 내리고 위는 프레임 상단 장식 아래에 맞췄다.
        private const float UiBackFillBottomInset = 0f;
        private const float UiBackFillTopInset = 68.86f;
        // 프레임을 바 밑단보다 더 내려 그리는 높이. 아래 테두리가 화면 최하단(y 0)에 닿도록
        // 바 밑단(= MenuRowY - UiBackPadding)만큼 내린다.
        private const float UiBackFrameBottomOverhang = MenuRowY - UiBackPadding;

        // 바 기하. 바 = 배경 프레임 + 아이콘 줄이며, 이 폭 그대로 화면 하단 중앙에 놓인다.
        private const float MenuAreaWidth = (MenuSlotCount - 1) * MenuSlotStep + MenuButtonWidth + UiBackSidePadding * 2f;
        private const float MenuBarBottom = MenuRowY - UiBackPadding;
        // 바 위쪽 끝 = 아이콘 줄 위 여백 + 프레임 추가 높이. 아이콘 줄은 바 아래를 기준으로 배치되므로
        // 바가 위로 커져도 아이콘 위치는 그대로다.
        private const float MenuBarTop = MenuRowY + MenuButtonHeight + UiBackPadding + UiBackTopExtend;
        private const float MenuBarHeight = MenuBarTop - MenuBarBottom;

        // 우측 상단 버프 아이콘: 화면 모서리에서 살짝 띄운 위치·크기(우상단 앵커 기준).
        private const float BuffIconSize = 96f;
        private const float BuffIconX = -28f;
        private const float BuffIconY = -28f;
        private static readonly Vector2 BuffIconPos = new Vector2(BuffIconX, BuffIconY);

        // ESC 메뉴 버튼: system_slot 원본(2048×731) 테두리 상하 128px → 배율 4로 32씩(합 64 < 100).
        private const float EscButtonWidth = 440f;
        private const float EscButtonHeight = 100f;
        private const float SystemSlotPixelsPerUnitMultiplier = 4f;

        // ESC 메뉴 창 크기. 배경은 공용 프레임(ui_bg_2)이며 Simple로 늘려 그리므로 장식이 찌그러지지 않게
        // 아트 비율(<see cref="PanelFrame.Aspect"/> ≒ 0.715)에 가깝게 잡는다(680/940 ≒ 0.723).
        private const float EscPanelWidth = 680f;
        private const float EscPanelHeight = 940f;
        // 내용 영역(프레임 테두리 안쪽 빈 칸) 크기 — 창 크기에서 파생되는 상수식이다(479.6 × 681.0).
        private const float EscContentWidth = EscPanelWidth * (1f - PanelFrame.InsetLeft - PanelFrame.InsetRight)
            - PanelFrame.Pad * 2f;
        private const float EscContentHeight = EscPanelHeight * (1f - PanelFrame.InsetTop - PanelFrame.InsetBottom)
            - PanelFrame.Pad * 2f;
        // 내용 영역 <b>중앙 기준</b> y 좌표(위로 +). 제목 + 버튼 3개를 세로 중앙에 고르게 둔다.
        private const float EscTitleY = 225f;
        private const float EscButton1Y = 75f;
        private const float EscButton2Y = -65f;
        private const float EscButton3Y = -205f;

        private GameObject _escMenuRoot; // ESC로 토글하는 메뉴(타이틀 복귀)

        // 하단 메뉴바(상시 노출).
        private RectTransform _menuArea;         // 배경 프레임 + 아이콘 줄이 들어가는 영역
        private RectTransform[] _menuButtons;    // 인덱스 = 칸 번호(0 = 맨 오른쪽)

        // 보상 획득 연출이 날아갈 칸 번호(위 CreateMenuButton 호출 순서와 같아야 한다).
        private const int InventorySlot = 1;     // 가방 — 모든 획득 보상(골드·경험치·전리품)이 들어가는 곳

        private void Awake()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildHud(font);
            BuildEscMenu(font);
        }

        /// <summary>ESC 키로 타이틀 복귀 메뉴를 토글한다(새 Input System).</summary>
        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                ToggleEscMenu();
            }
        }

        /// <summary>GameScene 진입 시 대기 중인 오프라인 보상 정산 결과가 있으면 팝업으로 표시하고,
        /// 메일 레드닷 판정을 위한 우편함 조회 루프·접속 시각 갱신(heartbeat) 루프와
        /// 출석부 자동 표시 판정을 시작한다.</summary>
        private void Start()
        {
            if (Session.PendingOfflineReward != null && UIManager.Instance != null)
            {
                UIManager.Instance.ShowOfflineReward();
            }
            // 접속 직후 1회만 활성 버프를 재동기화한다(잔여 시간 기준점 serverTime 확보 — 이후 폴링 없음).
            BuffManager.Refresh();
            StartCoroutine(MailNotifyLoop());
            StartCoroutine(HeartbeatLoop());
            if (autoOpenAttendance)
            {
                StartCoroutine(AutoOpenAttendanceRoutine());
            }
        }

        /// <summary>접속 직후 오늘자 출석 보상이 아직 남아 있으면 출석부를 자동으로 연다.
        /// <see cref="UIManager.Show"/>는 다른 패널을 모두 숨기므로, 먼저 뜬 오프라인 보상 팝업 등을
        /// 덮지 않도록 열려 있는 패널이 모두 닫힌 뒤에 조회하고, 조회 사이에 사용자가 다른 패널을 열었으면
        /// 표시를 포기한다. 조회 실패는 경고만 남기고 자동 표시를 생략한다(수동으로 열 수 있으므로).</summary>
        private IEnumerator AutoOpenAttendanceRoutine()
        {
            yield return new WaitUntil(() => UIManager.Instance != null && !UIManager.Instance.IsAnyPanelVisible());

            if (NetworkManager.Instance == null || !Session.IsLoggedIn)
            {
                yield break;
            }

            bool? claimable = null;
            var req = new AuthRequest { userId = Session.UserId, token = Session.Token };
            NetworkManager.Instance.PostToGame<AttendanceStatusResponse>("/api/game/attendance/status", req,
                // canClaim = 오늘 미수령 && 남은 일차 있음(30일차까지 다 받은 달에는 자동으로 열지 않는다).
                resp => claimable = resp != null && resp.data != null && resp.data.canClaim,
                error =>
                {
                    Debug.LogWarning($"[HUD] 출석 현황 조회 실패 — 출석부 자동 표시 생략: {error}");
                    claimable = false;
                });

            yield return new WaitUntil(() => claimable.HasValue);

            if (claimable.Value && !UIManager.Instance.IsAnyPanelVisible())
            {
                Debug.Log("[HUD] 오늘자 출석 보상 미수령 — 출석부 자동 표시");
                UIManager.Instance.ShowAttendance();
            }
        }

        /// <summary>미수령 보상 메일 레드닷용 우편함 스냅샷을 진입 직후 1회 조회하고, 주기가 설정돼 있으면
        /// 그 간격으로 재조회한다(플레이 중 도착한 메일도 알림에 반영). 전투 슬로우모션 등 timeScale 변화의
        /// 영향을 받지 않도록 실시간 대기를 사용한다.</summary>
        private IEnumerator MailNotifyLoop()
        {
            MailNotifier.Refresh();
            if (mailPollIntervalSeconds <= 0f)
            {
                yield break;
            }
            var wait = new WaitForSecondsRealtime(mailPollIntervalSeconds);
            while (true)
            {
                yield return wait;
                MailNotifier.Refresh();
            }
        }

        /// <summary>
        /// 접속 중임을 서버에 알리는 heartbeat 루프(<c>POST /api/game/update-last-active</c>).
        /// 서버는 이 요청마다 <c>game_player.last_active_at</c>을 현재 시각으로 갱신하며, 접속이 끊기면
        /// <b>마지막 heartbeat 시각이 곧 오프라인 정산의 시작점</b>이 된다([오프라인 보상 기획서] 5분 주기 규약).
        /// <para>
        /// 첫 요청은 <b>간격만큼 기다린 뒤</b> 보낸다 — GameScene 진입 직전에 오프라인 정산
        /// (<c>offline/claim</c>)이 기준 시각을 이미 현재로 리셋했으므로 진입 즉시 보낼 이유가 없고,
        /// 무엇보다 정산보다 heartbeat가 먼저 나가면 경과가 소실돼 보상이 0이 된다(같은 기획서 6.2·6.3).
        /// </para>
        /// 전투 슬로우모션 등 timeScale 변화에 영향받지 않도록 실시간 대기를 쓰고, 실패는 경고 로그만 남긴 뒤
        /// 다음 주기에 다시 시도한다(보조 갱신이라 사용자에게 오류를 노출하지 않는다).
        /// GameScene을 벗어나면(타이틀 복귀 등) 이 오브젝트와 함께 코루틴도 사라져 자동으로 멈춘다.
        /// </summary>
        private IEnumerator HeartbeatLoop()
        {
            if (heartbeatIntervalSeconds <= 0f)
            {
                yield break; // 0 이하면 heartbeat 비활성(개발 중 확인용)
            }
            var wait = new WaitForSecondsRealtime(heartbeatIntervalSeconds);
            while (true)
            {
                yield return wait;
                SendHeartbeat();
            }
        }

        /// <summary>heartbeat 요청 1건을 보낸다(비로그인·네트워크 매니저 부재면 건너뛴다).</summary>
        private static void SendHeartbeat()
        {
            if (NetworkManager.Instance == null || !Session.IsLoggedIn)
            {
                return;
            }
            var req = new AuthRequest { userId = Session.UserId, token = Session.Token };
            NetworkManager.Instance.PostToGame<UpdateLastActiveResponse>("/api/game/update-last-active", req,
                resp =>
                {
                    long at = resp != null && resp.data != null ? resp.data.lastActiveAt : 0;
                    Debug.Log($"[HUD] heartbeat 갱신 완료 lastActiveAt={at}");
                },
                error => Debug.LogWarning($"[HUD] heartbeat 실패(다음 주기에 재시도): {error}"));
        }

        /// <summary>HUD 캔버스와 토글 버튼(스테이지·가방)을 생성·배선한다.</summary>
        private void BuildHud(Font font)
        {
            var canvasGo = new GameObject("HudCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = UiSortingOrder.Hud; // 전투 오버레이(≤10)보다 위, 패널(100)보다 아래
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            // HUD는 캔버스 전체가 아니라 '전투 화면 밴드' 안에만 둔다. GameScene 창은 좌우에 패널
            // 여백이 붙어 캔버스가 전투 화면보다 훨씬 넓으므로(GameViewLayout), 캔버스 직속으로 두면
            // 우측 정렬 요소(메뉴 손잡이·버프 아이콘)가 빈 여백까지 밀려난다.
            var gameArea = new GameObject("GameArea", typeof(RectTransform));
            gameArea.transform.SetParent(canvasGo.transform, false);
            GameAreaRect.Attach((RectTransform)gameArea.transform);

            // 하단 바: 화면 하단 '가로 중앙'에 놓는다(예전에는 우하단 정렬이었다).
            // 구조 — MenuBar(중앙 정렬, 피벗 오른쪽) └ MenuArea(배경 프레임 + 아이콘 줄)
            var bar = CreateBottomCenterBar(gameArea.transform);
            _menuArea = CreateMenuArea(bar);
            BuildMenuBackground(_menuArea);      // 아이콘보다 먼저 만들어 뒤에 깔리게 한다(자식 순서 = 그리기 순서)

            // 한 줄로: 오른쪽부터 [환경설정] [가방] [스테이지] [편성] [메일] [출석부] [거래소] [뽑기].
            _menuButtons = new RectTransform[MenuSlotCount];
            CreateMenuButton(font, "SettingsButton", "환경설정", settingsIcon, 0, OnSettingsButton);
            var inventoryBtn = CreateMenuButton(font, "InventoryButton", "가방", inventoryIcon, 1, OnInventoryButton);
            CreateMenuButton(font, "StageButton", "스테이지", stageIcon, 2, OnStageButton);
            CreateMenuButton(font, "PartyButton", "편성", partyIcon, 3, OnPartyButton);
            var mailBtn = CreateMenuButton(font, "MailButton", "메일", mailIcon, 4, OnMailButton);
            CreateMenuButton(font, "AttendanceButton", "출석부", attendanceIcon, 5, OnAttendanceButton);
            CreateMenuButton(font, "TradeButton", "거래소", tradeIcon, 6, OnTradeButton);
            CreateMenuButton(font, "GachaButton", "뽑기", gachaIcon, 7, OnGachaButton);

            // 메일 버튼 우측 상단 레드닷: 아직 수령하지 않은 보상 첨부가 남은 메일이 있으면 표시(만료 전 수령 유도).
            RedDot.AttachTopRight((RectTransform)mailBtn.transform).Bind(RedDotConditions.HasUnclaimedMailReward);

            // 가방 버튼 우측 상단 레드닷: 잔여 스킬 포인트가 있으면 표시(스킬 레벨업은 가방 안에서 진입).
            RedDot.AttachTopRight((RectTransform)inventoryBtn.transform).Bind(RedDotConditions.HasUnspentSkillPoints);

            BuildBuffIndicator(gameArea.transform, font);

            // 클리어 보상이 날아올 목표를 전투 연출 쪽에 등록한다(어셈블리 참조가 UI → Battle 한 방향이라
            // 전투 쪽에서 HUD를 직접 찾을 수 없다 — 여기서 함수를 넘겨 준다).
            Battle.RewardFlyFx.TargetProvider = RewardFlyTarget;
        }

        /// <summary>화면 우측 상단의 '적용 중인 버프' 아이콘을 만든다(활성 버프가 있을 때만 스스로 노출한다).</summary>
        private void BuildBuffIndicator(Transform parent, Font font)
        {
            var go = new GameObject("BuffIndicator", typeof(RectTransform), typeof(BuffIndicator));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            go.GetComponent<BuffIndicator>()
                .Build(font, activeBuffIcon, buffTooltipBackground, BuffIconPos, BuffIconSize);
        }

        /// <summary>
        /// 하단 바를 화면 <b>하단 가로 중앙</b>에 만든다.
        /// 피벗을 오른쪽(1,0)에 두고 폭의 절반만큼 오른쪽으로 밀어, 바가 중앙을 기준으로 좌우 대칭이 되게 한다
        /// (동시에 '오른쪽 끝'이 펼침/접힘의 기준점이 된다).
        /// </summary>
        private static RectTransform CreateBottomCenterBar(Transform parent)
        {
            var go = new GameObject("MenuBar", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f); // 하단 중앙
            rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(MenuAreaWidth * 0.5f, MenuBarBottom);
            rt.sizeDelta = new Vector2(MenuAreaWidth, MenuBarHeight);
            return rt;
        }

        /// <summary>
        /// 배경 프레임 + 아이콘 줄이 들어가는 영역을 만든다(바를 꽉 채운다).
        /// <para>접기/펼치기가 없어져 <see cref="RectMask2D"/>도 두지 않는다 — 클리핑할 것이 없고,
        /// 두면 오히려 바 밑단보다 더 내려 그리는 프레임 아래 테두리를 잘라내게 된다.</para>
        /// </summary>
        private static RectTransform CreateMenuArea(RectTransform bar)
        {
            var go = new GameObject("MenuArea", typeof(RectTransform));
            go.transform.SetParent(bar, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f); // 바의 오른쪽 아래 기준
            rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(MenuAreaWidth, MenuBarHeight);
            return rt;
        }

        /// <summary>
        /// 아이콘 줄 뒤에 깔리는 배경 프레임(ui_bg_3)과 그 안쪽 짙은 파랑 바닥을 만든다(영역을 꽉 채운다).
        /// 프레임 가운데는 뚫려 있으므로 <b>바닥(<see cref="UiBackFillColor"/>)을 프레임보다 먼저 깔아</b>
        /// 던전 화면이 그대로 비치지 않게 한다. 스프라이트가 없으면 배경을 만들지 않는다(아이콘만 표시).
        /// </summary>
        private void BuildMenuBackground(RectTransform area)
        {
            if (uiBackgroundSprite == null)
            {
                return;
            }

            BuildMenuBackgroundFill(area);

            var go = new GameObject("MenuBackground", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(area, false);
            var img = go.GetComponent<Image>();
            img.sprite = uiBackgroundSprite;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = UiBackPixelsPerUnitMultiplier;
            img.color = Color.white;
            img.raycastTarget = false; // 배경은 클릭을 먹지 않는다(창 드래그·아이콘 클릭 방해 금지)

            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; // 영역 전체를 채운다
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(0f, -UiBackFrameBottomOverhang); // 아래 테두리는 바 밑단보다 더 내려 그린다
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// 프레임 안쪽 빈 구멍을 채우는 짙은 파랑 바닥을 만든다(프레임보다 먼저 생성해 <b>아래</b>에 깔린다).
        /// 영역 전체에 늘어붙되 <b>좌우</b>는 9-slice 테두리(<see cref="UiBackFrameBorder"/>)에서
        /// <see cref="UiBackFillBleed"/>만큼 물려 들여 색이 프레임 밖으로 삐져나오지 않게 하고,
        /// <b>위아래</b>는 플레이 모드에서 확정한 <see cref="UiBackFillBottomInset"/>·
        /// <see cref="UiBackFillTopInset"/>을 쓴다(아래는 바 밑단까지 꽉 채운다).
        /// </summary>
        private static void BuildMenuBackgroundFill(RectTransform area)
        {
            var go = new GameObject("MenuBackgroundFill", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(area, false);
            var img = go.GetComponent<Image>();
            img.sprite = null;
            img.color = UiBackFillColor;
            img.raycastTarget = false; // 배경은 클릭을 먹지 않는다(창 드래그·아이콘 클릭 방해 금지)

            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(UiBackFrameBorder.x - UiBackFillBleed, UiBackFillBottomInset);
            rt.offsetMax = new Vector2(-(UiBackFrameBorder.z - UiBackFillBleed), -UiBackFillTopInset);
        }

        /// <summary>보상 연출 목표 등록을 해제한다.</summary>
        private void OnDestroy()
        {
            // 내가 등록한 것일 때만 해제한다(씬 전환 중 새 HUD가 이미 등록했을 수 있다).
            if (Battle.RewardFlyFx.TargetProvider == RewardFlyTarget)
            {
                Battle.RewardFlyFx.TargetProvider = null;
            }
        }

        /// <summary>기능 버튼 하나를 바 안 <paramref name="slot"/>번째 칸(0 = 맨 오른쪽)에 만든다.</summary>
        private GameObject CreateMenuButton(Font font, string name, string label, Sprite icon,
            int slot, UnityEngine.Events.UnityAction onClick)
        {
            // 영역의 오른쪽 끝에서 좌우 여백만큼 들어온 지점이 첫 칸(0)의 오른쪽 끝이다.
            var pos = new Vector2(-UiBackSidePadding - slot * MenuSlotStep, UiBackPadding);
            var go = CreateButton(_menuArea, font, name, label, icon, pos, onClick);

            // 클릭 피드백(punch)이 아이콘 '제자리'에서 커지도록 피벗을 칸 중앙으로 옮긴다.
            // CreateButton의 피벗(1,0 = 우측 하단)을 그대로 두면 확대가 우측 하단으로 쏠려 아이콘이 바닥에 몰려 보인다.
            // 피벗 이동에 맞춰 위치를 칸 중심으로 바꾸므로 실제 사각형(rect)은 그대로다.
            var rt = (RectTransform)go.transform;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(pos.x - MenuButtonWidth * 0.5f, pos.y + MenuButtonHeight * 0.5f);

            // 클릭 피드백(커졌다 돌아오는 punch). 피벗을 방금 칸 중앙으로 옮겼으므로 버튼 루트에 붙여도
            // 아이콘·글자가 함께 제자리에서 커진다.
            var punch = go.AddComponent<ButtonPunchScale>();
            go.GetComponent<Button>().onClick.AddListener(() => punch.Play()); // 창 열림과 동시에 시작

            _menuButtons[slot] = rt;
            return go;
        }

        /// <summary>우하단 앵커 HUD 버튼 하나를 생성·배선하고 생성한 버튼 오브젝트를 반환한다.
        /// 아이콘이 있으면 아이콘을 상단에, 작아진 텍스트를 그 아래에 배치한다(아이콘 없으면 텍스트만 중앙).</summary>
        private static GameObject CreateButton(Transform parent, Font font, string name, string label,
            Sprite icon, Vector2 anchoredPos, UnityEngine.Events.UnityAction onClick)
        {
            var btnGo = new GameObject(name, typeof(RectTransform), typeof(Image));
            btnGo.transform.SetParent(parent, false);
            var img = btnGo.GetComponent<Image>();
            // 배경 투명(A=0). raycastTarget는 유지되므로 투명해도 클릭은 정상 동작한다.
            img.color = new Color(0.25f, 0.28f, 0.4f, 0f);
            var rt = (RectTransform)btnGo.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f); // 우하단
            rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = new Vector2(MenuButtonWidth, MenuButtonHeight);

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(btnGo.transform, false);
            var t = labelGo.GetComponent<Text>();
            t.font = font;
            t.text = label;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = Color.white;
            t.raycastTarget = false;
            var lrt = (RectTransform)labelGo.transform;

            if (icon != null)
            {
                // 아이콘(상단)
                var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                iconGo.transform.SetParent(btnGo.transform, false);
                var iconImg = iconGo.GetComponent<Image>();
                iconImg.sprite = icon;
                iconImg.preserveAspect = true;
                iconImg.raycastTarget = false;
                var irt = iconImg.rectTransform;
                irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 1f);
                irt.pivot = new Vector2(0.5f, 1f);
                irt.anchoredPosition = Vector2.zero;            // 버튼 위쪽에 딱 붙인다(상단 여백 없음)
                irt.sizeDelta = new Vector2(MenuIconSize, MenuIconSize);

                // 텍스트(아이콘 바로 아래) — 버튼 아래쪽에 딱 붙인다(하단 여백 없음)
                t.fontSize = 28;
                t.fontStyle = FontStyle.Bold;
                lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0f);
                lrt.pivot = new Vector2(0.5f, 0f);
                lrt.anchoredPosition = Vector2.zero;
                lrt.sizeDelta = new Vector2(MenuButtonWidth - 4f, MenuLabelHeight);
            }
            else
            {
                // 폴백: 아이콘이 없으면 기존처럼 텍스트만 버튼 전체 중앙에 표시.
                t.fontSize = 34;
                lrt.anchorMin = Vector2.zero;
                lrt.anchorMax = Vector2.one;
                lrt.offsetMin = Vector2.zero;
                lrt.offsetMax = Vector2.zero;
            }

            btnGo.AddComponent<Button>().onClick.AddListener(onClick);
            return btnGo;
        }

        /// <summary>출석부 패널 토글(UIManager 위임).</summary>
        private void OnAttendanceButton()
        {
            if (UIManager.Instance != null)
            {
                UIManager.Instance.ToggleAttendance();
            }
            else
            {
                Debug.LogWarning("[HUD] UIManager 인스턴스를 찾을 수 없습니다.");
            }
        }

        /// <summary>우편함(메일) 패널 토글(UIManager 위임).</summary>
        private void OnMailButton()
        {
            if (UIManager.Instance != null)
            {
                UIManager.Instance.ToggleMail();
            }
            else
            {
                Debug.LogWarning("[HUD] UIManager 인스턴스를 찾을 수 없습니다.");
            }
        }

        /// <summary>환경설정 패널 토글(UIManager 위임).</summary>
        private void OnSettingsButton()
        {
            if (UIManager.Instance != null)
            {
                UIManager.Instance.ToggleSettings();
            }
            else
            {
                Debug.LogWarning("[HUD] UIManager 인스턴스를 찾을 수 없습니다.");
            }
        }

        /// <summary>거래소 패널 토글(UIManager 위임).</summary>
        private void OnTradeButton()
        {
            if (UIManager.Instance != null)
            {
                UIManager.Instance.ToggleTrade();
            }
            else
            {
                Debug.LogWarning("[HUD] UIManager 인스턴스를 찾을 수 없습니다.");
            }
        }

        /// <summary>뽑기(가챠) 패널 토글(UIManager 위임).</summary>
        private void OnGachaButton()
        {
            if (UIManager.Instance != null)
            {
                UIManager.Instance.ToggleGacha();
            }
            else
            {
                Debug.LogWarning("[HUD] UIManager 인스턴스를 찾을 수 없습니다.");
            }
        }

        /// <summary>
        /// 클리어 보상 획득 연출(<see cref="Battle.RewardFlyFx"/>)이 날아갈 HUD 목표를 돌려준다 —
        /// 골드·경험치·전리품 <b>모두 가방</b>으로 향한다(획득물이 한곳으로 모이는 것으로 읽히게 통일).
        /// <para>하단 바가 상시 노출이므로 <b>진짜 가방 버튼</b>을 그대로 목표로 쓴다(접힘 대비 임시 아이콘 불필요).</para>
        /// </summary>
        public RectTransform RewardFlyTarget()
        {
            if (_menuButtons != null && InventorySlot < _menuButtons.Length)
            {
                return _menuButtons[InventorySlot];
            }
            return null;
        }

        /// <summary>인벤토리 패널 토글(UIManager 위임).</summary>
        private void OnInventoryButton()
        {
            if (UIManager.Instance != null)
            {
                UIManager.Instance.ToggleInventory();
            }
            else
            {
                Debug.LogWarning("[HUD] UIManager 인스턴스를 찾을 수 없습니다.");
            }
        }

        /// <summary>파티 편성 씬(TeamListScene)으로 이동한다. 편성은 팝업이 아니라 전용 씬으로 다룬다.
        /// 열려 있던 패널은 먼저 닫는다(패널은 DontDestroyOnLoad라 씬 전환 후에도 남는다).</summary>
        private void OnPartyButton()
        {
            if (UIManager.Instance != null)
            {
                UIManager.Instance.HideAll();
            }
            if (SceneManager.Instance != null)
            {
                Debug.Log("[HUD] 편성 → TeamListScene 이동");
                SceneManager.Instance.LoadScene("TeamListScene");
            }
            else
            {
                Debug.LogWarning("[HUD] SceneManager 인스턴스를 찾을 수 없습니다.");
            }
        }

        /// <summary>스테이지 선택 패널 토글(UIManager 위임).</summary>
        private void OnStageButton()
        {
            if (UIManager.Instance != null)
            {
                UIManager.Instance.ToggleStage();
            }
            else
            {
                Debug.LogWarning("[HUD] UIManager 인스턴스를 찾을 수 없습니다.");
            }
        }

        // ── ESC 메뉴(타이틀로 돌아가기) ──

        /// <summary>ESC로 토글하는 메뉴 오버레이(딤 + '타이틀로 돌아가기' / '계속하기' / '게임종료')를 최상단 캔버스로 구성한다(처음엔 숨김).</summary>
        private void BuildEscMenu(Font font)
        {
            var canvasGo = new GameObject("EscMenuCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200; // 패널(100)·오프라인 팝업(110)보다 위
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            _escMenuRoot = canvasGo;

            // 딤(클릭 시 닫힘 = 계속하기).
            var dim = new GameObject("Dim", typeof(RectTransform), typeof(Image));
            dim.transform.SetParent(canvasGo.transform, false);
            var dimRt = (RectTransform)dim.transform;
            dimRt.anchorMin = Vector2.zero; dimRt.anchorMax = Vector2.one;
            dimRt.offsetMin = Vector2.zero; dimRt.offsetMax = Vector2.zero;
            dim.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f); // 배경을 어둡게 하지 않는다 — 밖 클릭 닫기용 투명 차단막
            var dimBtn = dim.AddComponent<Button>();
            dimBtn.transition = Selectable.Transition.None;
            dimBtn.onClick.AddListener(HideEscMenu);

            // 메뉴 패널(중앙).
            var panel = new GameObject("EscPanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(canvasGo.transform, false);
            var prt = (RectTransform)panel.transform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(EscPanelWidth, EscPanelHeight);
            prt.anchoredPosition = Vector2.zero;
            var panelImg = panel.GetComponent<Image>();
            panelImg.color = new Color(0.10f, 0.12f, 0.18f, 0.98f); // 아트 미배선 시 단색 폴백
            if (escMenuFrameSprite != null)
            {
                // 9-slice 테두리가 없는 아트라 Simple로 늘려 그린다(가방·스킬·룬 창과 동일).
                panelImg.sprite = escMenuFrameSprite;
                panelImg.type = Image.Type.Simple;
                panelImg.color = Color.white;
            }

            // 내용물은 모두 프레임 테두리 안쪽 빈 칸에만 놓는다(테두리·상단 장식판을 침범하지 않도록).
            var content = PanelFrame.CreateContentArea(prt);

            var title = MakeMenuText(font, content, "메뉴", 48, new Vector2(0f, EscTitleY), EscContentWidth);
            title.fontStyle = FontStyle.Bold;
            title.color = new Color(1f, 0.92f, 0.72f); // 나무 배경 위 금색 톤

            MakeMenuButton(font, content, "타이틀로 돌아가기", new Vector2(0f, EscButton1Y), OnReturnToTitle);
            MakeMenuButton(font, content, "계속하기", new Vector2(0f, EscButton2Y), HideEscMenu);
            MakeMenuButton(font, content, "게임종료", new Vector2(0f, EscButton3Y), OnQuitGame);

            canvasGo.SetActive(false); // 처음엔 숨김
        }

        /// <summary>ESC 메뉴 표시/숨김 토글.</summary>
        private void ToggleEscMenu()
        {
            if (_escMenuRoot != null)
            {
                bool show = !_escMenuRoot.activeSelf;
                _escMenuRoot.SetActive(show);
                // 메뉴/패널 표시 상태를 창 제어기에 알린다(GameScene 창은 항상 확장이라 크기는 안 변함).
                TaskbarWindow.Instance?.SetExpanded(show || AnyUiPanelVisible());
            }
        }

        /// <summary>ESC 메뉴를 숨긴다(계속하기).</summary>
        private void HideEscMenu()
        {
            if (_escMenuRoot != null)
            {
                _escMenuRoot.SetActive(false);
                TaskbarWindow.Instance?.SetExpanded(AnyUiPanelVisible());
            }
        }

        /// <summary>UIManager 패널이 하나라도 표시 중인지(창 제어기에 알릴 패널 상태 판정용).</summary>
        private static bool AnyUiPanelVisible()
        {
            return UIManager.Instance != null && UIManager.Instance.IsAnyPanelVisible();
        }

        /// <summary>타이틀 화면으로 돌아간다(게임 세션 종료 후 TitleScene 로드).</summary>
        private void OnReturnToTitle()
        {
            HideEscMenu();
            Time.timeScale = 1f;                 // 전투 슬로우모션 등 배율 복원
            UIManager.Instance?.HideAll();        // 열려 있던 패널 정리
            Session.Clear();                      // 로그아웃(타이틀/로그인 새로 시작)
            // 저장된 자동 로그인 세션도 함께 버린다 — 남겨 두면 타이틀에서 곧바로 같은 계정으로
            // 자동 로그인돼 다른 계정으로 바꿀 방법이 없어진다(자동 로그인은 '앱 재실행' 편의 기능).
            SavedSession.Clear();
            MailNotifier.Clear();                 // 우편함 알림 캐시 폐기(다음 계정에 이전 알림이 새지 않도록)
            Debug.Log("[HUD] ESC 메뉴 → 타이틀로 돌아가기");
            if (SceneManager.Instance != null)
            {
                SceneManager.Instance.LoadScene("TitleScene");
            }
            else
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene("TitleScene");
            }
        }

        /// <summary>게임을 종료한다(에디터에서는 플레이 정지).</summary>
        private static void OnQuitGame()
        {
            Debug.Log("[HUD] ESC 메뉴 → 게임종료");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>ESC 메뉴용 중앙 정렬 텍스트를 만든다.</summary>
        private static Text MakeMenuText(Font font, Transform parent, string content, int size, Vector2 pos, float width)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = font;
            t.text = content;
            t.fontSize = size;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = Color.white;
            t.raycastTarget = false;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(width, 64f);
            return t;
        }

        /// <summary>ESC 메뉴용 중앙 버튼(라벨 텍스트를 버튼 전체에 채움)을 만든다.
        /// 배경은 system_slot 9-slice를 쓰고, 원본 테두리(상하 128px)가 버튼 높이(100)를 넘으므로
        /// <see cref="SystemSlotPixelsPerUnitMultiplier"/>로 줄여 그린다.</summary>
        private void MakeMenuButton(Font font, Transform parent, string label, Vector2 pos,
            UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject($"{label}Button", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(EscButtonWidth, EscButtonHeight);
            var img = go.GetComponent<Image>();
            img.color = new Color(0.25f, 0.28f, 0.4f, 1f); // 아트 미배선 시 단색 폴백
            if (systemSlotSprite != null)
            {
                img.sprite = systemSlotSprite;
                img.type = Image.Type.Sliced;
                img.pixelsPerUnitMultiplier = SystemSlotPixelsPerUnitMultiplier;
                img.color = Color.white;
            }

            var t = MakeMenuText(font, go.transform, label, 36, Vector2.zero, 420f);
            t.color = new Color(1f, 0.94f, 0.80f); // 짙은 남색 슬롯 위 밝은 미색
            var trt = (RectTransform)t.transform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;

            go.AddComponent<Button>().onClick.AddListener(onClick);
        }
    }
}
