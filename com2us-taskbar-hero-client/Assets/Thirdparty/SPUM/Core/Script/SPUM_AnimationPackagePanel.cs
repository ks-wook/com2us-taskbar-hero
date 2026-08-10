using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class SPUM_AnimationPackagePanel : MonoBehaviour
{
    public Text SelectedStateText;
    public Button CloseButton;
    public Transform parent;
    public SPUM_PackagePanel spumPackagePanel;

    // 모든 패키지 생성하기
    public void CreateSpumAnimationPackagePanel(SPUM_AnimationManager manager)
    {
        string SelectedType = manager.SelectedType;
        List<SpumPackage> spumPackages = manager.unit.spumPackages;
        foreach (Transform element in parent)
        {
            Destroy(element.gameObject);
        }
        
        foreach (var Package in spumPackages)
        {
            var PackagePanel = Instantiate(spumPackagePanel, parent);
            PackagePanel.CreatePackageUI(Package, manager);
        }
    }
}