using UnityEngine;
using UnityEngine.UI;
using TaskbarHero.Client.Managers;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 기능 패널을 <b>가운데 전투 화면 옆에 나란히</b> 배치하고, 활성화될 때마다 작은 상태에서 제 크기로
    /// 커지는 등장 연출을 붙이는 공용 헬퍼.
    ///
    /// <para><b>왜 필요한가</b> — 패널이 화면 중앙에 뜨면 뒤의 전투 화면을 가린다. 좌/우 한쪽으로 붙이면
    /// 반대쪽에 게임 화면이 남아 전투를 계속 볼 수 있다. 배치 방향은 기능별로 정해 둔다 —
    /// <b>왼쪽</b>: 거래소·출석부·메일 / <b>오른쪽</b>: 스테이지·가방.</para>
    ///
    /// <para><b>기준은 화면 끝이 아니라 전투 화면 밴드 가장자리다</b>(<see cref="GameViewLayout.PanelGap"/>).
    /// 화면 끝에 붙이면 패널 폭이 제각각이라(거래소 1120 · 메일 880 · 스테이지 1040 · 가방 920)
    /// 전투 화면까지의 거리가 패널마다 크게 달라진다 — 좁은 메일은 멀찍이, 넓은 스테이지는 딱 붙어 보였다.
    /// 전투 화면 쪽 가장자리에 피벗을 두고 앵커를 밴드 경계에 두면, <b>폭과 무관하게 안쪽 간격이 항상 같다</b>
    /// (남는 폭은 바깥쪽 — 창 가장자리 쪽 투명 영역 — 으로 흡수된다).</para>
    ///
    /// <para>패널 내부 요소는 모두 패널 루트 기준으로 배치돼 있으므로, 루트의 앵커·피벗만 옮기면
    /// 내용 레이아웃은 그대로 따라온다(크기는 건드리지 않는다).</para>
    ///
    /// <para>연출 컴포넌트는 <see cref="SidePanelPop"/>(별도 파일 — MonoBehaviour는 파일명이
    /// 클래스명과 같아야 Unity가 스크립트 참조를 프리팹에 저장할 수 있다).</para>
    /// </summary>
    public static class SidePanel
    {
        /// <summary>패널을 붙일 화면 방향.</summary>
        public enum Side
        {
            Left,
            Right
        }

        /// <summary>전투 화면과 패널 사이의 기본 간격(캔버스 단위). 좌우 여백 폭도 이 값을 전제로 잡혀 있다.</summary>
        public const float DefaultGap = GameViewLayout.PanelGap;

        /// <summary>
        /// 패널 루트를 전투 화면 밴드의 지정한 <paramref name="side"/> 옆(수직 중앙)에 붙이고,
        /// 활성화 시 작은 상태에서 커지며 나타나는 연출(<see cref="SidePanelPop"/>)을 부착한다.
        /// </summary>
        /// <param name="root">패널 루트 RectTransform(크기가 지정된 패널 본체).</param>
        /// <param name="side">붙일 방향.</param>
        /// <param name="gap">전투 화면과 띄울 간격(캔버스 단위).</param>
        public static void Attach(RectTransform root, Side side, float gap = DefaultGap)
        {
            if (root == null)
            {
                return;
            }

            Dock(root, side, gap);

            var pop = root.GetComponent<SidePanelPop>();
            if (pop == null)
            {
                pop = root.gameObject.AddComponent<SidePanelPop>();
            }
            pop.Configure(side); // 표시할 때마다 이 방향을 기준으로 배치를 다시 잡는다
        }

        /// <summary>
        /// 자동 도킹 없이 <b>등장 연출만</b> 붙인다 — 자리를 스스로 정하거나 다른 창에 맞춰 잡는 창
        /// (큐브·스킬·룬)이 쓴다. 이미 붙어 있으면 설정만 맞춘다.
        /// <para>계층은 에디터 빌더가 프리팹에 구우므로 보통은 이미 붙어 있다. 런타임에 새로 붙는 경우
        /// (옛 프리팹) <see cref="SidePanelPop"/>의 <c>OnEnable</c>이 컴포넌트 추가 즉시 돌아 자리를 흔들 수
        /// 있으므로, 위치를 보존한 뒤 연출의 도착 위치를 다시 잡아 준다.</para>
        /// </summary>
        /// <param name="root">패널 루트 RectTransform(PanelRoot).</param>
        public static void AttachCentered(RectTransform root)
        {
            if (root == null)
            {
                return;
            }

            var pop = root.GetComponent<SidePanelPop>();
            if (pop != null)
            {
                pop.ConfigureCentered();
                return;
            }

            var keep = root.anchoredPosition;
            pop = root.gameObject.AddComponent<SidePanelPop>();
            pop.ConfigureCentered();
            root.anchoredPosition = keep;
            pop.SyncRestPosition();
        }

        /// <summary>
        /// 패널을 <b>창을 움직이지 않고</b> 화면에 보이는 공간에 배치한다(표시할 때마다 호출).
        /// <list type="number">
        /// <item>기본은 지정한 <paramref name="side"/> 도킹(전투 화면 옆, 간격 <see cref="DefaultGap"/>).</item>
        /// <item>그쪽 여백이 화면 밖으로 나가 있으면 <b>반대쪽</b>으로 연다(양쪽 다 부족하면 더 넓게 보이는 쪽).</item>
        /// <item>그래도 넘치면 보이는 영역 안으로 <b>밀어 넣는다</b>(가로·세로 모두).</item>
        /// </list>
        /// <para><b>왜 이렇게 하나</b> — 예전에는 패널을 열 때 창을 작업영역 안으로 끌어당겨(<c>TaskbarWindow.Apply</c>)
        /// 공간을 만들었고, 그 바람에 <b>전투 화면이 갑자기 움직여</b> 플레이가 끊겼다. 창은 사용자가 둔 자리에
        /// 그대로 두고 패널만 남는 공간으로 옮긴다.</para>
        /// <para>창이 화면 안에 온전히 들어와 있는 보통 상황에서는 1번에서 끝나므로 배치가 달라지지 않는다.</para>
        /// </summary>
        public static void Place(RectTransform root, Side side, float gap = DefaultGap)
        {
            if (root == null)
            {
                return;
            }
            Dock(root, side, gap);

            var canvas = root.GetComponentInParent<Canvas>();
            if (canvas == null || Screen.width <= 0)
            {
                return; // 캔버스 밖(에디터 빌드 중) — 기본 도킹으로 둔다
            }

            // 배치 계산은 캔버스 배율에 의존하므로 먼저 현재 씬 규격을 확정한다. 패널을 처음 열 때는
            // 프리팹에 구워진 배율(1)이 아직 남아 있어(CanvasScaler는 자기 Update에서야 고친다)
            // 이 호출이 없으면 첫 프레임 크기·위치가 모두 어긋난다.
            GameViewLayout.ApplyCurrentScaler(canvas.GetComponent<CanvasScaler>());
            if (canvas.scaleFactor <= 0f)
            {
                return;
            }

            float scale = canvas.scaleFactor;
            Rect visible = TaskbarWindow.VisibleArea;
            float widthPx = root.rect.width * scale;
            float heightPx = root.rect.height * scale;
            float gapPx = gap * scale;
            float bandLeftPx = GameViewLayout.GameAreaMinX * Screen.width;
            float bandRightPx = GameViewLayout.GameAreaMaxX * Screen.width;

            // 각 쪽으로 열었을 때 패널의 왼쪽 끝(스크린 픽셀)
            float leftDockX = bandLeftPx - gapPx - widthPx;
            float rightDockX = bandRightPx + gapPx;
            bool leftFits = leftDockX >= visible.xMin;
            bool rightFits = rightDockX + widthPx <= visible.xMax;

            // 반대쪽으로 여는 것은 <b>창이 화면 밖으로 걸쳐 있을 때만</b> 한다. 창이 온전히 보이는데도
            // 폭이 모자라는 경우(에디터 Game 뷰처럼 창 비율이 설계와 다를 때)까지 좌우를 바꾸면
            // 가방이 왼쪽에서 열리는 식으로 배치가 오락가락한다 — 그때는 아래 클램프만 적용한다.
            bool windowClipped = visible.xMin > 0.5f || visible.xMax < Screen.width - 0.5f;
            Side chosen = side;
            if (windowClipped)
            {
                if (!leftFits && !rightFits)
                {
                    // 양쪽 다 부족 — 더 넓게 보이는 쪽을 고른다(그 뒤 클램프가 화면 안으로 밀어 넣는다).
                    float leftRoom = (bandLeftPx - gapPx) - visible.xMin;
                    float rightRoom = visible.xMax - (bandRightPx + gapPx);
                    chosen = leftRoom >= rightRoom ? Side.Left : Side.Right;
                }
                else if (side == Side.Left && !leftFits)
                {
                    chosen = Side.Right;
                }
                else if (side == Side.Right && !rightFits)
                {
                    chosen = Side.Left;
                }
            }

            if (chosen != side)
            {
                Dock(root, chosen, gap);
            }

            // 보이는 영역 안으로 밀어 넣는다. 세로는 항상 화면 중앙에 놓이므로 창이 위아래로 걸쳐 있을 때만 움직인다.
            float startX = chosen == Side.Left ? leftDockX : rightDockX;
            float startY = (Screen.height - heightPx) * 0.5f;
            float dx = FitIntoSpan(startX, widthPx, visible.xMin, visible.xMax) - startX;
            float dy = FitIntoSpan(startY, heightPx, visible.yMin, visible.yMax) - startY;
            if (dx != 0f || dy != 0f)
            {
                root.anchoredPosition += new Vector2(dx, dy) / scale;
            }
        }

        /// <summary>[<paramref name="min"/>, <paramref name="max"/>] 구간 안에 들어가도록 시작점을 민다
        /// (구간보다 크면 시작점을 구간 앞에 맞춘다).</summary>
        private static float FitIntoSpan(float start, float size, float min, float max)
        {
            if (size >= max - min)
            {
                return min;
            }
            return Mathf.Clamp(start, min, max - size);
        }

        /// <summary>
        /// 패널 루트의 앵커·피벗·위치만 전투 화면 옆으로 옮긴다(연출 없이 배치만 필요할 때).
        /// <para>앵커 = 전투 화면 밴드 경계, 피벗 = 패널의 전투 화면 쪽 모서리이므로,
        /// 위치 값은 패널 폭과 무관하게 항상 <paramref name="gap"/>이다.</para>
        /// </summary>
        public static void Dock(RectTransform root, Side side, float gap = DefaultGap)
        {
            if (root == null)
            {
                return;
            }
            bool left = side == Side.Left;
            float anchorX = left ? GameViewLayout.GameAreaMinX : GameViewLayout.GameAreaMaxX;
            root.anchorMin = root.anchorMax = new Vector2(anchorX, 0.5f);
            root.pivot = new Vector2(left ? 1f : 0f, 0.5f); // 전투 화면 쪽 모서리
            root.anchoredPosition = new Vector2(left ? -gap : gap, 0f);
        }
    }
}
