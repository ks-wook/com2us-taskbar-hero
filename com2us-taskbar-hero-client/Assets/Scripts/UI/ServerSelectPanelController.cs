using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using TaskbarHero.Client.Managers;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 로그인 UI보다 먼저 노출되는 '접속 서버 선택' 화면. <b>빌드 옵션(접속 환경) 버튼</b>(Dev / QA)과
    /// 선택된 환경의 계정·게임 서버 주소 표시, 호스트를 직접 입력하는 칸, 하단의 '확인' 버튼으로 구성된다.
    /// 기본 선택은 빌드에 구워진 환경(<see cref="ServerEnvironment.BuildDefault"/>)이며,
    /// 여기서 바꾼 환경은 <b>그 실행에만</b> 적용된다(저장하지 않는다 — 이유는 <see cref="ServerEnvironment"/> 참조).
    /// <b>QA 빌드에서는 이 화면 자체가 뜨지 않는다</b> — 접속처가 QA(원격)로 고정이므로 <see cref="Show"/>가
    /// 곧바로 다음 단계(로그인)로 넘긴다.
    /// '확인'을 누르면 환경과 접속 호스트가 확정(NetworkManager에 적용)되고, 이 화면은 파괴되며 로그인 UI가 활성화된다.
    /// 런타임에 자체 Canvas·EventSystem을 코드로 구성한다(타이틀 단계에는 EventSystem이 없으므로 필요 시 생성).
    /// </summary>
    public class ServerSelectPanelController : MonoBehaviour
    {
        // 환경 버튼 색(선택 / 미선택).
        private static readonly Color EnvSelectedColor = new Color(0.20f, 0.45f, 0.65f, 1f);
        private static readonly Color EnvUnselectedColor = new Color(0.18f, 0.20f, 0.26f, 1f);

        private InputField _input;
        private Action _onConfirmed;

        // 선택 중인 환경(확인 시점에 NetworkManager로 확정한다).
        private ServerEnvironmentKind _selectedEnv;
        private Image _devButtonImage;
        private Image _qaButtonImage;
        private Text _addressText;

        /// <summary>
        /// 접속 서버 선택 화면을 생성·표시한다. onConfirmed는 '확인' 후(서버 확정·화면 파괴 직후) 1회 호출된다.
        /// <para><b>접속처가 고정된 빌드</b>(QA — <see cref="ServerEnvironment.AllowServerSelection"/>가 false)에서는
        /// 화면을 만들지 않고 onConfirmed를 즉시 호출한다. 접속처는 이미 <see cref="NetworkManager"/>가 기동 시
        /// 구워진 환경 프리셋으로 확정해 뒀으므로, 여기서 더 확정할 것이 없다.</para>
        /// </summary>
        public static void Show(Action onConfirmed)
        {
            if (!ServerEnvironment.AllowServerSelection)
            {
                var env = NetworkManager.Instance != null
                    ? NetworkManager.Instance.CurrentEnvironment
                    : ServerEnvironment.BuildDefault;
                Debug.Log($"[ServerSelect] 접속처 고정 빌드({ServerEnvironment.DisplayNameOf(env)}) — 서버 선택 화면을 건너뛴다");
                onConfirmed?.Invoke();
                return;
            }

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
            card.sizeDelta = new Vector2(680f, 520f);
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

            // '빌드 옵션(접속 환경)' 라벨
            var envLabel = NewText("EnvLabel", card, font, "빌드 옵션(접속 환경)", 24, TextAnchor.MiddleLeft);
            envLabel.color = new Color(0.85f, 0.9f, 1f);
            var elrt = envLabel.rectTransform;
            elrt.anchorMin = new Vector2(0f, 1f); elrt.anchorMax = new Vector2(1f, 1f);
            elrt.pivot = new Vector2(0.5f, 1f);
            elrt.sizeDelta = new Vector2(-80f, 30f);
            elrt.anchoredPosition = new Vector2(0f, -104f);

            // 환경 선택 버튼(Dev / QA). 기본 선택 = 이 빌드에 구워진 환경.
            _selectedEnv = NetworkManager.Instance != null ? NetworkManager.Instance.CurrentEnvironment : ServerEnvironment.BuildDefault;

            var devBtn = NewButton("DevButton", card, font, "Dev (로컬)", 26, EnvUnselectedColor);
            _devButtonImage = devBtn.GetComponent<Image>();
            var dbrt = (RectTransform)devBtn.transform;
            dbrt.anchorMin = new Vector2(0f, 1f); dbrt.anchorMax = new Vector2(0f, 1f);
            dbrt.pivot = new Vector2(0f, 1f);
            dbrt.sizeDelta = new Vector2(290f, 56f);
            dbrt.anchoredPosition = new Vector2(40f, -138f);
            devBtn.onClick.AddListener(() => SelectEnvironment(ServerEnvironmentKind.Dev));

            var qaBtn = NewButton("QaButton", card, font, "QA (원격)", 26, EnvUnselectedColor);
            _qaButtonImage = qaBtn.GetComponent<Image>();
            var qbrt = (RectTransform)qaBtn.transform;
            qbrt.anchorMin = new Vector2(0f, 1f); qbrt.anchorMax = new Vector2(0f, 1f);
            qbrt.pivot = new Vector2(0f, 1f);
            qbrt.sizeDelta = new Vector2(290f, 56f);
            qbrt.anchoredPosition = new Vector2(350f, -138f);
            qaBtn.onClick.AddListener(() => SelectEnvironment(ServerEnvironmentKind.Qa));

            // 선택된 환경의 계정·게임 서버 주소 표시(두 줄).
            _addressText = NewText("AddressText", card, font, string.Empty, 20, TextAnchor.UpperLeft);
            _addressText.color = new Color(0.75f, 0.82f, 0.95f);
            var art = _addressText.rectTransform;
            art.anchorMin = new Vector2(0f, 1f); art.anchorMax = new Vector2(1f, 1f);
            art.pivot = new Vector2(0.5f, 1f);
            art.sizeDelta = new Vector2(-80f, 64f);
            art.anchoredPosition = new Vector2(0f, -204f);

            // '직접 입력' 라벨
            var label = NewText("InputLabel", card, font, "직접 입력 (호스트만 · 스킴·포트는 환경 값 유지)", 22, TextAnchor.MiddleLeft);
            label.color = new Color(0.85f, 0.9f, 1f);
            var lrt = label.rectTransform;
            lrt.anchorMin = new Vector2(0f, 1f); lrt.anchorMax = new Vector2(1f, 1f);
            lrt.pivot = new Vector2(0.5f, 1f);
            lrt.sizeDelta = new Vector2(-80f, 30f);
            lrt.anchoredPosition = new Vector2(0f, -282f);

            // 직접 입력 칸(기본값 = 현재 접속 호스트)
            string current = NetworkManager.Instance != null ? NetworkManager.Instance.ServerHost : "localhost";
            _input = NewInputField("ServerInput", card, font, current, "서버 주소(호스트) 입력");
            var irt = (RectTransform)_input.transform;
            irt.anchorMin = new Vector2(0f, 1f); irt.anchorMax = new Vector2(1f, 1f);
            irt.pivot = new Vector2(0.5f, 1f);
            irt.sizeDelta = new Vector2(-80f, 64f);
            irt.anchoredPosition = new Vector2(0f, -318f);

            RefreshEnvironmentView(fillInput: false);

            // '확인' 버튼(하단)
            var confirm = NewButton("ConfirmButton", card, font, "확인", 30, new Color(0.18f, 0.45f, 0.28f, 1f));
            var crt = (RectTransform)confirm.transform;
            crt.anchorMin = new Vector2(0.5f, 0f); crt.anchorMax = new Vector2(0.5f, 0f);
            crt.pivot = new Vector2(0.5f, 0f);
            crt.sizeDelta = new Vector2(600f, 72f);
            crt.anchoredPosition = new Vector2(0f, 28f);
            confirm.onClick.AddListener(OnConfirm);
        }

        /// <summary>환경 버튼을 눌렀을 때: 선택 환경을 바꾸고 표시·입력 칸을 그 환경의 프리셋 값으로 갱신한다(확정은 '확인' 시점).</summary>
        private void SelectEnvironment(ServerEnvironmentKind kind)
        {
            _selectedEnv = kind;
            RefreshEnvironmentView(fillInput: true);
        }

        /// <summary>선택된 환경에 맞춰 버튼 강조·주소 표시를 갱신한다. fillInput이면 입력 칸도 그 환경의 프리셋 호스트로 채운다.</summary>
        private void RefreshEnvironmentView(bool fillInput)
        {
            if (_devButtonImage != null)
            {
                _devButtonImage.color = _selectedEnv == ServerEnvironmentKind.Dev ? EnvSelectedColor : EnvUnselectedColor;
            }
            if (_qaButtonImage != null)
            {
                _qaButtonImage.color = _selectedEnv == ServerEnvironmentKind.Qa ? EnvSelectedColor : EnvUnselectedColor;
            }

            string accountUrl = ServerEnvironment.AccountBaseUrlOf(_selectedEnv);
            string gameUrl = ServerEnvironment.GameBaseUrlOf(_selectedEnv);
            if (_addressText != null)
            {
                _addressText.text = $"계정: {accountUrl}\n게임: {gameUrl}";
            }
            if (fillInput && _input != null)
            {
                _input.text = HostOf(accountUrl);
            }
        }

        /// <summary>URL에서 호스트명만 뽑아낸다(입력 칸 기본값용. 파싱 실패 시 원본 반환).</summary>
        private static string HostOf(string url)
        {
            try { return new Uri(url).Host; }
            catch { return url; }
        }

        /// <summary>'확인': 선택한 환경과 입력한 접속 호스트를 확정(NetworkManager에 적용)하고, 이 화면을 파괴한 뒤 로그인 UI를 활성화한다.</summary>
        private void OnConfirm()
        {
            string host = _input != null ? _input.text : null;
            if (NetworkManager.Instance != null)
            {
                // 환경(스킴·포트 프리셋)을 먼저 확정한 뒤, 호스트 override를 적용한다.
                NetworkManager.Instance.SetEnvironment(_selectedEnv);
                if (!string.IsNullOrWhiteSpace(host))
                {
                    NetworkManager.Instance.SetServerHost(host.Trim());
                }
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
