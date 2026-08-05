using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using TaskbarHero.Client.Managers;
using TaskbarHero.Client.UI;

namespace TaskbarHero.ClientEditor
{
    /// <summary>
    /// 인벤토리 UI 프리팹 생성 + GameScene 배선을 자동화하는 에디터 도구.
    /// 런타임 UI는 코드로 구성되므로(InventoryPanelController) 프리팹은 컨트롤러 + 스프라이트 참조만 담는다.
    /// 메뉴: TaskbarHero/UI/인벤토리 패널·씬 생성
    /// </summary>
    public static class InventoryUiBuilder
    {
        private const string ArtDir = "Assets/Art/UI/Inventory";
        // UI 프리팹은 Assets/Prefabs/ 아래 카테고리 폴더로 정리한다.
        private const string PrefabPath = "Assets/Prefabs/UI/InventoryPanel.prefab";
        private const string ItemSlotPrefabPath = "Assets/Prefabs/UI/ItemSlot.prefab";
        private const string GameScenePath = "Assets/Scenes/GameScene.unity";
        private const string TitleScenePath = "Assets/Scenes/TitleScene.unity";

        [MenuItem("TaskbarHero/UI/인벤토리 패널·씬 생성")]
        public static void Build()
        {
            var prefab = BuildPrefab();
            AssignToTitleScene(prefab);
            WireGameScene(prefab);
            AssetDatabase.SaveAssets();
            Debug.Log("[InventoryUiBuilder] 완료: 프리팹 생성 + Title/GameScene UIManager 배선.");
        }

        /// <summary>InventoryPanel 프리팹(컨트롤러 + 4개 스프라이트 참조)을 Assets/Prefabs/UI에 저장한다.</summary>
        private static GameObject BuildPrefab()
        {
            EnsureFolder("Assets/Prefabs");
            EnsureFolder("Assets/Prefabs/UI");

            // 사용자가 에디터에서 조정한 PanelRoot 크기를 보존한다(재생성 시 초기화 방지).
            // 위치는 보존하지 않는다 — 패널은 SidePanel.Dock이 전투 화면 오른쪽 옆(일정 간격)에 붙이므로,
            // 옛 좌표를 되살리면 도킹이 어긋난다.
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

            var root = new GameObject("InventoryPanel");
            var ctrl = root.AddComponent<InventoryPanelController>();

            var so = new SerializedObject(ctrl);
            so.FindProperty("panelBackground").objectReferenceValue = LoadSprite("ui_panel_background");
            so.FindProperty("slotNormal").objectReferenceValue = LoadSprite("ui_slot_normal");
            so.FindProperty("slotHighlight").objectReferenceValue = LoadSprite("ui_slot_highlight");
            so.FindProperty("slotPortrait").objectReferenceValue = LoadSprite("ui_slot_portrait");
            // 가방 칸·장비 부위 칸의 아이템 그림은 공용 슬롯 프리팹이 그린다(큐브·거래소와 외형 통일).
            so.FindProperty("_itemSlotPrefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>(ItemSlotPrefabPath);
            WireClassCharacters(so); // 초상화 캐릭터 프리팹(기사1·레인저2·마법사3)
            so.ApplyModifiedPropertiesWithoutUndo();

            // 전체 계층을 에디터에서 생성해 프리팹에 정적으로 굽는다(에디터에서 바로 보이도록).
            ctrl.EditorConstruct();

            // 툴팁 배경을 공용 아이템 상세 배경(item_detail_bg)으로 배선(상세 팝업 외형 통일, 재빌드 시 유지).
            var tooltip = root.GetComponentInChildren<InventoryTooltip>(true);
            if (tooltip != null)
            {
                var tso = new SerializedObject(tooltip);
                tso.FindProperty("_backgroundSprite").objectReferenceValue =
                    LoadSpriteAt("Assets/Art/UI/item_detail_bg.png");
                tso.ApplyModifiedPropertiesWithoutUndo();
            }

            // 보존한 사용자 조정 크기 재적용(장비 영역은 중앙 앵커라 폭이 바뀌어도 중앙 유지).
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
            Debug.Log($"[InventoryUiBuilder] 프리팹 저장: {PrefabPath}");
            return prefab;
        }

        /// <summary>GameScene에 EventSystem·UIManager·HUD를 배치하고 저장한다.</summary>
        private static void WireGameScene(GameObject inventoryPrefab)
        {
            var scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);

            // EventSystem (새 Input System 모듈)
            if (Object.FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include) == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem),
                    typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
                Undo.RegisterCreatedObjectUndo(es, "Create EventSystem");
            }

            // UIManager (GameScene에서 직접 실행/테스트 가능하도록 배치)
            var uiManager = Object.FindAnyObjectByType<UIManager>(FindObjectsInactive.Include);
            if (uiManager == null)
            {
                var go = new GameObject("UIManager", typeof(UIManager));
                uiManager = go.GetComponent<UIManager>();
            }
            var so = new SerializedObject(uiManager);
            so.FindProperty("inventoryPanelPrefab").objectReferenceValue = inventoryPrefab;
            so.FindProperty("showOnStart").boolValue = false; // GameScene에서는 자동 표시 안 함
            so.ApplyModifiedPropertiesWithoutUndo();

            // HUD (인벤토리 토글 버튼)
            if (Object.FindAnyObjectByType<GameSceneHudController>(FindObjectsInactive.Include) == null)
            {
                var hud = new GameObject("GameSceneHud", typeof(GameSceneHudController));
                Undo.RegisterCreatedObjectUndo(hud, "Create HUD");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[InventoryUiBuilder] GameScene 배선 저장 완료.");
        }

        /// <summary>TitleScene의 UIManager에도 인벤토리 프리팹 참조를 지정한다
        /// (DontDestroyOnLoad 인스턴스가 GameScene까지 유지되므로).</summary>
        private static void AssignToTitleScene(GameObject inventoryPrefab)
        {
            var scene = EditorSceneManager.OpenScene(TitleScenePath, OpenSceneMode.Single);
            var uiManager = Object.FindAnyObjectByType<UIManager>(FindObjectsInactive.Include);
            if (uiManager == null)
            {
                Debug.LogWarning("[InventoryUiBuilder] TitleScene에 UIManager가 없어 건너뜁니다.");
                return;
            }
            var so = new SerializedObject(uiManager);
            so.FindProperty("inventoryPanelPrefab").objectReferenceValue = inventoryPrefab;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[InventoryUiBuilder] TitleScene UIManager에 인벤토리 프리팹 배선 완료.");
        }

        /// <summary>초상화용 직업별 캐릭터 프리팹(_classCharacters)을 배선한다. classCode: 기사1·레인저2·마법사3.</summary>
        private static void WireClassCharacters(SerializedObject so)
        {
            var prop = so.FindProperty("_classCharacters");
            if (prop == null)
            {
                Debug.LogWarning("[InventoryUiBuilder] _classCharacters 프로퍼티를 찾지 못했습니다.");
                return;
            }
            prop.arraySize = 3;
            SetClassCharacter(prop, 0, 1, "Assets/Prefabs/Character/Knight_Male.prefab");
            SetClassCharacter(prop, 1, 2, "Assets/Prefabs/Character/Archer_Male.prefab");
            SetClassCharacter(prop, 2, 3, "Assets/Prefabs/Character/Mage_Female.prefab");
        }

        /// <summary>_classCharacters 배열의 index번째 요소에 classCode와 프리팹을 지정한다.</summary>
        private static void SetClassCharacter(SerializedProperty arrayProp, int index, int classCode, string prefabPath)
        {
            var element = arrayProp.GetArrayElementAtIndex(index);
            element.FindPropertyRelative("classCode").intValue = classCode;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[InventoryUiBuilder] 캐릭터 프리팹을 찾지 못했습니다: {prefabPath}");
            }
            element.FindPropertyRelative("prefab").objectReferenceValue = prefab;
        }

        /// <summary>이름으로 자식 Transform을 재귀 검색한다.</summary>
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

        /// <summary>스프라이트 로드(Single/Multiple 모두 대응).</summary>
        private static Sprite LoadSprite(string fileName)
        {
            return LoadSpriteAt($"{ArtDir}/{fileName}.png");
        }

        /// <summary>에셋 경로에서 스프라이트를 로드한다(Single/Multiple 모두 대응).</summary>
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
            Debug.LogWarning($"[InventoryUiBuilder] 스프라이트를 찾지 못했습니다: {path}");
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
