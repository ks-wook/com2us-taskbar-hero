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
            Party,
            Skill,
            Rune,
            Cube,
            OfflineReward,
            Mail,
            Attendance,
            Trade,
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
        [Tooltip("Assets/Prefabs/UI/PartyPanel 프리팹을 배선한다(Title·GameScene 양쪽 UIManager).")]
        [SerializeField] private GameObject partyPanelPrefab;
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

        /// <summary>파티 편성 패널을 표시한다.</summary>
        public void ShowParty() => Show(PanelType.Party);

        /// <summary>파티 편성 패널을 열려 있으면 닫고, 닫혀 있으면 연다(On/Off 토글).</summary>
        public void ToggleParty()
        {
            if (Current == PanelType.Party)
            {
                Hide(PanelType.Party);
            }
            else
            {
                Show(PanelType.Party);
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

            foreach (var pair in _instances)
            {
                if (pair.Value != null)
                {
                    pair.Value.SetActive(pair.Key == type);
                }
            }

            Current = type;
            TaskbarWindow.Instance?.SetExpanded(true); // 패널 표시 → 창 확장
        }

        /// <summary>지정한 패널을 숨긴다.</summary>
        public void Hide(PanelType type)
        {
            // 닫힘 사운드는 넣지 않는다 — 패널을 자주 여닫는 조작이라 소리가 과하다(열림음만 유지).
            if (_instances.TryGetValue(type, out var panel) && panel != null)
            {
                panel.SetActive(false);
            }

            if (Current == type)
            {
                Current = null;
            }
            TaskbarWindow.Instance?.SetExpanded(IsAnyPanelVisible()); // 남은 패널 여부를 창 제어기에 알림
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
                case PanelType.Party:
                    return partyPanelPrefab;
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
                case PanelType.Settings:
                    return settingsPanelPrefab;
                default:
                    return null;
            }
        }
    }
}
