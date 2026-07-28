using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace TaskbarHero.Client.Managers
{
    /// <summary>
    /// 클라이언트 공용 모달(안내창). 한 프리팹으로 두 종류를 표현한다.
    /// - <see cref="ShowOk"/>: '확인' 버튼만 있는 안내 모달
    /// - <see cref="ShowOkCancel"/>: '확인'·'취소' 버튼이 있는 선택 모달
    /// 배경/버튼 스프라이트는 Assets/Art/UI(modal_bg·pixel_rpg_button)를 사용하며, 버튼 클릭 시
    /// 잠깐 커졌다 돌아오는(punch) 연출 후 콜백을 실행한다. 계층은 에디터 빌드로 프리팹에 baked되고,
    /// 런타임에는 <see cref="ModalManager"/>가 인스턴스를 1개 캐싱해 재사용한다(오버레이 sortingOrder 500).
    /// </summary>
    public class ModalController : MonoBehaviour
    {
        [Header("UI 리소스 (Assets/Art/UI)")]
        [SerializeField] private Sprite modalBg;       // modal_bg
        [SerializeField] private Sprite buttonSprite;  // pixel_rpg_button(9-slice)

        [Header("구성 참조 (에디터 빌더가 배선)")]
        [SerializeField] private RectTransform _content; // dim+panel 컨테이너(표시 토글)
        [SerializeField] private RectTransform _panel;
        [SerializeField] private Text _titleText;
        [SerializeField] private Text _messageText;
        [SerializeField] private Button _okButton;
        [SerializeField] private Button _cancelButton;
        [SerializeField] private Text _okLabel;
        [SerializeField] private Text _cancelLabel;

        private const float CanvasRefWidth = 1080f;
        private const float CanvasRefHeight = 1920f;
        private const float PunchScale = 1.18f;
        private const float PunchDuration = 0.18f;

        private Font _font;
        private Action _onOk;
        private Action _onCancel;

        private bool AlreadyBuilt => _panel != null;

        private void Awake()
        {
            if (!AlreadyBuilt)
            {
                Construct(); // 폴백(프리팹 없이 직접 생성한 경우)
            }
            WireRuntime();
            Hide();
        }

        /// <summary>에디터 빌드 전용: 전체 계층을 생성해 프리팹에 baked한다.</summary>
        public void EditorConstruct() => Construct();

        // ── 공개 API ──

        /// <summary>'확인'만 있는 안내 모달을 표시한다. 확인 클릭 시 <paramref name="onOk"/> 실행.</summary>
        public void ShowOk(string title, string message, Action onOk = null)
        {
            _onOk = onOk;
            _onCancel = null;
            SetTexts(title, message);
            if (_cancelButton != null) _cancelButton.gameObject.SetActive(false);
            LayoutButtons(false);
            Present();
        }

        /// <summary>'확인'·'취소'가 있는 선택 모달을 표시한다. 각각 클릭 시 콜백 실행.</summary>
        public void ShowOkCancel(string title, string message, Action onOk = null, Action onCancel = null)
        {
            _onOk = onOk;
            _onCancel = onCancel;
            SetTexts(title, message);
            if (_cancelButton != null) _cancelButton.gameObject.SetActive(true);
            LayoutButtons(true);
            Present();
        }

        /// <summary>모달을 숨긴다(콜백 없이 닫기).</summary>
        public void Hide()
        {
            if (_content != null)
            {
                _content.gameObject.SetActive(false);
            }
        }

        private void SetTexts(string title, string message)
        {
            if (_titleText != null) _titleText.text = title ?? string.Empty;
            if (_messageText != null) _messageText.text = message ?? string.Empty;
        }

        /// <summary>버튼 배치: 확인만이면 중앙, 확인/취소면 좌(취소)·우(확인).</summary>
        private void LayoutButtons(bool withCancel)
        {
            if (_okButton != null)
            {
                var rt = (RectTransform)_okButton.transform;
                rt.anchoredPosition = new Vector2(withCancel ? 150f : 0f, 44f);
            }
            if (_cancelButton != null)
            {
                var rt = (RectTransform)_cancelButton.transform;
                rt.anchoredPosition = new Vector2(-150f, 44f);
            }
        }

        private void Present()
        {
            if (_content != null)
            {
                _content.gameObject.SetActive(true);
                _content.SetAsLastSibling();
            }
            StartCoroutine(PopIn());
        }

        /// <summary>패널이 살짝 커지며 나타나는 팝인 연출(unscaled).</summary>
        private IEnumerator PopIn()
        {
            if (_panel == null) yield break;
            float dur = 0.12f, t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / dur);
                _panel.localScale = Vector3.one * Mathf.Lerp(0.85f, 1f, k);
                yield return null;
            }
            _panel.localScale = Vector3.one;
        }

        // ── 버튼 처리(punch → 콜백) ──

        private void WireRuntime()
        {
            if (_okButton != null) _okButton.onClick.AddListener(OnOkClicked);
            if (_cancelButton != null) _cancelButton.onClick.AddListener(OnCancelClicked);
        }

        private void OnOkClicked()
        {
            SoundManager.Sfx(SoundId.UiModalOk);
            var cb = _onOk;
            PunchThenClose(_okButton, cb);
        }

        private void OnCancelClicked()
        {
            SoundManager.Sfx(SoundId.UiModalCancel);
            var cb = _onCancel;
            PunchThenClose(_cancelButton, cb);
        }

        /// <summary>버튼을 punch(커졌다 복귀)한 뒤 모달을 닫고 콜백을 실행한다.</summary>
        private void PunchThenClose(Button button, Action callback)
        {
            if (button == null)
            {
                Hide();
                callback?.Invoke();
                return;
            }
            // 중복 클릭 방지
            if (_okButton != null) _okButton.interactable = false;
            if (_cancelButton != null) _cancelButton.interactable = false;
            StartCoroutine(PunchRoutine((RectTransform)button.transform, () =>
            {
                if (_okButton != null) _okButton.interactable = true;
                if (_cancelButton != null) _cancelButton.interactable = true;
                Hide();
                callback?.Invoke();
            }));
        }

        private IEnumerator PunchRoutine(RectTransform rect, Action onComplete)
        {
            float half = Mathf.Max(0.01f, PunchDuration * 0.5f);
            float t = 0f;
            while (t < half)
            {
                t += Time.unscaledDeltaTime;
                rect.localScale = Vector3.one * Mathf.Lerp(1f, PunchScale, Mathf.Clamp01(t / half));
                yield return null;
            }
            t = 0f;
            while (t < half)
            {
                t += Time.unscaledDeltaTime;
                rect.localScale = Vector3.one * Mathf.Lerp(PunchScale, 1f, Mathf.Clamp01(t / half));
                yield return null;
            }
            rect.localScale = Vector3.one;
            onComplete?.Invoke();
        }

        // ── 계층 생성 ──

        private void Construct()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvas = gameObject.GetComponent<Canvas>();
            if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500; // 모든 패널(≤110)·연출(≤300) 위

            var scaler = gameObject.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(CanvasRefWidth, CanvasRefHeight);
            scaler.matchWidthOrHeight = 0.5f;

            if (gameObject.GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();

            var root = (RectTransform)transform;

            // Content(dim+panel) — 표시 토글 대상
            _content = NewRect("Content", root);
            Stretch(_content);

            // Dim(뒤 입력 차단)
            var dim = NewImage("Dim", _content, null);
            dim.color = new Color(0f, 0f, 0f, 0f); // 배경을 어둡게 하지 않는다 — 입력 차단용 투명 차단막(레이캐스트만 유지)
            dim.raycastTarget = true; // 뒤 UI 클릭 차단(모달은 버튼으로만 닫힘)
            Stretch(dim.rectTransform);

            // Panel(배경 이미지)
            var panelImg = NewImage("Panel", _content, modalBg);
            panelImg.type = Image.Type.Simple;
            _panel = panelImg.rectTransform;
            _panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 0.5f);
            _panel.pivot = new Vector2(0.5f, 0.5f);
            _panel.sizeDelta = new Vector2(760f, 440f);
            _panel.anchoredPosition = Vector2.zero;

            _titleText = NewText("Title", _panel, "", 44, TextAnchor.UpperCenter);
            _titleText.fontStyle = FontStyle.Bold;
            TopLeft(_titleText.rectTransform, 40f, 36f, 680f, 56f);

            _messageText = NewText("Message", _panel, "", 32, TextAnchor.UpperCenter);
            _messageText.horizontalOverflow = HorizontalWrapMode.Wrap;
            var mrt = _messageText.rectTransform;
            mrt.anchorMin = new Vector2(0f, 0f);
            mrt.anchorMax = new Vector2(1f, 1f);
            mrt.offsetMin = new Vector2(48f, 150f);
            mrt.offsetMax = new Vector2(-48f, -110f);

            _okButton = BuildButton("OkButton", "확인", out _okLabel);
            _cancelButton = BuildButton("CancelButton", "취소", out _cancelLabel);
        }

        /// <summary>패널 하단에 버튼 1개를 생성한다(punch 대상). 위치는 표시 시 LayoutButtons가 정한다.</summary>
        private Button BuildButton(string name, string label, out Text labelText)
        {
            var img = NewImage(name, _panel, buttonSprite);
            img.type = Image.Type.Sliced; // pixel_rpg_button 9-slice
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(280f, 100f);
            rt.anchoredPosition = new Vector2(0f, 44f);

            labelText = NewText("Label", rt, label, 34, TextAnchor.MiddleCenter);
            labelText.fontStyle = FontStyle.Bold;
            Stretch(labelText.rectTransform);

            return img.gameObject.AddComponent<Button>();
        }

        // ── UI 헬퍼 ──

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static Image NewImage(string name, Transform parent, Sprite sprite)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            return img;
        }

        private Text NewText(string name, Transform parent, string content, int size, TextAnchor anchor)
        {
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = _font;
            t.text = content;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = Color.white;
            t.raycastTarget = false;
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

        private static void TopLeft(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }
    }
}
