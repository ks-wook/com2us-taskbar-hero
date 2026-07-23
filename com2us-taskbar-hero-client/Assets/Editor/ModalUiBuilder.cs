using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TaskbarHero.Client.Managers;

namespace TaskbarHero.ClientEditor
{
    /// <summary>
    /// 공용 모달(안내창) 프리팹 생성 + Title/GameScene에 ModalManager 배치·배선을 자동화하는 에디터 도구.
    /// 런타임 UI는 코드로 구성되므로(ModalController) 프리팹은 컨트롤러 + 스프라이트 참조 + 정적 계층만 담는다.
    /// 배경/버튼은 Assets/Art/UI(modal_bg·pixel_rpg_button)를 사용한다.
    /// 메뉴: TaskbarHero/UI/모달 패널·씬 생성
    /// </summary>
    public static class ModalUiBuilder
    {
        private const string ArtDir = "Assets/Art/UI";
        private const string PrefabPath = "Assets/Prefabs/UI/Modal.prefab";
        private const string GameScenePath = "Assets/Scenes/GameScene.unity";
        private const string TitleScenePath = "Assets/Scenes/TitleScene.unity";

        [MenuItem("TaskbarHero/UI/모달 패널·씬 생성")]
        public static void Build()
        {
            var prefab = BuildPrefab();
            AssignToScene(TitleScenePath, prefab);
            AssignToScene(GameScenePath, prefab);
            AssetDatabase.SaveAssets();
            Debug.Log("[ModalUiBuilder] 완료: 모달 프리팹 생성 + Title/GameScene ModalManager 배선.");
        }

        /// <summary>Modal 프리팹(컨트롤러 + 스프라이트 참조 + 정적 계층)을 저장한다.</summary>
        private static GameObject BuildPrefab()
        {
            EnsureFolder("Assets/Prefabs");
            EnsureFolder("Assets/Prefabs/UI");

            var root = new GameObject("Modal");
            var ctrl = root.AddComponent<ModalController>();

            var so = new SerializedObject(ctrl);
            so.FindProperty("modalBg").objectReferenceValue = LoadSprite("modal_bg");
            so.FindProperty("buttonSprite").objectReferenceValue = LoadSprite("pixel_rpg_button");
            so.ApplyModifiedPropertiesWithoutUndo();

            ctrl.EditorConstruct();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log($"[ModalUiBuilder] 프리팹 저장: {PrefabPath}");
            return prefab;
        }

        /// <summary>지정 씬에 ModalManager(없으면 생성)를 두고 모달 프리팹을 배선·저장한다.</summary>
        private static void AssignToScene(string scenePath, GameObject modalPrefab)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            var manager = Object.FindAnyObjectByType<ModalManager>(FindObjectsInactive.Include);
            if (manager == null)
            {
                var go = new GameObject("ModalManager", typeof(ModalManager));
                manager = go.GetComponent<ModalManager>();
                Undo.RegisterCreatedObjectUndo(go, "Create ModalManager");
            }

            var so = new SerializedObject(manager);
            so.FindProperty("modalPrefab").objectReferenceValue = modalPrefab;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[ModalUiBuilder] {scenePath}에 ModalManager 배선 완료.");
        }

        /// <summary>스프라이트 로드(Single/Multiple 모두 대응).</summary>
        private static Sprite LoadSprite(string fileName)
        {
            string path = $"{ArtDir}/{fileName}.png";
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null) return sprite;
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (obj is Sprite s) return s;
            }
            Debug.LogWarning($"[ModalUiBuilder] 스프라이트를 찾지 못했습니다: {path}");
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
