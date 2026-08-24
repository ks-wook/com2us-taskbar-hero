using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using TaskbarHero.Client.Managers;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// '접속 서버' 화면. <b>빌드 옵션(접속 환경) 버튼</b>(Dev / QA)과 선택된 환경의 프리셋 주소 표시,
    /// <b>계정·게임 서버 주소를 각각 포트까지 입력하는 칸</b>(<c>호스트:포트</c>), 하단 버튼으로 구성된다.
    /// 두 서버는 포트가 다르므로(Dev 5160/5247 · QA 443/8443) 호스트 하나로는 접속처를 표현할 수 없어
    /// 서버별 입력 칸을 둔다(스킴 http/https는 선택한 환경 값을 유지한다).
    /// 여는 경로가 둘이다:
    /// <list type="bullet">
    /// <item><b>로그인 직전 자동 노출</b>(<see cref="Show"/>) — QA 빌드에서는 생략하고 곧바로 로그인으로 넘어간다
    ///   (<see cref="ServerEnvironment.ShowServerSelectOnStart"/>). <b>단, 이 기기에 저장된 접속처가 없으면</b>
    ///   (<see cref="NeedsInitialSelection"/>) 빌드 종류와 무관하게 <b>무조건 표시</b>한다 — 접속처를 한 번도
    ///   고르지 않은 기기를 프리셋 주소로 조용히 붙여 버리지 않기 위함이다.</item>
    /// <item><b>타이틀 우측 하단 톱니바퀴</b>(<see cref="ShowManual"/>) — <b>빌드 종류와 무관하게</b> 열린다.
    ///   접속처를 되돌릴 통로가 없으면 안 되므로 QA 빌드에서도 이 경로는 막지 않는다.
    ///   '취소'(또는 ESC)로 아무것도 바꾸지 않고 닫을 수 있다.</item>
    /// </list>
    /// 기본 선택은 <b>지금 접속 중인 환경</b>(= 마지막으로 고른 환경)이다. '확인'을 누르면 환경과 두 서버 주소가
    /// 확정(<see cref="NetworkManager.SetServerEndpoints"/>)되고 <b>다음 실행을 위해 저장</b>된다 —
    /// 마지막으로 접속한 서버가 계속 유지되고, 이후로는 "저장된 접속처가 있는 기기"가 된다.
    /// 런타임에 자체 Canvas·EventSystem을 코드로 구성한다(타이틀 단계에는 EventSystem이 없으므로 필요 시 생성).
    /// </summary>
    public class ServerSelectPanelController : MonoBehaviour
    {
        // 환경 버튼 색(선택 / 미선택).
        private static readonly Color EnvSelectedColor = new Color(0.20f, 0.45f, 0.65f, 1f);
        private static readonly Color EnvUnselectedColor = new Color(0.18f, 0.20f, 0.26f, 1f);

        // 하단 버튼 색(확인 / 취소).
        private static readonly Color ConfirmColor = new Color(0.18f, 0.45f, 0.28f, 1f);
        private static readonly Color CancelColor = new Color(0.30f, 0.22f, 0.24f, 1f);

        private InputField _accountInput;
        private InputField _gameInput;
        private Action _onConfirmed;
        private Action<bool> _onClosed;
        private bool _manual;

        // 선택 중인 환경(확인 시점에 NetworkManager로 확정한다).
        private ServerEnvironmentKind _selectedEnv;
        private Image _devButtonImage;
        private Image _qaButtonImage;
        private Text _addressText;

        /// <summary>
        /// 이 화면이 떠 있는가. 타이틀 화면은 <b>Pointer를 직접 읽어</b> 화면 아무 곳이나 누르면 게임을 시작하므로
        /// (딤 이미지가 그 입력을 막아 주지 못한다) <see cref="TitleScreen"/>이 이 값으로 입력을 멈춘다.
        /// </summary>
        public static bool IsOpen { get; private set; }

        /// <summary>
        /// <b>이 기기에 저장된 접속처가 없는가</b>(접속 서버 선택 UI의 '확인'을 누른 적이 없는가).
        /// true면 <b>빌드 종류·자동 로그인과 무관하게</b> 선택 화면을 반드시 한 번 보여 준다 —
        /// 접속처를 고른 적 없는 기기를 빌드 프리셋 주소로 조용히 붙여 버리지 않기 위함이다.
        /// </summary>
        public static bool NeedsInitialSelection
            => NetworkManager.Instance != null && !NetworkManager.HasServerSelection;

        /// <summary>
        /// 로그인 직전의 접속 서버 선택 화면을 표시한다. onConfirmed는 '확인' 후(서버 확정·화면 파괴 직후) 1회 호출된다.
        /// <para><b>서버 선택을 자동으로 묻지 않는 빌드</b>(QA — <see cref="ServerEnvironment.ShowServerSelectOnStart"/>가
        /// false)에서는 화면을 만들지 않고 onConfirmed를 즉시 호출한다. 접속처는 이미 <see cref="NetworkManager"/>가
        /// 기동 시 확정해 뒀으므로(마지막 선택 또는 빌드 프리셋) 여기서 더 확정할 것이 없다.
        /// 바꿔야 할 때는 타이틀의 톱니바퀴(<see cref="ShowManual"/>)로 연다.</para>
        /// <para><b>예외 — 저장된 접속처가 없는 기기</b>(<see cref="NeedsInitialSelection"/>)에서는 그 빌드에서도
        /// 화면을 띄운다.</para>
        /// </summary>
        public static void Show(Action onConfirmed)
        {
            if (!ServerEnvironment.ShowServerSelectOnStart && !NeedsInitialSelection)
            {
                var env = NetworkManager.Instance != null
                    ? NetworkManager.Instance.CurrentEnvironment
                    : ServerEnvironment.BuildDefault;
                Debug.Log($"[ServerSelect] 서버 선택을 자동으로 묻지 않는 빌드({ServerEnvironment.DisplayNameOf(env)}) — 화면을 건너뛴다");
                onConfirmed?.Invoke();
                return;
            }
            Create(manual: false, onConfirmed: onConfirmed, onClosed: null);
        }

        /// <summary>
        /// 접속처를 <b>반드시 확정하고 넘어가야 하는</b> 경우(저장된 접속처가 없는 기기)에 쓰는 강제 노출.
        /// 자동 노출과 같은 화면이라 '취소'·ESC가 없다.
        /// </summary>
        /// <param name="onClosed">'확인' 후 1회 호출된다. 인자는 <b>접속 주소가 실제로 바뀌었는지</b>이며,
        /// true면 호출측이 그 서버 기준으로 상태를 다시 잡아야 한다(예: 이전 서버로 끝낸 자동 로그인 무효화).</param>
        public static void ShowRequired(Action<bool> onClosed)
        {
            if (IsOpen)
            {
                return;   // 이미 떠 있으면 겹쳐 열지 않는다.
            }
            Create(manual: false, onConfirmed: null, onClosed: onClosed);
        }

        /// <summary>
        /// 타이틀의 톱니바퀴로 여는 '접속 서버 변경' 화면. <b>빌드 종류와 무관하게 항상 열린다.</b>
        /// </summary>
        /// <param name="onClosed">닫힌 뒤 1회 호출된다. 인자는 <b>접속 주소가 실제로 바뀌었는지</b>이며,
        /// true면 호출측이 그 서버 기준으로 상태를 다시 잡아야 한다(예: 자동 로그인 재검증).</param>
        public static void ShowManual(Action<bool> onClosed)
        {
            if (IsOpen)
            {
                return;   // 이미 떠 있으면 겹쳐 열지 않는다.
            }
            Create(manual: true, onConfirmed: null, onClosed: onClosed);
        }

        /// <summary>화면 오브젝트를 만들어 구성한다(두 진입 경로가 공유하는 생성부).</summary>
        private static void Create(bool manual, Action onConfirmed, Action<bool> onClosed)
        {
            var go = new GameObject(manual ? "ServerChangePanel" : "ServerSelectPanel");
            var c = go.AddComponent<ServerSelectPanelController>();
            c._manual = manual;
            c._onConfirmed = onConfirmed;
            c._onClosed = onClosed;
            IsOpen = true;
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
            card.sizeDelta = new Vector2(680f, 620f);   // 계정·게임 주소 입력 칸 2개가 들어가는 높이
            card.anchoredPosition = Vector2.zero;
            var cardImg = card.gameObject.AddComponent<Image>();
            cardImg.color = new Color(0.10f, 0.13f, 0.20f, 0.98f);

            // 제목
            var title = NewText("Title", card, font, _manual ? "접속 서버 변경" : "접속 서버 선택", 40, TextAnchor.MiddleCenter);
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

            // 환경 선택 버튼(Dev / QA). 기본 선택 = 지금 접속 중인 환경(= 마지막으로 고른 환경).
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

            // 선택된 환경의 프리셋 주소 표시(두 줄) — 입력 칸을 프리셋으로 되돌리고 싶을 때의 참고값.
            _addressText = NewText("AddressText", card, font, string.Empty, 20, TextAnchor.UpperLeft);
            _addressText.color = new Color(0.75f, 0.82f, 0.95f);
            var art = _addressText.rectTransform;
            art.anchorMin = new Vector2(0f, 1f); art.anchorMax = new Vector2(1f, 1f);
            art.pivot = new Vector2(0.5f, 1f);
            art.sizeDelta = new Vector2(-80f, 64f);
            art.anchoredPosition = new Vector2(0f, -204f);

            // 직접 입력 — 서버별로 '호스트:포트'를 입력한다(스킴은 선택한 환경 값 유지).
            string account = NetworkManager.Instance != null ? NetworkManager.Instance.AccountAuthority : "localhost:5160";
            string game = NetworkManager.Instance != null ? NetworkManager.Instance.GameAuthority : "localhost:5247";

            _accountInput = BuildAddressField(card, font, "AccountAddress", "계정 서버 (호스트:포트)", account, "예: localhost:5160", -282f);
            _gameInput = BuildAddressField(card, font, "GameAddress", "게임 서버 (호스트:포트)", game, "예: localhost:5247", -386f);

            RefreshEnvironmentView(fillInput: false);

            BuildFooterButtons(card, font);
        }

        /// <summary>라벨 + '호스트:포트' 입력 칸 한 쌍을 만들어 카드에 배치한다(라벨이 위, 입력 칸이 아래).</summary>
        /// <param name="top">라벨의 카드 상단 기준 y 오프셋(음수).</param>
        private static InputField BuildAddressField(RectTransform card, Font font, string name, string caption, string value, string placeholder, float top)
        {
            var label = NewText(name + "Label", card, font, caption, 22, TextAnchor.MiddleLeft);
            label.color = new Color(0.85f, 0.9f, 1f);
            var lrt = label.rectTransform;
            lrt.anchorMin = new Vector2(0f, 1f); lrt.anchorMax = new Vector2(1f, 1f);
            lrt.pivot = new Vector2(0.5f, 1f);
            lrt.sizeDelta = new Vector2(-80f, 30f);
            lrt.anchoredPosition = new Vector2(0f, top);

            var input = NewInputField(name + "Input", card, font, value, placeholder);
            var irt = (RectTransform)input.transform;
            irt.anchorMin = new Vector2(0f, 1f); irt.anchorMax = new Vector2(1f, 1f);
            irt.pivot = new Vector2(0.5f, 1f);
            irt.sizeDelta = new Vector2(-80f, 60f);
            irt.anchoredPosition = new Vector2(0f, top - 34f);
            return input;
        }

        /// <summary>하단 버튼을 만든다. 톱니바퀴로 연 경우에는 아무것도 바꾸지 않고 닫는 '취소'를 함께 둔다
        /// (로그인 직전 자동 노출에서는 접속처를 반드시 확정하고 넘어가야 하므로 '확인' 하나뿐이다).</summary>
        private void BuildFooterButtons(RectTransform card, Font font)
        {
            if (!_manual)
            {
                var confirmOnly = NewButton("ConfirmButton", card, font, "확인", 30, ConfirmColor);
                var only = (RectTransform)confirmOnly.transform;
                only.anchorMin = new Vector2(0.5f, 0f); only.anchorMax = new Vector2(0.5f, 0f);
                only.pivot = new Vector2(0.5f, 0f);
                only.sizeDelta = new Vector2(600f, 72f);
                only.anchoredPosition = new Vector2(0f, 28f);
                confirmOnly.onClick.AddListener(OnConfirm);
                return;
            }

            var cancel = NewButton("CancelButton", card, font, "취소", 30, CancelColor);
            var lrt2 = (RectTransform)cancel.transform;
            lrt2.anchorMin = new Vector2(0.5f, 0f); lrt2.anchorMax = new Vector2(0.5f, 0f);
            lrt2.pivot = new Vector2(0.5f, 0f);
            lrt2.sizeDelta = new Vector2(292f, 72f);
            lrt2.anchoredPosition = new Vector2(-154f, 28f);
            cancel.onClick.AddListener(OnCancel);

            var confirm = NewButton("ConfirmButton", card, font, "확인", 30, ConfirmColor);
            var crt = (RectTransform)confirm.transform;
            crt.anchorMin = new Vector2(0.5f, 0f); crt.anchorMax = new Vector2(0.5f, 0f);
            crt.pivot = new Vector2(0.5f, 0f);
            crt.sizeDelta = new Vector2(292f, 72f);
            crt.anchoredPosition = new Vector2(154f, 28f);
            confirm.onClick.AddListener(OnConfirm);
        }

        /// <summary>톱니바퀴로 연 화면은 ESC로도 닫는다(취소와 같다).
        /// 자동 노출 화면은 접속처를 확정하고 넘어가야 하므로 ESC를 무시한다.</summary>
        private void Update()
        {
            if (!_manual)
            {
                return;
            }
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                OnCancel();
            }
        }

        /// <summary>환경 버튼을 눌렀을 때: 선택 환경을 바꾸고 표시·입력 칸을 그 환경의 프리셋 값으로 갱신한다(확정은 '확인' 시점).</summary>
        private void SelectEnvironment(ServerEnvironmentKind kind)
        {
            SoundManager.Sfx(SoundId.UiTab);
            _selectedEnv = kind;
            RefreshEnvironmentView(fillInput: true);
        }

        /// <summary>선택된 환경에 맞춰 버튼 강조·프리셋 주소 표시를 갱신한다.
        /// fillInput이면 두 입력 칸도 그 환경의 프리셋 주소(호스트:포트)로 채운다.</summary>
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
                _addressText.text = $"환경 기본값 — 계정: {accountUrl}\n환경 기본값 — 게임: {gameUrl}";
            }
            if (fillInput)
            {
                if (_accountInput != null)
                {
                    _accountInput.text = AuthorityOf(accountUrl);
                }
                if (_gameInput != null)
                {
                    _gameInput.text = AuthorityOf(gameUrl);
                }
            }
        }

        /// <summary>URL에서 접속 주소(호스트:포트)를 뽑아낸다 — 기본 포트도 생략하지 않고 붙여
        /// 입력 칸에 포트가 항상 보이게 한다(파싱 실패 시 원본 반환).</summary>
        private static string AuthorityOf(string url)
        {
            try
            {
                var uri = new Uri(url);
                return $"{uri.Host}:{uri.Port}";
            }
            catch { return url; }
        }

        /// <summary>'확인': 선택한 환경과 입력한 계정·게임 서버 주소(호스트:포트)를 확정한다 —
        /// <see cref="NetworkManager.SetServerEndpoints"/>로 적용·저장한 뒤 화면을 닫는다.
        /// 빈 칸은 그 환경의 프리셋 주소로 본다(포트만 비면 프리셋 포트가 유지된다).</summary>
        private void OnConfirm()
        {
            SoundManager.Sfx(SoundId.UiModalOk);

            bool changed = false;
            if (NetworkManager.Instance != null)
            {
                string beforeAccount = NetworkManager.Instance.AccountServerBaseUrl;
                string beforeGame = NetworkManager.Instance.GameServerBaseUrl;

                string account = _accountInput != null ? _accountInput.text : null;
                string game = _gameInput != null ? _gameInput.text : null;
                if (string.IsNullOrWhiteSpace(account))
                {
                    account = AuthorityOf(ServerEnvironment.AccountBaseUrlOf(_selectedEnv));
                }
                if (string.IsNullOrWhiteSpace(game))
                {
                    game = AuthorityOf(ServerEnvironment.GameBaseUrlOf(_selectedEnv));
                }

                NetworkManager.Instance.SetServerEndpoints(_selectedEnv, account.Trim(), game.Trim());

                changed = !string.Equals(beforeAccount, NetworkManager.Instance.AccountServerBaseUrl, StringComparison.Ordinal)
                          || !string.Equals(beforeGame, NetworkManager.Instance.GameServerBaseUrl, StringComparison.Ordinal);
                if (changed)
                {
                    Debug.Log($"[ServerSelect] 접속 서버 변경: 계정 {beforeAccount} → {NetworkManager.Instance.AccountServerBaseUrl}" +
                              $" · 게임 {beforeGame} → {NetworkManager.Instance.GameServerBaseUrl}");
                }
            }
            Close(changed);
        }

        /// <summary>'취소'(톱니바퀴 경로 전용): 아무것도 확정하지 않고 닫는다.</summary>
        private void OnCancel()
        {
            SoundManager.Sfx(SoundId.UiClickBack);
            Close(false);
        }

        /// <summary>화면을 파괴하고 대기 중인 콜백을 1회씩 실행한다.</summary>
        /// <param name="changed">접속 주소가 실제로 바뀌었는지(톱니바퀴 경로의 onClosed 인자).</param>
        private void Close(bool changed)
        {
            var confirmed = _onConfirmed;
            var closed = _onClosed;
            _onConfirmed = null;
            _onClosed = null;

            Destroy(gameObject);   // 실제 파괴는 프레임 끝 — IsOpen은 OnDestroy에서 내린다
            confirmed?.Invoke();   // (자동 노출 경로) 로그인 UI 활성화
            closed?.Invoke(changed);
        }

        /// <summary>화면이 사라지면 열림 표시를 내린다. <b>여기서만</b> 내리는 이유 — 확인/취소 클릭이 처리된
        /// 그 프레임에 표시를 내리면, 같은 프레임에 타이틀이 그 클릭의 '뗌'을 보고 게임을 시작해 버린다.</summary>
        private void OnDestroy()
        {
            IsOpen = false;
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
