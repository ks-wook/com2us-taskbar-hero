using System.Collections.Generic;
using UnityEngine;

namespace TaskbarHero.Client.Managers
{
    /// <summary>
    /// 클라이언트 UI 패널의 생성·표시·전환을 담당하는 매니저.
    /// 각 패널 프리팹을 최초 1회 인스턴스화해 캐싱하고, 한 번에 하나의 패널만 활성화한다.
    /// (각 패널 프리팹은 자체 Canvas·EventSystem을 포함하므로 비활성 패널의 EventSystem은 함께 꺼진다.)
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        /// <summary>관리 대상 패널 종류.</summary>
        public enum PanelType
        {
            Login,
            SignUp,
            Inventory,
            Stage,
            // 파티 편성은 팝업 패널이 아니라 전용 씬(TeamListScene)으로 다루므로 패널 종류에 없다.
            Skill,
            Rune,
            Cube,
            OfflineReward,
            Mail,
            Attendance,
            Trade,
            Gacha,
            Settings,
        }

        public static UIManager Instance { get; private set; }

        [Header("패널 프리팹")]
        [SerializeField] private GameObject loginPanelPrefab;
        [SerializeField] private GameObject signUpPanelPrefab;
        [Tooltip("Assets/Prefabs/UI/InventoryPanel 프리팹을 배선한다(Title·GameScene 양쪽 UIManager).")]
        [SerializeField] private GameObject inventoryPanelPrefab;
        [Tooltip("Assets/Prefabs/UI/StagePanel 프리팹을 배선한다(Title·GameScene 양쪽 UIManager).")]
        [SerializeField] private GameObject stagePanelPrefab;
        [Tooltip("Assets/Prefabs/UI/SkillPanel 프리팹을 배선한다(Title·GameScene 양쪽 UIManager).")]
        [SerializeField] private GameObject skillPanelPrefab;
        [Tooltip("Assets/Prefabs/UI/RunePanel 프리팹을 배선한다(Title·GameScene 양쪽 UIManager).")]
        [SerializeField] private GameObject runePanelPrefab;
        [Tooltip("Assets/Prefabs/UI/CubePanel 프리팹을 배선한다(Title·GameScene 양쪽 UIManager).")]
        [SerializeField] private GameObject cubePanelPrefab;
        [Tooltip("Assets/Prefabs/UI/OfflineRewardPanel 프리팹을 배선한다(Title·GameScene 양쪽 UIManager).")]
        [SerializeField] private GameObject offlineRewardPanelPrefab;
        [Tooltip("Assets/Prefabs/UI/MailPanel 프리팹을 배선한다(Title·GameScene 양쪽 UIManager).")]
        [SerializeField] private GameObject mailPanelPrefab;
        [Tooltip("Assets/Prefabs/UI/AttendancePanel 프리팹을 배선한다(Title·GameScene 양쪽 UIManager).")]
        [SerializeField] private GameObject attendancePanelPrefab;
        [Tooltip("Assets/Prefabs/UI/TradePanel 프리팹을 배선한다(Title·GameScene 양쪽 UIManager).")]
        [SerializeField] private GameObject tradePanelPrefab;
        [Tooltip("Assets/Prefabs/UI/GachaPanel 프리팹을 배선한다(Title·GameScene 양쪽 UIManager).")]
        [SerializeField] private GameObject gachaPanelPrefab;
        [Tooltip("Assets/Prefabs/UI/SettingsPanel 프리팹을 배선한다(Title·GameScene 양쪽 UIManager).")]
        [SerializeField] private GameObject settingsPanelPrefab;

        [Header("시작 설정")]
        [Tooltip("Start 시 자동으로 표시할 패널. 자동 표시를 원치 않으면 비활성화한다.")]
        [SerializeField] private bool showOnStart = true;
        [SerializeField] private PanelType startPanel = PanelType.Login;

        private readonly Dictionary<PanelType, GameObject> _instances = new Dictionary<PanelType, GameObject>();

        /// <summary>현재 표시 중인 패널. 없으면 null.</summary>
        public PanelType? Current { get; private set; }

        /// <summary>현재 화면에 실제로 활성화(표시)된 관리 패널이 하나라도 있는지.
        /// (<see cref="Current"/>는 씬 전환 시 초기화되지 않아 신뢰할 수 없으므로, 실제 인스턴스 활성 상태로 판정한다.)</summary>
        public bool IsAnyPanelVisible()
        {
            foreach (var panel in _instances.Values)
            {
                if (panel != null && panel.activeInHierarchy)
                {
                    return true;
                }
            }
            return false;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Start()
        {
            if (showOnStart)
            {
                Show(startPanel);
            }
        }

        /// <summary>로그인 패널을 표시한다.</summary>
        public void ShowLogin() => Show(PanelType.Login);

        /// <summary>회원가입 패널을 표시한다.</summary>
        public void ShowSignUp() => Show(PanelType.SignUp);

        /// <summary>인벤토리 패널을 표시한다.</summary>
        public void ShowInventory() => Show(PanelType.Inventory);

        /// <summary>인벤토리 패널을 열려 있으면 닫고, 닫혀 있으면 연다(On/Off 토글).</summary>
        public void ToggleInventory()
        {
            if (Current == PanelType.Inventory)
            {
                Hide(PanelType.Inventory);
            }
            else
            {
                Show(PanelType.Inventory);
            }
        }

        /// <summary>스테이지 선택 패널을 표시한다.</summary>
        public void ShowStage() => Show(PanelType.Stage);

        /// <summary>스테이지 선택 패널을 열려 있으면 닫고, 닫혀 있으면 연다(On/Off 토글).</summary>
        public void ToggleStage()
        {
            if (Current == PanelType.Stage)
            {
                Hide(PanelType.Stage);
            }
            else
            {
                Show(PanelType.Stage);
            }
        }

        /// <summary>스킬 레벨업 패널을 표시한다.</summary>
        public void ShowSkill() => Show(PanelType.Skill);

        /// <summary>스킬 레벨업 패널을 열려 있으면 닫고, 닫혀 있으면 연다(On/Off 토글).</summary>
        public void ToggleSkill()
        {
            if (Current == PanelType.Skill)
            {
                Hide(PanelType.Skill);
            }
            else
            {
                Show(PanelType.Skill);
            }
        }

        /// <summary>룬 패널을 표시한다.</summary>
        public void ShowRune() => Show(PanelType.Rune);

        /// <summary>룬 패널을 열려 있으면 닫고, 닫혀 있으면 연다(On/Off 토글).</summary>
        public void ToggleRune()
        {
            if (Current == PanelType.Rune)
            {
                Hide(PanelType.Rune);
            }
            else
            {
                Show(PanelType.Rune);
            }
        }

        /// <summary>큐브 패널을 표시한다.</summary>
        public void ShowCube() => Show(PanelType.Cube);

        /// <summary>큐브 패널을 열려 있으면 닫고, 닫혀 있으면 연다(On/Off 토글).</summary>
        public void ToggleCube()
        {
            if (Current == PanelType.Cube)
            {
                Hide(PanelType.Cube);
            }
            else
            {
                Show(PanelType.Cube);
            }
        }

        /// <summary>오프라인 보상 정산 결과 팝업을 표시한다(GameScene 진입 시 대기 중인 보상이 있을 때).</summary>
        public void ShowOfflineReward() => Show(PanelType.OfflineReward);

        /// <summary>우편함(메일) 패널을 표시한다.</summary>
        public void ShowMail() => Show(PanelType.Mail);

        /// <summary>우편함(메일) 패널을 열려 있으면 닫고, 닫혀 있으면 연다(On/Off 토글).</summary>
        public void ToggleMail()
        {
            if (Current == PanelType.Mail)
            {
                Hide(PanelType.Mail);
            }
            else
            {
                Show(PanelType.Mail);
            }
        }

        /// <summary>출석부 패널을 표시한다.</summary>
        public void ShowAttendance() => Show(PanelType.Attendance);

        /// <summary>출석부 패널을 열려 있으면 닫고, 닫혀 있으면 연다(On/Off 토글).</summary>
        public void ToggleAttendance()
        {
            if (Current == PanelType.Attendance)
            {
                Hide(PanelType.Attendance);
            }
            else
            {
                Show(PanelType.Attendance);
            }
        }

        /// <summary>거래소 패널을 표시한다.</summary>
        public void ShowTrade() => Show(PanelType.Trade);

        /// <summary>거래소 패널을 열려 있으면 닫고, 닫혀 있으면 연다(On/Off 토글).</summary>
        public void ToggleTrade()
        {
            if (Current == PanelType.Trade)
            {
                Hide(PanelType.Trade);
            }
            else
            {
                Show(PanelType.Trade);
            }
        }

        /// <summary>뽑기(가챠) 패널을 표시한다.</summary>
        public void ShowGacha() => Show(PanelType.Gacha);

        /// <summary>뽑기(가챠) 패널을 열려 있으면 닫고, 닫혀 있으면 연다(On/Off 토글).</summary>
        public void ToggleGacha()
        {
            if (Current == PanelType.Gacha)
            {
                Hide(PanelType.Gacha);
            }
            else
            {
                Show(PanelType.Gacha);
            }
        }

        /// <summary>환경설정 패널을 표시한다.</summary>
        public void ShowSettings() => Show(PanelType.Settings);

        /// <summary>환경설정 패널을 열려 있으면 닫고, 닫혀 있으면 연다(On/Off 토글).</summary>
        public void ToggleSettings()
        {
            if (Current == PanelType.Settings)
            {
                Hide(PanelType.Settings);
            }
            else
            {
                Show(PanelType.Settings);
            }
        }

        /// <summary>지정한 패널을 표시하고 나머지 패널은 모두 숨긴다.</summary>
        public void Show(PanelType type)
        {
            var panel = GetOrCreate(type);
            if (panel == null)
            {
                return;
            }
            if (Current != type)
            {
                SoundManager.Sfx(SoundId.UiPanelOpen); // 이미 열린 패널을 다시 Show하면 울리지 않는다
            }

            // 캔버스 규격을 표시 직전에 다시 맞춘다. 만들 때 한 번만 맞추면 **처음 열 때 패널이 크게** 그려진다
            // (프리팹 기본 규격 1080×1920·match 0.5의 배율 0.84 vs GameScene 규격의 0.63 = 약 1.33배. 실측).
            // 창 크기가 바뀐 뒤 처음 여는 경우도 이 시점에 함께 교정된다.
            GameViewLayout.ApplyCurrentScalers(panel);
            AlignToInventoryPanel(type, panel);

            foreach (var pair in _instances)
            {
                if (pair.Value != null)
                {
                    // 큐브는 가방과 <b>함께</b> 보여야 한다 — 가방에서 아이템을 끌어다 큐브 슬롯에 올리기 때문이다.
                    bool keep = pair.Key == type
                                || (OpensWithInventory(type) && pair.Key == PanelType.Inventory);
                    pair.Value.SetActive(keep);
                }
            }

            Current = type;
            TaskbarWindow.Instance?.SetExpanded(true); // 패널 표시 → 창 확장
        }

        /// <summary>
        /// 이미 만들어진 패널의 창 본체(<c>PanelRoot</c>) RectTransform을 돌려준다. 없으면 null이며
        /// <b>새로 만들지 않는다</b>.
        /// <para>한 패널이 다른 패널이 있던 자리에 뜨게 할 때 쓴다(예: 인벤토리에서 여는 큐브 패널) —
        /// 사용자가 창을 옮겼거나 크기를 바꿨을 수 있으므로 프리팹 값이 아니라 <b>살아 있는 인스턴스</b>를 기준으로
        /// 삼아야 실제로 보고 있던 위치와 맞는다.</para>
        /// </summary>
        public RectTransform FindPanelRoot(PanelType type)
        {
            if (_instances.TryGetValue(type, out var panel) && panel != null)
            {
                return panel.transform.Find(PanelRootName) as RectTransform;
            }
            return null;
        }

        /// <summary>패널 프리팹들이 공통으로 쓰는 창 본체 오브젝트 이름(각 패널 컨트롤러의 Construct가 만든다).</summary>
        private const string PanelRootName = "PanelRoot";

        /// <summary>인벤토리 창 자리에 이어서 띄우는 패널들 — 모두 인벤토리 안의 버튼으로 진입한다.
        /// 큐브는 가방과 나란히 <b>동시에</b> 열려 자리를 물려받지 않으므로 제외한다
        /// (<see cref="OpensWithInventory"/>).</summary>
        private static bool FollowsInventoryPlacement(PanelType type)
            => type == PanelType.Skill || type == PanelType.Rune;

        /// <summary>
        /// 가방을 <b>켜 둔 채</b> 함께 여는 패널 — 현재는 큐브뿐이다.
        /// 가방에서 아이템을 끌어다 큐브 슬롯에 올려야 하므로 두 창이 동시에 보여야 하며,
        /// 겹치면 사용자가 <see cref="UI.PanelDragMove"/>로 창을 옮겨 배치한다.
        /// </summary>
        private static bool OpensWithInventory(PanelType type) => type == PanelType.Cube;

        /// <summary>
        /// 인벤토리에서 이어 여는 패널(큐브·스킬·룬)을 <b>인벤토리 창이 있던 자리</b>에 맞춘다.
        /// 화면 중앙에 뜨면 방금 보고 있던 창에서 시선이 크게 튄다.
        /// <para>기준 창의 <b>왼쪽 변(= 전투 화면 쪽 변)·세로 중심</b>에 맞춘다 — 인벤토리는 전투 화면 밴드
        /// 오른쪽에 <see cref="GameViewLayout.PanelGap"/>만큼 띄워 도킹돼 있으므로, 그 변에 맞추면 이 패널들이
        /// 더 넓어도(스킬 1040 · 룬 860 vs 765) <b>전투 화면을 침범하지 않고</b> 남는 폭이 바깥(창 가장자리)
        /// 쪽으로만 흘러간다. 오른쪽 변에 맞추면 넓은 패널일수록 왼쪽이 전투 화면을 덮는다.</para>
        /// <para>인벤토리 인스턴스가 아직 없으면(다른 경로로 먼저 열린 경우) 프리팹에 구워진 배치를 그대로 둔다.
        /// 패널들은 모두 ScreenSpaceOverlay 캔버스라 월드 좌표가 곧 화면 픽셀이며, 그 화면 좌표를 대상 패널
        /// 캔버스의 로컬 좌표로 되돌려 배치한다(캔버스 배율을 직접 가정하지 않는다).</para>
        /// </summary>
        private void AlignToInventoryPanel(PanelType type, GameObject panel)
        {
            if (!FollowsInventoryPlacement(type) || panel == null)
            {
                return;
            }
            var canvasRect = panel.transform as RectTransform;
            var target = panel.transform.Find(PanelRootName) as RectTransform;
            var reference = FindPanelRoot(PanelType.Inventory);
            if (canvasRect == null || target == null || reference == null || reference == target)
            {
                return;
            }

            var corners = new Vector3[4]; // 0=좌하 1=좌상 2=우상 3=우하
            reference.GetWorldCorners(corners);
            var leftCenter = new Vector2(corners[0].x, (corners[0].y + corners[2].y) * 0.5f);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, leftCenter, null, out var local))
            {
                return;
            }

            target.anchorMin = target.anchorMax = new Vector2(0.5f, 0.5f);
            target.pivot = new Vector2(0f, 0.5f); // 왼쪽 변(전투 화면 쪽) 기준
            target.anchoredPosition = local;
        }

        /// <summary>지정한 패널을 숨긴다.</summary>
        public void Hide(PanelType type)
        {
            if (_instances.TryGetValue(type, out var panel) && panel != null)
            {
                if (panel.activeSelf)
                {
                    SoundManager.Sfx(SoundId.UiPanelClose); // 열림음과 짝이 되는 닫힘음(사운드 정의서 §4.1)
                }
                panel.SetActive(false);
            }

            if (Current == type)
            {
                Current = null;
            }
            TaskbarWindow.Instance?.SetExpanded(IsAnyPanelVisible()); // 남은 패널 여부를 창 제어기에 알림
        }

        /// <summary>지정한 패널이 지금 화면에 떠 있는가.
        /// <see cref="Current"/>는 <b>마지막으로 연</b> 패널 하나만 가리키므로, 여러 창이 함께 열리는 경우
        /// (가방 + 큐브)에는 이 조회를 쓴다.</summary>
        public bool IsVisible(PanelType type)
        {
            return _instances.TryGetValue(type, out var panel) && panel != null && panel.activeSelf;
        }

        /// <summary>모든 패널을 숨긴다.</summary>
        public void HideAll()
        {
            foreach (var panel in _instances.Values)
            {
                if (panel != null)
                {
                    panel.SetActive(false);
                }
            }

            Current = null;
            TaskbarWindow.Instance?.SetExpanded(false); // 모든 패널 닫힘을 창 제어기에 알림
        }

        private GameObject GetOrCreate(PanelType type)
        {
            if (_instances.TryGetValue(type, out var existing) && existing != null)
            {
                return existing;
            }

            var prefab = GetPrefab(type);
            if (prefab == null)
            {
                Debug.LogError($"[UIManager] {type} 패널 프리팹이 지정되지 않았습니다.", this);
                return null;
            }

            var instance = Instantiate(prefab);
            instance.name = prefab.name;
            // 패널 프리팹에는 기본 규격(1080×1920) 캔버스 스케일러가 구워져 있다. GameViewLayout의 주기
            // 스윕을 기다리면 첫 표시 때 최대 0.4초 동안 잘못된 배율(폭 넓은 GameScene 창에서 약 1.6배)로
            // 렌더돼 패널이 크게 나왔다가 줄어들므로, 만든 즉시 현재 씬 규격으로 맞춘다.
            GameViewLayout.ApplyCurrentScalers(instance);
            // 방금 만든 인스턴스는 <b>끈 상태로 넘긴다</b> — 프리팹 루트가 활성이라 그냥 두면 <see cref="Show"/>의
            // <c>SetActive(true)</c>가 아무 일도 하지 않아 <c>OnEnable</c>이 돌지 않는다. 그러면 표시마다 걸리는
            // 처리(등장 연출 <see cref="UI.SidePanelPop"/>, 끌어다 둔 자리 복원 <see cref="UI.PanelDragMove"/>)가
            // <b>첫 표시에만</b> 통째로 건너뛰어진다(두 번째 열기부터 정상 동작해 더 눈에 띈다).
            instance.SetActive(false);
            _instances[type] = instance;
            return instance;
        }

        private GameObject GetPrefab(PanelType type)
        {
            switch (type)
            {
                case PanelType.Login:
                    return loginPanelPrefab;
                case PanelType.SignUp:
                    return signUpPanelPrefab;
                case PanelType.Inventory:
                    // 프리팹은 Assets/Prefabs/UI/에 두고 UIManager 인스턴스에 직접 배선한다.
                    // 지속 인스턴스가 생성되는 Title 씬과 사용 씬(GameScene) 양쪽에 참조를 지정한다.
                    return inventoryPanelPrefab;
                case PanelType.Stage:
                    return stagePanelPrefab;
                case PanelType.Skill:
                    // 프리팹은 Assets/Prefabs/UI/에 두고 UIManager 인스턴스에 직접 배선한다.
                    // 지속 인스턴스가 생성되는 Title 씬과 사용 씬(GameScene) 양쪽에 참조를 지정한다.
                    return skillPanelPrefab;
                case PanelType.Rune:
                    return runePanelPrefab;
                case PanelType.Cube:
                    return cubePanelPrefab;
                case PanelType.OfflineReward:
                    return offlineRewardPanelPrefab;
                case PanelType.Mail:
                    return mailPanelPrefab;
                case PanelType.Attendance:
                    return attendancePanelPrefab;
                case PanelType.Trade:
                    return tradePanelPrefab;
                case PanelType.Gacha:
                    return gachaPanelPrefab;
                case PanelType.Settings:
                    return settingsPanelPrefab;
                default:
                    return null;
            }
        }
    }
}
