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
        }

        public static UIManager Instance { get; private set; }

        [Header("패널 프리팹")]
        [SerializeField] private GameObject loginPanelPrefab;
        [SerializeField] private GameObject signUpPanelPrefab;

        [Header("시작 설정")]
        [Tooltip("Start 시 자동으로 표시할 패널. 자동 표시를 원치 않으면 비활성화한다.")]
        [SerializeField] private bool showOnStart = true;
        [SerializeField] private PanelType startPanel = PanelType.Login;

        private readonly Dictionary<PanelType, GameObject> _instances = new Dictionary<PanelType, GameObject>();

        /// <summary>현재 표시 중인 패널. 없으면 null.</summary>
        public PanelType? Current { get; private set; }

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

        /// <summary>지정한 패널을 표시하고 나머지 패널은 모두 숨긴다.</summary>
        public void Show(PanelType type)
        {
            var panel = GetOrCreate(type);
            if (panel == null)
            {
                return;
            }

            foreach (var pair in _instances)
            {
                if (pair.Value != null)
                {
                    pair.Value.SetActive(pair.Key == type);
                }
            }

            Current = type;
        }

        /// <summary>지정한 패널을 숨긴다.</summary>
        public void Hide(PanelType type)
        {
            if (_instances.TryGetValue(type, out var panel) && panel != null)
            {
                panel.SetActive(false);
            }

            if (Current == type)
            {
                Current = null;
            }
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
                default:
                    return null;
            }
        }
    }
}
