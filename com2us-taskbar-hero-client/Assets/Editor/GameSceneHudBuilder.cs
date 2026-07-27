using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TaskbarHero.Client.UI;

namespace TaskbarHero.ClientEditor
{
    /// <summary>
    /// GameScene 우하단 HUD 메뉴 버튼(메일·편성·스테이지·가방)의 아이콘을 배선하는 에디터 도구.
    /// 아이콘은 Assets/Art/Icon(출석부·편성·스테이지·인벤토리)과 Assets/Art/UI/Mail(메일)을 사용하며,
    /// Multiple로 임포트된 아이콘은 Single로 교정한다.
    /// HUD는 런타임에 코드로 구성되므로(GameSceneHudController) 씬의 HUD 오브젝트에 스프라이트 참조만 배선한다.
    /// 메뉴: TaskbarHero/UI/게임 HUD 아이콘·씬 배선
    /// </summary>
    public static class GameSceneHudBuilder
    {
        private const string IconDir = "Assets/Art/Icon";
        private const string MailIconPath = "Assets/Art/UI/Mail/메일.png";
        private const string AttendanceIconPath = IconDir + "/출석부.png";
        private const string GameScenePath = "Assets/Scenes/GameScene.unity";

        [MenuItem("TaskbarHero/UI/게임 HUD 아이콘·씬 배선")]
        public static void Build()
        {
            // 아이콘을 온전한 단일 스프라이트로 사용하도록 Single 모드로 교정(Multiple 슬라이스 방지).
            ReimportSingle("편성");
            ReimportSingle("스테이지");
            ReimportSingle("인벤토리");
            ReimportSingle("출석부");
            ReimportSingleAt(MailIconPath);
            AssetDatabase.Refresh();

            var scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
            var hud = Object.FindAnyObjectByType<GameSceneHudController>(FindObjectsInactive.Include);
            if (hud == null)
            {
                var go = new GameObject("GameSceneHud", typeof(GameSceneHudController));
                hud = go.GetComponent<GameSceneHudController>();
                Undo.RegisterCreatedObjectUndo(go, "Create GameSceneHud");
            }

            var so = new SerializedObject(hud);
            so.FindProperty("mailIcon").objectReferenceValue = LoadSpriteAt(MailIconPath);
            so.FindProperty("partyIcon").objectReferenceValue = LoadSprite("편성");
            so.FindProperty("stageIcon").objectReferenceValue = LoadSprite("스테이지");
            so.FindProperty("inventoryIcon").objectReferenceValue = LoadSprite("인벤토리");
            so.FindProperty("attendanceIcon").objectReferenceValue = LoadSpriteAt(AttendanceIconPath);
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[GameSceneHudBuilder] 완료: GameScene HUD(출석부·메일·편성·스테이지·가방) 아이콘 배선.");
        }

        /// <summary>아이콘 텍스처를 Sprite/Single 모드로 교정한다(이미 그렇다면 무시).</summary>
        private static void ReimportSingle(string fileName)
        {
            ReimportSingleAt($"{IconDir}/{fileName}.png");
        }

        /// <summary>지정 경로의 텍스처를 Sprite/Single 모드로 교정한다(이미 그렇다면 무시).</summary>
        private static void ReimportSingleAt(string path)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null)
            {
                Debug.LogWarning($"[GameSceneHudBuilder] 텍스처 임포터를 찾지 못했습니다: {path}");
                return;
            }
            bool changed = false;
            if (ti.textureType != TextureImporterType.Sprite) { ti.textureType = TextureImporterType.Sprite; changed = true; }
            if (ti.spriteImportMode != SpriteImportMode.Single) { ti.spriteImportMode = SpriteImportMode.Single; changed = true; }
            if (changed) ti.SaveAndReimport();
        }

        /// <summary>Assets/Art/Icon에서 스프라이트를 로드한다(Single/Multiple 모두 대응).</summary>
        private static Sprite LoadSprite(string fileName)
        {
            return LoadSpriteAt($"{IconDir}/{fileName}.png");
        }

        /// <summary>지정 경로에서 스프라이트를 로드한다(Single/Multiple 모두 대응).</summary>
        private static Sprite LoadSpriteAt(string path)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null) return sprite;
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (obj is Sprite s) return s;
            }
            Debug.LogWarning($"[GameSceneHudBuilder] 스프라이트를 찾지 못했습니다: {path}");
            return null;
        }
    }
}
