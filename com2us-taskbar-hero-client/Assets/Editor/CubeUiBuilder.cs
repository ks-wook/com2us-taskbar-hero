using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TaskbarHero.Client.Managers;
using TaskbarHero.Client.UI;

namespace TaskbarHero.ClientEditor
{
    /// <summary>
    /// 큐브 UI 프리팹 생성 + Title/GameScene 배선을 자동화하는 에디터 도구.
    /// 런타임 UI는 코드로 구성되므로(CubePanelController) 프리팹은 컨트롤러 + 스프라이트 참조만 담는다.
    /// 아트는 Assets/Art/UI/Cube(cube_bg·ui_slot_normal·ui_slot_highlight)를 사용한다.
    /// 메뉴: TaskbarHero/UI/큐브 패널·씬 생성
    /// </summary>
    public static class CubeUiBuilder
    {
        private const string ArtDir = "Assets/Art/UI/Cube";
        private const string PrefabPath = "Assets/Prefabs/UI/CubePanel.prefab";
        private const string GameScenePath = "Assets/Scenes/GameScene.unity";
        private const string TitleScenePath = "Assets/Scenes/TitleScene.unity";

        [MenuItem("TaskbarHero/UI/큐브 패널·씬 생성")]
        public static void Build()
        {
            var prefab = BuildPrefab();
            AssignToScene(TitleScenePath, prefab);
            AssignToScene(GameScenePath, prefab);
            AssetDatabase.SaveAssets();
            Debug.Log("[CubeUiBuilder] 완료: 프리팹 생성 + Title/GameScene UIManager 배선.");
        }

        /// <summary>CubePanel 프리팹(컨트롤러 + 스프라이트 참조 + 정적 계층)을 저장한다.</summary>
        private static GameObject BuildPrefab()
        {
            EnsureFolder("Assets/Prefabs");
            EnsureFolder("Assets/Prefabs/UI");

            // 사용자가 에디터에서 조정한 PanelRoot 크기/위치를 보존한다(재생성 시 초기화 방지).
            Vector2? keepSize = null;
            Vector2? keepPos = null;
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null)
            {
                var pr = FindChild(existing.transform, "PanelRoot") as RectTransform;
                if (pr != null)
                {
                    keepSize = pr.sizeDelta;
                    keepPos = pr.anchoredPosition;
                }
            }

            var root = new GameObject("CubePanel");
            var ctrl = root.AddComponent<CubePanelController>();

            var so = new SerializedObject(ctrl);
            so.FindProperty("panelBackground").objectReferenceValue = LoadSprite("cube_bg");
            so.FindProperty("slotNormal").objectReferenceValue = LoadSprite("ui_slot_normal");
            so.FindProperty("slotHighlight").objectReferenceValue = LoadSprite("ui_slot_highlight");
            so.ApplyModifiedPropertiesWithoutUndo();

            // 전체 정적 계층을 에디터에서 생성해 프리팹에 굽는다(에디터에서 바로 보이도록).
            ctrl.EditorConstruct();

            if (keepSize.HasValue)
            {
                var pr = FindChild(root.transform, "PanelRoot") as RectTransform;
                if (pr != null)
                {
                    pr.sizeDelta = keepSize.Value;
                    pr.anchoredPosition = keepPos.Value;
                }
            }

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log($"[CubeUiBuilder] 프리팹 저장: {PrefabPath}");
            return prefab;
        }

        /// <summary>지정 씬의 UIManager에 큐브 프리팹 참조를 배선하고 저장한다.</summary>
        private static void AssignToScene(string scenePath, GameObject cubePrefab)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            var uiManager = Object.FindAnyObjectByType<UIManager>(FindObjectsInactive.Include);
            if (uiManager == null)
            {
                Debug.LogWarning($"[CubeUiBuilder] {scenePath}에 UIManager가 없어 건너뜁니다.");
                return;
            }

            var so = new SerializedObject(uiManager);
            so.FindProperty("cubePanelPrefab").objectReferenceValue = cubePrefab;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[CubeUiBuilder] {scenePath} UIManager에 큐브 프리팹 배선 완료.");
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
            string path = $"{ArtDir}/{fileName}.png";
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
            Debug.LogWarning($"[CubeUiBuilder] 스프라이트를 찾지 못했습니다: {path}");
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
