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
        private void Awake()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildHud(font);
        }

        /// <summary>HUD 캔버스와 인벤토리 토글 버튼을 생성·배선한다.</summary>
        private void BuildHud(Font font)
        {
            var canvasGo = new GameObject("HudCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10; // 인벤토리 패널(100)보다 아래
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            var btnGo = new GameObject("InventoryButton", typeof(RectTransform), typeof(Image));
            btnGo.transform.SetParent(canvasGo.transform, false);
            var img = btnGo.GetComponent<Image>();
            img.color = new Color(0.25f, 0.28f, 0.4f, 0.95f);
            var rt = (RectTransform)btnGo.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f); // 우하단
            rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-40f, 40f);
            rt.sizeDelta = new Vector2(180f, 90f);

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(btnGo.transform, false);
            var label = labelGo.GetComponent<Text>();
            label.font = font;
            label.text = "가방";
            label.fontSize = 34;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.raycastTarget = false;
            var lrt = (RectTransform)labelGo.transform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = Vector2.zero;

            var btn = btnGo.AddComponent<Button>();
            btn.onClick.AddListener(OnInventoryButton);
        }

        /// <summary>인벤토리 패널 열고 닫기(UIManager 위임).</summary>
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
    }
}
