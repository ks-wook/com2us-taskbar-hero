using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System;
using System.Text.RegularExpressions;

public class SPUM_AssetExporterWithPreview : EditorWindow
{
    [System.Serializable]
    public class FolderInfo
    {
        public string Name;
        public string Version;
        public string CreationDate;
    }

    private Dictionary<string, FolderInfo> folderInfos = new Dictionary<string, FolderInfo>();
    private Vector2 scrollPosition;
    private Vector2 defaultAssetsScrollPosition;
    private List<string> assetsToExport = new List<string>();
    private List<string> DefaultExport = new List<string>();
    private bool includeDirectories = true;
    private string rootPath = "Assets/SPUM/Resources/Addons";
    private Dictionary<string, bool> folderStates = new Dictionary<string, bool>();
    private Dictionary<string, bool> DefualtAssetStates = new Dictionary<string, bool>();
    private Dictionary<string, bool> manualAssetStates = new Dictionary<string, bool>();
    private Vector2 manualAssetsScrollPosition;
    [MenuItem("SPUM/Export Assets with Dependencies")]
    static void Init()
    {
        SPUM_AssetExporterWithPreview window = (SPUM_AssetExporterWithPreview)EditorWindow.GetWindow(typeof(SPUM_AssetExporterWithPreview));
        window.Show();
    }
    void OnEnable()
    {
        RefreshFolderStates();
        
        SetDefaultAsset();
    }
    void OnGUI()
    {
        GUILayout.Label("Assets to Export", EditorStyles.boldLabel,GUILayout.ExpandWidth(true));

        if (GUILayout.Button("Select be Exported Addon Folder"))
        {
            RefreshSelectedAssets();
        }

        //includeDirectories = EditorGUILayout.Toggle("Include Directories", includeDirectories);

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        foreach (var folder in folderStates.Keys.ToList())
        {
            EditorGUILayout.BeginHorizontal();

           

            if (folderInfos.TryGetValue(folder, out FolderInfo info))
            {
                EditorGUILayout.LabelField($"{folder} (Ver: {info.Version}, Created: {info.CreationDate})", EditorStyles.wordWrappedLabel);
            }
            else
            {
                EditorGUILayout.LabelField(folder, EditorStyles.wordWrappedLabel);
            }
            bool isChecked = EditorGUILayout.Toggle(folderStates[folder], GUILayout.Width(20));
            EditorGUILayout.EndHorizontal();

            if (isChecked != folderStates[folder])
            {
                folderStates[folder] = isChecked;
                UpdateAssetsToExport();
            }
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space();

    GUILayout.Label("Required Asset", EditorStyles.boldLabel);
    if (GUILayout.Button("Default asset files selected for the package"))
    {
        SetDefaultAsset();
    }
    defaultAssetsScrollPosition = EditorGUILayout.BeginScrollView(defaultAssetsScrollPosition);
    foreach (var folder in DefualtAssetStates.Keys.ToList())
    {
        EditorGUILayout.BeginHorizontal();

        EditorGUILayout.LabelField(folder, EditorStyles.wordWrappedLabel);
        bool isChecked = EditorGUILayout.Toggle(DefualtAssetStates[folder], GUILayout.Width(20));

        EditorGUILayout.EndHorizontal();

        if (isChecked != DefualtAssetStates[folder])
        {
            DefualtAssetStates[folder] = isChecked;
            UpdateAssetsToExport();
        }
    }

    EditorGUILayout.EndScrollView();

        if (GUILayout.Button("Export Package"))
        {
            ExportPackage();
        }
        if (GUILayout.Button("Export Clean Install Package"))
        {
            ExportCleanInstallPackage();
            AssetDatabase.Refresh();
            float version = GameObject.Find("SPUM_Manager").GetComponent<SPUM_Manager>()._version;
            
            
            ExportPackage("Assets/SPUM/Package/SPUM"+version+".unitypackage");
            AssetDatabase.Refresh();
            ExportPackage("Assets/SPUM/Package/SPUM"+version+".unitypackage");
        }
        if (GUILayout.Button("README File Path"))
        {
            ReadMeFilePath();
        }
        EditorGUILayout.Space();
        GUILayout.Label("Manual Files", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Add Selected Files"))
        {
            AddSelectedFiles();
        }
        if (GUILayout.Button("Clear"))
        {
            manualAssetStates.Clear();
            UpdateAssetsToExport();
        }
        EditorGUILayout.EndHorizontal();
        manualAssetsScrollPosition = EditorGUILayout.BeginScrollView(manualAssetsScrollPosition, GUILayout.MaxHeight(100));
        foreach (var asset in manualAssetStates.Keys.ToList())
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(asset, EditorStyles.wordWrappedLabel);
            bool isChecked = EditorGUILayout.Toggle(manualAssetStates[asset], GUILayout.Width(20));
            EditorGUILayout.EndHorizontal();
            if (isChecked != manualAssetStates[asset])
            {
                manualAssetStates[asset] = isChecked;
                UpdateAssetsToExport();
            }
        }
        EditorGUILayout.EndScrollView();
    }
    void SetDefaultAsset(){
        DefaultExport.Clear();
        DefualtAssetStates.Clear();
        string[] array = new string[] {
            "Assets/SPUM/Resources/Addons/Legacy", 
            "Assets/SPUM/Core/Basic_Resources/Animator",
            "Assets/SPUM/Core/Basic_Resources/Materials",
            "Assets/SPUM/Core/Basic_Resources/Ect",
            "Assets/SPUM/Core/Script/Data",
            "Assets/SPUM/Sample"
        };
        DefaultExport.AddRange(array);
        
        foreach (string asset in array)
        {
            if (!DefualtAssetStates.ContainsKey(asset))
            {
                DefualtAssetStates[asset] = true;
            }
        }
        
        UpdateAssetsToExport();
    }
    void RefreshSelectedAssets()
    {
        assetsToExport.Clear();

        //assetsToExport.AddRange(DefaultExport);
        string[] selectedAssets = Selection.assetGUIDs.Select(AssetDatabase.GUIDToAssetPath).ToArray();

        foreach (string asset in selectedAssets)
        {
            if (includeDirectories || !AssetDatabase.IsValidFolder(asset))
            {
                assetsToExport.Add(asset);
            }
        }

        // 종속성 추가
        assetsToExport.AddRange(AssetDatabase.GetDependencies(selectedAssets, true)
            .Where(dep => !assetsToExport.Contains(dep)));

        assetsToExport = assetsToExport.Distinct().ToList();
    }
    void RefreshFolderStates()
    {
        folderStates.Clear();
        folderInfos.Clear();
        string[] allFolders = Directory.GetDirectories(rootPath, "*", SearchOption.AllDirectories);

        foreach (string folder in allFolders)
        {
            string indexPath = Path.Combine(folder, "index.json");
            if (File.Exists(indexPath))
            {
                string unityPath = folder.Replace('\\', '/');
                folderStates[unityPath] = false;

                // Read and parse index.json
                string jsonContent = File.ReadAllText(indexPath);
                FolderInfo info = JsonUtility.FromJson<FolderInfo>(jsonContent);
                folderInfos[unityPath] = info;
            }
        }
    }

    void UpdateAssetsToExport()
    {
        assetsToExport.Clear();
        foreach (var folder in folderStates.Keys)
        {
            if (folderStates[folder])
            {
                assetsToExport.AddRange(GetAssetsInFolder(folder));
            }
        }
        foreach (var asset in DefualtAssetStates.Keys)
        {
            if (DefualtAssetStates[asset])
            {
                if (!assetsToExport.Contains(asset))
                {
                    assetsToExport.Add(asset);
                }
            }
            else
            {
                assetsToExport.Remove(asset);
            }
        }
        foreach (var asset in manualAssetStates.Keys)
        {
            if (manualAssetStates[asset])
            {
                if (!assetsToExport.Contains(asset))
                    assetsToExport.Add(asset);
            }
            else
            {
                assetsToExport.Remove(asset);
            }
        }
    }
    List<string> GetAssetsInFolder(string folderPath)
    {
        List<string> assets = new List<string>();
        string[] guids = AssetDatabase.FindAssets("", new[] { folderPath });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            assets.Add(path);
        }
        return assets;
    }
    void ExportPackage(string Path = null)
    {
        // 선택된 폴더들의 Name 정보를 수집
        List<string> selectedNames = new List<string>();
        foreach (var folder in folderStates.Keys)
        {
            if (folderStates[folder] && folderInfos.TryGetValue(folder, out FolderInfo info))
            {
                if (!string.IsNullOrEmpty(info.Name))
                {
                    selectedNames.Add(info.Name);
                }
            }
        }

        // 패키지 이름 생성
        string packageName = string.Join("_", selectedNames);
        if (string.IsNullOrEmpty(packageName))
        {
            packageName = "SPUM_ExportedPackage"; // 기본 이름
        }
        string packagePath = Path;
        if(Path == null) {
            packagePath = EditorUtility.SaveFilePanel(
                        "Export Package",
                        "",
                        packageName,
                        "unitypackage"
            );
        }

        if (string.IsNullOrEmpty(packagePath))
        {
            return;
        }

        AssetDatabase.ExportPackage(
            assetsToExport.ToArray(),
            packagePath,
            ExportPackageOptions.Default | ExportPackageOptions.Recurse
        );

        Debug.Log("Package exported successfully: " + packagePath);
    }

    void ExportCleanInstallPackage()
    {
        DeleteFilesWithExtension("Assets/SPUM/Package", ".unitypackage");
        
        DefaultExport.Clear();
        DefualtAssetStates.Clear();
        string ReadMePath = ReadMeFilePath();
        List<string> list = new List<string> {
            "Assets/SPUM/Resources/Addons/Legacy", 
            "Assets/SPUM/Resources/Addons/Ver121", 
            "Assets/SPUM/Resources/Addons/Ver300", 
            "Assets/SPUM/Package", 
            "Assets/SPUM/Core",
            "Assets/SPUM/Scene",
            "Assets/SPUM/Script",
            "Assets/SPUM/Sprite_SheetExporter(Beta)",
            "Assets/SPUM/Sprite_Editor(Beta)",
            "Assets/SPUM/Sample"
        };
        if (!string.IsNullOrEmpty(ReadMePath))
        {
            list.Add(ReadMePath);
        }
        string[] array = list.ToArray();
        DefaultExport.AddRange(array);

        foreach (string asset in array)
        {
            if (!DefualtAssetStates.ContainsKey(asset))
            {
                DefualtAssetStates[asset] = true;
            }
        }
        
        UpdateAssetsToExport();
    }
    void AddSelectedFiles()
    {
        string[] selectedPaths = Selection.assetGUIDs.Select(AssetDatabase.GUIDToAssetPath).ToArray();
        foreach (string path in selectedPaths)
        {
            if (!manualAssetStates.ContainsKey(path))
                manualAssetStates[path] = true;
        }
        UpdateAssetsToExport();
    }
    string ReadMeFilePath()
    {
        string readMePath = "Assets/SPUM/README.txt";
        if (File.Exists(readMePath))
        {
            Debug.Log("ReadMe path: " + readMePath);
            return readMePath;
        }
        
        Debug.Log("ReadMe file not found");
        return null;
    }
    void DeleteFilesWithExtension(string directory, string extension)
    {
        Debug.Log(directory + $"*{extension}");
        // 지정된 디렉터리에서 확장자에 맞는 파일 삭제
        foreach (string file in Directory.GetFiles(directory, $"*{extension}"))
        {
            File.Delete(file);
            File.Delete(file+".meta");
        }

        // 하위 디렉터리도 재귀적으로 처리
        // foreach (string subDirectory in Directory.GetDirectories(directory))
        // {
        //     DeleteFilesWithExtension(subDirectory, extension);
        // }
    }
}