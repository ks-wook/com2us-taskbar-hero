#if UNITY_EDITOR
// 에디터에서만 동작
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class IPrefabFileHandler : MonoBehaviour, IFileHandler
{
    public void Delete(SPUM_Prefabs prefab)
    {
        string pathToDelete = AssetDatabase.GetAssetPath(prefab);
        Debug.Log(pathToDelete); 
        AssetDatabase.DeleteAsset(pathToDelete);
    }

    public SPUM_Prefabs Edit(SPUM_Prefabs SpumPreviewUnit, SPUM_Manager manager)
    {
        var SPUM_AnimatorDic = manager.SPUM_AnimatorDic;
        var _version = manager._version;
        var unitPath = manager.unitPath;
        var prefabName = manager.UIManager._unitCode.text;
        var EditPrefab = manager.EditPrefab;
        var isSaveSamePath = manager.isSaveSamePath;

        SPUM_Prefabs PreviewUnit = SpumPreviewUnit.GetComponent<SPUM_Prefabs>();

        //SpumPreviewUnit._code = prefabName;
        SpumPreviewUnit._version = _version;
        //SpumPreviewUnit.EditChk = false;

        GameObject prefabs = Instantiate(SpumPreviewUnit.gameObject);
        SPUM_Prefabs SpumUnitData = prefabs.GetComponent<SPUM_Prefabs>();
        SpumUnitData.ImageElement = SpumPreviewUnit.ImageElement;
        SpumUnitData.spumPackages = SpumPreviewUnit.spumPackages;

        // 비활성화된 오브젝트 삭제하기
        var inactiveObjects = prefabs.transform.Cast<Transform>()
            .Where(child => !child.gameObject.activeInHierarchy)
            .Select(child => child.gameObject)
            .ToList();

        inactiveObjects.ForEach(DestroyImmediate);

        prefabs.transform.localScale = Vector3.one;
        SpumUnitData._anim = prefabs.GetComponentInChildren<Animator>();
        SpumUnitData._anim.runtimeAnimatorController = SPUM_AnimatorDic[SpumPreviewUnit.UnitType];

        var sourcePath = AssetDatabase.GetAssetPath(EditPrefab);
        Debug.Log(sourcePath);
        if(string.IsNullOrWhiteSpace(sourcePath)) 
        {
            sourcePath = Path.Combine(unitPath,SpumUnitData._code );
        }
        var FileName = sourcePath.Split("/");
        var path = isSaveSamePath ? sourcePath.Replace(FileName[FileName.Length-1], "") : unitPath;
        SpumUnitData.PopulateAnimationLists();
        GameObject SavePrefab = PrefabUtility.SaveAsPrefabAsset(prefabs,path+SpumUnitData._code+".prefab");
        DestroyImmediate(prefabs);
        var Prefab = SavePrefab.GetComponent<SPUM_Prefabs>();

        PreviewUnit._code = "";
        return Prefab;
    }

    public SPUM_Prefabs[] Load()
    {
        return Resources.LoadAll<SPUM_Prefabs>("");
    }

    public SPUM_Prefabs Save(SPUM_Prefabs SpumPreviewUnit, SPUM_Manager manager)
    {
        var SPUM_AnimatorDic = manager.SPUM_AnimatorDic;
        var _version = manager._version;
        var unitPath = manager.unitPath;
        var prefabName = manager.UIManager._unitCode.text;

        GameObject prefabs = Instantiate(SpumPreviewUnit.gameObject);
        SPUM_Prefabs SpumUnitData = prefabs.GetComponent<SPUM_Prefabs>();
        SpumUnitData.ImageElement = SpumPreviewUnit.ImageElement;
        SpumUnitData.spumPackages = SpumPreviewUnit.spumPackages;
        // 비활성화된 오브젝트 삭제하기
        var inactiveObjects = prefabs.transform.Cast<Transform>()
            .Where(child => !child.gameObject.activeInHierarchy)
            .Select(child => child.gameObject)
            .ToList();

        inactiveObjects.ForEach(DestroyImmediate);
        
        prefabs.transform.localScale = Vector3.one;
        SpumUnitData._anim = prefabs.GetComponentInChildren<Animator>();
        SpumUnitData._anim.runtimeAnimatorController = SPUM_AnimatorDic[SpumPreviewUnit.UnitType];
        SpumUnitData._version = _version;
        SpumUnitData.PopulateAnimationLists();
        if (!Directory.Exists(unitPath))
        {
            Directory.CreateDirectory(unitPath);
            AssetDatabase.Refresh();
            Debug.Log("Folder created at: " + unitPath);
        }  
        GameObject SavePrefab = PrefabUtility.SaveAsPrefabAsset(prefabs,unitPath+prefabName+".prefab");
        DestroyImmediate(prefabs);
        var Prefab = SavePrefab.GetComponent<SPUM_Prefabs>();
        manager.paginationManager.AddNewPrefab(Prefab);
        return Prefab;
    }
    public void MoveOldPrefabBackup(SPUM_Prefabs asset, SPUM_Manager manager)
    {
        var sourcePath = AssetDatabase.GetAssetPath(asset);
        if (!Directory.Exists(manager.unitBackUpPath))
        {
            Directory.CreateDirectory(manager.unitBackUpPath);
            AssetDatabase.Refresh();
            Debug.Log("Folder created at: " + manager.unitBackUpPath);
        }  
        var destinationPath = manager.unitBackUpPath+asset.name+"_Backup.Prefab";
        AssetDatabase.MoveAsset(sourcePath, destinationPath);
        AssetDatabase.Refresh();
    }

    public (int, List<PreviewMatchingElement>) ValidateSpumFile(SPUM_Prefabs PrefabObject, SPUM_Manager manager)
    {
        var SpumPrefab = PrefabObject;
        var version = SpumPrefab._version;
        var UnitType =  SpumPrefab.UnitType;
        var MatchingList = SpumPrefab.GetComponentsInChildren<SPUM_MatchingList>();
        bool isMatchingListExist = MatchingList != null || MatchingList.Length > 0; // 2.0 시스템
        bool isVersionSame = SpumPrefab._version == version;
        var NewDataListElement = new List<PreviewMatchingElement>();
        var OldData = SpumPrefab.GetComponentInChildren<SPUM_SpriteList>(); // 1.0 시스템
        if(OldData == null) {
            //DebugList.AddRange(PrefabObject.ImageElement);
            return (2, PrefabObject.ImageElement);
        }
        var horseString = OldData._spHorseString;

        var path = AssetDatabase.GetAssetPath(PrefabObject);
        Debug.Log(path);
        bool HorseExist = !string.IsNullOrWhiteSpace(horseString);
        // var horseList = OldData._spHorseSPList._spList;
        // var HorseBodySet = new List<PreviewMatchingElement>();
        // foreach (var renderer in horseList)
        // {
        //     HorseBodySet.AddRange(StringToSpumElementList("Horse", (horseString, renderer)));
        // }
        // NewDataListElement.AddRange(HorseBodySet);
        if(HorseExist){
            var horseReset = manager.SetLegacyHorseData();
            NewDataListElement.AddRange(horseReset);
        }

        string Unitype = "Unit";

        // 메인 바디 
        


        var hairString = OldData._hairListString;
        var hairList = OldData._hairList;
        var TuppleHair = CreateTupleList(hairString, hairList);
        //LoopStringColor(TuppleHair);
        var MaskSet = new List<PreviewMatchingElement>();
        foreach (var tuple in TuppleHair)
        {
            // 투구 및 헤어
            MaskSet.AddRange(StringToSpumElementList(Unitype, tuple, manager));
        }
        //Debug.Log("count " + MaskSet.Count);
        List<string> requiredPartTypes = new List<string> { "Hair", "Helmet"};
        bool result = requiredPartTypes.All(partType => MaskSet.Any(element => element.PartType == partType));
        if(result) 
        {
            foreach (var item in MaskSet)
            {
                if(item.PartType.Equals("Hair")) item.MaskIndex = 1;
            }
        }

        NewDataListElement.AddRange(MaskSet);
        var clothString = OldData._clothListString;
        var clothList = OldData._clothList;
        var TuppleCloth = CreateTupleList(clothString, clothList);
        foreach (var tuple in TuppleCloth)
        {
            NewDataListElement.AddRange(StringToSpumElementList(Unitype, tuple, manager));
        }

        var armorString = OldData._armorListString;
        var armorList = OldData._armorList;
        var TuppleArmor = CreateTupleList(armorString, armorList);
        foreach (var tuple in TuppleArmor)
        {
            NewDataListElement.AddRange(StringToSpumElementList(Unitype, tuple, manager));
        }

        var pantString = OldData._pantListString;
        var pantList = OldData._pantList;
        var TupplePant = CreateTupleList(pantString, pantList);
        foreach (var tuple in TupplePant)
        {
            NewDataListElement.AddRange(StringToSpumElementList(Unitype, tuple, manager));
        }

        var weaponString = OldData._weaponListString;
        var weaponList = OldData._weaponList;
        var TuppleWeapon = CreateTupleList(weaponString, weaponList);
        foreach (var tuple in TuppleWeapon)
        {
            // 예외 케이스 설정 / 왼쪽 오른쪽
            var WeaponsData = StringToSpumElementList(Unitype, tuple, manager);
            NewDataListElement.AddRange(WeaponsData);
        }

        var backString = OldData._backListString;
        var backList = OldData._backList;
        var TuppleBack = CreateTupleList(backString, backList);
        foreach (var tuple in TuppleBack)
        {
            NewDataListElement.AddRange(StringToSpumElementList(Unitype, tuple, manager));
        }



        var bodyString = OldData._bodyString;
        var bodyList = OldData._bodyList;
        var BodySet = new List<PreviewMatchingElement>();
        foreach (var renderer in bodyList)
        {
            BodySet.AddRange(StringToSpumElementList(Unitype, (bodyString, renderer), manager));
        }
        // if(!BodySet.Count.Equals(6))
        // {
        //     BodySet.AddRange(DefaultData("Unit", "Body", "Human_1", Color.white));
        // }
        // UIManager.ConvertView.WarningText.SetActive(BodySet.Count < 6);
        //Debug.Log(BodySet.Count + " ======= Body Count");
        NewDataListElement.AddRange(BodySet);

        //DefaultData("Unit", "Eye", "Eye0", new Color32(71, 26,26, 255));
        var eyeString = "";
        var eyeList = OldData._eyeList; 
        var EyeColorSet = new List<PreviewMatchingElement>();
        foreach (var renderer in eyeList)
        {
            EyeColorSet.AddRange(StringToSpumElementList(Unitype, (eyeString, renderer), manager));
        }
    
        var EyeDistict = EyeColorSet.Distinct().GroupBy(x => new { x.Structure }).Select(g => g.First()).ToList();
        //Debug.Log(EyeDistict.Count + " ======= EyeDistict Count");
        // UIManager.ConvertView.WarningEyeText.SetActive(EyeDistict.Count.Equals(0));
        // if(EyeDistict.Count.Equals(0)){
        //     EyeDistict.AddRange(DefaultData("Unit", "Eye", "Eye0", new Color32(71, 26,26, 255)));
        // }
        foreach (var item in EyeDistict)
        {
            foreach (var sprite in eyeList)
            {
                if(sprite.name.Equals(item.Structure)) 
                { 
                    item.Color = sprite.color; 
                }
            }
        }
        // foreach (var item in EyeDistict)
        // {
        //     if(item.PartType.Equals("Eye")) Debug.Log(item.Color);
        // }
        NewDataListElement.AddRange(EyeDistict);
        //Debug.Log("Unit? " + string.IsNullOrWhiteSpace(horseString));
        
        var distinct = NewDataListElement.Distinct()
        .GroupBy(x => new { x.UnitType, x.PartType, x.Structure, x.Dir })
            .Select(g => g.First())
            .ToList();
        //Debug.Log( " distinct.Count " + distinct.Count);
        //DebugList.AddRange(distinct);
        return (1, distinct);
        // 버전이 다르거나, 구조가 다르거나, 컴포넌트가 없거나 
        // 체크 유닛 타입
        // 체크 패키지 버전
        // 재구축
        //GameObject prefabs = Instantiate(SpumPreviewUnit.gameObject);
        // GameObject tObj = PrefabUtility.SaveAsPrefabAsset(prefabs,unitPath+prefabName+".prefab");
        // DestroyImmediate(prefabs);
    }
    public List<PreviewMatchingElement> StringToSpumElementList(string UnitType, (string, SpriteRenderer) Tuple, SPUM_Manager manager)
    {
        var PartPath = Tuple.Item1;
        string unitType = UnitType;
        //bool isPackage = PartPath.Contains("Packages");
        string PackageName = "Legacy";
        string pattern = @"Packages\/([^\/]+)\/";
        // 패키지는 없지만 이미지 이름은 있는 경우
        bool isPackage = false;
        
        System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(PartPath, pattern);
        if (match.Success)
        {
            PackageName = match.Groups[1].Value;
            isPackage = true;
        }
        if(PackageName.Equals("Heroes")) PackageName = "RetroHeroes";
        bool missingPackage = isPackage && !manager.SpritePackageNameList.Contains(PackageName);
        if(missingPackage)
        {
            manager.MissingPackageNames.Add(PackageName);
        }

        // 경로가 없지만 이미지 리소스는 있는경우 , 패키지 이름은 매칭되지만 패키지 리스트에 없는 경우
        if( ((PartPath == "") && (Tuple.Item2.sprite != null)) || missingPackage){
            var path = AssetDatabase.GetAssetPath(Tuple.Item2.sprite);
            //Assets/SPUM/Resources/Elf/0_Unit/0_Sprite/0_Body/New_Elf_1.png
            PartPath = path;
            //Debug.Log(path);
            string pattern2 = @"Addons\/(.*?)\/0_Unit";

            // 등록된 이미지 리소스 경로로 매칭 시작
            System.Text.RegularExpressions.Match match2 = System.Text.RegularExpressions.Regex.Match(PartPath, pattern2);
            if (match2.Success)
            {
                PackageName = match2.Groups[1].Value;
            }
            

        }
        if(string.IsNullOrWhiteSpace(PartPath)) return new List<PreviewMatchingElement>();
        //var SpriteRendererData = Tuple.Item2;
        


        var PathArray =  PartPath.Split("/");
        string PartType = System.Text.RegularExpressions.Regex.Replace(PathArray[PathArray.Length-2],@"[^a-zA-Z가-힣\s]", "");
        // if(Tuple.Item1 != "") {
        //     Debug.Log("=====================" +Tuple.Item1);
        //     var PathArray2 = Tuple.Item1.Split('/');
        //     PartType = System.Text.RegularExpressions.Regex.Replace(PathArray2[PathArray2.Length-2],@"[^a-zA-Z가-힣\s]", "");
        //     Debug.Log("=====================" +PartType);
        // }
        string NoNamePackagePartType = System.Text.RegularExpressions.Regex.Replace(PathArray[PathArray.Length-3],@"[^a-zA-Z가-힣\s]", "");
        PartType = PartPath.Contains("BodySource") ? "Body" : NoNamePackagePartType.Equals("Weapons") ? "Weapons" : PartType; // 구 바디 예외

        // string NoNamePackagePartType = System.Text.RegularExpressions.Regex.Replace(PathArray[PathArray.Length-2],@"[^a-zA-Z가-힣\s]", "");
        // PartType = PartPath.Contains("BodySource") ? "Body" :  PartType; // 구 바디 예외
        string PartName = System.Text.RegularExpressions.Regex.Replace(PathArray[PathArray.Length-1], @"\..*", "");
        //Debug.Log(PackageName+"/"+unitType +"/"+ PartType+"/"+PartName + "/" + NoNamePackagePartType);
        if(NoNamePackagePartType.Equals("BasicResources")) 
        {
            PartType = PartType.Replace("Backup", "");
        }
        var dir = "";
        bool isHide = false;
        if(PartType.Equals("Helmet"))
        {
            if(Tuple.Item2.name == "12_Helmet2") { dir = "Front"; isHide = Tuple.Item1 == ""; }
            if(Tuple.Item2.name == "11_Helmet1") { dir = "Front"; isHide = Tuple.Item1 == ""; }
        }


        if(PartType.Equals("Weapons"))
        {
            if(Tuple.Item2.name == "R_Weapon") { dir = "Right"; isHide = Tuple.Item1 == ""; }
            if(Tuple.Item2.name == "R_Shield") { dir = "Right"; isHide = Tuple.Item1 == ""; }
            if(Tuple.Item2.name == "L_Weapon") { dir = "Left";  isHide = Tuple.Item1 == ""; }
            if(Tuple.Item2.name == "L_Shield") { dir = "Left";  isHide = Tuple.Item1 == ""; }
            // r weapon
            // r shield
            // l weapon
            // l shield
            //Debug.Log(PartName + "/" +dir);
        }
        //Debug.Log(PackageName + " : " + unitType+ " : " + PartType+ " : " + PartName);
        //패키지 구룹화
        var ExtractList = ExtractTextureData(PackageName, unitType, PartType, PartName, manager);
        
        var ListElement = new List<PreviewMatchingElement>();
        foreach (var item in ExtractList)
        {
            //PartColor = Tuple.Item2.color.Equals(Color.white) ? PartColor : Tuple.Item2.color;
            //Debug.Log($"{ item.Name } { item.UnitType } { item.PartType } { item.PartSubType }");
            //Debug.Log($"Path: {PartType}, SubType: { item.SubType}");
            var part = new PreviewMatchingElement();
            part.UnitType = UnitType;
            part.PartType = PartType;
            part.PartSubType = item.PartSubType;
            part.Dir = dir;//ButtonData.Direction;
            part.ItemPath =  isHide ? "" : item.Path;
            part.Structure = item.SubType.Equals(item.Name) ? PartType : item.SubType;
            part.MaskIndex = 0;//(int)ButtonData.SpriteMask;
            part.Color = Tuple.Item2.color;//ButtonData.PartSpriteColor;
            //Debug.Log(PartType + "/" +Tuple.Item2.color.ToString());

            ListElement.Add(part);
        }
        return ListElement;
    }
    public List<SpumTextureData> ExtractTextureData(string packageName, string unitType, string partType, string textureName, SPUM_Manager manager)
    {
        var query = manager.spumPackages.AsEnumerable();

        if (!string.IsNullOrEmpty(packageName))
        {
            query = query.Where(package => package.Name == packageName);
        }

        return query
            .SelectMany(package => package.SpumTextureData)
            .Where(texture => 
                texture.UnitType == unitType &&
                texture.PartType == partType &&
                texture.Name == textureName)
            .ToList();
    }
    List<(string, SpriteRenderer)> CreateTupleList(List<string> stringList, List<SpriteRenderer> spriteRendererList)
    {
        // 두 리스트의 길이가 다를 경우 짧은 쪽에 맞춥니다.
        int minLength = Mathf.Min(stringList.Count, spriteRendererList.Count);

        // LINQ를 사용하여 튜플 리스트 생성
        return stringList.Take(minLength)
                         .Zip(spriteRendererList.Take(minLength), (s, sr) => (s, sr))
                         .ToList();
    }
}

#endif