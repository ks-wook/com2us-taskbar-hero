//using System.IO;
using UnityEngine;
using UnityEngine.UI;

public class SPUM_AnimationControllerPanel : MonoBehaviour
{
    [Header("Animation Play Controller")]
    [SerializeField] Slider timeLineSlider;
    [SerializeField] Slider playSpeedSlider;
    [SerializeField] Text slidertimeLineInfo;
    [SerializeField] Text timeLineText;
    [SerializeField] Text playSpeedText;
    [SerializeField] Text ClipName;
    private SPUM_Prefabs previewUnit;

    public void Init(SPUM_Prefabs unit){
        previewUnit = unit;
        timeLineSlider.minValue = 0f;
        timeLineSlider.maxValue = 1f;
        timeLineText = timeLineSlider.transform.GetComponentInChildren<Text>();

        playSpeedSlider.minValue = 1;
        playSpeedSlider.maxValue = 200;
        
        playSpeedSlider.wholeNumbers = true;
        playSpeedText = playSpeedSlider.transform.GetComponentInChildren<Text>();
        
        timeLineSlider.onValueChanged.AddListener( x => {
            SetAnimationNormailzedTime(x);
        });
        playSpeedSlider.onValueChanged.AddListener( x => {
            var AnimationSpeed = x * .01f;
            previewUnit._anim.speed = AnimationSpeed;
            playSpeedText.text = string.Format("Speed x{0:0.00}", AnimationSpeed);
            var clipInfo = previewUnit._anim.GetCurrentAnimatorClipInfo(0);
            var ClipText = clipInfo[0].clip.name;
            ClipName.text = $"{ClipText} [{string.Format("{0:F2}", SetClipTime(1))}]";
        });
        playSpeedSlider.value = 100f;
    }
    public void RefreshSlier(string clipPath){
        var Name = clipPath.Split("/");
        var clip = Name[Name.Length-1].Replace(".anim","");
        timeLineSlider.SetValueWithoutNotify(0f);
        playSpeedSlider.onValueChanged.Invoke(playSpeedSlider.value);
        ClipName.text = $"{clip} [{string.Format("{0:F2}", SetClipTime(1))}]";
    }
    private float SetClipTime(float progress){
        var clipInfo = previewUnit._anim.GetCurrentAnimatorClipInfo(0);
        float clipLength = clipInfo[0].clip.length;
        var attackAnimationClipPlayTime = (clipLength / (playSpeedSlider.value * 0.01f)) * progress;
        return attackAnimationClipPlayTime;
       
    }
    private void SetAnimationNormailzedTime(float progress)
    {
        var state = previewUnit._anim.GetCurrentAnimatorStateInfo(0);
        //Debug.Log(attackAnimationClipPlayTime);
        previewUnit._anim.speed = 0;
        previewUnit._anim.Play(state.shortNameHash, 0, progress);
        previewUnit._anim.Update(0f);
        timeLineText.text = string.Format("SEC :{0:F2}", SetClipTime(progress));
    }
}