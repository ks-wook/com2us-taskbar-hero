using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace TaskbarHero.EditorTools
{
    /// <summary>
    /// 마스터 데이터 최신화 툴.
    /// 서버 리포의 Python 추출기(tools/master_data_export.py)를 실행해
    /// master-data-schema.sql 을 MySQL 에 재적용한 뒤 각 마스터 테이블을
    /// Assets/Resources/MasterData/*.json (기획서 §7 camelCase 배열)로 내보낸다.
    ///
    /// 정본: docs/세부/master-data/master-data-{값.md,기획서.md,schema.sql}
    /// 요구: 로컬 MySQL(docker: taskbar-hero-master) 실행 + Python3(+pymysql).
    /// </summary>
    public static class MasterDataExportTool
    {
        private const string MenuRoot = "TaskbarHero/마스터 데이터";
        private const string PythonPathPrefKey = "TaskbarHero.MasterData.PythonPath";

        // 리포 루트 기준 상대 경로
        private const string RelScript = "tools/master_data_export.py";
        private const string RelSchema = "docs/세부/master-data/master-data-schema.sql";
        private const string RelOutputFromAssets = "Resources/MasterData"; // Application.dataPath 하위

        [MenuItem(MenuRoot + "/최신화 (DB→JSON)", priority = 0)]
        public static void RefreshMasterData()
        {
            string repoRoot = GetRepoRoot();
            string scriptPath = Path.Combine(repoRoot, RelScript);
            string schemaPath = Path.Combine(repoRoot, RelSchema);
            string outputDir = Path.Combine(Application.dataPath, RelOutputFromAssets);

            if (!File.Exists(scriptPath))
            {
                EditorUtility.DisplayDialog("마스터 데이터 최신화",
                    "추출 스크립트를 찾을 수 없습니다:\n" + scriptPath +
                    "\n\n리포 구조(클라이언트/서버 형제 폴더)가 맞는지 확인하세요.", "확인");
                return;
            }

            string python = ResolvePython();
            if (string.IsNullOrEmpty(python))
            {
                if (EditorUtility.DisplayDialog("마스터 데이터 최신화",
                        "실행 가능한 Python 을 찾지 못했습니다.\n" +
                        "Python 3(+pymysql) 설치 후, python.exe 경로를 지정해 주세요.",
                        "경로 지정", "취소"))
                {
                    SetPythonPath();
                }
                return;
            }

            if (!EditorUtility.DisplayDialog("마스터 데이터 최신화",
                    "schema.sql 을 MySQL(master DB)에 재적용하고\n" +
                    "Assets/Resources/MasterData/*.json 을 재생성합니다.\n\n" +
                    "· Python: " + python + "\n" +
                    "· 로컬 MySQL(taskbar-hero-master)이 실행 중이어야 합니다.\n\n계속할까요?",
                    "실행", "취소"))
            {
                return;
            }

            RunExporter(python, scriptPath, schemaPath, outputDir, repoRoot);
        }

        [MenuItem(MenuRoot + "/Python 경로 지정...", priority = 20)]
        public static void SetPythonPath()
        {
            string start = EditorPrefs.GetString(PythonPathPrefKey, "");
            string dir = string.IsNullOrEmpty(start) ? "" : Path.GetDirectoryName(start);
            string picked = EditorUtility.OpenFilePanel("python.exe 선택", dir, "exe");
            if (!string.IsNullOrEmpty(picked))
            {
                EditorPrefs.SetString(PythonPathPrefKey, picked);
                Debug.Log("[MasterData] Python 경로 저장: " + picked);
            }
        }

        [MenuItem(MenuRoot + "/출력 폴더 열기", priority = 21)]
        public static void OpenOutputFolder()
        {
            string outputDir = Path.Combine(Application.dataPath, RelOutputFromAssets);
            Directory.CreateDirectory(outputDir);
            EditorUtility.RevealInFinder(outputDir);
        }

        private static void RunExporter(string python, string scriptPath, string schemaPath,
                                        string outputDir, string workingDir)
        {
            var psi = new ProcessStartInfo
            {
                FileName = python,
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            psi.ArgumentList.Add(scriptPath);
            psi.ArgumentList.Add("--schema");
            psi.ArgumentList.Add(schemaPath);
            psi.ArgumentList.Add("--output");
            psi.ArgumentList.Add(outputDir);
            // 자식 Python 이 한글 stdout 을 UTF-8 로 내보내도록 강제
            psi.EnvironmentVariables["PYTHONUTF8"] = "1";
            psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";

            string stdout, stderr;
            int exitCode;
            try
            {
                EditorUtility.DisplayProgressBar("마스터 데이터 최신화", "추출기 실행 중...", 0.5f);
                using (var proc = Process.Start(psi))
                {
                    stdout = proc.StandardOutput.ReadToEnd();
                    stderr = proc.StandardError.ReadToEnd();
                    proc.WaitForExit();
                    exitCode = proc.ExitCode;
                }
            }
            catch (Exception ex)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("마스터 데이터 최신화", "Python 실행 실패:\n" + ex.Message, "확인");
                Debug.LogError("[MasterData] 실행 실패: " + ex);
                return;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (exitCode == 0)
            {
                AssetDatabase.Refresh();
                Debug.Log("[MasterData] 최신화 완료\n" + stdout);
                EditorUtility.DisplayDialog("마스터 데이터 최신화",
                    "완료되었습니다.\n\n" + stdout.Trim(), "확인");
            }
            else
            {
                string msg = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                Debug.LogError("[MasterData] 실패(exit " + exitCode + ")\nSTDOUT:\n" + stdout + "\nSTDERR:\n" + stderr);
                EditorUtility.DisplayDialog("마스터 데이터 최신화",
                    "실패했습니다 (exit " + exitCode + ").\n\n" + msg.Trim() +
                    "\n\nMySQL(taskbar-hero-master) 실행 여부와 pymysql 설치를 확인하세요.", "확인");
            }
        }

        /// <summary>Application.dataPath(=.../com2us-taskbar-hero-client/Assets) 기준 리포 루트(클라 폴더의 부모).</summary>
        private static string GetRepoRoot()
        {
            // dataPath: <repo>/com2us-taskbar-hero-client/Assets  ->  parent x2 = <repo>
            var clientDir = Directory.GetParent(Application.dataPath);          // com2us-taskbar-hero-client
            var repoDir = clientDir != null ? clientDir.Parent : null;         // <repo>
            return repoDir != null ? repoDir.FullName : Application.dataPath;
        }

        /// <summary>실행 가능한 python.exe 경로를 찾는다(스토어 스텁 회피: 실제 설치 경로만 사용).</summary>
        private static string ResolvePython()
        {
            // 1) 사용자가 지정한 경로
            string pref = EditorPrefs.GetString(PythonPathPrefKey, "");
            if (!string.IsNullOrEmpty(pref) && File.Exists(pref)) return pref;

            // 2) winget/일반 설치 위치: %LOCALAPPDATA%\Programs\Python\Python3xx\python.exe
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string found = FindPythonUnder(Path.Combine(localAppData, "Programs", "Python"));
            if (found != null) return found;

            // 3) %ProgramFiles%\Python3xx\python.exe
            found = FindPythonUnder(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            if (found != null) return found;

            return null;
        }

        private static string FindPythonUnder(string root)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return null;
            string best = null;
            foreach (var dir in Directory.GetDirectories(root, "Python*"))
            {
                string exe = Path.Combine(dir, "python.exe");
                if (File.Exists(exe))
                {
                    // 이름 오름차순 마지막(대체로 최신 버전)을 선택
                    if (best == null || string.CompareOrdinal(dir, Path.GetDirectoryName(best)) > 0)
                        best = exe;
                }
            }
            return best;
        }
    }
}
