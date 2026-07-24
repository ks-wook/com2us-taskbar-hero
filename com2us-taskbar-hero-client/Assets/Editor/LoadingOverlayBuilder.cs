using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TaskbarHero.Client.UI;

namespace TaskbarHero.ClientEditor
{
    /// <summary>
    /// 로딩 오버레이 프리팹 생성(스피너 프레임 배선) + Title/GameScene에 배치를 자동화하는 에디터 도구.
    /// 오버레이는 코드로 구성되므로(LoadingOverlay) 프리팹은 컴포넌트 + 스피너 프레임 참조 + 정적 계층만 담는다.
    /// 스피너 프레임은 Assets/Art/Effect/UI/RedLoadingSpinner의 번호순 스프라이트를 사용한다.
    /// 메뉴: TaskbarHero/UI/로딩 오버레이·씬 배선
    /// </summary>
    public static class LoadingOverlayBuilder
    {
        private const string SpinnerDir = "Assets/Art/Effect/UI/RedLoadingSpinner";
        private const string PrefabPath = "Assets/Prefabs/UI/LoadingOverlay.prefab";
        private const string GameScenePath = "Assets/Scenes/GameScene.unity";
        private const string TitleScenePath = "Assets/Scenes/TitleScene.unity";

        [MenuItem("TaskbarHero/UI/로딩 오버레이·씬 배선")]
        public static void Build()
        {
            var prefab = BuildPrefab();
            AssignToScene(TitleScenePath, prefab);
            AssignToScene(GameScenePath, prefab);
            AssetDatabase.SaveAssets();
            Debug.Log("[LoadingOverlayBuilder] 완료: 로딩 오버레이 프리팹 생성 + Title/GameScene 배치.");
        }

        private static GameObject BuildPrefab()
        {
            EnsureFolder("Assets/Prefabs");
            EnsureFolder("Assets/Prefabs/UI");

            var root = new GameObject("LoadingOverlay");
            var ctrl = root.AddComponent<LoadingOverlay>();

            // 스피너 프레임(번호 오름차순)을 EditorConstruct 전에 배선해 Construct가 애니메이터에 적용하도록 한다.
            var frames = LoadFrames();
            var so = new SerializedObject(ctrl);
            var framesProp = so.FindProperty("_frames");
            framesProp.arraySize = frames.Count;
            for (int i = 0; i < frames.Count; i++)
            {
                framesProp.GetArrayElementAtIndex(i).objectReferenceValue = frames[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            ctrl.EditorConstruct();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log($"[LoadingOverlayBuilder] 프리팹 저장: {PrefabPath} (스피너 {frames.Count}프레임)");
            return prefab;
        }

        /// <summary>스피너 프레임(RedLoadingSpinner_XX.png)을 번호 오름차순으로 로드한다.</summary>
        private static List<Sprite> LoadFrames()
        {
            var frames = new List<Sprite>();
            if (!Directory.Exists(SpinnerDir))
            {
                Debug.LogWarning($"[LoadingOverlayBuilder] 스피너 폴더를 찾지 못했습니다: {SpinnerDir}");
                return frames;
            }
            var files = Directory.GetFiles(SpinnerDir, "RedLoadingSpinner_*.png")
                .Select(f => f.Replace('\\', '/'))
                .OrderBy(f => f)
                .ToArray();
            foreach (var f in files)
            {
                var sp = LoadSprite(f);
                if (sp != null)
                {
                    frames.Add(sp);
                }
            }
            return frames;
        }

        /// <summary>Multiple 스프라이트 모드 텍스처에서 첫 Sprite 서브에셋을 로드한다.</summary>
        private static Sprite LoadSprite(string path)
        {
            var direct = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (direct != null)
            {
                return direct;
            }
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (obj is Sprite sp)
                {
                    return sp;
                }
            }
            Debug.LogWarning($"[LoadingOverlayBuilder] Sprite 로드 실패: {path}");
            return null;
        }

        /// <summary>지정 씬에 로딩 오버레이 인스턴스를 배치한다(기존 것이 있으면 교체).</summary>
        private static void AssignToScene(string scenePath, GameObject prefab)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            // 기존 오버레이 제거(중복 방지).
            foreach (var existing in Object.FindObjectsByType<LoadingOverlay>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = prefab.name;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[LoadingOverlayBuilder] {scene.name}에 로딩 오버레이 배치 완료.");
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
