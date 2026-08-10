using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class SPUM_SpriteButtonST : MonoBehaviour
{
    private bool isActive = false;
    public bool IsSpriteFixed = false;
    public Image _mainSprite; // 선택된 파츠 아이콘 활성 상태 표시
    public Image _colorBG;  //선택된 컬러를 표시
    public SPUM_Manager _Manager;

    private Color partSpriteColor = Color.white;

    public Color InitColor;
    public List<GameObject> _LockBtn = new List<GameObject>();

    public Button DrawButton;
    public Button ChangeColorButton;
    public Button ChangeRandomButton;
    public Button ResetSpriteButton;
    public Button LockButton;
    public Toggle MaskToggle;
    public SPUM_SpriteButtonST ToggleTarget;
    public string Direction;
    public string UnitType;
    public string PartType;
    public string ItemShowType;
    public string DefaultPackageName = "Legacy";
    public string DefaultTextureName;
    public List<string> ignoreColorPart = new ();
    public SpriteMaskInteraction SpriteMask = SpriteMaskInteraction.None;
    public bool IsActive 
    {
        get { return isActive; }
        set
        {
            isActive = value;
            SetActiveColor(value);
        }
    }

    public Color PartSpriteColor
    { 
        get { return partSpriteColor; }
        set
        {
            partSpriteColor = value;
            SetSpriteColor(value);
        }
    }
    void Awake(){
        partSpriteColor = InitColor;
        UnitType = string.IsNullOrEmpty(UnitType) ? "Unit" : UnitType;
        PartType =  string.IsNullOrEmpty(PartType) ? gameObject.name : PartType;
        ItemShowType = gameObject.name;
    }
    void Start()
    {
        if(_mainSprite == null ) _mainSprite = transform.GetChild(0).GetChild(1).GetComponent<Image>();
        if(_Manager == null ) {
            #if UNITY_2023_1_OR_NEWER
                _Manager = FindFirstObjectByType<SPUM_Manager>();
            #else
                #pragma warning disable CS0618
                _Manager = FindObjectOfType<SPUM_Manager>();
                #pragma warning restore CS0618
            #endif
            }

        DrawButton = GetComponent<Button>();
        ChangeColorButton = transform.Find("ButtonSet/ButtonColor")?.GetComponent<Button>();
        ChangeRandomButton = transform.Find("ButtonSet/ButtonRandom")?.GetComponent<Button>();
        ResetSpriteButton = transform.Find("ButtonSet/ButtonDelete")?.GetComponent<Button>();
        LockButton = transform.Find("ButtonSet/LockBG")?.GetComponent<Button>();

        DrawButton.onClick.AddListener(()=> {DrawItem();});
        ChangeColorButton?.onClick.AddListener(()=> { 
            if(IsActive) {
                _Manager.UIManager.SetColorButton(this);
            }else{
                _Manager.UIManager.ToastOn(this.name + " No Selected");
            }
        });
        ChangeRandomButton?.onClick.AddListener(()=> { 
            if(IsActive || !IsSpriteFixed) {
                SetPartRandom();
            }else{
                _Manager.UIManager.ToastOn(this.name + " is Locked or No Selected");
            }
             
        });
        ResetSpriteButton?.onClick.AddListener(()=> {
            if(!IsSpriteFixed) {
                RemoveSprite();
            }else{
                _Manager.UIManager.ToastOn(this.name + " is Locked");
            }
        }
        );
        LockButton?.onClick.AddListener(()=> {
            ChangeLock();
            _Manager.UIManager.ToastOn(this.name + " is Locked " + IsSpriteFixed);
        });

        MaskToggle?.onValueChanged.AddListener((On) => {
                SpriteMask = On ? SpriteMaskInteraction.VisibleInsideMask : SpriteMaskInteraction.None;
                _Manager.SetSpriteVisualMaskIndex(this);
        });
    }

    public void SetSpriteColor(Color color)
    {
        _colorBG.color = color;
        _Manager.SetSpriteColor(this);
    }
    public void SetActiveColor(bool value)
    {
        if(value)
        {
            _mainSprite.color = Color.red;
            if(ToggleTarget) {
                ToggleTarget.SpriteMask = SpriteMaskInteraction.VisibleInsideMask;
                _Manager.SetSpriteVisualMaskIndex(ToggleTarget);
                
            }//_Manager.SetSpriteVisualMaskIndex(ToggleTarget, SpriteMaskInteraction.VisibleInsideMask);
        }
        else
        {
            _mainSprite.color = Color.white;
            _colorBG.color = Color.white;
            if(ToggleTarget) 
            {
                ToggleTarget.SpriteMask = SpriteMaskInteraction.None;
                _Manager.SetSpriteVisualMaskIndex(ToggleTarget);
            } //_Manager.SetSpriteVisualMaskIndex(ToggleTarget, SpriteMaskInteraction.None);
        }
        ToggleTarget?.MaskToggle?.SetIsOnWithoutNotify(value);
    }
    public void DrawItem()
    {
        _Manager.DrawItemList(this);
    }
    public void SetPartRandom()
    {
        if(IsSpriteFixed) return;
        IsActive = true;
        if(_Manager.RandomColorButton.isOn) ChangeRandomColor();
        _Manager.SetPartRandom(this);
    }
    public void SetInitPart()
    {
        IsActive = true;
        _Manager.SetDefaultPart(this);
    }
    public void ChangeRandomColor()
    {
        if(IsSpriteFixed) return;

        Color Color = Color.white;
        if(Random.Range(0, 1.0f) > 0.1f) 
        {
            Color = new Color(Random.Range(0,1f),Random.Range(0,1f),Random.Range(0,1f),1f);
            IsActive = true;
            ToggleTarget?.RemoveSprite();
        }
        else{
            IsActive = false;
        }

        PartSpriteColor = Color;
    }
    public void RemoveSprite()
    {
        if(IsSpriteFixed) return;
        IsActive = false;
        _Manager.RemoveSprite(this);
        MaskToggle?.SetIsOnWithoutNotify(false);
    }
    public void ChangeLock()
    {
        IsSpriteFixed = !IsSpriteFixed;
        if(_LockBtn[0].activeInHierarchy)
        {
            _LockBtn[0].SetActive(false);
            _LockBtn[1].SetActive(true);
        }
        else
        {
            _LockBtn[0].SetActive(true);
            _LockBtn[1].SetActive(false);
        }
    }
    // public void SetToggleTargetVisualMask()
    // {
    //     ToggleTarget?.RemoveSprite();
    // }
}
