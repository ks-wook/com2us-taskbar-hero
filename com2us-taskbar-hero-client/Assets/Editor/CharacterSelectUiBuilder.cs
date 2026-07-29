using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TaskbarHero.Client.UI;

namespace TaskbarHero.ClientEditor
{
    /// <summary>
    /// 캐릭터 선택 패널(CharacterSelectPanel.prefab)에 class_master 기본 능력치 블록을 심는 도구.
    /// 기존 계층(Title·Description·Select/Back 버튼·픽셀 스프라이트)은 유지하고 위치만 재배치한 뒤,
    /// 'StatsHeader' + 'StatRadar'(5각형 레이더 + 꼭짓점 라벨/수치)를 다시 생성한다
    /// (멱등 — 재실행 시 기존 블록 제거 후 재생성).
    /// 표시 능력치는 <see cref="ClassStatInfo.DisplayKinds"/>(체력·공격력·공격속도·이동속도·방어력) 5종이며
    /// 치명확률·치명피해는 표시하지 않는다.
    /// 패널 크기는 그대로 둔다(1920x1080 기준 CanvasScaler 논리 높이가 약 360px이라 더 키우면 화면을 벗어남).
    /// 메뉴: TaskbarHero/UI/캐릭터 선택 패널 능력치 배선
    /// </summary>
    public static class CharacterSelectUiBuilder
    {
        private const string PrefabPath = "Assets/Prefabs/UI/CharacterSelectPanel.prefab";

        private const float RadarCenterY = 178f;  // 패널 상단에서 레이더 중심까지의 거리
        private const float RadarRadius = 34f;    // 바깥 링 반지름
        private const float LabelRadius = 52f;    // 꼭짓점 라벨(이름+수치) 중심까지의 거리
        private const float LabelWidth = 44f;
        private const float LabelHeight = 24f;

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
                var radar = BuildRadarBlock(panel, out var valueTexts);
                WireController(root, radar, valueTexts);
                BakePreview(root);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"[CharacterSelectUiBuilder] 완료: 5각형 능력치 레이더({valueTexts.Length}축) 배선 → {PrefabPath}");
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

        /// <summary>
        /// '기본 능력치' 헤더와 5각형 레이더(축별 이름 + 수치 라벨)를 생성한다(기존 블록은 제거).
        /// 꼭짓점 순서는 <see cref="ClassStatInfo.DisplayKinds"/>와 같고, 맨 위에서 시계 방향으로 배치된다.
        /// </summary>
        private static StatRadarChart BuildRadarBlock(RectTransform panel, out Text[] valueTexts)
        {
            DestroyIfExists(panel, "StatsHeader");
            DestroyIfExists(panel, "Stats");      // 구버전(7행 게이지) 블록 제거
            DestroyIfExists(panel, "StatRadar");

            var header = CreateText(panel, "StatsHeader", "기본 능력치", 11, FontStyle.Bold,
                new Color(0.68f, 0.65f, 0.56f), TextAnchor.MiddleCenter);
            header.anchorMin = header.anchorMax = new Vector2(0.5f, 1f);
            header.pivot = new Vector2(0.5f, 1f);
            header.anchoredPosition = new Vector2(0f, -98f);
            header.sizeDelta = new Vector2(130f, 14f);

            // 레이더 본체(스프라이트 없이 메시로 그리는 Graphic).
            var chartRect = CreateRect(panel, "StatRadar");
            chartRect.anchorMin = chartRect.anchorMax = new Vector2(0.5f, 1f);
            chartRect.pivot = new Vector2(0.5f, 0.5f);
            chartRect.anchoredPosition = new Vector2(0f, -RadarCenterY);
            chartRect.sizeDelta = new Vector2(RadarRadius * 2f, RadarRadius * 2f);

            // Graphic 은 CanvasRenderer 가 있어야 메시가 실제로 출력된다(없으면 오각형이 안 보인다).
            if (chartRect.GetComponent<CanvasRenderer>() == null)
            {
                chartRect.gameObject.AddComponent<CanvasRenderer>();
            }
            var radar = chartRect.gameObject.AddComponent<StatRadarChart>();
            radar.raycastTarget = false;
            var radarSo = new SerializedObject(radar);
            radarSo.FindProperty("axisCount").intValue = ClassStatInfo.DisplayKinds.Length;
            radarSo.FindProperty("radius").floatValue = RadarRadius;
            radarSo.ApplyModifiedPropertiesWithoutUndo();

            // 꼭짓점 라벨(이름 + 수치). 레이더와 같은 각도 계산을 써서 축과 정확히 맞춘다.
            var kinds = ClassStatInfo.DisplayKinds;
            valueTexts = new Text[kinds.Length];
            for (int i = 0; i < kinds.Length; i++)
            {
                Vector2 dir = radar.DirectionOf(i);
                var label = CreateRect(chartRect, "Label_" + kinds[i]);
                label.anchorMin = label.anchorMax = new Vector2(0.5f, 0.5f);
                label.pivot = new Vector2(0.5f, 0.5f);
                label.anchoredPosition = new Vector2(dir.x * LabelRadius, dir.y * LabelRadius);
                label.sizeDelta = new Vector2(LabelWidth, LabelHeight);

                var name = CreateText(label, "Name", ClassStatInfo.LabelOf(kinds[i]), 9, FontStyle.Normal,
                    ClassStatInfo.ColorOf(kinds[i]), TextAnchor.MiddleCenter);
                name.anchorMin = new Vector2(0f, 0.5f);
                name.anchorMax = new Vector2(1f, 1f);
                name.offsetMin = Vector2.zero;
                name.offsetMax = Vector2.zero;

                var value = CreateText(label, "Value", "-", 9, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
                value.anchorMin = new Vector2(0f, 0f);
                value.anchorMax = new Vector2(1f, 0.5f);
                value.offsetMin = Vector2.zero;
                value.offsetMax = Vector2.zero;

                valueTexts[i] = value.GetComponent<Text>();
            }

            return radar;
        }

        /// <summary>컨트롤러에 레이더와 축별 수치 텍스트를 배선한다.</summary>
        private static void WireController(GameObject root, StatRadarChart radar, Text[] valueTexts)
        {
            var controller = root.GetComponent<CharacterSelectPanelController>();
            if (controller == null)
            {
                Debug.LogWarning("[CharacterSelectUiBuilder] CharacterSelectPanelController를 찾지 못해 배선을 건너뜁니다.");
                return;
            }

            var so = new SerializedObject(controller);
            so.FindProperty("statRadar").objectReferenceValue = radar;
            var prop = so.FindProperty("statValueTexts");
            prop.arraySize = valueTexts.Length;
            for (int i = 0; i < valueTexts.Length; i++)
            {
                prop.GetArrayElementAtIndex(i).objectReferenceValue = valueTexts[i];
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
    }
}
