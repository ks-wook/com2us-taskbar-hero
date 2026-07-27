using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TaskbarHero.Client.Managers;
using TaskbarHero.Client.UI;

namespace TaskbarHero.ClientEditor
{
    /// <summary>
    /// 우편함(메일) 패널 프리팹 생성 + Title/GameScene UIManager 배선 도구.
    /// 패널 정적 계층은 코드로 구성되므로(MailPanelController.EditorConstruct) 프리팹은
    /// 컨트롤러 + 정적 계층 + 메일 UI 스프라이트(Assets/Art/UI/Mail) 참조만 담는다.
    /// 메뉴: TaskbarHero/UI/메일 패널·씬 배선
    /// </summary>
    public static class MailUiBuilder
    {
        private const string PrefabPath = "Assets/Prefabs/UI/MailPanel.prefab";
        private const string MailArtDir = "Assets/Art/UI/Mail";
        private const string ButtonSpritePath = "Assets/Art/UI/pixel_rpg_button.png";
        private const string ItemSlotPrefabPath = "Assets/Prefabs/UI/ItemSlot.prefab";
        private const string GameScenePath = "Assets/Scenes/GameScene.unity";
        private const string TitleScenePath = "Assets/Scenes/TitleScene.unity";

        [MenuItem("TaskbarHero/UI/메일 패널·씬 배선")]
        public static void Build()
        {
            // 메일 UI 리소스를 온전한 단일 스프라이트로 교정(Multiple 자동 슬라이스 방지).
            ReimportSingle("mailbox_bg");
            ReimportSingle("mailbox_slot");
            ReimportSingle("mail_unread");
            ReimportSingle("mail_readed");
            AssetDatabase.Refresh();

            var prefab = BuildPrefab();
            AssignToScene(TitleScenePath, prefab);
            AssignToScene(GameScenePath, prefab);
            AssetDatabase.SaveAssets();
            Debug.Log("[MailUiBuilder] 완료: 메일 패널 프리팹 생성 + Title/GameScene UIManager 배선.");
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

            var root = new GameObject("MailPanel");
            var ctrl = root.AddComponent<MailPanelController>();

            // 스프라이트를 EditorConstruct 전에 배선해 Construct가 배경/슬롯에 적용하도록 한다.
            var so = new SerializedObject(ctrl);
            so.FindProperty("_backgroundSprite").objectReferenceValue = LoadSprite("mailbox_bg");
            so.FindProperty("_slotSprite").objectReferenceValue = LoadSprite("mailbox_slot");
            so.FindProperty("_unreadSprite").objectReferenceValue = LoadSprite("mail_unread");
            so.FindProperty("_readSprite").objectReferenceValue = LoadSprite("mail_readed");
            so.FindProperty("_buttonSprite").objectReferenceValue = LoadSpriteAt(ButtonSpritePath);
            // 첨부 표시용 공용 아이템 슬롯(재빌드 시 배선 유지). 아직 없으면 ItemSlotBuilder 실행 후 다시 배선된다.
            so.FindProperty("_itemSlotPrefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>(ItemSlotPrefabPath);
            so.ApplyModifiedPropertiesWithoutUndo();

            ctrl.EditorConstruct(); // 정적 계층을 프리팹에 굽는다

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log($"[MailUiBuilder] 프리팹 저장: {PrefabPath}");
            return prefab;
        }

        /// <summary>메일 아트 텍스처를 Sprite/Single 모드로 교정한다(이미 그렇다면 무시).</summary>
        private static void ReimportSingle(string fileName)
        {
            string path = $"{MailArtDir}/{fileName}.png";
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null)
            {
                Debug.LogWarning($"[MailUiBuilder] 텍스처 임포터를 찾지 못했습니다: {path}");
                return;
            }
            bool changed = false;
            if (ti.textureType != TextureImporterType.Sprite) { ti.textureType = TextureImporterType.Sprite; changed = true; }
            if (ti.spriteImportMode != SpriteImportMode.Single) { ti.spriteImportMode = SpriteImportMode.Single; changed = true; }
            if (changed) ti.SaveAndReimport();
        }

        /// <summary>Assets/Art/UI/Mail에서 스프라이트를 로드한다(Single/Multiple 모두 대응).</summary>
        private static Sprite LoadSprite(string fileName)
        {
            return LoadSpriteAt($"{MailArtDir}/{fileName}.png");
        }

        /// <summary>에셋 경로에서 스프라이트를 로드한다(Single/Multiple 모두 대응).</summary>
        private static Sprite LoadSpriteAt(string path)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null) return sprite;
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (obj is Sprite s) return s;
            }
            Debug.LogWarning($"[MailUiBuilder] 스프라이트를 찾지 못했습니다: {path}");
            return null;
        }

        private static void AssignToScene(string scenePath, GameObject prefab)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var uiManager = Object.FindAnyObjectByType<UIManager>(FindObjectsInactive.Include);
            if (uiManager == null)
            {
                Debug.LogWarning($"[MailUiBuilder] {scene.name}에 UIManager가 없어 건너뜁니다.");
                return;
            }
            var so = new SerializedObject(uiManager);
            so.FindProperty("mailPanelPrefab").objectReferenceValue = prefab;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[MailUiBuilder] {scene.name} UIManager에 메일 패널 배선 완료.");
        }
    }
}
