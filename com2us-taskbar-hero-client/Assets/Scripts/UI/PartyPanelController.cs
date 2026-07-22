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
                Refresh();
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
            var img = NewImage("Dim", (RectTransform)transform, new Color(0f, 0f, 0f, 0.6f));
            Stretch(img.rectTransform);
            _dimButton = img.gameObject.AddComponent<Button>();
            _dimButton.transition = Selectable.Transition.None;
        }

        private RectTransform BuildPanel()
        {
            var img = NewImage("PartyPanelRoot", (RectTransform)transform, new Color(0.10f, 0.12f, 0.18f, 0.98f));
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
        }

        // ── 갱신(세션 실데이터) ──

        /// <summary>각 슬롯을 현재 파티로 갱신한다. 캐릭터 있으면 직업·레벨, 없으면 '+' 버튼.</summary>
        private void Refresh()
        {
            var chars = Session.GameData != null ? Session.GameData.characters : null;
            var db = MasterDataManager.Db;

            for (int i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                if (slot == null)
                {
                    continue;
                }
                ClearSlotContent(slot);

                CharacterDto c = chars != null && i < chars.Count ? chars[i] : null;
                if (c != null)
                {
                    string cls = db != null && db.Classes.TryGetValue(c.classCode, out var cm) ? cm.name : $"직업 {c.classCode}";
                    var name = NewText("SlotName", slot, cls, 34, TextAnchor.MiddleCenter);
                    name.fontStyle = FontStyle.Bold;
                    PlaceCenter(name.rectTransform, 0.5f, 0.6f, 190f, 48f);
                    var lv = NewText("SlotLevel", slot, $"Lv.{c.level}", 28, TextAnchor.MiddleCenter);
                    lv.color = new Color(0.8f, 0.85f, 0.95f);
                    PlaceCenter(lv.rectTransform, 0.5f, 0.42f, 190f, 40f);
                }
                else
                {
                    // 빈 슬롯: '+' 버튼 → 캐릭터 생성 씬
                    var plus = NewImage("AddButton", slot, new Color(0.25f, 0.55f, 0.35f, 1f));
                    PlaceCenter(plus.rectTransform, 0.5f, 0.5f, 110f, 110f);
                    var pt = NewText("Plus", plus.rectTransform, "＋", 64, TextAnchor.MiddleCenter);
                    Stretch(pt.rectTransform);
                    var cap = NewText("AddCaption", slot, "캐릭터 추가", 24, TextAnchor.MiddleCenter);
                    cap.color = new Color(0.7f, 0.85f, 0.7f);
                    PlaceCenter(cap.rectTransform, 0.5f, 0.2f, 190f, 36f);
                    plus.gameObject.AddComponent<Button>().onClick.AddListener(OnAddCharacter);
                }
            }
        }

        /// <summary>슬롯의 런타임 생성 콘텐츠(이름/레벨/추가 버튼)를 모두 제거한다.</summary>
        private static void ClearSlotContent(RectTransform slot)
        {
            for (int i = slot.childCount - 1; i >= 0; i--)
            {
                Destroy(slot.GetChild(i).gameObject);
            }
        }

        /// <summary>'+' 클릭: 캐릭터 생성 씬으로 이동.</summary>
        private void OnAddCharacter()
        {
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

        private static Image NewImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            return img;
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
