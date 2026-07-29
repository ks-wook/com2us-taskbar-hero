using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 능력치 5각형(레이더/스파이더) 차트. 축 개수만큼 정다각형 거미줄(등급 링 + 축선)을 깔고,
    /// 그 위에 0~1로 정규화된 값으로 만든 다각형을 채워 그린다. 스프라이트 없이 <see cref="Graphic"/>의
    /// 메시를 직접 생성하므로 에디터(프리팹 미리보기)에서도 그대로 보인다.
    /// 값 정규화(전체 직업 중 최대값 대비 비율)와 라벨 문구는 호출하는 쪽이 정한다.
    /// </summary>
    /// <remarks>
    /// <see cref="CanvasRenderer"/>가 없으면 메시가 만들어져도 화면에 아무것도 출력되지 않으므로
    /// <see cref="RequireComponent"/>로 항상 함께 붙게 강제한다.
    /// </remarks>
    [RequireComponent(typeof(CanvasRenderer))]
    public class StatRadarChart : MaskableGraphic
    {
        [Header("형태")]
        [Tooltip("축(꼭짓점) 개수. 5면 오각형.")]
        [SerializeField] private int axisCount = 5;
        [Tooltip("바깥 링까지의 반지름(px).")]
        [SerializeField] private float radius = 38f;
        [Tooltip("거미줄 등급 링 개수(바깥 링 포함).")]
        [SerializeField] private int webRings = 3;
        [SerializeField] private float lineWidth = 1f;
        [Tooltip("값이 0이어도 다각형이 점으로 뭉치지 않도록 하는 최소 비율.")]
        [SerializeField] private float minRatio = 0.08f;

        [Header("색")]
        [SerializeField] private Color webColor = new Color(1f, 1f, 1f, 0.13f);
        [SerializeField] private Color webEdgeColor = new Color(1f, 1f, 1f, 0.28f);
        [SerializeField] private Color spokeColor = new Color(1f, 1f, 1f, 0.18f);
        [SerializeField] private Color fillColor = new Color(0.95f, 0.78f, 0.35f, 0.34f);
        [SerializeField] private Color outlineColor = new Color(1f, 0.86f, 0.45f, 0.95f);
        [SerializeField] private Color vertexColor = new Color(1f, 0.95f, 0.75f, 1f);

        [Header("값(0~1) — 에디터 미리보기용으로 직렬화")]
        [SerializeField] private float[] values = new float[0];

        /// <summary>축 개수(꼭짓점 수).</summary>
        public int AxisCount => Mathf.Max(3, axisCount);

        /// <summary>바깥 링까지의 반지름(px). 라벨을 꼭짓점 바깥에 배치할 때 기준으로 쓴다.</summary>
        public float Radius => radius;

        /// <summary>
        /// 정규화된 능력치 값(0~1)을 적용하고 다시 그린다. 길이가 축 개수와 달라도
        /// 모자란 축은 0, 남는 값은 무시한다.
        /// </summary>
        public void SetValues(IList<float> ratios)
        {
            int n = AxisCount;
            if (values == null || values.Length != n)
            {
                values = new float[n];
            }
            for (int i = 0; i < n; i++)
            {
                float v = ratios != null && i < ratios.Count ? ratios[i] : 0f;
                values[i] = Mathf.Clamp01(v);
            }
            SetVerticesDirty();
        }

        /// <summary>index번째 축의 방향 단위 벡터(맨 위에서 시작해 시계 방향).</summary>
        public Vector2 DirectionOf(int index)
        {
            int n = AxisCount;
            float angle = Mathf.PI * 0.5f - (Mathf.PI * 2f * index) / n;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            int n = AxisCount;
            var dirs = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                dirs[i] = DirectionOf(i);
            }

            // 1) 거미줄 등급 링(안쪽은 옅게, 바깥 링만 진하게)
            int rings = Mathf.Max(1, webRings);
            for (int ring = 1; ring <= rings; ring++)
            {
                float r = radius * ring / rings;
                var color = ring == rings ? webEdgeColor : webColor;
                for (int i = 0; i < n; i++)
                {
                    AddLine(vh, dirs[i] * r, dirs[(i + 1) % n] * r, lineWidth, color);
                }
            }

            // 2) 축선(중심 → 꼭짓점)
            for (int i = 0; i < n; i++)
            {
                AddLine(vh, Vector2.zero, dirs[i] * radius, lineWidth, spokeColor);
            }

            // 3) 능력치 다각형(채움)
            var points = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                float v = values != null && i < values.Length ? Mathf.Clamp01(values[i]) : 0f;
                points[i] = dirs[i] * radius * Mathf.Max(minRatio, v);
            }

            int center = vh.currentVertCount;
            vh.AddVert(Vector3.zero, fillColor, Vector2.zero);
            for (int i = 0; i < n; i++)
            {
                vh.AddVert(points[i], fillColor, Vector2.zero);
            }
            for (int i = 0; i < n; i++)
            {
                vh.AddTriangle(center, center + 1 + i, center + 1 + ((i + 1) % n));
            }

            // 4) 능력치 다각형 외곽선 + 꼭짓점 표시
            for (int i = 0; i < n; i++)
            {
                AddLine(vh, points[i], points[(i + 1) % n], lineWidth + 0.6f, outlineColor);
            }
            for (int i = 0; i < n; i++)
            {
                AddDot(vh, points[i], lineWidth + 1.1f, vertexColor);
            }
        }

        /// <summary>두 점을 잇는 선을 두께만큼의 사각형(2 삼각형)으로 추가한다.</summary>
        private static void AddLine(VertexHelper vh, Vector2 a, Vector2 b, float width, Color color)
        {
            Vector2 dir = b - a;
            if (dir.sqrMagnitude < 0.0001f)
            {
                return;
            }
            Vector2 perp = new Vector2(-dir.y, dir.x).normalized * (width * 0.5f);

            int idx = vh.currentVertCount;
            vh.AddVert(a - perp, color, Vector2.zero);
            vh.AddVert(a + perp, color, Vector2.zero);
            vh.AddVert(b + perp, color, Vector2.zero);
            vh.AddVert(b - perp, color, Vector2.zero);
            vh.AddTriangle(idx, idx + 1, idx + 2);
            vh.AddTriangle(idx + 2, idx + 3, idx);
        }

        /// <summary>꼭짓점 강조용 작은 정사각 점을 추가한다.</summary>
        private static void AddDot(VertexHelper vh, Vector2 center, float size, Color color)
        {
            float h = size * 0.5f;
            int idx = vh.currentVertCount;
            vh.AddVert(center + new Vector2(-h, -h), color, Vector2.zero);
            vh.AddVert(center + new Vector2(-h, h), color, Vector2.zero);
            vh.AddVert(center + new Vector2(h, h), color, Vector2.zero);
            vh.AddVert(center + new Vector2(h, -h), color, Vector2.zero);
            vh.AddTriangle(idx, idx + 1, idx + 2);
            vh.AddTriangle(idx + 2, idx + 3, idx);
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            axisCount = Mathf.Max(3, axisCount);
            webRings = Mathf.Max(1, webRings);
            SetVerticesDirty();
        }
#endif
    }
}
