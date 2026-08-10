using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[Serializable]
public class SPUM_ImprovedStyleData
{
    [Serializable]
    public class StyleInfo
    {
        public string id;
        public string description;
    }
    
    [Serializable]
    public class StyleCategory
    {
        public string id;
        public string name;
        public List<StyleInfo> styles = new List<StyleInfo>();
        
        public StyleCategory()
        {
            styles = new List<StyleInfo>();
        }
    }
    
    // JSON 직렬화용 List
    public List<StyleCategory> categories_list = new List<StyleCategory>();
    
    // 런타임용 Dictionary
    [NonSerialized]
    private Dictionary<string, StyleCategory> _categoryDict;
    [NonSerialized]
    private Dictionary<string, StyleInfo> _flatStyles;
    
    // 기존 코드 호환을 위한 Dictionary 프로퍼티
    public Dictionary<string, StyleCategory> categories
    {
        get
        {
            if (_categoryDict == null)
                RebuildDictionary();
            return _categoryDict;
        }
    }
    
    private void RebuildDictionary()
    {
        _categoryDict = new Dictionary<string, StyleCategory>();
        _flatStyles = new Dictionary<string, StyleInfo>();
        
        foreach (var category in categories_list)
        {
            if (!string.IsNullOrEmpty(category.id))
            {
                _categoryDict[category.id] = category;
                
                foreach (var style in category.styles)
                {
                    if (!string.IsNullOrEmpty(style.id))
                    {
                        _flatStyles[style.id.ToLower()] = style;
                    }
                }
            }
        }
    }
    
    private Dictionary<string, StyleInfo> GetFlatStyles()
    {
        if (_flatStyles == null)
            RebuildDictionary();
        return _flatStyles;
    }
    
    public string ToJson()
    {
        // Dictionary를 List로 동기화
        SyncListFromDictionary();
        return JsonUtility.ToJson(this, true);
    }
    
    private void SyncListFromDictionary()
    {
        if (_categoryDict != null)
        {
            categories_list = _categoryDict.Values.ToList();
        }
    }
    
    public static SPUM_ImprovedStyleData FromJson(string json)
    {
        var data = JsonUtility.FromJson<SPUM_ImprovedStyleData>(json);
        data.RebuildDictionary();
        return data;
    }
    
    public StyleInfo GetStyle(string styleName)
    {
        var flatStyles = GetFlatStyles();
        if (flatStyles.ContainsKey(styleName.ToLower()))
        {
            return flatStyles[styleName.ToLower()];
        }
        return null;
    }
    
    public void UpdateStyle(string categoryKey, string styleName, StyleInfo styleInfo)
    {
        styleInfo.id = styleName.ToLower();
        
        if (categories.ContainsKey(categoryKey))
        {
            var category = categories[categoryKey];
            var existing = category.styles.FirstOrDefault(s => s.id == styleInfo.id);
            
            if (existing != null)
            {
                int index = category.styles.IndexOf(existing);
                category.styles[index] = styleInfo;
            }
            else
            {
                category.styles.Add(styleInfo);
            }
            
            GetFlatStyles()[styleInfo.id] = styleInfo;
            SyncListFromDictionary();
        }
    }
    
    public bool RemoveStyle(string styleName)
    {
        string key = styleName.ToLower();
        
        foreach (var category in categories.Values)
        {
            var toRemove = category.styles.FirstOrDefault(s => s.id == key);
            if (toRemove != null)
            {
                category.styles.Remove(toRemove);
                GetFlatStyles().Remove(key);
                SyncListFromDictionary();
                return true;
            }
        }
        return false;
    }
    
    public List<string> GetAllStyleNames()
    {
        return new List<string>(GetFlatStyles().Keys);
    }
    
    public Dictionary<string, List<string>> GetStylesByCategory()
    {
        var result = new Dictionary<string, List<string>>();
        
        foreach (var kvp in categories)
        {
            result[kvp.Key] = kvp.Value.styles.Select(s => s.id).ToList();
        }
        
        return result;
    }
    
    public Dictionary<string, StyleCategory> GetCategorizedStyles()
    {
        return new Dictionary<string, StyleCategory>(categories);
    }
    
    public List<string> GetConflictingStyles(List<string> selectedStyles)
    {
        return new List<string>();
    }
    
    public List<string> GetCompatibleStyles(List<string> selectedStyles)
    {
        var allStyles = GetAllStyleNames();
        var compatible = new List<string>();
        
        foreach (var styleName in allStyles)
        {
            if (!selectedStyles.Contains(styleName))
            {
                compatible.Add(styleName);
            }
        }
        
        return compatible;
    }
    
    public List<string> GenerateRandomStyleCombination(int maxStyles = 3, List<string> requiredCategories = null)
    {
        var result = new List<string>();
        
        if (requiredCategories != null && requiredCategories.Count > 0)
        {
            foreach (var categoryKey in requiredCategories)
            {
                if (categories.ContainsKey(categoryKey))
                {
                    var category = categories[categoryKey];
                    if (category.styles.Count > 0)
                    {
                        var selected = category.styles[UnityEngine.Random.Range(0, category.styles.Count)];
                        result.Add(selected.id);
                    }
                }
            }
        }
        
        var allAvailable = GetCompatibleStyles(result);
        while (result.Count < maxStyles && allAvailable.Count > 0)
        {
            var selected = allAvailable[UnityEngine.Random.Range(0, allAvailable.Count)];
            result.Add(selected);
            allAvailable = GetCompatibleStyles(result);
        }
        
        return result;
    }
    
    public void AddCategory(string key, string name)
    {
        if (!categories.ContainsKey(key))
        {
            var newCategory = new StyleCategory { id = key, name = name };
            categories[key] = newCategory;
            SyncListFromDictionary();
        }
    }
    
    public bool RemoveCategory(string key)
    {
        if (categories.Remove(key))
        {
            // Remove all styles from flat dictionary
            var categoryToRemove = categories_list.FirstOrDefault(c => c.id == key);
            if (categoryToRemove != null)
            {
                foreach (var style in categoryToRemove.styles)
                {
                    GetFlatStyles().Remove(style.id);
                }
            }
            SyncListFromDictionary();
            return true;
        }
        return false;
    }
}