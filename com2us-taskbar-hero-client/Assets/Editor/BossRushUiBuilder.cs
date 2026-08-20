using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TaskbarHero.Client.Managers;
using TaskbarHero.Client.UI;
using TaskbarHero.Client.UI.BossRush;

namespace TaskbarHero.ClientEditor
{
    /// <summary>
    /// 보스 러시 UI 빌드 도구. 두 가지를 처리한다.
    /// <list type="number">
    /// <item>보스 러시 패널 정적 계층을 <see cref="BossRushPanelController.EditorConstruct"/>로 구성해
    ///   <c>Assets/Prefabs/UI/BossRushPanel.prefab</c>으로 굽는다.</item>
    /// <item>Title/GameScene의 UIManager에 그 프리팹을, GameScene HUD에 보스 러시 아이콘
    ///   (<c>Assets/Art/UI/BossRush/ranking.png</c>)을 배선한다. 창 상단 중앙 엠블럼은
    ///   <c>Assets/Art/UI/BossRush/boss_rush.png</c>이다.</item>
    /// </list>
    /// 아트는 <b>읽어서 배선만</b> 한다 — 공용 아트의 임포트 설정은 건드리지 않는다(프로젝트 규칙:
    /// Multiple 임포트 텍스처를 Single로 바꾸면 서브 스프라이트가 사라져 기존 참조가 끊긴다).
    /// 메뉴: TaskbarHero/UI/보스 러시 패널·씬 배선
    /// </summary>
    public static class BossRushUiBuilder
    {
        private const string PrefabPath = "Assets/Prefabs/UI/BossRushPanel.prefab";
        private const string PanelBgPath = "Assets/Art/UI/ui_bg_2.png";
        private const string MonsterPrefabDir = "Assets/Prefabs/Character/Monster";
        // 라운드 1~5 보스(Act별 9N99). 초상에 세울 프리팹을 이 순서로 배선한다.
        private static readonly int[] BossMonsterCodes = { 9099, 9199, 9299, 9399, 9499 };
        // 하단 메뉴 아이콘(다른 칸과 같은 크기)과 창 상단 중앙 엠블럼은 서로 다른 아트를 쓴다.
        private const string BossRushIconPath = "Assets/Art/UI/BossRush/ranking.png";
        private const string TitleEmblemPath = "Assets/Art/UI/BossRush/boss_rush.png";
        // 탭·도전 시작 버튼은 로그인 화면과 같은 공용 픽셀 UI 아트를 쓴다(플레이 모드에서 확정).
        private const string TabSpritePath = "Assets/Art/UI/pixel_rpg_input_field.png";
        private const string ButtonSpritePath = "Assets/Art/UI/pixel_rpg_button.png";
        // 순위 보상 상세 팝업은 공용 아이템 상세 팝업과 같은 배경 아트를 쓰고, 보상 칸은 공용 아이템 슬롯을 쓴다.
        private const string PopupBgPath = "Assets/Art/UI/item_detail_bg.png";
        private const string ItemSlotPrefabPath = "Assets/Prefabs/UI/ItemSlot.prefab";
        // 창 안쪽 위젯(리본·표 머리·탭·버튼)은 거래소가 쓰는 픽셀 키트를 그대로 재사용한다.
        private const string KitDir = "Assets/Art/UI/Trade";
        private const string GameScenePath = "Assets/Scenes/GameScene.unity";
        private const string TitleScenePath = "Assets/Scenes/TitleScene.unity";

        [MenuItem("TaskbarHero/UI/보스 러시 패널·씬 배선")]
        public static void Build()
        {
            var prefab = BuildPrefab();
            AssignToScene(TitleScenePath, prefab, false);
            AssignToScene(GameScenePath, prefab, true);
            AssetDatabase.SaveAssets();
            Debug.Log("[BossRushUiBuilder] 완료: 패널 프리팹 생성 + Title/GameScene 배선.");
        }

        /// <summary>패널 프리팹을 만든다. 아트를 먼저 배선한 뒤 정적 계층을 구성해 아트가 즉시 반영되게 한다.</summary>
        private static GameObject BuildPrefab()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs")) AssetDatabase.CreateFolder("Assets", "Prefabs");
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs/UI")) AssetDatabase.CreateFolder("Assets/Prefabs", "UI");

            var root = new GameObject("BossRushPanel");
            var ctrl = root.AddComponent<BossRushPanelController>();

            var so = new SerializedObject(ctrl);
            so.FindProperty("_windowFrame").objectReferenceValue = LoadSpriteAt(PanelBgPath);
            so.FindProperty("_titleEmblem").objectReferenceValue = LoadSpriteAt(TitleEmblemPath);
            so.FindProperty("_tableHeader").objectReferenceValue = LoadKit("01_Frames_Panels/bar_table_header");
            // 탭은 선택/비선택 모두 같은 아트를 쓰고 컨트롤러가 틴트로 구분한다.
            so.FindProperty("_btnCategory").objectReferenceValue = LoadSpriteAt(TabSpritePath);
            so.FindProperty("_btnCategorySel").objectReferenceValue = LoadSpriteAt(TabSpritePath);
            so.FindProperty("_btnGold").objectReferenceValue = LoadSpriteAt(ButtonSpritePath);
            so.FindProperty("_btnWood").objectReferenceValue = LoadKit("02_Buttons/btn_wood_normal");
            so.FindProperty("_popupBackground").objectReferenceValue = LoadSpriteAt(PopupBgPath);
            // 순위 보상 상세의 골드 칸은 공용 슬롯 프리팹을 그대로 쓴다(ItemSlotBuilder도 같은 필드를 배선하지만,
            // 이 프리팹은 여기서 새로 만들어지므로 재생성 직후에도 비어 있지 않도록 함께 배선한다).
            var itemSlot = AssetDatabase.LoadAssetAtPath<GameObject>(ItemSlotPrefabPath);
            if (itemSlot == null)
            {
                Debug.LogWarning($"[BossRushUiBuilder] 공용 아이템 슬롯 프리팹을 찾지 못했습니다: {ItemSlotPrefabPath}");
            }
            so.FindProperty("_itemSlotPrefab").objectReferenceValue = itemSlot;
            // 라운드 보스 프리팹 표(코드 → monster_{code}.prefab).
            var bosses = so.FindProperty("_bossPrefabs");
            bosses.arraySize = BossMonsterCodes.Length;
            for (int i = 0; i < BossMonsterCodes.Length; i++)
            {
                int code = BossMonsterCodes[i];
                var element = bosses.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("monsterCode").intValue = code;
                var bossPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{MonsterPrefabDir}/monster_{code}.prefab");
                if (bossPrefab == null)
                {
                    Debug.LogWarning($"[BossRushUiBuilder] 보스 프리팹을 찾지 못했습니다: monster_{code}");
                }
                element.FindPropertyRelative("prefab").objectReferenceValue = bossPrefab;
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            ctrl.EditorConstruct(); // 정적 계층을 프리팹에 굽는다

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log($"[BossRushUiBuilder] 프리팹 저장: {PrefabPath}");
            return prefab;
        }

        /// <summary>거래소 픽셀 키트 폴더 기준 상대 경로로 스프라이트를 로드한다.</summary>
        private static Sprite LoadKit(string relativePath)
        {
            return LoadSpriteAt($"{KitDir}/{relativePath}.png");
        }

        /// <summary>에셋 경로에서 스프라이트를 로드한다(Multiple 임포트면 서브 스프라이트를 집는다).</summary>
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
            Debug.LogWarning($"[BossRushUiBuilder] 스프라이트를 찾지 못했습니다: {path}");
            return null;
        }

        /// <summary>씬의 UIManager에 패널 프리팹을, GameScene이면 HUD에 보스 러시 아이콘도 함께 배선한다.</summary>
        private static void AssignToScene(string scenePath, GameObject prefab, bool wireHudIcon)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            var uiManager = Object.FindAnyObjectByType<UIManager>(FindObjectsInactive.Include);
            if (uiManager == null)
            {
                Debug.LogWarning($"[BossRushUiBuilder] {scene.name}에 UIManager가 없어 건너뜁니다.");
            }
            else
            {
                var so = new SerializedObject(uiManager);
                so.FindProperty("bossRushPanelPrefab").objectReferenceValue = prefab;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            if (wireHudIcon)
            {
                var hud = Object.FindAnyObjectByType<GameSceneHudController>(FindObjectsInactive.Include);
                if (hud == null)
                {
                    Debug.LogWarning($"[BossRushUiBuilder] {scene.name}에 GameSceneHudController가 없어 아이콘 배선을 건너뜁니다.");
                }
                else
                {
                    var hso = new SerializedObject(hud);
                    hso.FindProperty("bossRushIcon").objectReferenceValue = LoadSpriteAt(BossRushIconPath);
                    hso.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[BossRushUiBuilder] {scene.name} 배선 완료.");
        }
    }
}
