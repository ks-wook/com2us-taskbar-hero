using UnityEngine;
using System.Collections.Generic;
using UnityEngine.UI;

public class SPUM_PackagePanel : MonoBehaviour
{
    //public List<SpumAnimationClip> spumPackageElements;
    public Text PackageTitle;
    public SPUM_PackageElement spumPackageElement;

    // 패키지에 있는 클립 정보 생성
    public void CreatePackageUI(SpumPackage packageData, SPUM_AnimationManager manager) 
    {
        PackageTitle.text = packageData.Name;
        CreatePackageElementButton(packageData, manager);
    }

    // 스테이트에 맞는 클립 정보 생성
    void CreatePackageElementButton(SpumPackage packageData, SPUM_AnimationManager manager)
    {
        List<SpumAnimationClip> spumPackageElements = packageData.SpumAnimationData;
        spumPackageElement.gameObject.SetActive(true);

        var Parent = spumPackageElement.transform.parent;
        var selectedType = manager.SelectedType;
        foreach (var clip in spumPackageElements)
        {
            if(!clip.StateType.Equals(selectedType)) continue;
            if(!clip.UnitType.Equals(manager.unit.UnitType)) continue;
            var element = Instantiate(spumPackageElement, Parent);
            element.AnimName.text = clip.Name.Replace(".anim", "");
            element.SelectButton.isOn = clip.HasData;
            element.PlayButton.onClick.AddListener(
                ()=>
                {
                    manager.PlayAnimation( clip);
                    manager.CurrentPlayClip = clip.ClipPath;
                }
            );
            element.SelectButton.onValueChanged.AddListener( 
                (value) => 
                {
                    clip.HasData = value;
                }
            );
        }

        if(Parent.childCount.Equals(1)) Destroy(gameObject);

        spumPackageElement.gameObject.SetActive(false);

        var rectTransform = transform.parent.GetComponent<RectTransform>();
        LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);
    }
}