using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.Linq;
using System;
using System.Globalization;
using UnityEngine.SceneManagement;

[Serializable]
public class SPUM_Animator{
    public string Type;
    public RuntimeAnimatorController RuntimeAnimator;
}
public class SPUM_Manager : MonoBehaviour
{
    public float _version;
    public string unitPath = "Assets/SPUM/Resources/Units/"; // 프리펩 저장 장소
    public string unitBackUpPath = "Assets/SPUM/Backup/";
    public bool isSaveSamePath = false;
    public SPUM_Prefabs EditPrefab;
    public SPUM_Prefabs PreviewPrefab; // 프리뷰 프리펩
    public SPUM_Animator[] SPUM_Animator; 
    public Dictionary<string, RuntimeAnimatorController> SPUM_AnimatorDic = new();
    public Toggle RandomColorButton;
    public List<SpumPackage> spumPackages;
    public List<string> StateList = new ();
    public List<string> UnitTypeList = new();
    public List<string> SpritePackageNameList = new();
    [HideInInspector] public HashSet<string> MissingPackageNames = new();
    [Header("Manager")]
    [SerializeField] public SPUM_AnimationManager animationManager;
    [SerializeField] public SPUM_UIManager UIManager;
    [SerializeField] public SPUM_PaginationManager paginationManager;
    public IFileHandler fileHandler => GetComponent<IFileHandler>();

#region Unity Function
    void Awake()
    {
        SoonsoonData.Instance._spumManager= this;
        LoadPackages();
        var unit = PreviewPrefab;
        if(unit.spumPackages.Count.Equals(0)) {
            var InitLegacyData = GetSpumLegacyData();
            if(string.IsNullOrEmpty(unit.UnitType)) unit.UnitType = "Unit";
            unit.spumPackages = InitLegacyData;
        }
        var uniqueTypes = spumPackages
            .SelectMany(package => package.SpumAnimationData)
            .Select(clip => clip.StateType.ToUpper())
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .ToList();
        StateList = uniqueTypes;

        var uniqueUnitTypes = spumPackages
            .SelectMany(package => package.SpumAnimationData)
            .Select(clip => clip.UnitType)
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .ToList();
        UnitTypeList = uniqueUnitTypes;

        var uniquePackageNames = spumPackages
        .Where(package => package.SpumTextureData.Count > 0)
        .Select(package => package.Name)
        .Distinct(System.StringComparer.OrdinalIgnoreCase)
        .ToList();
        SpritePackageNameList = uniquePackageNames;
    }
    void Start()
    {
        SPUM_AnimatorDic = SPUM_Animator.ToDictionary(item => item.Type, item => item.RuntimeAnimator);
        
        StartCoroutine(StartProcess());
        SetType("Unit");
        ItemResetAll();
        Setup();
    }
    [ContextMenu("Setup")]
    public void Setup(){
        SetDefultSet("Unit", "Body", "Human_1", Color.white);
        SetDefultSet("Unit", "Eye", "Eye0", new Color32(71, 26,26, 255));
        SetDefultSet("Horse", "Body", "Horse1", Color.white);
    }
#endregion

    public IEnumerator StartProcess()
    {
        Debug.Log("Data Load processing..");

        // 버전 체크 및 패키지 데이터 체크
        yield return StartCoroutine(SoonsoonData.Instance.LoadData());

        // 작업 색상 정보 로드
        if( SoonsoonData.Instance._soonData2._savedColorList == null ||  SoonsoonData.Instance._soonData2._savedColorList.Count.Equals(0))
        {
            SoonsoonData.Instance._soonData2._savedColorList = new List<string>();
            for(var i = 0 ; i < 17 ;i++)
            {
                SoonsoonData.Instance._soonData2._savedColorList.Add("");
            }
            SoonsoonData.Instance.SaveData();
        }
        else
        {
            //Debug.Log( SoonsoonData.Instance._soonData2._savedColorList.Count);
            for(var i = 0 ; i < SoonsoonData.Instance._soonData2._savedColorList.Count ;i++)
            {
                string tSTR = SoonsoonData.Instance._soonData2._savedColorList[i];
                //Debug.Log(tSTR);
                Color parsedColor;
                if(ColorUtility.TryParseHtmlString(tSTR, out parsedColor)){
                    UIManager._colorSaveList[i]._savedColor.gameObject.SetActive(true);
                    UIManager._colorSaveList[i]._savedColor.color = parsedColor;
                }else{
                    if(string.IsNullOrWhiteSpace(tSTR)) continue;
                    Debug.LogWarning(tSTR + " is Invalid color information");
                }
            }

        }
    }
    private void LoadPackages()
    {
        spumPackages.Clear();
        var jsonFileArray = Resources.LoadAll<TextAsset>("");
        //Debug.Log("Index" + jsonFileArray.Length);
        foreach (var asset in jsonFileArray)
        {
            if(!asset.name.Contains("Index")) continue;
            if(!asset) continue;
            Debug.Log(asset);
            var Package = JsonUtility.FromJson<SpumPackage>(asset.ToString());
            spumPackages.Add(Package);
        }
        var dataSortList = spumPackages.OrderBy(p => DateTime.ParseExact(p.CreationDate, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).ToList();
        spumPackages = dataSortList;
    }
    public List<SpumPackage> GetSpumPackageData(){
        return spumPackages;
    }
    public List<SpumPackage> GetSpumLegacyData(){
        string targetString = "Legacy";
        var LegacyData = spumPackages;
        foreach (var package in LegacyData)
        {
            if(package.Name.Equals(targetString)) {
                foreach (var data in package.SpumAnimationData)
                {
                    data.HasData = true;
                }
            }
        }
        return LegacyData; // Package;
    }
#region PreviewItemPanel
    public void DrawItemList(SPUM_SpriteButtonST ButtonData)
    {
        UIManager.SetPackageButtons(ButtonData);
        
        var enabledPackages =  UIManager.SpritePackagesFilterList
            .Where(kvp => kvp.Value)
            .Select(kvp => kvp.Key)
            .ToList();
        
        UIManager._panelTitle.text = ButtonData.PartType;

        UIManager.ClearPreviewItems();

        var SpumPackages =  spumPackages;

        string unitType = ButtonData.UnitType;
        string partType = ButtonData.PartType;

        //패키지 구룹화 - 패키지 필터 조건
        var groupedPackageData = SpumPackages
            .Where(p => enabledPackages.Contains(p.Name)) // 패키지 이름이 enabledPackages 리스트에 있는 것만 선택
            .SelectMany(p => p.SpumTextureData.Select(t => new { Package = p, Texture = t }))
            .Where(x => x.Texture.PartType.Equals(partType) && x.Texture.UnitType.Equals(unitType))
            .GroupBy(x => new { x.Texture.PartType, x.Texture.Name, x.Texture.UnitType }) // 구룹 키
            .Select(g => new 
            { 
                Key = g.Key, 
                Items = g.ToList() 
            })
            .ToList();
        
        // 필터된 패키지 아이템 순환
        foreach (var package in groupedPackageData) 
        {
            // 프리뷰 아이템 버튼 생성
            var PreviewItem = UIManager.CreatePreviewItem();
            var previewButton = PreviewItem.GetComponent<SPUM_PreviewItem>();
            previewButton.name = package.Key.Name;
            
            // 관련 패키지 데이터 주입
            var relatedPackages = package.Items.Select(item => item.Package).Distinct().ToList();
            previewButton.spumPackages = relatedPackages;
            
            // Hide Other Element
            foreach (Transform tr in previewButton.transform)
            {
                tr.gameObject.SetActive(tr.name.ToUpper() == ButtonData.ItemShowType.ToUpper());
            }
            previewButton.SetSpriteButton.onClick.AddListener(()=> { ButtonData.IsActive = true; } );

            //Preview Element Matching Map
            var UnitPartLists = previewButton.GetComponentsInChildren<SPUM_PreviewMatchingList>(); 

            // 보여질 이미지 추출
            var UnitPartList = UnitPartLists 
                .SelectMany(table => table.matchingTables)
                .Where(element => element.PartType == package.Key.PartType && element.UnitType == package.Key.UnitType)
                .ToList();
            //Debug.Log($"PartType: {group.Key.PartType}, Name: {group.Key.Name}, UnitType: {group.Key.UnitType}");
            foreach (var item in package.Items) // 아이템 개별 항목 순환
            {
                //Debug.Log($"  Package: {item.Package.Name}, SubType: {item.Texture.SubType}, Path: {item.Texture.Path}");
                foreach (var part in UnitPartList)
                {
                    if(part.Structure == item.Texture.SubType){ // 멀티플 타입 이미지 인 경우
                        var LoadSprite = LoadSpriteFromMultiple(item.Texture.Path, item.Texture.SubType);
                        part.ItemPath = item.Texture.Path;
                        part.image.sprite = LoadSprite;
                        part.PartSubType = item.Texture.PartSubType;
                        float pixelWidth = LoadSprite.rect.width;
                        float pixelHeight = LoadSprite.rect.height;

                        float unityWidth = pixelWidth / LoadSprite.pixelsPerUnit;
                        float unityHeight = pixelHeight / LoadSprite.pixelsPerUnit;

                        part.image.rectTransform.sizeDelta = new Vector2(unityWidth * 100, unityHeight * 100);
                        part.Dir = ButtonData.Direction;
                        Color PartColor = ButtonData.ignoreColorPart.Contains(item.Texture.SubType) ? Color.white : ButtonData.PartSpriteColor;
                        part.image.color = PartColor;
                        part.Color = PartColor;
                        part.MaskIndex = (int)ButtonData.SpriteMask;
                        previewButton.ImageElement.Add(part);
                    }
                    else if(UnitPartList.Count == 1) // 서브타입이 없는 멀티플이 아닌경우 
                    {
                        var LoadSprite = LoadSpriteFromMultiple(item.Texture.Path, item.Texture.SubType);
                        part.ItemPath = item.Texture.Path;
                        part.image.sprite = LoadSprite;
                        part.PartSubType = item.Texture.PartSubType;
                        float pixelWidth = LoadSprite.rect.width;
                        float pixelHeight = LoadSprite.rect.height;

                        float unityWidth = pixelWidth / LoadSprite.pixelsPerUnit;
                        float unityHeight = pixelHeight / LoadSprite.pixelsPerUnit;

                        part.image.rectTransform.sizeDelta = new Vector2(unityWidth * 100, unityHeight * 100);
                        part.Dir = ButtonData.Direction;
                        part.Structure = item.Texture.SubType.Equals(item.Texture.Name) ? package.Key.PartType : item.Texture.SubType;
                        Color PartColor = ButtonData.ignoreColorPart.Contains(item.Texture.PartSubType) ? Color.white : ButtonData.PartSpriteColor;
                        part.image.color = PartColor;
                        part.Color = PartColor;
                        part.MaskIndex = (int)ButtonData.SpriteMask;
                        previewButton.ImageElement.Add(part);
                    }
                }
            }
            previewButton.ImageElement = previewButton.ImageElement.Distinct().ToList();
            // 필요없는 항목 비활성화
            UnitPartList.ForEach(item => 
            {
                item.image.gameObject.SetActive(item.image.sprite != null);
            });

            // 바뀔 프리뷰 항목 가져오기
        }
        // 프리뷰 아이템 데이터를 적용 필요
        UIManager.ShowItem();
    }
#endregion

#region PreviewUnit
    public void SetType(string Type){
        var PreviewUnit = PreviewPrefab;
        PreviewUnit.UnitType = Type;
        foreach (Transform child in PreviewUnit.transform)
        {
            child.gameObject.SetActive(child.name.Contains(Type));
        }
        var anim = PreviewUnit.GetComponentInChildren<Animator>();
        PreviewPrefab._anim = anim;
        if(PreviewUnit.UnitType.Equals(Type)) return;
        if(Type.Equals("Unit"))
        {
            var ElementList = PreviewUnit.ImageElement;
            ElementList.RemoveAll(element => element.UnitType != Type);
        }else{
            SetDefultSet(Type, "Body", Type+"1", Color.white);
        }
    }
    public void SetDefultSet(string UnitType, string PartType, string TextureName, Color color)
    {
        string PackageName ="Legacy";
        //패키지 구룹화
        var groupedData = spumPackages
            .SelectMany(p => p.SpumTextureData.Select(t => new { Package = p, Texture = t }))
            .Where(x => x.Texture.PartType.Equals(PartType) && x.Texture.UnitType.Equals(UnitType) && x.Package.Name.Equals(PackageName) && x.Texture.Name.Equals(TextureName))
            .GroupBy(x => new { x.Texture.PartType, x.Texture.Name, x.Texture.UnitType }) // 그룹 키
            .Select(g => new 
            { 
                Key = g.Key, 
                Items = g.ToList() 
            })
            .ToList();

        var Parts = groupedData[0];

        var ListElement = new List<PreviewMatchingElement>();
        foreach (var item in Parts.Items)
        {
            //Debug.Log($"Path: {PartType}, SubType: { item.Texture.SubType}");
            var part = new PreviewMatchingElement();
            part.UnitType = UnitType;
            part.PartType = PartType;
            part.PartSubType = item.Texture.PartSubType;
            part.Dir = "";
            part.ItemPath = item.Texture.Path;
            part.Structure = item.Texture.SubType.Equals(item.Texture.Name) ? PartType : item.Texture.SubType;
            part.MaskIndex = 0;
            part.Color = color;

            ListElement.Add(part);
        }
        SetSprite(ListElement);
    }
    public void SetSprite(List<PreviewMatchingElement> ImageElement)
    {
        var PreviewUnit = PreviewPrefab;
        SaveElementData(ImageElement);

        // 프리뷰 유닛의 매칭 리스트를 가지고 온다.
        var matchingTables = PreviewUnit.GetComponentsInChildren<SPUM_MatchingList>(true);

        var allMatchingElements = matchingTables.SelectMany(mt => mt.matchingTables);
        //Debug.Log(ImageElement.Count + "SetSpriteCount" + allMatchingElements.Count());
        foreach (var matchingElement in allMatchingElements)
        {
            var matchingTypeElement = ImageElement.FirstOrDefault(ie => 
            (ie.UnitType == matchingElement.UnitType)
            && ("Weapons" == matchingElement.PartType)
            //&& ie.Index == matchingElement.Index
            && (ie.Dir == matchingElement.Dir) 
            && (ie.Structure == matchingElement.Structure)
            );
            //Debug.Log(matchingTypeElement != null);
            if (matchingTypeElement != null)
            {
                matchingElement.renderer.sprite = null;
                matchingElement.renderer.maskInteraction = (SpriteMaskInteraction) matchingTypeElement.MaskIndex;
                matchingElement.renderer.color = matchingTypeElement.Color;
                matchingElement.ItemPath = "";
                matchingElement.Color = matchingTypeElement.Color;
            }
        }
        #if UNITY_2023_1_OR_NEWER
            var ItemButtons = FindObjectsByType<SPUM_SpriteButtonST>(FindObjectsSortMode.None);
        #else
            #pragma warning disable CS0618
            var ItemButtons = FindObjectsOfType<SPUM_SpriteButtonST>();
            #pragma warning restore CS0618
        #endif

        foreach (var matchingElement in allMatchingElements)
        {
            var matchingTypeElement = ImageElement.FirstOrDefault(ie => 
            (ie.UnitType == matchingElement.UnitType)
            && (ie.PartType == matchingElement.PartType)
            //&& ie.Index == matchingElement.Index
            && (ie.Dir == matchingElement.Dir) 
            && (ie.Structure == matchingElement.Structure) 
            && (ie.PartSubType == matchingElement.PartSubType)
            //&& !string.IsNullOrEmpty( matchingElement.PartSubType )
            );
            //Debug.Log(matchingTypeElement != null);
            if (matchingTypeElement != null)
            {
                var existingElement = ItemButtons.FirstOrDefault(e => e.PartType == matchingTypeElement.PartType);
                var LoadSprite = LoadSpriteFromMultiple(matchingTypeElement.ItemPath , matchingTypeElement.Structure);
                matchingElement.renderer.sprite = LoadSprite;
                matchingElement.renderer.maskInteraction = (SpriteMaskInteraction) matchingTypeElement.MaskIndex;
                Color PartColor = existingElement.ignoreColorPart.Contains(matchingElement.Structure) ? Color.white : matchingTypeElement.Color;
                matchingElement.renderer.color = PartColor;
                matchingElement.ItemPath = matchingTypeElement.ItemPath;
                matchingElement.Color =  PartColor;
            }
        }
        UIManager.DrawItemOff();
    }

    public void SetPartRandom(SPUM_SpriteButtonST ButtonData)
    {
        var isSpriteFixed = ButtonData.IsSpriteFixed;
        var UnitType = ButtonData.UnitType;
        var PartType = ButtonData.PartType;

        if(isSpriteFixed) return;
        string unitType = UnitType;
        string partType = PartType;

        //패키지 구룹화
        var groupedData = spumPackages
            .SelectMany(p => p.SpumTextureData.Select(t => new { Package = p, Texture = t }))
            .Where(x => x.Texture.PartType.Equals(partType) && x.Texture.UnitType.Equals(unitType))
            .GroupBy(x => new { x.Texture.PartType, x.Texture.Name, x.Texture.UnitType }) // 구룹 키
            .Select(g => new 
            { 
                Key = g.Key, 
                Items = g.ToList() 
            })
            .ToList();
        int randomValue = UnityEngine.Random.Range(0, groupedData.Count+1);

        if(randomValue.Equals(groupedData.Count)) 
        {
            ButtonData.RemoveSprite();
            return;
        }
        var randomGroup = groupedData[randomValue];

        var ListElement = new List<PreviewMatchingElement>();
        foreach (var item in randomGroup.Items)
        {
            //Debug.Log($"Path: {PartType}, SubType: { item.Texture.SubType}");
            var part = new PreviewMatchingElement();
            part.UnitType = UnitType;
            part.PartType = PartType;
            part.PartSubType = item.Texture.PartSubType;
            part.Dir = ButtonData.Direction;
            part.ItemPath = item.Texture.Path;
            part.Structure = item.Texture.SubType.Equals(item.Texture.Name) ? PartType : item.Texture.SubType;
            part.MaskIndex = (int)ButtonData.SpriteMask;
            Color PartColor = ButtonData.ignoreColorPart.Contains(item.Texture.SubType) ? Color.white : ButtonData.InitColor;
            part.Color = PartColor;

            ListElement.Add(part);
        }
        SetSprite(ListElement);
    }
    public void SetDefaultPart(SPUM_SpriteButtonST ButtonData)
    {
        var isSpriteFixed = ButtonData.IsSpriteFixed;
        var IsActive = ButtonData.IsActive;
        var UnitType = ButtonData.UnitType;
        var PartType = ButtonData.PartType;
        var PackageName = ButtonData.DefaultPackageName;
        var TextureName = ButtonData.DefaultTextureName;
        if(isSpriteFixed) return;
        IsActive = true;
        ButtonData.PartSpriteColor = ButtonData.InitColor;
        //Debug.Log(ButtonData.PartType + " " +  ButtonData.InitColor);
        //패키지 구룹화
        var groupedData = spumPackages
            .SelectMany(p => p.SpumTextureData.Select(t => new { Package = p, Texture = t }))
            .Where(x => x.Texture.PartType.Equals(PartType) && x.Texture.UnitType.Equals(UnitType) && x.Package.Name.Equals(PackageName) && x.Texture.Name.Equals(TextureName))
            .GroupBy(x => new { x.Texture.PartType, x.Texture.Name, x.Texture.UnitType }) // 그룹 키
            .Select(g => new 
            { 
                Key = g.Key, 
                Items = g.ToList() 
            })
            .ToList();
        //Debug.Log($"UnitType: {UnitType}, PartType: { PartType}, PackageName: { PackageName}, TextureName: { TextureName} / {groupedData.Count}");
        var randomGroup = groupedData[0];
        var ListElement = new List<PreviewMatchingElement>();
        foreach (var item in randomGroup.Items)
        {
            //Debug.Log($"Path: {PartType}, SubType: { item.Texture.Name}");
            var part = new PreviewMatchingElement();
            part.UnitType = UnitType;
            part.PartType = PartType;
            part.PartSubType = item.Texture.PartSubType;
            part.Dir = ButtonData.Direction;
            part.ItemPath = item.Texture.Path;
            part.Structure = item.Texture.SubType.Equals(item.Texture.Name) ? PartType : item.Texture.SubType;  
            part.MaskIndex = 0;
            //part.Color = ButtonData.InitColor;
            
            Color PartColor = ButtonData.ignoreColorPart.Contains(item.Texture.SubType) ? Color.white : ButtonData.InitColor;
            part.Color = PartColor;

            ListElement.Add(part);
        }
        SetSprite(ListElement);
    }
    public void RemoveSprite(SPUM_SpriteButtonST ButtonData)
    {
        var PreviewUnit = PreviewPrefab;
        var isSpriteFixed = ButtonData.IsSpriteFixed;
        var IsActive = ButtonData.IsActive;
        var UnitType = ButtonData.UnitType;
        var PartType = ButtonData.PartType;

        if(isSpriteFixed) return;
        ButtonData.IsActive = false;
        ButtonData.PartSpriteColor = ButtonData.InitColor;
        var matchingTables = PreviewUnit.GetComponentsInChildren<SPUM_MatchingList>(true);

        var allMatchingElements = matchingTables.SelectMany(mt => mt.matchingTables)
            .Where(element => 
                element.UnitType == UnitType &&
                element.PartType == PartType &&
                element.Dir == ButtonData.Direction
            );
        var ListElement = new List<PreviewMatchingElement>();
        foreach (var matchingElement in allMatchingElements)
        {
            //Debug.Log($"{matchingElement.UnitType} {matchingElement.Structure} {matchingElement.Dir}");
            var part = new PreviewMatchingElement();
            part.UnitType = UnitType;
            part.PartType = PartType;
            part.PartSubType = matchingElement.PartSubType;
            part.Dir = matchingElement.Dir;
            part.Structure = matchingElement.Structure;

            matchingElement.renderer.sprite = null;
            matchingElement.renderer.color = ButtonData.InitColor;
            matchingElement.ItemPath = "";
            matchingElement.Color = ButtonData.InitColor;
            ListElement.Add(part);
        }
        RemoveElementData(ListElement);
    }

    public void SetSpriteColor(SPUM_SpriteButtonST ButtonData)
    {
        var PreviewUnit = PreviewPrefab;
        var isSpriteFixed = ButtonData.IsSpriteFixed;
        var IsActive = ButtonData.IsActive;
        var UnitType = ButtonData.UnitType;
        var PartType = ButtonData.PartType;

        if(isSpriteFixed) return;
        var matchingTables = PreviewUnit.GetComponentsInChildren<SPUM_MatchingList>(true);

        var allMatchingElements = matchingTables.SelectMany(mt => mt.matchingTables)
            .Where(element => 
                element.UnitType == UnitType &&
                element.PartType == PartType &&
                element.Dir == ButtonData.Direction
            );
        var ListElement = new List<PreviewMatchingElement>();
        foreach (var matchingElement in allMatchingElements)
        {
            //Debug.Log($"{matchingElement.UnitType} {matchingElement.Structure} {matchingElement.Dir}");
            var part = new PreviewMatchingElement();
            part.UnitType = UnitType;
            part.PartType = PartType;
            part.Dir = matchingElement.Dir;
            part.Structure = matchingElement.Structure;  
            Color PartColor = ButtonData.ignoreColorPart.Contains(matchingElement.Structure) ? Color.white : ButtonData.PartSpriteColor;
            part.Color = PartColor;
            matchingElement.Color = PartColor;
            matchingElement.renderer.color = PartColor;
            ListElement.Add(part);
        }
        SetElementColorData(ListElement);
    }
    public void SetSpriteVisualMaskIndex(SPUM_SpriteButtonST ButtonData)
    {
        var PreviewUnit = PreviewPrefab;
        var isSpriteFixed = ButtonData.IsSpriteFixed;
        var IsActive = ButtonData.IsActive;
        var UnitType = ButtonData.UnitType;
        var PartType = ButtonData.PartType;

        //if(isSpriteFixed) return;
        var matchingTables = PreviewUnit.GetComponentsInChildren<SPUM_MatchingList>(true);

        var allMatchingElements = matchingTables.SelectMany(mt => mt.matchingTables)
            .Where(element => 
                element.UnitType == UnitType &&
                element.PartType == PartType &&
                element.Dir == ButtonData.Direction
            );
        var ListElement = new List<PreviewMatchingElement>();
        foreach (var matchingElement in allMatchingElements)
        {
            matchingElement.renderer.maskInteraction = ButtonData.SpriteMask;
            var part = new PreviewMatchingElement();
            part.UnitType = UnitType;
            part.PartType = PartType;
            part.Dir = matchingElement.Dir;
            part.Structure = matchingElement.Structure;  
            part.MaskIndex = (int)ButtonData.SpriteMask;
            ListElement.Add(part);
        }
        SetElementMaskData(ListElement);
    }
    public void ItemRandomAll()
    {
        #if UNITY_2023_1_OR_NEWER
            var ItemButtons = FindObjectsByType<SPUM_SpriteButtonST>(FindObjectsSortMode.None);
        #else
            #pragma warning disable CS0618
            var ItemButtons = FindObjectsOfType<SPUM_SpriteButtonST>();
            #pragma warning restore CS0618
        #endif
        List<string> conditionTypes = new List<string> {"Body", "Horse" };

        var filteredButtons = ItemButtons.Where(button => !conditionTypes.Contains(button.ItemShowType)).ToList();
        foreach (var button in filteredButtons)
        {
            button.SetPartRandom();
        }
    }
    public void ItemResetAll()
    {
        //Debug.Log("ItemResetAll");
        #if UNITY_2023_1_OR_NEWER
            var ItemButtons = FindObjectsByType<SPUM_SpriteButtonST>(FindObjectsSortMode.None);
        #else
            #pragma warning disable CS0618
            var ItemButtons = FindObjectsOfType<SPUM_SpriteButtonST>();
            #pragma warning restore CS0618
        #endif
        foreach (var button in ItemButtons)
        {
            button.RemoveSprite();
        }
        
        ResetBody();
    }
    public void ResetBody()
    {
        SetType(PreviewPrefab.UnitType);

        #if UNITY_2023_1_OR_NEWER
            var ItemButtons = FindObjectsByType<SPUM_SpriteButtonST>(FindObjectsSortMode.None);
        #else
            #pragma warning disable CS0618
            var ItemButtons = FindObjectsOfType<SPUM_SpriteButtonST>();
            #pragma warning restore CS0618
        #endif
        List<string> conditionTypes = new List<string> { "Eye" , "Body" };
        if(PreviewPrefab.UnitType.Equals("Horse")){
            conditionTypes.Add("Horse");
        }
        var filteredButtons = ItemButtons.Where(button => conditionTypes.Contains(button.ItemShowType)).ToList();
        foreach (var button in filteredButtons)
        {
            //Debug.Log(button.ItemShowType);
            button.SetInitPart();
        }
    }

    public void ItemLoadButtonActive(List<PreviewMatchingElement> ImageElement)
    {
        string[] uniquePartTypes = ImageElement.Select(m => m.PartType).Distinct().ToArray();
        var partTypeUnitTypeColorDict = ImageElement
                                            .GroupBy(m => new { m.PartType, m.UnitType, m.Dir })
                                            .ToDictionary(g => g.Key, g => g.First().Color);
        #if UNITY_2023_1_OR_NEWER
            var ItemButtons = FindObjectsByType<SPUM_SpriteButtonST>(FindObjectsSortMode.None);
        #else
            #pragma warning disable CS0618
            var ItemButtons = FindObjectsOfType<SPUM_SpriteButtonST>();
            #pragma warning restore CS0618
        #endif

        foreach (var button in ItemButtons)
        {
            var key = new { PartType = button.ItemShowType, UnitType = button.UnitType, Dir = button.Direction };
            
            if (partTypeUnitTypeColorDict.ContainsKey(key))
            {
                button.IsActive = true;
                button.PartSpriteColor = partTypeUnitTypeColorDict[key];
            }
            else
            {
                button.IsActive = false;
            }
        }
    }
    
    public void SetPrefabToPreviewPackageData(List<SpumPackage> packages){
        if(packages.Count.Equals(0)){
            PreviewPrefab.spumPackages = GetSpumLegacyData();
        }else{
            PreviewPrefab.spumPackages = packages;
        }
        // 패키지 체크
        Debug.Log($"Prefab Package { packages.Count } / Total Package { spumPackages.Count }");
        animationManager.PlayFirstAnimation();
    }

#endregion

#region ElementData
    private bool AreElementsEqual(PreviewMatchingElement element1, PreviewMatchingElement element2)
    {
        return element1.UnitType == element2.UnitType &&
            element1.PartType == element2.PartType &&
            element1.Dir == element2.Dir &&
            element1.Structure == element2.Structure
            ;
        // 필요한 만큼 조건을 추가할 수 있습니다.
    }
    public void SaveElementData(List<PreviewMatchingElement> ElementList)
    {
        var PreviewUnit = PreviewPrefab;
        foreach (var newElement in ElementList)
        {
            var existingElement = PreviewUnit.ImageElement.FirstOrDefault(e => AreElementsEqual(e, newElement));

            if (existingElement != null)
            {
                int index = PreviewUnit.ImageElement.IndexOf(existingElement);
                PreviewUnit.ImageElement[index] = newElement;
            }
            else
            {
                PreviewUnit.ImageElement.Add(newElement);
            }
        }
    }
    public void RemoveElementData(List<PreviewMatchingElement> ElementList)
    {
        var PreviewUnit = PreviewPrefab;
        foreach (var newElement in ElementList)
        {
            var existingElement = PreviewUnit.ImageElement.FirstOrDefault(e => AreElementsEqual(e, newElement));

            if (existingElement != null)
            {
                int index = PreviewUnit.ImageElement.IndexOf(existingElement);
                PreviewUnit.ImageElement.RemoveAt(index);
            }
        }
    }
    public void SetElementColorData(List<PreviewMatchingElement> ElementList)
    {
         var PreviewUnit = PreviewPrefab;
        foreach (var newElement in ElementList)
        {
            var existingElement = PreviewUnit.ImageElement.FirstOrDefault(e => AreElementsEqual(e, newElement));

            if (existingElement != null)
            {
                int index = PreviewUnit.ImageElement.IndexOf(existingElement);
                var ElementData = PreviewUnit.ImageElement[index];
                ElementData.Color = newElement.Color;
            }
        }
    }
    public void SetElementMaskData(List<PreviewMatchingElement> ElementList)
    {
         var PreviewUnit = PreviewPrefab;
        foreach (var newElement in ElementList)
        {
            var existingElement = PreviewUnit.ImageElement.FirstOrDefault(e => AreElementsEqual(e, newElement));

            if (existingElement != null)
            {
                int index = PreviewUnit.ImageElement.IndexOf(existingElement);
                var ElementData = PreviewUnit.ImageElement[index];
                ElementData.MaskIndex = newElement.MaskIndex;
            }
        }
    }

#endregion

#region Prefab


    //프리팹 저장 부분
    public void SavePrefabs()
    {
        animationManager.CloseAnimationPanels();
        var SpumPreviewUnit = PreviewPrefab;
        string prefabName = UIManager._unitCode.text;

        SpumPreviewUnit._code = prefabName;
        SyncPackagesWithImageElement(SpumPreviewUnit);
        var Prefab = fileHandler.Save(SpumPreviewUnit, this);
        
        UIManager.ToastOn("Saved Unit Object " + prefabName);
        
        //paginationManager.AddNewPrefab(Prefab);
        SpumPreviewUnit._code = "";
        UIManager.ResetUniqueID();
        //SpumPreviewUnit.EditChk = true;

        UIManager.ShowNowUnitNumber();
        NewMake();
    }

    //프리펩 수정 부분
    public void EditPrefabs()
    {
        var SpumPreviewUnit = PreviewPrefab;

        string prefabCode = SpumPreviewUnit._code;

        SyncPackagesWithImageElement(SpumPreviewUnit);
        var Prefab = fileHandler.Edit(SpumPreviewUnit, this);

        SpumPreviewUnit._code = "";
        UIManager.ResetUniqueID();

        UIManager.ToastOn("Edited Unit Object Unit" + prefabCode);

        NewMake();
    }

    private void SyncPackagesWithImageElement(SPUM_Prefabs unit)
    {
        var usedPackageNames = new HashSet<string>();

        // 이미지에서 사용 중인 패키지
        foreach (var elem in unit.ImageElement)
        {
            if (string.IsNullOrEmpty(elem.ItemPath)) continue;
            string[] pathParts = elem.ItemPath.Replace("\\", "/").Split('/');
            string pkgName = pathParts[0];
            if (pkgName == "Addons" && pathParts.Length >= 3) pkgName = pathParts[1];
            usedPackageNames.Add(pkgName);
        }

        // 애니메이션 클립에서 사용 중인 패키지
        var previousPackages = unit.spumPackages;
        foreach (var pkg in previousPackages)
        {
            if (pkg.SpumAnimationData.Any(a => a.HasData && a.UnitType == unit.UnitType))
            {
                usedPackageNames.Add(pkg.Name);
            }
        }

        // 사용 중인 패키지만 남기고, 마스터 데이터에서 깊은 복사로 재구성
        unit.spumPackages = spumPackages
            .Where(p => usedPackageNames.Contains(p.Name))
            .Select(p => (SpumPackage)p.Clone())
            .ToList();

        // 이전 애니메이션 설정(index, HasData) 복원
        foreach (var pkg in unit.spumPackages)
        {
            var prevPkg = previousPackages.FirstOrDefault(p => p.Name == pkg.Name);
            foreach (var clip in pkg.SpumAnimationData)
            {
                var prevClip = prevPkg?.SpumAnimationData
                    .FirstOrDefault(a => a.Name == clip.Name);
                if (prevClip != null)
                {
                    clip.index = prevClip.index;
                    clip.HasData = prevClip.HasData;
                }
                else
                {
                    clip.index = -1;
                    clip.HasData = false;
                }
            }
        }
    }

    public void NewMake()
    {
        ItemResetAll();
        EditPrefab = null;
        //UIManager._unitCode.text = UIManager.GetFileName();
        UIManager.LoadButtonSet(false);
        animationManager.CloseAnimationPanels();
        UIManager.CloseColorPick();
        animationManager.InitPreviewUnitPackage();
        ResetBody();
    }
    //프리팹 프리펩 리스트 불러오기
    public void OpenLoadData()
    {
        // 애니메이션 패널 닫기
        animationManager.CloseAnimationPanels();
        animationManager.InitializeDropdown();
        // 로드 오브젝트 캔버스 활성화

        UIManager.SetActiveLoadPanel(true);
        paginationManager.LoadPrefabs();
    }
    public bool ValidateAnimationClips(SpumAnimationClip clipData)
    {
        bool clipPathExists = true;
        
        AnimationClip LoadClip = Resources.Load<AnimationClip>(clipData.ClipPath.Replace(".anim", ""));
        
        if (LoadClip == null)
        {
            Debug.LogWarning($"Failed to load animation clip '{clipData.ClipPath}'.");
            clipPathExists = false;
        }
        return clipPathExists;
    }
    
    public List<PreviewMatchingElement> SetLegacyHorseData(){
        string PackageName = "Legacy";
        string UnitType = "Horse";
        string PartType = "Body";
        string TextureName = "Horse1";
        //패키지 구룹화
        var groupedData = spumPackages
            .SelectMany(p => p.SpumTextureData.Select(t => new { Package = p, Texture = t }))
            .Where(x => x.Texture.PartType.Equals(PartType) && x.Texture.UnitType.Equals(UnitType) && x.Package.Name.Equals(PackageName) && x.Texture.Name.Equals(TextureName))
            .GroupBy(x => new { x.Texture.PartType, x.Texture.Name, x.Texture.UnitType }) // 그룹 키
            .Select(g => new 
            { 
                Key = g.Key, 
                Items = g.ToList() 
            })
            .ToList();

        var randomGroup = groupedData[0];

        var ListElement = new List<PreviewMatchingElement>();
        foreach (var item in randomGroup.Items)
        {
            //Debug.Log($"Path: {PartType}, SubType: { item.Texture.SubType}");
            var part = new PreviewMatchingElement();
            part.UnitType = UnitType;
            part.PartType = PartType;
            part.PartSubType = item.Texture.PartSubType;
            part.Dir = "";
            part.ItemPath = item.Texture.Path;
            part.Structure = item.Texture.SubType.Equals(item.Texture.Name) ? PartType : item.Texture.SubType;
            part.MaskIndex = 0;
            part.Color = Color.white;

            ListElement.Add(part);
        }
        return ListElement;
    }

    public List<PreviewMatchingElement> DefaultData(string UnitType, string PartType, string TextureName, Color color)
    {
        string PackageName ="Legacy";
        //패키지 구룹화
        var groupedData = spumPackages
            .SelectMany(p => p.SpumTextureData.Select(t => new { Package = p, Texture = t }))
            .Where(x => x.Texture.PartType.Equals(PartType) && x.Texture.UnitType.Equals(UnitType) && x.Package.Name.Equals(PackageName) && x.Texture.Name.Equals(TextureName))
            .GroupBy(x => new { x.Texture.PartType, x.Texture.Name, x.Texture.UnitType }) // 그룹 키
            .Select(g => new 
            { 
                Key = g.Key, 
                Items = g.ToList() 
            })
            .ToList();

        var randomGroup = groupedData[0];

        var ListElement = new List<PreviewMatchingElement>();
        foreach (var item in randomGroup.Items)
        {
            //Debug.Log($"Path: {PartType}, SubType: { item.Texture.SubType}");
            var part = new PreviewMatchingElement();
            part.UnitType = UnitType;
            part.PartType = PartType;
            part.PartSubType = item.Texture.PartSubType;
            part.Dir = "";
            part.ItemPath = item.Texture.Path;
            part.Structure = item.Texture.SubType.Equals(item.Texture.Name) ? PartType : item.Texture.SubType; // item.Texture.SubType.Equals(item.Texture.Name) ? PartType : item.Texture.SubType;
            part.MaskIndex = 0;
            part.Color = color;

            ListElement.Add(part);
        }
        return ListElement;
    }
    public List<SpumTextureData> ExtractTextureData(string packageName, string unitType, string partType, string textureName)
    {
        var query = spumPackages.AsEnumerable();

        if (!string.IsNullOrEmpty(packageName))
        {
            query = query.Where(package => package.Name == packageName);
        }

        return query
            .SelectMany(package => package.SpumTextureData)
            .Where(texture =>
                string.Equals(texture.UnitType, unitType, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(texture.PartType, partType, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(texture.Name, textureName, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
    public List<SpumAnimationClip> ExtractAnimationData(string packageName, string unitType, string partType, string clipeName)
    {
        var query = spumPackages.AsEnumerable();

        if (!string.IsNullOrEmpty(packageName))
        {
            query = query.Where(package => package.Name == packageName);
        }

        return query
            .SelectMany(package => package.SpumAnimationData)
            .Where(clip => 
                clip.UnitType == unitType &&
                clip.StateType == partType &&
                clip.Name == clipeName)
            .ToList();
    }

    //Unit Delete
    public void DeleteUnit(SPUM_Prefabs prefab)
    {
        fileHandler.Delete(prefab);

        UIManager.ShowNowUnitNumber();
        UIManager.SetActiveLoadPanel(false);
        OpenLoadData();
    }
#endregion

    public Sprite LoadSpriteFromMultiple(string path, string spriteName)
    {
        Sprite[] sprites = Resources.LoadAll<Sprite>(path);

        if (sprites == null || sprites.Length == 0)
        {
            Debug.LogWarning($"No sprites found at path: {path}");
            return null;
        }

        Sprite foundSprite = System.Array.Find(sprites, sprite => sprite.name == spriteName);

        // 일치하는 spriteName이 없으면 첫 번째 항목 반환
        return foundSprite != null ? foundSprite : sprites[0];
    }


#region Scene Move

    #if UNITY_EDITOR
    public UnityEditor.SceneAsset ExportScene;
    #endif
    public string sceneName;

    public void LoadExportScene()
    {
        #if UNITY_EDITOR
        if (ExportScene != null)
        {
            string scenePath = UnityEditor.AssetDatabase.GetAssetPath(ExportScene);
            sceneName = System.IO.Path.GetFileNameWithoutExtension(scenePath);
        }
        else
        {
            Debug.LogWarning("ExportScene is not assigned.");
        }
        #endif

        if (!string.IsNullOrEmpty(sceneName))
        {
            SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
        }
        else
        {
            Debug.LogWarning("Scene name is not assigned.");
        }
    }

#endregion
}
