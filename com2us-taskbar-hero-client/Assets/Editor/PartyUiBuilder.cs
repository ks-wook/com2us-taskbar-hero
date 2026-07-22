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

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log($"[PartyUiBuilder] 프리팹 저장: {PrefabPath}");
            return prefab;
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
