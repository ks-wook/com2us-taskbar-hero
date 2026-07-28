using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TaskbarHero.Client.Managers;
using TaskbarHero.Client.UI;

namespace TaskbarHero.ClientEditor
{
    /// <summary>
    /// 출석부 패널 프리팹 생성 + Title/GameScene UIManager 배선 도구.
    /// 패널 정적 계층(보상 사다리 30칸 포함)은 코드로 구성되므로(AttendancePanelController.EditorConstruct)
    /// 프리팹은 컨트롤러 + 정적 계층 + 공용 아이템 슬롯/버튼 스프라이트 참조만 담는다.
    /// 메뉴: TaskbarHero/UI/출석부 패널·씬 배선
    /// </summary>
    public static class AttendanceUiBuilder
    {
        private const string PrefabPath = "Assets/Prefabs/UI/AttendancePanel.prefab";
        private const string ItemSlotPrefabPath = "Assets/Prefabs/UI/ItemSlot.prefab";
        private const string ButtonSpritePath = "Assets/Art/UI/pixel_rpg_button.png";
        private const string BoardSpritePath = "Assets/Art/UI/Attendance/attendance_board.png";
        private const string SlotFrameSpritePath = "Assets/Art/UI/Attendance/attendance_item_slot.png";
        private const string GameScenePath = "Assets/Scenes/GameScene.unity";
        private const string TitleScenePath = "Assets/Scenes/TitleScene.unity";

        [MenuItem("TaskbarHero/UI/출석부 패널·씬 배선")]
        public static void Build()
        {
            var prefab = BuildPrefab();
            AssignToScene(TitleScenePath, prefab);
            AssignToScene(GameScenePath, prefab);
            AssetDatabase.SaveAssets();
            Debug.Log("[AttendanceUiBuilder] 완료: 출석부 패널 프리팹 생성 + Title/GameScene UIManager 배선.");
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

            var root = new GameObject("AttendancePanel");
            var ctrl = root.AddComponent<AttendancePanelController>();

            // EditorConstruct 전에 배선해, 사다리 칸(BuildDayCell)과 패널 배경이 아트를 바로 반영하게 한다.
            var so = new SerializedObject(ctrl);
            so.FindProperty("_itemSlotPrefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>(ItemSlotPrefabPath);
            so.FindProperty("_boardSprite").objectReferenceValue = LoadSpriteAt(BoardSpritePath);
            so.FindProperty("_slotFrameSprite").objectReferenceValue = LoadSpriteAt(SlotFrameSpritePath);
            so.FindProperty("_buttonSprite").objectReferenceValue = LoadSpriteAt(ButtonSpritePath);
            so.ApplyModifiedPropertiesWithoutUndo();

            ctrl.EditorConstruct(); // 정적 계층(보상 사다리 30칸 포함)을 프리팹에 굽는다

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log($"[AttendanceUiBuilder] 프리팹 저장: {PrefabPath}");
            return prefab;
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
            Debug.LogWarning($"[AttendanceUiBuilder] 스프라이트를 찾지 못했습니다: {path}");
            return null;
        }

        private static void AssignToScene(string scenePath, GameObject prefab)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var uiManager = Object.FindAnyObjectByType<UIManager>(FindObjectsInactive.Include);
            if (uiManager == null)
            {
                Debug.LogWarning($"[AttendanceUiBuilder] {scene.name}에 UIManager가 없어 건너뜁니다.");
                return;
            }
            var so = new SerializedObject(uiManager);
            so.FindProperty("attendancePanelPrefab").objectReferenceValue = prefab;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[AttendanceUiBuilder] {scene.name} UIManager에 출석부 패널 배선 완료.");
        }
    }
}
