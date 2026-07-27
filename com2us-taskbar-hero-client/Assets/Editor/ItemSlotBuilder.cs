using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TaskbarHero.Client.Battle;
using TaskbarHero.Client.Managers;
using TaskbarHero.Client.UI;

namespace TaskbarHero.ClientEditor
{
    /// <summary>
    /// 공용 아이템 슬롯 프리팹(<c>Assets/Prefabs/UI/ItemSlot.prefab</c>) 생성 + 소비처 배선 도구.
    /// 슬롯은 item_slot 프레임 + 등급 배경 + 아이콘 + 수량으로 구성되고, hover 시 item_detail_bg
    /// 배경의 공용 상세 팝업(ItemDetailPopup)을 띄운다(ItemSlotView). 배선 대상:
    /// - StageClearAssets(Resources) → itemSlotPrefab (스테이지 클리어 보상)
    /// - MailPanel.prefab → MailPanelController._itemSlotPrefab (우편함 첨부)
    /// - InventoryPanel.prefab → InventoryTooltip._backgroundSprite (인벤토리 툴팁 배경 통일)
    /// 메뉴: TaskbarHero/UI/아이템 슬롯·상세 팝업 배선
    /// </summary>
    public static class ItemSlotBuilder
    {
        private const string PrefabPath = "Assets/Prefabs/UI/ItemSlot.prefab";
        private const string FrameSpritePath = "Assets/Art/UI/item_slot.png";
        private const string DetailBgSpritePath = "Assets/Art/UI/item_detail_bg.png";
        private const string ClaimedCheckSpritePath = "Assets/Art/UI/Attendance/check.png";
        private const string StageClearAssetPath = "Assets/Resources/StageClearAssets.asset";
        private const string MailPanelPath = "Assets/Prefabs/UI/MailPanel.prefab";
        private const string InventoryPanelPath = "Assets/Prefabs/UI/InventoryPanel.prefab";

        [MenuItem("TaskbarHero/UI/아이템 슬롯·상세 팝업 배선")]
        public static void Build()
        {
            var prefab = BuildPrefab();
            WireStageClearAssets(prefab);
            WireMailPanel(prefab);
            WireInventoryTooltip();
            AssetDatabase.SaveAssets();
            Debug.Log("[ItemSlotBuilder] 완료: 아이템 슬롯 프리팹 생성 + 클리어 연출/메일/인벤토리 배선.");
        }

        /// <summary>슬롯 프리팹(프레임 + 등급 배경 + 아이콘 + 수량 + ItemSlotView)을 생성한다.
        /// 내부 위젯은 앵커 비율 배치라 사용처에서 sizeDelta만 바꿔도 그대로 스케일된다.</summary>
        private static GameObject BuildPrefab()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var frameSprite = LoadSpriteAt(FrameSpritePath);
            var detailBgSprite = LoadSpriteAt(DetailBgSpritePath);
            var claimedCheckSprite = LoadSpriteAt(ClaimedCheckSpritePath);

            var root = new GameObject("ItemSlot", typeof(RectTransform), typeof(Image));
            var rootRt = (RectTransform)root.transform;
            rootRt.sizeDelta = new Vector2(140f, 140f);
            var frame = root.GetComponent<Image>();
            if (frameSprite != null)
            {
                frame.sprite = frameSprite;
                frame.type = Image.Type.Simple;
                frame.color = Color.white;
            }
            else
            {
                frame.color = new Color(0.12f, 0.14f, 0.22f, 0.95f); // 프레임 미배선 폴백
            }
            frame.raycastTarget = true; // hover 상세 감지 대상

            // 등급 배경(테두리 안쪽, 아이콘 뒤) — 앵커 비율로 안쪽 영역을 잡는다.
            var gradeBg = NewChildImage(root.transform, "GradeBg", new Vector2(0.09f, 0.09f), new Vector2(0.91f, 0.91f));

            // 아이템 아이콘.
            var icon = NewChildImage(root.transform, "Icon", new Vector2(0.09f, 0.09f), new Vector2(0.91f, 0.91f));
            icon.preserveAspect = true;

            // 아이콘 대신 텍스트 라벨(경험치 등 아이템 아이콘이 없는 보상, SetupLabel 전용). 기본은 숨김.
            var labelGo = new GameObject("IconLabel", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(root.transform, false);
            var iconLabel = labelGo.GetComponent<Text>();
            iconLabel.font = font;
            iconLabel.fontSize = 34;
            iconLabel.fontStyle = FontStyle.Bold;
            iconLabel.alignment = TextAnchor.MiddleCenter;
            iconLabel.raycastTarget = false;
            iconLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            iconLabel.verticalOverflow = VerticalWrapMode.Overflow;
            var labelRt = (RectTransform)labelGo.transform;
            labelRt.anchorMin = new Vector2(0.09f, 0.09f);
            labelRt.anchorMax = new Vector2(0.91f, 0.91f);
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;
            labelGo.SetActive(false);

            // 수량 텍스트(우하단, BestFit — 슬롯이 작아져도 읽히게).
            var qtyGo = new GameObject("Qty", typeof(RectTransform), typeof(Text), typeof(Outline));
            qtyGo.transform.SetParent(root.transform, false);
            var qty = qtyGo.GetComponent<Text>();
            qty.font = font;
            qty.fontSize = 30;
            qty.fontStyle = FontStyle.Bold;
            qty.alignment = TextAnchor.LowerRight;
            qty.color = Color.white;
            qty.raycastTarget = false;
            qty.resizeTextForBestFit = true;
            qty.resizeTextMinSize = 10;
            qty.resizeTextMaxSize = 30;
            qty.horizontalOverflow = HorizontalWrapMode.Overflow;
            qty.verticalOverflow = VerticalWrapMode.Truncate;
            var outline = qtyGo.GetComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f); // 밝은 아이콘 위 가독성
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            var qrt = (RectTransform)qtyGo.transform;
            qrt.anchorMin = new Vector2(0.08f, 0.06f);
            qrt.anchorMax = new Vector2(0.92f, 0.36f);
            qrt.offsetMin = Vector2.zero;
            qrt.offsetMax = Vector2.zero;

            // 획득 완료 표시(check) — 슬롯 레이어 가장 위(마지막 자식)에 그려진다. 출석부 등에서 SetClaimed로 켠다.
            var claimedOverlay = NewChildImage(root.transform, "ClaimedOverlay", new Vector2(0.09f, 0.09f), new Vector2(0.91f, 0.91f));
            claimedOverlay.sprite = claimedCheckSprite;
            claimedOverlay.preserveAspect = true;
            claimedOverlay.gameObject.SetActive(false);

            var view = root.AddComponent<ItemSlotView>();
            view.EditorInit(frame, gradeBg, icon, qty, iconLabel, claimedOverlay, detailBgSprite);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log($"[ItemSlotBuilder] 프리팹 저장: {PrefabPath}");
            return prefab;
        }

        /// <summary>앵커 비율 배치의 자식 Image를 만든다(레이캐스트 없음).</summary>
        private static Image NewChildImage(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            return img;
        }

        /// <summary>StageClearAssets(Resources)에 슬롯 프리팹을 배선한다(스테이지 클리어 보상 칸).</summary>
        private static void WireStageClearAssets(GameObject prefab)
        {
            var asset = AssetDatabase.LoadAssetAtPath<StageClearAssets>(StageClearAssetPath);
            if (asset == null)
            {
                Debug.LogWarning($"[ItemSlotBuilder] {StageClearAssetPath}가 없어 건너뜁니다. " +
                                 "'TaskbarHero/UI/클리어 연출 에셋 빌드'를 먼저 실행하세요.");
                return;
            }
            asset.itemSlotPrefab = prefab;
            EditorUtility.SetDirty(asset);
            Debug.Log("[ItemSlotBuilder] StageClearAssets.itemSlotPrefab 배선 완료.");
        }

        /// <summary>MailPanel 프리팹의 컨트롤러에 슬롯 프리팹을 배선한다(첨부 표시).</summary>
        private static void WireMailPanel(GameObject prefab)
        {
            var mailPanel = AssetDatabase.LoadAssetAtPath<GameObject>(MailPanelPath);
            var ctrl = mailPanel != null ? mailPanel.GetComponent<MailPanelController>() : null;
            if (ctrl == null)
            {
                Debug.LogWarning($"[ItemSlotBuilder] {MailPanelPath}의 MailPanelController를 찾지 못해 건너뜁니다.");
                return;
            }
            var so = new SerializedObject(ctrl);
            so.FindProperty("_itemSlotPrefab").objectReferenceValue = prefab;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(mailPanel);
            Debug.Log("[ItemSlotBuilder] MailPanelController._itemSlotPrefab 배선 완료.");
        }

        /// <summary>InventoryPanel 프리팹의 툴팁에 상세 배경(item_detail_bg)을 배선한다(팝업 배경 통일).</summary>
        private static void WireInventoryTooltip()
        {
            var panel = AssetDatabase.LoadAssetAtPath<GameObject>(InventoryPanelPath);
            var tooltip = panel != null ? panel.GetComponentInChildren<InventoryTooltip>(true) : null;
            if (tooltip == null)
            {
                Debug.LogWarning($"[ItemSlotBuilder] {InventoryPanelPath}의 InventoryTooltip을 찾지 못해 건너뜁니다.");
                return;
            }
            var so = new SerializedObject(tooltip);
            so.FindProperty("_backgroundSprite").objectReferenceValue = LoadSpriteAt(DetailBgSpritePath);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(panel);
            Debug.Log("[ItemSlotBuilder] InventoryTooltip._backgroundSprite 배선 완료.");
        }

        /// <summary>에셋 경로에서 스프라이트를 로드한다(Single/Multiple 모두 대응).</summary>
        private static Sprite LoadSpriteAt(string path)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null) return sprite;
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (obj is Sprite s) return s;
            }
            Debug.LogWarning($"[ItemSlotBuilder] 스프라이트를 찾지 못했습니다: {path}");
            return null;
        }
    }
}
