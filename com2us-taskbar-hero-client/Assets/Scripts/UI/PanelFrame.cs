using UnityEngine;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 공용 창 배경 프레임(<c>Assets/Art/UI/ui_bg_2.png</c>)의 <b>테두리 안쪽 빈 칸</b> 규격.
    ///
    /// <para><b>왜 필요한가</b> — 이 아트는 9-slice 테두리 값이 없어 <see cref="UnityEngine.UI.Image.Type.Simple"/>로
    /// <b>늘려</b> 그린다. 그래서 나무 테두리와 상단 보석 장식판의 두께도 창 크기에 <b>비례</b>하며, 내용물을
    /// 창 크기 기준의 고정 여백으로 배치하면 창을 키울 때마다 테두리를 파고든다. 아래 비율로 내용 영역을
    /// 잡으면 창 크기와 무관하게 항상 테두리 안쪽에만 내용이 놓인다.</para>
    ///
    /// <para>값은 실제 스프라이트(<c>ui_bg_2_0</c> 크롭 671×938)에서 테두리 안쪽 평면 영역의 경계를 측정한 것이다.
    /// 상단(<see cref="InsetTop"/>)이 유독 두꺼운 이유는 보석이 박힌 장식판 때문이다.</para>
    ///
    /// <para><b>창 비율</b> — 프레임을 늘려 그리므로 창의 가로세로비가 <see cref="Aspect"/>에서 크게 벗어나면
    /// 장식이 찌그러진다. 새 창을 만들 때는 이 비율에 가깝게 크기를 잡는다.</para>
    /// </summary>
    public static class PanelFrame
    {
        /// <summary>왼쪽 테두리 두께(창 폭 대비 비율).</summary>
        public const float InsetLeft = 0.1371f;

        /// <summary>오른쪽 테두리 두께(창 폭 대비 비율).</summary>
        public const float InsetRight = 0.1282f;

        /// <summary>위쪽 테두리 두께(창 높이 대비 비율) — 보석 장식판을 포함한다.</summary>
        public const float InsetTop = 0.1594f;

        /// <summary>아래쪽 테두리 두께(창 높이 대비 비율).</summary>
        public const float InsetBottom = 0.0949f;

        /// <summary>테두리에 닿아 보이지 않도록 안쪽으로 더 들이는 여유(UI 단위).</summary>
        public const float Pad = 10f;

        /// <summary>프레임 아트의 가로세로비(671 / 938 ≒ 0.715). 창 크기를 이 비율에 가깝게 잡는다.</summary>
        public const float Aspect = 671f / 938f;

        /// <summary>
        /// 테두리 <b>안쪽</b> 빈 칸만 차지하는 내용 영역을 만들어 돌려준다. 앵커를 비율로 잡으므로
        /// 창 크기를 바꿔도 테두리 두께에 맞춰 함께 따라간다.
        /// <para>자식은 이 영역의 <b>좌상단 기준</b>(TopLeft 규약)이나 중앙 기준으로 배치하면 된다.</para>
        /// </summary>
        /// <param name="panel">창 본체(배경 프레임 이미지를 가진 RectTransform).</param>
        /// <param name="name">만들 오브젝트 이름.</param>
        public static RectTransform CreateContentArea(RectTransform panel, string name = "ContentArea")
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(panel, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(InsetLeft, InsetBottom);
            rt.anchorMax = new Vector2(1f - InsetRight, 1f - InsetTop);
            rt.offsetMin = new Vector2(Pad, Pad);
            rt.offsetMax = new Vector2(-Pad, -Pad);
            return rt;
        }
    }
}
