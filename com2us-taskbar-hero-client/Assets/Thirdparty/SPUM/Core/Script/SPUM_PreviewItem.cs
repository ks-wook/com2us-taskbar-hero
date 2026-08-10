using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class SPUM_PreviewItem : MonoBehaviour
{
    public Image _emptyImage;
    public Image _basicImage;
    public List<SpumPackage> spumPackages = new List<SpumPackage>();
    public List<PreviewMatchingElement> ImageElement = new();
    public SPUM_Manager _managerST => SoonsoonData.Instance._spumManager;
    public Button SetSpriteButton;
    
    void Start()
    {
        if(!TryGetComponent(out SetSpriteButton)){
            Debug.LogWarning(this.GetType() + "SetSpriteButton is missing");
            return;
        }
        SetSpriteButton.onClick.AddListener(SetSprite);
    }
    public void SetSprite()
    {
        _managerST.SetSprite(ImageElement);
    }
    public GameObject DeleteButton;
    public void DeleteObj()
    {
        Debug.Log("Delete");
        //_managerST.DeleteUnit(_index);
    }
}
