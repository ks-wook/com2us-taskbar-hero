using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TaskbarHero.Client.Managers;
using TaskbarHero.Client.UI;

namespace TaskbarHero.ClientEditor
{
    /// <summary>
    /// 스킬 레벨업 UI 프리팹 생성 + Title/GameScene 배선을 자동화하는 에디터 도구.
    /// 런타임 UI는 코드로 구성되므로(SkillPanelController) 프리팹은 컨트롤러 + 스프라이트 참조만 담는다.
    /// 스킬 아이콘 DB(SkillIconDatabase)도 함께 빌드한다.
    /// 메뉴: TaskbarHero/UI/스킬 패널·씬 생성
    /// </summary>
    public static class SkillUiBuilder
    {
        // 인벤토리 패널과 동일한 UI 스프라이트를 공용으로 사용한다.
        private const string ArtDir = "Assets/Art/UI/Inventory";
        // 캐릭터 전환 화살표는 인벤토리 공용 아트가 아니라 Assets/Art/UI에 있다.
        private const string CharNavArrowPath = "Assets/Art/UI/화살표버튼.png";
        private const string PrefabPath = "Assets/Prefabs/UI/SkillPanel.prefab";
        private const string GameScenePath = "Assets/Scenes/GameScene.unity";
        private const string TitleScenePath = "Assets/Scenes/TitleScene.unity";

        [MenuItem("TaskbarHero/UI/스킬 패널·씬 생성")]
        public static void Build()
        {
            // 스킬 아이콘 DB를 먼저 빌드해 프리팹/런타임이 참조할 수 있게 한다.
            SkillIconDatabaseBuilder.Build();

            var prefab = BuildPrefab();
            AssignToScene(TitleScenePath, prefab);
            AssignToScene(GameScenePath, prefab);
            AssetDatabase.SaveAssets();
            Debug.Log("[SkillUiBuilder] 완료: 프리팹 생성 + Title/GameScene UIManager 배선.");
        }

        /// <summary>SkillPanel 프리팹(컨트롤러 + 스프라이트 참조 + 정적 계층)을 저장한다.</summary>
        private static GameObject BuildPrefab()
        {
            EnsureFolder("Assets/Prefabs");
            EnsureFolder("Assets/Prefabs/UI");

            // 사용자가 에디터에서 조정한 PanelRoot 크기/위치/pivot/앵커를 보존한다(재생성 시 초기화 방지).
            // 주의: 컨트롤러의 기본 배치값을 고쳤을 때도 이 보존이 옛 프리팹 값으로 되돌리므로,
            // 기본값을 바꿨으면 프리팹의 PanelRoot도 함께 갱신해야 반영된다.
            Vector2? keepSize = null;
            Vector2? keepPos = null;
            Vector2? keepPivot = null;
            Vector2? keepAnchorMin = null;
            Vector2? keepAnchorMax = null;
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null)
            {
                var pr = FindChild(existing.transform, "PanelRoot") as RectTransform;
                if (pr != null)
                {
                    keepSize = pr.sizeDelta;
                    keepPos = pr.anchoredPosition;
                    keepPivot = pr.pivot;
                    keepAnchorMin = pr.anchorMin;
                    keepAnchorMax = pr.anchorMax;
                }
            }

            var root = new GameObject("SkillPanel");
            var ctrl = root.AddComponent<SkillPanelController>();

            var so = new SerializedObject(ctrl);
            so.FindProperty("panelBackground").objectReferenceValue = LoadSprite("ui_panel_background");
            so.FindProperty("slotNormal").objectReferenceValue = LoadSprite("ui_slot_normal");
            so.FindProperty("slotHighlight").objectReferenceValue = LoadSprite("ui_slot_highlight");
            // 캐릭터 전환 화살표도 EditorConstruct 전에 배선해야 계층에 함께 구워진다.
            so.FindProperty("charNavArrow").objectReferenceValue = LoadSpriteAt(CharNavArrowPath);
            so.ApplyModifiedPropertiesWithoutUndo();

            // 전체 정적 계층을 에디터에서 생성해 프리팹에 굽는다(에디터에서 바로 보이도록).
            ctrl.EditorConstruct();

            if (keepSize.HasValue)
            {
                var pr = FindChild(root.transform, "PanelRoot") as RectTransform;
                if (pr != null)
                {
                    // 앵커·pivot을 먼저 되돌린 뒤 위치를 넣는다(둘 다 위치 해석을 바꾼다).
                    pr.anchorMin = keepAnchorMin.Value;
                    pr.anchorMax = keepAnchorMax.Value;
                    pr.pivot = keepPivot.Value;
                    pr.sizeDelta = keepSize.Value;
                    pr.anchoredPosition = keepPos.Value;
                }
            }

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log($"[SkillUiBuilder] 프리팹 저장: {PrefabPath}");
            return prefab;
        }

        /// <summary>지정 씬의 UIManager에 스킬 프리팹 참조를 배선하고(필요 시 EventSystem 생성) 저장한다.</summary>
        private static void AssignToScene(string scenePath, GameObject skillPrefab)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            var uiManager = Object.FindAnyObjectByType<UIManager>(FindObjectsInactive.Include);
            if (uiManager == null)
            {
                Debug.LogWarning($"[SkillUiBuilder] {scenePath}에 UIManager가 없어 건너뜁니다.");
                return;
            }

            // EventSystem은 이 빌더가 만들지 않는다: TitleScene은 Login/SignUp 패널이 자체 EventSystem을
            // 포함하고, GameScene은 인벤토리 빌더가 씬 EventSystem을 배치한다. 여기서 추가하면 중복이 된다.
            var so = new SerializedObject(uiManager);
            so.FindProperty("skillPanelPrefab").objectReferenceValue = skillPrefab;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[SkillUiBuilder] {scenePath} UIManager에 스킬 프리팹 배선 완료.");
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

        /// <summary>인벤토리 공용 아트 폴더에서 스프라이트를 로드한다.</summary>
        private static Sprite LoadSprite(string fileName) => LoadSpriteAt($"{ArtDir}/{fileName}.png");

        /// <summary>경로의 스프라이트 로드(Single/Multiple 모두 대응). 임포트 설정은 건드리지 않는다(공용 아트 규칙).</summary>
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
            Debug.LogWarning($"[SkillUiBuilder] 스프라이트를 찾지 못했습니다: {path}");
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
