using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

public class SPUM_AnimationPreset : MonoBehaviour
{
    public string Name;
    public string Type;
    public Text UnitType;
    public Text PresetName;
    public Button EditName;
    public Button InputSave;
    public Button Delete;
    public Button OK;
    public Button ApplyPreset;
    public GameObject NamePanel;
    public InputField inputName;
    public GameObject NameEditPanel;
    public SPUM_Preset animPreset;

    public SPUM_AnimationManager animationManager;
    public void Init(SPUM_Preset preset, SPUM_AnimationManager manager)
    {
        animationManager = manager;
        UnitType.text = preset.UnitType;
        PresetName.text = preset.PresetName;

        Name = preset.PresetName;
        Type = preset.UnitType;
        animPreset = preset;
        //spumPackages = preset.Packages;

        Delete.onClick.AddListener(() => {
            Debug.Log("Delete!");
            manager.DeletePresetData(Name);
            OnDestroy();
        });
        InputSave.onClick.AddListener(()=> {
            manager.EditPresetData(Name, inputName.text);
        });

        inputName.onEndEdit.AddListener((value)=> {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                manager.EditPresetData(Name, inputName.text);
            }
        });
        ApplyPreset.onClick.AddListener(() => {
            Debug.Log("ApplyPreset " + animPreset.PresetName);
            manager.ApplyPreset(animPreset);

        });
        OK.onClick.AddListener(() => {
            //manager.EditPresetData(PresetName.text, PresetName.text);
            manager.ApplyPreset(animPreset);

        });
    }
    
    void Start()
    {
        inputName.onValueChanged.AddListener(
            (word) => inputName.text = Regex.Replace(word, @"[^0-9a-zA-Z가-힣]", "")
        );
        EditName.onClick.AddListener(()=>{
            NamePanel.SetActive(false);
            NameEditPanel.SetActive(true);
            Debug.Log("Edit Name");
        });

        InputSave.onClick.AddListener(()=>{
            NamePanel.SetActive(true);
            NameEditPanel.SetActive(false);
        });
    }

    void OnDestroy()
    {
        EditName.onClick.RemoveAllListeners();
        InputSave.onClick.RemoveAllListeners();
        Delete.onClick.RemoveAllListeners();
        OK.onClick.RemoveAllListeners();
        ApplyPreset.onClick.RemoveAllListeners();
    }
}