using System;
using System.Collections.Generic;
using System.Linq;

[System.Serializable]
public class SpumPackage : ICloneable
{
    public string Name;
    public string Path;
    public string Version;
    public string CreationDate;
    public List<SpumAnimationClip> SpumAnimationData =  new List<SpumAnimationClip>();
    public List<SpumTextureData> SpumTextureData = new List<SpumTextureData>();
    public object Clone()
    {
        return new SpumPackage
        {
            Name = this.Name,
            Path = this.Path,
            Version = this.Version,
            CreationDate = this.CreationDate,
            SpumAnimationData = this.SpumAnimationData?.Select(a => (SpumAnimationClip)a.Clone()).ToList(),
            SpumTextureData = this.SpumTextureData?.Select(t => (SpumTextureData)t.Clone()).ToList()
        };
    }
}
