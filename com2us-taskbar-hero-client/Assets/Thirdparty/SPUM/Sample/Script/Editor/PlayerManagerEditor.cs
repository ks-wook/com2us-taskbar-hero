using UnityEngine;
using UnityEditor;
[CustomEditor(typeof(PlayerManager))]
public class PlayerManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        PlayerManager manager = (PlayerManager)target;

        if (GUILayout.Button("CREATE UNIT"))
        {
            manager.GetPlayerList();
        }
        if (GUILayout.Button("Align UNIT"))
        {
            manager.SetAlignUnits();
        }
        if (GUILayout.Button("CLEAR UNIT"))
        {
            manager.ClearPlayerList();
        }
        if (GUILayout.Button("CAPTURE UNITS"))
        {
            manager.SetScreenShot();
            AssetDatabase.Refresh();
        }
    }
}