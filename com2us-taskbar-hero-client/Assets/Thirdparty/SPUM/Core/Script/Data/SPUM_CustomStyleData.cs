using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[Serializable]
public class SPUM_CustomStyleData
{
    [Serializable]
    public class CustomStyle
    {
        public string id;
        public string name;
        public string description;
        public List<string> elements = new List<string>();
        public List<string> required_parts = new List<string>();
        public List<string> negative = new List<string>();
        
        public CustomStyle()
        {
            elements = new List<string>();
            required_parts = new List<string>();
            negative = new List<string>();
        }
    }
    
    // JSON 직렬화용 List
    public List<CustomStyle> custom_styles_list = new List<CustomStyle>();
    
    // 런타임용 Dictionary
    [NonSerialized]
    private Dictionary<string, CustomStyle> _styleDict;
    
    // 기존 코드 호환을 위한 Dictionary 프로퍼티
    public Dictionary<string, CustomStyle> custom_styles
    {
        get
        {
            if (_styleDict == null)
                RebuildDictionary();
            return _styleDict;
        }
    }
    
    private void RebuildDictionary()
    {
        _styleDict = new Dictionary<string, CustomStyle>();
        foreach (var style in custom_styles_list)
        {
            if (!string.IsNullOrEmpty(style.id))
            {
                _styleDict[style.id] = style;
            }
        }
    }
    
    public string ToJson()
    {
        // Dictionary를 List로 동기화
        SyncListFromDictionary();
        return JsonUtility.ToJson(this, true);
    }
    
    private void SyncListFromDictionary()
    {
        if (_styleDict != null)
        {
            custom_styles_list = _styleDict.Values.ToList();
        }
    }
    
    public static SPUM_CustomStyleData FromJson(string json)
    {
        var data = JsonUtility.FromJson<SPUM_CustomStyleData>(json);
        data.RebuildDictionary();
        return data;
    }
    
    public CustomStyle GetCustomStyle(string styleName)
    {
        if (custom_styles.ContainsKey(styleName))
        {
            return custom_styles[styleName];
        }
        return null;
    }
    
    public void UpdateCustomStyle(string styleName, CustomStyle customStyle)
    {
        customStyle.id = styleName;
        custom_styles[styleName] = customStyle;
        SyncListFromDictionary();
    }
    
    public bool RemoveCustomStyle(string styleName)
    {
        bool result = custom_styles.Remove(styleName);
        if (result)
        {
            SyncListFromDictionary();
        }
        return result;
    }
    
    public List<string> GetAllCustomStyleNames()
    {
        return new List<string>(custom_styles.Keys);
    }
    
    public List<string> GetStylesForPart(string partType)
    {
        var result = new List<string>();
        
        foreach (var kvp in custom_styles)
        {
            if (kvp.Value.required_parts.Count == 0 || kvp.Value.required_parts.Contains(partType))
            {
                result.Add(kvp.Key);
            }
        }
        
        return result;
    }
    
    public List<string> GetConflictingCustomStyles(List<string> selectedElements)
    {
        var conflicts = new List<string>();
        
        foreach (var kvp in custom_styles)
        {
            foreach (var negative in kvp.Value.negative)
            {
                if (selectedElements.Contains(negative))
                {
                    conflicts.Add(kvp.Key);
                    break;
                }
            }
        }
        
        return conflicts;
    }
    
    public List<string> GetCompatibleCustomStyles(List<string> selectedElements, List<string> availableParts)
    {
        var compatible = new List<string>();
        
        foreach (var kvp in custom_styles)
        {
            bool isCompatible = true;
            
            if (kvp.Value.required_parts.Count > 0)
            {
                foreach (var part in kvp.Value.required_parts)
                {
                    if (!availableParts.Contains(part))
                    {
                        isCompatible = false;
                        break;
                    }
                }
            }
            
            if (isCompatible)
            {
                foreach (var negative in kvp.Value.negative)
                {
                    if (selectedElements.Contains(negative))
                    {
                        isCompatible = false;
                        break;
                    }
                }
            }
            
            if (isCompatible)
            {
                compatible.Add(kvp.Key);
            }
        }
        
        return compatible;
    }
}