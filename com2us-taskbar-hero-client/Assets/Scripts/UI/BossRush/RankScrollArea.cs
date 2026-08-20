using UnityEngine;
using UnityEngine.EventSystems;

namespace TaskbarHero.Client.UI.BossRush
{
    /// <summary>
    /// 랭킹 목록 위에서 <b>마우스 휠·드래그를 "몇 픽셀 이동"으로 바꿔</b> 알려 주는 입력 영역.
    /// <para>목록은 받아 둔 페이지 안에서 <b>픽셀 단위로 움직인다</b> — 그래서 이동량을 행 단위로 끊지 않고
    /// 픽셀(캔버스 단위)로 넘긴다. 행 단위로 끊으면 한 칸씩 튀어 보이고, 부분적으로 걸친 행을 표현할 수 없다.</para>
    /// <para>휠과 드래그를 <b>따로</b> 알린다 — 휠은 한 칸이 뚝 떨어지는 입력이라 컨트롤러가 목표 위치를 잡고
    /// 부드럽게 따라가게 하고, 드래그는 손을 따라 즉시 움직여야 하기 때문이다.</para>
    /// <para>일반적인 <c>ScrollRect</c>(내용 전체를 만들어 두고 잘라 보여 주는 방식)를 쓰지 않는 이유는 목록이
    /// <b>서버에서 한 페이지씩</b> 오기 때문이다(서버 기획서 5.4) — 전체 행을 만들어 둘 수 없다.</para>
    /// <para>계층은 에디터 빌더가 프리팹에 굽고(그래서 파일명 = 클래스명), 콜백은 실행마다
    /// <see cref="BossRushPanelController"/>가 다시 연결한다.</para>
    /// </summary>
    public class RankScrollArea : MonoBehaviour, IScrollHandler, IBeginDragHandler, IDragHandler
    {
        /// <summary>휠 한 칸에 이동할 거리(캔버스 단위). 컨트롤러가 행 높이 기준으로 넣는다.</summary>
        private float _wheelStep = 174f;

        /// <summary>휠 한 칸 이동량(+가 아래 = 순위가 낮은 쪽)을 받는 콜백. 뚝 떨어지는 입력이라
        /// 컨트롤러가 <b>목표 위치</b>로 삼아 부드럽게 따라간다.</summary>
        public System.Action<float> OnWheel;

        /// <summary>드래그 이동량(+가 아래 = 순위가 낮은 쪽)을 받는 콜백. 손을 따라 <b>즉시</b> 움직인다.</summary>
        public System.Action<float> OnDragMove;

        /// <summary>휠 한 칸의 이동 거리(캔버스 단위)를 설정한다.</summary>
        public void Configure(float wheelStep)
        {
            _wheelStep = Mathf.Max(1f, wheelStep);
        }

        /// <summary>휠: 한 칸에 <see cref="_wheelStep"/>만큼 이동한다(위로 굴리면 상위 순위 쪽).</summary>
        public void OnScroll(PointerEventData eventData)
        {
            float delta = eventData.scrollDelta.y;
            if (Mathf.Approximately(delta, 0f))
            {
                return;
            }
            // 플랫폼에 따라 한 칸이 ±1이 아닐 수 있어 부호만 쓴다(값 크기에 좌우되지 않게).
            OnWheel?.Invoke(-Mathf.Sign(delta) * _wheelStep);
        }

        /// <summary>드래그 시작: 이 컴포넌트가 드래그를 받도록 이벤트를 붙잡는다(누적 상태는 없다).</summary>
        public void OnBeginDrag(PointerEventData eventData)
        {
        }

        /// <summary>
        /// 드래그: <b>스크롤 위치를 끄는</b> 방식이다 — 위로 끌면 위쪽(상위 순위)으로, 아래로 끌면 아래쪽으로
        /// 이동한다. 내용을 잡아 끄는 방향(아래로 끌면 상위 순위가 내려오는 쪽)과 반대이며,
        /// <b>휠 방향과 일치</b>시킨 것이다(사용자 확정).
        /// </summary>
        public void OnDrag(PointerEventData eventData)
        {
            var canvas = GetComponentInParent<Canvas>();
            float scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
            float move = eventData.delta.y / scale; // 화면 픽셀 → 캔버스 단위
            if (Mathf.Approximately(move, 0f))
            {
                return;
            }
            OnDragMove?.Invoke(move);
        }
    }
}
