using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[Serializable]
public class SPUM_ImprovedClassData
{
    [Serializable]
    public class CombatClass
    {
        public string id;
        public string description;
        public List<string> type = new List<string>();
        public List<string> class_tags = new List<string>();
        public List<string> style = new List<string>();
        public List<string> type_negative = new List<string>();
        public List<string> class_negative = new List<string>();
        public List<string> negative = new List<string>();
        
        public CombatClass()
        {
            type = new List<string>();
            class_tags = new List<string>();
            style = new List<string>();
            type_negative = new List<string>();
            class_negative = new List<string>();
            negative = new List<string>();
        }
    }
    
    // JSON 직렬화용 List
    public List<CombatClass> combat_classes_list = new List<CombatClass>();
    
    // 런타임용 Dictionary
    [NonSerialized]
    private Dictionary<string, CombatClass> _combatClassDict;
    
    // 기존 코드 호환을 위한 Dictionary 프로퍼티
    public Dictionary<string, CombatClass> combat_classes
    {
        get
        {
            if (_combatClassDict == null)
                RebuildDictionary();
            return _combatClassDict;
        }
    }
    
    private void RebuildDictionary()
    {
        _combatClassDict = new Dictionary<string, CombatClass>();
        foreach (var combatClass in combat_classes_list)
        {
            if (!string.IsNullOrEmpty(combatClass.id))
            {
                _combatClassDict[combatClass.id.ToLower()] = combatClass;
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
        if (_combatClassDict != null)
        {
            combat_classes_list = _combatClassDict.Values.ToList();
        }
    }
    
    public static SPUM_ImprovedClassData FromJson(string json)
    {
        var data = JsonUtility.FromJson<SPUM_ImprovedClassData>(json);
        data.RebuildDictionary();
        return data;
    }
    
    public CombatClass GetClass(string className)
    {
        if (combat_classes.ContainsKey(className.ToLower()))
        {
            return combat_classes[className.ToLower()];
        }
        return null;
    }
    
    public void UpdateClass(string className, CombatClass combatClass)
    {
        string key = className.ToLower();
        combatClass.id = key;
        combat_classes[key] = combatClass;
        SyncListFromDictionary();
    }
    
    public bool RemoveClass(string className)
    {
        bool result = combat_classes.Remove(className.ToLower());
        if (result)
        {
            SyncListFromDictionary();
        }
        return result;
    }
    
    public List<string> GetAllClassNames()
    {
        return new List<string>(combat_classes.Keys);
    }
    
    public List<string> GetClassesByType(string type)
    {
        var result = new List<string>();
        foreach (var kvp in combat_classes)
        {
            if (kvp.Value.type.Contains(type))
            {
                result.Add(kvp.Key);
            }
        }
        return result;
    }
    
    public List<string> GetClassesByClassTag(string classTag)
    {
        var result = new List<string>();
        foreach (var kvp in combat_classes)
        {
            if (kvp.Value.class_tags.Contains(classTag))
            {
                result.Add(kvp.Key);
            }
        }
        return result;
    }
}