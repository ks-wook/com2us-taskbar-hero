using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TaskbarHero.Client.Managers;
using TaskbarHero.Client.MasterData;
using TaskbarHero.Common.Dto;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 파티 편성 패널. 3개 슬롯으로 현재 계정의 파티(캐릭터) 상태를 보여준다.
    /// 캐릭터가 있는 슬롯은 직업·레벨을 표시하고, 빈 슬롯은 '+' 버튼을 노출해
    /// 누르면 캐릭터 생성 씬(CreateCharacterScene)으로 이동한다.
    /// 계층은 에디터 빌드 시 프리팹에 정적 저장되고, 표시될 때마다 세션 실데이터로 갱신한다.
    /// </summary>
    public class PartyPanelController : MonoBehaviour
    {
        private const int MaxSlots = 3;
        private const float CanvasRefWidth = 1080f;
        private const float CanvasRefHeight = 1920f;

        [SerializeField] private List<RectTransform> _slots = new List<RectTransform>();
        [SerializeField] private Button _closeButton;
        [SerializeField] private Button _dimButton;
        // 슬롯별 구성(에디터 빌드로 baked): 캐릭터 프리팹 렌더(RawImage)·직업/레벨 캡션·'+' 추가 버튼 루트.
        [SerializeField] private List<RawImage> _slotRenders = new List<RawImage>();
        [SerializeField] private List<Text> _slotCaptions = new List<Text>();
        [SerializeField] private List<GameObject> _slotAddRoots = new List<GameObject>();
        [SerializeField] private List<Button> _slotAddButtons = new List<Button>();
        [Header("직업별 캐릭터 프리팹(classCode → 프리팹, 에디터 빌더가 배선: 기사1·레인저2·마법사3)")]
        [SerializeField] private List<ClassCharacter> _classCharacters = new List<ClassCharacter>();
        [Header("배경")]
        [Tooltip("Assets/Art/UI/modal_bg를 배선한다(에디터 빌더). 없으면 단색 배경.")]
        [SerializeField] private Sprite _backgroundSprite;

        /// <summary>초상화에 렌더할 직업별 캐릭터 프리팹 매핑(classCode → 프리팹).</summary>
        [System.Serializable]
        private struct ClassCharacter
        {
            public int classCode;
            public GameObject prefab;
        }

        // 초상화 렌더 설정(인벤토리와 동일 방식). 슬롯마다 화면 밖 격리 위치에 캐릭터를 두고 전용 카메라로 렌더한다.
        private const int PortraitLayer = 28; // 인벤토리와 공유(패널 상호 배타), 전투 초상(29~31)과 비겹침
        private const float PortraitOrtho = 0.7f;
        private static readonly Vector2 PortraitAim = new Vector2(0f, 0.45f);
        private static readonly Color PortraitBg = new Color(0.10f, 0.11f, 0.16f, 1f);

        private CharacterPortrait[] _portraits;
        private GameObject[] _portraitStages;
        private int[] _portraitClassCode;

        private Font _font;
        private bool AlreadyBuilt => _slots != null && _slots.Count == MaxSlots && _slots[0] != null;

        private void Awake()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (!AlreadyBuilt)
            {
                Construct();
            }
            WireRuntime();
        }

        private void OnEnable()
        {
            if (AlreadyBuilt)
            {
                EnsurePortraits();
                Refresh();
            }
        }

        /// <summary>패널이 숨겨지면 초상화 렌더러(카메라)를 꺼 불필요한 렌더를 막는다.</summary>
        private void OnDisable()
        {
            if (_portraitStages != null)
            {
                foreach (var s in _portraitStages)
                {
                    if (s != null) s.SetActive(false);
                }
            }
        }

        /// <summary>파괴 시 화면 밖 초상화 스테이지(카메라·RT·캐릭터 인스턴스)를 함께 정리한다.</summary>
        private void OnDestroy()
        {
            if (_portraitStages != null)
            {
                foreach (var s in _portraitStages)
                {
                    if (s != null) Destroy(s);
                }
                _portraitStages = null;
            }
        }

        /// <summary>에디터 빌드 전용: 전체 계층 생성.</summary>
        public void EditorConstruct() => Construct();

        // ── 구성 ──

        private void Construct()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildCanvas();
            BuildDim();
            var panel = BuildPanel();
            BuildHeader(panel);
            BuildSlots(panel);
        }

        private void BuildCanvas()
        {
            var canvas = gameObject.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = gameObject.AddComponent<Canvas>();
            }
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
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
        }

        private void BuildDim()
        {
            var img = NewImage("Dim", (RectTransform)transform, new Color(0f, 0f, 0f, 0f)); // 배경을 어둡게 하지 않는다 — 밖 클릭 닫기용 투명 차단막
            Stretch(img.rectTransform);
            _dimButton = img.gameObject.AddComponent<Button>();
            _dimButton.transition = Selectable.Transition.None;
        }

        private RectTransform BuildPanel()
        {
            var img = NewImage("PartyPanelRoot", (RectTransform)transform, new Color(0.10f, 0.12f, 0.18f, 0.98f));
            ApplyBackground(img);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(760f, 460f);
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        private void BuildHeader(RectTransform panel)
        {
            var title = NewText("Title", panel, "편성", 40, TextAnchor.MiddleCenter);
            var trt = title.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.anchoredPosition = new Vector2(0f, -20f);
            trt.sizeDelta = new Vector2(400f, 56f);

            var close = NewImage("CloseButton", panel, new Color(0.25f, 0.28f, 0.4f, 1f));
            var crt = close.rectTransform;
            crt.anchorMin = crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(1f, 1f);
            crt.anchoredPosition = new Vector2(-16f, -16f);
            crt.sizeDelta = new Vector2(60f, 60f);
            var xt = NewText("X", close.rectTransform, "X", 32, TextAnchor.MiddleCenter);
            Stretch(xt.rectTransform);
            _closeButton = close.gameObject.AddComponent<Button>();
        }

        private void BuildSlots(RectTransform panel)
        {
            _slots.Clear();
            _slotRenders.Clear();
            _slotCaptions.Clear();
            _slotAddRoots.Clear();
            _slotAddButtons.Clear();
            const float slotW = 200f;
            const float slotH = 300f;
            const float gap = 24f;
            float totalW = MaxSlots * slotW + (MaxSlots - 1) * gap;
            float startX = -totalW * 0.5f + slotW * 0.5f;

            for (int i = 0; i < MaxSlots; i++)
            {
                var slot = NewImage($"PartySlot{i}", panel, new Color(0.15f, 0.17f, 0.26f, 1f));
                var rt = slot.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(slotW, slotH);
                rt.anchoredPosition = new Vector2(startX + i * (slotW + gap), -20f);
                _slots.Add(rt);

                // 캐릭터 프리팹 렌더(RawImage). 상단 대부분을 채우고 하단은 캡션 공간. 텍스처는 런타임 CharacterPortrait가 배정.
                var render = NewRawImage("Render", rt);
                render.rectTransform.anchorMin = Vector2.zero;
                render.rectTransform.anchorMax = Vector2.one;
                render.rectTransform.offsetMin = new Vector2(10f, 44f);
                render.rectTransform.offsetMax = new Vector2(-10f, -10f);
                render.color = new Color(1f, 1f, 1f, 0f); // 캐릭터 배정 전 투명
                render.raycastTarget = false;
                render.gameObject.SetActive(false);
                _slotRenders.Add(render);

                // 직업 · 레벨 캡션(하단)
                var cap = NewText("Caption", rt, "", 26, TextAnchor.MiddleCenter);
                cap.fontStyle = FontStyle.Bold;
                PlaceCenter(cap.rectTransform, 0.5f, 0.09f, 190f, 40f);
                cap.gameObject.SetActive(false);
                _slotCaptions.Add(cap);

                // 빈 슬롯 '+' 추가 버튼 루트
                var addRoot = new GameObject("AddRoot", typeof(RectTransform));
                addRoot.transform.SetParent(rt, false);
                Stretch((RectTransform)addRoot.transform);
                var plus = NewImage("AddButton", addRoot.transform, new Color(0.25f, 0.55f, 0.35f, 1f));
                PlaceCenter(plus.rectTransform, 0.5f, 0.55f, 110f, 110f);
                var pt = NewText("Plus", plus.rectTransform, "＋", 64, TextAnchor.MiddleCenter);
                Stretch(pt.rectTransform);
                var addCap = NewText("AddCaption", addRoot.transform, "캐릭터 추가", 24, TextAnchor.MiddleCenter);
                addCap.color = new Color(0.7f, 0.85f, 0.7f);
                PlaceCenter(addCap.rectTransform, 0.5f, 0.22f, 190f, 36f);
                var addBtn = plus.gameObject.AddComponent<Button>();
                addRoot.SetActive(false);
                _slotAddRoots.Add(addRoot);
                _slotAddButtons.Add(addBtn);
            }
        }

        private void WireRuntime()
        {
            if (_closeButton != null)
            {
                _closeButton.onClick.AddListener(Close);
            }
            if (_dimButton != null)
            {
                _dimButton.onClick.AddListener(Close);
            }
            if (_slotAddButtons != null)
            {
                foreach (var b in _slotAddButtons)
                {
                    if (b != null)
                    {
                        b.onClick.AddListener(OnAddCharacter);
                    }
                }
            }
        }

        /// <summary>슬롯별 초상화 렌더러(전용 카메라+RT를 얹은 화면 밖 오브젝트)를 1회 생성한다(런타임 전용).</summary>
        private void EnsurePortraits()
        {
            if (!Application.isPlaying || _slotRenders == null || _slotRenders.Count < MaxSlots)
            {
                return;
            }
            if (_portraits != null)
            {
                foreach (var s in _portraitStages)
                {
                    if (s != null) s.SetActive(true);
                }
                return;
            }
            _portraits = new CharacterPortrait[MaxSlots];
            _portraitStages = new GameObject[MaxSlots];
            _portraitClassCode = new int[MaxSlots];
            for (int i = 0; i < MaxSlots; i++)
            {
                _portraitClassCode[i] = -2; // 첫 Refresh에서 반드시 배정되도록
                var origin = new Vector3(700f + i * 40f, 700f, 0f);
                var stage = new GameObject($"PartyPortraitStage{i}");
                stage.transform.position = origin;
                var p = stage.AddComponent<CharacterPortrait>();
                p.Initialize(_slotRenders[i], PortraitLayer, 300, 400, PortraitOrtho, PortraitAim, PortraitBg, origin);
                _portraits[i] = p;
                _portraitStages[i] = stage;
            }
        }

        // ── 갱신(세션 실데이터) ──

        /// <summary>각 슬롯을 현재 파티로 갱신한다. 캐릭터가 있으면 프리팹 렌더 + 직업·레벨 캡션, 없으면 '+' 버튼.</summary>
        private void Refresh()
        {
            var chars = Session.GameData != null ? Session.GameData.characters : null;
            var db = MasterDataManager.Db;

            for (int i = 0; i < _slots.Count && i < MaxSlots; i++)
            {
                CharacterDto c = chars != null && i < chars.Count ? chars[i] : null;
                var render = i < _slotRenders.Count ? _slotRenders[i] : null;
                var cap = i < _slotCaptions.Count ? _slotCaptions[i] : null;
                var addRoot = i < _slotAddRoots.Count ? _slotAddRoots[i] : null;

                if (c != null)
                {
                    if (addRoot != null) addRoot.SetActive(false);

                    var prefab = PrefabForClass(c.classCode, c.gender);
                    int portraitKey = PortraitKeyOf(c.classCode, c.gender);
                    if (_portraits != null && i < _portraits.Length && _portraits[i] != null && _portraitClassCode[i] != portraitKey)
                    {
                        _portraits[i].SetCharacter(prefab);
                        _portraitClassCode[i] = portraitKey;
                    }
                    if (render != null)
                    {
                        render.gameObject.SetActive(true);
                        render.color = prefab != null ? Color.white : new Color(1f, 1f, 1f, 0f);
                    }
                    if (cap != null)
                    {
                        string cls = db != null && db.Classes.TryGetValue(c.classCode, out var cm) ? cm.name : $"직업 {c.classCode}";
                        cap.gameObject.SetActive(true);
                        cap.text = $"{cls} Lv.{c.level}";
                    }
                }
                else
                {
                    if (_portraits != null && i < _portraits.Length && _portraits[i] != null && _portraitClassCode[i] != -1)
                    {
                        _portraits[i].SetCharacter(null);
                        _portraitClassCode[i] = -1;
                    }
                    if (render != null) render.gameObject.SetActive(false);
                    if (cap != null) cap.gameObject.SetActive(false);
                    if (addRoot != null) addRoot.SetActive(true);
                }
            }
        }

        /// <summary>초상화 캐시 키(직업+성별). 성별이 다르면 다른 외형이므로 프리팹을 다시 심어야 한다.</summary>
        private static int PortraitKeyOf(int classCode, int gender)
        {
            return classCode * 10 + gender;
        }

        /// <summary>
        /// 직업·성별에 해당하는 초상화 캐릭터 프리팹을 반환한다(없으면 null).
        /// 공용 <see cref="CharacterPrefabDatabase"/>(Resources)를 먼저 보고, 없으면 인스펙터에 배선된
        /// 직업별 프리팹 목록으로 폴백한다(성별 구분 없음).
        /// </summary>
        private GameObject PrefabForClass(int classCode, int gender)
        {
            var fromDb = CharacterPrefabDatabase.PrefabOf(classCode, gender);
            if (fromDb != null)
            {
                return fromDb;
            }
            if (_classCharacters != null)
            {
                foreach (var e in _classCharacters)
                {
                    if (e.prefab != null && e.classCode == classCode)
                    {
                        return e.prefab;
                    }
                }
            }
            return null;
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

        /// <summary>'+' 클릭: 다음 캐릭터 생성 비용을 모달로 안내하고, 골드가 부족하면 차단(요청·이동 안 함).
        /// 충분하면 확인 시 CreateCharacterScene으로 이동한다(생성·차감은 그 씬에서 서버 권위로 처리).</summary>
        private void OnAddCharacter()
        {
            MasterDataManager.EnsureLoaded();
            var db = MasterDataManager.Db;
            int existing = Session.GameData != null && Session.GameData.characters != null ? Session.GameData.characters.Count : 0;
            int nextSlot = existing + 1;
            long cost = db != null ? db.CharacterCreateCostOf(nextSlot) : 0L;
            long gold = CurrentGold();

            // 골드 부족 → 차단(서버 요청·씬 이동 없이 안내만).
            if (cost > 0 && gold < cost)
            {
                if (ModalManager.Instance != null)
                {
                    ModalManager.Instance.ShowConfirm("캐릭터 추가",
                        $"골드가 부족합니다.\n필요 골드: {GoldFormat.Highlight(cost)}\n보유 골드: {GoldFormat.Highlight(gold)}");
                }
                Debug.Log($"[Party] 캐릭터 추가 차단(골드 부족): 필요 {cost}, 보유 {gold}");
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

        /// <summary>캐릭터 생성 씬으로 이동한다(게임 안 진입 표시 → 그 씬에서 뒤로가기 노출).</summary>
        private void GoToCreateScene()
        {
            Session.CreateCharacterFromGame = true; // CreateCharacterScene에서 '뒤로가기'로 GameScene 복귀 허용
            Debug.Log("[Party] 캐릭터 추가 → CreateCharacterScene 이동");
            if (SceneManager.Instance != null)
            {
                SceneManager.Instance.LoadScene("CreateCharacterScene");
            }
        }

        /// <summary>패널을 닫는다.</summary>
        public void Close()
        {
            if (UIManager.Instance != null)
            {
                UIManager.Instance.Hide(UIManager.PanelType.Party);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }

        // ── UI 헬퍼 ──

        /// <summary>패널 루트 배경에 배경 스프라이트(modal_bg)를 적용한다(지정 시). 없으면 기존 단색 배경 유지.</summary>
        private void ApplyBackground(Image img)
        {
            if (img == null || _backgroundSprite == null) return;
            img.sprite = _backgroundSprite;
            img.type = Image.Type.Simple;
            img.color = Color.white;
        }

        private static Image NewImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            return img;
        }

        private static RawImage NewRawImage(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RawImage>();
        }

        private Text NewText(string name, Transform parent, string content, int size, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = _font;
            t.text = content;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = Color.white;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void PlaceCenter(RectTransform rt, float fx, float fy, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(fx, fy);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(w, h);
        }
    }
}
