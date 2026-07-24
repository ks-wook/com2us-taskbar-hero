using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TaskbarHero.Client.Battle;

namespace TaskbarHero.ClientEditor
{
    /// <summary>
    /// 스테이지 입장 배너(<see cref="StageEnterBanner"/>) 프리팹 생성 + GameScene 배선을 자동화하는 에디터 도구.
    /// 런타임 UI는 코드로 구성되므로(StageEnterBanner) 프리팹은 컨트롤러 + 정적 계층(Canvas·바·텍스트)만 담는다.
    /// 저장 후 GameScene의 <see cref="DungeonBattleFlow"/>에 프리팹 참조를 배선한다(입장 시 Instantiate).
    /// 메뉴: TaskbarHero/UI/스테이지 입장 배너 프리팹 생성
    /// </summary>
    public static class StageEnterBannerBuilder
    {
        private const string PrefabPath = "Assets/Prefabs/UI/StageEnterBanner.prefab";
        private const string GameScenePath = "Assets/Scenes/GameScene.unity";

        [MenuItem("TaskbarHero/UI/스테이지 입장 배너 프리팹 생성")]
        public static void Build()
        {
            var prefab = BuildPrefab();
            AssignToScene(GameScenePath, prefab);
            AssetDatabase.SaveAssets();
            Debug.Log("[StageEnterBannerBuilder] 완료: 프리팹 생성 + GameScene DungeonBattleFlow 배선.");
        }

        /// <summary>StageEnterBanner 프리팹(컨트롤러 + 정적 계층)을 저장한다.</summary>
        private static GameObject BuildPrefab()
        {
            EnsureFolder("Assets/Prefabs");
            EnsureFolder("Assets/Prefabs/UI");

            var root = new GameObject("StageEnterBanner");
            var ctrl = root.AddComponent<StageEnterBanner>();

            // 정적 계층을 에디터에서 생성해 프리팹에 굽는다(인스펙터에서 바로 수정 가능).
            ctrl.EditorConstruct();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log($"[StageEnterBannerBuilder] 프리팹 저장: {PrefabPath}");
            return prefab;
        }

        /// <summary>지정 씬의 DungeonBattleFlow에 배너 프리팹 참조를 배선하고 저장한다.</summary>
        private static void AssignToScene(string scenePath, GameObject bannerPrefab)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            var flow = Object.FindAnyObjectByType<DungeonBattleFlow>(FindObjectsInactive.Include);
            if (flow == null)
            {
                Debug.LogWarning($"[StageEnterBannerBuilder] {scenePath}에 DungeonBattleFlow가 없어 배선을 건너뜁니다. " +
                                 "먼저 'TaskbarHero/UI/던전 전투 배선'을 실행하세요.");
                return;
            }

            var so = new SerializedObject(flow);
            so.FindProperty("stageEnterBannerPrefab").objectReferenceValue = bannerPrefab;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[StageEnterBannerBuilder] {scenePath} DungeonBattleFlow에 배너 프리팹 배선 완료.");
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
