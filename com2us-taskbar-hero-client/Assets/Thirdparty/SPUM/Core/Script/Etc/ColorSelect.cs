using UnityEngine;
using UnityEngine.UI;

public class ColorSelect : MonoBehaviour
{
    public int index;
    public Image _savedColor;

    public void SetColorSave()
    {
        SoonsoonData.Instance._spumManager.UIManager._nowSelectColorNum = index; // 인덱스 전달
        if(_savedColor.gameObject.activeInHierarchy) // 활성화 상태면
        {
            SoonsoonData.Instance._spumManager.UIManager.NowColor = _savedColor.color;
            string parsedColor = ColorUtility.ToHtmlStringRGB(_savedColor.color); 
            SoonsoonData.Instance._spumManager.UIManager.ToastOn($"Selected Color <color=#{parsedColor}>◆</color>#" + parsedColor);
            SoonsoonData.Instance._spumManager.UIManager._nowSelectColor.SetActive(true); // 셀럭터 이동
            SoonsoonData.Instance._spumManager.UIManager._nowSelectColor.transform.position = transform.position;
            //set the color
        }
        else
        {
            //saved the color
            _savedColor.gameObject.SetActive(true);
            _savedColor.color = SoonsoonData.Instance._spumManager.UIManager.NowColor;
            string parsedColor = ColorUtility.ToHtmlStringRGB(_savedColor.color); // SoonsoonData.Instance._spumManager.UIManager.ColorToStr(_savedColor.color);
            SoonsoonData.Instance._spumManager.UIManager.ToastOn($"Saved Color <color=#{parsedColor}>◆</color>#" + parsedColor);
            SoonsoonData.Instance._soonData2._savedColorList[index] = "#"+parsedColor;
            SoonsoonData.Instance._spumManager.UIManager._nowSelectColor.transform.position = transform.position;
            SoonsoonData.Instance.SaveData();
        }
    }
}
