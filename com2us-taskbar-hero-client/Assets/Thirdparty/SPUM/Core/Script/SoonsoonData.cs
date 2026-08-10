using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Runtime.Serialization.Formatters.Binary;
using System.IO;

public class SoonsoonData
{
    // Start is called before the first frame updateprivate 
    static SoonsoonData instance = null;
    public static SoonsoonData Instance
    {
        get
        {
            if (instance == null)
            {
                instance = new SoonsoonData();
            }
            return instance;
        }
    }

    private SoonsoonData(){}


    [Serializable]
    public class SoonData
    {
        public string Version; // 매니저 버전
        public List<Dictionary<string,string>> packageList = new List<Dictionary<string,string>>(); // 패키지 버전
        //public List<Dictionary<string,bool>> SelectedPackageInfo = new List<Dictionary<string, bool>>();
        public List<string> _savedColorList = new List<string>(); // 작업 중인 색상 저장 
    }

    public SoonData _soonData2 = new SoonData();

    public SPUM_Manager _spumManager;
    public bool _gifAlphaTrigger; // for using gif trigger at Soonsoon Exporter.
    public Color _gifBasicColor; //for using gif bg color at Soonsoon Exporter.
    public Color _alphaColor; // for using gif alpha color at Soonsoon Exporter.


    public void SaveData()
    {
        // bool _saveAvailable = false;

        try
        {
            FileSaveToPrefab();
        }
        catch (System.Exception e)
        {
            Debug.Log("Failed to save the data");
            Debug.Log(e);
        }
        finally
        {
        }
    }


    private void FileSaveToPrefab()
    {
        var b = new BinaryFormatter();
        var m = new MemoryStream();
        b.Serialize(m , _soonData2);
        PlayerPrefs.SetString("SoonsoonSave2",Convert.ToBase64String(m.GetBuffer())); 
    }

    public IEnumerator LoadData()
    {
        yield return null;
        try
        {
            LoadProcess();
        }
        catch( System.Exception e)
        {
            Debug.Log(" Failed to load Data...");
            Debug.Log(e.ToString());
        }

        yield return new WaitForSecondsRealtime(0.1f);
    }
    public void LoadProcess()
    {
        Debug.Log("Trying Loading data ...");

        if(!PlayerPrefs.HasKey("SoonsoonSave2"))
        {
            Debug.Log("You don't use save data yet.");
        }
        else
        {
            string _str = PlayerPrefs.GetString("SoonsoonSave2");

            if( _str.Length > 0)
            {
                string _tmpStr = PlayerPrefs.GetString("SoonsoonSave2");
                if(!string.IsNullOrEmpty(_tmpStr)) 
                {
                    var b = new BinaryFormatter();
                    var m = new MemoryStream(Convert.FromBase64String(_tmpStr));
                    _soonData2 = (SoonData) b.Deserialize(m);
                    Debug.Log("Load Successful!!");
                }
            }
        }
    }

    private const string PackageFilterKey = "SPUM_PackageFilter";

    public void SavePackageData(Dictionary<string, bool> filterList)
    {
        var data = new PackageFilterData();
        foreach (var kvp in filterList)
        {
            data.keys.Add(kvp.Key);
            data.values.Add(kvp.Value);
        }
        PlayerPrefs.SetString(PackageFilterKey, JsonUtility.ToJson(data));
    }

    public Dictionary<string, bool> LoadPackageData()
    {
        var result = new Dictionary<string, bool>();
        if (!PlayerPrefs.HasKey(PackageFilterKey)) return result;
        var json = PlayerPrefs.GetString(PackageFilterKey);
        var data = JsonUtility.FromJson<PackageFilterData>(json);
        if (data == null) return result;
        for (int i = 0; i < data.keys.Count; i++)
        {
            result[data.keys[i]] = data.values[i];
        }
        return result;
    }

    [Serializable]
    private class PackageFilterData
    {
        public List<string> keys = new List<string>();
        public List<bool> values = new List<bool>();
    }
}
