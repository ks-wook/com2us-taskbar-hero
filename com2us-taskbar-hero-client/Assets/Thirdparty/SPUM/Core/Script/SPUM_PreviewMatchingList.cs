using UnityEngine;
using System.Collections.Generic;
using UnityEngine.UI;
public class SPUM_PreviewMatchingList : MonoBehaviour
{
    public List<PreviewMatchingElement> matchingTables = new List<PreviewMatchingElement>();
    public void LoadItems()
    {
        matchingTables = new List<PreviewMatchingElement>();
        Image[] Images = GetComponentsInChildren<Image>(true);
        string Text = "";
        foreach (var image in Images)
        {
            var item = new PreviewMatchingElement(); 
            item.Structure = System.Text.RegularExpressions.Regex.Replace(image.name ,@"[^a-zA-Z가-힣\s]", "");
            item.image = image;
            item.Color = Color.white;
            Text +=item.Structure+"\n";
            matchingTables.Add(item);
        }
        Debug.Log(Text);
    }
}
