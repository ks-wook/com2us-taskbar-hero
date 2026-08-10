using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System;
using System.Collections.Generic;

[CustomEditor(typeof(SPUM_Manager))]
public class SPUM_AnimationManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        SPUM_Manager manager = (SPUM_Manager)target;

        if (GUILayout.Button("REGENERATE DATA & LOAD PACKAGES"))
        {
            var folders = AssetDatabase.GetSubFolders("Assets/SPUM/Resources/Addons");
            Debug.Log(folders.Length);
            foreach (var folder in folders)
            {
                string PackageName = Path.GetDirectoryName(folder);
                Debug.Log(PackageName);

                AllExportJson(folder);
            }
            GetPackageDB(manager);
        }
    }
    private void GetPackageDB(SPUM_Manager target)
    {
        target.spumPackages.Clear();

        var jsonFileArray = Resources.LoadAll<TextAsset>("");
        Debug.Log("Index" + jsonFileArray.Length);
        foreach (var asset in jsonFileArray)
        {
            if(!asset.name.Contains("Index")) continue;
            if(!asset) continue;
            Debug.Log(asset);
            var Package = JsonUtility.FromJson<SpumPackage>(asset.ToString());
            target.spumPackages.Add(Package);
        }
    }
    private void AllExportJson(string folderPath)
    {
        var spumPackageData = new SpumPackage();

        spumPackageData.SpumAnimationData.Clear();
        spumPackageData.SpumTextureData.Clear();
        string DataPath = folderPath;
        string directory = Path.GetDirectoryName(DataPath);
        var directoryArray = DataPath.Split("/");
        string packageName = directoryArray[directoryArray.Length-1];
        spumPackageData.Name = packageName;
        spumPackageData.Path = directory;
        spumPackageData.CreationDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        string[] guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { folderPath });
        foreach (string guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);

            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
            if (clip != null)
            {
                var path = AssetDatabase.GetAssetPath(clip);
                var pathArray = path.Split("/");
                int PathCount = pathArray.Length;
                Debug.Log(PathCount);
                if(PathCount.Equals(9)) {
                    string name = pathArray[pathArray.Length-1]; // 클립 이름
                    string type = Regex.Replace(pathArray[pathArray.Length-2], @"[^a-zA-Z가-힣\s]", ""); //타입 폴더
                    string unitType = Regex.Replace(pathArray[pathArray.Length-4], @"[^a-zA-Z가-힣\s]", ""); //타입 폴더
                    var clipData = new SpumAnimationClip();
                    clipData.Name = name;
                    clipData.StateType = type.ToUpper();
                    clipData.UnitType = unitType; 
                    var clippath = Path.GetRelativePath($"Assets/SPUM/Resources/", path);
                    clipData.ClipPath = clippath;
                    spumPackageData.SpumAnimationData.Add(clipData);
                } 
                else if(PathCount.Equals(10)){
                    string name = pathArray[pathArray.Length-1]; // 클립 이름
                    string type = Regex.Replace(pathArray[pathArray.Length-3],@"[^a-zA-Z가-힣\s]", ""); // 스테이트 타입 폴더
                    string unitType =  Regex.Replace(pathArray[pathArray.Length-5], @"[^a-zA-Z가-힣\s]", ""); //유닛 타입 폴더
                    string SubCategory =Regex.Replace(pathArray[pathArray.Length-2], @"[^a-zA-Z가-힣\s]", "");
                    var clipData = new SpumAnimationClip();
                    clipData.Name = name;
                    clipData.StateType = type.ToUpper();
                    clipData.SubCategory = SubCategory;
                    Debug.Log(unitType);
                    clipData.UnitType = unitType; 
                    var clippath = Path.GetRelativePath($"Assets/SPUM/Resources/", path);
                    clipData.ClipPath = clippath;
                    spumPackageData.SpumAnimationData.Add(clipData);
                }
            }
        }

        Debug.Log($"Found {guids.Count()} animation clips in {directory}");

        guids = AssetDatabase.FindAssets("t:Texture2d", new[] { folderPath });
        foreach (string guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (texture != null)
            {
                var path = AssetDatabase.GetAssetPath(texture);
                //Debug.Log(path);
                // var pathArray = path.Split("/");
                // int PathCount = pathArray.Length;
                Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToArray();  //유니티 에디터에만 사용
                foreach (var sprite in sprites)
                {
                    // public string Name; // 메인 텍스쳐 이름
                    // public string UnitType; // 유닛 타입
                    // public string PartType; // 장비 타입
                    // public string SubType;
                    // public string Path;
                    var t= AssetDatabase.GetAssetPath(sprite);

                    var SpritePath = t.Replace($"Assets/SPUM/Resources/", ""); 
                    var pathArray = SpritePath.Split("/");
                     
                    // 0_Unit\0_Sprite\4_Helmet\Normal_Helmet1.png
                    var textureData = new SpumTextureData();
                    textureData.Name =  texture.name;
                    textureData.UnitType = Regex.Replace(pathArray[2], @"[^a-zA-Z가-힣\s]", "");
                    textureData.PartType = Regex.Replace(pathArray[4], @"[^a-zA-Z가-힣\s]", "");
                    textureData.SubType =  sprite.name; 
                    List<string> conditionTypes = new List<string> { "Front", "Body", "Horse", "Left", "Right", "Back" };
                    var filteredCondition = !conditionTypes.Contains(sprite.name);
                    sprite.name = sprites.Length.Equals(2) && filteredCondition ? texture.name : sprite.name;

                    // Body/Left/Right 토큰은 아머/옷/팬츠류에만 적용
                    var tokens = new[] { "Body", "Left", "Right" };
                    var armorKeywords = new string[] { "armor", "cloth", "pant"};
                    bool isArmorOrCloth = armorKeywords.Any(k => textureData.PartType.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);

                    if (isArmorOrCloth && textureData.SubType == sprite.name)
                    {
                        var nameParts = sprite.name.Split('_');
                        if (nameParts.Length == 3 && armorKeywords.Any(k => nameParts[0].IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            string middleToken = nameParts[1];
                            if (tokens.Any(t => string.Equals(t, middleToken, StringComparison.OrdinalIgnoreCase)))
                            {
                                textureData.SubType = middleToken;
                                textureData.Name = $"{nameParts[0]}{nameParts[2]}";
                            }
                            else
                            {
                                textureData.SubType = sprite.name;
                            }
                        }
                        else
                        {
                            textureData.SubType = sprite.name;
                        }
                    }
                    else
                    {
                        textureData.SubType = sprite.name;
                    }
                    Debug.Log(sprite.name + "/" + pathArray.Length);
                    textureData.PartSubType = pathArray.Length.Equals(7) ? Regex.Replace(pathArray[5], @"[^a-zA-Z가-힣\s]", "") : "";
                    textureData.Path = Regex.Replace(SpritePath, @"\..*", "");
                    spumPackageData.SpumTextureData.Add(textureData);
                    
                }
            }
        }
        if(spumPackageData.SpumTextureData.Count.Equals(0) && spumPackageData.SpumAnimationData.Count.Equals(0)) return;
        string json = JsonUtility.ToJson(spumPackageData, true);
        
        if (!string.IsNullOrEmpty(folderPath))
        {
            string normalizedPath = json.Replace("\\\\", "/");
            File.WriteAllText(folderPath+"/Index.json", normalizedPath);
            AssetDatabase.Refresh();
            Debug.Log("JSON file created at: " + directory);
        }
    }
    // private void go(SPUM_AnimationManager target){
    //     target.AllLoad = Resources.LoadAll<UnityEngine.Object>("").ToList();
    // }
}
