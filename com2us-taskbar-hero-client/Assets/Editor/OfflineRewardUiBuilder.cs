using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TaskbarHero.Client.Managers;
using TaskbarHero.Client.UI;

namespace TaskbarHero.ClientEditor
{
    /// <summary>
    /// 오프라인 보상 팝업 프리팹 생성 + Title/GameScene UIManager 배선 도구.
    /// 패널은 코드로 구성되므로(OfflineRewardPanelController) 프리팹은 컨트롤러 + 정적 계층 + 초상화 프리팹 배선만 담는다.
    /// 메뉴: TaskbarHero/UI/오프라인 보상 팝업·씬 배선
    /// </summary>
    public static class OfflineRewardUiBuilder
    {
        private const string PrefabPath = "Assets/Prefabs/UI/OfflineRewardPanel.prefab";
        private const string GameScenePath = "Assets/Scenes/GameScene.unity";
        private const string TitleScenePath = "Assets/Scenes/TitleScene.unity";

        [MenuItem("TaskbarHero/UI/오프라인 보상 팝업·씬 배선")]
        public static void Build()
        {
            var prefab = BuildPrefab();
            AssignToScene(TitleScenePath, prefab);
            AssignToScene(GameScenePath, prefab);
            AssetDatabase.SaveAssets();
            Debug.Log("[OfflineRewardUiBuilder] 완료: 오프라인 보상 팝업 프리팹 생성 + Title/GameScene UIManager 배선.");
        }

        private static GameObject BuildPrefab()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
            {
                AssetDatabase.CreateFolder("Assets", "Prefabs");
            }
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs/UI"))
            {
                AssetDatabase.CreateFolder("Assets/Prefabs", "UI");
            }

            var root = new GameObject("OfflineRewardPanel");
            var ctrl = root.AddComponent<OfflineRewardPanelController>();

            // 배경 스프라이트(modal_bg)를 EditorConstruct 전에 배선해 Construct가 배경에 적용하도록 한다.
            var bgSo = new SerializedObject(ctrl);
            bgSo.FindProperty("_backgroundSprite").objectReferenceValue = LoadSprite("modal_bg");
            bgSo.ApplyModifiedPropertiesWithoutUndo();

            ctrl.EditorConstruct(); // 계층을 프리팹에 정적으로 굽는다

            WireClassCharacters(new SerializedObject(ctrl)); // 초상화 프리팹(기사1·레인저2·마법사3)

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log($"[OfflineRewardUiBuilder] 프리팹 저장: {PrefabPath}");
            return prefab;
        }

        /// <summary>초상화용 직업별 캐릭터 프리팹(_classCharacters)을 배선한다. classCode: 기사1·레인저2·마법사3.</summary>
        private static void WireClassCharacters(SerializedObject so)
        {
            var prop = so.FindProperty("_classCharacters");
            if (prop == null)
            {
                Debug.LogWarning("[OfflineRewardUiBuilder] _classCharacters 프로퍼티를 찾지 못했습니다.");
                return;
            }
            prop.arraySize = 3;
            SetClassCharacter(prop, 0, 1, "Assets/Prefabs/Character/Knight.prefab");
            SetClassCharacter(prop, 1, 2, "Assets/Prefabs/Character/Archer.prefab");
            SetClassCharacter(prop, 2, 3, "Assets/Prefabs/Character/Mage.prefab");
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetClassCharacter(SerializedProperty arrayProp, int index, int classCode, string prefabPath)
        {
            var element = arrayProp.GetArrayElementAtIndex(index);
            element.FindPropertyRelative("classCode").intValue = classCode;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[OfflineRewardUiBuilder] 캐릭터 프리팹을 찾지 못했습니다: {prefabPath}");
            }
            element.FindPropertyRelative("prefab").objectReferenceValue = prefab;
        }

        /// <summary>Assets/Art/UI에서 스프라이트를 로드한다(Single/Multiple 모두 대응).</summary>
        private static Sprite LoadSprite(string fileName)
        {
            string path = $"Assets/Art/UI/{fileName}.png";
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null) return sprite;
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (obj is Sprite s) return s;
            }
            Debug.LogWarning($"[OfflineRewardUiBuilder] 스프라이트를 찾지 못했습니다: {path}");
            return null;
        }

        private static void AssignToScene(string scenePath, GameObject prefab)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var uiManager = Object.FindAnyObjectByType<UIManager>(FindObjectsInactive.Include);
            if (uiManager == null)
            {
                Debug.LogWarning($"[OfflineRewardUiBuilder] {scene.name}에 UIManager가 없어 건너뜁니다.");
                return;
            }
            var so = new SerializedObject(uiManager);
            so.FindProperty("offlineRewardPanelPrefab").objectReferenceValue = prefab;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[OfflineRewardUiBuilder] {scene.name} UIManager에 오프라인 보상 팝업 배선 완료.");
        }
    }
}
