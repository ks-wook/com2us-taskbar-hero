using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TaskbarHero.Client.Managers;
using TaskbarHero.Client.UI;

namespace TaskbarHero.ClientEditor
{
    /// <summary>
    /// 파티 편성 패널 프리팹 생성 + Title/GameScene UIManager 배선 도구.
    /// 패널은 코드로 구성되므로(PartyPanelController) 프리팹은 컨트롤러 + 정적 계층만 담는다.
    /// 메뉴: TaskbarHero/UI/편성 패널·씬 배선
    /// </summary>
    public static class PartyUiBuilder
    {
        private const string PrefabPath = "Assets/Prefabs/UI/PartyPanel.prefab";
        private const string GameScenePath = "Assets/Scenes/GameScene.unity";
        private const string TitleScenePath = "Assets/Scenes/TitleScene.unity";

        [MenuItem("TaskbarHero/UI/편성 패널·씬 배선")]
        public static void Build()
        {
            var prefab = BuildPrefab();
            AssignToScene(TitleScenePath, prefab);
            AssignToScene(GameScenePath, prefab);
            AssetDatabase.SaveAssets();
            Debug.Log("[PartyUiBuilder] 완료: 파티 패널 프리팹 생성 + Title/GameScene UIManager 배선.");
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

            var root = new GameObject("PartyPanel");
            var ctrl = root.AddComponent<PartyPanelController>();
            ctrl.EditorConstruct(); // 계층을 프리팹에 정적으로 굽는다

            WireClassCharacters(new SerializedObject(ctrl)); // 초상화 프리팹(기사1·레인저2·마법사3)

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log($"[PartyUiBuilder] 프리팹 저장: {PrefabPath}");
            return prefab;
        }

        /// <summary>초상화용 직업별 캐릭터 프리팹(_classCharacters)을 배선한다. classCode: 기사1·레인저2·마법사3.</summary>
        private static void WireClassCharacters(SerializedObject so)
        {
            var prop = so.FindProperty("_classCharacters");
            if (prop == null)
            {
                Debug.LogWarning("[PartyUiBuilder] _classCharacters 프로퍼티를 찾지 못했습니다.");
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
                Debug.LogWarning($"[PartyUiBuilder] 캐릭터 프리팹을 찾지 못했습니다: {prefabPath}");
            }
            element.FindPropertyRelative("prefab").objectReferenceValue = prefab;
        }

        private static void AssignToScene(string scenePath, GameObject prefab)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var uiManager = Object.FindAnyObjectByType<UIManager>(FindObjectsInactive.Include);
            if (uiManager == null)
            {
                Debug.LogWarning($"[PartyUiBuilder] {scene.name}에 UIManager가 없어 건너뜁니다.");
                return;
            }
            var so = new SerializedObject(uiManager);
            so.FindProperty("partyPanelPrefab").objectReferenceValue = prefab;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[PartyUiBuilder] {scene.name} UIManager에 파티 패널 배선 완료.");
        }
    }
}
