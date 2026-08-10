using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
public partial class SPUM_PaginationManager : MonoBehaviour
{
    public GameObject itemPrefab;
    public Transform contentParent;
    public Button prevButton;
    public Button nextButton;
    public Button ApplyPresetAllButton;
    public Button CloseButton;
    public Toggle BatchModeButton;
    public Toggle SelectAllToggleButton;
    public Text pageText;
    public SPUM_Manager SPUM_Manager;
    public SPUM_AnimationManager SPUM_AnimationManager;
    private SortedDictionary<int, SPUM_Prefabs> sortedItems = new SortedDictionary<int, SPUM_Prefabs>();
    private Queue<int> deletedIndexes = new Queue<int>();
    private int nextAvailableIndex = 0;
    private int currentPage = 1;
    private int itemsPerPage = 10;
    public GameObject PreviewPanel;
    void Start()
    {
        LoadPrefabs();
        prevButton.onClick.AddListener(PrevPage);
        nextButton.onClick.AddListener(NextPage);
        ApplyPresetAllButton.onClick.AddListener(ApplyPresetAll);
        BatchModeButton.isOn = false;
    }
    public void ApplyPresetAll()
    {
        var presetDropdown = SPUM_AnimationManager.presetDropdown;
        var presetData = SPUM_AnimationManager.SPUM_PresetData;
        int selectedIndex = presetDropdown.value;
        if (selectedIndex >= 0 && selectedIndex < presetData.Presets.Count)
        {
            SPUM_Preset selectedPreset = presetData.Presets[selectedIndex];
            Debug.Log($"Applying preset: {selectedPreset.UnitType} - {selectedPreset.PresetName}");
            ApplyPresetToAll(selectedPreset);
        }
        else
        {
            Debug.LogError("Invalid preset selection");
        }
    }
    public void ApplyPresetToAll(SPUM_Preset preset)
    {
        foreach (var kvp in sortedItems)
        {
            SPUM_Prefabs savedPrefab = kvp.Value;
            if(!savedPrefab.EditChk) continue;
            savedPrefab.spumPackages = preset.Packages;
            savedPrefab.PopulateAnimationLists();
            savedPrefab._version = SPUM_Manager._version;
            savedPrefab.EditChk = false;
            Debug.Log(savedPrefab._code);
        }
        SPUM_Manager.NewMake();
        DisplayPage();
    }

    public void LoadPrefabs(){
        SPUM_Manager.MissingPackageNames.Clear();
        LoadItems();
        CheckMissingPackages();
        DisplayPage();
        BatchModeButton.isOn = false;
        if (SPUM_Manager.MissingPackageNames.Count > 0)
        {
            var names = string.Join(", ", SPUM_Manager.MissingPackageNames);
            Debug.LogWarning($"[SPUM] Missing packages: {names}");
            SPUM_Manager.UIManager.ToastOn($"Missing packages: {names}");
        }
    }
    void CheckMissingPackages()
    {
        var installed = SPUM_Manager.SpritePackageNameList;
        foreach (var kvp in sortedItems)
        {
            // ImageElement에서 실제 사용 중인 패키지만 검사
            foreach (var elem in kvp.Value.ImageElement)
            {
                if (string.IsNullOrEmpty(elem.ItemPath)) continue;
                string[] pathParts = elem.ItemPath.Replace("\\", "/").Split('/');
                string pkgName = pathParts[0];
                if (pkgName == "Addons" && pathParts.Length >= 3) pkgName = pathParts[1];
                if (!installed.Contains(pkgName))
                    SPUM_Manager.MissingPackageNames.Add(pkgName);
            }
        }
    }
    void LoadItems()
    {
        if(sortedItems.Count> 0) return;
        sortedItems.Clear();
        deletedIndexes.Clear();
        nextAvailableIndex = 0;
        var SavedPrefabs = SPUM_Manager.fileHandler.Load();
        foreach (var prefab in SavedPrefabs)
        {
            AddItemToSortedDictionary(prefab);
        }
    }
    void AddItemToSortedDictionary(SPUM_Prefabs prefab)
    {
        int index;
        if (deletedIndexes.Count > 0)
        {
            index = deletedIndexes.Dequeue();
        }
        else
        {
            index = nextAvailableIndex++;
        }
        sortedItems[index] = prefab;
    }
    public void AddNewPrefab(SPUM_Prefabs newPrefab)
    {
        AddItemToSortedDictionary(newPrefab);
        DisplayPage();
    }
    public void DeleteUnit(int index, SPUM_Prefabs prefab)
    {
        if (sortedItems.Remove(index))
        {
            deletedIndexes.Enqueue(index);
        }
        SPUM_Manager.DeleteUnit(prefab);
    }
    void DisplayPage()
    {
        foreach (Transform child in contentParent)
        {
            Destroy(child.gameObject);
        }
        SelectAllToggleButton.onValueChanged.RemoveAllListeners();
        SelectAllToggleButton.isOn = false;

        int startIndex = (currentPage - 1) * itemsPerPage;
        int endIndex = Mathf.Min(startIndex + itemsPerPage, sortedItems.Count);
        int i = 0;
        foreach (var kvp in sortedItems)
        {
            if (i >= startIndex && i < endIndex)
            {
            GameObject PreviewElement = Instantiate(itemPrefab, contentParent);
            Text UnitName = PreviewElement.GetComponentInChildren<Text>();
            SPUM_Prefabs PreviewData = PreviewElement.GetComponentInChildren<SPUM_Prefabs>();
            SPUM_Prefabs SavedPrefab = kvp.Value;
            int prefabIndex = kvp.Key;
            PreviewData.spumPackages = SavedPrefab.spumPackages;
            PreviewData.ImageElement = SavedPrefab.ImageElement;

            var UnitType = SavedPrefab.UnitType.Equals("") ? "Unit" : SavedPrefab.UnitType;

            foreach (Transform child in PreviewData.transform)
            {
                child.gameObject.SetActive(child.name.Contains(UnitType));
            }
            var anim = PreviewData.GetComponentInChildren<Animator>();
            PreviewData._anim = anim;

            PreviewData.UnitType = UnitType;
            PreviewData.PopulateAnimationLists();
            PreviewData.OverrideControllerInit();

            if(contentParent.gameObject.activeInHierarchy && PreviewData._anim != null)
            {
                try
                {
                    PreviewData.PlayAnimation(PlayerState.IDLE, 0);
                }
                catch (System.ArgumentOutOfRangeException)
                {
                    Debug.LogWarning($"Animation index out of range for {SavedPrefab._code}, skipping animation play");
                }
            }

            var matchingTables = PreviewElement.GetComponentsInChildren<SPUM_MatchingList>();
            bool isInvalidPath = false;
            var allMatchingElements = matchingTables.SelectMany(mt => mt.matchingTables).ToList();
            foreach (var matchingElement in allMatchingElements)
            {
                var matchingTypeElement = SavedPrefab.ImageElement.FirstOrDefault(ie =>
                (ie.UnitType == matchingElement.UnitType)
                && (ie.PartType == matchingElement.PartType)
                && (ie.Dir == matchingElement.Dir)
                && (ie.Structure == matchingElement.Structure)
                && ie.PartSubType == matchingElement.PartSubType
                );
                if (matchingTypeElement != null && !string.IsNullOrEmpty(matchingTypeElement.ItemPath))
                {
                    matchingTypeElement.ItemPath = matchingTypeElement.ItemPath.Replace("/Unit/", "/0_Unit/");
                    var LoadSprite = SPUM_Manager.LoadSpriteFromMultiple(matchingTypeElement.ItemPath, matchingTypeElement.Structure);
                    isInvalidPath = LoadSprite == null;
                    matchingElement.renderer.sprite = LoadSprite;
                    matchingElement.renderer.maskInteraction = (SpriteMaskInteraction)matchingTypeElement.MaskIndex;
                    matchingElement.renderer.color = matchingTypeElement.Color;
                    matchingElement.ItemPath = matchingTypeElement.ItemPath;
                    matchingElement.MaskIndex = matchingTypeElement.MaskIndex;
                    matchingElement.Color = matchingTypeElement.Color;
                }
            }
            var ButtonPanel = PreviewElement.GetComponentInChildren<SPUM_LoadPrefabPanel>();
            ButtonPanel.UnitCodeText.text = SavedPrefab._code;

            ButtonPanel.CheckedButton.isOn = SavedPrefab.EditChk;
            ButtonPanel.CheckedButton.onValueChanged.AddListener((On) => {
                SavedPrefab.EditChk = On;
            });
            SelectAllToggleButton.onValueChanged.AddListener((On) => {
                if (ButtonPanel != null && ButtonPanel.CheckedButton != null)
                    ButtonPanel.CheckedButton.isOn = On;
            });
            BatchModeButton.onValueChanged.AddListener((On) => {
                if (ButtonPanel != null && ButtonPanel.CheckedButton != null)
                {
                    ButtonPanel.CheckedButton.gameObject.SetActive(On);
                    ButtonPanel.DeleteButton.gameObject.SetActive(!On);
                    ButtonPanel.SelectButton.gameObject.SetActive(!On);
                    CloseButton.gameObject.SetActive(!On);
                }
            });
            if (ButtonPanel != null && ButtonPanel.CheckedButton != null)
            {
                ButtonPanel.CheckedButton.gameObject.SetActive(BatchModeButton.isOn);
                ButtonPanel.DeleteButton.gameObject.SetActive(!BatchModeButton.isOn);
                ButtonPanel.SelectButton.gameObject.SetActive(!BatchModeButton.isOn);
            }
            ButtonPanel.SelectButton.onClick.AddListener(()=>
            {
                SPUM_Manager.EditPrefab = SavedPrefab;
                SPUM_Manager.UIManager.LoadButtonSet(true);
                SPUM_Manager.ItemResetAll();
                SPUM_Manager.SetType(SavedPrefab.UnitType);
                SPUM_Manager.ItemLoadButtonActive(SavedPrefab.ImageElement);
                SPUM_Manager.SetSprite(SavedPrefab.ImageElement);

                // 기존 모든 패키지를 깊은 복사
                var allPackages = SPUM_Manager.GetSpumPackageData();
                SPUM_Manager.PreviewPrefab.spumPackages = allPackages
                    .Select(p => (SpumPackage)p.Clone())
                    .ToList();

                for (int j = 0; j < SPUM_Manager.PreviewPrefab.spumPackages.Count; j++)
                {
                    var previewPackage = SPUM_Manager.PreviewPrefab.spumPackages[j];
                    var savedPackage = SavedPrefab.spumPackages
                        .FirstOrDefault(p => p.Name == previewPackage.Name);

                    for (int k = 0; k < previewPackage.SpumAnimationData.Count; k++)
                    {
                        var previewAnimData = previewPackage.SpumAnimationData[k];
                        var savedAnimData = savedPackage?.SpumAnimationData
                            .FirstOrDefault(a => a.Name == previewAnimData.Name);

                        if (savedAnimData != null)
                        {
                            SPUM_Manager.PreviewPrefab.spumPackages[j].SpumAnimationData[k].index = savedAnimData.index;
                            SPUM_Manager.PreviewPrefab.spumPackages[j].SpumAnimationData[k].HasData = savedAnimData.HasData;
                        }
                        else
                        {
                            SPUM_Manager.PreviewPrefab.spumPackages[j].SpumAnimationData[k].index = -1;
                            SPUM_Manager.PreviewPrefab.spumPackages[j].SpumAnimationData[k].HasData = false;
                        }
                    }
                }

                SPUM_Manager.PreviewPrefab._version = SavedPrefab._version;
                SPUM_Manager.PreviewPrefab._code = SavedPrefab._code;
                SPUM_Manager.UIManager._loadObjCanvas.SetActive(false);
                SPUM_Manager.animationManager.PlayFirstAnimation();
            });

            ButtonPanel.DeleteButton.onClick.AddListener(()=> {
                DeleteUnit(prefabIndex, SavedPrefab);
                DisplayPage();
            });

            // 누락 이미지 감지 — ImageElement에서 실제 사용 중인 이미지의 패키지가 설치되어 있는지
            var installed = SPUM_Manager.SpritePackageNameList;
            var missingImageSet = new HashSet<(string imageName, string packageName)>();
            foreach (var elem in SavedPrefab.ImageElement)
            {
                if (string.IsNullOrEmpty(elem.ItemPath)) continue;
                string[] pathParts = elem.ItemPath.Replace("\\", "/").Split('/');
                string pkgName = pathParts[0];
                if (pkgName == "Addons" && pathParts.Length >= 3) pkgName = pathParts[1];
                if (!installed.Contains(pkgName))
                {
                    string fileName = pathParts[pathParts.Length - 1];
                    missingImageSet.Add((fileName, pkgName));
                }
            }
            var missingImages = missingImageSet.ToList();

            // 애니메이션 클립 유효성 검사 — 프리팹의 실제 클립 리스트에서 null 레퍼런스 감지
            // spumPackages.SpumAnimationData에서 역매칭하여 패키지명/클립경로 복원
            var invalidClips = new List<(string clipName, string packageName)>();
            var animSourceMap = SavedPrefab.spumPackages
                .SelectMany(pkg => pkg.SpumAnimationData.Select(clip => new { pkg.Name, clip }))
                .Where(x => x.clip.HasData && x.clip.UnitType == UnitType && x.clip.index > -1)
                .GroupBy(x => x.clip.StateType)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(x => x.clip.index).ToList()
                );

            var clipLists = new Dictionary<string, List<AnimationClip>>
            {
                ["IDLE"] = SavedPrefab.IDLE_List,
                ["MOVE"] = SavedPrefab.MOVE_List,
                ["ATTACK"] = SavedPrefab.ATTACK_List,
                ["DAMAGED"] = SavedPrefab.DAMAGED_List,
                ["DEBUFF"] = SavedPrefab.DEBUFF_List,
                ["DEATH"] = SavedPrefab.DEATH_List,
                ["OTHER"] = SavedPrefab.OTHER_List
            };
            foreach (var clipEntry in clipLists)
            {
                if (clipEntry.Value == null) continue;
                for (int ci = 0; ci < clipEntry.Value.Count; ci++)
                {
                    if (clipEntry.Value[ci] == null)
                    {
                        string clipName = $"{clipEntry.Key}[{ci}]";
                        string pkgName = clipEntry.Key;
                        if (animSourceMap.TryGetValue(clipEntry.Key, out var sources) && ci < sources.Count)
                        {
                            var src = sources[ci];
                            clipName = System.IO.Path.GetFileName(src.clip.ClipPath);
                            pkgName = src.Name;
                        }
                        invalidClips.Add((clipName, pkgName));
                        Debug.LogWarning($"[SPUM] Missing animation clip, {clipName} (package: {pkgName}) in {SavedPrefab._code}");
                    }
                }
            }

            bool hasIssues = missingImages.Count > 0 || invalidClips.Count > 0;

            if (ButtonPanel.ConvertButton != null)
            {
                ButtonPanel.ConvertButton.transform.parent.gameObject.SetActive(hasIssues);
                ButtonPanel.SelectButton.image.enabled = !hasIssues;

                if (hasIssues)
                {
                    ButtonPanel.ConvertButton.onClick.AddListener(() => {
                        var convertView = SPUM_Manager.UIManager.ConvertView;
                        if (convertView != null)
                        {
                            convertView.Show(SavedPrefab._version, missingImages, invalidClips);

                            if (convertView.ForceSelectButton != null)
                            {
                                convertView.ForceSelectButton.onClick.RemoveAllListeners();
                                convertView.ForceSelectButton.onClick.AddListener(() => {
                                    convertView.gameObject.SetActive(false);
                                    ButtonPanel.SelectButton.onClick.Invoke();
                                });
                            }
                        }
                    });
                }
            }

            }
            i++;
            if (i >= endIndex) break;
        }

        pageText.text = $"Page {currentPage} / {Mathf.Ceil(sortedItems.Count / (float)itemsPerPage)}";
        prevButton.interactable = (currentPage > 1);
        nextButton.interactable = (endIndex < sortedItems.Count);
    }

    void PrevPage()
    {
        if (currentPage > 1)
        {
            currentPage--;
            DisplayPage();
        }
    }

    void NextPage()
    {
        if (currentPage < Mathf.CeilToInt(sortedItems.Count / (float)itemsPerPage))
        {
            currentPage++;
            DisplayPage();
        }
    }
}
