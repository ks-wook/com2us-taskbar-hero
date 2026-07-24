using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TaskbarHero.Client.Managers;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// GameScene 상시 HUD. 인벤토리·편성·스테이지 토글 버튼과 ESC 메뉴(타이틀로 돌아가기)를 코드로 구성한다.
    /// </summary>
    public class GameSceneHudController : MonoBehaviour
    {
        [Header("메뉴 버튼 아이콘 (에디터 빌더가 배선: Assets/Art/Icon)")]
        [SerializeField] private Sprite partyIcon;      // 편성
        [SerializeField] private Sprite stageIcon;      // 스테이지
        [SerializeField] private Sprite inventoryIcon;  // 가방

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

        /// <summary>GameScene 진입 시 대기 중인 오프라인 보상 정산 결과가 있으면 팝업으로 표시한다(Login에서 정산됨).</summary>
        private void Start()
        {
            if (Session.PendingOfflineReward != null && UIManager.Instance != null)
            {
                UIManager.Instance.ShowOfflineReward();
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

            // 우하단: [편성] [스테이지] [가방] — 아이콘 위 + 작은 텍스트 아래
            CreateButton(canvasGo.transform, font, "PartyButton", "편성", partyIcon, new Vector2(-440f, 40f), OnPartyButton);
            CreateButton(canvasGo.transform, font, "StageButton", "스테이지", stageIcon, new Vector2(-240f, 40f), OnStageButton);
            var inventoryBtn = CreateButton(canvasGo.transform, font, "InventoryButton", "가방", inventoryIcon, new Vector2(-40f, 40f), OnInventoryButton);

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

        /// <summary>ESC로 토글하는 메뉴 오버레이(딤 + '타이틀로 돌아가기' / '계속하기')를 최상단 캔버스로 구성한다(처음엔 숨김).</summary>
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
            dim.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);
            var dimBtn = dim.AddComponent<Button>();
            dimBtn.transition = Selectable.Transition.None;
            dimBtn.onClick.AddListener(HideEscMenu);

            // 메뉴 패널(중앙).
            var panel = new GameObject("EscPanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(canvasGo.transform, false);
            var prt = (RectTransform)panel.transform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(560f, 420f);
            prt.anchoredPosition = Vector2.zero;
            panel.GetComponent<Image>().color = new Color(0.10f, 0.12f, 0.18f, 0.98f);

            var title = MakeMenuText(font, panel.transform, "메뉴", 48, new Vector2(0f, 140f), 480f);
            title.fontStyle = FontStyle.Bold;

            MakeMenuButton(font, panel.transform, "타이틀로 돌아가기", new Vector2(0f, 20f), OnReturnToTitle);
            MakeMenuButton(font, panel.transform, "계속하기", new Vector2(0f, -110f), HideEscMenu);

            canvasGo.SetActive(false); // 처음엔 숨김
        }

        /// <summary>ESC 메뉴 표시/숨김 토글.</summary>
        private void ToggleEscMenu()
        {
            if (_escMenuRoot != null)
            {
                bool show = !_escMenuRoot.activeSelf;
                _escMenuRoot.SetActive(show);
                // 메뉴가 열리면 창 확장, 닫히면 남은 패널 여부에 따라 스트립 복귀.
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

        /// <summary>UIManager 패널이 하나라도 표시 중인지(창 스트립/확장 판정용).</summary>
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
