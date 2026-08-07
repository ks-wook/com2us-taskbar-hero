using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TaskbarHero.Client.Managers;
using TaskbarHero.Client.UI;
using TaskbarHero.Client.UI.Trade;

namespace TaskbarHero.ClientEditor
{
    /// <summary>
    /// 거래소 UI 빌드 도구. 세 가지를 한 번에 처리한다.
    /// <list type="number">
    /// <item>Assets/Art/UI/Trade 픽셀 키트를 임포트 규격(Sprite/Single · Point 필터 · 무압축 · Full Rect)으로
    ///   교정하고, 9-slice 대상 스프라이트에 키트 README의 Border 값을 적용한다.</item>
    /// <item>거래소 패널 정적 계층을 <see cref="TradePanelController.EditorConstruct"/>로 구성해 프리팹으로 굽는다.</item>
    /// <item>Title/GameScene의 UIManager에 프리팹을, GameScene HUD에 거래소 아이콘을 배선한다.</item>
    /// </list>
    /// 메뉴: TaskbarHero/UI/거래소 패널·씬 배선
    /// </summary>
    public static class TradeUiBuilder
    {
        private const string PrefabPath = "Assets/Prefabs/UI/TradePanel.prefab";
        private const string ItemSlotPrefabPath = "Assets/Prefabs/UI/ItemSlot.prefab";
        private const string ArtDir = "Assets/Art/UI/Trade";
        // 창 본체 배경은 거래소 전용 프레임(window_frame) 대신 다른 패널과 같은 공용 프레임을 쓴다.
        private const string PanelBgPath = "Assets/Art/UI/ui_bg_2.png";
        private const string TradeIconPath = ArtDir + "/거래소.png";
        private const string GameScenePath = "Assets/Scenes/GameScene.unity";
        private const string TitleScenePath = "Assets/Scenes/TitleScene.unity";

        /// <summary>9-slice 경계값(키트 README 3장 표). 값이 없는 스프라이트는 Simple로 쓴다.</summary>
        private static readonly Dictionary<string, Vector4> SliceBorders = new Dictionary<string, Vector4>
        {
            // 01_Frames_Panels — (L, B, R, T) 순서(Unity spriteBorder 규약)
            { "01_Frames_Panels/badge_currency", new Vector4(7, 7, 7, 7) },
            { "01_Frames_Panels/bar_table_header", new Vector4(6, 6, 6, 6) },
            { "01_Frames_Panels/field_search", new Vector4(7, 7, 7, 7) },
            { "01_Frames_Panels/inset_dark", new Vector4(6, 6, 6, 6) },
            { "01_Frames_Panels/panel_parchment", new Vector4(8, 8, 8, 8) },
            { "01_Frames_Panels/panel_tooltip", new Vector4(7, 7, 7, 7) },
            { "01_Frames_Panels/panel_wood", new Vector4(10, 10, 10, 10) },
            { "01_Frames_Panels/row_alt", new Vector4(6, 6, 6, 6) },
            { "01_Frames_Panels/row_hover", new Vector4(6, 6, 6, 6) },
            { "01_Frames_Panels/row_normal", new Vector4(6, 6, 6, 6) },
            { "01_Frames_Panels/row_selected", new Vector4(6, 6, 6, 6) },
            { "01_Frames_Panels/window_frame", new Vector4(20, 20, 20, 20) },
            { "01_Frames_Panels/window_frame_hollow", new Vector4(20, 20, 20, 20) },
            // 02_Buttons — 세로 그라디언트 보존을 위해 T/B는 6
            { "02_Buttons/btn_blue_disabled", new Vector4(8, 6, 8, 6) },
            { "02_Buttons/btn_blue_hover", new Vector4(8, 6, 8, 6) },
            { "02_Buttons/btn_blue_normal", new Vector4(8, 6, 8, 6) },
            { "02_Buttons/btn_blue_pressed", new Vector4(8, 6, 8, 6) },
            { "02_Buttons/btn_category_hover", new Vector4(9, 8, 9, 8) },
            { "02_Buttons/btn_category_normal", new Vector4(9, 8, 9, 8) },
            { "02_Buttons/btn_category_selected", new Vector4(9, 8, 9, 8) },
            { "02_Buttons/btn_gold_disabled", new Vector4(8, 6, 8, 6) },
            { "02_Buttons/btn_gold_hover", new Vector4(8, 6, 8, 6) },
            { "02_Buttons/btn_gold_normal", new Vector4(8, 6, 8, 6) },
            { "02_Buttons/btn_gold_pressed", new Vector4(8, 6, 8, 6) },
            { "02_Buttons/btn_page_hover", new Vector4(5, 5, 5, 5) },
            { "02_Buttons/btn_page_normal", new Vector4(5, 5, 5, 5) },
            { "02_Buttons/btn_page_pressed", new Vector4(5, 5, 5, 5) },
            { "02_Buttons/btn_page_sel_hover", new Vector4(5, 5, 5, 5) },
            { "02_Buttons/btn_page_sel_normal", new Vector4(5, 5, 5, 5) },
            { "02_Buttons/btn_page_sel_pressed", new Vector4(5, 5, 5, 5) },
            { "02_Buttons/btn_red_disabled", new Vector4(8, 6, 8, 6) },
            { "02_Buttons/btn_red_hover", new Vector4(8, 6, 8, 6) },
            { "02_Buttons/btn_red_normal", new Vector4(8, 6, 8, 6) },
            { "02_Buttons/btn_red_pressed", new Vector4(8, 6, 8, 6) },
            { "02_Buttons/btn_wood_disabled", new Vector4(8, 6, 8, 6) },
            { "02_Buttons/btn_wood_hover", new Vector4(8, 6, 8, 6) },
            { "02_Buttons/btn_wood_normal", new Vector4(8, 6, 8, 6) },
            { "02_Buttons/btn_wood_pressed", new Vector4(8, 6, 8, 6) },
            // 03_Slots_Frames
            { "03_Slots_Frames/frame_avatar", new Vector4(5, 5, 5, 5) },
            { "03_Slots_Frames/slot_common", new Vector4(5, 5, 5, 5) },
            { "03_Slots_Frames/slot_empty", new Vector4(5, 5, 5, 5) },
            { "03_Slots_Frames/slot_epic", new Vector4(5, 5, 5, 5) },
            { "03_Slots_Frames/slot_legendary", new Vector4(5, 5, 5, 5) },
            { "03_Slots_Frames/slot_quest", new Vector4(5, 5, 5, 5) },
            { "03_Slots_Frames/slot_rare", new Vector4(5, 5, 5, 5) },
            { "03_Slots_Frames/slot_uncommon", new Vector4(5, 5, 5, 5) },
            // 05_Ornaments — banner_center·divider_gold는 가로만 늘린다(T/B = 0)
            { "05_Ornaments/banner_center", new Vector4(6, 0, 6, 0) },
            { "05_Ornaments/banner_ribbon", new Vector4(14, 6, 14, 6) },
            { "05_Ornaments/divider_gold", new Vector4(8, 0, 8, 0) },
            { "05_Ornaments/scroll_handle", new Vector4(4, 4, 4, 4) },
            { "05_Ornaments/scroll_track", new Vector4(4, 4, 4, 4) },
        };

        [MenuItem("TaskbarHero/UI/거래소 패널·씬 배선")]
        public static void Build()
        {
            ImportTradeArt();
            var prefab = BuildPrefab();
            AssignToScene(TitleScenePath, prefab, false);
            AssignToScene(GameScenePath, prefab, true);
            AssetDatabase.SaveAssets();
            Debug.Log("[TradeUiBuilder] 완료: 거래소 아트 임포트 교정 + 패널 프리팹 생성 + Title/GameScene 배선.");
        }

        /// <summary>거래소 아트를 픽셀아트 규격으로 교정한다(Point 필터·무압축·Full Rect·9-slice Border).
        /// 이미 규격에 맞으면 재임포트하지 않는다.</summary>
        private static void ImportTradeArt()
        {
            int fixedCount = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ArtDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null)
                {
                    continue;
                }

                bool changed = false;
                if (ti.textureType != TextureImporterType.Sprite) { ti.textureType = TextureImporterType.Sprite; changed = true; }
                if (ti.spriteImportMode != SpriteImportMode.Single) { ti.spriteImportMode = SpriteImportMode.Single; changed = true; }
                if (ti.filterMode != FilterMode.Point) { ti.filterMode = FilterMode.Point; changed = true; }
                if (ti.textureCompression != TextureImporterCompression.Uncompressed)
                {
                    ti.textureCompression = TextureImporterCompression.Uncompressed; changed = true;
                }
                if (ti.mipmapEnabled) { ti.mipmapEnabled = false; changed = true; }
                if (ti.wrapMode != TextureWrapMode.Clamp) { ti.wrapMode = TextureWrapMode.Clamp; changed = true; }
                // Mesh Type은 TextureImporterSettings 경유로만 접근된다(9-slice에 Full Rect 필수).
                var settings = new TextureImporterSettings();
                ti.ReadTextureSettings(settings);
                if (settings.spriteMeshType != SpriteMeshType.FullRect)
                {
                    settings.spriteMeshType = SpriteMeshType.FullRect;
                    ti.SetTextureSettings(settings);
                    changed = true;
                }

                var border = BorderFor(path);
                if (ti.spriteBorder != border) { ti.spriteBorder = border; changed = true; }

                if (changed)
                {
                    ti.SaveAndReimport();
                    fixedCount++;
                }
            }
            Debug.Log($"[TradeUiBuilder] 아트 임포트 교정: {fixedCount}개 재임포트");
        }

        /// <summary>경로에서 9-slice Border를 찾는다(표에 없으면 0 = Simple 사용).</summary>
        private static Vector4 BorderFor(string assetPath)
        {
            foreach (var pair in SliceBorders)
            {
                if (assetPath.EndsWith(pair.Key + ".png", System.StringComparison.Ordinal))
                {
                    return pair.Value;
                }
            }
            return Vector4.zero;
        }

        private static GameObject BuildPrefab()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs")) AssetDatabase.CreateFolder("Assets", "Prefabs");
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs/UI")) AssetDatabase.CreateFolder("Assets/Prefabs", "UI");

            var root = new GameObject("TradePanel");
            var ctrl = root.AddComponent<TradePanelController>();

            // EditorConstruct 전에 배선해 정적 계층이 아트를 바로 반영하게 한다.
            var so = new SerializedObject(ctrl);
            so.FindProperty("_windowFrame").objectReferenceValue = LoadSpriteAt(PanelBgPath);
            so.FindProperty("_panelParchment").objectReferenceValue = Load("01_Frames_Panels/panel_parchment");
            so.FindProperty("_panelWood").objectReferenceValue = Load("01_Frames_Panels/panel_wood");
            so.FindProperty("_tableHeader").objectReferenceValue = Load("01_Frames_Panels/bar_table_header");
            so.FindProperty("_rowNormal").objectReferenceValue = Load("01_Frames_Panels/row_normal");
            so.FindProperty("_rowAlt").objectReferenceValue = Load("01_Frames_Panels/row_alt");
            so.FindProperty("_fieldSearch").objectReferenceValue = Load("01_Frames_Panels/field_search");
            so.FindProperty("_bannerRibbon").objectReferenceValue = Load("05_Ornaments/banner_ribbon");
            so.FindProperty("_dividerGold").objectReferenceValue = Load("05_Ornaments/divider_gold");
            so.FindProperty("_btnGold").objectReferenceValue = Load("02_Buttons/btn_gold_normal");
            so.FindProperty("_btnRed").objectReferenceValue = Load("02_Buttons/btn_red_normal");
            so.FindProperty("_btnBlue").objectReferenceValue = Load("02_Buttons/btn_blue_normal");
            so.FindProperty("_btnWood").objectReferenceValue = Load("02_Buttons/btn_wood_normal");
            so.FindProperty("_btnClose").objectReferenceValue = Load("02_Buttons/btn_close_normal");
            so.FindProperty("_btnCategory").objectReferenceValue = Load("02_Buttons/btn_category_normal");
            so.FindProperty("_btnCategorySel").objectReferenceValue = Load("02_Buttons/btn_category_selected");
            so.FindProperty("_iconCoin").objectReferenceValue = Load("04_Icons/icon_coin");
            so.FindProperty("_itemSlotPrefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>(ItemSlotPrefabPath);

            // 등급 1~5 → common / uncommon / rare / epic / legendary
            var grades = so.FindProperty("_slotByGrade");
            string[] gradeSlots =
            {
                "03_Slots_Frames/slot_common", "03_Slots_Frames/slot_uncommon", "03_Slots_Frames/slot_rare",
                "03_Slots_Frames/slot_epic", "03_Slots_Frames/slot_legendary",
            };
            grades.arraySize = gradeSlots.Length;
            for (int i = 0; i < gradeSlots.Length; i++)
            {
                grades.GetArrayElementAtIndex(i).objectReferenceValue = Load(gradeSlots[i]);
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            ctrl.EditorConstruct(); // 정적 계층을 프리팹에 굽는다

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log($"[TradeUiBuilder] 프리팹 저장: {PrefabPath}");
            return prefab;
        }

        /// <summary>거래소 아트 폴더 기준 상대 경로로 스프라이트를 로드한다.</summary>
        private static Sprite Load(string relativePath)
        {
            return LoadSpriteAt($"{ArtDir}/{relativePath}.png");
        }

        /// <summary>에셋 경로에서 스프라이트를 로드한다(Single/Multiple 임포트 모두 대응 — Multiple이면
        /// 메인 에셋이 Sprite가 아니므로 서브 스프라이트를 집는다).</summary>
        private static Sprite LoadSpriteAt(string path)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null)
            {
                return sprite;
            }
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (obj is Sprite s)
                {
                    return s;
                }
            }
            Debug.LogWarning($"[TradeUiBuilder] 스프라이트를 찾지 못했습니다: {path}");
            return null;
        }

        /// <summary>씬의 UIManager에 거래소 패널을, GameScene이면 HUD에 거래소 아이콘도 함께 배선한다.</summary>
        private static void AssignToScene(string scenePath, GameObject prefab, bool wireHudIcon)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            var uiManager = Object.FindAnyObjectByType<UIManager>(FindObjectsInactive.Include);
            if (uiManager == null)
            {
                Debug.LogWarning($"[TradeUiBuilder] {scene.name}에 UIManager가 없어 건너뜁니다.");
            }
            else
            {
                var so = new SerializedObject(uiManager);
                so.FindProperty("tradePanelPrefab").objectReferenceValue = prefab;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            if (wireHudIcon)
            {
                var hud = Object.FindAnyObjectByType<GameSceneHudController>(FindObjectsInactive.Include);
                if (hud == null)
                {
                    Debug.LogWarning($"[TradeUiBuilder] {scene.name}에 GameSceneHudController가 없어 아이콘 배선을 건너뜁니다.");
                }
                else
                {
                    var icon = AssetDatabase.LoadAssetAtPath<Sprite>(TradeIconPath);
                    if (icon == null)
                    {
                        Debug.LogWarning($"[TradeUiBuilder] 거래소 아이콘을 찾지 못했습니다: {TradeIconPath}");
                    }
                    var hso = new SerializedObject(hud);
                    hso.FindProperty("tradeIcon").objectReferenceValue = icon;
                    hso.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[TradeUiBuilder] {scene.name} 배선 완료.");
        }
    }
}
