using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;


public class SPUM_PackageButton : MonoBehaviour
{
    public int _index; // 패키지의 인덱스
    public Text _title; // 패키지의 이름
    public Toggle PackageToggleButton;
    public SPUM_UIManager _Manager;
    void Start()
    {
        PackageToggleButton.GetComponent<SPUM_PackageButton>();
    }

    public void SetInit(int index, string PackageName, SPUM_UIManager manager, SPUM_SpriteButtonST button)
    {
        _Manager = manager;
        _title.text = PackageName;
        _index = index;

        PackageToggleButton.onValueChanged.AddListener((On) => {
            Debug.Log(PackageName + " " + On);
            if(_Manager.SpritePackagesFilterList.TryGetValue(PackageName, out bool value)){
                _Manager.SpritePackagesFilterList[PackageName] = On;
                button.DrawItem();
                SoonsoonData.Instance.SavePackageData(_Manager.SpritePackagesFilterList);
            }
        });
    }
}
