using System;

[Serializable]
public class SpumAnimationClip : ICloneable
{
    public int index;
    public string Name;
    public string StateType;
    public string ClipPath;
    public bool HasData;
    public string UnitType;
    public string SubCategory;
    public object Clone()
    {
        return new SpumAnimationClip
        {
            index = this.index,
            Name = this.Name,
            StateType = this.StateType,
            ClipPath= this.ClipPath,
            HasData = this.HasData,
            UnitType = this.UnitType,
            SubCategory= this.SubCategory
        };
    }
}
