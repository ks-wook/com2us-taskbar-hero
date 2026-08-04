using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TaskbarHero.Client.Managers;
using TaskbarHero.Client.UI;

namespace TaskbarHero.ClientEditor
{
    /// <summary>
    /// 스테이지 선택 UI 프리팹 생성 + Title/GameScene UIManager 배선을 자동화하는 에디터 도구.
    /// 런타임 UI는 코드로 구성되므로(StagePanelController) 프리팹에 계층이 정적으로 구워진다.
    /// 메뉴: TaskbarHero/UI/스테이지 선택 패널 생성
    /// </summary>
    public static class StageUiBuilder
    {
        private const string ArtDir = "Assets/Art/UI/Stage";
        // 지역 창 배경은 공용 모달 배경을 재사용한다(Stage 전용 아트가 없어 팝업 톤을 맞춘다).
        private const string RegionWindowBgPath = "Assets/Art/UI/modal_bg.png";
        // 지역별 스테이지 창 배경(1~5지역 = 평원·얼음·화산·사막·묘지). 파일명 숫자가 곧 지역 번호다.
        private const string RegionBgDir = "Assets/Art/Background/stage_ui_bg";
        // 지역 창 테두리 — 가운데가 빈 9-slice 프레임(거래소 픽셀 UI 키트 공용 아트를 재사용한다).
        private const string RegionWindowFramePath =
            "Assets/Art/UI/Trade/01_Frames_Panels/window_frame_hollow.png";
        private const string PrefabPath = "Assets/Prefabs/UI/StagePanel.prefab";
        private const string GameScenePath = "Assets/Scenes/GameScene.unity";
        private const string TitleScenePath = "Assets/Scenes/TitleScene.unity";

        [MenuItem("TaskbarHero/UI/스테이지 선택 패널 생성")]
        public static void Build()
        {
            var prefab = BuildPrefab();
            AssignToScene(TitleScenePath, prefab, false);
            AssignToScene(GameScenePath, prefab, true);
            AssetDatabase.SaveAssets();
            Debug.Log("[StageUiBuilder] 완료: 프리팹 생성 + Title/GameScene UIManager 배선.");
        }

        /// <summary>StagePanel 프리팹(컨트롤러 + 스프라이트 참조)을 Assets/Prefabs/UI에 저장한다.</summary>
        private static GameObject BuildPrefab()
        {
            EnsureFolder("Assets/Prefabs");
            EnsureFolder("Assets/Prefabs/UI");

            // 사용자가 조정한 PanelRoot 크기 보존. 위치는 보존하지 않는다 — 패널은
            // SidePanel.Dock이 전투 화면 오른쪽 옆(일정 간격)에 붙이므로, 옛 좌표를 되살리면 도킹이 어긋난다.
            Vector2? keepSize = null;
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null)
            {
                var pr = FindChild(existing.transform, "PanelRoot") as RectTransform;
                if (pr != null)
                {
                    keepSize = pr.sizeDelta;
                }
            }

            var root = new GameObject("StagePanel");
            var ctrl = root.AddComponent<StagePanelController>();

            var so = new SerializedObject(ctrl);
            so.FindProperty("nodeUnlocked").objectReferenceValue = LoadSprite("ui_stage_node_unlocked");
            so.FindProperty("nodeLocked").objectReferenceValue = LoadSprite("ui_stage_node_locked");
            so.FindProperty("iconLock").objectReferenceValue = LoadSprite("ui_icon_lock");
            so.FindProperty("nodeHighlight").objectReferenceValue = LoadSprite("ui_node_highlight");
            so.FindProperty("pathConnector").objectReferenceValue = LoadSprite("ui_path_connector");
            so.FindProperty("nameplateBar").objectReferenceValue = LoadSprite("ui_nameplate_bar");
            so.FindProperty("mapBackground").objectReferenceValue = LoadSprite("dungeon_map_bg");
            so.FindProperty("regionWindowBackground").objectReferenceValue = LoadSpriteAt(RegionWindowBgPath);
            so.FindProperty("regionWindowFrame").objectReferenceValue = LoadSpriteAt(RegionWindowFramePath);
            FillRegionBackgrounds(so.FindProperty("regionBackgrounds"));
            so.FindProperty("iconCleared").objectReferenceValue = LoadSprite("stage_cleared");
            so.FindProperty("iconInProgress").objectReferenceValue = LoadSprite("stage_ing");
            so.ApplyModifiedPropertiesWithoutUndo();

            ctrl.EditorConstruct();

            if (keepSize.HasValue)
            {
                var pr = FindChild(root.transform, "PanelRoot") as RectTransform;
                if (pr != null)
                {
                    pr.sizeDelta = keepSize.Value;
                    SidePanel.Dock(pr, SidePanel.Side.Right); // 크기 복원 후 도킹 좌표를 다시 잡는다
                }
            }

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            // 일부 스프라이트(별/해골)가 SaveAsPrefabAsset(root) 경로에서 유지되지 않아,
            // 저장된 프리팹을 다시 열어 상태 아이콘 스프라이트를 확실히 배정한다.
            var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            var cso = new SerializedObject(contents.GetComponent<StagePanelController>());
            cso.FindProperty("iconCleared").objectReferenceValue = LoadSprite("stage_cleared");
            cso.FindProperty("iconInProgress").objectReferenceValue = LoadSprite("stage_ing");
            cso.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
            PrefabUtility.UnloadPrefabContents(contents);

            Debug.Log($"[StageUiBuilder] 프리팹 저장: {PrefabPath}");
            return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        }

        /// <summary>지정 씬의 UIManager에 stagePanelPrefab을 배선한다(GameScene은 저장만).</summary>
        private static void AssignToScene(string scenePath, GameObject stagePrefab, bool isGameScene)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var uiManager = Object.FindAnyObjectByType<UIManager>(FindObjectsInactive.Include);
            if (uiManager == null)
            {
                Debug.LogWarning($"[StageUiBuilder] {scenePath}에 UIManager가 없어 건너뜁니다.");
                return;
            }
            var so = new SerializedObject(uiManager);
            so.FindProperty("stagePanelPrefab").objectReferenceValue = stagePrefab;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[StageUiBuilder] {scenePath} UIManager 배선 완료.");
        }

        private static Transform FindChild(Transform root, string name)
        {
            if (root.name == name)
            {
                return root;
            }
            foreach (Transform c in root)
            {
                var r = FindChild(c, name);
                if (r != null)
                {
                    return r;
                }
            }
            return null;
        }

        /// <summary>
        /// 지역별 스테이지 창 배경 5장을 배선한다(<c>stage_ui_bg_1~5</c> = 1~5지역).
        /// 없는 지역은 비워 두며, 컨트롤러가 기본(모달) 배경으로 폴백한다.
        /// </summary>
        private static void FillRegionBackgrounds(SerializedProperty array)
        {
            const int regions = 5;
            array.arraySize = regions;
            int found = 0;
            for (int i = 0; i < regions; i++)
            {
                var sprite = LoadSpriteAt($"{RegionBgDir}/stage_ui_bg_{i + 1}.png");
                array.GetArrayElementAtIndex(i).objectReferenceValue = sprite;
                if (sprite != null)
                {
                    found++;
                }
            }
            Debug.Log($"[StageUiBuilder] 지역 배경 배선: {found}/{regions}장");
        }

        private static Sprite LoadSprite(string fileName)
        {
            return LoadSpriteAt($"{ArtDir}/{fileName}.png");
        }

        /// <summary>지정 경로에서 스프라이트를 로드한다(Multiple 임포트면 첫 서브 스프라이트로 폴백 — 임포트 설정은 바꾸지 않는다).</summary>
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
            Debug.LogWarning($"[StageUiBuilder] 스프라이트를 찾지 못했습니다: {path}");
            return null;
        }

        private static void EnsureFolder(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                int idx = path.LastIndexOf('/');
                AssetDatabase.CreateFolder(path.Substring(0, idx), path.Substring(idx + 1));
            }
        }
    }
}
