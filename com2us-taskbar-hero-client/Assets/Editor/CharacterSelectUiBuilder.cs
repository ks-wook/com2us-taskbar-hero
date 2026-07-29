using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TaskbarHero.Client.UI;

namespace TaskbarHero.ClientEditor
{
    /// <summary>
    /// 캐릭터 선택 패널(CharacterSelectPanel.prefab)에 class_master 기본 능력치 블록을 심는 도구.
    /// 기존 계층(Title·Description·Select/Back 버튼·픽셀 스프라이트)은 유지하고 위치만 재배치한 뒤,
    /// 'StatsHeader' + 'Stats'(CharacterStatRow 7행)를 다시 생성한다(멱등 — 재실행 시 기존 블록 제거 후 재생성).
    /// 패널 크기는 그대로 둔다(1920x1080 기준 CanvasScaler 논리 높이가 약 360px이라 더 키우면 화면을 벗어남).
    /// 메뉴: TaskbarHero/UI/캐릭터 선택 패널 능력치 배선
    /// </summary>
    public static class CharacterSelectUiBuilder
    {
        private const string PrefabPath = "Assets/Prefabs/UI/CharacterSelectPanel.prefab";

        private const float RowHeight = 18f;   // 행 간격(행 높이 17 + 여백 1)
        private const float RowInner = 17f;
        private const float StatsTop = 114f;   // 패널 상단에서 능력치 블록까지의 거리

        private static readonly ClassStatKind[] Kinds =
        {
            ClassStatKind.Hp, ClassStatKind.Atk, ClassStatKind.Def, ClassStatKind.AttackSpeed,
            ClassStatKind.CritChance, ClassStatKind.CritDamage, ClassStatKind.MoveSpeed,
        };

        [MenuItem("TaskbarHero/UI/캐릭터 선택 패널 능력치 배선")]
        public static void Build()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            if (root == null)
            {
                Debug.LogError($"[CharacterSelectUiBuilder] 프리팹을 열지 못했습니다: {PrefabPath}");
                return;
            }

            try
            {
                var panel = root.transform.Find("Panel") as RectTransform;
                if (panel == null)
                {
                    Debug.LogError("[CharacterSelectUiBuilder] 'Panel' 자식을 찾지 못했습니다.");
                    return;
                }

                Relayout(panel);
                var rows = BuildStatsBlock(panel);
                WireController(root, rows);
                BakePreview(root);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"[CharacterSelectUiBuilder] 완료: 기본 능력치 {rows.Length}행 배선 → {PrefabPath}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>능력치 블록 자리를 만들기 위해 제목·설명·버튼을 재배치한다(패널 크기는 유지).</summary>
        private static void Relayout(RectTransform panel)
        {
            var title = panel.Find("Title") as RectTransform;
            if (title != null)
            {
                title.anchoredPosition = new Vector2(0f, -10f);
                title.sizeDelta = new Vector2(130f, 30f);
                var text = title.GetComponent<Text>();
                if (text != null) text.fontSize = 20;
            }

            var description = panel.Find("Description") as RectTransform;
            if (description != null)
            {
                description.anchoredPosition = new Vector2(0f, -42f);
                description.sizeDelta = new Vector2(130f, 52f);
                var text = description.GetComponent<Text>();
                if (text != null)
                {
                    text.fontSize = 11;
                    text.verticalOverflow = VerticalWrapMode.Overflow; // 설명이 길어도 잘리지 않게
                }
            }

            var select = panel.Find("SelectButton") as RectTransform;
            if (select != null)
            {
                select.anchoredPosition = new Vector2(0f, 54f);
                select.sizeDelta = new Vector2(112f, 42f);
            }

            var back = panel.Find("BackButton") as RectTransform;
            if (back != null)
            {
                back.anchoredPosition = new Vector2(0f, 12f);
                back.sizeDelta = new Vector2(112f, 38f);
            }
        }

        /// <summary>'기본 능력치' 헤더와 능력치 7행을 생성해 돌려준다(기존 블록은 제거).</summary>
        private static CharacterStatRow[] BuildStatsBlock(RectTransform panel)
        {
            DestroyIfExists(panel, "StatsHeader");
            DestroyIfExists(panel, "Stats");

            var header = CreateText(panel, "StatsHeader", "기본 능력치", 11, FontStyle.Bold,
                new Color(0.68f, 0.65f, 0.56f), TextAnchor.MiddleCenter);
            header.anchorMin = header.anchorMax = new Vector2(0.5f, 1f);
            header.pivot = new Vector2(0.5f, 1f);
            header.anchoredPosition = new Vector2(0f, -98f);
            header.sizeDelta = new Vector2(130f, 14f);

            var block = CreateRect(panel, "Stats");
            block.anchorMin = block.anchorMax = new Vector2(0.5f, 1f);
            block.pivot = new Vector2(0.5f, 1f);
            block.anchoredPosition = new Vector2(0f, -StatsTop);
            block.sizeDelta = new Vector2(130f, Kinds.Length * RowHeight);

            var rows = new CharacterStatRow[Kinds.Length];
            for (int i = 0; i < Kinds.Length; i++)
            {
                rows[i] = CreateRow(block, Kinds[i], i);
            }
            return rows;
        }

        /// <summary>능력치 한 행(배경 + 게이지 + 라벨 + 수치)을 만들고 CharacterStatRow를 배선한다.</summary>
        private static CharacterStatRow CreateRow(RectTransform parent, ClassStatKind kind, int index)
        {
            var row = CreateRect(parent, "Row_" + kind);
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.anchoredPosition = new Vector2(0f, -index * RowHeight);
            row.sizeDelta = new Vector2(0f, RowInner);

            var bg = row.gameObject.AddComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.06f);
            bg.raycastTarget = false;

            // 게이지: 행 전체를 채우는 Filled 이미지(가로 방향). fillAmount 는 런타임에 비율로 갱신된다.
            var fillRect = CreateRect(row, "BarFill");
            Stretch(fillRect);
            var fill = fillRect.gameObject.AddComponent<Image>();
            fill.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.fillAmount = 0f;
            fill.color = ClassStatInfo.ColorOf(kind);
            fill.raycastTarget = false;

            var label = CreateText(row, "Label", ClassStatInfo.LabelOf(kind), 11, FontStyle.Normal,
                new Color(0.80f, 0.78f, 0.70f), TextAnchor.MiddleLeft);
            label.anchorMin = new Vector2(0f, 0f);
            label.anchorMax = new Vector2(0.55f, 1f);
            label.offsetMin = new Vector2(5f, 0f);
            label.offsetMax = Vector2.zero;

            var value = CreateText(row, "Value", "-", 11, FontStyle.Bold, Color.white, TextAnchor.MiddleRight);
            value.anchorMin = new Vector2(0.45f, 0f);
            value.anchorMax = new Vector2(1f, 1f);
            value.offsetMin = Vector2.zero;
            value.offsetMax = new Vector2(-5f, 0f);

            var component = row.gameObject.AddComponent<CharacterStatRow>();
            var so = new SerializedObject(component);
            so.FindProperty("kind").enumValueIndex = (int)kind;
            so.FindProperty("labelText").objectReferenceValue = label.GetComponent<Text>();
            so.FindProperty("valueText").objectReferenceValue = value.GetComponent<Text>();
            so.FindProperty("barFill").objectReferenceValue = fill;
            so.ApplyModifiedPropertiesWithoutUndo();
            return component;
        }

        /// <summary>컨트롤러의 statRows 배열에 생성한 행들을 배선한다.</summary>
        private static void WireController(GameObject root, CharacterStatRow[] rows)
        {
            var controller = root.GetComponent<CharacterSelectPanelController>();
            if (controller == null)
            {
                Debug.LogWarning("[CharacterSelectUiBuilder] CharacterSelectPanelController를 찾지 못해 배선을 건너뜁니다.");
                return;
            }

            var so = new SerializedObject(controller);
            var prop = so.FindProperty("statRows");
            prop.arraySize = rows.Length;
            for (int i = 0; i < rows.Length; i++)
            {
                prop.GetArrayElementAtIndex(i).objectReferenceValue = rows[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>에디터에서 프리팹 미리보기가 비어 보이지 않게 기사(classCode 1) 수치를 구워 넣는다.</summary>
        private static void BakePreview(GameObject root)
        {
            var controller = root.GetComponent<CharacterSelectPanelController>();
            if (controller == null)
            {
                return;
            }
            try
            {
                controller.SetStats(1);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[CharacterSelectUiBuilder] 미리보기 수치 채우기 실패(무해): {e.Message}");
            }
        }

        private static void DestroyIfExists(RectTransform parent, string childName)
        {
            var child = parent.Find(childName);
            if (child != null)
            {
                Object.DestroyImmediate(child.gameObject);
            }
        }

        private static RectTransform CreateRect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static RectTransform CreateText(Transform parent, string name, string content, int fontSize,
            FontStyle style, Color color, TextAnchor anchor)
        {
            var rect = CreateRect(parent, name);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = content;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.alignment = anchor;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return rect;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
