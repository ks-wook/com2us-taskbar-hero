using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Linq;

public class SPUM_FolderStructureCloner : EditorWindow
{
    private string newFolderPath = "Assets/SPUM/Resources/Addons/";
    private string selectedPath = "";
    private Vector2 scrollPosition;
    private List<SPUMFolderNode> folderStructure = new List<SPUMFolderNode>();

    [MenuItem("SPUM/Create Addon Folder Structure")]
    public static void ShowWindow()
    {
        GetWindow<SPUM_FolderStructureCloner>("Folder Cloner");
    }
    public void InitFolderStructure()
    {
        var input = 
@"NewAddon|true
-0_Unit|true
--0_Sprite|true
---0_Hair|true
---1_FaceHair|true
---1_Body|true
---2_Cloth|true
---3_Pant|true
---4_Helmet|true
---5_Armor|true
---6_Weapons|true
----0_Sword|true
----1_Axe|true
----2_Bow|true
----3_Shield|true
----4_Spear|true
----5_Wand|true
----6_Hammer|true
----7_Dagger|true
----8_Mace|true
---7_Back|true
--1_Animation|true
---0_Idle|true
---1_Move|true
---2_Attack|true
----0_MeleeAttack|true
----1_LongRangeAttack|true
----2_MagicAttack|true
---3_Damaged|true
---4_Death|true
---5_Debuff|true
---6_Other|true
----0_Concentrate|true
----1_Buff|true
----2_Etc|true
-1_Horse|true
--0_Sprite|true
---0_Hair|true
---1_FaceHair|true
---2_Cloth|true
---3_Pant|true
---4_Helmet|true
---5_Armor|true
---6_Weapons|true
----0_Sword|true
----1_Axe|true
----2_Bow|true
----3_Shield|true
----4_Spear|true
----5_Wand|true
----6_Hammer|true
----7_Dagger|true
----8_Mace|true
---7_Back|true
--1_Animation|true
---0_Idle|true
---1_Move|true
---2_Attack|true
----0_MeleeAttack|true
----1_LongRangeAttack|true
----2_MagicAttack|true
---3_Damaged|true
---4_Death|true
---5_Debuff|true
---6_Other|true
----0_Concentrate|true
----1_Buff|true
----2_Etc|true
-2_Prefabs|true"
;
        folderStructure = ParseFolderStructure(input);

    }
    private void OnGUI()
    {
        
        GUILayout.Label("Select a folder and Load Structure.", EditorStyles.boldLabel);
        GUILayout.Space(10);
        if (GUILayout.Button("Copy the folder structure"))
        {
            selectedPath = EditorUtility.OpenFolderPanel("Select Folder to Copy", newFolderPath, "");
            if (!string.IsNullOrEmpty(selectedPath))
            {
                folderStructure = GetFolderStructure(selectedPath);
            }
        }
        GUILayout.Space(10);
        GUILayout.Label("Initialize with the default folder structure.", EditorStyles.boldLabel);
        GUILayout.Space(10);
        if (GUILayout.Button("Initialize Structure"))
        {
            InitFolderStructure();
        }
        GUILayout.Space(10);
        GUILayout.Label("Quickly create with a basic folder structure.", EditorStyles.boldLabel);
        GUILayout.Space(10);
        
        if (GUILayout.Button("Quick Create"))
        {
            InitFolderStructure();
            CreateAddonFolderStructure();
        }
        // if (GUILayout.Button("Add New Folder"))
        // {
        //     folderStructure.Add(new SPUMFolderNode { Name = "New Folder", IsExpanded = true });
        // }
        if (folderStructure.Any())
        {
            GUILayout.Label("Folder Structure (Editable):", EditorStyles.boldLabel);
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            DrawFolderStructure(folderStructure, 0);
            EditorGUILayout.EndScrollView();



            if (GUILayout.Button("Create Folder Structure"))
            {
                CreateAddonFolderStructure();
            }
        }

    }
    public List<SPUMFolderNode> ParseFolderStructure(string input, char separator = '|', char indentChar = '-')
    {
        var lines = input.Split('\n');
        var rootNodes = new List<SPUMFolderNode>();
        var stack = new Stack<SPUMFolderNode>();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            // indentChar 개수 세기
            int indent = line.TakeWhile(c => c == indentChar).Count();
            var trimmedLine = line.Substring(indent);
            var parts = trimmedLine.Split(separator);

            var node = new SPUMFolderNode
            {
                Name = parts[0].TrimStart(),
                IsExpanded = parts.Length > 1 && bool.TryParse(parts[1], out bool isExpanded) && isExpanded,
                Children = new List<SPUMFolderNode>()  // 명시적으로 Children 리스트 초기화
            };

            while (stack.Count > 0 && stack.Count > indent)
            {
                stack.Pop();
            }

            if (stack.Count == 0)
            {
                rootNodes.Add(node);
            }
            else
            {
                stack.Peek().Children.Add(node);
            }

            stack.Push(node);
        }

        return rootNodes;
    }

    private void DrawFolderStructure(List<SPUMFolderNode> nodes, int indent)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            SPUMFolderNode node = nodes[i];
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * 20);

            node.IsExpanded = EditorGUILayout.Foldout(node.IsExpanded, "", true);
            node.Name = EditorGUILayout.TextField(node.Name);

            if (GUILayout.Button("+", GUILayout.Width(20)))
            {
                node.Children.Add(new SPUMFolderNode { Name = "New Folder", IsExpanded = true });
            }

            if (GUILayout.Button("-", GUILayout.Width(20)))
            {
                nodes.RemoveAt(i);
                i--;
            }

            EditorGUILayout.EndHorizontal();

            if (node.IsExpanded && node.Children.Any())
            {
                DrawFolderStructure(node.Children, indent + 1);
            }
        }
    }

    private List<SPUMFolderNode> GetFolderStructure(string path)
    {
        List<SPUMFolderNode> nodes = new List<SPUMFolderNode>();
        string folderName = Path.GetFileName(path);
        SPUMFolderNode node = new SPUMFolderNode { Name = folderName, IsExpanded = true };

        foreach (string subDir in Directory.GetDirectories(path))
        {
            node.Children.AddRange(GetFolderStructure(subDir));
        }

        nodes.Add(node);
        return nodes;
    }

    private void CreateAddonFolderStructure()
    {
        CreateFolderStructure(folderStructure, newFolderPath);
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Success", "Folder structure cloned successfully!", "OK");
    }

    private void CreateFolderStructure(List<SPUMFolderNode> nodes, string parentPath)
    {
        foreach (var node in nodes)
        {
            string newPath = Path.Combine(parentPath, node.Name);
            Directory.CreateDirectory(newPath);

            if (node.Children.Any())
            {
                CreateFolderStructure(node.Children, newPath);
            }
        }
    }
    [MenuItem("SPUM/Print Sprite Renderer Names")]
    static void PrintSpriteRendererNames()
    {
        // 선택된 오브젝트 가져오기
        GameObject selectedObject = Selection.activeGameObject;

        if (selectedObject == null)
        {
            Debug.LogWarning("Select Object");
            return;
        }

        // 스프라이트 랜더러 리스트 생성
        List<string> spriteRendererNames = new List<string>();

        // 하위 오브젝트의 모든 스프라이트 랜더러 찾기
        SpriteRenderer[] spriteRenderers = selectedObject.GetComponentsInChildren<SpriteRenderer>();
        string Text = "";
        foreach (SpriteRenderer sr in spriteRenderers)
        {
            spriteRendererNames.Add(sr.name);
            Text += sr.name + " / (" + sr.sortingOrder +")\n";
        }
        Debug.Log(Text);
        // 리스트 출력
        // foreach (string name in spriteRendererNames)
        // {
        //     Debug.Log(name);
        // }
    }
    [MenuItem("SPUM/Print Pivot Names")]
    static void PrintPivotNames()
    {
        // 선택된 오브젝트 가져오기
        GameObject selectedObject = Selection.activeGameObject;

        if (selectedObject == null)
        {
            Debug.LogWarning("Select Object");
            return;
        }

        // 스프라이트 랜더러 리스트 생성
        List<string> spriteRendererNames = new List<string>();

        // 하위 오브젝트의 모든 스프라이트 랜더러 찾기
        Transform[] transforms = selectedObject.GetComponentsInChildren<Transform>();
        string Text = "";
        foreach (var tr in transforms)
        {
            if(tr.name.Contains("P_") || tr.name.Contains("Pivot_")){
                Text += tr.name + "\n";
            }
        }
        Debug.Log(Text);
        // 리스트 출력
        // foreach (string name in spriteRendererNames)
        // {
        //     Debug.Log(name);
        // }
    }
}

public class SPUMFolderNode
{
    public string Name { get; set; }
    public bool IsExpanded { get; set; }
    public List<SPUMFolderNode> Children { get; set; } = new List<SPUMFolderNode>();
}