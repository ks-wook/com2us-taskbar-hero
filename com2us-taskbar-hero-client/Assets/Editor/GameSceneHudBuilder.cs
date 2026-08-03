using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TaskbarHero.Client.UI;

namespace TaskbarHero.ClientEditor
{
    /// <summary>
    /// GameScene 하단 중앙 HUD 버튼(거래소·출석부·메일·편성·스테이지·가방·환경설정)의 아이콘을 배선하는 에디터 도구.
    /// 아이콘은 Assets/Art/Icon(출석부·편성·스테이지·인벤토리)과 Assets/Art/UI(햄버거메뉴)·Assets/Art/UI/Mail(메일)을 사용하며,
    /// Multiple로 임포트된 아이콘은 Single로 교정한다.
    /// HUD는 런타임에 코드로 구성되므로(GameSceneHudController) 씬의 HUD 오브젝트에 스프라이트 참조만 배선한다.
    /// 메뉴: TaskbarHero/UI/게임 HUD 아이콘·씬 배선
    /// </summary>
    public static class GameSceneHudBuilder
    {
        private const string IconDir = "Assets/Art/Icon";
        private const string MailIconPath = "Assets/Art/UI/Mail/메일.png";
        private const string AttendanceIconPath = IconDir + "/출석부.png";
        private const string UiBackgroundPath = "Assets/Art/UI/ui_bg.png";
        // 우측 상단 '적용 중인 버프' 아이콘과 그 상세 툴팁 배경(둘 다 임포트 설정은 건드리지 않고 읽어서 배선만 한다).
        private const string ActiveBuffIconPath = IconDir + "/적용중인버프.png";
        private const string ItemDetailBgPath = "Assets/Art/UI/item_detail_bg.png";
        // 하단 메뉴바 토글 버튼 아이콘. **Sprite가 아니라 Texture2D로 배선한다** — 이 파일은 Multiple로 임포트돼
        // 햄버거 3줄이 서브 스프라이트로 쪼개져 있어(메뉴_0/1/2) 스프라이트를 쓰면 줄 한 개만 나온다.
        // 임포트 설정은 바꾸지 않고(공용 아트 규칙) HUD가 런타임에 텍스처 전체로 스프라이트를 만들어 쓴다.
        private const string MenuToggleIconPath = IconDir + "/메뉴.png";
        // ui_bg(2048×731) 9-slice 경계: 나무 테두리 + 모서리 금장식이 온전히 남는 크기(L,B,R,T).
        private static readonly Vector4 UiBackgroundBorder = new Vector4(230f, 250f, 230f, 250f);

        // ESC 메뉴 아트(Assets/Art/UI/System).
        private const string SystemBackgroundPath = "Assets/Art/UI/System/system_bg.png";
        private const string SystemSlotPath = "Assets/Art/UI/System/system_slot.png";
        // system_bg(512×384): 나무 테두리 + 모서리 장식이 남는 크기. system_slot(2048×731)은 ui_bg와 같은 형태.
        private static readonly Vector4 SystemBackgroundBorder = new Vector4(30f, 30f, 30f, 30f);
        private static readonly Vector4 SystemSlotBorder = new Vector4(191f, 128f, 191f, 128f);
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
            ImportSliced(UiBackgroundPath, UiBackgroundBorder);
            ImportSliced(SystemBackgroundPath, SystemBackgroundBorder);
            ImportSliced(SystemSlotPath, SystemSlotBorder);
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
            so.FindProperty("uiBackgroundSprite").objectReferenceValue = LoadSpriteAt(UiBackgroundPath);
            so.FindProperty("systemBackgroundSprite").objectReferenceValue = LoadSpriteAt(SystemBackgroundPath);
            so.FindProperty("systemSlotSprite").objectReferenceValue = LoadSpriteAt(SystemSlotPath);
            so.FindProperty("activeBuffIcon").objectReferenceValue = LoadSpriteAt(ActiveBuffIconPath);
            so.FindProperty("buffTooltipBackground").objectReferenceValue = LoadSpriteAt(ItemDetailBgPath);
            so.FindProperty("menuToggleIconTexture").objectReferenceValue = LoadTextureAt(MenuToggleIconPath);
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[GameSceneHudBuilder] 완료: GameScene HUD 아이콘 + 하단 배경 + ESC 메뉴 아트 + 버프 아이콘 배선.");
        }

        /// <summary>프레임 텍스처를 9-slice로 쓸 수 있게 교정한다(Sprite/Single · Full Rect · Border).
        /// Full Rect가 아니면(기본 Tight) Sliced 렌더가 깨지고, Multiple 모드면 스프라이트를 통째로 못 쓴다.</summary>
        private static void ImportSliced(string path, Vector4 border)
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
            if (ti.spriteBorder != border) { ti.spriteBorder = border; changed = true; }

            var settings = new TextureImporterSettings();
            ti.ReadTextureSettings(settings);
            if (settings.spriteMeshType != SpriteMeshType.FullRect)
            {
                settings.spriteMeshType = SpriteMeshType.FullRect;
                ti.SetTextureSettings(settings);
                changed = true;
            }
            if (changed)
            {
                ti.SaveAndReimport();
                Debug.Log($"[GameSceneHudBuilder] 9-slice 임포트 교정: {System.IO.Path.GetFileName(path)} (border {border})");
            }
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

        /// <summary>지정 경로의 텍스처를 그대로 로드한다(스프라이트 분할 상태와 무관하게 전체 이미지가 필요할 때).</summary>
        private static Texture2D LoadTextureAt(string path)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null)
            {
                Debug.LogWarning($"[GameSceneHudBuilder] 텍스처를 찾지 못했습니다: {path}");
            }
            return tex;
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
