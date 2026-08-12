using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TaskbarHero.Client.Managers;
using TaskbarHero.Client.UI;

namespace TaskbarHero.ClientEditor
{
    /// <summary>
    /// 스킬 레벨업 UI 프리팹 생성 + Title/GameScene 배선을 자동화하는 에디터 도구.
    /// 런타임 UI는 코드로 구성되므로(SkillPanelController) 프리팹은 컨트롤러 + 스프라이트 참조만 담는다.
    /// 스킬 아이콘 DB(SkillIconDatabase)도 함께 빌드한다.
    /// 메뉴: TaskbarHero/UI/스킬 패널·씬 생성
    /// </summary>
    public static class SkillUiBuilder
    {
        // 인벤토리 패널과 동일한 UI 스프라이트를 공용으로 사용한다.
        private const string ArtDir = "Assets/Art/UI/Inventory";
        // 패널 본체 배경 프레임(인벤토리·룬 패널과 같은 공용 프레임).
        private const string PanelBgPath = "Assets/Art/UI/ui_bg_2.png";
        // 버튼 공용 아트(모달·출석부·메일 등과 동일).
        private const string ButtonSpritePath = "Assets/Art/UI/pixel_rpg_button.png";
        private const string PrefabPath = "Assets/Prefabs/UI/SkillPanel.prefab";
        private const string GameScenePath = "Assets/Scenes/GameScene.unity";
        private const string TitleScenePath = "Assets/Scenes/TitleScene.unity";

        [MenuItem("TaskbarHero/UI/스킬 패널·씬 생성")]
        public static void Build()
        {
            // 스킬 아이콘 DB를 먼저 빌드해 프리팹/런타임이 참조할 수 있게 한다.
            SkillIconDatabaseBuilder.Build();

            var prefab = BuildPrefab();
            AssignToScene(TitleScenePath, prefab);
            AssignToScene(GameScenePath, prefab);
            AssetDatabase.SaveAssets();
            Debug.Log("[SkillUiBuilder] 완료: 프리팹 생성 + Title/GameScene UIManager 배선.");
        }

        /// <summary>SkillPanel 프리팹(컨트롤러 + 스프라이트 참조 + 정적 계층)을 저장한다.</summary>
        private static GameObject BuildPrefab()
        {
            EnsureFolder("Assets/Prefabs");
            EnsureFolder("Assets/Prefabs/UI");

            // PanelRoot의 크기·위치는 <b>코드(SkillPanelController)가 정본</b>이라 프리팹 값을 보존하지 않는다.
            // 창 안쪽 내용 영역(ContentArea)의 여백과 그 안의 모든 좌표가 PanelSize에서 파생되므로,
            // 프리팹에 남은 옛 크기를 되살리면 내용이 배경 프레임 테두리를 침범한다.
            // 창을 옮기는 것은 실행 중 드래그(PanelDragMove, 위치는 PlayerPrefs에 저장)로 처리한다.
            var root = new GameObject("SkillPanel");
            var ctrl = root.AddComponent<SkillPanelController>();

            var so = new SerializedObject(ctrl);
            so.FindProperty("panelBackground").objectReferenceValue = LoadSpriteAt(PanelBgPath);
            so.FindProperty("slotNormal").objectReferenceValue = LoadSprite("ui_slot_normal");
            so.FindProperty("slotHighlight").objectReferenceValue = LoadSprite("ui_slot_highlight");
            // 하단 '스킬 초기화' 버튼 아트(다른 패널 버튼과 같은 공용 아트, 9-slice).
            so.FindProperty("buttonSprite").objectReferenceValue = LoadSpriteAt(ButtonSpritePath);
            so.ApplyModifiedPropertiesWithoutUndo();

            // 전체 정적 계층을 에디터에서 생성해 프리팹에 굽는다(에디터에서 바로 보이도록).
            ctrl.EditorConstruct();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log($"[SkillUiBuilder] 프리팹 저장: {PrefabPath}");
            return prefab;
        }

        /// <summary>지정 씬의 UIManager에 스킬 프리팹 참조를 배선하고(필요 시 EventSystem 생성) 저장한다.</summary>
        private static void AssignToScene(string scenePath, GameObject skillPrefab)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            var uiManager = Object.FindAnyObjectByType<UIManager>(FindObjectsInactive.Include);
            if (uiManager == null)
            {
                Debug.LogWarning($"[SkillUiBuilder] {scenePath}에 UIManager가 없어 건너뜁니다.");
                return;
            }

            // EventSystem은 이 빌더가 만들지 않는다: TitleScene은 Login/SignUp 패널이 자체 EventSystem을
            // 포함하고, GameScene은 인벤토리 빌더가 씬 EventSystem을 배치한다. 여기서 추가하면 중복이 된다.
            var so = new SerializedObject(uiManager);
            so.FindProperty("skillPanelPrefab").objectReferenceValue = skillPrefab;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[SkillUiBuilder] {scenePath} UIManager에 스킬 프리팹 배선 완료.");
        }

        /// <summary>인벤토리 공용 아트 폴더에서 스프라이트를 로드한다.</summary>
        private static Sprite LoadSprite(string fileName) => LoadSpriteAt($"{ArtDir}/{fileName}.png");

        /// <summary>경로의 스프라이트 로드(Single/Multiple 모두 대응). 임포트 설정은 건드리지 않는다(공용 아트 규칙).</summary>
        private static Sprite LoadSpriteAt(string path)
        {
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
            Debug.LogWarning($"[SkillUiBuilder] 스프라이트를 찾지 못했습니다: {path}");
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
