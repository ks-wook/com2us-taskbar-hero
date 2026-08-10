using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;


//using System.IO;
using UnityEngine;
using UnityEngine.UI;

public class SPUM_AnimationManager : MonoBehaviour
{
    [Header("Animation Play Controller")]
    [SerializeField] Slider timeLineSlider;
    [SerializeField] Slider playSpeedSlider;
    [SerializeField] Text slidertimeLineInfo;
    [SerializeField] Text timeLineText;
    [SerializeField] Text playSpeedText;
    
    public SPUM_Prefabs unit => SPUM_Manager.PreviewPrefab;
    public Transform StatePanel;
    public Button StateButtonPrefab;

    public string SelectedType;
    //private AnimatorOverrideController animatorOverrideController;
    public SPUM_AnimationControllerPanel AnimationControllerPanel;
    public SPUM_AnimationStatePanel spumAnimationStatePanel;
    public SPUM_AnimationPackagePanel spumAnimationPackagePanel;
    public string CurrentPlayClip;

    public RectTransform rectTransform;

    [Header("Animation Preset")]
    public SPUM_AnimationPreset PresetPrefab;
    public Transform PresetContent;
    public Button AddPresetButton;
    public SPUM_PresetData SPUM_PresetData;
    public Toggle PresetTogle;
    public Dropdown presetDropdown;
    [Header("Manager")]
    public SPUM_Manager SPUM_Manager;
    public void ScrollContentReset(){
        LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);
    }
    public void PlayAnimation(SpumAnimationClip currentPlayClip){
        Animator animator = unit._anim;
        AnimatorOverrideController animatorOverrideController = animator.runtimeAnimatorController as AnimatorOverrideController;

        var PlayState = $"{currentPlayClip.StateType}";
        animator.Rebind();
        animator.Update(0f);
        
        animatorOverrideController[PlayState] = LoadAnimationClip(currentPlayClip.ClipPath);

        animator.SetBool("1_Move", PlayState.Contains("MOVE"));
        animator.SetBool("5_Debuff", PlayState.Contains("DEBUFF"));
        animator.SetBool("isDeath", PlayState.Contains("DEATH"));
        animator.Play(PlayState, 0, 0);
        AnimationControllerPanel.RefreshSlier(currentPlayClip.ClipPath);
    }

    AnimationClip LoadAnimationClip(string clipPath)
    {
        AnimationClip clip = Resources.Load<AnimationClip>(clipPath.Replace(".anim", ""));
        
        if (clip == null)
        {
            Debug.LogWarning($"Failed to load animation clip '{clipPath}'.");
        }
        
        return clip;
    }
    public void CloseAnimationPanels(){
        spumAnimationStatePanel.gameObject.SetActive(false);
        spumAnimationPackagePanel.gameObject.SetActive(false);
    }
    
    void InitAnimator()
    {
        foreach (var anim in unit.GetComponentsInChildren<Animator>(true))
        {
            Animator animator = anim; //unit._anim;
            var animatorOverrideController = new AnimatorOverrideController();
            animatorOverrideController.runtimeAnimatorController= animator.runtimeAnimatorController;

            AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;

            foreach (AnimationClip clip in clips)
            {
                animatorOverrideController[clip.name] = clip;
            }

            animator.runtimeAnimatorController = animatorOverrideController;
        }
    }

    public void InitializeDropdown()
    {
        presetDropdown.ClearOptions();

        List<string> options = new List<string>();
        foreach (SPUM_Preset preset in SPUM_PresetData.Presets)
        {
            options.Add($"{preset.UnitType} - {preset.PresetName}");
        }

        presetDropdown.AddOptions(options);
    }

    void Start()
    {
        InitAnimator();
        CreateSpumAnimationTypeButton();
        AnimationControllerPanel.Init(unit);

        spumAnimationStatePanel.CloseButton.onClick.AddListener( ()=>{
            spumAnimationStatePanel.gameObject.SetActive(false);
            } );
        spumAnimationPackagePanel.CloseButton.onClick.AddListener( ()=>{

            spumAnimationStatePanel.CreateStateButton(this);
            spumAnimationPackagePanel.gameObject.SetActive(false);
            } );
        spumAnimationStatePanel.ResetButton.onClick.AddListener(()=>{
            //AllResetIndex();
            ResetSelectedStateTypeIndex();
            // 새로 패널 그리기
            RefreshStatePanel();

        });
        InitPreviewUnitPackage();
        LoadPresetLst();
        AddPresetButton.onClick.AddListener(()=> { 
            Debug.Log("PresetAdd"); 
            AddPreset();
            
            });
        PresetTogle.onValueChanged.AddListener((On)=> {
            if(On) {
                LoadPresetLst();
            }
        });
        InitializeDropdown();
    }
    [ContextMenu("TESE")]
    public void LoadPresetLst()
    {
        foreach (Transform element in PresetContent)
        {
            Destroy(element.gameObject);
        }
        if(SPUM_PresetData == null) return;
        var filteredPresets = FilterPresetsByUnitType(unit.UnitType);

        foreach (var presetData in filteredPresets)
        {
            var Preset = Instantiate(PresetPrefab, PresetContent);
            Preset.Init(presetData, this);
        }
    }
    public List<SPUM_Preset> FilterPresetsByName(string presetName)
    {
        var filteredPresets = SPUM_PresetData.Presets.Where(p => p.PresetName == presetName).ToList();
        return filteredPresets;
    }

    public List<SPUM_Preset> FilterPresetsByUnitType(string unitType)
    {
        var filteredPresets = SPUM_PresetData.Presets.Where(p => p.UnitType == unitType).ToList();
        return filteredPresets;
    }
    public void AddPreset(){
        var Preset = Instantiate(PresetPrefab, PresetContent);
        SPUM_Preset presetData = new SPUM_Preset
        {
            UnitType = unit.UnitType,
            PresetName = System.DateTime.Now.ToString("yyyyMMddHHmmssfff"),
            Packages = unit.spumPackages?.Select(p => (SpumPackage)p.Clone()).ToList()
        };

        SavePresetData(presetData);
    }
    public void EditPresetData(string PreviousName, string ChangedName)
    {
        SPUM_Preset presetToRemove = SPUM_PresetData.Presets.Find(p => p.PresetName == PreviousName);
        if (presetToRemove != null)
        {
            presetToRemove.PresetName = ChangedName;
            LoadPresetLst();
            Debug.Log("Changed: " + ChangedName);
        }
        else
        {
            Debug.LogWarning("Preset not found: " + PreviousName);
        }
    }
    public void SavePresetData(SPUM_Preset presetData)
    {
        SPUM_PresetData.Presets.Add(presetData);
        // EditorUtility.SetDirty(SPUM_PresetData);
        // AssetDatabase.SaveAssets();
        LoadPresetLst();
        Debug.Log("Preset saved: " + presetData.PresetName);
    }
    public void DeletePresetData(string name)
    {
        // 프리셋 데이터를 삭제하는 로직을 추가합니다.
        SPUM_Preset presetToRemove = SPUM_PresetData.Presets.Find(p => p.PresetName == name);
        if (presetToRemove != null)
        {
            SPUM_PresetData.Presets.Remove(presetToRemove);
            Debug.Log("Preset deleted: " + presetToRemove.PresetName);
            LoadPresetLst();
        }
        else
        {
            Debug.LogWarning("Preset not found: " + unit._code);
        }
    }
    public void ApplyPreset(SPUM_Preset preset)
    {
        unit.spumPackages = preset.Packages;
        PresetTogle.isOn = false;
        PlayFirstAnimation() ;
    }
    public void PlayFirstAnimation() 
    {
        var clip = unit.spumPackages
            .SelectMany(package => package.SpumAnimationData)
            .FirstOrDefault(data => 
                data.StateType.Equals("IDLE", System.StringComparison.OrdinalIgnoreCase) && 
                data.HasData && 
                data.index == 0 && 
                data.UnitType.Equals(unit.UnitType));
        if (clip == null) {
            Debug.LogWarning("package data error");
            unit.spumPackages  = SPUM_Manager.GetSpumLegacyData();
            return;
        }
        PlayAnimation(clip);
    } 


    void CreateSpumAnimationTypeButton()
    {
        var UnitStateData = unit.spumPackages;

        spumAnimationStatePanel.AddClipButton.onClick.RemoveAllListeners();
       
        foreach (string state in  SPUM_Manager.StateList)
        {
            var StateButton = Instantiate(StateButtonPrefab, StatePanel);
            StateButton.GetComponentInChildren<Text>().text = state;

            var stateType = state;
            StateButton.onClick.AddListener( () => 
            {
                SelectedType = stateType;
                spumAnimationPackagePanel.gameObject.SetActive(false);
                spumAnimationStatePanel.gameObject.SetActive(true);
                spumAnimationStatePanel.CreateStateButton(this);
            } );
        }

        // 클립 추가 버튼 데이터 할당
        spumAnimationStatePanel.AddClipButton.onClick.AddListener( () =>
        {
            spumAnimationPackagePanel.gameObject.SetActive(true);
            spumAnimationPackagePanel.CreateSpumAnimationPackagePanel(this);
      
        });
    }

    public void RefreshStatePanel(){
        spumAnimationStatePanel.CreateStateButton(this);
        
    }
    public void InitPreviewUnitPackage(){
        unit.spumPackages =  SPUM_Manager.GetSpumLegacyData();
        ResetSelectedStateTypeIndex();
        PlayFirstAnimation();
    }
    public void ResetSelectedStateTypeIndex()
    {
        unit.spumPackages = SPUM_Manager.spumPackages;
        var UnitPackagesData = unit.spumPackages;
        string SelectState = SelectedType;
        string UnitType = unit.UnitType;

        foreach (var package in UnitPackagesData)
        {
            foreach (var clipData in package.SpumAnimationData)
            {
                if (clipData.StateType.Equals(SelectedType) && clipData.UnitType.Equals(unit.UnitType))
                {
                    clipData.index = -1;
                    clipData.HasData = false;
                }
            }
        }
SpumPackage legacyPackage = null;

foreach (var package in UnitPackagesData)
{
    if (package.Name.Trim().Equals("Legacy", StringComparison.OrdinalIgnoreCase))
    {
        legacyPackage = package;
        break;
    }
}
        //var legacyPackage = UnitPackagesData.Where(p => p.Name.Trim().Equals("Legacy", StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
        if (legacyPackage != null)
        {
            var relevantClips = legacyPackage.SpumAnimationData
                .Where(clipData => 
                    clipData.StateType.Equals(SelectedType) &&
                    clipData.UnitType.Equals(unit.UnitType))
                .ToList();
            for (int i = 0; i < relevantClips.Count; i++)
            {
                //Debug.Log(relevantClips[i].Name);
                relevantClips[i].index = i;
                relevantClips[i].HasData = true;
            }
        }
    }
    public void IndexSawp(int clipindex, int dir){
        if(clipindex == 0  && dir < 0) return;

        var UnitPackagesData = unit.spumPackages;
        var filteredClips = UnitPackagesData
        .SelectMany(package => package.SpumAnimationData)
        .Where(clip => clip.StateType.Equals(SelectedType) && clip.HasData && clip.UnitType.Equals(unit.UnitType)).OrderBy(clip => clip.index).ToList();
        

        int currentIndex = clipindex;
        int swapTargetIndex = clipindex + dir;
        if(swapTargetIndex == filteredClips.Count) return;
        //Debug.Log($"{currentIndex}==>{swapTargetIndex} {filteredClips.Count}");
        filteredClips[currentIndex].index = swapTargetIndex;
        filteredClips[swapTargetIndex].index = currentIndex;
        RefreshStatePanel();
    }
}