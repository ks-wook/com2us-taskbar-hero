using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using TaskbarHero.Client.Battle;
using TaskbarHero.Client.Game;

namespace TaskbarHero.ClientEditor
{
    /// <summary>
    /// GameScene에 던전 전투를 배선하는 에디터 도구.
    /// - BattleDevScene의 완성된 <see cref="BattleDevController"/>(파티·스킬·이펙트 포함)를 GameScene으로 복사하고
    ///   서버 구동 모드로 전환한다.
    /// - <see cref="DungeonBattleFlow"/>를 붙이고 monster_{code}.prefab 맵을 채운다.
    /// - 이전 단계의 StageEntry 오브젝트는 입장 중복을 막기 위해 제거한다.
    /// 메뉴: TaskbarHero/UI/던전 전투 배선
    /// </summary>
    public static class DungeonBattleBuilder
    {
        private const string BattleDevScenePath = "Assets/Scenes/BattleDevScene.unity";
        private const string GameScenePath = "Assets/Scenes/GameScene.unity";
        private const string MonsterDir = "Assets/Prefabs/Character/Monster";

        [MenuItem("TaskbarHero/UI/던전 전투 배선")]
        public static void Build()
        {
            // 1) BattleDevScene의 BattleDevController 복사(클립보드).
            EditorSceneManager.OpenScene(BattleDevScenePath, OpenSceneMode.Single);
            var src = Object.FindAnyObjectByType<BattleDevController>(FindObjectsInactive.Include);
            if (src == null)
            {
                Debug.LogError("[DungeonBattleBuilder] BattleDevScene에 BattleDevController가 없습니다.");
                return;
            }
            ComponentUtility.CopyComponent(src);

            // BattleDevScene의 스크롤 배경 설정(높이/중심/정렬/타일)을 읽어 GameScene 배경에 그대로 적용.
            var srcBg = Object.FindAnyObjectByType<ScrollingBackground>(FindObjectsInactive.Include);
            float bgWorldHeight = srcBg != null ? srcBg.worldHeight : 8f;
            float bgCenterY = srcBg != null ? srcBg.centerY : 1f;
            int bgSorting = srcBg != null ? srcBg.sortingOrder : -100;
            int bgTiles = srcBg != null ? srcBg.tileCount : 3;

            // 2) GameScene 열기 + 기존 배선 정리.
            var scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);

            var oldFlow = Object.FindAnyObjectByType<DungeonBattleFlow>(FindObjectsInactive.Include);
            if (oldFlow != null)
            {
                Object.DestroyImmediate(oldFlow.gameObject);
            }
            var oldEntry = Object.FindAnyObjectByType<StageEntryController>(FindObjectsInactive.Include);
            if (oldEntry != null)
            {
                Object.DestroyImmediate(oldEntry.gameObject); // 입장 중복 방지(DungeonBattleFlow가 대체)
            }
            var oldBg = Object.FindAnyObjectByType<ScrollingBackground>(FindObjectsInactive.Include);
            if (oldBg != null)
            {
                Object.DestroyImmediate(oldBg.gameObject);
            }

            // 3) DungeonBattle 오브젝트 = BattleDevController(붙여넣기) + DungeonBattleFlow.
            var go = new GameObject("DungeonBattle");
            ComponentUtility.PasteComponentAsNew(go);
            var battle = go.GetComponent<BattleDevController>();
            if (battle == null)
            {
                Debug.LogError("[DungeonBattleBuilder] BattleDevController 붙여넣기 실패.");
                return;
            }
            battle.serverMode = true; // 서버 웨이브 유한 전투

            var flow = go.AddComponent<DungeonBattleFlow>();
            var fso = new SerializedObject(flow);
            fso.FindProperty("battle").objectReferenceValue = battle;

            // 4) monster_{code}.prefab 맵 채우기.
            var listProp = fso.FindProperty("monsterPrefabs");
            listProp.ClearArray();
            int idx = 0;
            foreach (var file in Directory.GetFiles(MonsterDir, "monster_*.prefab"))
            {
                string path = file.Replace('\\', '/');
                string name = Path.GetFileNameWithoutExtension(path); // monster_9001
                string codeStr = name.Substring("monster_".Length);
                if (!int.TryParse(codeStr, out int code))
                {
                    continue;
                }
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    continue;
                }
                listProp.InsertArrayElementAtIndex(idx);
                var el = listProp.GetArrayElementAtIndex(idx);
                el.FindPropertyRelative("code").intValue = code;
                el.FindPropertyRelative("prefab").objectReferenceValue = prefab;
                idx++;
            }

            // 5) 배경: 스크롤 배경 오브젝트 생성(설정 이식) + backgroundType(1~5) 스프라이트 배선.
            var bgGo = new GameObject("DungeonBackground");
            var bg = bgGo.AddComponent<ScrollingBackground>();
            bg.worldHeight = bgWorldHeight;
            bg.centerY = bgCenterY;
            bg.sortingOrder = bgSorting;
            bg.tileCount = bgTiles;
            bg.autoFitCamera = true; // GameScene 카메라 뷰에 꽉 차게
            fso.FindProperty("background").objectReferenceValue = bg;

            var bgProp = fso.FindProperty("backgrounds");
            bgProp.ClearArray();
            for (int i = 0; i < 5; i++)
            {
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Art/Background/dungeon_bg_{i + 1}.png");
                bgProp.InsertArrayElementAtIndex(i);
                bgProp.GetArrayElementAtIndex(i).objectReferenceValue = sprite;
            }

            fso.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[DungeonBattleBuilder] 완료: GameScene에 DungeonBattle 배선, 몬스터 프리팹 {idx}종 매핑.");
        }
    }
}
