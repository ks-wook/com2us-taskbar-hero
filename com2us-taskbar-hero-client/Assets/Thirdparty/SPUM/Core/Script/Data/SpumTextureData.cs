using System;

[Serializable]
public class SpumTextureData: ICloneable
{
    public string Name; // 메인 텍스쳐 이름
    public string UnitType; // 유닛 타입
    public string PartType; // 장비 타입
    public string SubType; // 스프라이트 멀티플
    public string PartSubType;
    public string Path;
    public object Clone()
    {
        return new SpumTextureData
        {
            Name = this.Name,
            UnitType = this.UnitType,
            PartType = this.PartType,
            SubType= this.SubType,
            PartSubType = this.PartSubType,
            Path = this.Path
        };
    }
}
