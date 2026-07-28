using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TaskbarHero.Client.Managers;
using TaskbarHero.Common.Dto;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// GameScene 상시 HUD. 메일·인벤토리·편성·스테이지 토글 버튼과 ESC 메뉴(타이틀로 돌아가기)를 코드로 구성한다.
    /// </summary>
    public class GameSceneHudController : MonoBehaviour
    {
        [Header("메뉴 버튼 아이콘 (에디터 빌더가 배선: Assets/Art/Icon, 메일은 Assets/Art/UI/Mail, 출석부는 Assets/Art/UI/Attendance)")]
        [SerializeField] private Sprite mailIcon;       // 메일(우편함)
        [SerializeField] private Sprite partyIcon;      // 편성
        [SerializeField] private Sprite stageIcon;      // 스테이지
        [SerializeField] private Sprite inventoryIcon;  // 가방
        [SerializeField] private Sprite attendanceIcon; // 출석부

        [Header("알림(레드닷)")]
        [Tooltip("미수령 보상 메일 확인을 위한 우편함 재조회 주기(초). 0 이하면 진입 시 1회만 조회한다.")]
        [SerializeField] private float mailPollIntervalSeconds = 60f;

        [Header("접속 시 자동 표시")]
        [Tooltip("접속 시 오늘자 출석 보상이 아직 남아 있으면 출석부 패널을 자동으로 연다.")]
        [SerializeField] private bool autoOpenAttendance = true;

        private GameObject _escMenuRoot; // ESC로 토글하는 메뉴(타이틀 복귀)

        private void Awake()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildHud(font);
            BuildEscMenu(font);
        }

        /// <summary>ESC 키로 타이틀 복귀 메뉴를 토글한다(새 Input System).</summary>
        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                ToggleEscMenu();
            }
        }

        /// <summary>GameScene 진입 시 대기 중인 오프라인 보상 정산 결과가 있으면 팝업으로 표시하고,
        /// 메일 레드닷 판정을 위한 우편함 조회 루프와 출석부 자동 표시 판정을 시작한다.</summary>
        private void Start()
        {
            if (Session.PendingOfflineReward != null && UIManager.Instance != null)
            {
                UIManager.Instance.ShowOfflineReward();
            }
            StartCoroutine(MailNotifyLoop());
            if (autoOpenAttendance)
            {
                StartCoroutine(AutoOpenAttendanceRoutine());
            }
        }

        /// <summary>접속 직후 오늘자 출석 보상이 아직 남아 있으면 출석부를 자동으로 연다.
        /// <see cref="UIManager.Show"/>는 다른 패널을 모두 숨기므로, 먼저 뜬 오프라인 보상 팝업 등을
        /// 덮지 않도록 열려 있는 패널이 모두 닫힌 뒤에 조회하고, 조회 사이에 사용자가 다른 패널을 열었으면
        /// 표시를 포기한다. 조회 실패는 경고만 남기고 자동 표시를 생략한다(수동으로 열 수 있으므로).</summary>
        private IEnumerator AutoOpenAttendanceRoutine()
        {
            yield return new WaitUntil(() => UIManager.Instance != null && !UIManager.Instance.IsAnyPanelVisible());

            if (NetworkManager.Instance == null || !Session.IsLoggedIn)
            {
                yield break;
            }

            bool? claimable = null;
            var req = new AuthRequest { userId = Session.UserId, token = Session.Token };
            NetworkManager.Instance.PostToGame<AttendanceStatusResponse>("/api/game/attendance/status", req,
                // canClaim = 오늘 미수령 && 남은 일차 있음(30일차까지 다 받은 달에는 자동으로 열지 않는다).
                resp => claimable = resp != null && resp.data != null && resp.data.canClaim,
                error =>
                {
                    Debug.LogWarning($"[HUD] 출석 현황 조회 실패 — 출석부 자동 표시 생략: {error}");
                    claimable = false;
                });

            yield return new WaitUntil(() => claimable.HasValue);

            if (claimable.Value && !UIManager.Instance.IsAnyPanelVisible())
            {
                Debug.Log("[HUD] 오늘자 출석 보상 미수령 — 출석부 자동 표시");
                UIManager.Instance.ShowAttendance();
            }
        }

        /// <summary>미수령 보상 메일 레드닷용 우편함 스냅샷을 진입 직후 1회 조회하고, 주기가 설정돼 있으면
        /// 그 간격으로 재조회한다(플레이 중 도착한 메일도 알림에 반영). 전투 슬로우모션 등 timeScale 변화의
        /// 영향을 받지 않도록 실시간 대기를 사용한다.</summary>
        private IEnumerator MailNotifyLoop()
        {
            MailNotifier.Refresh();
            if (mailPollIntervalSeconds <= 0f)
            {
                yield break;
            }
            var wait = new WaitForSecondsRealtime(mailPollIntervalSeconds);
            while (true)
            {
                yield return wait;
                MailNotifier.Refresh();
            }
        }

        /// <summary>HUD 캔버스와 토글 버튼(스테이지·가방)을 생성·배선한다.</summary>
        private void BuildHud(Font font)
        {
            var canvasGo = new GameObject("HudCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10; // 패널(100)보다 아래
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            // 우하단: [출석부] [메일] [편성] [스테이지] [가방] — 아이콘 위 + 작은 텍스트 아래
            CreateButton(canvasGo.transform, font, "AttendanceButton", "출석부", attendanceIcon, new Vector2(-840f, 40f), OnAttendanceButton);
            var mailBtn = CreateButton(canvasGo.transform, font, "MailButton", "메일", mailIcon, new Vector2(-640f, 40f), OnMailButton);
            CreateButton(canvasGo.transform, font, "PartyButton", "편성", partyIcon, new Vector2(-440f, 40f), OnPartyButton);
            CreateButton(canvasGo.transform, font, "StageButton", "스테이지", stageIcon, new Vector2(-240f, 40f), OnStageButton);
            var inventoryBtn = CreateButton(canvasGo.transform, font, "InventoryButton", "가방", inventoryIcon, new Vector2(-40f, 40f), OnInventoryButton);

            // 메일 버튼 우측 상단 레드닷: 아직 수령하지 않은 보상 첨부가 남은 메일이 있으면 표시(만료 전 수령 유도).
            RedDot.AttachTopRight((RectTransform)mailBtn.transform).Bind(RedDotConditions.HasUnclaimedMailReward);

            // 가방 버튼 우측 상단 레드닷: 잔여 스킬 포인트가 있으면 표시(스킬 레벨업은 가방 안에서 진입).
            RedDot.AttachTopRight((RectTransform)inventoryBtn.transform).Bind(RedDotConditions.HasUnspentSkillPoints);
        }

        /// <summary>우하단 앵커 HUD 버튼 하나를 생성·배선하고 생성한 버튼 오브젝트를 반환한다.
        /// 아이콘이 있으면 아이콘을 상단에, 작아진 텍스트를 그 아래에 배치한다(아이콘 없으면 텍스트만 중앙).</summary>
        private static GameObject CreateButton(Transform parent, Font font, string name, string label,
            Sprite icon, Vector2 anchoredPos, UnityEngine.Events.UnityAction onClick)
        {
            var btnGo = new GameObject(name, typeof(RectTransform), typeof(Image));
            btnGo.transform.SetParent(parent, false);
            var img = btnGo.GetComponent<Image>();
            // 배경 투명(A=0). raycastTarget는 유지되므로 투명해도 클릭은 정상 동작한다.
            img.color = new Color(0.25f, 0.28f, 0.4f, 0f);
            var rt = (RectTransform)btnGo.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f); // 우하단
            rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = new Vector2(180f, 150f);

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(btnGo.transform, false);
            var t = labelGo.GetComponent<Text>();
            t.font = font;
            t.text = label;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = Color.white;
            t.raycastTarget = false;
            var lrt = (RectTransform)labelGo.transform;

            if (icon != null)
            {
                // 아이콘(상단)
                var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                iconGo.transform.SetParent(btnGo.transform, false);
                var iconImg = iconGo.GetComponent<Image>();
                iconImg.sprite = icon;
                iconImg.preserveAspect = true;
                iconImg.raycastTarget = false;
                var irt = iconImg.rectTransform;
                irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 1f);
                irt.pivot = new Vector2(0.5f, 1f);
                irt.anchoredPosition = new Vector2(0f, -2f);   // 상단 여백 축소
                irt.sizeDelta = new Vector2(104f, 104f);        // 아이콘 확대

                // 텍스트(아이콘 아래) — 크게 + 볼드, 여백 축소
                t.fontSize = 30;
                t.fontStyle = FontStyle.Bold;
                lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0f);
                lrt.pivot = new Vector2(0.5f, 0f);
                lrt.anchoredPosition = new Vector2(0f, 4f);
                lrt.sizeDelta = new Vector2(176f, 40f);
            }
            else
            {
                // 폴백: 아이콘이 없으면 기존처럼 텍스트만 버튼 전체 중앙에 표시.
                t.fontSize = 34;
                lrt.anchorMin = Vector2.zero;
                lrt.anchorMax = Vector2.one;
                lrt.offsetMin = Vector2.zero;
                lrt.offsetMax = Vector2.zero;
            }

            btnGo.AddComponent<Button>().onClick.AddListener(onClick);
            return btnGo;
        }

        /// <summary>출석부 패널 토글(UIManager 위임).</summary>
        private void OnAttendanceButton()
        {
            if (UIManager.Instance != null)
            {
                UIManager.Instance.ToggleAttendance();
            }
            else
            {
                Debug.LogWarning("[HUD] UIManager 인스턴스를 찾을 수 없습니다.");
            }
        }

        /// <summary>우편함(메일) 패널 토글(UIManager 위임).</summary>
        private void OnMailButton()
        {
            if (UIManager.Instance != null)
            {
                UIManager.Instance.ToggleMail();
            }
            else
            {
                Debug.LogWarning("[HUD] UIManager 인스턴스를 찾을 수 없습니다.");
            }
        }

        /// <summary>인벤토리 패널 토글(UIManager 위임).</summary>
        private void OnInventoryButton()
        {
            if (UIManager.Instance != null)
            {
                UIManager.Instance.ToggleInventory();
            }
            else
            {
                Debug.LogWarning("[HUD] UIManager 인스턴스를 찾을 수 없습니다.");
            }
        }

        /// <summary>파티 편성 패널 토글(UIManager 위임).</summary>
        private void OnPartyButton()
        {
            if (UIManager.Instance != null)
            {
                UIManager.Instance.ToggleParty();
            }
            else
            {
                Debug.LogWarning("[HUD] UIManager 인스턴스를 찾을 수 없습니다.");
            }
        }

        /// <summary>스테이지 선택 패널 토글(UIManager 위임).</summary>
        private void OnStageButton()
        {
            if (UIManager.Instance != null)
            {
                UIManager.Instance.ToggleStage();
            }
            else
            {
                Debug.LogWarning("[HUD] UIManager 인스턴스를 찾을 수 없습니다.");
            }
        }

        // ── ESC 메뉴(타이틀로 돌아가기) ──

        /// <summary>ESC로 토글하는 메뉴 오버레이(딤 + '타이틀로 돌아가기' / '계속하기' / '게임종료')를 최상단 캔버스로 구성한다(처음엔 숨김).</summary>
        private void BuildEscMenu(Font font)
        {
            var canvasGo = new GameObject("EscMenuCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200; // 패널(100)·오프라인 팝업(110)보다 위
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            _escMenuRoot = canvasGo;

            // 딤(클릭 시 닫힘 = 계속하기).
            var dim = new GameObject("Dim", typeof(RectTransform), typeof(Image));
            dim.transform.SetParent(canvasGo.transform, false);
            var dimRt = (RectTransform)dim.transform;
            dimRt.anchorMin = Vector2.zero; dimRt.anchorMax = Vector2.one;
            dimRt.offsetMin = Vector2.zero; dimRt.offsetMax = Vector2.zero;
            dim.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f); // 배경을 어둡게 하지 않는다 — 밖 클릭 닫기용 투명 차단막
            var dimBtn = dim.AddComponent<Button>();
            dimBtn.transition = Selectable.Transition.None;
            dimBtn.onClick.AddListener(HideEscMenu);

            // 메뉴 패널(중앙).
            var panel = new GameObject("EscPanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(canvasGo.transform, false);
            var prt = (RectTransform)panel.transform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(560f, 540f);
            prt.anchoredPosition = Vector2.zero;
            panel.GetComponent<Image>().color = new Color(0.10f, 0.12f, 0.18f, 0.98f);

            var title = MakeMenuText(font, panel.transform, "메뉴", 48, new Vector2(0f, 200f), 480f);
            title.fontStyle = FontStyle.Bold;

            MakeMenuButton(font, panel.transform, "타이틀로 돌아가기", new Vector2(0f, 70f), OnReturnToTitle);
            MakeMenuButton(font, panel.transform, "계속하기", new Vector2(0f, -50f), HideEscMenu);
            MakeMenuButton(font, panel.transform, "게임종료", new Vector2(0f, -170f), OnQuitGame);

            canvasGo.SetActive(false); // 처음엔 숨김
        }

        /// <summary>ESC 메뉴 표시/숨김 토글.</summary>
        private void ToggleEscMenu()
        {
            if (_escMenuRoot != null)
            {
                bool show = !_escMenuRoot.activeSelf;
                _escMenuRoot.SetActive(show);
                // 메뉴/패널 표시 상태를 창 제어기에 알린다(GameScene 창은 항상 확장이라 크기는 안 변함).
                TaskbarWindow.Instance?.SetExpanded(show || AnyUiPanelVisible());
            }
        }

        /// <summary>ESC 메뉴를 숨긴다(계속하기).</summary>
        private void HideEscMenu()
        {
            if (_escMenuRoot != null)
            {
                _escMenuRoot.SetActive(false);
                TaskbarWindow.Instance?.SetExpanded(AnyUiPanelVisible());
            }
        }

        /// <summary>UIManager 패널이 하나라도 표시 중인지(창 제어기에 알릴 패널 상태 판정용).</summary>
        private static bool AnyUiPanelVisible()
        {
            return UIManager.Instance != null && UIManager.Instance.IsAnyPanelVisible();
        }

        /// <summary>타이틀 화면으로 돌아간다(게임 세션 종료 후 TitleScene 로드).</summary>
        private void OnReturnToTitle()
        {
            HideEscMenu();
            Time.timeScale = 1f;                 // 전투 슬로우모션 등 배율 복원
            UIManager.Instance?.HideAll();        // 열려 있던 패널 정리
            Session.Clear();                      // 로그아웃(타이틀/로그인 새로 시작)
            MailNotifier.Clear();                 // 우편함 알림 캐시 폐기(다음 계정에 이전 알림이 새지 않도록)
            Debug.Log("[HUD] ESC 메뉴 → 타이틀로 돌아가기");
            if (SceneManager.Instance != null)
            {
                SceneManager.Instance.LoadScene("TitleScene");
            }
            else
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene("TitleScene");
            }
        }

        /// <summary>게임을 종료한다(에디터에서는 플레이 정지).</summary>
        private static void OnQuitGame()
        {
            Debug.Log("[HUD] ESC 메뉴 → 게임종료");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>ESC 메뉴용 중앙 정렬 텍스트를 만든다.</summary>
        private static Text MakeMenuText(Font font, Transform parent, string content, int size, Vector2 pos, float width)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = font;
            t.text = content;
            t.fontSize = size;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = Color.white;
            t.raycastTarget = false;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(width, 64f);
            return t;
        }

        /// <summary>ESC 메뉴용 중앙 버튼(라벨 텍스트를 버튼 전체에 채움)을 만든다.</summary>
        private static void MakeMenuButton(Font font, Transform parent, string label, Vector2 pos,
            UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject($"{label}Button", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(440f, 100f);
            go.GetComponent<Image>().color = new Color(0.25f, 0.28f, 0.4f, 1f);

            var t = MakeMenuText(font, go.transform, label, 36, Vector2.zero, 420f);
            var trt = (RectTransform)t.transform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;

            go.AddComponent<Button>().onClick.AddListener(onClick);
        }
    }
}
