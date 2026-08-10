using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Reflection;
[CustomEditor(typeof(SPUM_Exporter))]
[CanEditMultipleObjects]
public class SPUM_ExporterEditor : Editor
{
    //field list
    SerializedProperty _unitPrefab;
    SerializedProperty _separated;
    SerializedProperty _imageName;
    SerializedProperty _imageSize;
    SerializedProperty _fullSize;
    SerializedProperty _scaleFactor;
    SerializedProperty _frameRate;
    SerializedProperty _advanced;    
    SerializedProperty _camera;
	SerializedProperty _anim;
	SerializedProperty _objectPivot;
	SerializedProperty _objectNow;
	SerializedProperty _imgBG;
	SerializedProperty _bgSet;
	SerializedProperty frameNowNumber;
	SerializedProperty _animNum;
	SerializedProperty timer;
	SerializedProperty timerForSave;
	SerializedProperty useTimer;
	SerializedProperty _netAnimClip;
	SerializedProperty animNum;
	SerializedProperty _textSaveList;
    SerializedProperty _gifExportUse;
	SerializedProperty _gifBGColor; 
	SerializedProperty _gifUseTransparancy;
	SerializedProperty _gifAlphaBGColor;
    SerializedProperty _gifDelay; 
	SerializedProperty _gifQuality;
	SerializedProperty _gifRepeatNum;



    //parameter list
    float tValue = 0;
    SPUM_Exporter SPB;
    AnimationClip tAnimSave;
    float tAnimTimer;
    float tAnimTimerFactor;
    float timeSave;
    //float tValuee = 0 ;
    //bool objectSelectionFoldout = false;
    //int objectSelectionToolbar = 0;
    bool allChecked = false;
    void OnEnable()
    {
        _unitPrefab = serializedObject.FindProperty("_unitPrefab");
        _separated = serializedObject.FindProperty("_separated");
        _imageName = serializedObject.FindProperty("_imageName");
        _imageSize = serializedObject.FindProperty("_imageSize");
        _fullSize = serializedObject.FindProperty("_fullSize");
        _scaleFactor = serializedObject.FindProperty("_scaleFactor");
        _frameRate = serializedObject.FindProperty("_frameRate");
        _advanced = serializedObject.FindProperty("_advanced");
        _camera = serializedObject.FindProperty("_camera");
        _anim = serializedObject.FindProperty("_anim");
        _objectPivot = serializedObject.FindProperty("_objectPivot");
        _objectNow = serializedObject.FindProperty("_objectNow");
        _imgBG = serializedObject.FindProperty("_imgBG");
        _bgSet = serializedObject.FindProperty("_bgSet");
        _animNum = serializedObject.FindProperty("_animNum");
        timer = serializedObject.FindProperty("timer");
        timerForSave = serializedObject.FindProperty("timerForSave");
        useTimer = serializedObject.FindProperty("useTimer");
        _netAnimClip = serializedObject.FindProperty("_netAnimClip");
        animNum = serializedObject.FindProperty("animNum");
        _textSaveList = serializedObject.FindProperty("_textSaveList");
        _gifExportUse = serializedObject.FindProperty("_gifExportUse");
        _gifBGColor = serializedObject.FindProperty("_gifBGColor");
        _gifUseTransparancy = serializedObject.FindProperty("_gifUseTransparancy");
        _gifAlphaBGColor = serializedObject.FindProperty("_gifAlphaBGColor");
        _gifDelay = serializedObject.FindProperty("_gifDelay");
        _gifQuality = serializedObject.FindProperty("_gifQuality");
        _gifRepeatNum = serializedObject.FindProperty("_gifRepeatNum");

    }
    private void OnUnitPrefabChanged()
    {
        SPB.CheckObjNow();
 
        SPB.MakeObjNow();
    }

    // Start is called before the first frame update
    public override void OnInspectorGUI()
    {

        SPB = (SPUM_Exporter)target;
        // base.OnInspectorGUI();
        // EditorGUILayout.BeginVertical();
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(_unitPrefab);
        if (EditorGUI.EndChangeCheck())
        {
            serializedObject.ApplyModifiedProperties();
            OnUnitPrefabChanged();
        }
        //EditorGUILayout.PropertyField(_unitPrefab);
        EditorGUILayout.PropertyField(_separated);
        EditorGUILayout.PropertyField(_imageName);
        EditorGUILayout.PropertyField(_imageSize);
        EditorGUILayout.PropertyField(_fullSize);
        EditorGUILayout.PropertyField(_scaleFactor);
        EditorGUILayout.PropertyField(_frameRate);
        //Gif Exporter Value Sync;
        //For Gif Exporter
        if (GUILayout.Button("RELOAD CLIP DATA"))
        {
            SPB.LoadAnimationStateClip();
        }
        EditorGUILayout.HelpBox("Extracting the checked clips.",MessageType.Info);
        if(SPB.items.Count > 0)
        {
            EditorGUILayout.BeginHorizontal();
            bool newAllChecked = EditorGUILayout.Toggle(allChecked, GUILayout.Width(20));
            if (newAllChecked != allChecked)
            {
                allChecked = newAllChecked;
                SPB.items.ForEach(item => item.isChecked = allChecked);
            }
            EditorGUILayout.LabelField( "STATES", GUILayout.Width(100) );
            EditorGUILayout.LabelField( "ANIMATION CLIP");
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(-9);
            GUIStyle horizontalLine = new GUIStyle(GUI.skin.horizontalSlider);
            EditorGUILayout.LabelField("", horizontalLine);
            GUILayout.Space(-9);
        }
        for (int i = 0; i < SPB.items.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            SPB.items[i].isChecked = EditorGUILayout.Toggle( SPB.items[i].isChecked, GUILayout.Width(20));
            EditorGUILayout.LabelField( ""+SPB.items[i].name, GUILayout.Width(100) );
            SPB.items[i].animationClip = (AnimationClip)EditorGUILayout.ObjectField(SPB.items[i].animationClip, typeof(AnimationClip), false);
            // if (GUILayout.Button("Remove"))
            // {
            //     SPB.items.RemoveAt(i);
            // }
            EditorGUILayout.EndHorizontal();
        }

         

        float xSize = Handles.GetMainGameViewSize().x;
        float ySize = Handles.GetMainGameViewSize().y;
        
        if( xSize < 1920f || ySize < 1080)
        {
            EditorGUILayout.PropertyField(_gifExportUse, new GUIContent("Disable Gif Export"));
            EditorGUILayout.HelpBox("!!! Please set Game Windows size over the Full HD (1920x1080) to use export method !!!",MessageType.Error);
            if (GUILayout.Button("OK Change resolution now.",GUILayout.Height(50))) 
            {
                SetCaptureSize();
            }
            return;
        }


        if(!SPB._gifExportUse)
        {
            EditorGUILayout.PropertyField(_gifExportUse, new GUIContent("Enable Gif Export"));
        }
        else
        {
            EditorGUILayout.PropertyField(_gifExportUse, new GUIContent("Disable Gif Export"));
            EditorGUILayout.HelpBox("Gif Exporter isn't stable now (Preview Version).",MessageType.Warning);
            // EditorGUILayout.IntSlider (_gifQuality, 1, 100, new GUIContent ("Gif Quality"));
            EditorGUILayout.PropertyField(_gifDelay, new GUIContent("Animation Time Delay"));
            // EditorGUILayout.PropertyField(_gifRepeatNum, new GUIContent("Animation Repeat Number"));
            if(!SPB._gifUseTransparancy)
            {
                EditorGUILayout.PropertyField(_gifUseTransparancy, new GUIContent("Enable Backgrund Transparancy"));
                // EditorGUILayout.HelpBox("Basic BG Color is white",MessageType.Info);
                EditorGUILayout.PropertyField(_gifBGColor);
            }
            else
            {
                EditorGUILayout.PropertyField(_gifUseTransparancy, new GUIContent("Disable Backgrund Transparancy"));
                // EditorGUILayout.HelpBox("Basic Alpha BG Color is Green",MessageType.Info);
                EditorGUILayout.PropertyField(_gifAlphaBGColor);
            }
        }

        EditorGUILayout.HelpBox("Adavnced settings only for more options (not recommended)",MessageType.Info);
        if(!SPB._advanced)
        {
            EditorGUILayout.PropertyField(_advanced, new GUIContent("Advanced Settings Show"));
        }
        else
        {
            EditorGUILayout.PropertyField(_advanced, new GUIContent("Advanced Settings Off"));
            EditorGUILayout.HelpBox("Editing is not recommended.",MessageType.Warning);
            EditorGUILayout.PropertyField(_camera);
            EditorGUILayout.PropertyField(_anim);
            EditorGUILayout.PropertyField(_objectPivot);
            EditorGUILayout.PropertyField(_objectNow);
            EditorGUILayout.PropertyField(_imgBG);
            EditorGUILayout.PropertyField(_bgSet);
            EditorGUILayout.PropertyField(_animNum);
            EditorGUILayout.PropertyField(timer);
            EditorGUILayout.PropertyField(timerForSave);
            EditorGUILayout.PropertyField(useTimer);
            EditorGUILayout.PropertyField(_netAnimClip);
            EditorGUILayout.PropertyField(animNum);
            //EditorGUILayout.PropertyField(animationClips);
            //EditorGUILayout.PropertyField(_animNameNow);
            EditorGUILayout.PropertyField(_textSaveList);
        }

        

        serializedObject.ApplyModifiedProperties();

    

        if (GUILayout.Button("Make Sprite Images",GUILayout.Height(50))) 
        {
            if(!SPB.useTimer)
            {
                var isAnyAnimationChecked = SPB.items.Any(item => item.isChecked);
                if (!isAnyAnimationChecked)
                {
                    Debug.Log("No animations are checked. The process will not start.");
                    return;
                }
                Debug.Log("Starting Export Sprite Sheets...");
                SPB.ImageNumber = 0;
                SPB.animNum = 0;

                SPB._textSaveList.Clear();
                SPB._netAnimClip = true;
            }
        }

        if(SPB._unitPrefab!=null)
        {
            if (GUILayout.Button("Remove Object",GUILayout.Height(50))) 
            {
                Debug.Log("Removed Prefab Object!!");
                SPB.items.Clear();
                SPB._unitPrefab = null;
                SPB._imageName = "";
            }
        }
        SPB._imgBG.sizeDelta = new Vector2( SPB._imageSize.x, SPB._imageSize.y );
        if(SPB._unitPrefab==null)
        {
            SPB.CheckObjNow();
        }
        else
        {
            SPB.MakeObjNow();
            SPB._objectPivot.transform.localScale = new Vector3(SPB._scaleFactor,SPB._scaleFactor,SPB._scaleFactor);
        }

        UpdateTimer();
        ProcessNextAnimationClip();
    }
    public void ExporterShot()
    {
        AnimationMode.StartAnimationMode();
        AnimationMode.BeginSampling();
        AnimationMode.SampleAnimationClip(SPB._anim.gameObject,tAnimSave,tValue);
        AnimationMode.EndSampling();
    }

    public void ExporterReset()
    {
        AnimationMode.StartAnimationMode();
        AnimationMode.BeginSampling();
        AnimationMode.SampleAnimationClip(SPB._objectNow,tAnimSave,0);
        AnimationMode.EndSampling();
        AnimationMode.StopAnimationMode();
    }

private void UpdateTimer()
{
    if (!SPB.useTimer) return;

    float elapsedTime = Time.realtimeSinceStartup - timeSave;
    timeSave = Time.realtimeSinceStartup;
    SPB.timer += elapsedTime;

    if (SPB.timer <= SPB.timerForSave) return;

    ProcessFrame();
}

private void ProcessFrame()
{
    tValue += tAnimTimerFactor;
    SPB.timer = 0;
    SPB.frameNowNumber++;

    if (SPB.frameNowNumber >= SPB._frameNumber)
    {
        ProcessAnimation();
    }
    else
    {
        CaptureAndSaveScreenshot();
    }
}

private void ProcessAnimation()
{
    SPB.frameNowNumber = 0;
    tValue = 0;
    SPB.useTimer = false;

    if (SPB._separated)
    {
        SPB._sepaName = tAnimSave.name;
    }

    if (MoveToNextCheckedAnimation())
    {
        if (SPB._separated) SPB.MakeScreenShotFile();
        SPB._netAnimClip = true;
    }
    else
    {
        SPB.MakeScreenShotFile();
        SPB.PrintEndMessage();
        ExporterReset();
        SPB.MakeGifAnimation();
    }
}

private bool MoveToNextCheckedAnimation()
{
    do
    {
        SPB.animNum++;
        if (SPB.animNum >= SPB.items.Count)
        {
            return false;
        }
    } while (!SPB.items[SPB.animNum].isChecked);

    return true;
}


private void CaptureAndSaveScreenshot()
{
    ExporterShot();
    SPB.SetScreenShot();
}

private void ProcessNextAnimationClip()
{
    if (!SPB._netAnimClip) return;

    //Debug.Log(SPB.animNum);
    SPB._netAnimClip = false;

    if (!SPB.items[SPB.animNum].isChecked)
    {
        // 현재 애니메이션이 체크되지 않았다면 다음 체크된 애니메이션으로 이동
        if (!MoveToNextCheckedAnimation())
        {
            // 더 이상 체크된 애니메이션이 없으면 종료
            SPB.MakeScreenShotFile();
            SPB.PrintEndMessage();
            ExporterReset();
            SPB.MakeGifAnimation();
            return;
        }
    }

    AnimationClip currentAnim = SPB.items[SPB.animNum].animationClip;
    string stateName = SPB.items[SPB.animNum].name;

    if (currentAnim == null) return;

    SetupNewAnimation(currentAnim, stateName);
    CaptureAndSaveScreenshot();
}

private void SetupNewAnimation(AnimationClip anim, string stateName)
{
    tAnimSave = anim;
    tAnimTimer = tAnimSave.length;
    tAnimTimerFactor = 1f / (SPB._frameRate * 1f);
    SPB._frameNumber = (int)(tAnimTimer / tAnimTimerFactor);
    
    Debug.Log($"[[Generating : {stateName}_{tAnimSave.name} || Time Length : {tAnimSave.length} sec || Frame Numbers : {SPB._frameNumber}]]");
    
    SPB.frameNowNumber = 0;
    tValue = 0;
    SPB.timer = 0;
    SPB.useTimer = true;
    timeSave = Time.realtimeSinceStartup;

    SPB._textSaveList.Clear();
}
    public void SetCaptureSize()
    {
        int idx = FindSize(GameViewSizeGroupType.Standalone, 1920, 1080);
        if (idx != -1)
            SetSize(idx);
    }
    public int FindSize(GameViewSizeGroupType sizeGroupType, int width, int height)
    {
        // goal:
        // GameViewSizes group = gameViewSizesInstance.GetGroup(sizeGroupType);
        // int sizesCount = group.GetBuiltinCount() + group.GetCustomCount();
        // iterate through the sizes via group.GetGameViewSize(int index)

        var group = GetGroup(sizeGroupType);
        var groupType = group.GetType();
        var getBuiltinCount = groupType.GetMethod("GetBuiltinCount");
        var getCustomCount = groupType.GetMethod("GetCustomCount");
        int sizesCount = (int)getBuiltinCount.Invoke(group, null) + (int)getCustomCount.Invoke(group, null);
        var getGameViewSize = groupType.GetMethod("GetGameViewSize");
        var gvsType = getGameViewSize.ReturnType;
        var widthProp = gvsType.GetProperty("width");
        var heightProp = gvsType.GetProperty("height");
        var indexValue = new object[1];
        for (int i = 0; i < sizesCount; i++)
        {
            indexValue[0] = i;
            var size = getGameViewSize.Invoke(group, indexValue);
            int sizeWidth = (int)widthProp.GetValue(size, null);
            int sizeHeight = (int)heightProp.GetValue(size, null);
            if (sizeWidth == width && sizeHeight == height)
                return i;
        }
        return -1;
    }
    private object GetGroup(GameViewSizeGroupType type)
    {
        var sizesType = typeof(Editor).Assembly.GetType("UnityEditor.GameViewSizes");
        var singleType = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
        var instanceProp = singleType.GetProperty("instance");
        var getGroup = sizesType.GetMethod("GetGroup");
        var gameViewSizesInstance = instanceProp.GetValue(null, null);
        return getGroup.Invoke(gameViewSizesInstance, new object[] { (int)type });
    }
    public void SetSize(int index)
    {
        var gvWndType = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
        var selectedSizeIndexProp = gvWndType.GetProperty("selectedSizeIndex",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var gvWnd = EditorWindow.GetWindow(gvWndType);
        selectedSizeIndexProp.SetValue(gvWnd, index, null);
    }
}
