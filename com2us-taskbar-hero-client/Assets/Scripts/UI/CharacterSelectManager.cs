using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
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
        [Tooltip("좌측 상단 '뒤로가기' 버튼 배경(Assets/Art/UI/pixel_rpg_button.png, 9-slice). 없으면 단색 버튼.")]
        [SerializeField] private Sprite backButtonSprite;

        // ── 화면 배치 ───────────────────────────────────────────────────────────────
        // 좌: 큰 일러스트(주인공) + 그 아래 성별 버튼 │ 중: 선택 캐릭터(SPUM) │ 우: 정보 패널
        //
        // 일러스트와 캐릭터는 <b>겹쳐도 된다</b> — 캐릭터 정렬 순서를 올려 두어(SelectedSortingOrder) 위에 그려진다.
        // 반드시 지켜야 하는 선은 하나뿐이다:
        //   · <b>패널 텍스트 최좌측 x ≈ 0.756</b>(레이더의 '방어력' 라벨) — 캐릭터 오른쪽 끝이 이보다 커지면 글자를 덮는다.
        //   · 캐릭터 최대 우측 돌출(발 기준, zoomSize 3.4)은 0.185로 8종 중 기사(여)가 가장 크다.
        //   → focusScreen.x 상한 = 0.756 − 0.185 = <b>0.571</b>.
        // 캐릭터를 키우거나(zoomSize↓) 오른쪽으로 옮길 때 이 상한을 넘지 않는지 확인할 것.

        [Header("줌 설정")]
        [Tooltip("선택 시 카메라 orthographicSize. 값이 클수록 캐릭터가 작게 보인다.")]
        [SerializeField] private float zoomSize = 3.4f;
        [SerializeField] private float zoomDuration = 0.4f;
        [Tooltip("줌인 시 선택 캐릭터가 위치할 화면 정규화 좌표(발밑 기준). x 상한 0.571 — 위 배치 주석 참고.")]
        [SerializeField] private Vector2 focusScreen = new Vector2(0.551f, 0.20f);

        [Header("좌측 일러스트 · 성별 선택")]
        [Tooltip("캐릭터 일러스트의 화면 정규화 위치. 그림의 아래변 중앙이 이 지점에 온다.")]
        [SerializeField] private Vector2 illustrationScreen = new Vector2(0.306f, 0.140f);
        [Tooltip("일러스트 크기(캔버스 단위). 그림 비율은 preserveAspect로 유지되므로 실제 표시 크기는 높이(y)가 정한다.")]
        [SerializeField] private Vector2 illustrationSize = new Vector2(1098f, 710f);
        [Tooltip("성별 토글 줄의 화면 정규화 위치(줄의 상단 중앙 기준).")]
        [SerializeField] private Vector2 genderRowScreen = new Vector2(0.334f, 0.12f);

        private CharacterSelectPanelController _panel;
        private Vector3 _defaultCamPos;
        private float _defaultCamSize;
        private SelectableCharacter _selected;
        private Coroutine _zoomRoutine;
        private ScreenAnchoredWorldObject[] _anchors;
        private SelectableCharacter[] _characters;
        private GameObject _topUi;

        // 성별 선택(좌측 하단 토글). 선택 중인 직업의 외형만 바꾸며 스탯·비용에는 영향이 없다.
        private int _gender = (int)CharacterGender.Male;
        private GameObject _genderRoot;      // 토글 UI 캔버스(선택 중에만 노출)
        private Button _maleButton;
        private Button _femaleButton;
        private Image _illustration;         // 성별 버튼 위에 세우는 직업·성별 일러스트
        private GameObject _variant;         // 선택 캐릭터를 반대 성별 프리팹으로 교체해 띄운 인스턴스

        // 선택 중인 캐릭터를 우측 정보 패널 위에 그리기 위해 올려 둔 정렬 순서(해제 시 원래 값으로 복원).
        private UnityEngine.Rendering.SortingGroup _raisedSorting;
        private int _raisedSortingOrder;

        private void Awake()
        {
            // 캐릭터 생성 씬 전용 BGM + 모닥불 앰비언트(씬을 벗어나면 다음 씬이 BGM을 바꾼다).
            SoundManager.Bgm(SoundId.BgmCharacterCreate);
            SoundManager.Ambient(SoundId.AmbCampfireLoop);

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
                WirePanelCanvas(go);
                _panel = go.GetComponent<CharacterSelectPanelController>();
                if (_panel != null)
                {
                    _panel.Backed += Deselect;
                    _panel.Selected += Confirm;
                    _panel.Show(false);
                }
            }

            MarkOwnedCharacters(); // 이미 보유한 직업은 '선택불가' 표시 + 선택 차단

            // 게임 안(파티 편성 '+')에서 진입한 경우에만 좌측 상단 뒤로가기 버튼 노출(회원가입 직후 최초 생성은 미노출).
            if (Session.CreateCharacterFromGame)
            {
                CreateBackButton();
            }
        }

        /// <summary>
        /// 선택 패널 캔버스를 Screen Space - Camera로 살려낸다 — 씬 카메라를 연결하고 렌더 모드를 다시 지정한다.
        ///
        /// <para>프리팹은 씬 카메라를 참조할 수 없어 런타임 배선이 필요하다. 그리고 카메라가 비어 있는 동안
        /// <c>Canvas.renderMode</c>는 프리팹에 저장된 값과 무관하게 <b>Overlay로 읽힌다</b> — 그래서
        /// "카메라 모드일 때만 배선"하는 식의 조건 검사는 절대 통과하지 못한다(카메라를 넣어야 모드가 살아나는데
        /// 모드가 살아야 카메라를 넣는 순환). 조건 없이 카메라 → 모드 순서로 지정한다.
        /// 이 배선이 빠지면 패널이 Overlay로 그려져 정렬 순서와 무관하게 선택 캐릭터를 덮는다.</para>
        /// </summary>
        private void WirePanelCanvas(GameObject panelRoot)
        {
            var canvas = panelRoot.GetComponent<Canvas>();
            if (canvas == null)
            {
                return;
            }

            canvas.worldCamera = cam;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
        }

        /// <summary>좌측 상단 '뒤로가기' 버튼을 만들어 GameScene으로 복귀한다(게임 안에서 캐릭터 추가로 진입한 경우 전용).</summary>
        private void CreateBackButton()
        {
            var canvasGo = new GameObject("BackCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            var btnGo = new GameObject("BackButton", typeof(RectTransform), typeof(Image));
            btnGo.transform.SetParent(canvasGo.transform, false);
            var img = btnGo.GetComponent<Image>();
            ApplyButtonSprite(img);
            var rt = (RectTransform)btnGo.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); // 좌측 상단
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(40f, -40f);
            rt.sizeDelta = new Vector2(130f, 60f);

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(btnGo.transform, false);
            var t = labelGo.GetComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.text = "뒤로";
            t.fontSize = 26;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = Color.white;
            t.raycastTarget = false;
            var lrt = (RectTransform)labelGo.transform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = Vector2.zero;

            btnGo.AddComponent<Button>().onClick.AddListener(OnBack);
        }

        /// <summary>뒤로가기 버튼 배경에 공용 버튼 아트(pixel_rpg_button 9-slice)를 적용한다.
        /// 스프라이트가 배선되지 않았으면 기존 단색 배경으로 폴백한다.</summary>
        private void ApplyButtonSprite(Image img)
        {
            if (backButtonSprite == null)
            {
                img.color = new Color(0.20f, 0.22f, 0.30f, 0.95f);
                return;
            }
            img.sprite = backButtonSprite;
            img.type = Image.Type.Sliced; // 테두리 장식을 유지한 채 버튼 크기에 맞춰 늘린다
            img.color = Color.white;
        }

        /// <summary>뒤로가기: 게임 진입 플래그를 해제하고 진입한 화면(편성 씬 또는 GameScene)으로 돌아간다.</summary>
        private void OnBack()
        {
            Session.CreateCharacterFromGame = false;
            string target = ConsumeReturnScene();
            Debug.Log($"[CharacterSelect] 뒤로가기 → {target} 복귀");
            if (SceneManager.Instance != null)
            {
                SceneManager.Instance.LoadScene(target);
            }
        }

        /// <summary>생성 완료·뒤로가기 후 돌아갈 씬 이름을 꺼내고(기본 GameScene) 복귀 지점을 초기화한다.</summary>
        private static string ConsumeReturnScene()
        {
            string target = string.IsNullOrEmpty(Session.CreateCharacterReturnScene)
                ? "GameScene" : Session.CreateCharacterReturnScene;
            Session.CreateCharacterReturnScene = "GameScene";
            return target;
        }

        /// <summary>계정이 이미 보유한 직업의 캐릭터 위에 빨간 '선택불가' 라벨을 띄운다.</summary>
        private void MarkOwnedCharacters()
        {
            if (_characters == null)
            {
                return;
            }
            foreach (var sc in _characters)
            {
                if (sc != null && IsClassOwned(sc.ClassCode))
                {
                    CreateLockedLabel(sc);
                }
            }
        }

        /// <summary>직업 설명(class_master.description). 마스터 데이터에 없으면 빈 문자열을 돌려준다.</summary>
        private static string ClassDescriptionOf(int classCode)
        {
            MasterDataManager.EnsureLoaded();
            var db = MasterDataManager.Db;
            if (db != null && db.Classes.TryGetValue(classCode, out var cls) && cls != null)
            {
                return cls.description;
            }
            return string.Empty;
        }

        /// <summary>계정이 해당 직업 캐릭터를 이미 보유했는지(세션 세이브 기준).</summary>
        private static bool IsClassOwned(int classCode)
        {
            var chars = Session.GameData != null ? Session.GameData.characters : null;
            if (chars != null)
            {
                foreach (var c in chars)
                {
                    if (c != null && c.classCode == classCode)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>캐릭터 머리 위에 빨간 '선택불가' 월드 텍스트를 생성한다(캐릭터에 종속되어 함께 표시/숨김).</summary>
        private void CreateLockedLabel(SelectableCharacter sc)
        {
            var go = new GameObject("LockedLabel");
            go.transform.position = sc.transform.position + Vector3.up * 2.8f;
            go.transform.SetParent(sc.transform, worldPositionStays: true); // 부모 스케일 영향 없이 월드 크기 유지

            var tm = go.AddComponent<TextMesh>();
            tm.text = "선택불가";
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontSize = 80;
            tm.characterSize = 0.08f;
            tm.color = new Color(1f, 0.2f, 0.2f);
            tm.fontStyle = FontStyle.Bold;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.font = font;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = font.material;
            mr.sortingOrder = 500; // 캐릭터 스프라이트 위
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
            if (IsClassOwned(sc.ClassCode))
            {
                Debug.Log($"[CharacterSelect] 이미 보유한 직업(class={sc.ClassCode}) — 선택 불가");
                return; // 이미 보유한 직업은 선택 차단
            }
            _selected = sc;
            SoundManager.Sfx(SoundId.CharFocus); // 직업 슬롯 선택음(사운드 정의서 §4.2)
            SetAnchorsEnabled(false);      // 화면좌표 고정 해제(카메라 줌 반영)
            ShowOnlySelected(sc);          // 비선택 캐릭터 숨김
            if (_topUi != null) _topUi.SetActive(false); // 상단 로고 숨김

            // 성별은 클릭한 프리팹의 외형 성별에서 시작한다(선택 직후 외형이 튀지 않도록).
            _gender = sc.Gender > 0 ? sc.Gender : (int)CharacterGender.Male;
            RaiseCharacterSorting(sc.gameObject, remember: true); // 우측 정보 패널 위에 세운다
            ShowGenderToggle(true);

            // 공격 애니메이션은 여기서 재생하지 않는다 — 성별을 바꿀 때만 1회 재생한다(ApplyGenderAppearance).

            if (_panel != null)
            {
                _panel.SetTitle(sc.DisplayName);
                _panel.SetDescription(ClassDescriptionOf(sc.ClassCode));
                _panel.SetStats(sc.ClassCode); // class_master 기본 능력치(체력·공격·방어·공격속도 등) 표시
                _panel.Show(true);
            }

            if (_zoomRoutine != null) StopCoroutine(_zoomRoutine);
            _zoomRoutine = StartCoroutine(LerpCam(TargetPosFor(sc.transform.position), zoomSize));
        }

        private void Deselect()
        {
            _selected = null;
            RestoreCharacterSorting();
            ShowGenderToggle(false);
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
            int classCode = _selected.ClassCode;
            int gender = _gender;
            string display = $"{_selected.DisplayName}({GenderLabel(gender)})";

            // 이번에 생성될 슬롯 = 기존 캐릭터 수 + 1. 2·3번은 골드 비용(character_create_cost), 1번은 무료.
            int existing = (Session.GameData != null && Session.GameData.characters != null) ? Session.GameData.characters.Count : 0;
            int nextSlot = existing + 1;
            MasterDataManager.EnsureLoaded();
            long cost = MasterDataManager.Db != null ? MasterDataManager.Db.CharacterCreateCostOf(nextSlot) : 0L;

            // 비용이 있으면(2·3번) 확인 모달로 소모 골드를 안내하고, 무료(1번)면 바로 생성한다.
            if (cost > 0 && ModalManager.Instance != null)
            {
                ModalManager.Instance.ShowConfirmCancel(
                    "캐릭터 생성",
                    $"{display} 캐릭터를 생성합니다.\n소모 골드: {GoldFormat.Highlight(cost)}\n생성하시겠습니까?",
                    () => DoCreate(nickname, classCode, gender));
            }
            else
            {
                DoCreate(nickname, classCode, gender);
            }
        }

        /// <summary>
        /// 실제 캐릭터 생성 요청(확인 모달의 '확인' 콜백 또는 무료 생성). 서버가 비용 차감·검증한다(서버 권위).
        /// 성별(1:남 2:여)은 외형 전용 값이라 스탯·비용에 영향이 없고, 생성 이후에는 변경할 수 없다.
        /// </summary>
        private void DoCreate(string nickname, int classCode, int gender)
        {
            if (NetworkManager.Instance == null)
            {
                return;
            }
            var request = new CreateCharacterRequest
            {
                userId = Session.UserId,
                token = Session.Token,
                data = new CreateCharacterData { nickname = nickname, classCode = classCode, gender = gender },
            };

            Debug.Log($"[CharacterSelect] 캐릭터 생성 요청: class={classCode}, gender={gender}, nickname={nickname}");
            NetworkManager.Instance.PostToGame<ApiResponse>("/api/game/create-character", request, OnCreated, OnCreateError);
        }

        private void OnCreated(ApiResponse response)
        {
            // 생성 성공 → 생성된 캐릭터 포함 최신 세이브를 load로 다시 가져온다.
            Debug.Log("[CharacterSelect] 캐릭터 생성 성공 → 세이브 로드(/api/game/load)");
            SoundManager.Sfx(SoundId.CharCreate); // 캐릭터 생성 성공음(§4.2)
            // 2번째 이후 캐릭터는 골드를 소모하므로 차감음을 함께 울린다(§6).
            if (Session.GameData != null && Session.GameData.characters != null &&
                Session.GameData.characters.Count >= 1)
            {
                SoundManager.Sfx(SoundId.GoldSpend);
            }
            var request = new AuthRequest { userId = Session.UserId, token = Session.Token };
            NetworkManager.Instance.PostToGame<LoadResponse>("/api/game/load", request, OnLoadedAfterCreate, OnCreateError);
        }

        private void OnLoadedAfterCreate(LoadResponse response)
        {
            // load로 받은 세이브 스냅샷을 캐싱하고, 성공한 뒤에 GameScene으로 전환한다.
            Session.SetGameData(response.data);
            int charCount = response.data != null && response.data.characters != null ? response.data.characters.Count : 0;

            Session.CreateCharacterFromGame = false; // 진입 플래그 정리
            string target = ConsumeReturnScene();    // 편성 씬에서 진입했다면 그 씬으로 복귀
            Debug.Log($"[CharacterSelect] 세이브 로드 완료(캐릭터수={charCount}) → {target} 전환");
            if (SceneManager.Instance != null)
            {
                SceneManager.Instance.LoadScene(target);
            }
        }

        private void OnCreateError(NetworkError error)
        {
            Debug.LogWarning($"[CharacterSelect] 캐릭터 생성/로드 실패: {error}");
            if (ModalManager.Instance != null)
            {
                ModalManager.Instance.ShowConfirm("캐릭터 생성 실패", ErrorMessages.ToKorean(error));
            }
            else if (_panel != null)
            {
                _panel.SetTitle(ErrorMessages.ToKorean(error));
            }
        }

        // ---- 성별 선택(캐릭터 발밑 토글) ----

        /// <summary>성별 값을 화면 표기용 문구로 바꾼다(1:남 2:여).</summary>
        private static string GenderLabel(int gender)
        {
            return gender == (int)CharacterGender.Female ? "여" : "남";
        }

        /// <summary>화면 좌측 하단에 '남 / 여' 토글 UI와 그 위의 캐릭터 일러스트를 만든다(최초 1회 생성 후 재사용).</summary>
        private void EnsureGenderToggle()
        {
            if (_genderRoot != null)
            {
                return;
            }

            var canvasGo = new GameObject("GenderToggleCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 210;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            // 일러스트를 먼저 만들어 성별 버튼보다 뒤에 그린다. 둘 다 각자의 화면 정규화 좌표에 고정하므로
            // 화면 비율이 바뀌어도 좌측 구도가 유지된다.
            _illustration = CreateIllustration(canvasGo.transform);

            var rowGo = new GameObject("GenderRow", typeof(RectTransform));
            rowGo.transform.SetParent(canvasGo.transform, false);
            var rowRt = (RectTransform)rowGo.transform;
            rowRt.anchorMin = rowRt.anchorMax = genderRowScreen;
            rowRt.pivot = new Vector2(0.5f, 1f);
            rowRt.anchoredPosition = Vector2.zero;
            rowRt.sizeDelta = new Vector2(300f, 76f);

            _maleButton = CreateGenderButton(rowRt, "MaleButton", GenderLabel((int)CharacterGender.Male),
                new Vector2(-76f, 0f), (int)CharacterGender.Male);
            _femaleButton = CreateGenderButton(rowRt, "FemaleButton", GenderLabel((int)CharacterGender.Female),
                new Vector2(76f, 0f), (int)CharacterGender.Female);

            _genderRoot = canvasGo;
        }

        /// <summary>
        /// 화면 좌측에 선택 캐릭터 일러스트를 띄울 Image를 만든다(그림은 선택·성별 전환 때 채운다).
        /// 그림의 <b>아래변 중앙</b>(pivot y=0)을 <see cref="illustrationScreen"/>에 맞추므로,
        /// 그림마다 세로 비율이 달라도 발밑 선이 일정하게 유지된다.
        /// </summary>
        private Image CreateIllustration(Transform parent)
        {
            var go = new GameObject("Illustration", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.transform.SetAsFirstSibling(); // 성별 버튼보다 먼저(=뒤에) 그린다

            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = illustrationScreen;
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = illustrationSize;

            var image = go.GetComponent<Image>();
            image.preserveAspect = true; // 일러스트가 가로로 늘어나지 않게
            image.raycastTarget = false; // 뒤쪽 캐릭터 클릭을 막지 않게
            image.enabled = false;       // 스프라이트를 채우기 전까지는 흰 사각형이 보이지 않게
            return image;
        }

        /// <summary>
        /// 선택한 직업·성별에 해당하는 일러스트를 좌측에 표시한다.
        /// 등록된 그림이 없으면(일러스트 DB 미배선) 이미지를 꺼서 빈 사각형이 남지 않게 한다.
        /// </summary>
        private void UpdateIllustration()
        {
            if (_illustration == null)
            {
                return;
            }

            if (_selected != null &&
                CharacterIllustrationDatabase.TryGetIllustration(_selected.ClassCode, _gender, out var entry))
            {
                _illustration.sprite = entry.sprite;
                _illustration.enabled = true;
                return;
            }

            _illustration.sprite = null;
            _illustration.enabled = false;
        }

        /// <summary>성별 토글 버튼 1개를 만든다(배경 이미지 + 라벨, 클릭 시 그 성별로 전환).</summary>
        private Button CreateGenderButton(RectTransform parent, string name, string label, Vector2 pos, int gender)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(140f, 68f);

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(go.transform, false);
            var text = labelGo.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = label;
            text.fontSize = 34;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            var lrt = (RectTransform)labelGo.transform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = Vector2.zero;

            var button = go.AddComponent<Button>();
            // 전역 클릭음(sfx_ui_click)에서 제외한다 — 성별 전환은 이 화면 '뒤로' 버튼과 같은
            // 소리(sfx_ui_click_back)를 SetGender에서 직접 재생한다(두 소리가 겹치지 않게).
            UiClickSound.Suppress(button);
            button.onClick.AddListener(() => SetGender(gender));
            return button;
        }

        /// <summary>성별 토글 UI와 일러스트를 표시/숨김한다(캐릭터 선택 중에만 노출).</summary>
        private void ShowGenderToggle(bool visible)
        {
            if (visible)
            {
                EnsureGenderToggle();
                UpdateGenderButtons();
                UpdateIllustration();
            }
            if (_genderRoot != null)
            {
                _genderRoot.SetActive(visible);
            }
        }

        /// <summary>선택 성별을 바꾸고(같은 값이면 무시) 캐릭터 외형과 좌측 일러스트를 그 성별로 교체한다.
        /// 소리는 이 화면 <b>'뒤로' 버튼과 같은 소리</b>(<see cref="SoundId.UiClickBack"/>)를 낸다 —
        /// 같은 성별을 다시 눌러 아무것도 바뀌지 않을 때는 소리도 내지 않는다.</summary>
        private void SetGender(int gender)
        {
            if (_selected == null || _gender == gender)
            {
                return;
            }
            SoundManager.Sfx(SoundId.UiClickBack); // 성별 전환 = '뒤로' 버튼과 같은 소리(사운드 정의서 §4.1)
            _gender = gender;
            UpdateGenderButtons();
            UpdateIllustration();
            ApplyGenderAppearance();
        }

        /// <summary>
        /// 선택 중인 캐릭터의 정렬 순서. 우측 정보 패널 캔버스(sortingOrder 0)보다 커야 패널 위에 그려진다
        /// (배경 캔버스 -100 &lt; 패널 0 &lt; 선택 캐릭터 100 &lt; 모달·로딩 Overlay).
        /// </summary>
        private const int SelectedSortingOrder = 100;

        /// <summary>'남' 버튼 배경색(파랑).</summary>
        private static readonly Color MaleButtonColor = new Color(0.22f, 0.45f, 0.88f, 0.98f);

        /// <summary>'여' 버튼 배경색(빨강).</summary>
        private static readonly Color FemaleButtonColor = new Color(0.86f, 0.26f, 0.30f, 0.98f);

        /// <summary>성별 버튼을 각자의 고유색(남=파랑·여=빨강)으로 칠하고, 선택 여부로 명도를 나눈다.</summary>
        private void UpdateGenderButtons()
        {
            PaintGenderButton(_maleButton, _gender == (int)CharacterGender.Male, MaleButtonColor);
            PaintGenderButton(_femaleButton, _gender == (int)CharacterGender.Female, FemaleButtonColor);
        }

        /// <summary>
        /// 성별 버튼 한 개를 칠한다. 배경은 항상 그 성별의 고유색이며, 비선택 쪽은 같은 색을 어둡게 낮춰
        /// 어느 쪽이 선택되어 있는지 구분되게 한다.
        /// </summary>
        private static void PaintGenderButton(Button button, bool selected, Color genderColor)
        {
            if (button == null)
            {
                return;
            }
            var image = button.GetComponent<Image>();
            if (image != null)
            {
                const float dim = 0.34f; // 비선택 쪽 명도 배율
                image.color = selected
                    ? genderColor
                    : new Color(genderColor.r * dim, genderColor.g * dim, genderColor.b * dim, 0.95f);
            }
            var text = button.GetComponentInChildren<Text>();
            if (text != null)
            {
                text.color = selected ? Color.white : new Color(0.78f, 0.78f, 0.82f);
            }
        }

        /// <summary>
        /// 선택 중인 캐릭터를 현재 성별 프리팹으로 교체해 보여준다.
        /// 클릭한 프리팹과 같은 성별이면 원래 인스턴스를 복원하고, 다르면 같은 위치·크기(좌우 반전 포함)로
        /// 반대 성별 프리팹을 띄운 뒤 원래 인스턴스를 숨긴다. 등록된 프리팹이 없으면 외형을 바꾸지 않는다.
        /// 교체된(또는 복원된) 외형으로 공격 애니메이션을 1회 재생한다.
        /// </summary>
        private void ApplyGenderAppearance()
        {
            if (_selected == null)
            {
                return;
            }

            if (_gender == _selected.Gender)
            {
                ClearVariant();
                PlayAttackOnce(_selected.gameObject);
                return;
            }

            var prefab = CharacterPrefabDatabase.PrefabOf(_selected.ClassCode, _gender);
            if (prefab == null)
            {
                Debug.LogWarning($"[CharacterSelect] 성별 프리팹이 없어 외형을 유지합니다(class={_selected.ClassCode}, gender={_gender}).");
                return;
            }

            ClearVariant(restoreOriginal: false);
            var t = _selected.transform;
            _variant = Instantiate(prefab, t.position, t.rotation);
            _variant.transform.localScale = t.localScale; // SPUM은 scale.x 부호로 좌우를 뒤집는다
            RaiseCharacterSorting(_variant, remember: false); // 교체 외형도 패널 위에 그린다(임시라 복원 불필요)
            _selected.gameObject.SetActive(false);
            PlayAttackOnce(_variant);
        }

        /// <summary>
        /// 캐릭터 외형의 정렬 순서를 <see cref="SelectedSortingOrder"/>로 올려 우측 정보 패널
        /// (Screen Space - Camera 캔버스, sortingOrder 0) 위에 그려지게 한다.
        /// remember면 원래 값을 기억해 뒀다가 선택 해제 시 <see cref="RestoreCharacterSorting"/>으로 되돌린다.
        /// </summary>
        private void RaiseCharacterSorting(GameObject character, bool remember)
        {
            if (character == null)
            {
                return;
            }

            var group = character.GetComponentInChildren<UnityEngine.Rendering.SortingGroup>(includeInactive: true);
            if (group == null)
            {
                return;
            }

            if (remember)
            {
                _raisedSorting = group;
                _raisedSortingOrder = group.sortingOrder;
            }
            group.sortingOrder = SelectedSortingOrder;
        }

        /// <summary>올려 뒀던 캐릭터 정렬 순서를 원래 값으로 되돌린다(선택 해제 시).</summary>
        private void RestoreCharacterSorting()
        {
            if (_raisedSorting != null)
            {
                _raisedSorting.sortingOrder = _raisedSortingOrder;
                _raisedSorting = null;
            }
        }

        /// <summary>캐릭터 외형의 공격 애니메이션을 1회 재생한다(SPUM 헬퍼는 Assembly-CSharp이라 SendMessage로 호출).</summary>
        private static void PlayAttackOnce(GameObject character)
        {
            if (character == null)
            {
                return;
            }
            character.SendMessage("PlayAttackOnce", SendMessageOptions.DontRequireReceiver);
        }

        /// <summary>성별 교체용 임시 인스턴스를 제거한다(restoreOriginal이면 원래 캐릭터를 다시 켠다).</summary>
        private void ClearVariant(bool restoreOriginal = true)
        {
            if (_variant != null)
            {
                Destroy(_variant);
                _variant = null;
            }
            if (restoreOriginal && _selected != null)
            {
                _selected.gameObject.SetActive(true);
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

            // 성별 교체 인스턴스는 줌아웃이 끝난 뒤 정리한다(줌아웃 중에도 고른 외형이 그대로 보이도록).
            ClearVariant(restoreOriginal: false);

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
