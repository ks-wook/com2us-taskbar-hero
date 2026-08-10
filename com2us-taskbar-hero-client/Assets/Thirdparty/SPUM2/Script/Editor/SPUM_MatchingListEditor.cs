using UnityEngine;
using UnityEditor;
using System.Linq;

[CustomEditor(typeof(SPUM_MatchingList))]
public class SPUM_MatchingListEditor : Editor
{
    SerializedProperty matchingTablesProperty;

    private void OnEnable()
    {
        matchingTablesProperty = serializedObject.FindProperty("matchingTables");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // 기본 인스펙터 뷰 표시
        EditorGUILayout.PropertyField(matchingTablesProperty, true);

        EditorGUILayout.Space(10);  // 약간의 간격 추가

        SPUM_MatchingList matchingList = (SPUM_MatchingList)target;
        EditorGUILayout.LabelField("Custom Editor View", EditorStyles.boldLabel);

        // 정렬 버튼 추가
        if (GUILayout.Button("Sort by Index"))
        {
            SortByName(matchingList);
        }
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Index",GUILayout.Width(20));
        EditorGUILayout.LabelField("UnitType",GUILayout.Width(50));
        EditorGUILayout.LabelField("PartType",GUILayout.Width(50));
        EditorGUILayout.LabelField("PartSubType",GUILayout.Width(50));
        EditorGUILayout.LabelField("Direction",GUILayout.Width(50));
        EditorGUILayout.LabelField("Structure",GUILayout.Width(100));
        EditorGUILayout.LabelField("ItemPath",GUILayout.Width(40));
        EditorGUILayout.LabelField("Renderer", GUILayout.Width(80));
        EditorGUILayout.LabelField("Color",GUILayout.Width(50));
        EditorGUILayout.EndHorizontal();

        for (int i = 0; i < matchingList.matchingTables.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            matchingList.matchingTables[i].Index = EditorGUILayout.IntField(matchingList.matchingTables[i].Index,GUILayout.Width(20));
            matchingList.matchingTables[i].UnitType = EditorGUILayout.TextField(matchingList.matchingTables[i].UnitType,GUILayout.Width(50));
            matchingList.matchingTables[i].PartType = EditorGUILayout.TextField(matchingList.matchingTables[i].PartType,GUILayout.Width(50));
            matchingList.matchingTables[i].PartSubType = EditorGUILayout.TextField(matchingList.matchingTables[i].PartSubType,GUILayout.Width(50));
            matchingList.matchingTables[i].Dir = EditorGUILayout.TextField(matchingList.matchingTables[i].Dir,GUILayout.Width(50));
            matchingList.matchingTables[i].Structure = EditorGUILayout.TextField(matchingList.matchingTables[i].Structure,GUILayout.Width(100));
            matchingList.matchingTables[i].ItemPath = EditorGUILayout.TextField(matchingList.matchingTables[i].ItemPath,GUILayout.Width(40));
            matchingList.matchingTables[i].renderer = (SpriteRenderer)EditorGUILayout.ObjectField(matchingList.matchingTables[i].renderer, typeof(SpriteRenderer), true, GUILayout.Width(80));
            matchingList.matchingTables[i].Color = (Color32)EditorGUILayout.ColorField(matchingList.matchingTables[i].Color,GUILayout.Width(50));
            GUI.enabled = i > 0;
            if (GUILayout.Button("▲", GUILayout.Width(20)))
            {
                SwapItems(matchingList.matchingTables, i, i - 1);
            }
            GUI.enabled = i < matchingList.matchingTables.Count - 1;
            if (GUILayout.Button("▼", GUILayout.Width(20)))
            {
                SwapItems(matchingList.matchingTables, i, i + 1);
            }
            GUI.enabled = true;
            if (GUILayout.Button("-", GUILayout.Width(20)))
            {
                matchingList.matchingTables.RemoveAt(i);
                i--;
            }

            EditorGUILayout.EndHorizontal();
        }

        if (GUILayout.Button("Add New Item"))
        {
            matchingList.matchingTables.Add(new MatchingElement());
        }
        if (GUILayout.Button("Find All Renderer Component"))
        {
            if(EditorUtility.DisplayDialog("Warrning",  "Warning: Data mapping table is being initialized.", "OK","Cancel"))
            {
                if(EditorUtility.DisplayDialog("Warrning",   "Are you sure you want to proceed?", "Yes","No"))
                {
                    Debug.Log("초기화됨");
                    matchingList.LoadItems();
                }
                Debug.Log("초기화됨");
            }
        }
        if (GUI.changed)
        {
            EditorUtility.SetDirty(matchingList);
        }
    }
    private void SwapItems(System.Collections.Generic.List<MatchingElement> list, int indexA, int indexB)
    {
        MatchingElement temp = list[indexA];
        list[indexA] = list[indexB];
        list[indexB] = temp;
    }
    private void SortByName(SPUM_MatchingList matchingList)
    {
        matchingList.matchingTables = matchingList.matchingTables.OrderBy(item => item.Index).ToList();
        EditorUtility.SetDirty(matchingList);
    }
}
