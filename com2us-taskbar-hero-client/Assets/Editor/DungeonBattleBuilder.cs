using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;
using TaskbarHero.Client.Battle;
using TaskbarHero.Client.Game;

namespace TaskbarHero.ClientEditor
{
    /// <summary>
    /// GameScene의 전투를 BattleDevScene(정본)에서 복제해 배선하는 에디터 도구.
    /// BattleDevScene에서 전투를 개발한 뒤 이 빌더를 재실행하면 GameScene이 그 구성을 그대로 따라간다.
    /// - 완성된 <see cref="BattleDevController"/>(파티·스킬·이펙트 포함)를 복사하고 서버 구동 모드로 전환.
    /// - BattleDevScene의 비(非)개발용 씬 구성을 복제: Global Light 2D(URP 2D 조명), 스폰 앵커(PlayerSpawn·MonsterSpawn),
    ///   스크롤 배경 설정. 단 배경·스폰은 <see cref="GameSceneBattleLiftY"/>만큼 위로 올려 화면 맨 아래에
    ///   하단 HUD 아이콘 줄이 들어갈 빈 띠를 남긴다(카메라는 BattleDevScene과 동일하게 유지).
    /// - <see cref="DungeonBattleFlow"/>를 붙이고 monster_{code}.prefab 맵을 채운다.
    /// - <b>개발용 UI는 제외한다</b>: 초상화·아군 스킬 슬롯·아군 HP바(SkillCooldownUI)와 적 HP바·하네스 IMGUI는
    ///   GameScene에 복제하지 않는다(serverMode에서 <c>OnGUI</c>가 비활성, SkillUICanvas는 미복제).
    /// - 이전 단계의 StageEntry·플레이스홀더 라벨은 제거한다.
    /// 메뉴: TaskbarHero/UI/던전 전투 배선
    /// </summary>
    public static class DungeonBattleBuilder
    {
        private const string BattleDevScenePath = "Assets/Scenes/BattleDevScene.unity";
        private const string GameScenePath = "Assets/Scenes/GameScene.unity";
        private const string MonsterDir = "Assets/Prefabs/Character/Monster";
        private const string LightGoName = "Global Light 2D";
        /// <summary>체력바 프레임 아트. 적 HP바(BattleDevController)와 아군 HP바(SkillCooldownUI)가 같은 것을 쓴다.</summary>
        private const string HpBarFramePath = "Assets/Art/Icon/Combat/체력바.png";
        /// <summary>보스 등장 경고 이미지(중앙에서 커졌다 작아지는 연출에 쓴다).</summary>
        private const string BossWarningPath = "Assets/Art/UI/System/boss_warning.png";

        /// <summary>
        /// GameScene 전용 전투 띠 상향 오프셋(월드 단위). BattleDevScene은 배경 띠가 화면 바닥에 붙어 있는데,
        /// GameScene은 그 자리에 하단 HUD 아이콘 줄이 놓이므로 배경·스폰을 통째로 이만큼 위로 올려
        /// 화면 맨 아래에 아이콘 줄이 들어갈 빈 띠를 만든다(카메라는 그대로 두어 시야는 유지).
        /// GameScene 창(정사각형) 기준 1 월드 단위 ≈ 캔버스 180 단위 → 1.2는 약 216 단위,
        /// 아이콘 줄(하단 40~190)보다 위에서 배경이 시작된다.
        /// </summary>
        private const float GameSceneBattleLiftY = 1.2f;

        [MenuItem("TaskbarHero/UI/던전 전투 배선")]
        public static void Build()
        {
            // ── 1) BattleDevScene(정본)에서 구성 캡처 ──
            EditorSceneManager.OpenScene(BattleDevScenePath, OpenSceneMode.Single);
            var src = Object.FindAnyObjectByType<BattleDevController>(FindObjectsInactive.Include);
            if (src == null)
            {
                Debug.LogError("[DungeonBattleBuilder] BattleDevScene에 BattleDevController가 없습니다.");
                return;
            }

            // 스폰 앵커 위치(없으면 컨트롤러 폴백과 동일한 기본값).
            // GameScene은 하단 HUD 자리를 비우기 위해 배경과 함께 통째로 위로 올린다(GameSceneBattleLiftY).
            Vector3 playerSpawnPos = src.playerSpawn != null ? src.playerSpawn.position : new Vector3(-4.5f, -1.6f, 0f);
            Vector3 monsterSpawnPos = src.monsterSpawn != null ? src.monsterSpawn.position : new Vector3(2.5f, -1.6f, 0f);
            playerSpawnPos.y += GameSceneBattleLiftY;
            monsterSpawnPos.y += GameSceneBattleLiftY;

            // 스크롤 배경 설정(높이/중심/정렬/타일/하단 노출 비율).
            var srcBg = Object.FindAnyObjectByType<ScrollingBackground>(FindObjectsInactive.Include);
            float bgWorldHeight = srcBg != null ? srcBg.worldHeight : 8f;
            float bgCenterY = (srcBg != null ? srcBg.centerY : 1f) + GameSceneBattleLiftY;
            int bgSorting = srcBg != null ? srcBg.sortingOrder : -100;
            int bgTiles = srcBg != null ? srcBg.tileCount : 3;
            float bgVisibleFrac = srcBg != null ? srcBg.visibleBottomFrac : 1f / 3f;

            // 카메라 설정: GameScene 카메라를 BattleDevScene과 동일하게 맞춰 전투 프레이밍(배경·길·캐릭터 위치)을 일치시킨다.
            var srcCam = Camera.main;
            float camY = srcCam != null ? srcCam.transform.position.y : 1f;
            float camOrtho = srcCam != null ? srcCam.orthographicSize : 4f;

            // Global Light 2D(URP 2D 조명) 컴포넌트 캡처.
            Component srcLight = FindLight2D();

            // ── 2) Global Light 2D를 GameScene에 복제(컴포넌트 클립보드는 씬 전환에도 유지) ──
            //     ⚠️ 씬을 전환하기 전에 반드시 저장한다(미저장 변경은 재오픈 시 폐기됨).
            if (srcLight != null)
            {
                ComponentUtility.CopyComponent(srcLight);
                var gs = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
                if (FindLight2D() == null)
                {
                    var lgo = new GameObject(LightGoName);
                    ComponentUtility.PasteComponentAsNew(lgo);
                    EditorSceneManager.MarkSceneDirty(gs);
                    EditorSceneManager.SaveScene(gs); // 컨트롤러 복사를 위해 씬 전환하므로 먼저 저장
                }
                // 컨트롤러 복사를 위해 BattleDevScene 재오픈.
                EditorSceneManager.OpenScene(BattleDevScenePath, OpenSceneMode.Single);
                src = Object.FindAnyObjectByType<BattleDevController>(FindObjectsInactive.Include);
            }

            // ── 3) BattleDevController 복사(클립보드) ──
            ComponentUtility.CopyComponent(src);

            // ── 4) GameScene 열기 + 기존 배선 정리 ──
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
            DestroyIfExists("PlayerSpawn");
            DestroyIfExists("MonsterSpawn");
            DestroyIfExists("SceneLabel"); // "GameScene" 플레이스홀더 워터마크 제거(BattleDevScene엔 없음)
            DestroyIfExists("SkillUICanvas"); // 이전 복제본 제거(재실행 idempotency)

            // 카메라를 BattleDevScene 기준으로 맞춘다(y·orthographicSize). x는 전투 중 컨트롤러가 추종.
            var gameCam = Camera.main;
            if (gameCam != null)
            {
                var cp = gameCam.transform.position;
                gameCam.transform.position = new Vector3(cp.x, camY, cp.z);
                gameCam.orthographic = true;
                gameCam.orthographicSize = camOrtho;
            }

            // ── 5) DungeonBattle 오브젝트 = BattleDevController(붙여넣기) + DungeonBattleFlow ──
            var go = new GameObject("DungeonBattle");
            ComponentUtility.PasteComponentAsNew(go);
            var battle = go.GetComponent<BattleDevController>();
            if (battle == null)
            {
                Debug.LogError("[DungeonBattleBuilder] BattleDevController 붙여넣기 실패.");
                return;
            }
            battle.serverMode = true; // 서버 웨이브 유한 전투

            // 스폰 앵커 생성 + 컨트롤러에 배선(씬 간 참조는 복사되지 않으므로 새로 만든다).
            var playerSpawn = new GameObject("PlayerSpawn");
            playerSpawn.transform.position = playerSpawnPos;
            var monsterSpawn = new GameObject("MonsterSpawn");
            monsterSpawn.transform.position = monsterSpawnPos;
            var battleSo = new SerializedObject(battle);
            battleSo.FindProperty("playerSpawn").objectReferenceValue = playerSpawn.transform;
            battleSo.FindProperty("monsterSpawn").objectReferenceValue = monsterSpawn.transform;
            // 보스 왕관 아이콘 배선(보스 몬스터 머리 위 표시용).
            var bossIcon = LoadSprite("Assets/Art/Icon/stage_king.png");
            battleSo.FindProperty("bossIcon").objectReferenceValue = bossIcon;
            // 몬스터 머리 위 HP바 프레임 아트 배선(미배선 시 단색 배경으로 폴백된다).
            var hpBarFrame = LoadSprite(HpBarFramePath);
            battleSo.FindProperty("enemyHpBarFrame").objectReferenceValue = hpBarFrame;
            // 보스 등장 경고 이미지 배선(미배선 시 붉은 "Warning!!" 문구로 폴백된다).
            var bossWarning = LoadSprite(BossWarningPath);
            battleSo.FindProperty("bossWarningImage").objectReferenceValue = bossWarning;
            battleSo.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"[DungeonBattleBuilder] 보스 왕관 아이콘 {(bossIcon != null ? "배선" : "없음")}, "
                      + $"몬스터 체력바 프레임 {(hpBarFrame != null ? "배선" : "없음")}, "
                      + $"보스 경고 이미지 {(bossWarning != null ? "배선" : "없음")}.");

            var flow = go.AddComponent<DungeonBattleFlow>();
            var fso = new SerializedObject(flow);
            fso.FindProperty("battle").objectReferenceValue = battle;

            // ── 6) monster_{code}.prefab 맵 채우기 ──
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

            // ── 7) 배경: 스크롤 배경 오브젝트 생성(설정 이식) + backgroundType(1~5) 스프라이트 배선 ──
            var bgGo = new GameObject("DungeonBackground");
            var bg = bgGo.AddComponent<ScrollingBackground>();
            bg.worldHeight = bgWorldHeight;
            bg.centerY = bgCenterY;
            bg.sortingOrder = bgSorting;
            bg.tileCount = bgTiles;
            bg.visibleBottomFrac = bgVisibleFrac;
            bg.autoFitCamera = false; // 카메라를 BattleDevScene과 일치시켰으므로 고정 지오메트리(worldHeight/centerY)로 동일 프레이밍
            fso.FindProperty("background").objectReferenceValue = bg;

            var bgProp = fso.FindProperty("backgrounds");
            bgProp.ClearArray();
            for (int i = 0; i < 5; i++)
            {
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Art/Background/dungeon_bg_{i + 1}.png");
                bgProp.InsertArrayElementAtIndex(i);
                bgProp.GetArrayElementAtIndex(i).objectReferenceValue = sprite;
            }

            // 레벨업 글로우 프레임(LevelUpGlow_01~30) 배선(순서대로).
            var lvlProp = fso.FindProperty("levelUpFrames");
            lvlProp.ClearArray();
            int lvlCount = 0;
            for (int i = 1; i <= 30; i++)
            {
                var sp = LoadSprite($"Assets/Art/Effect/Character/LevelUpGlow/LevelUpGlow_{i:00}.png");
                if (sp == null)
                {
                    continue;
                }
                lvlProp.InsertArrayElementAtIndex(lvlCount);
                lvlProp.GetArrayElementAtIndex(lvlCount).objectReferenceValue = sp;
                lvlCount++;
            }
            Debug.Log($"[DungeonBattleBuilder] 레벨업 글로우 프레임 {lvlCount}장 배선.");

            // 레벨업 배너 이미지("LEVEL UP!") 배선 — 글로우와 함께 캐릭터 위에 떠올랐다 사라진다.
            var lvlBanner = LoadSprite("Assets/Art/UI/System/레벨업.png");
            fso.FindProperty("levelUpBanner").objectReferenceValue = lvlBanner;
            Debug.Log($"[DungeonBattleBuilder] 레벨업 배너 이미지 {(lvlBanner != null ? "배선" : "없음")}.");

            // 스테이지 입장 배너 프리팹(있으면 배선 — 없으면 런타임 코드 생성 폴백). 배너 프리팹은
            // 'TaskbarHero/UI/스테이지 입장 배너 프리팹 생성'(StageEnterBannerBuilder)으로 만든다.
            var bannerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/StageEnterBanner.prefab");
            fso.FindProperty("stageEnterBannerPrefab").objectReferenceValue = bannerPrefab;
            Debug.Log($"[DungeonBattleBuilder] 입장 배너 프리팹 {(bannerPrefab != null ? "배선" : "없음(코드 폴백)")}.");

            // 스테이지 진행도 바 — 진행 위치 화살표·우측 끝 보스(목표) 아이콘 배선.
            var progressArrow = LoadSprite("Assets/Art/UI/straight_up.png");
            var progressBoss = LoadSprite("Assets/Art/UI/boss.png");
            fso.FindProperty("stageProgressArrow").objectReferenceValue = progressArrow;
            fso.FindProperty("stageProgressBoss").objectReferenceValue = progressBoss;
            Debug.Log($"[DungeonBattleBuilder] 진행도 바 화살표 {(progressArrow != null ? "배선" : "없음")}, " +
                      $"보스 아이콘 {(progressBoss != null ? "배선" : "없음")}.");

            fso.ApplyModifiedPropertiesWithoutUndo();

            // ── 8) 전투 UI(초상화·아군 스킬 슬롯·아군 HP바) 복제: BattleDevScene의 SkillUICanvas를 그대로 GameScene에 ──
            //     (적 HP바는 BattleDevController.OnGUI가 serverMode에서도 그린다.)
            bool skillUiCopied = CopySkillUICanvas(scene);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[DungeonBattleBuilder] 완료: DungeonBattle 배선, 몬스터 {idx}종, " +
                      $"Global Light 2D {(srcLight != null ? "복제" : "원본없음")}, 스폰 앵커 배선, " +
                      $"카메라 정합(y={camY:0.##}, ortho={camOrtho:0.##}), SkillUICanvas {(skillUiCopied ? "복제" : "원본없음")}.");
        }

        /// <summary>스프라이트를 경로에서 로드한다(Single/Multiple 스프라이트 모드 모두 대응).</summary>
        private static Sprite LoadSprite(string path)
        {
            var s = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (s != null)
            {
                return s;
            }
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (o is Sprite sp)
                {
                    return sp;
                }
            }
            Debug.LogWarning($"[DungeonBattleBuilder] 스프라이트를 찾지 못했습니다: {path}");
            return null;
        }

        /// <summary>BattleDevScene의 SkillUICanvas(초상화·스킬 슬롯·아군 HP바)를 GameScene으로 복제한다.
        /// 부가 씬으로 잠깐 로드해 계층을 Instantiate한 뒤 대상 씬으로 옮긴다(내부 템플릿 참조는 보존,
        /// controller 참조는 씬 종료로 끊겨 런타임에 자동 재탐색). 반환: 복제 성공 여부.</summary>
        private static bool CopySkillUICanvas(Scene targetScene)
        {
            var devScene = EditorSceneManager.OpenScene(BattleDevScenePath, OpenSceneMode.Additive);
            GameObject srcUi = null;
            foreach (var root in devScene.GetRootGameObjects())
            {
                if (root.name == "SkillUICanvas")
                {
                    srcUi = root;
                    break;
                }
            }

            bool ok = false;
            if (srcUi != null)
            {
                var clone = Object.Instantiate(srcUi);
                clone.name = "SkillUICanvas";
                SceneManager.MoveGameObjectToScene(clone, targetScene);

                // 아군 체력바 프레임 아트 배선(적 HP바와 같은 아트). 스프라이트 참조는 복제 시에도 보존되지만,
                // BattleDevScene 쪽이 비어 있어도 GameScene은 프레임을 갖도록 여기서 한 번 더 확정한다.
                var skillUi = clone.GetComponentInChildren<SkillCooldownUI>(true);
                if (skillUi != null)
                {
                    var so = new SerializedObject(skillUi);
                    so.FindProperty("hpBarFrame").objectReferenceValue = LoadSprite(HpBarFramePath);
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                ok = true;
            }
            else
            {
                Debug.LogWarning("[DungeonBattleBuilder] BattleDevScene에 SkillUICanvas가 없어 전투 UI를 복제하지 못했습니다.");
            }

            EditorSceneManager.CloseScene(devScene, true);
            return ok;
        }

        /// <summary>현재 씬에서 Global Light 2D 컴포넌트를 찾는다(URP 타입 직접 참조 없이 이름으로).</summary>
        private static Component FindLight2D()
        {
            var go = GameObject.Find(LightGoName);
            if (go == null)
            {
                return null;
            }
            foreach (var comp in go.GetComponents<Component>())
            {
                if (comp != null && comp.GetType().Name == "Light2D")
                {
                    return comp;
                }
            }
            return null;
        }

        /// <summary>이름으로 오브젝트를 찾아 있으면 제거한다(재실행 idempotency).</summary>
        private static void DestroyIfExists(string name)
        {
            var go = GameObject.Find(name);
            if (go != null)
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
