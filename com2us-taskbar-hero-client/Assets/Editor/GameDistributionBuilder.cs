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
    /// 게임 본체를 한 단계 아래 <c>Game/</c>에 넣고 그 <b>한 단계 위</b>에 플레이 가이드를 놓는다.
    /// <b>실행 파일은 따로 만들지 않는다</b> — 받는 사람은 <c>Game</c> 폴더의 게임 exe를 직접 실행하며,
    /// 그 방법은 가이드 1장('게임 실행 방법')에 적혀 있다.
    /// </para>
    /// <code>
    /// Builds/TaskbarHero/               ← 이 폴더를 통째로 압축해 전달한다
    ///   게임-플레이-가이드.html         ← docs/게임-플레이-가이드.html (스크린샷을 내장한 단일 파일)
    ///   Game/                           ← 유니티 빌드 결과물(exe + *_Data + 런타임 DLL)
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

            string guidePath = Path.Combine(packageDir, GuideFileName);
            CopyGuide(guideSource, guidePath);
            RemoveObsoleteLaunchers(packageDir);

            Debug.Log($"[GameDistributionBuilder] 배포 패키지 완성 → {packageDir}\n" +
                      $"  · {GuideFileName} (플레이 가이드)\n" +
                      $"  · {GameDirName}/{gameExeName} (게임 실행 파일)\n" +
                      $"이 폴더를 통째로 압축해 전달하면, 받는 사람은 {GameDirName} 폴더의 {gameExeName}을 누르면 됩니다" +
                      "(가이드 1장에 같은 안내가 있습니다).");
            EditorUtility.RevealInFinder(guidePath);
        }

        /// <summary>
        /// 예전 빌드가 배포 루트에 만들어 둔 <b>런처 파일을 지운다</b>. 지금은 런처를 만들지 않고
        /// 받는 사람이 <c>Game</c> 폴더의 게임 exe를 직접 실행하므로, 남아 있으면 무엇을 눌러야 할지 헷갈린다.
        /// 이 빌더가 예전에 만들던 <b>정확한 이름만</b> 지운다(사용자가 넣어 둔 다른 파일은 건드리지 않는다).
        /// </summary>
        private static void RemoveObsoleteLaunchers(string packageDir)
        {
            string[] obsoleteNames =
            {
                "Com2us Taskbar Hero 실행.exe",
                "Com2us Taskbar Hero 실행.cmd",
                "게임 플레이.exe",
                "게임 플레이.cmd",
                "먼저 읽어주세요.txt",
            };

            foreach (string name in obsoleteNames)
            {
                string path = Path.Combine(packageDir, name);
                if (!File.Exists(path))
                {
                    continue;
                }
                try
                {
                    File.Delete(path);
                    Debug.Log($"[GameDistributionBuilder] 예전 빌드가 남긴 파일을 지웠습니다: {name}");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[GameDistributionBuilder] 예전 파일을 지우지 못했습니다({name}): {e.Message}");
                }
            }
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
