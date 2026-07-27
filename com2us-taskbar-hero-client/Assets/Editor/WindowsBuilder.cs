using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace TaskbarHero.ClientEditor
{
    /// <summary>
    /// Windows(StandaloneWindows64) 원클릭 빌드 & 실행 도구. 프로젝트 루트의 <c>Builds/Windows/</c>에 결과물을 만들고,
    /// 빌드 성공 시 빌드된 실행 파일을 곧바로 실행한다.
    /// <b>현재 화면 비율을 유지한 채 크기를 키워</b> 빌드한다: PlayerSettings의 현재 기본 해상도 비율을 유지하고
    /// 높이를 <see cref="TargetHeight"/>(1080)로 스케일(예: 1280×720 → 1920×1080). 창 모드로 고정한다.
    /// 메뉴: TaskbarHero/Build/Windows 빌드 & 실행 (현재 비율 확대)
    /// </summary>
    public static class WindowsBuilder
    {
        private const int TargetHeight = 1080; // 확대 목표 높이(현재 720 → 1080). 비율은 현재 값에서 유지.
        private const string BuildDirName = "Builds";
        private const string PlatformDirName = "Windows";

        [MenuItem("TaskbarHero/Build/Windows 빌드 & 실행 (현재 비율 확대)")]
        public static void BuildWindows()
        {
            // 1) 현재 화면 비율을 유지하며 크기를 키운다(높이 기준 스케일). 재실행해도 비율이 같으면 동일 결과(멱등).
            int curW = PlayerSettings.defaultScreenWidth;
            int curH = PlayerSettings.defaultScreenHeight;
            if (curW <= 0 || curH <= 0)
            {
                curW = 1280;
                curH = 720;
            }
            int newH = TargetHeight;
            int newW = Mathf.RoundToInt((float)curW / curH * TargetHeight);

            PlayerSettings.defaultScreenWidth = newW;
            PlayerSettings.defaultScreenHeight = newH;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed; // 창 모드로 비율 유지
            PlayerSettings.defaultIsNativeResolution = false;
            Debug.Log($"[WindowsBuilder] 해상도 설정: {curW}x{curH} → {newW}x{newH} (비율 유지 확대, 창 모드).");

            // 2) 빌드에 포함된(enabled) 씬 목록.
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                Debug.LogError("[WindowsBuilder] 빌드에 포함된 씬이 없습니다(Build Settings 확인).");
                return;
            }

            // 3) 출력 경로: <프로젝트 루트>/Builds/Windows/<productName>.exe
            string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
            string outDir = Path.Combine(projectRoot, BuildDirName, PlatformDirName);
            Directory.CreateDirectory(outDir);
            string exePath = Path.Combine(outDir, SafeName(PlayerSettings.productName) + ".exe");

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = exePath,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };

            Debug.Log($"[WindowsBuilder] 빌드 시작 → {exePath} (씬 {scenes.Length}개)");
            BuildReport report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[WindowsBuilder] 빌드 성공: {exePath} · 크기 {summary.totalSize / (1024 * 1024)}MB · " +
                          $"해상도 {newW}x{newH}");
                LaunchBuild(exePath, outDir);
            }
            else
            {
                Debug.LogError($"[WindowsBuilder] 빌드 실패: {summary.result} (에러 {summary.totalErrors}개)");
            }
        }

        /// <summary>빌드된 실행 파일을 실행한다(작업 디렉터리는 빌드 폴더). 다른 빌더(TransparentOverlayBuilder)도 공용.</summary>
        internal static void LaunchBuild(string exePath, string workingDir)
        {
            if (!File.Exists(exePath))
            {
                Debug.LogError($"[WindowsBuilder] 실행 파일을 찾지 못해 실행을 건너뜁니다: {exePath}");
                return;
            }
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(exePath)
                {
                    WorkingDirectory = workingDir,
                    UseShellExecute = true,
                };
                System.Diagnostics.Process.Start(psi);
                Debug.Log($"[WindowsBuilder] 빌드 실행: {exePath}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[WindowsBuilder] 실행 실패: {e.Message}");
            }
        }

        /// <summary>파일명에 쓸 수 없는 문자를 '_'로 치환한다. 다른 빌더(TransparentOverlayBuilder)도 공용.</summary>
        internal static string SafeName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "TaskbarHero";
            }
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }
    }
}
