using System;
using System.Collections.Generic;
using UnityEngine;
[Serializable]
public class MatchingElement
{
    public string UnitType;
    public string PartType;
    public string PartSubType;
    public int Index;
    public string Dir;
    public string Structure;
    public string ItemPath;
    public int MaskIndex;
	public SpriteRenderer renderer;
    public Color32 Color;
}
public class SPUM_MatchingList : MonoBehaviour
{
    public List<MatchingElement> matchingTables = new List<MatchingElement>();
    public void LoadItems()
    {
        matchingTables = new List<MatchingElement>();
        SpriteRenderer[] spriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        string Text = "";
        foreach (SpriteRenderer sr in spriteRenderers)
        {
            var item = new MatchingElement();
            item.Structure = System.Text.RegularExpressions.Regex.Replace(sr.name,@"[^a-zA-Z가-힣\s]", "");
            item.renderer = sr;
            item.Color = Color.white;
            Text +=item.Structure+"\n";
            matchingTables.Add(item);
        }
        Debug.Log(Text);
    }
}
