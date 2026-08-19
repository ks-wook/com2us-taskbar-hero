using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TaskbarHero.Client.Managers;
using TaskbarHero.Client.UI;

namespace TaskbarHero.ClientEditor
{
    /// <summary>
    /// 환경설정 패널 프리팹 생성 + 씬 배선 도구. 패널 정적 계층은 코드로 구성되므로
    /// (<see cref="SettingsPanelController.EditorConstruct"/>) 프리팹은 컨트롤러 + 계층 + 아트 참조만 담는다.
    /// 같은 톱니바퀴 아이콘을 쓰는 두 곳도 함께 배선한다 — GameScene HUD의 환경설정 버튼과,
    /// TitleScene 우측 하단의 '접속 서버 변경' 버튼(<see cref="TitleScreen"/>의 settingsIcon).
    /// 메뉴: TaskbarHero/UI/환경설정 패널·씬 배선
    /// </summary>
    public static class SettingsUiBuilder
    {
        private const string PrefabPath = "Assets/Prefabs/UI/SettingsPanel.prefab";
        // 창 배경은 가방·스킬·룬 창과 같은 공용 프레임을 쓴다. **Multiple로 임포트된 아트라 임포트 설정을
        // 건드리지 않고**(공용 아트 규칙) LoadSpriteAt의 서브 스프라이트 폴백으로 읽어 배선만 한다.
        private const string PanelSpritePath = "Assets/Art/UI/ui_bg_2.png";
        private const string TrackSpritePath = "Assets/Art/UI/System/system_slot.png";
        private const string SettingsIconPath = "Assets/Art/Icon/환경설정.png";
        private const string GameScenePath = "Assets/Scenes/GameScene.unity";
        private const string TitleScenePath = "Assets/Scenes/TitleScene.unity";

        [MenuItem("TaskbarHero/UI/환경설정 패널·씬 배선")]
        public static void Build()
        {
            ReimportSingle(SettingsIconPath);
            var prefab = BuildPrefab();
            AssignToScene(TitleScenePath, prefab, false);
            AssignToScene(GameScenePath, prefab, true);
            AssetDatabase.SaveAssets();
            Debug.Log("[SettingsUiBuilder] 완료: 환경설정 패널 프리팹 생성 + Title/GameScene 배선"
                      + "(타이틀 톱니바퀴 · GameScene HUD 아이콘 포함).");
        }

        private static GameObject BuildPrefab()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs")) AssetDatabase.CreateFolder("Assets", "Prefabs");
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs/UI")) AssetDatabase.CreateFolder("Assets/Prefabs", "UI");

            var root = new GameObject("SettingsPanel");
            var ctrl = root.AddComponent<SettingsPanelController>();

            // EditorConstruct 전에 배선해 정적 계층이 아트를 바로 반영하게 한다.
            var so = new SerializedObject(ctrl);
            so.FindProperty("_panelSprite").objectReferenceValue = LoadSpriteAt(PanelSpritePath);
            so.FindProperty("_trackSprite").objectReferenceValue = LoadSpriteAt(TrackSpritePath);
            so.ApplyModifiedPropertiesWithoutUndo();

            ctrl.EditorConstruct();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log($"[SettingsUiBuilder] 프리팹 저장: {PrefabPath}");
            return prefab;
        }

        /// <summary>씬의 UIManager에 환경설정 패널을, GameScene이면 HUD에 환경설정 아이콘도 배선한다.</summary>
        private static void AssignToScene(string scenePath, GameObject prefab, bool wireHudIcon)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            var uiManager = Object.FindAnyObjectByType<UIManager>(FindObjectsInactive.Include);
            if (uiManager == null)
            {
                Debug.LogWarning($"[SettingsUiBuilder] {scene.name}에 UIManager가 없어 건너뜁니다.");
            }
            else
            {
                var so = new SerializedObject(uiManager);
                so.FindProperty("settingsPanelPrefab").objectReferenceValue = prefab;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // 타이틀 화면 우측 하단 톱니바퀴('접속 서버 변경') 아이콘. 없으면 글자 버튼으로 대체되므로
            // 기능이 사라지지는 않지만, 배선해 두어야 의도한 모양으로 나온다.
            var titleScreen = Object.FindAnyObjectByType<TitleScreen>(FindObjectsInactive.Include);
            if (titleScreen != null)
            {
                var tso = new SerializedObject(titleScreen);
                tso.FindProperty("settingsIcon").objectReferenceValue = LoadSpriteAt(SettingsIconPath);
                tso.ApplyModifiedPropertiesWithoutUndo();
                Debug.Log($"[SettingsUiBuilder] {scene.name} 타이틀 톱니바퀴 아이콘 배선 완료.");
            }

            if (wireHudIcon)
            {
                var hud = Object.FindAnyObjectByType<GameSceneHudController>(FindObjectsInactive.Include);
                if (hud == null)
                {
                    Debug.LogWarning($"[SettingsUiBuilder] {scene.name}에 GameSceneHudController가 없습니다.");
                }
                else
                {
                    var hso = new SerializedObject(hud);
                    hso.FindProperty("settingsIcon").objectReferenceValue = LoadSpriteAt(SettingsIconPath);
                    hso.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[SettingsUiBuilder] {scene.name} 배선 완료.");
        }

        /// <summary>아이콘 텍스처를 Sprite/Single로 교정한다(Multiple 슬라이스 방지).</summary>
        private static void ReimportSingle(string path)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null)
            {
                Debug.LogWarning($"[SettingsUiBuilder] 텍스처 임포터를 찾지 못했습니다: {path}");
                return;
            }
            bool changed = false;
            if (ti.textureType != TextureImporterType.Sprite) { ti.textureType = TextureImporterType.Sprite; changed = true; }
            if (ti.spriteImportMode != SpriteImportMode.Single) { ti.spriteImportMode = SpriteImportMode.Single; changed = true; }
            if (changed) ti.SaveAndReimport();
        }

        private static Sprite LoadSpriteAt(string path)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null) return sprite;
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (obj is Sprite s) return s;
            }
            Debug.LogWarning($"[SettingsUiBuilder] 스프라이트를 찾지 못했습니다: {path}");
            return null;
        }
    }
}
