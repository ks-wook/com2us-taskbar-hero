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
    /// 버튼 줄은 던전 배경 띠보다 아래(화면 최하단)에 놓여 배경 아트에 묻히지 않는다 —
    /// 배경 띠를 위로 띄우는 쪽은 <c>DungeonBattleBuilder</c>가 GameScene을 구울 때 처리한다.
    /// <para>
    /// 하단 바는 <b>토글 버튼으로 열고 닫는다</b>(<b>기본 접힘</b> — 손잡이만 보이고 눌러야 펼쳐진다).
    /// 열면 접히는 영역이 오른쪽에서 왼쪽으로 펼쳐지고
    /// 이어서 아이콘이 왼쪽부터 오른쪽으로 작아진 상태에서 원래 크기까지 커지며, 닫을 때는 이를 역재생한다
    /// (아이콘이 오른쪽부터 왼쪽으로 작아진 뒤 바가 말려 접힘 — <see cref="AnimateMenuBar"/>).
    /// 토글 버튼은 하단 바에 붙지 않고 <b>우측 상단 버프 아이콘 바로 아래</b>에 있어 접혀도 남는다
    /// (아이콘은 <c>Assets/Art/Icon/메뉴.png</c>, 배경 프레임 없음). 누르면 아이콘이 잠깐 커졌다 원래 크기로
    /// 돌아오는 클릭 피드백(<see cref="ButtonPunchScale"/>)이 바 연출과 함께 재생된다.
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
        [Tooltip("하단 아이콘 줄 뒷배경 프레임(Assets/Art/UI/ui_bg.png, 9-slice). 없으면 배경 없이 아이콘만 표시.")]
        [SerializeField] private Sprite uiBackgroundSprite;
        [Tooltip("하단 메뉴바 토글 버튼 아이콘(Assets/Art/Icon/메뉴.png). " +
                 "Sprite가 아니라 Texture2D인 이유 — 이 파일은 Multiple로 임포트돼 햄버거 3줄이 서브 스프라이트로 " +
                 "쪼개져 있어(메뉴_0/1/2) 스프라이트 하나를 쓰면 줄 한 개만 나온다. 임포트 설정은 바꾸지 않고(공용 아트 규칙) " +
                 "런타임에 텍스처 전체로 스프라이트를 만들어 쓴다. 없으면 화살표 텍스트로 대체한다.")]
        [SerializeField] private Texture2D menuToggleIconTexture;

        [Header("적용 중인 버프 표시 (우측 상단)")]
        [Tooltip("적용 중인 버프 아이콘(Assets/Art/Icon/적용중인버프.png). 활성 버프가 있을 때만 노출된다.")]
        [SerializeField] private Sprite activeBuffIcon;
        [Tooltip("버프 상세 툴팁 배경(Assets/Art/UI/item_detail_bg.png). 없으면 단색 배경으로 표시.")]
        [SerializeField] private Sprite buffTooltipBackground;

        [Header("ESC 메뉴 리소스 (Assets/Art/UI/System — 에디터 빌더가 배선)")]
        [Tooltip("ESC 메뉴 패널 배경(system_bg.png, 9-slice). 없으면 단색 패널.")]
        [SerializeField] private Sprite systemBackgroundSprite;
        [Tooltip("ESC 메뉴 버튼 배경(system_slot.png, 9-slice). 없으면 단색 버튼.")]
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

        // 하단 버튼 줄: 접히는 영역 안에서 오른쪽 끝부터 왼쪽으로 한 칸씩. 바는 전투 화면 밴드
        // (GameViewLayout.GameWidth = 1440) 안에 놓이므로 칸 수 × 간격이 그 폭을 넘지 않아야 한다 —
        // 뽑기를 더해 8칸이 되면서 종전 간격(180)으로는 1468이 되어 넘치므로 간격·버튼 폭을 함께 줄였다
        // (7 × 164 + 152 + 48 = 1348 ≤ 1440).
        private const int MenuSlotCount = 8;
        private const float MenuSlotStep = 164f;
        private const float MenuButtonWidth = 152f;
        private const float MenuButtonHeight = 150f;
        private const float MenuRowY = 28f;       // 화면 하단에서 띄우는 높이(던전 배경 띠 아래)

        // 하단 UI 뒷배경(ui_bg) 크기 계산용.
        private const float UiBackPadding = 18f;          // 아이콘 줄과 배경 프레임 안쪽 여백
        private const float UiBackSidePadding = 24f;      // 좌우 여백
        private const float DungeonBandBottomY = 216f;    // 던전 배경 띠의 아래 끝(DungeonBattleBuilder.GameSceneBattleLiftY와 짝)
        private const float UiBackGapFromDungeon = 20f;   // 던전 배경과 띄울 간격(패딩)
        // 9-slice 원본(2048×731)의 테두리(상하 250px)가 두꺼워 그대로 쓰면 바 높이(186)를 넘는다
        // → 배율로 줄여 쓴다(250/4 = 62.5씩, 상하 합 125 < 186).
        private const float UiBackPixelsPerUnitMultiplier = 4f;

        // 토글 버튼(접혀도 남아 있는 손잡이). 하단 바에 붙이지 않고 <b>우측 상단 버프 아이콘 바로 아래</b>에 둔다
        // (우상단 앵커 기준). 버프 아이콘이 y -28에서 아래로 BuffIconSize(96)만큼 차지하므로 그 아래로 12 띄운다.
        // 아이콘만 노출한다(뒷배경 프레임 없음) — 버튼 크기가 곧 아이콘 크기다.
        private const float MenuToggleSize = 128f;
        private const float MenuToggleGapFromBuff = 12f;
        // BuffIconPos(static readonly)를 참조하지 않고 좌표 상수로 계산한다 —
        // 정적 필드는 선언 순서대로 초기화되므로 뒤에 선언된 필드를 읽으면 0이 된다.
        private static readonly Vector2 MenuTogglePos =
            new Vector2(BuffIconX, BuffIconY - BuffIconSize - MenuToggleGapFromBuff);

        // 열기/닫기 연출. 열 때는 ① 바가 오른쪽 끝에서 왼쪽으로 펼쳐지고 ② 아이콘이 왼쪽부터 오른쪽으로
        // 작은 크기에서 원래 크기까지 순차적으로 커진다. 닫을 때는 이 순서를 역재생한다 —
        // ① 아이콘이 오른쪽부터 왼쪽으로 차례로 작아지고 ② 바가 오른쪽으로 말려 접힌다.
        private const float MenuOpenSeconds = 0.20f;
        private const float MenuCloseSeconds = 0.16f;
        private const float MenuIconPopSeconds = 0.16f;      // 아이콘 하나가 커지는 데 걸리는 시간
        private const float MenuIconPopStagger = 0.03f;      // 왼쪽→오른쪽 아이콘 간 시차
        private const float MenuIconPopStartScale = 0.35f;   // 팝 시작 크기(작아진 상태)
        private const float MenuIconPopOvershoot = 0.6f;     // 원래 크기를 살짝 지나치는 정도(0이면 오버슈트 없음)

        // 바 기하. 바 = 접히는 영역(배경 프레임 + 아이콘 줄)이며, 이 폭 그대로 화면 하단 중앙에 놓인다.
        private const float MenuAreaWidth = (MenuSlotCount - 1) * MenuSlotStep + MenuButtonWidth + UiBackSidePadding * 2f;
        private const float MenuBarBottom = MenuRowY - UiBackPadding;
        // 바 위쪽 끝: 아이콘 줄 위 여백과 던전 배경과의 간격 중 더 낮은 쪽을 택해 던전 띠를 침범하지 않게 한다.
        private static readonly float MenuBarTop = Mathf.Min(MenuRowY + MenuButtonHeight + UiBackPadding,
                                                            DungeonBandBottomY - UiBackGapFromDungeon);
        private static readonly float MenuBarHeight = MenuBarTop - MenuBarBottom;

        // 우측 상단 버프 아이콘: 화면 모서리에서 살짝 띄운 위치·크기(우상단 앵커 기준).
        // 좌표를 상수로 둬 메뉴 토글 버튼 위치(MenuTogglePos)가 이 아래에 붙도록 계산할 수 있게 한다.
        private const float BuffIconSize = 96f;
        private const float BuffIconX = -28f;
        private const float BuffIconY = -28f;
        private static readonly Vector2 BuffIconPos = new Vector2(BuffIconX, BuffIconY);

        // ESC 메뉴 버튼: system_slot 원본(2048×731) 테두리 상하 128px → 배율 4로 32씩(합 64 < 100).
        private const float EscButtonWidth = 440f;
        private const float EscButtonHeight = 100f;
        private const float SystemSlotPixelsPerUnitMultiplier = 4f;

        private GameObject _escMenuRoot; // ESC로 토글하는 메뉴(타이틀 복귀)

        // 하단 메뉴바 토글 상태·연출 대상.
        private RectTransform _menuArea;         // 접히는 영역(폭을 0↔MenuAreaWidth로 애니메이션)
        private RectTransform[] _menuButtons;    // 인덱스 = 칸 번호(0 = 맨 오른쪽)
        private RectTransform _menuToggleRect;   // 접혀 있을 때도 남는 손잡이(보상 연출 폴백 목표)

        // 보상 획득 연출이 날아갈 칸 번호(위 CreateMenuButton 호출 순서와 같아야 한다).
        private const int InventorySlot = 1;     // 가방 — 모든 획득 보상(골드·경험치·전리품)이 들어가는 곳
        private Text _menuToggleLabel;           // 토글 버튼의 화살표 폴백(아이콘이 없을 때만 생성)
        private Sprite _menuToggleSprite;        // 메뉴 아이콘 텍스처로 런타임에 만든 스프라이트(OnDestroy에서 정리)
        private ButtonPunchScale _menuTogglePunch; // 클릭 시 아이콘이 커졌다 작아지는 연출(아이콘/화살표에 부착)
        private Coroutine _menuAnim;             // 진행 중인 열기/닫기 연출
        private bool _menuOpen = false;          // 기본은 접힌 상태(손잡이만 보이고, 눌러야 펼쳐진다)

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
            canvas.sortingOrder = 10; // 패널(100)보다 아래
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
            // 구조 — MenuBar(중앙 정렬, 피벗 오른쪽) ├ MenuArea(접히는 영역: 배경+아이콘, 마스크로 클리핑)
            //                                        └ MenuToggleButton(오른쪽 끝 고정, 접혀도 남는 손잡이)
            var bar = CreateBottomCenterBar(gameArea.transform);
            _menuArea = CreateMenuArea(bar);
            BuildMenuBackground(_menuArea);      // 아이콘보다 먼저 만들어 뒤에 깔리게 한다(자식 순서 = 그리기 순서)
            _menuToggleRect = CreateMenuToggleButton(gameArea.transform, font); // 바가 아니라 전투 화면 밴드 직속(게임 화면 안쪽 우측에 배치)

            // 접히는 영역 안에 한 줄로: 오른쪽부터 [환경설정] [가방] [스테이지] [편성] [메일] [출석부] [거래소] [뽑기].
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

            // 기본 상태(_menuOpen)를 연출 없이 즉시 반영한다. 접힌 상태가 기본이므로 폭 0·아이콘 축소로
            // 만들어 둬야 첫 프레임에 펼쳐진 바가 잠깐 보였다가 사라지는 일이 없다.
            ApplyMenuBarState(_menuOpen);
        }

        /// <summary>메뉴바를 지정 상태로 <b>연출 없이</b> 즉시 맞춘다(초기 상태 적용용).</summary>
        private void ApplyMenuBarState(bool open)
        {
            SetMenuAreaWidth(open ? MenuAreaWidth : 0f);
            SetMenuIconScale(open ? 1f : MenuIconPopStartScale);
            _menuArea.gameObject.SetActive(open); // 접혀 있으면 꺼 둔다(잔여 클릭·갱신 비용 제거)
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
        /// 접히는 영역(배경 프레임 + 아이콘 줄)을 만든다. 오른쪽 끝을 기준으로 폭이 0 ↔ <see cref="MenuAreaWidth"/>로
        /// 변하며 <b>오른쪽에서 왼쪽으로 펼쳐진다</b>. <see cref="RectMask2D"/>로 클리핑해 아직 펼쳐지지 않은
        /// 아이콘이 밖으로 새지 않게 한다(클리핑된 영역은 클릭도 받지 않는다).
        /// </summary>
        private static RectTransform CreateMenuArea(RectTransform bar)
        {
            var go = new GameObject("MenuArea", typeof(RectTransform), typeof(RectMask2D));
            go.transform.SetParent(bar, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f); // 바의 오른쪽 아래 기준
            rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(MenuAreaWidth, MenuBarHeight);
            return rt;
        }

        /// <summary>
        /// 아이콘 줄 뒤에 깔리는 배경 프레임(ui_bg)을 만든다. 접히는 영역을 꽉 채우도록 늘려 두어
        /// <b>영역이 접힐 때 프레임도 함께 말려</b>(9-slice라 테두리는 유지된 채) 보인다.
        /// 스프라이트가 없으면 배경을 만들지 않는다(아이콘만 표시).
        /// </summary>
        private void BuildMenuBackground(RectTransform area)
        {
            if (uiBackgroundSprite == null)
            {
                return;
            }

            var go = new GameObject("MenuBackground", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(area, false);
            var img = go.GetComponent<Image>();
            img.sprite = uiBackgroundSprite;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = UiBackPixelsPerUnitMultiplier;
            img.color = Color.white;
            img.raycastTarget = false; // 배경은 클릭을 먹지 않는다(창 드래그·아이콘 클릭 방해 금지)

            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; // 영역 전체를 채운다(폭 애니메이션에 따라 함께 줄었다 늘어난다)
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// 메뉴바 토글 버튼을 만든다(접혀도 남아 있어 다시 펼치는 손잡이).
        /// 하단 바에 붙이지 않고 <b>우측 상단 버프 아이콘 바로 아래</b>(<see cref="MenuTogglePos"/>)에 두며,
        /// <b>뒷배경 프레임 없이 아이콘만</b> 노출한다(버튼 크기 = 아이콘 크기).
        /// 아이콘은 <c>Assets/Art/Icon/메뉴.png</c>(메뉴 버튼 아이콘)이며, 배선되지 않았으면
        /// 화살표 텍스트로 대체한다 — 펼쳐져 있으면 '&gt;', 접혀 있으면 '&lt;'.
        /// </summary>
        private RectTransform CreateMenuToggleButton(Transform parent, Font font)
        {
            var go = new GameObject("MenuToggleButton", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            // 뒷배경 프레임 없이 아이콘만 보이게 한다. 이 Image는 클릭 판정용이라
            // 완전 투명(A=0)으로 두되 raycastTarget은 유지한다(투명해도 클릭은 정상 동작).
            var img = go.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0f);

            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f); // 화면 우상단 기준(버프 아이콘과 같은 앵커)
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = MenuTogglePos;
            rt.sizeDelta = new Vector2(MenuToggleSize, MenuToggleSize);

            if (menuToggleIconTexture != null)
            {
                CreateMenuToggleIcon(go.transform);
            }
            else
            {
                CreateMenuToggleArrow(go.transform, font);
            }

            // 클릭 피드백(커졌다 작아지는 punch)은 버튼 루트가 아니라 <b>아이콘 자식</b>에 붙인다 —
            // 버튼 루트의 피벗이 우상단(1,1)이라 그걸 키우면 아이콘이 좌하단으로 쏠려 커진다.
            // 아이콘 자식은 피벗이 중앙이라 제자리에서 커졌다 돌아온다.
            go.GetComponent<Button>().onClick.AddListener(() =>
            {
                if (_menuTogglePunch != null)
                {
                    _menuTogglePunch.Play(); // 바 연출과 동시에 시작(끝날 때까지 기다리지 않는다)
                }
                ToggleMenuBar();
            });
            return rt;
        }

        /// <summary>
        /// 토글 버튼 안에 메뉴 아이콘을 붙인다.
        /// <para>텍스처 <b>전체 영역</b>으로 스프라이트를 만든다 — 이 파일(메뉴.png)은 Multiple로 임포트돼
        /// 햄버거 3줄이 각각 서브 스프라이트로 쪼개져 있어, 스프라이트 에셋을 그대로 쓰면 줄 한 개만 나온다.
        /// 임포트 설정을 Single로 바꾸는 것은 공용 아트 규칙상 금지(서브 스프라이트 참조가 끊긴다)이므로
        /// 런타임에 합쳐 쓴다. 만든 스프라이트는 <see cref="OnDestroy"/>에서 정리한다.</para>
        /// </summary>
        private void CreateMenuToggleIcon(Transform parent)
        {
            _menuToggleSprite = Sprite.Create(menuToggleIconTexture,
                new Rect(0f, 0f, menuToggleIconTexture.width, menuToggleIconTexture.height),
                new Vector2(0.5f, 0.5f), 100f);

            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(parent, false);
            var iconImg = iconGo.GetComponent<Image>();
            iconImg.sprite = _menuToggleSprite;
            iconImg.preserveAspect = true;
            iconImg.raycastTarget = false; // 클릭은 부모 버튼이 받는다
            var irt = iconImg.rectTransform;
            irt.anchorMin = Vector2.zero; // 버튼 전체를 채운다(배경 프레임이 없으므로 여백을 두지 않는다)
            irt.anchorMax = Vector2.one;
            irt.offsetMin = Vector2.zero;
            irt.offsetMax = Vector2.zero;

            _menuTogglePunch = iconGo.AddComponent<ButtonPunchScale>();
        }

        /// <summary>아이콘이 배선되지 않았을 때 쓰는 화살표 폴백(다음에 일어날 동작 방향을 가리킨다).</summary>
        private void CreateMenuToggleArrow(Transform parent, Font font)
        {
            var labelGo = new GameObject("Arrow", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(parent, false);
            _menuToggleLabel = labelGo.GetComponent<Text>();
            _menuToggleLabel.font = font;
            _menuToggleLabel.fontSize = 40;
            _menuToggleLabel.fontStyle = FontStyle.Bold;
            _menuToggleLabel.alignment = TextAnchor.MiddleCenter;
            _menuToggleLabel.color = Color.white;
            _menuToggleLabel.raycastTarget = false;
            _menuToggleLabel.text = MenuToggleArrow(_menuOpen);
            var lrt = (RectTransform)labelGo.transform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = Vector2.zero;

            _menuTogglePunch = labelGo.AddComponent<ButtonPunchScale>();
        }

        /// <summary>런타임에 만든 토글 아이콘 스프라이트를 정리하고, 보상 연출 목표 등록을 해제한다.</summary>
        private void OnDestroy()
        {
            if (_menuToggleSprite != null)
            {
                Destroy(_menuToggleSprite);
                _menuToggleSprite = null;
            }
            // 내가 등록한 것일 때만 해제한다(씬 전환 중 새 HUD가 이미 등록했을 수 있다).
            if (Battle.RewardFlyFx.TargetProvider == RewardFlyTarget)
            {
                Battle.RewardFlyFx.TargetProvider = null;
            }
        }

        /// <summary>토글 버튼에 표시할 화살표(다음에 일어날 동작 방향)를 돌려준다.</summary>
        private static string MenuToggleArrow(bool open) => open ? ">" : "<";

        /// <summary>기능 버튼 하나를 접히는 영역 안 <paramref name="slot"/>번째 칸(0 = 맨 오른쪽)에 만든다.</summary>
        private GameObject CreateMenuButton(Font font, string name, string label, Sprite icon,
            int slot, UnityEngine.Events.UnityAction onClick)
        {
            // 영역의 오른쪽 끝에서 안쪽 여백만큼 들어온 지점이 첫 칸(0)의 오른쪽 끝이다.
            var pos = new Vector2(-UiBackSidePadding - slot * MenuSlotStep, UiBackPadding);
            var go = CreateButton(_menuArea, font, name, label, icon, pos, onClick);

            // 팝 연출이 아이콘 '제자리'에서 커지도록 피벗을 칸 중앙으로 옮긴다.
            // CreateButton의 피벗(1,0 = 우측 하단)을 그대로 두면 축소가 우측 하단으로 쏠려 아이콘이 바닥에 몰려 보인다.
            // 피벗 이동에 맞춰 위치를 칸 중심으로 바꾸므로 실제 사각형(rect)은 그대로다.
            var rt = (RectTransform)go.transform;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(pos.x - MenuButtonWidth * 0.5f, pos.y + MenuButtonHeight * 0.5f);

            // 클릭 피드백(커졌다 돌아오는 punch). 피벗을 방금 칸 중앙으로 옮겼으므로 버튼 루트에 붙여도
            // 아이콘·글자가 함께 제자리에서 커진다(토글 버튼은 피벗이 우상단이라 아이콘 자식에 붙인다).
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
                irt.anchoredPosition = new Vector2(0f, -2f);   // 상단 여백 축소
                irt.sizeDelta = new Vector2(104f, 104f);        // 아이콘 확대

                // 텍스트(아이콘 아래) — 크게 + 볼드, 여백 축소
                t.fontSize = 28;
                t.fontStyle = FontStyle.Bold;
                lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0f);
                lrt.pivot = new Vector2(0.5f, 0f);
                lrt.anchoredPosition = new Vector2(0f, 4f);
                lrt.sizeDelta = new Vector2(MenuButtonWidth - 4f, 40f);
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

        /// <summary>
        /// 하단 메뉴바를 토글한다(펼침 ↔ 접힘). 연출 중에 다시 누르면 진행 중인 코루틴을 멈추고
        /// <b>현재 폭에서</b> 반대 방향으로 이어 달려, 연타해도 상태와 화면이 어긋나지 않는다.
        /// </summary>
        private void ToggleMenuBar()
        {
            _menuOpen = !_menuOpen;
            if (_menuToggleLabel != null)
            {
                _menuToggleLabel.text = MenuToggleArrow(_menuOpen);
            }
            if (_menuAnim != null)
            {
                StopCoroutine(_menuAnim);
            }
            _menuAnim = StartCoroutine(AnimateMenuBar(_menuOpen));
        }

        /// <summary>
        /// 메뉴바 열기/닫기 연출. <b>닫기는 열기의 역재생</b>이다.
        /// <para><b>열 때</b> — ① 접히는 영역의 폭을 0 → <see cref="MenuAreaWidth"/>로 키운다. 피벗이 오른쪽이라
        /// <b>오른쪽에서 왼쪽으로 펼쳐진다.</b> ② 이어서 아이콘을 <b>왼쪽부터 오른쪽으로</b> 시차를 두고
        /// 작아진 상태에서 원래 크기까지 키운다.</para>
        /// <para><b>닫을 때</b> — ① 아이콘을 <b>오른쪽부터 왼쪽으로</b>(= 열 때의 반대 순서) 차례로 작게 줄인 뒤
        /// ② 폭을 0으로 되돌려 오른쪽으로 말아 넣고 영역을 비활성화한다.</para>
        /// <para>남은 거리에 비례해 시간을 잡으므로 연출 중간에 방향이 바뀌어도 속도가 일정하다.
        /// 시간은 <see cref="Time.unscaledDeltaTime"/> 기준이라 일시정지(timeScale 0)에서도 동작한다.</para>
        /// </summary>
        private IEnumerator AnimateMenuBar(bool open)
        {
            if (open)
            {
                _menuArea.gameObject.SetActive(true);
                SetMenuIconScale(MenuIconPopStartScale); // 펼침이 끝난 뒤 커지도록 작은 크기에서 시작
                yield return RollMenuArea(true);
                yield return ScaleMenuIcons(true);
            }
            else
            {
                yield return ScaleMenuIcons(false);
                yield return RollMenuArea(false);
                _menuArea.gameObject.SetActive(false); // 접힌 뒤에는 꺼 둔다(잔여 클릭·갱신 비용 제거)
                SetMenuIconScale(1f);                  // 다음에 열 때를 위해 원래 크기로 되돌려 둔다
            }
            _menuAnim = null;
        }

        /// <summary>접히는 영역의 폭을 목표까지 애니메이션한다(열기 = 왼쪽으로 펼침, 닫기 = 오른쪽으로 말아 넣기).</summary>
        private IEnumerator RollMenuArea(bool open)
        {
            float from = _menuArea.sizeDelta.x;
            float to = open ? MenuAreaWidth : 0f;
            float duration = (open ? MenuOpenSeconds : MenuCloseSeconds) * (Mathf.Abs(to - from) / MenuAreaWidth);

            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                SetMenuAreaWidth(Mathf.Lerp(from, to, EaseOutCubic(t / duration)));
                yield return null;
            }
            SetMenuAreaWidth(to);
        }

        /// <summary>
        /// 아이콘 크기를 <see cref="MenuIconPopStagger"/>초씩 시차를 두고 차례로 바꾼다.
        /// <para><paramref name="grow"/>면 <b>왼쪽부터 오른쪽으로</b> 원래 크기까지 커지고(살짝 오버슈트 후 안착),
        /// 아니면 <b>오른쪽부터 왼쪽으로</b> 작은 크기까지 줄어든다 — 순서가 반대라 닫기가 열기의 역재생이 된다.
        /// 칸 번호는 오른쪽부터 0이므로 커질 때만 역순(<c>n-1-slot</c>)을 쓴다.</para>
        /// <para>시작 크기를 현재 값에서 읽으므로, 연출 중간에 토글을 눌러 방향이 바뀌어도 크기가 튀지 않는다.</para>
        /// </summary>
        private IEnumerator ScaleMenuIcons(bool grow)
        {
            int n = _menuButtons.Length;
            var from = new float[n];
            for (int i = 0; i < n; i++)
            {
                from[i] = _menuButtons[i] != null ? _menuButtons[i].localScale.x : 1f;
            }

            float target = grow ? 1f : MenuIconPopStartScale;
            float total = (n - 1) * MenuIconPopStagger + MenuIconPopSeconds;

            for (float t = 0f; t < total; t += Time.unscaledDeltaTime)
            {
                for (int slot = 0; slot < n; slot++)
                {
                    var rt = _menuButtons[slot];
                    if (rt == null)
                    {
                        continue;
                    }
                    int order = grow ? n - 1 - slot : slot; // 커질 때는 왼쪽부터, 줄어들 때는 오른쪽부터
                    float k = Mathf.Clamp01((t - order * MenuIconPopStagger) / MenuIconPopSeconds);
                    float eased = grow ? EaseOutBack(k) : EaseInCubic(k);
                    float scale = Mathf.LerpUnclamped(from[slot], target, eased);
                    rt.localScale = new Vector3(scale, scale, 1f);
                }
                yield return null;
            }
            SetMenuIconScale(target); // 끝나면 정확히 목표 크기로 고정
        }

        /// <summary>접히는 영역의 폭을 설정한다(피벗이 오른쪽이라 폭이 줄면 오른쪽으로 말려 들어간다).</summary>
        private void SetMenuAreaWidth(float width)
        {
            _menuArea.sizeDelta = new Vector2(Mathf.Max(0f, width), MenuBarHeight);
        }

        /// <summary>메뉴 아이콘 전체의 크기 배율을 한 번에 지정한다(1 = 원래 크기).</summary>
        private void SetMenuIconScale(float scale)
        {
            if (_menuButtons == null)
            {
                return;
            }
            foreach (var rt in _menuButtons)
            {
                if (rt != null)
                {
                    rt.localScale = new Vector3(scale, scale, 1f);
                }
            }
        }

        /// <summary>
        /// 커질 때 쓰는 곡선(ease-out-back). 0에서 0, 1에서 <b>정확히 1</b>이고 그 사이에서 1을 살짝 넘겼다가
        /// (<see cref="MenuIconPopOvershoot"/>) 되돌아와 안착한다 — 아이콘이 원래 크기를 살짝 지나쳐 커지는 탄력.
        /// </summary>
        private static float EaseOutBack(float k)
        {
            if (k <= 0f)
            {
                return 0f;
            }
            if (k >= 1f)
            {
                return 1f;
            }
            float c = 1.70158f * MenuIconPopOvershoot;
            float p = k - 1f;
            return 1f + (c + 1f) * p * p * p + c * p * p;
        }

        /// <summary>줄어들 때 쓰는 곡선(ease-in-cubic — 천천히 시작해 빠르게 줄어든다. 커질 때의 감속과 거울 관계).</summary>
        private static float EaseInCubic(float k)
        {
            k = Mathf.Clamp01(k);
            return k * k * k;
        }

        /// <summary>펼침/접힘 폭 변화에 쓰는 감속 곡선(끝에서 부드럽게 멎는다).</summary>
        private static float EaseOutCubic(float k)
        {
            float p = 1f - Mathf.Clamp01(k);
            return 1f - p * p * p;
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
        /// <para>메뉴 바가 접혀 있으면 버튼이 폭 0으로 눌려 있어 그 자리로 보내면 어디로 갔는지 알 수 없다.
        /// 그때는 항상 보이는 <b>토글 손잡이</b>를 목표로 준다.</para>
        /// </summary>
        public RectTransform RewardFlyTarget()
        {
            if (!_menuOpen)
            {
                return _menuToggleRect;
            }
            if (_menuButtons != null && InventorySlot < _menuButtons.Length && _menuButtons[InventorySlot] != null)
            {
                return _menuButtons[InventorySlot];
            }
            return _menuToggleRect;
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
            prt.sizeDelta = new Vector2(560f, 540f);
            prt.anchoredPosition = Vector2.zero;
            var panelImg = panel.GetComponent<Image>();
            panelImg.color = new Color(0.10f, 0.12f, 0.18f, 0.98f); // 아트 미배선 시 단색 폴백
            if (systemBackgroundSprite != null)
            {
                panelImg.sprite = systemBackgroundSprite;
                panelImg.type = Image.Type.Sliced;
                panelImg.color = Color.white;
            }

            var title = MakeMenuText(font, panel.transform, "메뉴", 48, new Vector2(0f, 200f), 480f);
            title.fontStyle = FontStyle.Bold;
            title.color = new Color(1f, 0.92f, 0.72f); // 나무 배경 위 금색 톤

            MakeMenuButton(font, panel.transform, "타이틀로 돌아가기", new Vector2(0f, 70f), OnReturnToTitle);
            MakeMenuButton(font, panel.transform, "계속하기", new Vector2(0f, -50f), HideEscMenu);
            MakeMenuButton(font, panel.transform, "게임종료", new Vector2(0f, -170f), OnQuitGame);

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
