using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SPUM_AssetHelper : AssetPostprocessor
{
    public const string NoticeFilePath = "Assets/SPUM/README.txt";
    private const string LastSeenNoticeFileTimeKey = "SPUM.Notice.LastSeenFileTimeUtc";
    
    public static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        foreach (string asset in importedAssets)
        {
            if (asset.Replace("\\", "/") == NoticeFilePath)
            {
                if (!TryGetNoticeFileTime(out long noticeFileTimeUtc))
                {
                    continue;
                }

                if (noticeFileTimeUtc == GetLastSeenNoticeFileTime())
                {
                    continue;
                }

                SPUM_NoticeWindow.ShowWindow(noticeFileTimeUtc);
            }
        }
    }

    public static bool TryGetNoticeFileTime(out long noticeFileTimeUtc)
    {
        noticeFileTimeUtc = 0;

        if (!File.Exists(NoticeFilePath))
        {
            Debug.LogWarning($"SPUM notice skipped. README not found: {NoticeFilePath}");
            return false;
        }

        noticeFileTimeUtc = File.GetLastWriteTimeUtc(NoticeFilePath).Ticks;
        return true;
    }

    public static long GetLastSeenNoticeFileTime()
    {
        if (!long.TryParse(EditorPrefs.GetString(LastSeenNoticeFileTimeKey, "0"), out long fileTimeUtc))
        {
            return 0;
        }

        return fileTimeUtc;
    }

    public static void SetLastSeenNoticeFileTime(long noticeFileTimeUtc)
    {
        if (noticeFileTimeUtc <= 0)
        {
            return;
        }

        EditorPrefs.SetString(LastSeenNoticeFileTimeKey, noticeFileTimeUtc.ToString());
    }

    static void ShowHelpWindow(string assetName)
    {
        HelpWindow.ShowWindow(assetName);
    }

}

public class SPUM_NoticeWindow : EditorWindow
{
    private const string WindowTitle = "SPUM Notice";
    private string noticeText;
    private Vector2 scrollPosition;
    private bool doNotShowAgain;
    private long noticeFileTimeUtc;

    public static void ShowWindow(long currentNoticeFileTimeUtc)
    {
        SPUM_NoticeWindow window = GetWindow<SPUM_NoticeWindow>(true, WindowTitle);
        window.minSize = new Vector2(520, 260);
        window.maxSize = new Vector2(520, 260);
        window.noticeFileTimeUtc = currentNoticeFileTimeUtc;
        window.noticeText = LoadNoticeText();
        window.doNotShowAgain = false;
        window.Show();
    }

    private static string LoadNoticeText()
    {
        if (!File.Exists(SPUM_AssetHelper.NoticeFilePath))
        {
            return string.Empty;
        }

        return File.ReadAllText(SPUM_AssetHelper.NoticeFilePath);
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Latest notice", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        using (var scroll = new EditorGUILayout.ScrollViewScope(scrollPosition))
        {
            scrollPosition = scroll.scrollPosition;
            EditorGUILayout.TextArea(noticeText, GUILayout.ExpandHeight(true));
        }

        doNotShowAgain = EditorGUILayout.ToggleLeft("Do not show again for this version", doNotShowAgain);
        EditorGUILayout.Space(6);
        if (GUILayout.Button("Close", GUILayout.Height(28)))
        {
            if (doNotShowAgain)
            {
                SPUM_AssetHelper.SetLastSeenNoticeFileTime(noticeFileTimeUtc);
            }
            Close();
        }
    }
}

public class HelpWindow : EditorWindow
{
    public string scenePath = "Assets/SPUM/Scenes/SPUM_Scene.unity";
    private string packagePath => GetPackageFilePath();
    private string rootPath = "Assets/SPUM";
    private List<(string text, string url, int startIndex, int endIndex)> links = new List<(string, string, int, int)>();
    // 제외할 파일 및 디렉토리 목록
    private List<string> excludedFiles = new List<string>
    {
        "SPUM_PackageImporter.cs",  // 삭제하지 않을 파일
    };
 
    private List<string> excludedDirectories = new List<string>
    {
        //"Assets/SPUM/Core/Basic_Resources/Package", // 1.7.7 버전 
        "Assets/SPUM/Package/",  // 1.8.0 이후 유지
        "Assets/SPUM/Preset/",
        "Assets/SPUM/Resources/",
        "Assets/SPUM/Backup/"
    };
    private string assetName;
    private Texture2D helpImage;
    private string readmeContent;
    private Vector2 scrollPosition;
    private Rect contentRect;
    public static void ShowWindow(string assetName)
    {
        
        HelpWindow window = GetWindow<HelpWindow>("SPUM PIXEL UNIT MAKER");
        
        window.assetName = assetName;
        window.helpImage = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/SPUM/Package/Image/HelpImage.png");
        if (window.helpImage != null)
        {
            window.minSize = new Vector2(window.helpImage.width, 600); // 이미지 너비에 맞게 고정
            window.maxSize = new Vector2(window.helpImage.width, 600);
        }
        else
        {
            window.minSize = new Vector2(400, 300); // 기본 크기
            window.maxSize = window.minSize;
        }
        string path = assetName; // README.md 파일 경로 지정
        if (File.Exists(path))
        {
            window.readmeContent = File.ReadAllText(path);
            // window.readmeContent = window.ParseMarkdown(window.readmeContent);
        }
        else
        {
            window.readmeContent = "README file not found.";
        }
        window.RenameFolderUsingFileUtil("Assets/SPUM/Resources/Addons/Legacy/1_Horse/0_Spite", "0_Sprite");
        window.Show();
        
    }
    string ParseMarkdown(string markdown)
    {
        links.Clear();
        // 헤더 파싱
        markdown = Regex.Replace(markdown, @"^# (.*?)$", "<size=24><b>$1</b></size>", RegexOptions.Multiline);
        markdown = Regex.Replace(markdown, @"^## (.*?)$", "<size=20><b>$1</b></size>", RegexOptions.Multiline);
        markdown = Regex.Replace(markdown, @"^### (.*?)$", "<size=18><b>$1</b></size>", RegexOptions.Multiline);

        // 볼드체와 이탤릭체
        markdown = Regex.Replace(markdown, @"\*\*(.*?)\*\*", "<b>$1</b>");
        markdown = Regex.Replace(markdown, @"\*(.*?)\*", "<i>$1</i>");

        // 목록
        markdown = Regex.Replace(markdown, @"^- (.*?)$", "• $1", RegexOptions.Multiline);

        // 링크
        markdown = Regex.Replace(markdown, @"\[(.*?)\]\((.*?)\)", m =>
        {
            string linkText = m.Groups[1].Value;
            string url = m.Groups[2].Value;
            int startIndex = m.Index;
            int endIndex = startIndex + m.Length;
            links.Add((linkText, url, startIndex, endIndex));
            return $"<color=blue>{linkText}</color>";
        });

        // 코드 블록
        markdown = Regex.Replace(markdown, @"```(.*?)```", "<color=grey>$1</color>", RegexOptions.Singleline);

        return markdown;
    }
    private void OnGUI()
    {
        // GUILayout.Label("[S]oonSoon [P]ixel [U]nit [M]aker" + assetName, EditorStyles.boldLabel);
        // GUILayout.Label("Here are some tips on how to use it:", EditorStyles.wordWrappedLabel);
        GUIStyle style = new GUIStyle(EditorStyles.label);
        style.fontSize = 14;
        style.richText = true;
        // style.wordWrap = true;
        if (helpImage != null)
        {
            GUILayout.Label(helpImage);
        }
        else
        {
            GUILayout.Label("Image not found.");
        }
        scrollPosition = GUILayout.BeginScrollView(scrollPosition);
        string parsedMarkdown = ParseMarkdown(readmeContent);

        GUIContent content = new GUIContent(parsedMarkdown);
        contentRect = GUILayoutUtility.GetRect(content, style, GUILayout.ExpandWidth(true));
        EditorGUI.LabelField(contentRect, content, style);

        
        Vector2 mousePos = Event.current.mousePosition;
        mousePos.y += scrollPosition.y;

        Event currentEvent = Event.current;
        if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0)
        {
            foreach (var link in links)
            {
                if (IsPositionInLink(mousePos, link, contentRect))
                {
                    Application.OpenURL(link.url);
                    currentEvent.Use();
                    break;
                }
            }
        }
                
        
        //  var linkTexture = new Texture2D(1, 1);
        //  linkTexture.SetPixel(0, 0, Color.blue);
        //  linkTexture.Apply();

        foreach (var link in links)
        {

            if (IsPositionInLink(mousePos, link, contentRect))
            {
                EditorGUIUtility.AddCursorRect(contentRect, MouseCursor.Link);
            }else{
                EditorGUIUtility.AddCursorRect(contentRect, MouseCursor.Arrow);
            }
           
            //GUI.DrawTexture(linkRect, linkTexture);
            
        }
        GUILayout.EndScrollView();
        if (GUILayout.Button("OK"))
        {
            this.Close();
        }
   
        EditorGUILayout.TextField("Package Path", packagePath);
        rootPath = EditorGUILayout.TextField("Root Path", rootPath);
        var styles = new GUIStyle(GUI.skin.button);
        styles.normal.textColor = Color.blue;
        styles.fontSize = 20;
        if (GUILayout.Button("Clean Install LocalPath Package ", styles, GUILayout.Height(60)))
        {
            if(packagePath == null || packagePath == "") {
                Debug.Log("No packagePath exists.");
                return;
            }
            if(EditorUtility.DisplayDialog("Warrning",  "All Spum paths except for the one below will be deleted. \n" + 
            excludedDirectories[0] + "\n"
            + excludedDirectories[1] + "\n"
            + excludedDirectories[2] + "\n"
            , "OK","Cancel"))
            {
                if(EditorUtility.DisplayDialog("Warrning",   "Are you sure you want to proceed?", "Yes","No"))
                {
                    MovePackageToMainPath();
                    RenameFolderUsingFileUtil("Assets/SPUM/Resources/Addons/Legacy/1_Horse/0_Spite", "0_Sprite");
                    DeleteAllExceptExcluded();
                    ImportNewPackage();
                    DeleteEmptyCSFiles();
                    LoadSceneByPath();
                }
            }

        }
        // if (GUILayout.Button("package Path", styles, GUILayout.Height(60)))
        // {
        //     GetPackageFilePath();
        // }
        // if (GUILayout.Button("Clean Install AssetStorePath package ", styles, GUILayout.Height(60)))
        // {
        //     if(EditorUtility.DisplayDialog("Warrning",  "All Spum paths except for the one below will be deleted. \n" + 
        //     excludedDirectories[0] + "\n"
        //     + excludedDirectories[1] + "\n"
        //     + excludedDirectories[2] + "\n"
        //     , "OK","Cancel"))
        //     {
        //         if(EditorUtility.DisplayDialog("Warrning",   "Are you sure you want to proceed?", "Yes","No"))
        //         {
        //             DeleteAllExceptExcluded();
        //             ImportStorePackage();
        //             DeleteEmptyCSFiles();
        //             LoadSceneByPath();
        //         }
        //     }

        // }
    }
    public void LoadSceneByPath()
    {
        string sceneName = System.IO.Path.GetFileNameWithoutExtension(scenePath);
        SceneManager.LoadScene(sceneName);
    }
    private bool IsPositionInLink(Vector2 position, (string text, string url, int startIndex, int endIndex) link, Rect totalRect)
    {
        Rect linkRect = GetLinkRect(link, totalRect);
        return linkRect.Contains(position);
    }

    private Rect GetLinkRect((string text, string url, int startIndex, int endIndex) link, Rect totalRect)
    {
        GUIStyle style = new GUIStyle(EditorStyles.label);
        style.fontSize = 14;
        //style.richText = true;
        Vector2 startPosition = style.GetCursorPixelPosition(totalRect, new GUIContent(readmeContent), link.startIndex);
        Vector2 endPosition = style.GetCursorPixelPosition(totalRect, new GUIContent(readmeContent), link.endIndex);

        return new Rect(totalRect.x + startPosition.x, totalRect.y + startPosition.y - style.lineHeight, endPosition.x - startPosition.x, style.lineHeight);
    }

    private void DeleteAllExceptExcluded()
    {
        if (Directory.Exists(rootPath))
        {
            DeleteFilesAndFoldersRecursively(rootPath);
            AssetDatabase.Refresh();
        }
        else
        {
            Debug.LogError($"Directory not found: {rootPath}");
        }
    }

    private void DeleteFilesAndFoldersRecursively(string path)
    {
        // 삭제하지 않을 디렉토리는 건너뜀
        if (IsExcluded(path))
        {
            Debug.Log($"Skipped directory: {path} (excluded from deletion)");
            return;
        }

        // 디렉토리 내의 모든 파일 삭제
        string[] files = Directory.GetFiles(path);
        foreach (string file in files)
        {
            if (!IsExcluded(file))
            {
                FileUtil.DeleteFileOrDirectory(file);
                Debug.Log($"Deleted file: {file}");
            }
            else
            {
                Debug.Log($"Skipped file: {file} (excluded from deletion)");
            }
        }

        // 디렉토리 내의 모든 하위 디렉토리 삭제
        string[] directories = Directory.GetDirectories(path);
        foreach (string dir in directories)
        {
            DeleteFilesAndFoldersRecursively(dir);

            // 만약 하위 디렉토리 삭제 후 해당 디렉토리가 비어 있으면 삭제
            if (Directory.Exists(dir) && Directory.GetFiles(dir).Length == 0 && Directory.GetDirectories(dir).Length == 0)
            {
                Directory.Delete(dir);
                Debug.Log($"Deleted directory: {dir}");
            }
        }
    }

    private bool IsExcluded(string path)
    {
        // 경로를 절대 경로로 변환하여 비교
        string fullPath = Path.GetFullPath(path).Replace("\\", "/");
        
        // 제외할 파일 또는 디렉토리인 경우 true 반환
        bool isExcludedFile = excludedFiles.Exists(file => Path.GetFullPath(file).Replace("\\", "/") == fullPath);
        bool isExcludedDirectory = excludedDirectories.Exists(dir => fullPath.StartsWith(Path.GetFullPath(dir).Replace("\\", "/")));

        return isExcludedFile || isExcludedDirectory;
    }
    string GetPackageFilePath()
    {
        // 검색할 경로들 정의
        string[] searchPaths = new string[]
        {
            "Assets/SPUM/Package/",
            "Assets/SPUM/Basic_Resources/Package"  // 두 번째 경로 (원하는 경로로 변경)
        };

        string highestVersionFile = null;
        int highestVersion = -1;
        Regex versionPattern = new Regex(@"\d{1,3}");

        // 각 경로에서 파일 검색
        foreach (string path in searchPaths)
        {
            if (!Directory.Exists(path))
                continue;

            string[] files = Directory.GetFiles(path, "SPUM*.unitypackage", SearchOption.AllDirectories);

            foreach (string file in files)
            {
                string fileNameWithoutSpacesAndSpecialChars = Regex.Replace(Path.GetFileName(file), @"[\s\W_]+", "");
                Match match = versionPattern.Match(fileNameWithoutSpacesAndSpecialChars);
                if (match.Success)
                {
                    int version = int.Parse(match.Value);
                    if (version > highestVersion)
                    {
                        highestVersion = version;
                        highestVersionFile = file;
                    }
                }
            }
        }

        if (highestVersionFile != null)
        {
            Debug.Log("Package Path: " + highestVersionFile);
        }
        else
        {
            Debug.Log("Package Not Found in any search paths");
        }
        return highestVersionFile;
    }
    
    public void MovePackageToMainPath()
    {
        string mainPath = "Assets/SPUM/Package/";
        string packageFilePath = GetPackageFilePath();
        
        if (string.IsNullOrEmpty(packageFilePath))
        {
            Debug.LogError("No package file found to move.");
            return;
        }
        
        // 이미 메인 경로에 있는 경우
        if (packageFilePath.StartsWith(mainPath))
        {
            Debug.Log("Package file is already in the main path.");
            return;
        }
        
        // 메인 경로가 없으면 생성
        if (!Directory.Exists(mainPath))
        {
            Directory.CreateDirectory(mainPath);
            Debug.Log($"Created directory: {mainPath}");
        }
        
        string fileName = Path.GetFileName(packageFilePath);
        string destinationPath = Path.Combine(mainPath, fileName);
        
        try
        {
            // 대상 파일이 이미 존재하는 경우 처리
            if (File.Exists(destinationPath))
            {
                if (EditorUtility.DisplayDialog("File Exists", 
                    $"File {fileName} already exists in {mainPath}. Overwrite?", 
                    "Yes", "No"))
                {
                    FileUtil.DeleteFileOrDirectory(destinationPath);
                }
                else
                {
                    Debug.Log("Move operation cancelled by user.");
                    return;
                }
            }
            
            // 파일 이동
            FileUtil.MoveFileOrDirectory(packageFilePath, destinationPath);
            
            // .meta 파일도 함께 이동
            string metaSource = packageFilePath + ".meta";
            string metaDest = destinationPath + ".meta";
            if (File.Exists(metaSource))
            {
                FileUtil.MoveFileOrDirectory(metaSource, metaDest);
            }
            
            AssetDatabase.Refresh();
            Debug.Log($"Successfully moved package to: {destinationPath}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to move package file: {e.Message}");
        }
    }
    public void RenameFolderUsingFileUtil(string oldPath, string newName)
    {
        if (!Directory.Exists(oldPath))
        {
            Debug.Log($"Directory not found: {oldPath}");
            return;
        }

        string parentPath = Path.GetDirectoryName(oldPath);
        string newPath = Path.Combine(parentPath, newName);

        try
        {
            // FileUtil을 사용한 이동
            FileUtil.MoveFileOrDirectory(oldPath, newPath);

            // .meta 파일도 함께 처리
            string oldMetaPath = oldPath + ".meta";
            string newMetaPath = newPath + ".meta";

            if (File.Exists(oldMetaPath))
            {
                FileUtil.MoveFileOrDirectory(oldMetaPath, newMetaPath);
            }

            AssetDatabase.Refresh();
            Debug.Log($"Successfully renamed folder from {oldPath} to {newPath}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to rename folder: {e.Message}");
        }
    }
    [MenuItem("SPUM/Clean Install")]
    public static void ShowPackage()
    {
        ShowWindow(SPUM_AssetHelper.NoticeFilePath);
    }
    private void ImportNewPackage()
    {
        string directoryPath = @"Assets/SPUM/Package/";
        string pattern = @"SPUM(\d+)\.unitypackage$";
        var files = Directory.GetFiles(directoryPath);

        int maxNumber = files
            .Select(file => Regex.Match(file, pattern))
            .Where(match => match.Success)
            .Select(match => int.Parse(match.Groups[1].Value))
            .DefaultIfEmpty(0)
            .Max();

        string highestNumberFile = files
            .FirstOrDefault(file => Regex.IsMatch(Path.GetFileName(file), $"SPUM{maxNumber}\\.unitypackage$"));
        if(string.IsNullOrEmpty(highestNumberFile)){
            Debug.Log("No path exists.");
            return;
        }
        AssetDatabase.ImportPackage(highestNumberFile, false);
        Debug.Log($"Imported: {highestNumberFile}");
    }
    private void ImportStorePackage(){
        string assetStorePath = ""; // = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), @"Unity\Asset Store-5.x\soonsoon\Textures Materials2D Characters\2D Pixel Unit Maker - SPUM.unitypackage");
        //string assetStorePath;

        
        if (Application.platform == RuntimePlatform.WindowsEditor)
        {
            // Windows 
            assetStorePath = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), 
                                         @"Unity\Asset Store-5.x\soonsoon\Textures Materials2D Characters\2D Pixel Unit Maker - SPUM.unitypackage");
        }
        else if (Application.platform == RuntimePlatform.OSXEditor)
        {
            // macOS 
            assetStorePath = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Personal), 
                                          @"Library\Asset Store-5.x\soonsoon\Textures Materials2D Characters\2D Pixel Unit Maker - SPUM.unitypackage");
        }
        if(string.IsNullOrEmpty(assetStorePath)){
            Debug.Log("No path exists.");
            return;
        }
        AssetDatabase.ImportPackage(assetStorePath, false);
    }
    private void DeleteEmptyCSFiles()
    {
        if (Directory.Exists(rootPath))
        {
            DeleteEmptyCSFilesRecursively(rootPath);
            AssetDatabase.Refresh();
        }
        else
        {
            Debug.LogError($"Directory not found: {rootPath}");
        }
    }

    private void DeleteEmptyCSFilesRecursively(string currentPath)
    {
        foreach (var directory in Directory.GetDirectories(currentPath))
        {
            DeleteEmptyCSFilesRecursively(directory);
        }

        foreach (var file in Directory.GetFiles(currentPath, "*.cs"))
        {
            if (new FileInfo(file).Length == 0)
            {
                File.Delete(file);
                Debug.Log($"Deleted empty file: {file}");
            }
        }
    }
}
