using System.IO;
using UnityEngine;

namespace TaskbarHero.ClientEditor
{
    /// <summary>
    /// Windows 빌드 공용 헬퍼(실행·파일명 안전화). 다른 빌더(FullGameBuilder·TransparentOverlayBuilder)가 사용한다.
    /// (구 "Windows 빌드 & 실행 (현재 비율 확대)" 메뉴는 제거됨 — 전체 게임 빌드는
    /// <c>TaskbarHero/Build/전체 게임 빌드 & 실행 (Title→Game)</c>을 사용한다.)
    /// </summary>
    public static class WindowsBuilder
    {
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
