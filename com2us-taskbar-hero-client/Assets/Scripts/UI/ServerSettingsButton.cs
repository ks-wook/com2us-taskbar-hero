using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using TaskbarHero.Client.Managers;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 타이틀 화면 <b>우측 하단의 톱니바퀴 버튼</b>(왼쪽에 '접속 서버 선택' 글자를 함께 노출한다).
    /// 누르면 '접속 서버 변경' 화면(<see cref="ServerSelectPanelController.ShowManual"/>)이 열린다.
    ///
    /// <para><b>왜 필요한가</b> — QA 빌드는 로그인 전 서버 선택 화면을 자동으로 띄우지 않으므로
    /// (<see cref="ServerEnvironment.ShowServerSelectOnStart"/>) 그 빌드에는 접속처를 바꿀 통로가 없었다.
    /// 이 버튼은 <b>빌드 종류와 무관하게 항상 노출</b>돼 어느 빌드에서든 접속처를 바꿀 수 있게 한다.</para>
    ///
    /// <para><b>자체 Canvas·EventSystem을 런타임에 만든다</b> — 타이틀 씬에는 EventSystem도
    /// GraphicRaycaster도 없어(화면 클릭은 <see cref="TitleScreen"/>이 Pointer로 직접 읽는다)
    /// 그대로는 버튼이 클릭을 받지 못한다. 여기서 만든 EventSystem은 이 버튼과 함께 파괴되므로
    /// 이후 로그인 패널이 자기 EventSystem을 만드는 것과 충돌하지 않는다.</para>
    ///
    /// <para>타이틀은 화면 아무 곳이나 누르면 게임이 시작되므로, 이 버튼 위에서 누른 클릭이 시작으로
    /// 이어지지 않게 <see cref="TitleScreen"/>이 <see cref="PointerOverButton"/>으로 걸러 낸다.</para>
    /// </summary>
    public class ServerSettingsButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        // 기본 캔버스 규격(1080×1920) 기준 크기. 화면 배율은 GameViewLayout이 씬 규격에 맞춰 잡아 준다.
        private const float ButtonSize = 84f;
        private const float Margin = 24f;

        // 톱니바퀴 왼쪽에 붙는 안내 글자. 아이콘만으로는 무엇을 여는 버튼인지 알 수 없어 함께 노출한다.
        private const string LabelText = "접속 서버 선택";
        private const float LabelFontSize = 30f;
        private const float LabelWidth = 300f;
        private const float LabelHeight = 44f;   // 글자 높이(30)보다 여유를 둔다 — 빡빡하면 줄이 통째로 사라진다
        private const float LabelGap = 10f;      // 아이콘과 글자 사이 간격

        // 타이틀 배경은 밝은 일러스트라 흰 글자만으로는 묻힌다 — 검은 외곽선을 함께 깐다.
        private static readonly Color LabelOutlineColor = new Color(0f, 0f, 0f, 0.95f);
        private const float LabelOutlineDistance = 2f;

        // 평소에는 배경에 묻히게 반투명으로 두고, 커서를 올리면 또렷해진다(hover 효과음은 넣지 않는다).
        private static readonly Color IdleColor = new Color(1f, 1f, 1f, 0.55f);
        private static readonly Color HoverColor = new Color(1f, 1f, 1f, 1f);

        private Graphic _icon;
        private Text _label;
        private Outline _labelOutline;
        private Action _onClick;
        private RectTransform _buttonRect;

        // 지금 떠 있는 버튼(하나뿐이다). PointerOverButton이 매 프레임 찾지 않도록 들고 있는다.
        private static ServerSettingsButton s_instance;

        // 수동 레이캐스트 결과 버퍼.
        private static readonly List<RaycastResult> s_hits = new List<RaycastResult>();

        /// <summary>
        /// 커서가 <b>이 버튼 위</b>에 있는가. 타이틀은 화면 아무 곳이나 누르면 시작하므로,
        /// 그 판정에서 톱니바퀴를 누른 클릭을 걸러 내는 데 쓴다.
        /// <para><c>EventSystem.IsPointerOverGameObject()</c>를 쓰지 않는 이유 — 작업표시줄 오버레이 창은
        /// 포커스가 없으면 그 값이 갱신되지 않는다(<see cref="TaskbarWindow"/>가 창 드래그 판정에서
        /// 수동 <c>RaycastAll</c>을 쓰는 것과 같은 이유). 여기서도 커서 위치로 직접 레이캐스트한다.</para>
        /// </summary>
        public static bool PointerOverButton
        {
            get
            {
                if (s_instance == null || s_instance._buttonRect == null)
                {
                    return false;
                }
                var es = EventSystem.current;
                var pointer = Pointer.current;
                if (es == null || pointer == null)
                {
                    return false;
                }

                var ped = new PointerEventData(es) { position = pointer.position.ReadValue() };
                s_hits.Clear();
                es.RaycastAll(ped, s_hits);
                foreach (var hit in s_hits)
                {
                    if (hit.gameObject != null && hit.gameObject.transform.IsChildOf(s_instance.transform))
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// 톱니바퀴 버튼을 만든다. 이미 있으면 중복 생성하지 않고 기존 것을 돌려준다.
        /// </summary>
        /// <param name="icon">톱니바퀴 스프라이트(<c>Assets/Art/Icon/환경설정.png</c>). null이면 글자로 대체한다.</param>
        /// <param name="onClick">버튼을 눌렀을 때 실행할 동작.</param>
        public static ServerSettingsButton Create(Sprite icon, Action onClick)
        {
            var existing = FindAnyObjectByType<ServerSettingsButton>();
            if (existing != null)
            {
                existing._onClick = onClick;
                return existing;
            }

            var go = new GameObject("ServerSettingsButton");
            var c = go.AddComponent<ServerSettingsButton>();
            c._onClick = onClick;
            c.Build(icon);
            s_instance = c;
            return c;
        }

        /// <summary>자체 Canvas(+ 필요 시 EventSystem)를 만들고 우측 하단에 톱니바퀴 버튼을 배치한다.</summary>
        private void Build(Sprite icon)
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // 타이틀 로고(0) 위 · 접속 서버 변경 화면(100)과 공용 모달(500) 아래.
            canvas.sortingOrder = UiSortingOrder.Hud;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            GameViewLayout.ApplyCurrentScaler(scaler);   // 첫 프레임부터 올바른 배율로 그린다
            gameObject.AddComponent<GraphicRaycaster>();

            EnsureEventSystem();

            var btnGo = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
            btnGo.transform.SetParent(transform, false);
            var rect = (RectTransform)btnGo.transform;
            _buttonRect = rect;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);   // 우측 하단
            rect.pivot = new Vector2(1f, 0f);
            rect.sizeDelta = new Vector2(ButtonSize, ButtonSize);
            rect.anchoredPosition = new Vector2(-Margin, Margin);

            var image = btnGo.GetComponent<Image>();
            var button = btnGo.GetComponent<Button>();
            button.targetGraphic = image;

            if (icon != null)
            {
                image.sprite = icon;
                image.preserveAspect = true;
                image.color = IdleColor;
                _icon = image;
            }
            else
            {
                // 아이콘 배선이 빠진 경우에도 기능이 사라지지 않도록 글자 버튼으로 대체한다.
                image.color = new Color(0.10f, 0.13f, 0.20f, 0.75f);
                var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
                textGo.transform.SetParent(btnGo.transform, false);
                var trt = (RectTransform)textGo.transform;
                trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
                trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
                var text = textGo.GetComponent<Text>();
                text.font = font;
                text.text = "서버";
                text.fontSize = 30;
                text.alignment = TextAnchor.MiddleCenter;
                text.color = IdleColor;
                text.raycastTarget = false;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                text.verticalOverflow = VerticalWrapMode.Overflow;
                _icon = text;
            }

            CreateLabel(btnGo.transform);
            button.onClick.AddListener(OnClick);
        }

        /// <summary>
        /// 톱니바퀴 <b>왼쪽</b>에 '접속 서버 선택' 글자를 붙인다. 아이콘만으로는 무엇을 여는 버튼인지 알 수 없기 때문이다.
        /// 버튼의 자식이라 <b>글자도 함께 눌리고 함께 밝아진다</b>(클릭은 부모 <c>Button</c>으로, hover는 루트의
        /// <see cref="IPointerEnterHandler"/>로 올라간다).
        /// </summary>
        private void CreateLabel(Transform parent)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text), typeof(Outline));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);   // 아이콘의 왼쪽 변, 세로 가운데
            rect.pivot = new Vector2(1f, 0.5f);
            rect.sizeDelta = new Vector2(LabelWidth, LabelHeight);
            rect.anchoredPosition = new Vector2(-LabelGap, 0f);

            _label = go.GetComponent<Text>();
            _label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _label.text = LabelText;
            _label.fontSize = Mathf.RoundToInt(LabelFontSize);
            _label.alignment = TextAnchor.MiddleRight;
            _label.color = IdleColor;
            _label.horizontalOverflow = HorizontalWrapMode.Overflow;
            _label.verticalOverflow = VerticalWrapMode.Overflow;
            UiTextStyle.Apply(_label);   // 0.4초 주기 스캔을 기다리지 않고 첫 프레임부터 선명하게

            _labelOutline = go.GetComponent<Outline>();
            _labelOutline.effectDistance = new Vector2(LabelOutlineDistance, -LabelOutlineDistance);
            ApplyOutlineAlpha(IdleColor.a);
        }

        /// <summary>외곽선 진하기를 글자 투명도에 맞춘다(글자만 반투명하고 외곽선은 진한 상태를 막는다).</summary>
        private void ApplyOutlineAlpha(float alpha)
        {
            if (_labelOutline != null)
            {
                var color = LabelOutlineColor;
                color.a *= alpha;
                _labelOutline.effectColor = color;
            }
        }

        /// <summary>버튼을 눌렀을 때: 클릭음을 내고 등록된 동작을 실행한다.</summary>
        private void OnClick()
        {
            SoundManager.Sfx(SoundId.UiClick);
            _onClick?.Invoke();
        }

        /// <summary>커서가 올라오면 아이콘을 또렷하게 한다(효과음은 내지 않는다 — hover UI 무음 규칙).</summary>
        public void OnPointerEnter(PointerEventData eventData) => Tint(HoverColor);

        /// <summary>커서가 벗어나면 아이콘을 다시 반투명으로 되돌린다.</summary>
        public void OnPointerExit(PointerEventData eventData) => Tint(IdleColor);

        /// <summary>아이콘(또는 대체 글자)과 옆의 안내 글자 색을 함께 바꾼다.</summary>
        private void Tint(Color color)
        {
            if (_icon != null)
            {
                _icon.color = color;
            }
            if (_label != null)
            {
                _label.color = color;
                ApplyOutlineAlpha(color.a);
            }
        }

        /// <summary>버튼이 사라지면 정적 참조도 비운다(게임 시작 시 파괴된다).</summary>
        private void OnDestroy()
        {
            if (s_instance == this)
            {
                s_instance = null;
            }
        }

        /// <summary>씬에 EventSystem이 없으면(타이틀 씬) 이 버튼 하위에 InputSystem용 EventSystem을 만든다.
        /// 버튼이 파괴되면 함께 사라지므로 이후 다른 패널의 EventSystem과 중복되지 않는다.</summary>
        private void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }
            var esGo = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            esGo.transform.SetParent(transform, false);
        }
    }
}
