using System;

[Serializable]
public class SPUM_AnimationData
{
    //       "type": "idle",      // enum 값으로...
    //   "clipPath": "path/undead idle.anim"
    //   "PackageName" : "Legacy" 
    //   "Speed" : 0.75f
    public string UnitType;
    public string StateType;
    public string Path;
    public string PackageName;
    public float SpeedParam;
}