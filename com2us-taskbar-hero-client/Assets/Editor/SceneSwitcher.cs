using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TaskbarHero.ClientEditor
{
    /// <summary>
    /// 상단 메뉴에서 작업 씬을 바로 여는 도구. 메뉴: <b>TaskbarHero/씬/…</b> (단축키 Alt+1~5)
    /// <para>
    /// 저장하지 않은 변경이 있으면 Unity 표준 저장 프롬프트를 먼저 띄우고(취소하면 전환하지 않음),
    /// 현재 열려 있는 씬에는 체크 표시를 단다. 플레이 중에는 에디터 씬을 열 수 없으므로 항목이 비활성화된다.
    /// </para>
    /// 씬을 새로 추가하면 이 파일에 항목을 하나 늘린다([MenuItem]은 컴파일 타임 속성이라 동적 생성이 불가).
    /// </summary>
    public static class SceneSwitcher
    {
        private const string SceneFolder = "Assets/Scenes/";
        private const string MenuRoot = "TaskbarHero/씬/";

        // 메뉴 표시 이름(단축키 제외). Menu.SetChecked에는 단축키를 뺀 이 경로를 넘겨야 한다.
        private const string TitleItem = MenuRoot + "TitleScene";
        private const string CreateCharacterItem = MenuRoot + "CreateCharacterScene";
        private const string GameItem = MenuRoot + "GameScene";
        private const string TeamListItem = MenuRoot + "TeamListScene (파티 편성)";
        private const string BattleDevItem = MenuRoot + "BattleDevScene (전투 개발용)";

        private const string TitlePath = SceneFolder + "TitleScene.unity";
        private const string CreateCharacterPath = SceneFolder + "CreateCharacterScene.unity";
        private const string GamePath = SceneFolder + "GameScene.unity";
        private const string TeamListPath = SceneFolder + "TeamListScene.unity";
        private const string BattleDevPath = SceneFolder + "BattleDevScene.unity";

        // ── TitleScene ──

        [MenuItem(TitleItem + " &1", false, 0)]
        private static void OpenTitle() => Open(TitlePath);

        [MenuItem(TitleItem + " &1", true)]
        private static bool OpenTitleValidate() => Validate(TitleItem, TitlePath);

        // ── CreateCharacterScene ──

        [MenuItem(CreateCharacterItem + " &2", false, 1)]
        private static void OpenCreateCharacter() => Open(CreateCharacterPath);

        [MenuItem(CreateCharacterItem + " &2", true)]
        private static bool OpenCreateCharacterValidate() => Validate(CreateCharacterItem, CreateCharacterPath);

        // ── GameScene ──

        [MenuItem(GameItem + " &3", false, 2)]
        private static void OpenGame() => Open(GamePath);

        [MenuItem(GameItem + " &3", true)]
        private static bool OpenGameValidate() => Validate(GameItem, GamePath);

        // ── TeamListScene (파티 편성 전용 씬 — GameScene 하단 [편성]으로 진입) ──

        [MenuItem(TeamListItem + " &4", false, 3)]
        private static void OpenTeamList() => Open(TeamListPath);

        [MenuItem(TeamListItem + " &4", true)]
        private static bool OpenTeamListValidate() => Validate(TeamListItem, TeamListPath);

        // ── BattleDevScene (구분선 뒤: 빌드에 포함되지 않는 개발용 하네스) ──

        [MenuItem(BattleDevItem + " &5", false, 20)]
        private static void OpenBattleDev() => Open(BattleDevPath);

        [MenuItem(BattleDevItem + " &5", true)]
        private static bool OpenBattleDevValidate() => Validate(BattleDevItem, BattleDevPath);

        /// <summary>저장 프롬프트를 거친 뒤 해당 씬을 단독(Single)으로 연다. 사용자가 저장을 취소하면 아무것도 하지 않는다.</summary>
        private static void Open(string scenePath)
        {
            if (!File.Exists(scenePath))
            {
                Debug.LogError($"[SceneSwitcher] 씬 파일을 찾을 수 없습니다: {scenePath}");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return; // 저장 프롬프트에서 취소 → 현재 씬 유지
            }

            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        }

        /// <summary>현재 열린 씬이면 체크 표시를 달고, 플레이 중이거나 파일이 없으면 항목을 비활성화한다.</summary>
        private static bool Validate(string menuPath, string scenePath)
        {
            Menu.SetChecked(menuPath, EditorSceneManager.GetActiveScene().path == scenePath);
            return !EditorApplication.isPlaying && File.Exists(scenePath);
        }
    }
}
