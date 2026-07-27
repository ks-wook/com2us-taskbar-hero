using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace TaskbarHero.ClientEditor
{
    /// <summary>
    /// 투명 오버레이 검증용 원클릭 빌드 & 실행 도구. 서버 없이 바로 전투가 도는 <b>BattleDevScene 하나만</b> 넣은
    /// Windows 빌드를 프로젝트 루트의 <c>Builds/TransparencyTest/</c>에 만들고, 성공 시 곧바로 실행한다.
    /// 창 투명화(DWM per-pixel alpha)·클릭 통과·배경 생략은 Windows 빌드에서만 동작하므로(에디터에서 확인 불가),
    /// <c>docs/투명-오버레이-창.md</c>의 검증 절차를 이 메뉴 하나로 재현한다.
    /// 빌드 전에 투명화 전제 설정(그래픽 API D3D11 고정 등)이 되돌아가지 않았는지 검사해 경고한다.
    /// 메뉴: TaskbarHero/Build/투명 오버레이 검증 빌드 & 실행 (BattleDevScene)
    /// </summary>
    public static class TransparentOverlayBuilder
    {
        private const string ScenePath = "Assets/Scenes/BattleDevScene.unity";
        private const string BuildDirName = "Builds";
        private const string OutDirName = "TransparencyTest";

        [MenuItem("TaskbarHero/Build/투명 오버레이 검증 빌드 & 실행 (BattleDevScene)")]
        public static void BuildAndRun()
        {
            if (!File.Exists(ScenePath))
            {
                Debug.LogError($"[TransparentOverlayBuilder] 씬을 찾지 못했습니다: {ScenePath}");
                return;
            }

            WarnIfTransparencyPrerequisitesBroken();

            string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
            string outDir = Path.Combine(projectRoot, BuildDirName, OutDirName);
            Directory.CreateDirectory(outDir);
            string exePath = Path.Combine(outDir, WindowsBuilder.SafeName(PlayerSettings.productName) + ".exe");

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = exePath,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };

            Debug.Log($"[TransparentOverlayBuilder] 투명 오버레이 검증 빌드 시작 → {exePath} (BattleDevScene 단독)");
            BuildReport report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[TransparentOverlayBuilder] 빌드 성공: {exePath} · 크기 {summary.totalSize / (1024 * 1024)}MB — " +
                          "실행 후 창 빈 영역에 바탕화면이 비치는지, 빈 영역 클릭이 뒤 창으로 통과하는지 확인하세요.");
                WindowsBuilder.LaunchBuild(exePath, outDir);
            }
            else
            {
                Debug.LogError($"[TransparentOverlayBuilder] 빌드 실패: {summary.result} (에러 {summary.totalErrors}개)");
            }
        }

        /// <summary>
        /// 투명화 전제 설정이 되돌아가 검은 배경이 나올 상황이면 경고를 남긴다(빌드는 계속 진행).
        /// 상세 근거는 docs/투명-오버레이-창.md의 「전제가 되는 프로젝트 설정」 참조.
        /// </summary>
        private static void WarnIfTransparencyPrerequisitesBroken()
        {
            var apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows64);
            if (PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64) ||
                apis.Length == 0 || apis[0] != GraphicsDeviceType.Direct3D11)
            {
                Debug.LogWarning("[TransparentOverlayBuilder] Windows 그래픽 API가 D3D11 고정이 아닙니다 " +
                                 $"(현재: {string.Join(", ", apis.Select(a => a.ToString()))}). " +
                                 "D3D12는 flip 모델이 강제되어 배경이 검게 나옵니다 — Player Settings에서 D3D11로 고정하세요.");
            }
            if (PlayerSettings.useFlipModelSwapchain)
            {
                Debug.LogWarning("[TransparentOverlayBuilder] useFlipModelSwapchain이 켜져 있습니다. " +
                                 "flip 모델 스왑체인은 레이어드 창 투명과 비호환이라 배경이 검게 나옵니다 — 꺼 주세요.");
            }
        }
    }
}
