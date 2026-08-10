using System.Collections.Generic;
using UnityEngine;
using System;
using System.IO;
using Moments.Encoder;
using ThreadPriority = System.Threading.ThreadPriority;
using System.Linq;
[Serializable]
public class ListItem
{
    public string name;
	public AnimationClip animationClip;
    public bool isChecked;
}
public class SPUM_Exporter : MonoBehaviour
{
    public GameObject _unitPrefab;
	public string _imageName;
	public bool _separated = false; 
	public string _sepaName ="";
    public Vector2 _imageSize = new Vector2(128,128);
    public Vector2 _fullSize = new Vector2(1024,1024);
    public float _scaleFactor = 1;
    public int _frameRate = 8;
	public int _frameNumber = 0;
	public bool _advanced;

	public int ImageNumber;
	public List<ListItem> items = new List<ListItem>();
    // Start is called before the first frame update

    public void CheckObjNow()
    {
        _objectNow = null;
        _anim = null;
		
        if(_objectPivot.childCount > 0)
        {
            DestroyImmediate(_objectPivot.GetChild(0).gameObject);
        }
    }

    public void MakeObjNow()
    {
        if(_objectNow!=null) return;
		_imageName = "";
		items.Clear();
		if(!_unitPrefab) return;
        GameObject tObj = Instantiate(_unitPrefab);
        tObj.transform.SetParent(_objectPivot);
        tObj.transform.localScale = new Vector3(1,1,1);
        tObj.transform.localPosition = new Vector3(0,-0.5f,0);

        _objectNow = tObj;
        _anim = tObj.transform.GetChild(0).GetComponent<Animator>();
		SPUM_Prefabs = tObj.GetComponent<SPUM_Prefabs>();
		LoadAnimationStateClip();
    }
	public void LoadAnimationStateClip()
	{
		if(!SPUM_Prefabs) return;
		List<string> _animNameNow = new List<string>();
		List<string> IndexedClipNames = new List<string>();
		items = new();
		IDLE_List = new();
        MOVE_List = new();
        ATTACK_List = new();
        DAMAGED_List = new();
        DEBUFF_List = new();
        DEATH_List = new();
        OTHER_List = new();
        
        var groupedClips = SPUM_Prefabs.spumPackages
        .SelectMany(package => package.SpumAnimationData)
        .Where(spumClip => spumClip.HasData && 
                        spumClip.UnitType.Equals(SPUM_Prefabs.UnitType) && 
                        spumClip.index > -1 )
        .GroupBy(spumClip => spumClip.StateType)
        .ToDictionary(
            group => group.Key, 
            group => group.OrderBy(clip => clip.index).ToList()
        );
    // foreach (var item in groupedClips)
    // {
    //     foreach (var clip in item.Value)
    //     {
    //         Debug.Log(clip.ClipPath);
    //     }
    // }
		List<AnimationClip> animationClipsList = new();
		IndexedClipNames = new();
		foreach (var kvp in groupedClips)
		{
			var stateType = kvp.Key;
			var orderedClips = kvp.Value;
			//int index = 0;
			switch (stateType)
			{
				case "IDLE":
					IDLE_List.AddRange(orderedClips.Select(clip => LoadAnimationClip(clip.ClipPath)));
					IndexedClipNames.AddRange(IDLE_List.Select((_, i) => $"IDLE{i}"));
					animationClipsList.AddRange(IDLE_List);
					break;
				case "MOVE":
					MOVE_List.AddRange(orderedClips.Select(clip => LoadAnimationClip(clip.ClipPath)));
					IndexedClipNames.AddRange(MOVE_List.Select((_, i) => $"MOVE{i}"));
					animationClipsList.AddRange(MOVE_List);
					break;
				case "ATTACK":
					ATTACK_List.AddRange(orderedClips.Select(clip => LoadAnimationClip(clip.ClipPath)));
					IndexedClipNames.AddRange(ATTACK_List.Select((_, i) => $"ATTACK{i}"));
					animationClipsList.AddRange(ATTACK_List);
					break;
				case "DAMAGED":
					DAMAGED_List.AddRange(orderedClips.Select(clip => LoadAnimationClip(clip.ClipPath)));
					IndexedClipNames.AddRange(DAMAGED_List.Select((_, i) => $"DAMAGED{i}"));
					animationClipsList.AddRange(DAMAGED_List);
					break;
				case "DEBUFF":
					DEBUFF_List.AddRange(orderedClips.Select(clip => LoadAnimationClip(clip.ClipPath)));
					IndexedClipNames.AddRange(DEBUFF_List.Select((_, i) => $"DEBUFF{i}"));
					animationClipsList.AddRange(DEBUFF_List);
					break;
				case "DEATH":
					DEATH_List.AddRange(orderedClips.Select(clip => LoadAnimationClip(clip.ClipPath)));
					IndexedClipNames.AddRange(DEATH_List.Select((_, i) => $"DEATH{i}"));
					animationClipsList.AddRange(DEATH_List);
					break;
				case "OTHER":
					OTHER_List.AddRange(orderedClips.Select(clip => LoadAnimationClip(clip.ClipPath)));
					IndexedClipNames.AddRange(OTHER_List.Select((_, i) => $"OTHER{i}"));
					animationClipsList.AddRange(OTHER_List);
					break;
			}
		}
		//animationClips = animationClipsList.ToArray();

		items = animationClipsList.Select((clip, index) => new ListItem
        {
            name = IndexedClipNames.ElementAtOrDefault(index) ?? "Unnamed",
            animationClip = clip,
            isChecked = false
        }).ToList();

	}
	AnimationClip LoadAnimationClip(string clipPath)
    {
        // "Animations" 폴더에서 애니메이션 클립 로드
        AnimationClip clip = Resources.Load<AnimationClip>(clipPath.Replace(".anim", ""));
        
        if (clip == null)
        {
            Debug.LogWarning($"애니메이션 클립 '{clipPath}'을 로드하지 못했습니다.");
        }
        
        return clip;
    }
	//advanced field
    public List<AnimationClip> IDLE_List = new();
    public List<AnimationClip> MOVE_List = new();
    public List<AnimationClip> ATTACK_List = new();
    public List<AnimationClip> DAMAGED_List = new();
    public List<AnimationClip> DEBUFF_List = new();
    public List<AnimationClip> DEATH_List = new();
    public List<AnimationClip> OTHER_List = new();

	public Camera _camera;
	public Animator _anim;
	public SPUM_Prefabs SPUM_Prefabs;
	public Transform _objectPivot;
	public GameObject _objectNow;
	public RectTransform _imgBG;
	public GameObject _bgSet;
	public int frameNowNumber;
	public int _animNum;
	public float timer;
	public float timerForSave;
	public bool useTimer;
	public bool _netAnimClip;
	public int animNum;
	//public List<string> _animNameList = new List<string>();
    //public AnimationClip[] animationClips;

	public List<Texture2D> _textSaveList = new List<Texture2D>();
	Queue<RenderTexture> m_Frames;
	// public RenderTexture tempRT;
	public ThreadPriority WorkerPriority = ThreadPriority.BelowNormal;
	public Action<int, string> OnFileSaved;
	public Action<int, float> OnFileSaveProgress;

	//For Gif Exporter
	public bool _gifExportUse;
	public Color _gifBGColor = Color.white;
	public bool _gifUseTransparancy;
	public Color _gifAlphaBGColor = Color.green;
	public float _gifDelay = 0.1f;
	public int _gifQuality = 10; // you have to set 1-100, it is realtive with file sizes;
	public int _gifRepeatNum = 0; // 0 is repeats continuously, number is repeated as many times as the number.
	Texture2D imageSave;


    // Start is called before the first frame update
	// private bool takeHiResShot = false;

	//  public void TakeHiResShot() {
    //      takeHiResShot = true;
    //  }

	public void SetScreenShot()
	{
		_bgSet.SetActive(false);
		int tX = _camera.scaledPixelWidth;
		int tY = _camera.scaledPixelHeight;

		RenderTexture tempRT = new RenderTexture(tX, tY, 24, RenderTextureFormat.ARGB32)
		{
			antiAliasing = 4
		};

		_camera.targetTexture = tempRT;
		RenderTexture.active = tempRT;
		_camera.Render();

		imageSave = new Texture2D((int)_imageSize.x, (int)_imageSize.y, TextureFormat.ARGB32, false, true);
		
		float tXPos = tX*0.5f - imageSave.width*0.5f;
		float tYPos = tY*0.5f - imageSave.height*0.5f;

		imageSave.ReadPixels(new Rect(tXPos, tYPos, imageSave.width, imageSave.height), 0, 0);
		imageSave.Apply();

		RenderTexture.active = null;

		_textSaveList.Add(imageSave);
		_bgSet.SetActive(true);
		_camera.targetTexture = null;

		DestroyImmediate(tempRT);
	}

	public void MakeScreenShotFile()
	{
		ImageNumber++;
		int numX = ((int)_fullSize.x) / ((int)_imageSize.x);
		int numY = ((int)_fullSize.y) / ((int)_imageSize.y);
		int allSpriteNum = numX * numY;
		// Debug.Log(allSpriteNum);

		
		List<Texture2D> resultImages = new List<Texture2D>();
		resultImages.Add(new Texture2D((int)_fullSize.x, (int)_fullSize.y, TextureFormat.ARGB32, false, true));
		FillColorAlpha(resultImages[0]);

		int resultImageNum = 0;
		int rISave = allSpriteNum;

		int numXSave = numX;

		int tYNum = 1;
		int tXNum = -1;
		string stateName = items[animNum-1].name;
		for(var i = 0 ; i < _textSaveList.Count; i++)
		{
			
			if(i == rISave)
			{
				tYNum = 1;
				resultImages.Add(new Texture2D((int)_fullSize.x, (int)_fullSize.y, TextureFormat.ARGB32, false, true));
				resultImageNum++;
				resultImages[resultImageNum].filterMode = FilterMode.Point;
				rISave += i;
				FillColorAlpha(resultImages[resultImageNum]);
			}

			Texture2D tTex = _textSaveList[i];
			tXNum++;

			for (int x = 0; x < tTex.width; x++)
			{
				for (int y = 0; y < tTex.height; y++)
				{
					Color bgColor = tTex.GetPixel(x, y);
					resultImages[resultImageNum].SetPixel(x + ((int)_imageSize.x) * tXNum, (((int)_fullSize.y) - ((int)_imageSize.y)*tYNum ) +  y , bgColor);
				}
			}

			if(i == numX-1) 
			{
				tYNum++;
				tXNum = -1;
				numX += numXSave;
			}
		}

		for(var i = 0 ; i < resultImages.Count;i++)
		{
			byte[] bytes = resultImages[i].EncodeToPNG();
			string tName ="";

			if(_imageName == "")
			{
				_imageName = _unitPrefab.name;
			}

			tName = _imageName;

			if(_separated)
			{
				tName = _imageName+ "_" +stateName +"_"+_sepaName; //+ "_" +ImageNumber
			}
			else
			{
				tName = _imageName+ "_"+stateName +"_Full";
			}

			if(!Directory.Exists("Assets/SPUM/ScreenShots/"))
			{
				Directory.CreateDirectory("Assets/SPUM/ScreenShots/");
			}

			string filename = string.Format("{0}/SPUM/ScreenShots/{1}_{2}.png", Application.dataPath,tName,i);
			System.IO.File.WriteAllBytes(filename, bytes);
			Debug.Log(@"<a href=\file:///"+filename+">"+filename+"</a>");
			
    		#if UNITY_EDITOR
			UnityEditor.AssetDatabase.Refresh();
			#endif
		}
	
		//takeHiResShot = false;
		_camera.targetTexture = null;
		if(_gifExportUse ) MakeGifAnimation();
	
	}

	public void MakeGifAnimation()
	{
		if(!_separated) return;
		if(_textSaveList.Count>0)
		{
			//gif 애니메이션 제작을 시작한다.
			PreProcess();
		}
	}


// for gif exporter - preview version
	void PreProcess()
	{
		Texture2D temp = new Texture2D((int)_imageSize.x, (int)_imageSize.y, TextureFormat.ARGB32, false);
		temp.hideFlags = HideFlags.HideAndDontSave;
		temp.wrapMode = TextureWrapMode.Clamp;
		temp.filterMode = FilterMode.Bilinear;
		temp.anisoLevel = 0;

		List<GifFrame> frames = new List<GifFrame>(_textSaveList.Count);
		if(!Directory.Exists("Assets/SPUM/ScreenShots/GifExports"))
		{
			Directory.CreateDirectory("Assets/SPUM/ScreenShots/GifExports");
		}
		string filepath = Application.dataPath + "/SPUM/ScreenShots/GifExports/"+_unitPrefab.name+"_"+_sepaName+".gif";
		for(var i = 0 ; i < _textSaveList.Count ; i++)
		{
			GifFrame frame = ToGifFrame(_textSaveList[i],temp);
			frames.Add(frame);
		}
		// Setup a worker thread and let it do its magic
		GifEncoder encoder = new GifEncoder(_gifRepeatNum, _gifQuality);
		encoder.SetDelay((int)(_gifDelay * 1000));
		encoder.SetAlphaValue(_gifUseTransparancy, _gifAlphaBGColor); 
				OnFileSaved += (i, t) => { 
			Debug.Log(i + "/" + @"<a href=\file:///"+t+">"+t+"</a>");
			};
		// OnFileSaveProgress += (i, t) => Debug.Log(i + "/" + t);
		Moments.Worker worker = new Moments.Worker(WorkerPriority)
		{
			m_Encoder = encoder,
			m_Frames = frames,
			m_FilePath = filepath,
			m_OnFileSaved = OnFileSaved,
			m_OnFileSaveProgress = OnFileSaveProgress
		};

		worker.Start();
	} 
	 

	GifFrame ToGifFrame(Texture2D source, Texture2D target)
	{
		
		if(!_gifUseTransparancy)
		{
			FillColorAlpha(target,_gifBGColor);
		}
		else
		{
			FillColorAlpha(target,_gifAlphaBGColor);
		}

		for (int x = 0; x < source.width; x++)
		{
			for (int y = 0; y < source.height; y++)
			{
				Color bgColor = source.GetPixel(x, y);
				if(bgColor.a != 0) target.SetPixel(x, y , bgColor);
			}
		}
		return new GifFrame() { Width = target.width, Height = target.height, Data = target.GetPixels32()};
	}

	public void PrintEndMessage()
	{
		_camera.targetTexture = null;
		_textSaveList.Clear();
		Debug.Log(string.Format("{0} Numbers Images Exported!!!", ImageNumber));
		
	}

	public static Texture2D FillColorAlpha(Texture2D tex2D, Color32? fillColor = null)
	{   
		if (fillColor ==null)
		{
			fillColor = Color.clear;
		}
		Color32[] fillPixels = new Color32[tex2D.width * tex2D.height];
		for (int i = 0; i < fillPixels.Length; i++)
		{
			fillPixels[i] = (Color32) fillColor;
		}
		tex2D.SetPixels32(fillPixels);
		return tex2D;
	}

}
