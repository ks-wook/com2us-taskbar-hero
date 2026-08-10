using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class SPUM_ImprovedTagManager : MonoBehaviour
{
    // 캐시된 클래스/스타일 데이터
    private static SPUM_ImprovedClassData cachedImprovedClassData;
    private static SPUM_ImprovedStyleData cachedImprovedStyleData;
    private static SPUM_ImprovedStyleData cachedStyleElementsData;
    private static SPUM_CustomStyleData cachedCustomStyleData;
    
    // 내부 저장용 변수 (ContextMenu로 로드)
    [Header("Loaded Data (Read Only)")]
    [SerializeField] private SPUM_ImprovedClassData internalClassData;
    [SerializeField] private SPUM_ImprovedStyleData internalStyleElementsData;
    [SerializeField] private SPUM_CustomStyleData internalCustomStyleData;
    
    // Public properties to access the data
    public SPUM_ImprovedClassData classData 
    { 
        get 
        {
            // 내부 데이터 우선 사용
            if (internalClassData != null)
                return internalClassData;
                
            if (cachedImprovedClassData == null)
                LoadImprovedClassData();
            return cachedImprovedClassData;
        }
        set { cachedImprovedClassData = value; internalClassData = value; }
    }
    
    public SPUM_ImprovedStyleData styleData 
    { 
        get 
        {
            if (cachedImprovedStyleData == null)
                LoadImprovedStyleData();
            return cachedImprovedStyleData;
        }
        set { cachedImprovedStyleData = value; }
    }
    
    public SPUM_CustomStyleData customStyleData 
    { 
        get 
        {
            // 내부 데이터 우선 사용
            if (internalCustomStyleData != null)
                return internalCustomStyleData;
                
            if (cachedCustomStyleData == null)
                LoadCustomStyleData();
            return cachedCustomStyleData;
        }
        set { cachedCustomStyleData = value; internalCustomStyleData = value; }
    }
    
    public SPUM_ImprovedStyleData styleElementsData 
    { 
        get 
        {
            // 내부 데이터 우선 사용
            if (internalStyleElementsData != null)
            {
                Debug.Log("[SPUM] styleElementsData: Using internal data");
                return internalStyleElementsData;
            }
                
            // 캐시된 데이터 확인
            if (cachedStyleElementsData != null)
            {
                Debug.Log("[SPUM] styleElementsData: Using cached data");
                return cachedStyleElementsData;
            }
            
            // 둘 다 없으면 동적으로 로드
            Debug.Log("[SPUM] styleElementsData: Loading data dynamically");
            LoadInternalStyleElementsData();
            
            // 로드 후 다시 확인
            return internalStyleElementsData ?? cachedStyleElementsData;
        }
        set { cachedStyleElementsData = value; internalStyleElementsData = value; }
    }
    
    private static DateTime lastClassDataLoad = DateTime.MinValue;
    private static DateTime lastStyleElementsDataLoad = DateTime.MinValue;
    private static DateTime lastCustomStyleDataLoad = DateTime.MinValue;
    private static readonly TimeSpan cacheExpiration = TimeSpan.FromMinutes(5);
    
    [System.Serializable]
    public class ImprovedCharacterDataItem
    {
        public string FileName;
        public string Part;
        public string[] Theme;
        public string Race;
        public string Gender;
        public string Type;
        public string Addon;
        public string[] Properties;
        public string[] Class;
        public string[] Style;
        
        public ImprovedCharacterDataItem()
        {
            Theme = new string[0];
            Properties = new string[0];
            Class = new string[0];
            Style = new string[0];
        }
    }
    
    
    [Header("UI Concept Reset Buttons")]
    public Button themeTogglesResetButton;
    public Button raceTogglesResetButton;
    public Button genderTogglesResetButton;
    public Button classTogglesResetButton;
    public Button styleTogglesResetButton;
    
    [Header("UI Controls")]
    public Transform ThemeContent;
    public Transform RaceContent;
    public Transform GenderContent;
    public Transform ClassContent;
    public Transform StyleContent;
    public Button generateCharacterButton;
    public Button FilterResetButton;
    public Toggle FixBodyToggleButton;
    public Toggle EmptyAllowToggleButton;
    
    [Header("UI Concept Prefab Button")]
    public Toggle ConceptPrefabButton;
    
    // Button cache for performance
    private Dictionary<string, List<SPUM_SpriteButtonST>> cachedSpriteButtons = new Dictionary<string, List<SPUM_SpriteButtonST>>();
    private bool buttonCacheValid = false;
    
    [Header("Toggle-based Filter System")]
    private Dictionary<string, List<Toggle>> filterToggles = new Dictionary<string, List<Toggle>>();
    private Dictionary<string, List<string>> filterOptions = new Dictionary<string, List<string>>();
    
    [Header("Cached Filter Options")]
    [SerializeField] private List<string> cachedThemeOptions = new List<string>();
    [SerializeField] private List<string> cachedRaceOptions = new List<string>();
    [SerializeField] private List<string> cachedGenderOptions = new List<string>();
    [SerializeField] private List<string> cachedPartOptions = new List<string>();
    [SerializeField] private bool filterOptionsCached = false;
    
    [Header("Data")]
    [SerializeField] public List<ImprovedCharacterDataItem> allCharacterData = new List<ImprovedCharacterDataItem>();
    
    // 저장된 Body 요소들 (FixBodyToggleButton용) - Head, Body, Left, Right 등 모든 Structure 포함
    private List<PreviewMatchingElement> savedBodyElements = new List<PreviewMatchingElement>();
    
    [Header("Style Filter Fallback Settings")]
    public bool enableStyleFilterFallback = false; // 전체 폴백 기능 온/오프 - 스타일 필터로 인해 파츠가 없을 때 스타일 조건을 제외하고 재검색
    
    [Header("Debug")]
    public bool verboseLogging = false;
    
    // 이벤트
    public System.Action<List<ImprovedCharacterDataItem>> OnDataLoaded;
    
    // 싱글톤 인스턴스
    private static SPUM_ImprovedTagManager _instance;
    public static SPUM_ImprovedTagManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<SPUM_ImprovedTagManager>();
                if (_instance == null)
                {
                    GameObject go = new GameObject("SPUM_ImprovedTagManager");
                    _instance = go.AddComponent<SPUM_ImprovedTagManager>();
                }
            }
            return _instance;
        }
    }
    
    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
    }
    
    void Start()
    {
        // Initialize toggle system
        StartCoroutine(InitializeToggleSystemDelayed());
        
        // 생성 버튼 이벤트 연결
        if (generateCharacterButton != null)
        {
            generateCharacterButton.onClick.AddListener(ApplyGeneratedCharacterToManager);
        }
        
        // FixBodyToggleButton 이벤트 연결
        if (FixBodyToggleButton != null)
        {
            FixBodyToggleButton.onValueChanged.AddListener(OnFixBodyToggleChanged);
        }
        if (EmptyAllowToggleButton != null)
        {
            EmptyAllowToggleButton.onValueChanged.AddListener((On) => enableStyleFilterFallback = On);
        }
        // Load class and style data
        // LoadImprovedClassData();
        // LoadImprovedStyleData();
        // LoadStyleElementsData();
        // LoadCustomStyleData();
    }
    
    /// <summary>
    /// FixBodyToggleButton 상태 변경 시 호출
    /// </summary>
    private void OnFixBodyToggleChanged(bool isOn)
    {
        if (isOn)
        {
            // 토글이 켜질 때 현재 Body 저장
            SPUM_Manager spumManager = FindFirstObjectByType<SPUM_Manager>();
            
            if (spumManager != null && spumManager.PreviewPrefab != null)
            {
                // PartType이 "Body"인 모든 요소들을 찾아서 저장 (Head, Body, Left, Right 등)
                var currentBodyElements = spumManager.PreviewPrefab.ImageElement
                    .Where(e => e.PartType.Equals("Body", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                
                savedBodyElements.Clear();
                foreach (var bodyElement in currentBodyElements)
                {
                    if (!string.IsNullOrEmpty(bodyElement.ItemPath))
                    {
                        // 각 Body 요소를 깊은 복사로 저장
                        savedBodyElements.Add(new PreviewMatchingElement
                        {
                            ItemPath = bodyElement.ItemPath,
                            PartType = bodyElement.PartType,
                            UnitType = bodyElement.UnitType,
                            PartSubType = bodyElement.PartSubType,
                            Structure = bodyElement.Structure,
                            Dir = bodyElement.Dir,
                            MaskIndex = bodyElement.MaskIndex,
                            Color = bodyElement.Color != default(Color) ? bodyElement.Color : Color.white
                        });
                    }
                }
            }
        }
        else
        {
            // 토글이 꺼질 때 저장된 Body 요소들 클리어
            savedBodyElements.Clear();
        }
    }
    
    /// <summary>
    /// 지연된 토글 시스템 초기화
    /// </summary>
    private IEnumerator InitializeToggleSystemDelayed()
    {
        yield return null;
        
        // Always use cached data, never auto-load from Google Sheets
        Debug.Log($"[SPUM] Initializing with cached data - Character data count: {allCharacterData.Count}, Filter cached: {filterOptionsCached}");
        
        // Only create filter options if they were previously cached
        if (filterOptionsCached)
        {
            // Restore filter options from cache
            filterOptions["Theme"] = new List<string>(cachedThemeOptions);
            filterOptions["Race"] = new List<string>(cachedRaceOptions);
            filterOptions["Gender"] = new List<string>(cachedGenderOptions);
            filterOptions["Part"] = new List<string>(cachedPartOptions);
            
            // Always load Class and Style from internal data
            if (internalClassData != null && internalClassData.combat_classes != null)
            {
                filterOptions["Class"] = internalClassData.combat_classes.Keys.ToList();
            }
            else
            {
                filterOptions["Class"] = new List<string>();
            }
            
            if (internalCustomStyleData != null && internalCustomStyleData.custom_styles != null)
            {
                filterOptions["Style"] = internalCustomStyleData.custom_styles.Keys.ToList();
            }
            else
            {
                filterOptions["Style"] = new List<string>();
            }
            
            Debug.Log("[SPUM] Filter options restored from cache");
        }
        else
        {
            // Initialize empty filter options
            filterOptions["Theme"] = new List<string>();
            filterOptions["Race"] = new List<string>();
            filterOptions["Gender"] = new List<string>();
            filterOptions["Part"] = new List<string>();
            filterOptions["Class"] = new List<string>();
            filterOptions["Style"] = new List<string>();
            
            Debug.LogWarning("[SPUM] No filter options cached. Load data manually via Context Menu.");
        }
        
        // Initialize toggle system
        InitializeToggleSystem();
    }
    
    /// <summary>
    /// Check if we have cached internal data
    /// </summary>
    private bool HasCachedInternalData()
    {
        return internalClassData != null || internalCustomStyleData != null || internalStyleElementsData != null;
    }
    
    /// <summary>
    /// Get cached sprite buttons by part type
    /// </summary>
    private List<SPUM_SpriteButtonST> GetCachedSpriteButtons(string partType = null)
    {
        // Refresh cache if invalid
        if (!buttonCacheValid)
        {
            RefreshButtonCache();
        }
        
        if (string.IsNullOrEmpty(partType))
        {
            // Return all buttons
            var allButtons = new List<SPUM_SpriteButtonST>();
            foreach (var kvp in cachedSpriteButtons)
            {
                allButtons.AddRange(kvp.Value);
            }
            return allButtons;
        }
        
        // Return buttons for specific part type
        if (cachedSpriteButtons.ContainsKey(partType))
        {
            return new List<SPUM_SpriteButtonST>(cachedSpriteButtons[partType]);
        }
        
        return new List<SPUM_SpriteButtonST>();
    }
    
    /// <summary>
    /// Refresh the button cache
    /// </summary>
    private void RefreshButtonCache()
    {
        cachedSpriteButtons.Clear();
        
        var allButtons = FindObjectsByType<SPUM_SpriteButtonST>(FindObjectsSortMode.None);
        foreach (var button in allButtons)
        {
            if (!cachedSpriteButtons.ContainsKey(button.PartType))
            {
                cachedSpriteButtons[button.PartType] = new List<SPUM_SpriteButtonST>();
            }
            cachedSpriteButtons[button.PartType].Add(button);
        }
        
        buttonCacheValid = true;
        Debug.Log($"[SPUM] Button cache refreshed: {allButtons.Length} buttons cached");
    }
    
    /// <summary>
    /// Invalidate button cache
    /// </summary>
    public void InvalidateButtonCache()
    {
        buttonCacheValid = false;
    }
    
    /// <summary>
    /// Create filter options from internal data without loading from Google Sheets
    /// </summary>
    private void CreateFilterOptionsFromInternalData()
    {
        filterOptions.Clear();
        
        // Check if we have cached filter options
        if (filterOptionsCached && cachedThemeOptions != null && cachedRaceOptions != null && 
            cachedGenderOptions != null && cachedPartOptions != null)
        {
            // Use cached filter options for performance
            filterOptions["Theme"] = new List<string>(cachedThemeOptions);
            filterOptions["Race"] = new List<string>(cachedRaceOptions);
            filterOptions["Gender"] = new List<string>(cachedGenderOptions);
            filterOptions["Part"] = new List<string>(cachedPartOptions);
            Debug.Log($"[SPUM] Using cached filter options - Theme: {cachedThemeOptions.Count}, Race: {cachedRaceOptions.Count}, Gender: {cachedGenderOptions.Count}, Part: {cachedPartOptions.Count}");
        }
        else
        {
            // Generate filter options from allCharacterData
            if (allCharacterData.Count == 0)
            {
                Debug.LogWarning("[SPUM] allCharacterData is empty. Theme, Race, Gender filters will be empty.");
            }
            
            // Theme - Extract from allCharacterData
            var themes = new HashSet<string>();
            foreach (var item in allCharacterData)
            {
                if (item.Theme != null)
                {
                    foreach (var theme in item.Theme)
                    {
                        if (!string.IsNullOrEmpty(theme))
                            themes.Add(theme);
                    }
                }
            }
            filterOptions["Theme"] = themes.ToList();
            cachedThemeOptions = new List<string>(filterOptions["Theme"]);
            
            // Race - Extract from allCharacterData
            var races = new HashSet<string>();
            foreach (var item in allCharacterData)
            {
                if (!string.IsNullOrEmpty(item.Race))
                    races.Add(item.Race);
            }
            filterOptions["Race"] = races.ToList();
            cachedRaceOptions = new List<string>(filterOptions["Race"]);
            
            // Gender - Extract from allCharacterData
            var genders = new HashSet<string>();
            foreach (var item in allCharacterData)
            {
                if (!string.IsNullOrEmpty(item.Gender))
                    genders.Add(item.Gender);
            }
            filterOptions["Gender"] = genders.ToList();
            cachedGenderOptions = new List<string>(filterOptions["Gender"]);
            
            // Part - Extract from allCharacterData
            var parts = new HashSet<string>();
            foreach (var item in allCharacterData)
            {
                if (!string.IsNullOrEmpty(item.Part))
                    parts.Add(item.Part);
            }
            filterOptions["Part"] = parts.ToList();
            cachedPartOptions = new List<string>(filterOptions["Part"]);
            
            // Mark as cached if we have data
            if (allCharacterData.Count > 0)
            {
                filterOptionsCached = true;
                #if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(this);
                #endif
            }
            
            Debug.Log($"[SPUM] Filter options created and cached - Theme: {filterOptions["Theme"].Count}, Race: {filterOptions["Race"].Count}, Gender: {filterOptions["Gender"].Count}, Part: {filterOptions["Part"].Count}");
        }
        
        // Class - From internal class data (always regenerate as it's from internal data)
        if (internalClassData != null && internalClassData.combat_classes != null)
        {
            filterOptions["Class"] = internalClassData.combat_classes.Keys.ToList();
            Debug.Log($"[SPUM] Loaded {filterOptions["Class"].Count} classes from internal data");
        }
        else
        {
            filterOptions["Class"] = new List<string>();
        }
        
        // Style - From internal custom style data (always regenerate as it's from internal data)
        if (internalCustomStyleData != null && internalCustomStyleData.custom_styles != null)
        {
            filterOptions["Style"] = internalCustomStyleData.custom_styles.Keys.ToList();
            Debug.Log($"[SPUM] Loaded {filterOptions["Style"].Count} styles from internal data");
        }
        else
        {
            filterOptions["Style"] = new List<string>();
        }
    }
    
    /// <summary>
    /// Load improved class data from JSON
    /// </summary>
    public void LoadImprovedClassData()
    {
        if (cachedImprovedClassData != null && DateTime.Now - lastClassDataLoad < cacheExpiration)
        {
            if (verboseLogging) Debug.Log("[SPUM] Using cached improved class data");
            return;
        }
        
        TextAsset jsonAsset = Resources.Load<TextAsset>("SPUM_Class_Tags_Improved");
        if (jsonAsset != null)
        {
            cachedImprovedClassData = SPUM_ImprovedClassData.FromJson(jsonAsset.text);
            lastClassDataLoad = DateTime.Now;
            Debug.Log($"[SPUM] Loaded {cachedImprovedClassData.combat_classes.Count} improved combat classes");
        }
        else
        {
            Debug.LogError("[SPUM] Failed to load SPUM_Class_Tags_Improved.json");
            cachedImprovedClassData = new SPUM_ImprovedClassData();
        }
    }
    
    /// <summary>
    /// Load improved style data from JSON (kept for backward compatibility)
    /// </summary>
    public void LoadImprovedStyleData()
    {
        // This is now just for backward compatibility
        // The actual custom styles are loaded in LoadCustomStyleData
        cachedImprovedStyleData = new SPUM_ImprovedStyleData();
    }
    
    /// <summary>
    /// Load style elements data from JSON
    /// </summary>
    public void LoadStyleElementsData()
    {
        if (cachedStyleElementsData != null && DateTime.Now - lastStyleElementsDataLoad < cacheExpiration)
        {
            if (verboseLogging) Debug.Log("[SPUM] Using cached style elements data");
            return;
        }
        
        TextAsset jsonAsset = Resources.Load<TextAsset>("SPUM_Style_Elements");
        if (jsonAsset != null)
        {
            cachedStyleElementsData = SPUM_ImprovedStyleData.FromJson(jsonAsset.text);
            lastStyleElementsDataLoad = DateTime.Now;
            // Count total elements across all categories
            int elementCount = 0;
            if (cachedStyleElementsData.categories != null)
            {
                foreach (var category in cachedStyleElementsData.categories.Values)
                {
                    elementCount += category.styles.Count;
                }
            }
            Debug.Log($"[SPUM] Loaded {elementCount} style elements across {cachedStyleElementsData.categories?.Count ?? 0} categories");
        }
        else
        {
            Debug.LogError("[SPUM] Failed to load SPUM_Style_Elements.json");
            cachedStyleElementsData = new SPUM_ImprovedStyleData();
        }
    }
    
    /// <summary>
    /// Load custom style data from JSON
    /// </summary>
    public void LoadCustomStyleData()
    {
        if (cachedCustomStyleData != null && DateTime.Now - lastCustomStyleDataLoad < cacheExpiration)
        {
            if (verboseLogging) Debug.Log("[SPUM] Using cached custom style data");
            return;
        }
        
        TextAsset jsonAsset = Resources.Load<TextAsset>("SPUM_Custom_Styles");
        if (jsonAsset != null)
        {
            cachedCustomStyleData = SPUM_CustomStyleData.FromJson(jsonAsset.text);
            lastCustomStyleDataLoad = DateTime.Now;
            Debug.Log($"[SPUM] Loaded {cachedCustomStyleData.custom_styles?.Count ?? 0} custom styles");
            if (cachedCustomStyleData.custom_styles != null)
            {
                foreach (var style in cachedCustomStyleData.custom_styles)
                {
                    Debug.Log($"  - {style.Key}: {style.Value.name}");
                }
            }
        }
        else
        {
            Debug.LogError("[SPUM] Failed to load SPUM_Custom_Styles.json for custom styles");
            cachedCustomStyleData = new SPUM_CustomStyleData();
        }
    }
    
    /// <summary>
    /// 내부 클래스 데이터 로드
    /// </summary>
    public void LoadInternalClassData()
    {
        TextAsset jsonAsset = Resources.Load<TextAsset>("SPUM_Class_Tags_Improved");
        if (jsonAsset != null)
        {
            internalClassData = SPUM_ImprovedClassData.FromJson(jsonAsset.text);
        }
        else
        {
            Debug.LogError("[SPUM] Failed to load SPUM_Class_Tags_Improved.json");
            internalClassData = new SPUM_ImprovedClassData();
        }
    }
    
    /// <summary>
    /// 내부 스타일 엘리먼트 데이터 로드
    /// </summary>
    public void LoadInternalStyleElementsData()
    {
        Debug.Log("[SPUM] LoadInternalStyleElementsData called - attempting to load SPUM_Style_Elements.json");
        
        TextAsset jsonAsset = Resources.Load<TextAsset>("SPUM_Style_Elements");
        if (jsonAsset != null)
        {
            Debug.Log($"[SPUM] Successfully loaded JSON asset, size: {jsonAsset.text.Length} characters");
            
            try
            {
                internalStyleElementsData = SPUM_ImprovedStyleData.FromJson(jsonAsset.text);
                
                if (internalStyleElementsData == null)
                {
                    Debug.LogError("[SPUM] FromJson returned null");
                    internalStyleElementsData = new SPUM_ImprovedStyleData();
                    return;
                }
                
                int elementCount = 0;
                if (internalStyleElementsData.categories != null)
                {
                    foreach (var category in internalStyleElementsData.categories.Values)
                    {
                        elementCount += category.styles.Count;
                    }
                    Debug.Log($"[SPUM] Loaded {elementCount} style elements across {internalStyleElementsData.categories.Count} categories to internal storage");
                    
                    // Log category names for debugging
                    foreach (var cat in internalStyleElementsData.categories.Keys)
                    {
                        Debug.Log($"[SPUM] Category loaded: {cat}");
                    }
                }
                else
                {
                    Debug.LogWarning("[SPUM] Loaded data but categories is null");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[SPUM] Error parsing JSON: {e.Message}");
                internalStyleElementsData = new SPUM_ImprovedStyleData();
            }
        }
        else
        {
            Debug.LogError("[SPUM] Failed to load SPUM_Style_Elements.json - Resources.Load returned null");
            internalStyleElementsData = new SPUM_ImprovedStyleData();
        }
    }
    
    /// <summary>
    /// 내부 커스텀 스타일 데이터 로드
    /// </summary>
    public void LoadInternalCustomStyleData()
    {
        TextAsset jsonAsset = Resources.Load<TextAsset>("SPUM_Custom_Styles");
        if (jsonAsset != null)
        {
            internalCustomStyleData = SPUM_CustomStyleData.FromJson(jsonAsset.text);
            if (internalCustomStyleData.custom_styles != null)
            {
                foreach (var style in internalCustomStyleData.custom_styles)
                {
                    Debug.Log($"  - {style.Key}: {style.Value.name}");
                }
            }
        }
        else
        {
            Debug.LogError("[SPUM] Failed to load SPUM_Custom_Styles.json for custom styles");
            internalCustomStyleData = new SPUM_CustomStyleData();
        }
    }
    
    /// <summary>
    /// 데이터에서 필터 옵션 생성
    /// </summary>
    private void CreateFilterOptionsFromData()
    {
        filterOptions.Clear();
        
        // Invalidate filter cache when creating from Google Sheets data
        filterOptionsCached = false;
        
        // Theme
        var themes = new HashSet<string>();
        foreach (var item in allCharacterData)
        {
            foreach (var theme in item.Theme)
                themes.Add(theme);
        }
        filterOptions["Theme"] = themes.ToList();
        
        // Race
        var races = new HashSet<string>();
        foreach (var item in allCharacterData)
        {
            if (!string.IsNullOrEmpty(item.Race))
                races.Add(item.Race);
        }
        filterOptions["Race"] = races.ToList();
        
        // Gender
        var genders = new HashSet<string>();
        foreach (var item in allCharacterData)
        {
            if (!string.IsNullOrEmpty(item.Gender))
                genders.Add(item.Gender);
        }
        filterOptions["Gender"] = genders.ToList();
        
        // Class - Load from JSON file
        var classes = new HashSet<string>();
        var classData = GetClassData();
        if (classData != null && classData.combat_classes != null)
        {
            foreach (var className in classData.combat_classes.Keys)
            {
                classes.Add(className);
            }
        }
        filterOptions["Class"] = classes.ToList();
        
        // Style - Load custom styles from JSON file
        var styles = new HashSet<string>();
        var customStyleData = GetCustomStyleData();
        Debug.Log($"[SPUM] CreateFilterOptionsFromData - customStyleData: {customStyleData != null}, custom_styles: {customStyleData?.custom_styles?.Count ?? 0}");
        if (customStyleData != null && customStyleData.custom_styles != null)
        {
            foreach (var styleName in customStyleData.custom_styles.Keys)
            {
                styles.Add(styleName);
                Debug.Log($"[SPUM] Added style to filter: {styleName}");
            }
        }
        filterOptions["Style"] = styles.ToList();
        Debug.Log($"[SPUM] Total styles in filter options: {filterOptions["Style"].Count}");
        
        // Cache the filter options for faster initialization
        cachedThemeOptions = new List<string>(filterOptions["Theme"]);
        cachedRaceOptions = new List<string>(filterOptions["Race"]);
        cachedGenderOptions = new List<string>(filterOptions["Gender"]);
        cachedPartOptions = new List<string>(filterOptions.ContainsKey("Part") ? filterOptions["Part"] : new List<string>());
        filterOptionsCached = true;
        
        Debug.Log($"[SPUM] Filter options cached for next session");
    }
    
    /// <summary>
    /// 토글 시스템 초기화
    /// </summary>
    private void InitializeToggleSystem()
    {
        // Initialize toggle dictionaries
        filterToggles["Theme"] = new List<Toggle>();
        filterToggles["Race"] = new List<Toggle>();
        filterToggles["Gender"] = new List<Toggle>();
        filterToggles["Class"] = new List<Toggle>();
        filterToggles["Style"] = new List<Toggle>();
        
        // Create toggles for each category
        if (ThemeContent != null && filterOptions.ContainsKey("Theme"))
            CreateCategoryToggles("Theme", ThemeContent, filterOptions["Theme"].ToArray());
        if (RaceContent != null && filterOptions.ContainsKey("Race"))
            CreateCategoryToggles("Race", RaceContent, filterOptions["Race"].ToArray());
        if (GenderContent != null && filterOptions.ContainsKey("Gender"))
            CreateCategoryToggles("Gender", GenderContent, filterOptions["Gender"].ToArray());
        if (ClassContent != null && filterOptions.ContainsKey("Class"))
            CreateCategoryToggles("Class", ClassContent, filterOptions["Class"].ToArray());
        if (StyleContent != null && filterOptions.ContainsKey("Style"))
            CreateCategoryToggles("Style", StyleContent, filterOptions["Style"].ToArray());
        
        // Connect reset buttons
        if (themeTogglesResetButton != null)
            themeTogglesResetButton.onClick.AddListener(() => ResetCategoryToggles("Theme"));
        if (raceTogglesResetButton != null)
            raceTogglesResetButton.onClick.AddListener(() => ResetCategoryToggles("Race"));
        if (genderTogglesResetButton != null)
            genderTogglesResetButton.onClick.AddListener(() => ResetCategoryToggles("Gender"));
        if (classTogglesResetButton != null)
            classTogglesResetButton.onClick.AddListener(() => ResetCategoryToggles("Class"));
        if (styleTogglesResetButton != null)
            styleTogglesResetButton.onClick.AddListener(() => ResetCategoryToggles("Style"));
        
        // Connect filter reset button
        if (FilterResetButton != null)
            FilterResetButton.onClick.AddListener(ResetAllFilters);
    }
    
    /// <summary>
    /// 카테고리별 토글 생성 (최적화 버전 - 버튼 재사용)
    /// </summary>
    private void CreateCategoryToggles(string category, Transform contentParent, string[] options)
    {
        if (contentParent == null || ConceptPrefabButton == null) return;
        
        // Get existing toggles
        List<GameObject> existingToggles = new List<GameObject>();
        foreach (Transform child in contentParent)
        {
            existingToggles.Add(child.gameObject);
        }
        
        filterToggles[category].Clear();
        
        // Calculate required count (options + 1 for ALL)
        int requiredCount = options.Length + 1;
        int existingCount = existingToggles.Count;
        
        // Reuse or create ALL toggle first
        GameObject allToggleObj;
        if (existingCount > 0)
        {
            allToggleObj = existingToggles[0];
            allToggleObj.SetActive(true);
        }
        else
        {
            allToggleObj = Instantiate(ConceptPrefabButton.gameObject, contentParent);
        }
        
        Toggle allToggle = allToggleObj.GetComponent<Toggle>();
        Text allLabel = allToggleObj.GetComponentInChildren<Text>();
        if (allLabel != null) allLabel.text = "ALL";
        
        // Remove existing listener before adding new one
        allToggle.onValueChanged.RemoveAllListeners();
        allToggle.isOn = true; // Default: ALL selected
        filterToggles[category].Add(allToggle);
        
        // Reuse or create toggles for each option
        int toggleIndex = 1;
        foreach (string option in options)
        {
            if (string.IsNullOrEmpty(option)) continue;
            
            GameObject toggleObj;
            if (toggleIndex < existingCount)
            {
                // Reuse existing toggle
                toggleObj = existingToggles[toggleIndex];
                toggleObj.SetActive(true);
            }
            else
            {
                // Create new toggle
                toggleObj = Instantiate(ConceptPrefabButton.gameObject, contentParent);
            }
            
            Toggle toggle = toggleObj.GetComponent<Toggle>();
            Text label = toggleObj.GetComponentInChildren<Text>();
            if (label != null) label.text = option;
            
            // Remove existing listener before adding new one
            toggle.onValueChanged.RemoveAllListeners();
            toggle.isOn = false;
            filterToggles[category].Add(toggle);
            
            toggleIndex++;
        }
        
        // Hide unused toggles instead of destroying them
        for (int i = toggleIndex; i < existingCount; i++)
        {
            existingToggles[i].SetActive(false);
        }
        
        // Store options for later use
        filterOptions[category] = new List<string>(options);
        filterOptions[category].Insert(0, "ALL");
        
        // Add listeners to handle ALL toggle behavior
        for (int i = 0; i < filterToggles[category].Count; i++)
        {
            int index = i;
            Toggle toggle = filterToggles[category][index];
            toggle.onValueChanged.AddListener((isOn) => 
            {
                OnToggleValueChanged(category, index, isOn);
            });
        }
    }
    
    /// <summary>
    /// Handle toggle value changes with ALL logic
    /// </summary>
    private void OnToggleValueChanged(string category, int toggleIndex, bool isOn)
    {
        if (toggleIndex == 0) // ALL toggle
        {
            if (isOn)
            {
                // When ALL is selected, deselect all others
                for (int i = 1; i < filterToggles[category].Count; i++)
                {
                    filterToggles[category][i].isOn = false;
                }
            }
            else
            {
                // ALL 버튼을 클릭했을 때 꺼지려고 하면 다시 켜기
                filterToggles[category][0].SetIsOnWithoutNotify(true);
            }
        }
        else // Regular toggle
        {
            if (isOn)
            {
                // When any regular toggle is selected, deselect ALL
                filterToggles[category][0].SetIsOnWithoutNotify(false);
            }
            else // 토글이 OFF될 때
            {
                // 모든 일반 토글이 OFF인지 확인
                bool anySelected = false;
                for (int i = 1; i < filterToggles[category].Count; i++)
                {
                    if (filterToggles[category][i].isOn)
                    {
                        anySelected = true;
                        break;
                    }
                }
                
                // 모든 토글이 OFF면 ALL 자동 활성화
                if (!anySelected)
                {
                    filterToggles[category][0].isOn = true;
                }
            }
        }
        
        // Update dependent categories
        UpdateDependentCategories(category);
        LogFilterStatus(category);
    }
    
    /// <summary>
    /// Update dependent categories based on current selections
    /// </summary>
    private void UpdateDependentCategories(string category)
    {
        switch (category)
        {
            case "Theme":
                UpdateToggleOptions("Race", GetFilteredRaces());
                UpdateToggleOptions("Gender", GetFilteredGenders());
                // 클래스와 스타일은 상위 필터에 영향받지 않음
                break;
            case "Race":
                UpdateToggleOptions("Gender", GetFilteredGenders());
                // 클래스와 스타일은 상위 필터에 영향받지 않음
                break;
            case "Gender":
                // 클래스와 스타일은 상위 필터에 영향받지 않음
                break;
            case "Class":
                // 스타일은 클래스 선택에도 영향받지 않음
                break;
        }
    }
    
    /// <summary>
    /// Update toggle options for a category while preserving selections
    /// </summary>
    private void UpdateToggleOptions(string category, string[] newOptions)
    {
        if (!filterToggles.ContainsKey(category)) return;
        
        // Save current selections
        var currentSelections = GetSelectedToggleValues(category);
        
        // Get the content parent
        Transform contentParent = null;
        switch (category)
        {
            case "Race": contentParent = RaceContent; break;
            case "Gender": contentParent = GenderContent; break;
            case "Class": contentParent = ClassContent; break;
            case "Style": contentParent = StyleContent; break;
        }
        
        if (contentParent == null) return;
        
        // Recreate toggles with new options
        CreateCategoryToggles(category, contentParent, newOptions);
        
        // Restore valid selections
        var validSelections = currentSelections.Where(s => newOptions.Contains(s)).ToList();
        if (validSelections.Count > 0)
        {
            SetSelectedToggleValues(category, validSelections);
        }
    }
    
    /// <summary>
    /// Get selected values from toggles for a category
    /// </summary>
    private List<string> GetSelectedToggleValues(string category)
    {
        var selected = new List<string>();
        if (!filterToggles.ContainsKey(category)) return selected;
        
        for (int i = 0; i < filterToggles[category].Count; i++)
        {
            if (filterToggles[category][i].isOn && i > 0) // Skip ALL toggle at index 0
            {
                if (i - 1 < filterOptions[category].Count - 1) // Adjust for ALL being at index 0
                {
                    selected.Add(filterOptions[category][i]);
                }
            }
        }
        
        return selected;
    }
    
    /// <summary>
    /// Set selected toggle values for a category
    /// </summary>
    private void SetSelectedToggleValues(string category, List<string> values)
    {
        if (!filterToggles.ContainsKey(category)) return;
        
        // First, deselect ALL
        if (filterToggles[category].Count > 0)
            filterToggles[category][0].isOn = false;
        
        // Then set the specific values
        for (int i = 1; i < filterOptions[category].Count; i++)
        {
            if (values.Contains(filterOptions[category][i]))
            {
                if (i < filterToggles[category].Count)
                    filterToggles[category][i].isOn = true;
            }
        }
    }
    
    /// <summary>
    /// Get filtered races based on current theme selection
    /// </summary>
    private string[] GetFilteredRaces()
    {
        var selectedThemes = GetSelectedToggleValues("Theme");
        var filteredData = GetFilteredItems(
            selectedThemes: selectedThemes
        );
        
        var races = new HashSet<string>();
        foreach (var item in filteredData)
        {
            if (!string.IsNullOrEmpty(item.Race))
                races.Add(item.Race);
        }
        
        return races.ToArray();
    }
    
    /// <summary>
    /// Get filtered genders based on current selections
    /// </summary>
    private string[] GetFilteredGenders()
    {
        var selectedThemes = GetSelectedToggleValues("Theme");
        var selectedRaces = GetSelectedToggleValues("Race");
        var filteredData = GetFilteredItems(
            selectedThemes: selectedThemes,
            selectedRaces: selectedRaces
        );
        
        var genders = new HashSet<string>();
        foreach (var item in filteredData)
        {
            if (!string.IsNullOrEmpty(item.Gender))
                genders.Add(item.Gender);
        }
        
        return genders.ToArray();
    }
    
    /// <summary>
    /// Get filtered classes based on current selections
    /// </summary>
    private string[] GetFilteredClasses()
    {
        // 클래스는 항상 JSON 파일에서 정의된 항목만 반환
        var classData = GetClassData();
        if (classData != null && classData.combat_classes != null)
        {
            return classData.combat_classes.Keys.ToArray();
        }
        return new string[0];
    }
    
    /// <summary>
    /// Get filtered styles based on current selections
    /// </summary>
    private string[] GetFilteredStyles()
    {
        // 커스텀 스타일을 JSON 파일에서 가져오기
        var customStyleData = GetCustomStyleData();
        var styles = new HashSet<string>();
        
        if (customStyleData != null && customStyleData.custom_styles != null)
        {
            foreach (var styleName in customStyleData.custom_styles.Keys)
            {
                styles.Add(styleName);
            }
        }
        
        return styles.ToArray();
    }
    
    /// <summary>
    /// Get filtered data based on selections
    /// [DEPRECATED] Use GetFilteredItems instead for consistent filtering with ImprovedTagWindow
    /// </summary>
    [System.Obsolete("Use GetFilteredItems instead for consistent filtering logic")]
    private List<ImprovedCharacterDataItem> GetFilteredData(
        List<string> themes, List<string> races, List<string> genders,
        List<string> types, List<string> classes, List<string> styles)
    {
        var filtered = allCharacterData.AsEnumerable();
        
        // Theme filter
        if (themes != null && themes.Count > 0)
        {
            filtered = filtered.Where(item => item.Theme.Any(t => themes.Contains(t)));
        }
        
        // Race filter
        if (races != null && races.Count > 0)
        {
            filtered = filtered.Where(item => races.Contains(item.Race));
        }
        
        // Gender filter
        if (genders != null && genders.Count > 0)
        {
            filtered = filtered.Where(item => genders.Contains(item.Gender));
        }
        
        // Class filter - 개선된 필터링 로직 (모든 태그 포함 후 네거티브 제외)
        if (classes != null && classes.Count > 0)
        {
            var classData = GetClassData();
            if (classData != null && classData.combat_classes != null)
            {
                filtered = filtered.Where(item =>
                {
                    // 선택된 클래스 중 하나라도 매치되는지 확인
                    foreach (var className in classes)
                    {
                        if (!classData.combat_classes.ContainsKey(className))
                            continue;
                            
                        var combatClass = classData.combat_classes[className];
                        
                        // Type 체크 - 허용된 타입이거나 비어있어야 하고, 네거티브에 없어야 함
                        bool typeOk = string.IsNullOrEmpty(item.Type) || 
                                     combatClass.type.Contains(item.Type);
                        bool typeNotNegative = string.IsNullOrEmpty(item.Type) ||
                                             !combatClass.type_negative.Contains(item.Type);
                        
                        if (!typeOk || !typeNotNegative)
                            continue;
                        
                        // Class 태그 체크 - 클래스 태그 중 하나를 가지거나 클래스 이름 자체를 가져야 함
                        bool hasClassTag = item.Class.Any(c => combatClass.class_tags.Contains(c)) ||
                                         item.Class.Contains(className);
                        
                        if (!hasClassTag)
                            continue;
                        
                        // Negative class 태그 체크 - 네거티브 클래스 태그를 가지면 안됨
                        bool hasNegativeClass = item.Class.Any(c => combatClass.class_negative.Contains(c));
                        
                        if (hasNegativeClass)
                            continue;
                        
                        // Style 태그 체크 (클래스에 style이 정의되어 있다면)
                        if (combatClass.style != null && combatClass.style.Count > 0)
                        {
                            bool hasStyleMatch = item.Style != null && 
                                               item.Style.Any(s => combatClass.style.Contains(s));
                            if (!hasStyleMatch)
                                continue;
                        }
                        
                        // 모든 조건을 통과하면 이 아이템은 해당 클래스에 적합
                        return true;
                    }
                    
                    // 어떤 클래스에도 매치되지 않으면 제외
                    return false;
                });
            }
            else
            {
                // Fallback to simple class matching if JSON data not available
                filtered = filtered.Where(item => item.Class.Any(c => classes.Contains(c)));
            }
        }
        
        // Style filter - 개선된 필터링 로직 (elements 태그 활용 및 네거티브 제외)
        if (styles != null && styles.Count > 0)
        {
            var customStyleData = GetCustomStyleData();
            
            filtered = filtered.Where(item =>
            {
                // 스타일이 없는 아이템은 기본적으로 포함
                if (item.Style == null || item.Style.Length == 0)
                    return true;
                
                // 선택된 스타일 중 하나라도 매치되는지 확인
                foreach (var selectedStyle in styles)
                {
                    if (customStyleData != null && customStyleData.custom_styles.ContainsKey(selectedStyle))
                    {
                        var styleInfo = customStyleData.custom_styles[selectedStyle];
                        
                        // Elements 태그 체크 - 스타일의 elements 중 하나라도 매치되거나 스타일 이름 자체가 매치
                        bool hasElement = false;
                        if (styleInfo.elements != null && styleInfo.elements.Count > 0)
                        {
                            hasElement = item.Style.Any(itemStyle => 
                                styleInfo.elements.Contains(itemStyle) || 
                                itemStyle.Equals(selectedStyle, StringComparison.OrdinalIgnoreCase));
                        }
                        else
                        {
                            // elements가 없으면 스타일 이름만 체크
                            hasElement = item.Style.Contains(selectedStyle);
                        }
                        
                        if (!hasElement)
                            continue;
                        
                        // Negative 태그 체크 제거 - SPUM_Style_Elements.json에서 제거됨
                        
                        // Required parts 체크 - 스타일이 특정 파츠를 요구한다면 확인
                        if (styleInfo.required_parts != null && styleInfo.required_parts.Count > 0)
                        {
                            if (!styleInfo.required_parts.Contains(item.Part))
                                continue;
                        }
                        
                        // 모든 조건을 통과하면 이 아이템은 해당 스타일에 적합
                        return true;
                    }
                    else
                    {
                        // 커스텀 스타일 데이터가 없으면 단순 이름 매치
                        if (item.Style.Contains(selectedStyle))
                            return true;
                    }
                }
                
                // 어떤 스타일에도 매치되지 않으면 제외
                return false;
            });
        }
        
        return filtered.ToList();
    }
    
    /// <summary>
    /// Reset toggles for a specific category
    /// </summary>
    private void ResetCategoryToggles(string category)
    {
        if (!filterToggles.ContainsKey(category)) return;
        
        // Select ALL toggle
        if (filterToggles[category].Count > 0)
        {
            filterToggles[category][0].isOn = true;
        }
        
        // This will automatically deselect others due to OnToggleValueChanged logic
    }
    
    /// <summary>
    /// Reset all filters
    /// </summary>
    private void ResetAllFilters()
    {
        ResetCategoryToggles("Theme");
        ResetCategoryToggles("Race");
        ResetCategoryToggles("Gender");
        ResetCategoryToggles("Class");
        ResetCategoryToggles("Style");
    }
    
    /// <summary>
    /// Log filter status for debugging
    /// </summary>
    private void LogFilterStatus(string category)
    {
        if (!verboseLogging) return;
        
        var selected = GetSelectedToggleValues(category);
        if (selected.Count == 0)
        {
            Debug.Log($"[Filter] {category}: ALL");
        }
        else
        {
            Debug.Log($"[Filter] {category}: {string.Join(", ", selected)}");
        }
    }
    
    /// <summary>
    /// Apply generated character to SPUM_Manager
    /// </summary>
    private void ApplyGeneratedCharacterToManager()
    {
        // SPUM_Manager 참조 가져오기
        SPUM_Manager spumManager = FindFirstObjectByType<SPUM_Manager>();
        if (spumManager == null)
        {
            Debug.LogError("[SPUM] SPUM_Manager not found in scene!");
            return;
        }
        
        // Lock된 파츠들을 먼저 수집 (ItemResetAll 이전에)
        var lockedParts = new Dictionary<string, PreviewMatchingElement>();
        if (spumManager.PreviewPrefab != null)
        {
            // 모든 파츠 타입 확인
            var allPartTypes = spumManager.PreviewPrefab.ImageElement
                .Select(e => e.PartType)
                .Distinct()
                .ToList();
            
            foreach (var partType in allPartTypes)
            {
                var partButtons = GetCachedSpriteButtons(partType);
                bool isPartLocked = partButtons.Any(b => b.IsSpriteFixed);// && b.IsActive);
                
                if (isPartLocked)
                {
                    var currentElement = spumManager.PreviewPrefab.ImageElement
                        .FirstOrDefault(e => e.PartType.Equals(partType, StringComparison.OrdinalIgnoreCase));
                    
                    if (currentElement != null)
                    {
                        // Lock된 버튼의 색상 정보 가져오기
                        var lockedButton = partButtons.FirstOrDefault(b => b.IsSpriteFixed && b.IsActive);
                        Color buttonColor = lockedButton != null ? lockedButton.PartSpriteColor : Color.white;
                        
                        lockedParts[partType] = new PreviewMatchingElement
                        {
                            ItemPath = currentElement.ItemPath,
                            PartType = currentElement.PartType,
                            UnitType = currentElement.UnitType,
                            PartSubType = currentElement.PartSubType,
                            Structure = currentElement.Structure,
                            Dir = currentElement.Dir,
                            MaskIndex = currentElement.MaskIndex,
                            Color = buttonColor
                        };
                    }
                }
            }
        }
        
        // 버튼 색상 및 활성화 상태 정보 저장
        var savedButtonStates = new Dictionary<(string PartType, string UnitType, string Direction), (bool IsActive, Color Color)>();
        var buttons = GetCachedSpriteButtons();
        
        foreach (var button in buttons)
        {
            if (button.IsActive)
            {
                var key = (button.PartType, button.UnitType, button.Direction ?? "");
                savedButtonStates[key] = (button.IsActive, button.PartSpriteColor);
            }
        }
        
        // FixBodyToggleButton이 켜져있고 savedBodyElements가 비어있으면 현재 Body 정보 저장
        if (FixBodyToggleButton != null && FixBodyToggleButton.isOn && savedBodyElements.Count == 0 && spumManager.PreviewPrefab != null)
        {
            var currentBodyElements = spumManager.PreviewPrefab.ImageElement
                .Where(e => e.PartType.Equals("Body", StringComparison.OrdinalIgnoreCase))
                .ToList();
            
            foreach (var bodyElement in currentBodyElements)
            {
                if (!string.IsNullOrEmpty(bodyElement.ItemPath))
                {
                    // Deep copy of body element
                    savedBodyElements.Add(new PreviewMatchingElement
                    {
                        ItemPath = bodyElement.ItemPath,
                        PartType = bodyElement.PartType,
                        UnitType = bodyElement.UnitType,
                        PartSubType = bodyElement.PartSubType,
                        Structure = bodyElement.Structure,
                        Dir = bodyElement.Dir,
                        MaskIndex = bodyElement.MaskIndex,
                        Color = bodyElement.Color != default(Color) ? bodyElement.Color : Color.white
                    });
                }
            }
        }
        
        // Unit 타입 아이템만 초기화
        ResetUnitTypeItemsOnly(spumManager);
        
        // 필터에 따라 캐릭터 생성
        var characterData = GenerateRandomCharacter();
        if (characterData == null || characterData.Count == 0)
        {
            Debug.LogError("[SPUM] Failed to generate character. No parts found with current filters.");
            return;
        }
        
        // 생성된 데이터를 PreviewMatchingElement 리스트로 변환
        var previewElements = ConvertToPreviewMatchingElements(characterData);
        
        if (previewElements.Count > 0)
        {
            // 무기 요소 확인
            var weaponElements = previewElements.Where(e => e.PartType == "Weapons").ToList();
            
            // 헬멧이 있는 경우 헤어의 마스크 플래그 설정
            bool hasHelmet = previewElements.Any(e => e.PartType == "Helmet");
            if (hasHelmet)
            {
                // 헤어 요소들의 마스크 인덱스 설정
                var hairElements = previewElements.Where(e => e.PartType == "Hair").ToList();
                foreach (var hair in hairElements)
                {
                    hair.MaskIndex = 1; // 마스크 활성화
                }
            }
            
            // Lock된 파츠들은 이미 위에서 수집했으므로 previewElements에서 제거만 하면 됨
            foreach (var lockedPart in lockedParts)
            {
                previewElements.RemoveAll(e => e.PartType.Equals(lockedPart.Key, StringComparison.OrdinalIgnoreCase));
            }
            
            // FixBodyToggleButton이 켜져있고 저장된 Body가 있으면 처리
            if (FixBodyToggleButton != null && FixBodyToggleButton.isOn && savedBodyElements.Count > 0)
            {
                previewElements.RemoveAll(e => e.PartType.Equals("Body", StringComparison.OrdinalIgnoreCase));
                // savedBodyElements의 모든 요소를 lockedParts에 추가
                foreach (var savedElement in savedBodyElements)
                {
                    // Structure를 키로 사용하여 각각의 Body 요소를 저장
                    var key = $"Body_{savedElement.Structure}";
                    lockedParts[key] = savedElement;
                }
            }
            
            // SPUM_Manager의 SetSprite 메서드 호출
            spumManager.SetSprite(previewElements);
            
            // Lock된 파츠들 복원
            if (lockedParts.Count > 0)
            {
                var lockedPartsList = lockedParts.Values.ToList();
                spumManager.SetSprite(lockedPartsList);
            }
            
            // 저장된 버튼 상태 복원
            StartCoroutine(RestoreButtonStates(savedButtonStates, characterData));
        }
        else
        {
            Debug.LogError("[SPUM] Failed to convert character data to preview elements.");
        }
    }
    
    /// <summary>
    /// Generate random character based on current filter selections
    /// </summary>
    public Dictionary<string, ImprovedCharacterDataItem> GenerateRandomCharacter()
    {
        var result = new Dictionary<string, ImprovedCharacterDataItem>();
        
        // 캐릭터 파트 우선순위 정의
        var characterParts = GetCharacterPartPriority();
        
        // Get current selections
        var selectedThemes = GetSelectedToggleValues("Theme");
        var selectedRaces = GetSelectedToggleValues("Race");
        var selectedGenders = GetSelectedToggleValues("Gender");
        var selectedClasses = GetSelectedToggleValues("Class");
        var selectedStyles = GetSelectedToggleValues("Style");
        
        // Get filtered data using the same logic as ImprovedTagWindow
        var filteredData = GetFilteredItems(
            selectedThemes: selectedThemes,
            selectedRaces: selectedRaces,
            selectedGenders: selectedGenders,
            selectedClasses: selectedClasses,
            selectedStyles: selectedStyles,
            selectedStyleElements: null,
            selectedParts: null,
            selectedCustomStyles: selectedStyles // Pass styles as custom styles too
        );
        
        if (filteredData.Count == 0)
        {
            Debug.LogWarning("[SPUM] No items match the current filter criteria");
            return result;
        }
        
        // Group by part
        var partGroups = filteredData.GroupBy(item => item.Part).ToDictionary(g => g.Key, g => g.ToList());
        
        // SPUM_Manager 참조 가져오기 (파츠 락 확인용)
        SPUM_Manager spumManager = FindFirstObjectByType<SPUM_Manager>();
        
        // 각 파트별로 랜덤 아이템 선택
        foreach (var partType in characterParts)
        {
            // Weapons는 별도로 처리
            if (partType == "Weapons")
            {
                continue;
            }
            
            // 해당 파츠의 버튼이 Lock되어 있는지 확인
            var partButtons = GetCachedSpriteButtons(partType);
            bool isPartLocked = partButtons.Any(b => b.IsSpriteFixed);
            
            // Lock된 파츠는 생성에서 제외 (아이템 유무와 관계없이)
            if (isPartLocked)
            {
                continue;
            }
            
            // 아이템 리스트 초기화
            List<ImprovedCharacterDataItem> items = new List<ImprovedCharacterDataItem>();
            
            // partGroups에 해당 파트가 있으면 가져오기
            if (partGroups.ContainsKey(partType))
            {
                items = partGroups[partType];
            }
            
            // Body 파트이고 FixBodyToggleButton이 켜져있으면 현재 Body 유지
            if (partType.Equals("Body", StringComparison.OrdinalIgnoreCase) && 
                FixBodyToggleButton != null && FixBodyToggleButton.isOn)
            {
                // 저장된 Body 요소가 있으면 사용
                if (savedBodyElements.Count > 0)
                {
                    // 첫 번째 요소의 파일명을 기준으로 찾기 (모든 Body 구조는 동일한 아이템에서 나옴)
                    var firstElement = savedBodyElements.First();
                    var bodyFileName = System.IO.Path.GetFileNameWithoutExtension(firstElement.ItemPath);
                    
                    // 저장된 파일명과 일치하는 아이템 찾기
                    var savedBody = items.FirstOrDefault(item => 
                        item.FileName.Equals(bodyFileName, StringComparison.OrdinalIgnoreCase));
                    
                    if (savedBody != null)
                    {
                        result[partType] = savedBody;
                        continue;
                    }
                }
                
                // 저장된 Body가 없으면 현재 Body 확인 및 저장
                if (savedBodyElements.Count == 0 && spumManager != null && spumManager.PreviewPrefab != null)
                {
                    // 현재 Body 파츠들 찾기
                    var currentBodyElements = spumManager.PreviewPrefab.ImageElement
                        .Where(e => e.PartType.Equals("Body", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    
                    foreach (var bodyElement in currentBodyElements)
                    {
                        if (!string.IsNullOrEmpty(bodyElement.ItemPath))
                        {
                            // 전체 Body 요소를 저장
                            savedBodyElements.Add(new PreviewMatchingElement
                            {
                                ItemPath = bodyElement.ItemPath,
                                PartType = bodyElement.PartType,
                                UnitType = bodyElement.UnitType,
                                PartSubType = bodyElement.PartSubType,
                                Structure = bodyElement.Structure,
                                Dir = bodyElement.Dir,
                                MaskIndex = bodyElement.MaskIndex,
                                Color = bodyElement.Color != default(Color) ? bodyElement.Color : Color.white
                            });
                        }
                    }
                    
                    // 첫 번째로 저장된 Body 요소를 사용하여 아이템 찾기
                    if (savedBodyElements.Count > 0)
                    {
                        var firstElement = savedBodyElements.First();
                        var bodyFileName = System.IO.Path.GetFileNameWithoutExtension(firstElement.ItemPath);
                        
                        // 해당 파일명과 일치하는 아이템 찾기
                        var bodyItem = items.FirstOrDefault(item => 
                            item.FileName.Equals(bodyFileName, StringComparison.OrdinalIgnoreCase));
                        
                        if (bodyItem != null)
                        {
                            result[partType] = bodyItem;
                            continue;
                        }
                    }
                }
            }
            
            // 스타일 필터 폴백 처리 - 스타일 조건으로 인해 아이템이 없을 때
            if (items.Count == 0 && enableStyleFilterFallback)
            {
                // 스타일 필터를 제외한 나머지 필터로 재검색
                items = GetFilteredItemsWithoutStyle(partType, 
                    selectedThemes, selectedRaces, selectedGenders, selectedClasses);
                
                if (verboseLogging && items.Count > 0)
                {
                    Debug.Log($"[SPUM] Style filter fallback activated for {partType}. Found {items.Count} items without style filter.");
                }
            }
            
            // Random selection
            if (items.Count > 0)
            {
                int randomIndex = UnityEngine.Random.Range(0, items.Count);
                result[partType] = items[randomIndex];
            }
        }
        
        // Weapons 특별 처리 - 듀얼 웨폰 시스템
        var weaponButtons = GetCachedSpriteButtons("Weapons");
        
        // Left/Right 무기 Lock 상태 개별 확인
        bool isLeftWeaponLocked = weaponButtons.Any(b => b.IsSpriteFixed && b.Direction == "Left");
        bool isRightWeaponLocked = weaponButtons.Any(b => b.IsSpriteFixed && b.Direction == "Right");
        
        // 둘 다 Lock되어 있으면 무기 생성 스킵
        if (isLeftWeaponLocked && isRightWeaponLocked)
        {
            // 무기 생성하지 않음
            return result;
        }
        
        var allWeaponItems = new List<ImprovedCharacterDataItem>();
        
        // partGroups에 Weapons가 있으면 가져오기
        if (partGroups.ContainsKey("Weapons"))
        {
            allWeaponItems = partGroups["Weapons"];
        }
        
        // 스타일 필터 폴백 처리 for Weapons
        if (allWeaponItems.Count == 0 && enableStyleFilterFallback)
        {
            allWeaponItems = GetFilteredItemsWithoutStyle("Weapons", 
                selectedThemes, selectedRaces, selectedGenders, selectedClasses);
            
            if (verboseLogging && allWeaponItems.Count > 0)
            {
                Debug.Log($"[SPUM] Style filter fallback activated for Weapons. Found {allWeaponItems.Count} items without style filter.");
            }
        }
        
        // 무기가 있을 때만 듀얼 웨폰 처리
        if (allWeaponItems.Count > 0)
        {
            
            // Shield와 일반 무기 분리
            var shields = allWeaponItems.Where(w => IsShield(w)).ToList();
            var nonShieldWeapons = allWeaponItems.Where(w => !IsShield(w)).ToList();
            
            
            // 첫 번째 무기 선택
            ImprovedCharacterDataItem firstWeapon = null;
            ImprovedCharacterDataItem secondWeapon = null;
            
            // Left 무기 처리 (Shield)
            if (!isLeftWeaponLocked && shields.Count > 0)
            {
                // 50% 확률로 Shield 장착
                if (UnityEngine.Random.value < 0.5f)
                {
                    firstWeapon = shields[UnityEngine.Random.Range(0, shields.Count)];
                }
            }
            
            // Right 무기 처리 (일반 무기)
            if (!isRightWeaponLocked && nonShieldWeapons.Count > 0)
            {
                // Shield가 없거나 Shield와 함께 듀얼 웨폰
                if (firstWeapon == null || (firstWeapon != null && UnityEngine.Random.value < 0.7f))
                {
                    secondWeapon = nonShieldWeapons[UnityEngine.Random.Range(0, nonShieldWeapons.Count)];
                }
            }
            
            // Lock되지 않은 상태에서 무기가 없는 경우 처리
            if (!isLeftWeaponLocked && !isRightWeaponLocked)
            {
                // 둘 다 선택되지 않은 경우 최소한 하나는 선택
                if (firstWeapon == null && secondWeapon == null)
                {
                    if (nonShieldWeapons.Count > 0)
                    {
                        secondWeapon = nonShieldWeapons[UnityEngine.Random.Range(0, nonShieldWeapons.Count)];
                    }
                    else if (shields.Count > 0)
                    {
                        firstWeapon = shields[UnityEngine.Random.Range(0, shields.Count)];
                    }
                }
            }
            
            // 결과에 무기 추가 (듀얼 웨폰을 위해 특별한 처리)
            if (firstWeapon != null || secondWeapon != null)
            {
                // 단일 무기만 선택된 경우
                if (firstWeapon != null && secondWeapon == null)
                {
                    result["Weapons"] = firstWeapon;
                }
                else if (firstWeapon == null && secondWeapon != null)
                {
                    result["Weapons"] = secondWeapon;
                }
                else if (firstWeapon != null && secondWeapon != null)
                {
                    // 듀얼 웨폰의 경우, 콤마로 구분된 파일명 생성
                    var dualWeaponItem = new ImprovedCharacterDataItem
                    {
                        FileName = $"{firstWeapon.FileName},{secondWeapon.FileName}",
                        Part = "Weapons",
                        Theme = firstWeapon.Theme,
                        Race = firstWeapon.Race,
                        Gender = firstWeapon.Gender,
                        Type = firstWeapon.Type,
                        Properties = firstWeapon.Properties,
                        Class = firstWeapon.Class,
                        Style = firstWeapon.Style
                    };
                    result["Weapons"] = dualWeaponItem;
                }
                
            }
        }
        
        // 필수 파트 확인 (Body, Eye)
        if (!result.ContainsKey("Body") && partGroups.ContainsKey("Body"))
        {
            var bodyItems = partGroups["Body"];
            if (bodyItems.Count > 0)
            {
                result["Body"] = bodyItems[UnityEngine.Random.Range(0, bodyItems.Count)];
            }
        }
        
        if (!result.ContainsKey("Eye") && partGroups.ContainsKey("Eye"))
        {
            var eyeItems = partGroups["Eye"];
            if (eyeItems.Count > 0)
            {
                result["Eye"] = eyeItems[UnityEngine.Random.Range(0, eyeItems.Count)];
            }
        }
        
        Debug.Log($"[SPUM] Generated character with {result.Count} parts");
        return result;
    }
    
    /// <summary>
    /// 아이템이 Shield인지 확인
    /// </summary>
    private bool IsShield(ImprovedCharacterDataItem item)
    {
        // Type으로 확인
        if (!string.IsNullOrEmpty(item.Type) && 
            item.Type.Equals("shield", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        
        // 파일명으로 확인
        if (!string.IsNullOrEmpty(item.FileName) && 
            item.FileName.ToLower().Contains("shield"))
        {
            return true;
        }
        
        // Properties에서 확인
        if (item.Properties != null && 
            item.Properties.Any(p => p.Equals("shield", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// 아이템이 특정 스타일과 호환되는지 확인
    /// </summary>
    private bool IsStyleCompatibleWithItem(ImprovedCharacterDataItem item, string styleName, SPUM_CustomStyleData customStyleData = null)
    {
        if (customStyleData == null)
            customStyleData = GetCustomStyleData();
            
        if (customStyleData == null || !customStyleData.custom_styles.ContainsKey(styleName))
            return false;
            
        var style = customStyleData.custom_styles[styleName];
        
        // Required parts 체크 - 아이템의 파츠가 스타일이 요구하는 파츠 중 하나인지 확인
        if (style.required_parts != null && style.required_parts.Count > 0)
        {
            if (!style.required_parts.Contains(item.Part))
                return false;
        }
        
        // Negative tags 체크는 customStyleData에만 남아있음 (SPUM_Custom_Styles.json)
        if (style.negative != null && style.negative.Count > 0 && item.Style != null)
        {
            if (item.Style.Any(itemStyle => style.negative.Contains(itemStyle)))
                return false;
        }
        
        return true;
    }
    
    // Public methods for external access
    public SPUM_ImprovedClassData GetClassData() => classData;
    public SPUM_ImprovedStyleData GetStyleData() => styleData;
    public SPUM_CustomStyleData GetCustomStyleData() => customStyleData;
    public SPUM_ImprovedStyleData GetStyleElementsData()
    {
        // Try internal data first
        if (internalStyleElementsData != null)
        {
            Debug.Log("[SPUM] Using internal style elements data");
            return internalStyleElementsData;
        }
        
        // Try cached data
        if (cachedStyleElementsData != null)
        {
            Debug.Log("[SPUM] Using cached style elements data");
            return cachedStyleElementsData;
        }
        
        // Load data dynamically if not available
        Debug.Log("[SPUM] Loading style elements data dynamically");
        LoadInternalStyleElementsData();
        return internalStyleElementsData ?? cachedStyleElementsData;
    }
    public List<ImprovedCharacterDataItem> GetAllCharacterData()
    {
        var spumManager = SoonsoonData.Instance._spumManager;
        if (spumManager == null) 
            return new List<ImprovedCharacterDataItem>(allCharacterData);
        
        var validPackages = spumManager.SpritePackageNameList;
        
        return allCharacterData
            .Where(item => {
                // Addon이 비어있으면 제외
                if (string.IsNullOrEmpty(item.Addon)) 
                    return false;
                
                // Addon이 validPackages에 있는지 확인
                return validPackages.Contains(item.Addon);
            })
            .ToList();
    }
    
    /// <summary>
    /// Check if character data is loaded
    /// </summary>
    public bool IsDataLoaded() => allCharacterData != null && allCharacterData.Count > 0;
    
    #region Filter Methods
    
    /// <summary>
    /// Filter character data based on provided criteria
    /// </summary>
    public List<ImprovedCharacterDataItem> GetFilteredItems(
        List<string> selectedAddons = null,
        List<string> selectedThemes = null,
        List<string> selectedRaces = null,
        List<string> selectedGenders = null,
        List<string> selectedClasses = null,
        List<string> selectedStyles = null,
        List<string> selectedStyleElements = null,
        List<string> selectedParts = null,
        List<string> selectedCustomStyles = null,
        bool globalFilterCombineAND = true,  // Always use AND mode between filters
        bool characterAttributesCombineAND = false,  // Default: OR mode between character attributes
        bool combatVisualStyleCombineAND = true,  // Default: AND mode between combat/visual style
        bool addonFilterUseAND = false,  // Default: OR mode
        bool themeFilterUseAND = false,  // Default: OR mode
        bool raceFilterUseAND = false,  // Default: OR mode
        bool genderFilterUseAND = false,  // Default: OR mode
        bool classFilterUseAND = false,  // Default: OR mode
        bool styleFilterUseAND = false,  // Default: OR mode
        bool styleElementsFilterUseAND = false,  // Default: OR mode
        bool customStylesFilterUseAND = false,  // Default: OR mode
        bool themeDefaultSelectAll = true,  // Default: select all when empty
        bool raceDefaultSelectAll = true,  // Default: select all when empty
        bool genderDefaultSelectAll = true)  // Default: select all when empty
    {
        var allData = GetAllCharacterData();
        
        // Always use AND mode: Apply filters sequentially
        return GetFilteredItemsANDMode(allData, selectedAddons, selectedThemes, selectedRaces, selectedGenders, 
            selectedClasses, selectedStyles, selectedStyleElements, selectedParts, selectedCustomStyles,
            characterAttributesCombineAND, combatVisualStyleCombineAND,
            addonFilterUseAND, themeFilterUseAND, raceFilterUseAND, genderFilterUseAND,
            classFilterUseAND, styleFilterUseAND, styleElementsFilterUseAND, customStylesFilterUseAND,
            themeDefaultSelectAll, raceDefaultSelectAll, genderDefaultSelectAll);
    }
    
    private List<ImprovedCharacterDataItem> GetFilteredItemsANDMode(
        List<ImprovedCharacterDataItem> allData,
        List<string> selectedAddons, List<string> selectedThemes, List<string> selectedRaces, List<string> selectedGenders,
        List<string> selectedClasses, List<string> selectedStyles, List<string> selectedStyleElements,
        List<string> selectedParts, List<string> selectedCustomStyles,
        bool characterAttributesCombineAND, bool combatVisualStyleCombineAND,
        bool addonFilterUseAND, bool themeFilterUseAND, bool raceFilterUseAND, bool genderFilterUseAND,
        bool classFilterUseAND, bool styleFilterUseAND, bool styleElementsFilterUseAND, bool customStylesFilterUseAND,
        bool themeDefaultSelectAll, bool raceDefaultSelectAll, bool genderDefaultSelectAll)
    {
        var filtered = allData.AsEnumerable();
        
        // Apply Addon filter first (최상단 필터)
        if (selectedAddons?.Count > 0)
        {
            if (addonFilterUseAND)
            {
                filtered = filtered.Where(item => 
                    !string.IsNullOrEmpty(item.Addon) && 
                    selectedAddons.All(selectedAddon => item.Addon == selectedAddon)
                );
            }
            else
            {
                filtered = filtered.Where(item => 
                    !string.IsNullOrEmpty(item.Addon) && 
                    selectedAddons.Contains(item.Addon)
                );
            }
        }
        
        // Apply Character Attributes filters as a group
        bool hasCharAttrFilters = (selectedThemes?.Count > 0) || (selectedRaces?.Count > 0) || (selectedGenders?.Count > 0);
        if (hasCharAttrFilters)
        {
            filtered = filtered.Where(item => CheckCharacterAttributesMatch(item, 
                selectedThemes, selectedRaces, selectedGenders,
                characterAttributesCombineAND, themeFilterUseAND, raceFilterUseAND, genderFilterUseAND,
                themeDefaultSelectAll, raceDefaultSelectAll, genderDefaultSelectAll));
        }
        
        // Race가 선택된 경우 헬멧, FaceHair, Hair 특별 처리 - 심플한 방식
        if (selectedRaces?.Count > 0)
        {
            // Race가 있는데 선택된 Race와 다른 헬멧, FaceHair, Hair들만 제외
            filtered = filtered.Where(item => 
                (item.Part != "Helmet" && item.Part != "FaceHair" && item.Part != "Hair") ||  // 헬멧이나 FaceHair, Hair가 아니거나
                string.IsNullOrEmpty(item.Race) ||  // Race가 없거나
                selectedRaces.Contains(item.Race)  // 선택된 Race와 일치하거나
            );
        }
        
        // Apply Combat & Visual Style filters as a group
        bool hasCombatStyleFilters = (selectedClasses?.Count > 0) || (selectedStyles?.Count > 0) || 
                                   (selectedStyleElements?.Count > 0) || (selectedCustomStyles?.Count > 0);
        if (hasCombatStyleFilters)
        {
            filtered = filtered.Where(item => CheckCombatVisualStyleMatch(item,
                selectedClasses, selectedStyles, selectedStyleElements, selectedCustomStyles,
                combatVisualStyleCombineAND, classFilterUseAND, styleFilterUseAND, 
                styleElementsFilterUseAND, customStylesFilterUseAND));
        }
        
        // Apply Part filter independently
        if (selectedParts?.Count > 0)
        {
            filtered = filtered.Where(item => selectedParts.Contains(item.Part));
        }
        
        return filtered.ToList();
    }
    
    
    private bool CheckCharacterAttributesMatch(ImprovedCharacterDataItem item,
        List<string> selectedThemes, List<string> selectedRaces, List<string> selectedGenders,
        bool characterAttributesCombineAND, bool themeFilterUseAND, bool raceFilterUseAND, bool genderFilterUseAND,
        bool themeDefaultSelectAll, bool raceDefaultSelectAll, bool genderDefaultSelectAll)
    {
        // Handle default modes for empty selections
        bool themeMatch;
        if (selectedThemes == null || selectedThemes.Count == 0)
        {
            themeMatch = themeDefaultSelectAll; // If defaultSelectAll=true (All mode), pass all. If false (None mode), match none.
        }
        else
        {
            themeMatch = CheckThemeMatch(item, selectedThemes, themeFilterUseAND);
        }
        
        bool raceMatch;
        if (selectedRaces == null || selectedRaces.Count == 0)
        {
            raceMatch = raceDefaultSelectAll; // If defaultSelectAll=true (All mode), pass all. If false (None mode), match none.
        }
        else
        {
            raceMatch = CheckRaceMatch(item, selectedRaces, raceFilterUseAND);
        }
        
        bool genderMatch;
        if (selectedGenders == null || selectedGenders.Count == 0)
        {
            genderMatch = genderDefaultSelectAll; // If defaultSelectAll=true (All mode), pass all. If false (None mode), match none.
        }
        else
        {
            genderMatch = CheckGenderMatch(item, selectedGenders, genderFilterUseAND);
        }
        
        // Combine results based on mode
        if (characterAttributesCombineAND)
        {
            return themeMatch && raceMatch && genderMatch;
        }
        else
        {
            // OR 모드 수정: 적용 가능한 필터들은 AND로 결합
            // Theme은 항상 체크, Race는 Body에만, Gender는 Hair/FaceHair에만

            // Theme 필터가 활성화되어 있으면 반드시 통과해야 함
            if (selectedThemes != null && selectedThemes.Count > 0)
            {
                if (!themeMatch) return false;
            }

            // Race 필터가 활성화되어 있고 Body 파츠면 반드시 통과해야 함
            if (selectedRaces != null && selectedRaces.Count > 0 && item.Part == "Body")
            {
                if (!raceMatch) return false;
            }

            // Gender 필터가 활성화되어 있고 Hair/FaceHair 파츠면 반드시 통과해야 함
            if (selectedGenders != null && selectedGenders.Count > 0 &&
                (item.Part == "Hair" || item.Part == "FaceHair"))
            {
                if (!genderMatch) return false;
            }

            return true;
        }
    }
    
    private bool CheckCombatVisualStyleMatch(ImprovedCharacterDataItem item,
        List<string> selectedClasses, List<string> selectedStyles, 
        List<string> selectedStyleElements, List<string> selectedCustomStyles,
        bool combatVisualStyleCombineAND, bool classFilterUseAND, bool styleFilterUseAND,
        bool styleElementsFilterUseAND, bool customStylesFilterUseAND)
    {
        bool classMatch = (selectedClasses == null || selectedClasses.Count == 0) || 
                         CheckClassMatch(item, selectedClasses, classFilterUseAND);
        bool styleMatch = (selectedStyles == null || selectedStyles.Count == 0) || 
                         CheckStyleMatch(item, selectedStyles, styleFilterUseAND);
        bool styleElementsMatch = (selectedStyleElements == null || selectedStyleElements.Count == 0) || 
                                 CheckStyleElementsMatch(item, selectedStyleElements, styleElementsFilterUseAND);
        bool customStylesMatch = (selectedCustomStyles == null || selectedCustomStyles.Count == 0) || 
                                CheckCustomStylesMatch(item, selectedCustomStyles, customStylesFilterUseAND);
        
        if (combatVisualStyleCombineAND)
        {
            return classMatch && styleMatch && styleElementsMatch && customStylesMatch;
        }
        else
        {
            // OR mode: at least one filter must be active and match
            bool hasActiveFilter = (selectedClasses != null && selectedClasses.Count > 0) || 
                                 (selectedStyles != null && selectedStyles.Count > 0) || 
                                 (selectedStyleElements != null && selectedStyleElements.Count > 0) ||
                                 (selectedCustomStyles != null && selectedCustomStyles.Count > 0);
            if (!hasActiveFilter) return true;
            
            return ((selectedClasses != null && selectedClasses.Count > 0) && CheckClassMatch(item, selectedClasses, classFilterUseAND)) ||
                   ((selectedStyles != null && selectedStyles.Count > 0) && CheckStyleMatch(item, selectedStyles, styleFilterUseAND)) ||
                   ((selectedStyleElements != null && selectedStyleElements.Count > 0) && CheckStyleElementsMatch(item, selectedStyleElements, styleElementsFilterUseAND)) ||
                   ((selectedCustomStyles != null && selectedCustomStyles.Count > 0) && CheckCustomStylesMatch(item, selectedCustomStyles, customStylesFilterUseAND));
        }
    }
    
    private bool CheckThemeMatch(ImprovedCharacterDataItem item, List<string> selectedThemes, bool useAND)
    {
        if (item.Theme == null || item.Theme.Length == 0) return false;
        
        if (useAND)
        {
            return selectedThemes.All(theme => item.Theme.Contains(theme));
        }
        else
        {
            return selectedThemes.Any(theme => item.Theme.Contains(theme));
        }
    }
    
    private bool CheckRaceMatch(ImprovedCharacterDataItem item, List<string> selectedRaces, bool useAND)
    {
        // Part가 비어있는 경우 포함
        if (string.IsNullOrEmpty(item.Part)) return true;
        
        // Race filter only applies to Body parts
        if (item.Part != "Body") return true;
        
        // Race가 비어있는 경우 포함
        if (string.IsNullOrEmpty(item.Race)) return true;
        
        if (useAND)
        {
            return selectedRaces.All(race => item.Race == race);
        }
        else
        {
            return selectedRaces.Any(race => item.Race == race);
        }
    }
    
    private bool CheckGenderMatch(ImprovedCharacterDataItem item, List<string> selectedGenders, bool useAND)
    {
        // Part가 비어있는 경우 포함
        if (string.IsNullOrEmpty(item.Part)) return true;
        
        // Gender filter only applies to Hair and FaceHair parts (not Body, Eye)
        if (item.Part != "Hair" && item.Part != "FaceHair") return true;
        
        // Gender가 비어있는 경우 포함
        if (string.IsNullOrEmpty(item.Gender)) return true;
        
        if (useAND)
        {
            return selectedGenders.All(gender => item.Gender == gender);
        }
        else
        {
            return selectedGenders.Any(gender => item.Gender == gender);
        }
    }
    
    private bool CheckClassMatch(ImprovedCharacterDataItem item, List<string> selectedClasses, bool useAND)
    {
        // Part가 비어있는 경우 포함
        if (string.IsNullOrEmpty(item.Part)) return true;
        
        // Class filter only applies to parts other than Body, Eye, Hair, and FaceHair
        if (item.Part == "Body" || item.Part == "Eye" || item.Part == "Hair" || item.Part == "FaceHair") return true;
        
        // Get class data
        var classData = GetClassData();
        if (classData == null || classData.combat_classes == null)
        {
            Debug.LogWarning("[SPUM] Class data not loaded");
            return false;
        }
        
        // Check if item matches any of the selected classes
        foreach (var selectedClass in selectedClasses)
        {
            if (!classData.combat_classes.ContainsKey(selectedClass))
            {
                Debug.LogWarning($"[SPUM] Class '{selectedClass}' not found in class data");
                continue;
            }
            
            var classInfo = classData.combat_classes[selectedClass];
            
            // Type match: Check if item type is allowed by this class
            bool typeMatch = string.IsNullOrEmpty(item.Type) || 
                           (classInfo.type != null && classInfo.type.Contains(item.Type));
            
            // Class tags match: Check if item has any of the required class tags
            bool classMatch = item.Class == null || item.Class.Length == 0 || 
                            (classInfo.class_tags != null && item.Class.Any(c => classInfo.class_tags.Contains(c)));
            
            // Type negative check: Make sure item type is not in the forbidden list
            bool typeNegativeCheck = classInfo.type_negative == null || classInfo.type_negative.Count == 0 ||
                                   string.IsNullOrEmpty(item.Type) ||
                                   !classInfo.type_negative.Contains(item.Type);
            
            // Class negative check: Make sure item style doesn't have forbidden tags
            bool classNegativeCheck = classInfo.class_negative == null || classInfo.class_negative.Count == 0 ||
                                    item.Style == null || item.Style.Length == 0 ||
                                    !item.Style.Any(s => classInfo.class_negative.Contains(s));
            
            // If all checks pass for this class, item matches
            if (typeMatch && classMatch && typeNegativeCheck && classNegativeCheck)
            {
                return true;
            }
        }
        
        // Item doesn't match any selected class
        return false;
    }
    
    private bool CheckStyleMatch(ImprovedCharacterDataItem item, List<string> selectedStyles, bool useAND)
    {
        // Part가 비어있는 경우 포함
        if (string.IsNullOrEmpty(item.Part)) return true;
        
        // Style filter only applies to parts other than Body, Eye, Hair, and FaceHair
        if (item.Part == "Body" || item.Part == "Eye" || item.Part == "Hair" || item.Part == "FaceHair") return true;
        
        if (item.Style == null || item.Style.Length == 0) return false;
        
        if (useAND)
        {
            return selectedStyles.All(style => item.Style.Contains(style));
        }
        else
        {
            return selectedStyles.Any(style => item.Style.Contains(style));
        }
    }
    
    private bool CheckStyleElementsMatch(ImprovedCharacterDataItem item, List<string> selectedStyleElements, bool useAND)
    {
        if (item.Style == null || item.Style.Length == 0) return false;
        
        if (useAND)
        {
            return selectedStyleElements.All(element => item.Style.Contains(element));
        }
        else
        {
            return item.Style.Any(s => selectedStyleElements.Contains(s));
        }
    }
    
    private bool CheckCustomStylesMatch(ImprovedCharacterDataItem item, List<string> selectedCustomStyles, bool useAND)
    {
        // Part가 비어있는 경우 포함
        if (string.IsNullOrEmpty(item.Part)) return true;
        
        // Style filter only applies to parts other than Body, Eye, Hair, and FaceHair
        if (item.Part == "Body" || item.Part == "Eye" || item.Part == "Hair" || item.Part == "FaceHair") return true;
        
        if (item.Style == null || item.Style.Length == 0) return false;
        
        if (useAND)
        {
            return selectedCustomStyles.All(style => item.Style.Contains(style));
        }
        else
        {
            return selectedCustomStyles.Any(style => item.Style.Contains(style));
        }
    }
    
    #endregion
    
    /// <summary>
    /// Simple filter method with basic parameters
    /// </summary>
    public List<ImprovedCharacterDataItem> FilterItems(
        List<string> themes = null,
        List<string> races = null,
        List<string> genders = null,
        List<string> classes = null,
        List<string> styles = null,
        List<string> parts = null,
        bool useANDMode = false)  // Default: OR mode (same as editor)
    {
        return GetFilteredItems(
            selectedThemes: themes,
            selectedRaces: races,
            selectedGenders: genders,
            selectedClasses: classes,
            selectedStyles: styles,
            selectedParts: parts,
            globalFilterCombineAND: useANDMode
        );
    }
    
    /// <summary>
    /// Get loaded data count
    /// </summary>
    public int GetLoadedDataCount() => allCharacterData?.Count ?? 0;
    
    /// <summary>
    /// Update a character data item by filename
    /// </summary>
    public bool UpdateCharacterDataItem(string fileName, ImprovedCharacterDataItem updatedItem)
    {
        if (allCharacterData == null || string.IsNullOrEmpty(fileName))
            return false;
        
        // Find the item by filename
        for (int i = 0; i < allCharacterData.Count; i++)
        {
            if (allCharacterData[i].FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase))
            {
                // Update the item
                allCharacterData[i] = updatedItem;
                return true;
            }
        }
        
        return false;
    }
    
    
    /// <summary>
    /// 캐릭터 파트 우선순위 반환
    /// </summary>
    public List<string> GetCharacterPartPriority()
    {
        // SPUM 시스템의 일반적인 파트 순서
        return new List<string>
        {
            "Body",      // 필수 - 기본 몸체
            "Eye",       // 필수 - 눈
            "Hair",      // 선택 - 머리카락
            "FaceHair",  // 선택 - 수염
            "Cloth",     // 선택 - 상의
            "Pant",      // 선택 - 하의
            "Armor",     // 선택 - 갑옷
            "Helmet",    // 선택 - 투구
            "Weapons",   // 선택 - 무기류 (Sword, Axe, Shield 등)
            "Back"       // 선택 - 등 장식
        };
    }
    
    /// <summary>
    /// 스타일 필터를 제외한 나머지 필터로 아이템 검색
    /// </summary>
    private List<ImprovedCharacterDataItem> GetFilteredItemsWithoutStyle(
        string partType,
        List<string> selectedThemes,
        List<string> selectedRaces,
        List<string> selectedGenders,
        List<string> selectedClasses)
    {
        var filteredData = GetFilteredItems(
            selectedThemes: selectedThemes,
            selectedRaces: selectedRaces,
            selectedGenders: selectedGenders,
            selectedClasses: selectedClasses,
            selectedStyles: null,  // 스타일 필터 제외
            selectedStyleElements: null,
            selectedParts: new List<string> { partType },
            selectedCustomStyles: null
        );
        
        return filteredData.Where(item => item.Part == partType).ToList();
    }
    
    /// <summary>
    /// Dictionary<string, ImprovedCharacterDataItem>을 List<PreviewMatchingElement>로 변환
    /// </summary>
    private List<PreviewMatchingElement> ConvertToPreviewMatchingElements(Dictionary<string, ImprovedCharacterDataItem> characterData)
    {
        var elements = new List<PreviewMatchingElement>();
        
        foreach (var kvp in characterData)
        {
            var partType = kvp.Key;
            var item = kvp.Value;
            
            // Weapons의 경우 특별 처리 (여러 무기 지원)
            if (partType == "Weapons" && item.FileName.Contains(","))
            {
                var weaponFileNames = item.FileName.Split(',');
                foreach (var weaponFileName in weaponFileNames)
                {
                    var trimmedFileName = weaponFileName.Trim();
                    var weaponItem = allCharacterData.FirstOrDefault(data => 
                        data.FileName.Equals(trimmedFileName, StringComparison.OrdinalIgnoreCase));
                    
                    if (weaponItem != null)
                    {
                        var weaponElements = CreatePreviewMatchingElements(weaponItem);
                        elements.AddRange(weaponElements);
                    }
                    else
                    {
                        Debug.LogWarning($"[SPUM] Could not find weapon item data for {trimmedFileName}");
                    }
                }
                continue;
            }
            
            // 일반 파츠 처리
            var previewElements = CreatePreviewMatchingElements(item);
            elements.AddRange(previewElements);
        }
        
        return elements;
    }
    
    /// <summary>
    /// ImprovedCharacterDataItem을 PreviewMatchingElement로 변환
    /// </summary>
    private List<PreviewMatchingElement> CreatePreviewMatchingElements(ImprovedCharacterDataItem item)
    {
        var elements = new List<PreviewMatchingElement>();
        
        if (item == null) return elements;
        
        // SPUM_Manager 참조 가져오기
        SPUM_Manager spumManager = FindFirstObjectByType<SPUM_Manager>();
        if (spumManager == null)
        {
            Debug.LogError("[SPUM] SPUM_Manager not found!");
            return elements;
        }
        
        // spumPackages에서 해당 파일명과 일치하는 모든 텍스처 데이터 찾기
        var matchingTextureDataList = new List<SpumTextureData>();
        foreach (var package in spumManager.spumPackages)
        {
            var matches = package.SpumTextureData.Where(t => 
                t.Name.Equals(item.FileName, StringComparison.OrdinalIgnoreCase) &&
                t.PartType.Equals(item.Part, StringComparison.OrdinalIgnoreCase) &&
                t.UnitType.Equals("Unit", StringComparison.OrdinalIgnoreCase)).ToList();
            
            matchingTextureDataList.AddRange(matches);
        }
        
        if (matchingTextureDataList.Count == 0)
        {
            Debug.LogWarning($"[SPUM] Could not find texture data for {item.FileName} in spumPackages");
            return elements;
        }
        
        // 각 텍스처 데이터에 대해 PreviewMatchingElement 생성
        foreach (var textureData in matchingTextureDataList)
        {
            var element = new PreviewMatchingElement();
            
            // SpumTextureData에서 정확한 데이터 가져오기
            element.UnitType = textureData.UnitType;
            element.PartType = textureData.PartType;
            element.PartSubType = textureData.PartSubType;
            element.ItemPath = textureData.Path;
            element.Structure = textureData.SubType.Equals(textureData.Name) ? textureData.PartType : textureData.SubType;
            
            // 기본값 설정
            element.Index = 0;
            element.Dir = "";
            element.MaskIndex = 0;
            element.Color = GetInitialColorForPart(element.PartType, element.UnitType, element.PartSubType, element.Structure);
            
            // 무기의 경우 특별 처리
            if (element.PartType == "Weapons")
            {
                // 무기는 Structure가 "Weapons"여야 함
                element.Structure = "Weapons";
                // Shield는 Left, 다른 무기는 Right
                element.Dir = IsShieldTexture(textureData) ? "Left" : "Right";
                Debug.Log($"[Weapon] Created weapon element - Name: {textureData.Name}, Path: {element.ItemPath}, Structure: {element.Structure}, PartSubType: {element.PartSubType}, Dir: {element.Dir}");
            }
            
            // 헬멧의 경우 특별 처리
            if (element.PartType == "Helmet")
            {
                element.Structure = "Helmet";
                element.Dir = "Front";
                Debug.Log($"[Helmet] Created helmet element - Name: {textureData.Name}, Path: {element.ItemPath}, Structure: {element.Structure}, Dir: {element.Dir}");
            }
            
            elements.Add(element);
            Debug.Log($"[SPUM] Created element - Path: {element.ItemPath}, Structure: {element.Structure}, SubType: {textureData.SubType}, PartType: {element.PartType}");
        }
        
        Debug.Log($"[SPUM] Created {elements.Count} elements for {item.FileName} ({item.Part})");
        
        return elements;
    }
    
    /// <summary>
    /// 아이템의 애드온 폴더명 가져오기
    /// </summary>
    private string GetAddonFolderName(ImprovedCharacterDataItem item)
    {
        // Theme에서 애드온 이름 추출 (보통 첫 번째 테마가 애드온 이름)
        if (item.Theme != null && item.Theme.Length > 0)
        {
            return item.Theme[0];
        }
        
        // 파일명에서 추출 시도
        if (!string.IsNullOrEmpty(item.FileName))
        {
            // 일반적인 패턴에서 추출
            if (item.FileName.StartsWith("F_SR_"))
                return "Legacy";
            else if (item.FileName.StartsWith("Orc_"))
                return "MS_Orc";
            else if (item.FileName.StartsWith("Elf_"))
                return "Elf";
            else if (item.FileName.StartsWith("Undead_"))
                return "Undead";
        }
        
        return "Legacy"; // 기본값
    }
    
    /// <summary>
    /// 리소스 경로 구성
    /// </summary>
    private string ConstructResourcePath(ImprovedCharacterDataItem item, string folderName)
    {
        // 파트별 경로 매핑
        var partPaths = new Dictionary<string, string>
        {
            { "Body", "0_Body" },
            { "Eye", "0_Eye" },
            { "Hair", "0_Hair" },
            { "FaceHair", "1_FaceHair" },
            { "Cloth", "2_Cloth" },
            { "Pant", "3_Pant" },
            { "Helmet", "4_Helmet" },
            { "Armor", "5_Armor" },
            { "Back", "7_Back" }
        };
        
        if (partPaths.TryGetValue(item.Part, out string partPath))
        {
            return $"Addons/{folderName}/0_Unit/0_Sprite/{partPath}/{item.FileName}";
        }
        
        // 기본 경로
        return $"Addons/{folderName}/0_Unit/0_Sprite/{item.Part}/{item.FileName}";
    }
    
    /// <summary>
    /// 무기 타입에 따른 Structure 값 반환
    /// </summary>
    private string GetWeaponStructure(string weaponType)
    {
        var structureMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "sword", "0_Sword" },
            { "axe", "1_Axe" },
            { "bow", "2_Bow" },
            { "spear", "3_Spear" },
            { "dagger", "4_Dagger" },
            { "hammer", "5_Hammer" },
            { "shield", "6_Shield" },
            { "wand", "7_Wand" },
            { "staff", "8_Staff" }
        };
        
        return structureMap.TryGetValue(weaponType.ToLower(), out string structure) ? structure : weaponType;
    }
    
    /// <summary>
    /// SpumTextureData가 Shield인지 확인
    /// </summary>
    private bool IsShieldTexture(SpumTextureData textureData)
    {
        // SubType으로 확인
        if (!string.IsNullOrEmpty(textureData.SubType) && 
            textureData.SubType.ToLower().Contains("shield"))
        {
            return true;
        }
        
        // PartSubType으로 확인
        if (!string.IsNullOrEmpty(textureData.PartSubType) && 
            textureData.PartSubType.ToLower().Contains("shield"))
        {
            return true;
        }
        
        // Name으로 확인
        if (!string.IsNullOrEmpty(textureData.Name) && 
            textureData.Name.ToLower().Contains("shield"))
        {
            return true;
        }
        
        // Path로 확인
        if (!string.IsNullOrEmpty(textureData.Path) && 
            textureData.Path.ToLower().Contains("shield"))
        {
            return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Get initial color for specific part type
    /// </summary>
    private Color GetInitialColorForPart(string partType, string unitType = "Unit", string subType = null, string structure = null)
    {
        // SPUM_Manager 참조 가져오기
        SPUM_Manager spumManager = FindFirstObjectByType<SPUM_Manager>();
        if (spumManager == null)
        {
            return Color.white;
        }
        
        // 모든 SPUM_SpriteButtonST 버튼들 가져오기
        var allButtons = GetCachedSpriteButtons();
        
        // 해당 파츠 타입과 유닛 타입에 맞는 버튼 찾기
        foreach (var button in allButtons)
        {
            if (button.PartType.Equals(partType, StringComparison.OrdinalIgnoreCase) &&
                button.UnitType.Equals(unitType, StringComparison.OrdinalIgnoreCase))
            {
                // ignoreColorPart 체크 - SubType, Structure 모두 확인
                if (button.ignoreColorPart != null && button.ignoreColorPart.Count > 0)
                {
                    // Check if any of the part identifiers are in the ignore list
                    if (button.ignoreColorPart.Contains(partType) ||
                        (!string.IsNullOrEmpty(subType) && button.ignoreColorPart.Contains(subType)) ||
                        (!string.IsNullOrEmpty(structure) && button.ignoreColorPart.Contains(structure)))
                    {
                        return Color.white;
                    }
                }
                
                return button.InitColor;
            }
        }
        
        // 기본 색상 설정
        switch (partType.ToLower())
        {
            case "eye":
                return new Color32(71, 26, 26, 255); // 눈 기본 색상
            case "body":
            case "hair":
            case "facehair":
            case "cloth":
            case "pant":
            case "armor":
            case "helmet":
            case "back":
            case "weapons":
            default:
                return Color.white;
        }
    }
    
    /// <summary>
    /// 저장된 버튼 상태를 복원하는 코루틴
    /// </summary>
    private IEnumerator RestoreButtonStates(Dictionary<(string PartType, string UnitType, string Direction), (bool IsActive, Color Color)> savedStates, Dictionary<string, ImprovedCharacterDataItem> generatedCharacter)
    {
        // SetSprite가 완전히 적용될 때까지 한 프레임 대기
        yield return null;
        
        var buttons = GetCachedSpriteButtons();
        
        // 생성된 캐릭터의 파츠 타입 목록 만들기
        var generatedPartTypes = new HashSet<string>(generatedCharacter.Keys);
        
        foreach (var button in buttons)
        {
            // Lock된 버튼은 상태 변경하지 않음
            if (button.IsSpriteFixed)
            {
                continue;
            }
            
            var key = (button.PartType, button.UnitType, button.Direction ?? "");
            
            // 생성된 캐릭터에 해당 파츠가 있으면 활성화
            if (generatedPartTypes.Contains(button.PartType))
            {
                // 저장된 상태가 있으면 해당 상태로 복원
                if (savedStates.ContainsKey(key))
                {
                    button.IsActive = savedStates[key].IsActive;
                    button.PartSpriteColor = savedStates[key].Color;
                }
                else
                {
                    // 저장된 상태가 없지만 생성된 캐릭터에 파츠가 있는 경우 활성화
                    button.IsActive = true;
                }
            }
        }
    }
    
    /// <summary>
    /// Unit 타입의 아이템만 선택적으로 초기화
    /// </summary>
    private void ResetUnitTypeItemsOnly(SPUM_Manager spumManager)
    {
        if (spumManager == null || spumManager.PreviewPrefab == null) return;
        
        // PreviewPrefab의 ImageElement에서 Unit 타입만 제거
        spumManager.PreviewPrefab.ImageElement.RemoveAll(e => e.UnitType == "Unit");
        
        // 모든 SPUM_SpriteButtonST 버튼들 가져오기
        #if UNITY_2023_1_OR_NEWER
            var ItemButtons = FindObjectsByType<SPUM_SpriteButtonST>(FindObjectsSortMode.None);
        #else
            #pragma warning disable CS0618
            var ItemButtons = FindObjectsOfType<SPUM_SpriteButtonST>();
            #pragma warning restore CS0618
        #endif
        
        // Unit 타입 버튼들만 RemoveSprite 호출
        foreach (var button in ItemButtons)
        {
            if (button.UnitType == "Unit")
            {
                button.RemoveSprite();
            }
        }
        
        // Unit 타입일 때만 ResetBody 호출 (Body와 Eye 초기화)
        if (spumManager.PreviewPrefab.UnitType == "Unit")
        {
            spumManager.ResetBody();
        }
    }
}