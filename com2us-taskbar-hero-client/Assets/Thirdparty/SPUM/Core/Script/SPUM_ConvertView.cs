using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class SPUM_ConvertView : MonoBehaviour
{
    public Button Cancel;
    public Button ForceSelectButton;
    public Text PrefabVersionText;
    public Text MissingImageText;
    public Text MissingAnimText;

    void Start()
    {
        Cancel.onClick.AddListener(() => gameObject.SetActive(false));
    }

    void OnDisable()
    {
        if (ForceSelectButton != null)
            ForceSelectButton.onClick.RemoveAllListeners();
    }

    public void Show(float prefabVersion, List<(string imageName, string packageName)> missingImages, List<(string clipName, string packageName)> invalidClips)
    {
        if (PrefabVersionText != null)
            PrefabVersionText.text = $"Prefab Version {prefabVersion}";

        if (MissingImageText != null)
        {
            bool hasMissing = missingImages != null && missingImages.Count > 0;
            MissingImageText.transform.parent.gameObject.SetActive(hasMissing);
            if (hasMissing)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("<b>Missing Image Packages</b>");
                sb.AppendLine("--------------");
                foreach (var item in missingImages)
                    sb.AppendLine($"{item.imageName} ({item.packageName})");
                MissingImageText.text = sb.ToString();
            }
        }

        if (MissingAnimText != null)
        {
            bool hasInvalid = invalidClips != null && invalidClips.Count > 0;
            MissingAnimText.transform.parent.gameObject.SetActive(hasInvalid);
            if (hasInvalid)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("<b>Missing Anim Packages</b>");
                sb.AppendLine("--------------");
                foreach (var clip in invalidClips)
                    sb.AppendLine($"{clip.clipName} ({clip.packageName})");
                MissingAnimText.text = sb.ToString();
            }
        }

        gameObject.SetActive(true);
    }
}
