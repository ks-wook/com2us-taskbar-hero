using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using TaskbarHero.Client.Managers;
using Debug = UnityEngine.Debug;

namespace TaskbarHero.ClientEditor
{
    /// <summary>
    /// <b>배포 패키지</b> 원클릭 빌드 도구 — 이 프로젝트의 <b>유일한 빌드 옵션</b>이다.
    /// <para>
    /// 유니티 프로젝트 구조를 모르는 사람이 <b>폴더째 받아서 바로 플레이</b>할 수 있도록,
    /// 게임 본체를 한 단계 아래 <c>Game/</c>에 넣고 그 <b>한 단계 위</b>에 플레이 가이드와
    /// 실행 버튼 역할의 런처 exe를 함께 놓는다.
    /// </para>
    /// <code>
    /// Builds/TaskbarHero/          ← 이 폴더를 통째로 압축해 전달한다
    ///   Com2us Taskbar Hero 실행.exe   ← 런처(버튼). 아래 Game/의 실제 exe를 실행한다
    ///   게임-플레이-가이드.html    ← docs/게임-플레이-가이드.html (스크린샷을 내장한 단일 파일)
    ///   Game/                      ← 유니티 빌드 결과물(exe + *_Data + 런타임 DLL)
    /// </code>
    /// <para>
    /// 접속 환경은 <b>QA(원격 서버)</b> 고정이다 — 남에게 건네는 배포본이므로 로컬 서버 주소로는
    /// 아무것도 할 수 없다. 스크립팅 정의 심볼 <c>TH_QA</c>를 <c>extraScriptingDefines</c>로만 넣으므로
    /// <b>Player Settings·에디터 컴파일 상태는 건드리지 않는다</b>. 받는 사람이 로컬 서버로 붙어야 하면
    /// 게임 안 타이틀 화면 우측 하단 톱니바퀴에서 접속처를 바꿀 수 있다(<see cref="ServerEnvironment"/>).
    /// </para>
    /// 메뉴: TaskbarHero/Build/배포 패키지 빌드 (게임 + 플레이 가이드)
    /// </summary>
    public static class GameDistributionBuilder
    {
        private const string BuildDirName = "Builds";

        /// <summary>배포 루트 폴더 이름. 이 폴더를 통째로 압축해 전달한다.</summary>
        private const string PackageDirName = "TaskbarHero";

        /// <summary>게임 본체(유니티 빌드 결과물)를 담는 하위 폴더 이름.</summary>
        private const string GameDirName = "Game";

        /// <summary>배포 루트에 놓이는 실행 버튼(런처)의 파일 이름.</summary>
        private const string LauncherFileName = "Com2us Taskbar Hero 실행.exe";

        /// <summary>C# 컴파일러를 찾지 못했을 때 런처 대신 만드는 배치 파일 이름.</summary>
        private const string LauncherFallbackFileName = "Com2us Taskbar Hero 실행.cmd";

        /// <summary>배포 루트에 놓이는 플레이 가이드의 파일 이름.</summary>
        private const string GuideFileName = "게임-플레이-가이드.html";

        /// <summary>가이드 원본 경로(프로젝트 루트 기준). 이미지는 이 파일 위치를 기준으로 상대 참조된다.</summary>
        private const string GuideSourceRelativePath = "docs/게임-플레이-가이드.html";

        // 실제 게임 플로우 순서(첫 항목 = 부팅 씬). 개발 전용 씬(BattleDevScene·AnimDevScene 등)은 넣지 않는다.
        private static readonly string[] GameScenes =
        {
            "Assets/Scenes/TitleScene.unity",
            "Assets/Scenes/CreateCharacterScene.unity",
            "Assets/Scenes/GameScene.unity",
            "Assets/Scenes/TeamListScene.unity",   // 파티 편성 전용 씬(GameScene 하단 [편성] 버튼으로 진입)
        };

        /// <summary>가이드 HTML의 로컬 PNG 참조(<c>src="images/guide/01-title.png"</c>). data URI·외부 URL은 콜론으로 걸러 제외한다.</summary>
        private static readonly Regex GuideImagePattern =
            new Regex("src=\"(?<path>[^\":]+\\.png)\"", RegexOptions.IgnoreCase);

        /// <summary>가이드 HTML 말미의 「개발 문서」 링크 목록. 저장소 안 .md를 가리켜 배포본에서는 전부 깨지므로 떼어 낸다.</summary>
        private static readonly Regex GuideDevDocsPattern =
            new Regex(@"\s*<h2>개발 문서</h2>\s*<ul>.*?</ul>", RegexOptions.Singleline);

        /// <summary>배포본에서 깨지는 링크(문서 안 앵커도, 외부 URL도 아닌 로컬 파일 참조)를 찾는 패턴.</summary>
        private static readonly Regex GuideLocalLinkPattern =
            new Regex("href=\"(?<link>(?![#\"]|https?:|mailto:|data:)[^\"]+)\"", RegexOptions.IgnoreCase);

        [MenuItem("TaskbarHero/Build/배포 패키지 빌드 (게임 + 플레이 가이드)", false, 0)]
        public static void BuildPackage()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;

            // 오래 걸리는 빌드를 돌린 뒤에 재료가 없다는 걸 알면 늦으므로 먼저 확인한다.
            foreach (string scene in GameScenes)
            {
                if (!File.Exists(scene))
                {
                    Debug.LogError($"[GameDistributionBuilder] 씬을 찾지 못했습니다: {scene}");
                    return;
                }
            }
            string guideSource = Path.Combine(projectRoot, GuideSourceRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(guideSource))
            {
                Debug.LogError($"[GameDistributionBuilder] 플레이 가이드를 찾지 못했습니다: {guideSource}");
                return;
            }

            WarnIfTransparencyPrerequisitesBroken();

            string packageDir = Path.Combine(projectRoot, BuildDirName, PackageDirName);
            string gameDir = Path.Combine(packageDir, GameDirName);
            Directory.CreateDirectory(gameDir);
            string gameExeName = SafeFileName(PlayerSettings.productName) + ".exe";
            string gameExePath = Path.Combine(gameDir, gameExeName);

            var options = new BuildPlayerOptions
            {
                scenes = GameScenes,
                locationPathName = gameExePath,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
                extraScriptingDefines = new[] { ServerEnvironment.QaDefineSymbol },
            };

            Debug.Log($"[GameDistributionBuilder] 배포 패키지 빌드 시작 → {packageDir}\n" +
                      $"  게임 본체: {gameExePath} (씬 {GameScenes.Length}개, 시작=TitleScene)\n" +
                      $"  계정 서버: {ServerEnvironment.QaAccountBaseUrl}\n" +
                      $"  게임 서버: {ServerEnvironment.QaGameBaseUrl}");

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"[GameDistributionBuilder] 빌드 실패: {summary.result} (에러 {summary.totalErrors}개)");
                return;
            }

            Debug.Log($"[GameDistributionBuilder] 게임 본체 빌드 성공 · 크기 {summary.totalSize / (1024 * 1024)}MB");

            CopyGuide(guideSource, Path.Combine(packageDir, GuideFileName));
            string launcher = CreateLauncher(packageDir, GameDirName + "\\" + gameExeName);

            Debug.Log($"[GameDistributionBuilder] 배포 패키지 완성 → {packageDir}\n" +
                      $"  · {Path.GetFileName(launcher)} (실행 버튼)\n" +
                      $"  · {GuideFileName} (플레이 가이드)\n" +
                      $"  · {GameDirName}/ (게임 본체)\n" +
                      "이 폴더를 통째로 압축해 전달하면 받는 사람은 실행 버튼만 누르면 됩니다.");
            EditorUtility.RevealInFinder(launcher);
        }

        /// <summary>파일명에 쓸 수 없는 문자를 '_'로 치환한다(제품 이름을 exe 이름으로 쓰기 위함).</summary>
        private static string SafeFileName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "TaskbarHero";
            }
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(invalid, '_');
            }
            return name;
        }

        /// <summary>
        /// 플레이 가이드를 <b>단일 파일</b>로 만들어 배포 루트에 쓴다.
        /// 스크린샷 PNG는 data URI로 본문에 내장하고(이미지 폴더를 함께 옮기지 않아도 되게),
        /// 저장소 안 .md를 가리키는 「개발 문서」 목록은 배포본에서 전부 깨지므로 떼어 낸다.
        /// </summary>
        private static void CopyGuide(string sourcePath, string destPath)
        {
            string html = File.ReadAllText(sourcePath, Encoding.UTF8);
            string sourceDir = Path.GetDirectoryName(sourcePath)!;
            var missing = new List<string>();
            int embedded = 0;

            html = GuideImagePattern.Replace(html, match =>
            {
                string relative = match.Groups["path"].Value;
                string imagePath = Path.GetFullPath(Path.Combine(sourceDir, relative.Replace('/', Path.DirectorySeparatorChar)));
                if (!File.Exists(imagePath))
                {
                    missing.Add(relative);
                    return match.Value;
                }
                embedded++;
                return "src=\"data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(imagePath)) + "\"";
            });

            // 「개발 문서」 목록이 있으면 떼어 낸다(없으면 그대로 — 원본에 없는 게 정상인 시기도 있다).
            html = GuideDevDocsPattern.Replace(html, string.Empty);

            // 남은 로컬 링크는 배포본에서 전부 깨진다. 지우지는 않고(문맥을 모르므로) 사실만 알린다.
            string[] brokenLinks = GuideLocalLinkPattern.Matches(html)
                .Cast<Match>()
                .Select(m => m.Groups["link"].Value)
                .Distinct()
                .ToArray();
            if (brokenLinks.Length > 0)
            {
                Debug.LogWarning($"[GameDistributionBuilder] 가이드에 저장소 안 파일을 가리키는 링크 {brokenLinks.Length}개가 남아 " +
                                 $"배포본에서 깨집니다: {string.Join(", ", brokenLinks)}");
            }
            if (missing.Count > 0)
            {
                Debug.LogWarning($"[GameDistributionBuilder] 가이드 이미지 {missing.Count}장을 찾지 못해 내장하지 못했습니다 " +
                                 $"(배포본에서 깨져 보입니다): {string.Join(", ", missing)}");
            }

            File.WriteAllText(destPath, html, new UTF8Encoding(false));
            Debug.Log($"[GameDistributionBuilder] 플레이 가이드 생성: {destPath} " +
                      $"(스크린샷 {embedded}장 내장 · {new FileInfo(destPath).Length / 1024}KB)");
        }

        /// <summary>
        /// 배포 루트에 <b>실행 버튼</b>(런처)을 만든다. 자기 위치를 기준으로 <c>Game\게임.exe</c>를 찾아 실행하므로
        /// 폴더를 어디로 옮기든·압축을 어디에 풀든 동작한다(절대 경로를 굽는 바로가기 .lnk와 다른 점).
        /// C# 컴파일러(윈도우 기본 탑재 .NET Framework csc)를 찾지 못하면 같은 일을 하는 .cmd로 대체한다.
        /// 만들어진 파일의 전체 경로를 돌려준다.
        /// </summary>
        private static string CreateLauncher(string packageDir, string gameRelativePath)
        {
            string exePath = Path.Combine(packageDir, LauncherFileName);
            string cmdPath = Path.Combine(packageDir, LauncherFallbackFileName);
            string compilerPath = FindCSharpCompiler();

            if (compilerPath != null && CompileLauncher(compilerPath, exePath, gameRelativePath))
            {
                if (File.Exists(cmdPath))
                {
                    File.Delete(cmdPath);   // 예전 빌드가 남긴 대체 배치 파일을 치운다
                }
                Debug.Log($"[GameDistributionBuilder] 실행 버튼 생성: {exePath}");
                return exePath;
            }

            File.WriteAllText(cmdPath,
                "@echo off\r\n" +
                "start \"\" \"%~dp0" + gameRelativePath + "\"\r\n",
                new UTF8Encoding(false));
            Debug.LogWarning("[GameDistributionBuilder] C# 컴파일러를 찾지 못했거나 컴파일에 실패해 " +
                             $"런처 exe 대신 배치 파일을 만들었습니다: {cmdPath}");
            return cmdPath;
        }

        /// <summary>윈도우에 기본 탑재된 .NET Framework C# 컴파일러 경로를 찾는다(없으면 null).</summary>
        private static string FindCSharpCompiler()
        {
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string[] candidates =
            {
                Path.Combine(windows, @"Microsoft.NET\Framework64\v4.0.30319\csc.exe"),
                Path.Combine(windows, @"Microsoft.NET\Framework\v4.0.30319\csc.exe"),
            };
            return candidates.FirstOrDefault(File.Exists);
        }

        /// <summary>런처 소스를 임시 파일로 써서 <paramref name="compilerPath"/>로 콘솔 창 없는 exe(winexe)로 컴파일한다.</summary>
        private static bool CompileLauncher(string compilerPath, string exePath, string gameRelativePath)
        {
            string sourcePath = Path.GetFullPath(FileUtil.GetUniqueTempPathInProject() + ".cs");
            try
            {
                // 소스에 한글 문자열이 있으므로 BOM을 붙여 쓴다(BOM이 없으면 csc가 ANSI로 읽는다).
                File.WriteAllText(sourcePath, LauncherSource(gameRelativePath), new UTF8Encoding(true));

                var startInfo = new System.Diagnostics.ProcessStartInfo(compilerPath)
                {
                    Arguments = "/nologo /target:winexe /optimize+ /platform:anycpu " +
                                "/r:System.dll /r:System.Windows.Forms.dll " +
                                $"/out:\"{exePath}\" \"{sourcePath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                using (var process = System.Diagnostics.Process.Start(startInfo))
                {
                    string stdout = process!.StandardOutput.ReadToEnd();
                    string stderr = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    if (process.ExitCode == 0 && File.Exists(exePath))
                    {
                        return true;
                    }
                    Debug.LogWarning($"[GameDistributionBuilder] 런처 컴파일 실패(종료 코드 {process.ExitCode}):\n{stdout}\n{stderr}");
                    return false;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GameDistributionBuilder] 런처 컴파일 중 예외: {e.Message}");
                return false;
            }
            finally
            {
                if (File.Exists(sourcePath))
                {
                    File.Delete(sourcePath);
                }
            }
        }

        /// <summary>런처 exe의 C# 소스. 게임 exe의 상대 경로만 끼워 넣는다.</summary>
        private static string LauncherSource(string gameRelativePath)
        {
            const string template = @"
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

// Taskbar Hero 실행 버튼. 자기 자신이 놓인 폴더를 기준으로 게임 본체를 찾아 실행한다.
// (에디터 메뉴 TaskbarHero/Build/배포 패키지 빌드가 빌드 때마다 생성하는 파일 — 직접 편집하지 말 것)
internal static class TaskbarHeroLauncher
{
    private const string GameRelativePath = @""__GAME_RELATIVE_PATH__"";
    private const string Caption = ""Taskbar Hero"";

    [STAThread]
    private static int Main()
    {
        string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        string exePath = Path.Combine(baseDir, GameRelativePath);

        if (!File.Exists(exePath))
        {
            MessageBox.Show(
                ""게임 파일을 찾지 못했습니다."" + Environment.NewLine + Environment.NewLine +
                exePath + Environment.NewLine + Environment.NewLine +
                ""압축을 풀 때 폴더 구조가 바뀌지 않았는지 확인해 주세요. "" +
                ""'Com2us Taskbar Hero 실행'과 'Game' 폴더가 같은 자리에 있어야 합니다."",
                Caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }

        try
        {
            Process.Start(new ProcessStartInfo(exePath)
            {
                WorkingDirectory = Path.GetDirectoryName(exePath),
                UseShellExecute = true,
            });
            return 0;
        }
        catch (Exception e)
        {
            MessageBox.Show(
                ""게임을 실행하지 못했습니다."" + Environment.NewLine + Environment.NewLine + e.Message,
                Caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
";
            return template.Replace("__GAME_RELATIVE_PATH__", gameRelativePath);
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
                Debug.LogWarning("[GameDistributionBuilder] Windows 그래픽 API가 D3D11 고정이 아닙니다 " +
                                 $"(현재: {string.Join(", ", apis.Select(a => a.ToString()))}). " +
                                 "D3D12는 flip 모델이 강제되어 배경이 검게 나옵니다 — Player Settings에서 D3D11로 고정하세요.");
            }
            if (PlayerSettings.useFlipModelSwapchain)
            {
                Debug.LogWarning("[GameDistributionBuilder] useFlipModelSwapchain이 켜져 있습니다. " +
                                 "flip 모델 스왑체인은 레이어드 창 투명과 비호환이라 배경이 검게 나옵니다 — 꺼 주세요.");
            }
        }
    }
}
