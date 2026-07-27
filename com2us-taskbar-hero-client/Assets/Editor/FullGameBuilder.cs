using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace TaskbarHero.ClientEditor
{
    /// <summary>
    /// <b>전체 게임</b> 원클릭 빌드 & 실행 도구. TitleScene을 시작 씬으로 실제 게임 플로우 전체
    /// (Title → CreateCharacter → Game)를 담은 Windows 빌드를 프로젝트 루트의 <c>Builds/Game/</c>에 만들고,
    /// 성공 시 곧바로 실행한다. 개발 전용 씬(BattleDevScene)은 포함하지 않는다.
    /// 창 형태(투명 오버레이·작업표시줄 도킹)는 런타임의 <c>TaskbarWindow</c>가 제어하므로 해상도 설정은 손대지 않는다.
    /// 메뉴: TaskbarHero/Build/전체 게임 빌드 & 실행 (Title→Game)
    /// </summary>
    public static class FullGameBuilder
    {
        private const string BuildDirName = "Builds";
        private const string OutDirName = "Game";

        // 실제 게임 플로우 순서(첫 항목 = 부팅 씬).
        private static readonly string[] GameScenes =
        {
            "Assets/Scenes/TitleScene.unity",
            "Assets/Scenes/CreateCharacterScene.unity",
            "Assets/Scenes/GameScene.unity",
        };

        [MenuItem("TaskbarHero/Build/전체 게임 빌드 & 실행 (Title→Game)")]
        public static void BuildAndRun()
        {
            foreach (var scene in GameScenes)
            {
                if (!File.Exists(scene))
                {
                    Debug.LogError($"[FullGameBuilder] 씬을 찾지 못했습니다: {scene}");
                    return;
                }
            }

            string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
            string outDir = Path.Combine(projectRoot, BuildDirName, OutDirName);
            Directory.CreateDirectory(outDir);
            string exePath = Path.Combine(outDir, WindowsBuilder.SafeName(PlayerSettings.productName) + ".exe");

            var options = new BuildPlayerOptions
            {
                scenes = GameScenes,
                locationPathName = exePath,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };

            Debug.Log($"[FullGameBuilder] 전체 게임 빌드 시작 → {exePath} (씬 {GameScenes.Length}개, 시작=TitleScene)");
            BuildReport report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[FullGameBuilder] 빌드 성공: {exePath} · 크기 {summary.totalSize / (1024 * 1024)}MB");
                WindowsBuilder.LaunchBuild(exePath, outDir);
            }
            else
            {
                Debug.LogError($"[FullGameBuilder] 빌드 실패: {summary.result} (에러 {summary.totalErrors}개)");
            }
        }
    }
}
