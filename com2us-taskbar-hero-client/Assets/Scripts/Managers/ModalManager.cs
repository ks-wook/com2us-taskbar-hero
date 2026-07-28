using System;
using UnityEngine;

namespace TaskbarHero.Client.Managers
{
    /// <summary>
    /// 클라이언트 전역 공용 모달(안내창) 매니저. 모달 프리팹 인스턴스를 1개만 생성·캐싱해 재사용하며
    /// 씬 전환에도 유지된다(DontDestroyOnLoad 싱글턴, UIManager와 동일 패턴). 어느 어셈블리(UI·Battle·Game)
    /// 에서도 <c>ModalManager.Instance.ShowConfirm(...)</c>로 호출한다. 프리팹은 <c>[SerializeField]</c>로
    /// 배선하며(Resources 미사용), Title·GameScene의 ModalManager에 지정한다.
    /// </summary>
    public class ModalManager : MonoBehaviour
    {
        public static ModalManager Instance { get; private set; }

        [Tooltip("Assets/Prefabs/UI/Modal 프리팹을 배선한다(Title·GameScene 양쪽).")]
        [SerializeField] private GameObject modalPrefab;

        private ModalController _modal;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // 이미 존재하는 싱글턴이 프리팹을 안 들고 있으면 이쪽 참조를 넘겨준다(씬별 배선 보전).
                if (Instance.modalPrefab == null && modalPrefab != null)
                {
                    Instance.modalPrefab = modalPrefab;
                }
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>'확인'만 있는 안내 모달을 표시한다.</summary>
        public void ShowConfirm(string title, string message, Action onOk = null)
        {
            var modal = GetOrCreate();
            if (modal == null) return;
            SoundManager.Sfx(SoundId.UiModalOpen);
            modal.ShowOk(title, message, onOk);
        }

        /// <summary>'확인'·'취소'가 있는 선택 모달을 표시한다.</summary>
        public void ShowConfirmCancel(string title, string message, Action onOk = null, Action onCancel = null)
        {
            var modal = GetOrCreate();
            if (modal == null) return;
            SoundManager.Sfx(SoundId.UiModalOpen);
            modal.ShowOkCancel(title, message, onOk, onCancel);
        }

        /// <summary>모달 인스턴스를 최초 1회 생성·캐싱해 반환한다(없으면 null 경고).</summary>
        private ModalController GetOrCreate()
        {
            if (_modal != null)
            {
                return _modal;
            }
            if (modalPrefab == null)
            {
                Debug.LogError("[ModalManager] Modal 프리팹이 배선되지 않았습니다.", this);
                return null;
            }
            var go = Instantiate(modalPrefab);
            go.name = modalPrefab.name;
            DontDestroyOnLoad(go);
            _modal = go.GetComponent<ModalController>();
            if (_modal == null)
            {
                Debug.LogError("[ModalManager] Modal 프리팹에 ModalController가 없습니다.", this);
            }
            return _modal;
        }
    }
}
