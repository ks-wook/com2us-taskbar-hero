using UnityEngine;
using UnityEditor;
using System.Linq;
using System.Collections.Generic;
using UnityEngine.UI;

[CustomEditor(typeof(SPUM_PreviewMatchingList))]
public class SPUM_PreviewMatchingListEditor : Editor
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

        SPUM_PreviewMatchingList matchingList = (SPUM_PreviewMatchingList)target;
        EditorGUILayout.LabelField("Custom Editor View", EditorStyles.boldLabel);

        // 정렬 버튼 추가
        if (GUILayout.Button("Sort by Index"))
        {
            SortByName(matchingList);
        }
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Index",GUILayout.Width(20));
        EditorGUILayout.LabelField("Unit",GUILayout.Width(50));
        EditorGUILayout.LabelField("Part",GUILayout.Width(30));
        EditorGUILayout.LabelField("PartSubType",GUILayout.Width(30));
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
            matchingList.matchingTables[i].PartType = EditorGUILayout.TextField(matchingList.matchingTables[i].PartType,GUILayout.Width(30));
            matchingList.matchingTables[i].PartSubType = EditorGUILayout.TextField(matchingList.matchingTables[i].PartSubType,GUILayout.Width(30));
            matchingList.matchingTables[i].Dir = EditorGUILayout.TextField(matchingList.matchingTables[i].Dir,GUILayout.Width(50));
            matchingList.matchingTables[i].Structure = EditorGUILayout.TextField(matchingList.matchingTables[i].Structure,GUILayout.Width(100));
            matchingList.matchingTables[i].ItemPath = EditorGUILayout.TextField(matchingList.matchingTables[i].ItemPath,GUILayout.Width(40));
            matchingList.matchingTables[i].image = (Image)EditorGUILayout.ObjectField(matchingList.matchingTables[i].image, typeof(Image), true, GUILayout.Width(80));
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
            matchingList.matchingTables.Add(new PreviewMatchingElement());
        }
        if (GUILayout.Button("Load Item"))
        {
            matchingList.LoadItems();
        }
        if (GUI.changed)
        {
            EditorUtility.SetDirty(matchingList);
        }
    }
    private void SwapItems(List<PreviewMatchingElement> list, int indexA, int indexB)
    {
        PreviewMatchingElement temp = list[indexA];
        list[indexA] = list[indexB];
        list[indexB] = temp;
    }
    private void SortByName(SPUM_PreviewMatchingList matchingList)
    {
        matchingList.matchingTables = matchingList.matchingTables.OrderBy(item => item.Index).ToList();
        EditorUtility.SetDirty(matchingList);
    }
}