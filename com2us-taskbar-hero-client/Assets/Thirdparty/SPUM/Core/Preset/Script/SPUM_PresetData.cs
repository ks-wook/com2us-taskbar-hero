using System.Collections.Generic;

using System;
using UnityEngine;

[CreateAssetMenu(fileName = "PresetData", menuName = "ScriptableObjects/SPUM_PresetData", order = 1)]
[Serializable]
public class SPUM_PresetData : ScriptableObject
{
    public List<SPUM_Preset> Presets;
}
[Serializable]
public class SPUM_Preset{
    public string UnitType;
    public string PresetName;
    public List<SpumPackage> Packages;
}