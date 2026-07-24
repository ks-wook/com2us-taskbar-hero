using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TaskbarHero.Client.Managers;
using TaskbarHero.Client.UI;

namespace TaskbarHero.ClientEditor
{
    /// <summary>
    /// 룬(Rune Tree) UI 프리팹 생성 + Title/GameScene 배선을 자동화하는 에디터 도구.
    /// 런타임 UI는 코드로 구성되므로(RunePanelController) 프리팹은 컨트롤러 + 스프라이트 참조만 담는다.
    /// EventSystem은 만들지 않는다(스킬 빌더와 동일 정책 — 씬/패널이 이미 제공).
    /// 메뉴: TaskbarHero/UI/룬 패널·씬 생성
    /// </summary>
    public static class RuneUiBuilder
    {
        private const string ArtDir = "Assets/Art/UI/Inventory";
        private const string PrefabPath = "Assets/Prefabs/UI/RunePanel.prefab";
        private const string GameScenePath = "Assets/Scenes/GameScene.unity";
        private const string TitleScenePath = "Assets/Scenes/TitleScene.unity";

        [MenuItem("TaskbarHero/UI/룬 패널·씬 생성")]
        public static void Build()
        {
            var prefab = BuildPrefab();
            AssignToScene(TitleScenePath, prefab);
            AssignToScene(GameScenePath, prefab);
            AssetDatabase.SaveAssets();
            Debug.Log("[RuneUiBuilder] 완료: 프리팹 생성 + Title/GameScene UIManager 배선.");
        }

        /// <summary>RunePanel 프리팹(컨트롤러 + 스프라이트 참조 + 정적 계층)을 저장한다.</summary>
        private static GameObject BuildPrefab()
        {
            EnsureFolder("Assets/Prefabs");
            EnsureFolder("Assets/Prefabs/UI");

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

            var root = new GameObject("RunePanel");
            var ctrl = root.AddComponent<RunePanelController>();

            var so = new SerializedObject(ctrl);
            so.FindProperty("panelBackground").objectReferenceValue = LoadSprite("ui_panel_background");
            so.FindProperty("slotNormal").objectReferenceValue = LoadSprite("ui_slot_normal");
            so.FindProperty("slotHighlight").objectReferenceValue = LoadSprite("ui_slot_highlight");
            WireRuneIcons(so); // runeCode → Assets/Art/Icon/Rune 아이콘
            so.ApplyModifiedPropertiesWithoutUndo();

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
            Debug.Log($"[RuneUiBuilder] 프리팹 저장: {PrefabPath}");
            return prefab;
        }

        /// <summary>지정 씬의 UIManager에 룬 프리팹 참조를 배선하고 저장한다(EventSystem은 만들지 않음).</summary>
        private static void AssignToScene(string scenePath, GameObject runePrefab)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var uiManager = Object.FindAnyObjectByType<UIManager>(FindObjectsInactive.Include);
            if (uiManager == null)
            {
                Debug.LogWarning($"[RuneUiBuilder] {scenePath}에 UIManager가 없어 건너뜁니다.");
                return;
            }
            var so = new SerializedObject(uiManager);
            so.FindProperty("runePanelPrefab").objectReferenceValue = runePrefab;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[RuneUiBuilder] {scenePath} UIManager에 룬 프리팹 배선 완료.");
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

        /// <summary>마스터 데이터의 모든 룬을 runeCode → 룬 아이콘(Assets/Art/Icon/Rune)으로 배선한다.
        /// 룬 이름을 파일명으로 정규화한다(예: "공격력 I" → "공격력1", 명명형 "정밀 사격"은 그대로).</summary>
        private static void WireRuneIcons(SerializedObject so)
        {
            var prop = so.FindProperty("_runeIcons");
            if (prop == null)
            {
                Debug.LogWarning("[RuneUiBuilder] _runeIcons 프로퍼티를 찾지 못했습니다.");
                return;
            }
            MasterDataManager.Load();
            var db = MasterDataManager.Db;
            if (db == null || db.Runes.Count == 0)
            {
                Debug.LogWarning("[RuneUiBuilder] 룬 마스터 데이터가 없어 아이콘 배선을 건너뜁니다.");
                prop.arraySize = 0;
                return;
            }

            prop.arraySize = db.Runes.Count;
            int i = 0;
            foreach (var r in db.Runes.Values)
            {
                var el = prop.GetArrayElementAtIndex(i++);
                el.FindPropertyRelative("runeCode").intValue = r.runeCode;
                el.FindPropertyRelative("sprite").objectReferenceValue = LoadRuneIcon(RuneIconFileName(r.name));
            }
        }

        /// <summary>룬 이름을 아이콘 파일명으로 정규화한다(끝의 로마숫자 " I/II/III" → "1/2/3", 그 외는 그대로).</summary>
        private static string RuneIconFileName(string runeName)
        {
            if (string.IsNullOrEmpty(runeName))
            {
                return runeName;
            }
            if (runeName.EndsWith(" III")) return runeName.Substring(0, runeName.Length - 4) + "3";
            if (runeName.EndsWith(" II")) return runeName.Substring(0, runeName.Length - 3) + "2";
            if (runeName.EndsWith(" I")) return runeName.Substring(0, runeName.Length - 2) + "1";
            return runeName;
        }

        /// <summary>룬 아이콘을 Single 스프라이트로 교정 후 로드한다(Multiple 슬라이스 방지).</summary>
        private static Sprite LoadRuneIcon(string fileName)
        {
            string path = $"Assets/Art/Icon/Rune/{fileName}.png";
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti != null)
            {
                bool changed = false;
                if (ti.textureType != TextureImporterType.Sprite) { ti.textureType = TextureImporterType.Sprite; changed = true; }
                if (ti.spriteImportMode != SpriteImportMode.Single) { ti.spriteImportMode = SpriteImportMode.Single; changed = true; }
                if (changed) ti.SaveAndReimport();
            }
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null) return sprite;
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (obj is Sprite s) return s;
            }
            Debug.LogWarning($"[RuneUiBuilder] 룬 아이콘을 찾지 못했습니다: {path}");
            return null;
        }

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
            Debug.LogWarning($"[RuneUiBuilder] 스프라이트를 찾지 못했습니다: {path}");
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
