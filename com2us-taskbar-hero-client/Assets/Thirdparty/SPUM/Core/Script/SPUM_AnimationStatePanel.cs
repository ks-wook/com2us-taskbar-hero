using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class SPUM_AnimationStatePanel : MonoBehaviour
{
    public Text SelectedStateText;
    public Button AddClipButton;
    public Button ResetButton;
    public Button CloseButton;
    public Transform parent;
    public SPUM_AnimationStateElement StateElementPrefab;
    
    public void CreateStateButton(SPUM_AnimationManager manager) 
    {
        List<SpumPackage> spumPackages = manager.unit.spumPackages;
        string type = manager.SelectedType;
        string UnitType = manager.unit.UnitType;
        SelectedStateText.text = $"{type}"; // $"SELECTED - {state}";
        
        foreach (Transform element in parent)
        {
            Destroy(element.gameObject);
        }

        var filteredClips = spumPackages
            .SelectMany(package => package.SpumAnimationData.Select(clip => new { 
                Clip = clip, 
                Package = package 
            }))
            .Where(item => item.Clip.StateType.Equals(type) && item.Clip.HasData && item.Clip.UnitType.Equals(UnitType))
            .OrderBy(item => item.Clip.index);
        int index = 0;
        foreach (var Items in filteredClips)
        {
            var clip = Items.Clip;
            var Package = Items.Package;
            int Clipindex = index;

            var StateButton = Instantiate(StateElementPrefab, parent);
            StateButton.StatePackageText.text = Package.Name;
            var SubCategory = string.IsNullOrEmpty( clip.SubCategory) ? clip.StateType : clip.SubCategory;
            StateButton.StateSubTypeText.text = $"{SubCategory}";
            StateButton.UpButton.onClick.AddListener(
            () =>
            {
                manager.IndexSawp(Clipindex, -1);
            });
            StateButton.DownButton.onClick.AddListener(
            () =>
            {
                manager.IndexSawp(Clipindex, 1);
            });
 
            
            StateButton.NameButton.group = parent.GetComponent<ToggleGroup>();
            StateButton.SetData(index, clip.Name);
            clip.index = index;
            StateButton.NameButton.onValueChanged.AddListener((value)=>{
                manager.PlayAnimation(clip);
             });

            StateButton.RemoveButton.onClick.AddListener(()=>{
                clip.HasData = false;
                manager.RefreshStatePanel();
            });
            index++;
        }

        manager.ScrollContentReset();
    }
}
