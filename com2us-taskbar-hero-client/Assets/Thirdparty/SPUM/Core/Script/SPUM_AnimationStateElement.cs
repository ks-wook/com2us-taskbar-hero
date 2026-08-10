using UnityEngine;
using UnityEngine.UI;

public class SPUM_AnimationStateElement : MonoBehaviour
{
    public Text StateIndexText;
    public Text StateNameText;
    public ToggleGroup NameToggleGroup;
    public Toggle NameButton;
    public Button UpButton;
    public Button DownButton;
    public Button RemoveButton;
    public Text StateSubTypeText;
    public Text StatePackageText;
    public void SetData(int index, string Name)
    {
        StateIndexText.text = $"{index}";
        StateNameText.text = $"{Name}";
    }
}