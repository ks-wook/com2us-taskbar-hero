using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

#if UNITY_6000_0_OR_NEWER
using UnityEngine.InputSystem;
#endif

public class SPUM_UIManager : MonoBehaviour
{
    [Header("▼ Version")] [Space(5)]
    public Text _spumVersion;
    public string SpumUnitPrefix;
    public string UniqueID;
    public Text _unitCode;
    public Text _unitNumber;
    public Text _panelTitle;

    [Header("▼ ConvertView")] [Space(5)]
    public SPUM_ConvertView ConvertView;

    [Header("▼ Toast")] [Space(5)]
    [SerializeField] private CanvasGroup _toastObj;
    [SerializeField] private Text _toastMSG;
    public GameObject _loadObjCanvas;
    public Transform _loadPool;
    public Button CloseLoadPrefabPanelButton;
    private const float FADE_START_TIME = 2.0f;
    private const float TOTAL_DURATION = 3.0f;

    private float toastTimer;
    private bool isToastActive;

    [Header("▼ Package")] [Space(5)]
    public Transform _packageButtonPool;        // 위치 
    public ScrollRect _packageButtonScroll;     // 스크롤뷰
    public GameObject _packageButtonObj;        // 패키지 버튼 프리펩
    public GameObject _childItem;   // 스펌 이미지 아이템 프리펩
    public Transform _childPool;    // 이미지 아이템 보여질 위치
    public Dictionary<string, bool> SpritePackagesFilterList = new Dictionary<string, bool>(); //보여질 패키지 상태 관리
    [Header("▼ Button")] [Space(5)]
    public List<GameObject> _buttonList = new List<GameObject>();
    public Button NewMakeButton;
    public Button DataLoadButton;
    public Button EditButton;
    public Button SaveButton;
    public GameObject _noticeObj;
    public Text _noticeText;
    public List<Button> _buttonSet = new List<Button>();
    public int callbackNum = 0;

    [Header("▼ Charactor View Zoom")] [Space(5)]
    public Transform _characterPivot;
    public Button PlusButton;
    public Button MiunusButton;

    [Header("▼ Color Picker")] [Space(5)]
    public Image _nowColorShow;
    public InputField _hexColorText;
    public List<GameObject> _colorPanelType = new List<GameObject>();
    public List<ColorSelect> _colorSaveList = new List<ColorSelect>();
    public int _nowSelectColorNum;
    public GameObject _nowSelectColor;
    public GameObject _colorPicker;

    public Color _basicColor;
    private Color nowColor;

    public GameObject SpritePanel;
    public Button SpritePanelCloseButton;
    private Texture2D tex;
    public SPUM_SpriteButtonST NowSelectedButton;

    [Header("▼ Manager")] [Space(5)]
    public SPUM_AnimationManager animationManager;
    public SPUM_Manager spumManager;
    public Color NowColor 
    { 
        get { return nowColor; }
        set
        {
            nowColor = value;
            OnColorChanged(value);
        }
    }
    void OnColorChanged(Color color)
    {
        _nowColorShow.color = color;
        _hexColorText.text = ColorUtility.ToHtmlStringRGB(color);
        NowSelectedButton.PartSpriteColor = color;
    }   
    void Start()
    {
        #if UNITY_6000_0_OR_NEWER
        CheckInputSystemUIModule();
        #endif
        NewMakeButton.onClick.AddListener(()=>{  spumManager.NewMake();  });
        DataLoadButton.onClick.AddListener(()=>{   spumManager.OpenLoadData();  });
        EditButton.onClick.AddListener(()=>{   spumManager.EditPrefabs();  });
        SaveButton.onClick.AddListener(()=>{   spumManager.SavePrefabs();  });
        CloseLoadPrefabPanelButton.onClick.AddListener(() => { SetActiveLoadPanel(false); });
        
        _buttonSet[0].onClick.AddListener(()=> {});
        _buttonSet[1].onClick.AddListener(()=> {});

        PlusButton.onClick.AddListener(()=> {SetCharPivotSize(0.1f);});
        MiunusButton.onClick.AddListener(()=> {SetCharPivotSize(-0.1f);});

        SpritePanelCloseButton.onClick.AddListener(()=>{ DrawItemOff(); });

        _spumVersion.text = "VER " +  SoonsoonData.Instance._spumManager._version;
        SpumUnitPrefix = string.IsNullOrWhiteSpace(SpumUnitPrefix) ? "SPUM" : SpumUnitPrefix;
        SetPackageActiveStateList();
        ResetUniqueID();
        ShowNowUnitNumber();
        spumManager.PreviewPrefab._code =  _unitCode.text;
    }
    public void ResetUniqueID() 
    {
        UniqueID = System.DateTime.Now.ToString("yyyyMMddHHmmssfff");
        _unitCode.text = SpumUnitPrefix + "_" + UniqueID;
    }
    public void ShowNowUnitNumber()
    {
        var SavedPrefabs = Resources.LoadAll<SPUM_Prefabs>("");
        _unitNumber.text = "All " + SavedPrefabs.Length.ToString("D3") + " Unit";
    }
    void Update()
    {
        if (isToastActive)
        {
            toastTimer += Time.deltaTime;
            
            if (toastTimer > FADE_START_TIME)
            {
                _toastObj.alpha = 1.0f - (toastTimer - FADE_START_TIME);
            }
            
            if (toastTimer > TOTAL_DURATION)
            {
                CloseToast();
            }
        }
    }
    public void SetPackageActiveStateList(){
        var saved = SoonsoonData.Instance.LoadPackageData();
        SpritePackagesFilterList = spumManager.SpritePackageNameList.ToDictionary(
            name => name,
            name => saved.ContainsKey(name) ? saved[name] : true
        );
    }
    public void SetPackageButtons(SPUM_SpriteButtonST ButtonData)
    {
        var packageList = SpritePackagesFilterList;
        foreach (Transform obj in _packageButtonPool)
        {
            Destroy(obj.gameObject);
        }

        if (packageList.Count == 0) return;
        foreach (var item in packageList)
        {
            bool hasPackageWithPart = spumManager.spumPackages
            .Any(package => 
                package.SpumTextureData.Count > 0 && 
                package.Name == item.Key &&
                package.SpumTextureData.Any(textureData => textureData.PartType == ButtonData.PartType)
            );
            if(!hasPackageWithPart) continue;
            GameObject PackageButtonObject = Instantiate(_packageButtonObj, _packageButtonPool);
            PackageButtonObject.transform.localScale = Vector3.one;
            SPUM_PackageButton PackageButton = PackageButtonObject.GetComponent<SPUM_PackageButton>();
            PackageButton.PackageToggleButton.isOn = item.Value;
            PackageButton.SetInit(0, item.Key, this, ButtonData);
        }
    }
    public void ShowItem() 
    {
        _packageButtonScroll.verticalNormalizedPosition = 1f; 
        ShowItemPanel();
        animationManager.CloseAnimationPanels();
    }

    public void ClearPreviewItems(){
        if(_childPool.childCount > 0)
        {
            for(var i=0; i < _childPool.childCount;i++)
            {
                Destroy(_childPool.GetChild(i).gameObject);
            }
        }
    } 

    public SPUM_PreviewItem CreatePreviewItem(){ 
        GameObject ttObj = Instantiate(_childItem, _childPool);
        ttObj.transform.localScale = new Vector3(1,1,1);
        SPUM_PreviewItem ttObjST = ttObj.GetComponent<SPUM_PreviewItem>();
        return ttObjST;
    }
    public void OnNotice(string text,int type = 0, int callback = -1)
    {
        _noticeObj.SetActive(true);
        _noticeText.text = text;
        callbackNum = callback;

        if(type == 0 ) //버튼 사용 선택
        {
            _buttonSet[0].transform.parent.gameObject.SetActive(true);
            _buttonSet[1].transform.parent.gameObject.SetActive(false);
        }
        else
        {
            _buttonSet[0].transform.parent.gameObject.SetActive(false);
            _buttonSet[1].transform.parent.gameObject.SetActive(true);
        }
    }

    public void CloseNotice()
    {
        if(callbackNum!=1)CloseOnlyNotice();
        switch(callbackNum)
        {
            case 0:
            break;

            case 1:
            Debug.Log("Please Check Error Message");
            break;
        }
    }

    public void CloseOnlyNotice()
    {
        _noticeObj.SetActive(false);
    }
    public void LoadButtonSet(bool value)
    {
        _buttonList[0].SetActive(!value);
        _buttonList[1].SetActive(value);
    }
    public void ClearChildTransform(){
        if(_loadPool.childCount > 0)
        {
            for(var i=0; i < _loadPool.childCount;i++)
            {
                Destroy(_loadPool.GetChild(i).gameObject);
            }
        }
    }
    public void SetActiveLoadPanel(bool isActive)
    {
        _loadObjCanvas.SetActive(isActive);
    }
    
    // public string GetFileName()
    // {
    //     string tName ="Unit";
    //     int tNameNum = 0;
    //     var _prefabUnitList = SoonsoonData.Instance._spumManager._prefabUnitList;
    //     List<string> _prefabNameList = new List<string>();
    //     for(var i = 0 ; i < _prefabUnitList.Count;i++)
    //     {
    //         _prefabNameList.Add(_prefabUnitList[i].name);
    //     }

    //     for(var i = 0; i < 10000; i++)
    //     {
    //         if(_prefabNameList.Contains(tName+i.ToString("D3")) == false)
    //         {
    //             tNameNum = i;
    //             break;
    //         }
    //     }
        
    //     tName = tName + tNameNum.ToString("D3");
    //     return tName;
    // }
    void CloseToast()
    {
        isToastActive = false;
        toastTimer = 0;
        _toastObj.gameObject.SetActive(false);
    }
    public void ToastOn(string text)
    {
        // 이전 토스트가 활성화되어 있다면 즉시 종료
        if (isToastActive)
        {
            CloseToast();
        }

        // 새로운 토스트 표시
        _toastObj.gameObject.SetActive(true);
        _toastObj.alpha = 1.0f;
        _toastMSG.text = text;
        toastTimer = 0;
        isToastActive = true;
    }
    public void DrawItemOff()
    {
        SpritePanel.SetActive(false);
    }
    public void ShowItemPanel()
    {
        SpritePanel.SetActive(true);
    }
    public void SetCharPivotSize( float num )
    {
        _characterPivot.localScale += new Vector3(num,num,num);

        if( _characterPivot.localScale.x < 0.5f) 
        {
            _characterPivot.localScale = new Vector3(0.5f,0.5f,0.5f);
            ToastOn("Reached Minimum size");
        }
        if(_characterPivot.localScale.x > 1.1f)
        {
            _characterPivot.localScale = new Vector3(1.1f,1.1f,1.1f);
            ToastOn("Reached Maximum size");
        }
    }
    #region ColorPicker Function
    public void DeleteSelectColor()
    {
        if(!_nowSelectColor.activeInHierarchy) return;
        _colorSaveList[_nowSelectColorNum]._savedColor.gameObject.SetActive(false);
        SoonsoonData.Instance._soonData2._savedColorList[_nowSelectColorNum]="";
        _nowSelectColor.SetActive(false);
        SoonsoonData.Instance.SaveData();
    }
    
    public void SetColorPickerPanel(int num)
    {
        foreach( var obj in _colorPanelType )
        {
            obj.SetActive(false);
        }

        _colorPanelType[num].SetActive(true);
    }
    public void SetColorButton(SPUM_SpriteButtonST button)
    {
        _colorPicker.SetActive(true);
        NowSelectedButton = button;
        NowColor =  button.PartSpriteColor;
    }
    public void PickColor()
    {
        tex = new Texture2D(1, 1);
        StartCoroutine(CaptureTempArea());
    }

    IEnumerator CaptureTempArea() {
        yield return new WaitForEndOfFrame();
        
        Vector2 pos;
        #if UNITY_6000_0_OR_NEWER
        pos = Mouse.current.position.ReadValue();
        #else
        pos = Input.mousePosition;
        #endif

        tex.ReadPixels(new Rect(pos.x, pos.y, 1, 1), 0, 0);
        tex.Apply();
        NowColor = tex.GetPixel(0, 0);

        yield return new WaitForSecondsRealtime(0.1f);

        _nowColorShow.color = NowColor;
        _hexColorText.text = ColorUtility.ToHtmlStringRGB(NowColor);
        NowSelectedButton.PartSpriteColor = NowColor;
    }
    public void CloseColorPick()
    {
        _colorPicker.SetActive(false);
    }
    //#if UNITY_EDITOR
    public void CopyToClipboard()
    {
        GUIUtility.systemCopyBuffer = _hexColorText.text;
        ToastOn("Copied Color Code");
    }
    #endregion

    #if UNITY_6000_0_OR_NEWER
    void CheckInputSystemUIModule()
    {
        if (EventSystem.current == null)
        {
            Debug.LogWarning("[SPUM] EventSystem not found in scene");
            return;
        }

        var currentModule = EventSystem.current.currentInputModule;
        
        // StandaloneInputModule Remove and InputSystemUIInputModule Add
        if (currentModule == null || currentModule.GetType().Name != "InputSystemUIInputModule")
        {
            // StandaloneInputModule Remove
            var standaloneModule = EventSystem.current.GetComponent<StandaloneInputModule>();
            if (standaloneModule != null)
            {
                DestroyImmediate(standaloneModule);
                Debug.Log("[SPUM] StandaloneInputModule removed");
            }

            // Other InputModules Remove
            if (currentModule != null && currentModule != standaloneModule)
            {
                DestroyImmediate(currentModule);
            }

            // InputSystemUIInputModule Add
            try
            {
                var inputSystemModule = EventSystem.current.gameObject.AddComponent(System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem"));
                Debug.Log("[SPUM] InputSystemUIInputModule added to EventSystem for Unity 6+ compatibility");
                ToastOn("Input System UI Module added");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[SPUM] Failed to add InputSystemUIInputModule: {e.Message}");
                ToastOn("Input System package required for Unity 6+");
                
                // 실패 시 StandaloneInputModule 복원
                EventSystem.current.gameObject.AddComponent<StandaloneInputModule>();
                Debug.Log("[SPUM] StandaloneInputModule restored as fallback");
            }
        }
        else
        {
            Debug.Log("[SPUM] InputSystemUIInputModule already configured");
        }
    }
    #endif

}