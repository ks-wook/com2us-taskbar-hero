using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using TaskbarHero.Client.Managers;

namespace TaskbarHero.ClientEditor
{
    /// <summary>
    /// <b>전체 게임</b> 원클릭 빌드 &amp; 실행 도구. TitleScene을 시작 씬으로 실제 게임 플로우 전체
    /// (Title → CreateCharacter → Game)를 담은 Windows 빌드를 만들고, 성공 시 곧바로 실행한다.
    /// 개발 전용 씬(BattleDevScene)은 포함하지 않는다.
    /// 창 형태(투명 오버레이·작업표시줄 도킹)는 런타임의 <c>TaskbarWindow</c>가 제어하므로 해상도 설정은 손대지 않는다.
    /// <para>
    /// <b>빌드 옵션(접속 환경)</b>이 둘 있고, 차이는 <b>접속 서버 주소뿐</b>이다(주소 정의: <see cref="ServerEnvironment"/>).
    /// <list type="bullet">
    /// <item>Dev — 로컬 서버(<c>http://localhost:5160</c> / <c>http://localhost:5247</c>). 출력 <c>Builds/Game/</c></item>
    /// <item>QA — 원격 서버(<c>https://…ts.net</c> / <c>https://…ts.net:8443</c>). 출력 <c>Builds/Game-QA/</c></item>
    /// </list>
    /// QA 빌드는 스크립팅 정의 심볼 <c>TH_QA</c>를 <c>extraScriptingDefines</c>로만 넣으므로
    /// <b>프로젝트 설정(Player Settings)이나 에디터 컴파일 상태를 건드리지 않는다</b>(빌드 후 되돌릴 것도 없다).
    /// 출력 폴더가 갈라져 두 빌드를 동시에 보관·실행할 수 있다.
    /// </para>
    /// 메뉴: TaskbarHero/Build/전체 게임 빌드 &amp; 실행 (Dev · 로컬) / (QA · 원격)
    /// </summary>
    public static class FullGameBuilder
    {
        private const string BuildDirName = "Builds";
        private const string DevOutDirName = "Game";
        private const string QaOutDirName = "Game-QA";

        // 실제 게임 플로우 순서(첫 항목 = 부팅 씬).
        private static readonly string[] GameScenes =
        {
            "Assets/Scenes/TitleScene.unity",
            "Assets/Scenes/CreateCharacterScene.unity",
            "Assets/Scenes/GameScene.unity",
            "Assets/Scenes/TeamListScene.unity",   // 파티 편성 전용 씬(GameScene 하단 [편성] 버튼으로 진입)
        };

        [MenuItem("TaskbarHero/Build/전체 게임 빌드 & 실행 (Dev · 로컬)", false, 0)]
        public static void BuildAndRunDev() => BuildAndRun(ServerEnvironmentKind.Dev);

        [MenuItem("TaskbarHero/Build/전체 게임 빌드 & 실행 (QA · 원격)", false, 1)]
        public static void BuildAndRunQa() => BuildAndRun(ServerEnvironmentKind.Qa);

        /// <summary>지정한 접속 환경으로 전체 게임을 빌드하고 성공 시 실행한다.</summary>
        private static void BuildAndRun(ServerEnvironmentKind environment)
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
            string outDir = Path.Combine(projectRoot, BuildDirName, OutDirNameOf(environment));
            Directory.CreateDirectory(outDir);
            string exePath = Path.Combine(outDir, WindowsBuilder.SafeName(PlayerSettings.productName) + ".exe");

            var options = new BuildPlayerOptions
            {
                scenes = GameScenes,
                locationPathName = exePath,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
                extraScriptingDefines = ExtraDefinesOf(environment),
            };

            string envName = ServerEnvironment.DisplayNameOf(environment);
            Debug.Log($"[FullGameBuilder] 전체 게임 빌드 시작({envName}) → {exePath}" +
                      $" (씬 {GameScenes.Length}개, 시작=TitleScene)\n" +
                      $"  계정 서버: {ServerEnvironment.AccountBaseUrlOf(environment)}\n" +
                      $"  게임 서버: {ServerEnvironment.GameBaseUrlOf(environment)}");
            BuildReport report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[FullGameBuilder] 빌드 성공({envName}): {exePath} · 크기 {summary.totalSize / (1024 * 1024)}MB");
                WindowsBuilder.LaunchBuild(exePath, outDir);
            }
            else
            {
                Debug.LogError($"[FullGameBuilder] 빌드 실패({envName}): {summary.result} (에러 {summary.totalErrors}개)");
            }
        }

        /// <summary>환경별 출력 폴더 이름(두 빌드를 동시에 보관할 수 있게 분리한다).</summary>
        private static string OutDirNameOf(ServerEnvironmentKind environment)
            => environment == ServerEnvironmentKind.Qa ? QaOutDirName : DevOutDirName;

        /// <summary>환경별 추가 스크립팅 정의 심볼. QA만 <c>TH_QA</c>를 넣는다(프로젝트 설정은 건드리지 않는다).</summary>
        private static string[] ExtraDefinesOf(ServerEnvironmentKind environment)
            => environment == ServerEnvironmentKind.Qa
                ? new[] { ServerEnvironment.QaDefineSymbol }
                : new string[0];
    }
}
