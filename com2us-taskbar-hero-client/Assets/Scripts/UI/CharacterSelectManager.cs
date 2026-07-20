using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using TaskbarHero.Client.Managers;
using TaskbarHero.Common.Dto;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 캐릭터 선택 화면 매니저. 캐릭터 클릭 시 해당 캐릭터로 카메라 줌인하고,
    /// 우측에 선택 패널을 띄운다. '뒤로'로 원래 뷰로 복귀한다.
    /// 클릭 감지는 Input System 포인터 + Physics2D.OverlapPoint(캐릭터 Collider2D)로 처리한다.
    /// </summary>
    public class CharacterSelectManager : MonoBehaviour
    {
        [Header("참조")]
        [SerializeField] private Camera cam;
        [SerializeField] private GameObject selectPanelPrefab;

        [Header("줌 설정")]
        [SerializeField] private float zoomSize = 3f;
        [SerializeField] private float zoomDuration = 0.4f;
        [Tooltip("줌인 시 선택 캐릭터가 위치할 화면 정규화 좌표(발밑 기준).")]
        [SerializeField] private Vector2 focusScreen = new Vector2(0.3f, 0.42f);

        private CharacterSelectPanelController _panel;
        private Vector3 _defaultCamPos;
        private float _defaultCamSize;
        private SelectableCharacter _selected;
        private Coroutine _zoomRoutine;
        private ScreenAnchoredWorldObject[] _anchors;
        private SelectableCharacter[] _characters;
        private GameObject _topUi;

        private void Awake()
        {
            if (cam == null)
            {
                cam = Camera.main;
            }

            _defaultCamPos = cam.transform.position;
            _defaultCamSize = cam.orthographicSize;
            _anchors = Object.FindObjectsByType<ScreenAnchoredWorldObject>(FindObjectsSortMode.None);
            _characters = Object.FindObjectsByType<SelectableCharacter>(FindObjectsSortMode.None);
            _topUi = GameObject.Find("TopUICanvas"); // 상단 로고(선택 중 숨김)

            if (selectPanelPrefab != null)
            {
                var go = Instantiate(selectPanelPrefab);
                _panel = go.GetComponent<CharacterSelectPanelController>();
                if (_panel != null)
                {
                    _panel.Backed += Deselect;
                    _panel.Selected += Confirm;
                    _panel.Show(false);
                }
            }
        }

        private void Update()
        {
            if (_selected != null)
            {
                return; // 선택 중에는 패널 버튼으로만 조작
            }

            var pointer = Pointer.current;
            if (pointer == null || !pointer.press.wasPressedThisFrame)
            {
                return;
            }

            Vector3 world = cam.ScreenToWorldPoint(pointer.position.ReadValue());
            var hit = Physics2D.OverlapPoint(world);
            if (hit != null)
            {
                var sc = hit.GetComponentInParent<SelectableCharacter>();
                if (sc != null)
                {
                    Select(sc);
                }
            }
        }

        private void Select(SelectableCharacter sc)
        {
            _selected = sc;
            SetAnchorsEnabled(false);      // 화면좌표 고정 해제(카메라 줌 반영)
            ShowOnlySelected(sc);          // 비선택 캐릭터 숨김
            if (_topUi != null) _topUi.SetActive(false); // 상단 로고 숨김

            // 선택한 캐릭터 고유의 공격 애니메이션 1회 재생(SPUM 헬퍼는 Assembly-CSharp이므로 SendMessage로 호출).
            sc.SendMessage("PlayAttackOnce", SendMessageOptions.DontRequireReceiver);

            if (_panel != null)
            {
                _panel.SetTitle(sc.DisplayName);
                _panel.Show(true);
            }

            if (_zoomRoutine != null) StopCoroutine(_zoomRoutine);
            _zoomRoutine = StartCoroutine(LerpCam(TargetPosFor(sc.transform.position), zoomSize));
        }

        private void Deselect()
        {
            _selected = null;
            if (_panel != null) _panel.Show(false);

            // 비선택 캐릭터는 여기서 켜지 않는다. 카메라가 원위치로 돌아온 뒤(RestoreCam 끝) 재활성화한다.
            if (_zoomRoutine != null) StopCoroutine(_zoomRoutine);
            _zoomRoutine = StartCoroutine(RestoreCam());
        }

        private void Confirm()
        {
            if (_selected == null || NetworkManager.Instance == null)
            {
                return;
            }

            // 계정 닉네임(회원가입/세이브에서 캐싱). 없으면 기본값.
            string nickname = string.IsNullOrEmpty(Session.Nickname) ? ("Hero" + Session.UserId) : Session.Nickname;

            var request = new CreateCharacterRequest
            {
                userId = Session.UserId,
                token = Session.Token,
                data = new CreateCharacterData { nickname = nickname, classCode = _selected.ClassCode },
            };

            Debug.Log($"[CharacterSelect] 캐릭터 생성 요청: class={_selected.ClassCode}({_selected.DisplayName}), nickname={nickname}");
            NetworkManager.Instance.PostToGame<ApiResponse>("/api/game/create-character", request, OnCreated, OnCreateError);
        }

        private void OnCreated(ApiResponse response)
        {
            // 생성 성공 → 생성된 캐릭터 포함 최신 세이브를 load로 다시 가져온다.
            Debug.Log("[CharacterSelect] 캐릭터 생성 성공 → 세이브 로드(/api/game/load)");
            var request = new AuthRequest { userId = Session.UserId, token = Session.Token };
            NetworkManager.Instance.PostToGame<LoadResponse>("/api/game/load", request, OnLoadedAfterCreate, OnCreateError);
        }

        private void OnLoadedAfterCreate(LoadResponse response)
        {
            // load로 받은 세이브 스냅샷을 캐싱하고, 성공한 뒤에 GameScene으로 전환한다.
            Session.SetGameData(response.data);
            int charCount = response.data != null && response.data.characters != null ? response.data.characters.Count : 0;
            Debug.Log($"[CharacterSelect] 세이브 로드 완료(캐릭터수={charCount}) → GameScene 전환");

            if (SceneManager.Instance != null)
            {
                SceneManager.Instance.LoadScene("GameScene");
            }
        }

        private void OnCreateError(NetworkError error)
        {
            Debug.LogWarning($"[CharacterSelect] 캐릭터 생성/로드 실패: {error}");
            if (_panel != null)
            {
                _panel.SetTitle(ErrorMessages.ToKorean(error));
            }
        }

        private Vector3 TargetPosFor(Vector3 charPos)
        {
            float aspect = cam.aspect;
            float camX = charPos.x - (focusScreen.x - 0.5f) * 2f * zoomSize * aspect;
            float camY = charPos.y - (focusScreen.y - 0.5f) * 2f * zoomSize;
            return new Vector3(camX, camY, _defaultCamPos.z);
        }

        private IEnumerator RestoreCam()
        {
            yield return LerpCam(_defaultCamPos, _defaultCamSize);
            SetAnchorsEnabled(true); // 복귀 후 화면좌표 고정 재활성

            // 카메라가 제 위치(기본 뷰)로 돌아온 뒤에 비선택 캐릭터와 상단 로고를 다시 활성화한다.
            foreach (var c in _characters)
            {
                if (c != null) c.gameObject.SetActive(true);
            }
            if (_topUi != null) _topUi.SetActive(true);
        }

        private IEnumerator LerpCam(Vector3 targetPos, float targetSize)
        {
            Vector3 startPos = cam.transform.position;
            float startSize = cam.orthographicSize;
            float elapsed = 0f;
            while (elapsed < zoomDuration)
            {
                elapsed += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / zoomDuration));
                cam.transform.position = Vector3.Lerp(startPos, targetPos, k);
                cam.orthographicSize = Mathf.Lerp(startSize, targetSize, k);
                yield return null;
            }
            cam.transform.position = targetPos;
            cam.orthographicSize = targetSize;
        }

        private void ShowOnlySelected(SelectableCharacter sc)
        {
            foreach (var c in _characters)
            {
                if (c != null) c.gameObject.SetActive(c == sc);
            }
        }

        private void SetAnchorsEnabled(bool value)
        {
            foreach (var a in _anchors)
            {
                if (a != null) a.enabled = value;
            }
        }
    }
}
