using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using TaskbarHero.Client.Managers;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 로그인 UI보다 먼저 노출되는 '접속 서버 선택' 화면. 접속 서버를 직접 입력하는 칸과 로컬 프리셋 버튼,
    /// 하단의 '확인' 버튼으로 구성된다. 입력 기본값은 현재 사용 중인 로컬 주소(호스트)다.
    /// '확인'을 누르면 접속 서버가 확정(NetworkManager에 적용)되고, 이 화면은 파괴되며 로그인 UI가 활성화된다.
    /// 런타임에 자체 Canvas·EventSystem을 코드로 구성한다(타이틀 단계에는 EventSystem이 없으므로 필요 시 생성).
    /// </summary>
    public class ServerSelectPanelController : MonoBehaviour
    {
        private InputField _input;
        private Action _onConfirmed;

        /// <summary>접속 서버 선택 화면을 생성·표시한다. onConfirmed는 '확인' 후(서버 확정·화면 파괴 직후) 1회 호출된다.</summary>
        public static void Show(Action onConfirmed)
        {
            var go = new GameObject("ServerSelectPanel");
            var c = go.AddComponent<ServerSelectPanelController>();
            c._onConfirmed = onConfirmed;
            c.Build();
        }

        private void Build()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100; // 타이틀 위, 공용 모달(500) 아래
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();

            EnsureEventSystem();

            // 전체 화면 어두운 배경(입력 차단).
            var dim = NewRect("Dim", transform);
            dim.anchorMin = Vector2.zero; dim.anchorMax = Vector2.one;
            dim.offsetMin = Vector2.zero; dim.offsetMax = Vector2.zero;
            var dimImg = dim.gameObject.AddComponent<Image>();
            dimImg.color = new Color(0f, 0f, 0f, 0.72f);

            // 중앙 카드
            var card = NewRect("Card", transform);
            card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(680f, 460f);
            card.anchoredPosition = Vector2.zero;
            var cardImg = card.gameObject.AddComponent<Image>();
            cardImg.color = new Color(0.10f, 0.13f, 0.20f, 0.98f);

            // 제목
            var title = NewText("Title", card, font, "접속 서버 선택", 40, TextAnchor.MiddleCenter);
            title.color = new Color(1f, 0.95f, 0.7f);
            title.fontStyle = FontStyle.Bold;
            var trt = title.rectTransform;
            trt.anchorMin = new Vector2(0f, 1f); trt.anchorMax = new Vector2(1f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.sizeDelta = new Vector2(-40f, 60f);
            trt.anchoredPosition = new Vector2(0f, -28f);

            // '직접 입력' 라벨
            var label = NewText("InputLabel", card, font, "직접 입력", 24, TextAnchor.MiddleLeft);
            label.color = new Color(0.85f, 0.9f, 1f);
            var lrt = label.rectTransform;
            lrt.anchorMin = new Vector2(0f, 1f); lrt.anchorMax = new Vector2(1f, 1f);
            lrt.pivot = new Vector2(0.5f, 1f);
            lrt.sizeDelta = new Vector2(-80f, 30f);
            lrt.anchoredPosition = new Vector2(0f, -110f);

            // 직접 입력 칸(기본값 = 현재 접속 호스트)
            string current = NetworkManager.Instance != null ? NetworkManager.Instance.ServerHost : "localhost";
            _input = NewInputField("ServerInput", card, font, current, "서버 주소(호스트) 입력");
            var irt = (RectTransform)_input.transform;
            irt.anchorMin = new Vector2(0f, 1f); irt.anchorMax = new Vector2(1f, 1f);
            irt.pivot = new Vector2(0.5f, 1f);
            irt.sizeDelta = new Vector2(-80f, 64f);
            irt.anchoredPosition = new Vector2(0f, -148f);

            // '로컬' 프리셋 버튼(현재 로컬 주소로 채움)
            string localHost = NetworkManager.Instance != null ? NetworkManager.Instance.DefaultServerHost : "localhost";
            var localBtn = NewButton("LocalButton", card, font, "로컬", 24, new Color(0.20f, 0.30f, 0.45f, 1f));
            var lbrt = (RectTransform)localBtn.transform;
            lbrt.anchorMin = new Vector2(0f, 1f); lbrt.anchorMax = new Vector2(0f, 1f);
            lbrt.pivot = new Vector2(0f, 1f);
            lbrt.sizeDelta = new Vector2(180f, 56f);
            lbrt.anchoredPosition = new Vector2(40f, -232f);
            localBtn.onClick.AddListener(() =>
            {
                if (_input != null) _input.text = localHost;
            });

            // '확인' 버튼(하단)
            var confirm = NewButton("ConfirmButton", card, font, "확인", 30, new Color(0.18f, 0.45f, 0.28f, 1f));
            var crt = (RectTransform)confirm.transform;
            crt.anchorMin = new Vector2(0.5f, 0f); crt.anchorMax = new Vector2(0.5f, 0f);
            crt.pivot = new Vector2(0.5f, 0f);
            crt.sizeDelta = new Vector2(600f, 72f);
            crt.anchoredPosition = new Vector2(0f, 28f);
            confirm.onClick.AddListener(OnConfirm);
        }

        /// <summary>'확인': 입력한 접속 서버를 확정(NetworkManager에 적용)하고, 이 화면을 파괴한 뒤 로그인 UI를 활성화한다.</summary>
        private void OnConfirm()
        {
            string host = _input != null ? _input.text : null;
            if (NetworkManager.Instance != null && !string.IsNullOrWhiteSpace(host))
            {
                NetworkManager.Instance.SetServerHost(host.Trim());
            }

            var cb = _onConfirmed;
            _onConfirmed = null;
            Destroy(gameObject);   // 서버 선택 UI 비활성화(파괴)
            cb?.Invoke();          // 로그인 UI 활성화
        }

        /// <summary>씬에 EventSystem이 없으면(타이틀 단계) 이 화면 하위에 InputSystem용 EventSystem을 만든다.
        /// 화면이 파괴되면 함께 사라지므로 이후 로그인 패널의 EventSystem과 중복되지 않는다.</summary>
        private void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }
            var esGo = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            esGo.transform.SetParent(transform, false);
        }

        // ── UI 생성 헬퍼 ──

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static Text NewText(string name, Transform parent, Font font, string content, int size, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = font;
            t.text = content;
            t.fontSize = size;
            t.alignment = anchor;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        /// <summary>배경 이미지 + 텍스트 + 플레이스홀더가 있는 InputField를 코드로 구성한다.</summary>
        private static InputField NewInputField(string name, Transform parent, Font font, string value, string placeholder)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(InputField));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.95f);

            var input = go.GetComponent<InputField>();

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            var trt = (RectTransform)textGo.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(14f, 6f); trt.offsetMax = new Vector2(-14f, -6f);
            var txt = textGo.GetComponent<Text>();
            txt.font = font; txt.fontSize = 26; txt.color = Color.black;
            txt.alignment = TextAnchor.MiddleLeft; txt.supportRichText = false;

            var phGo = new GameObject("Placeholder", typeof(RectTransform), typeof(Text));
            phGo.transform.SetParent(go.transform, false);
            var prt = (RectTransform)phGo.transform;
            prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
            prt.offsetMin = new Vector2(14f, 6f); prt.offsetMax = new Vector2(-14f, -6f);
            var ph = phGo.GetComponent<Text>();
            ph.font = font; ph.fontSize = 26; ph.fontStyle = FontStyle.Italic;
            ph.color = new Color(0.3f, 0.3f, 0.3f, 0.6f);
            ph.alignment = TextAnchor.MiddleLeft; ph.text = placeholder;

            input.textComponent = txt;
            input.placeholder = ph;
            input.lineType = InputField.LineType.SingleLine;
            input.text = value;
            return input;
        }

        /// <summary>배경 이미지 + 캡션 텍스트가 있는 Button을 코드로 구성한다.</summary>
        private static Button NewButton(string name, Transform parent, Font font, string caption, int size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            var trt = (RectTransform)textGo.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            var t = textGo.GetComponent<Text>();
            t.font = font; t.text = caption; t.fontSize = size;
            t.alignment = TextAnchor.MiddleCenter; t.color = Color.white;
            t.fontStyle = FontStyle.Bold;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            return btn;
        }
    }
}
