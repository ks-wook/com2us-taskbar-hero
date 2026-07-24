using UnityEngine;
using UnityEngine.UI;
using TaskbarHero.Client.Managers;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// GameScene 상시 HUD. 현재는 인벤토리 토글 버튼만 코드로 구성해 UIManager로 패널을 열고 닫는다.
    /// (HUD 재화/메뉴바 등은 후속 확장)
    /// </summary>
    public class GameSceneHudController : MonoBehaviour
    {
        [Header("메뉴 버튼 아이콘 (에디터 빌더가 배선: Assets/Art/Icon)")]
        [SerializeField] private Sprite partyIcon;      // 편성
        [SerializeField] private Sprite stageIcon;      // 스테이지
        [SerializeField] private Sprite inventoryIcon;  // 가방

        private void Awake()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildHud(font);
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
    }
}
