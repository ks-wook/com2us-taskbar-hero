using UnityEngine;
using UnityEngine.EventSystems;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 패널 창을 <b>마우스로 끌어 옮길 수 있게</b> 하는 공용 컴포넌트. 패널 루트(<c>PanelRoot</c>)에 붙인다.
    ///
    /// <para><b>어디를 잡아야 옮겨지나</b> — 이 컴포넌트는 패널 <b>배경</b>에 붙으므로, 배경의 빈 곳을 끌면
    /// 창이 움직인다. 아이템 칸·버튼·스크롤 영역처럼 자체 드래그 핸들러를 가진 자식 위에서는 uGUI가
    /// <b>더 깊은 쪽 핸들러를 먼저</b> 잡으므로 그쪽 동작(아이템 드래그·스크롤)이 그대로 유지된다.</para>
    ///
    /// <para><b>옮긴 자리는 유지된다</b> — 패널을 닫았다 열면 <see cref="SidePanelPop"/>이 표시할 때마다
    /// 배치를 다시 잡는데(<see cref="SidePanel.Place"/>), 그러면 사용자가 옮긴 자리가 초기화된다.
    /// 그래서 한 번이라도 끌어 옮기면 <see cref="SidePanelPop.MarkUserPlaced"/>로 알려 자동 배치를 멈춘다.</para>
    ///
    /// <para>창이 화면 밖으로 완전히 나가 다시 잡을 수 없게 되지 않도록, 놓을 때마다 최소한
    /// <see cref="MinVisible"/>만큼은 화면 안에 남도록 밀어 넣는다.</para>
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class PanelDragMove : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        /// <summary>화면 안에 반드시 남겨 둘 최소 크기(스크린 픽셀). 이만큼은 보여야 다시 잡을 수 있다.</summary>
        private const float MinVisible = 80f;

        /// <summary>저장 키 접두사(<see cref="PlayerPrefs"/>).</summary>
        private const string PrefsPrefix = "PanelPos_";

        /// <summary>
        /// 저장 식별자. 창마다 달라야 한다(가방·큐브…). 비어 있으면 위치를 기억하지 않는다.
        /// 계층은 에디터 빌더가 프리팹에 구워 두므로 직렬화해야 실행 시에도 남는다.
        /// </summary>
        [SerializeField] private string prefsKey;

        private RectTransform _rect;
        private Canvas _canvas;
        private SidePanelPop _pop;
        private Vector2 _grabOffset;   // 창 기준점과 커서 사이의 거리(잡은 지점을 유지하며 따라오게)
        private bool _dragging;

        private RectTransform Rect => _rect != null ? _rect : (_rect = (RectTransform)transform);

        private void Awake()
        {
            _rect = (RectTransform)transform;
            _pop = GetComponent<SidePanelPop>();
        }

        /// <summary>창을 다시 열 때마다 마지막으로 둔 자리로 되돌린다.</summary>
        private void OnEnable()
        {
            RestoreSavedPosition();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            _canvas = GetComponentInParent<Canvas>();
            if (_canvas == null)
            {
                return;
            }
            _dragging = true;

            // 잡은 지점이 창 안에서 그대로 유지되도록 커서와 창 위치의 차이를 기억한다.
            _grabOffset = (Vector2)Rect.position - eventData.position;

            // 사용자가 자리를 정했으므로 이후 자동 배치(열 때마다 옆으로 도킹)를 멈춘다.
            if (_pop != null)
            {
                _pop.MarkUserPlaced();
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_dragging)
            {
                return;
            }
            Rect.position = eventData.position + _grabOffset;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!_dragging)
            {
                return;
            }
            _dragging = false;
            ClampIntoScreen();
            SavePosition();
        }

        // ── 위치 기억 ──

        /// <summary>
        /// 창의 현재 자리를 <b>화면 크기 대비 비율</b>로 저장한다. 픽셀로 저장하면 창 크기나 해상도가 달라졌을 때
        /// 엉뚱한 곳에 뜨므로 비율로 둔다. 앵커·피벗이 창마다 달라도 <c>position</c>(월드=스크린 좌표) 기준이라
        /// 그대로 복원된다.
        /// </summary>
        private void SavePosition()
        {
            if (string.IsNullOrEmpty(prefsKey) || Screen.width <= 0 || Screen.height <= 0)
            {
                return;
            }
            Vector3 p = Rect.position;
            PlayerPrefs.SetFloat(PrefsPrefix + prefsKey + "_x", p.x / Screen.width);
            PlayerPrefs.SetFloat(PrefsPrefix + prefsKey + "_y", p.y / Screen.height);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// 저장해 둔 자리로 되돌린다(저장값이 없으면 아무것도 하지 않는다).
        /// <para>복원했으면 <see cref="SidePanelPop"/>에 알려 ① 표시할 때마다 하던 자동 도킹을 멈추고
        /// ② 등장 연출의 도착 위치를 <b>복원한 자리</b>로 갱신하게 한다. 그러지 않으면 연출이 끝나면서
        /// 옛 위치로 되돌아간다.</para>
        /// </summary>
        private void RestoreSavedPosition()
        {
            if (string.IsNullOrEmpty(prefsKey))
            {
                return;
            }
            string kx = PrefsPrefix + prefsKey + "_x";
            string ky = PrefsPrefix + prefsKey + "_y";
            if (!PlayerPrefs.HasKey(kx) || !PlayerPrefs.HasKey(ky))
            {
                return;
            }

            Rect.position = new Vector3(
                PlayerPrefs.GetFloat(kx) * Screen.width,
                PlayerPrefs.GetFloat(ky) * Screen.height,
                Rect.position.z);

            _canvas = _canvas != null ? _canvas : GetComponentInParent<Canvas>();
            ClampIntoScreen(); // 저장 당시보다 화면이 작아졌을 수 있다

            if (_pop != null)
            {
                _pop.MarkUserPlaced();
                _pop.SyncRestPosition();
            }
        }

        /// <summary>창의 일부(<see cref="MinVisible"/>)가 반드시 화면 안에 남도록 위치를 민다.</summary>
        private void ClampIntoScreen()
        {
            if (_canvas == null)
            {
                return;
            }
            float scale = _canvas.scaleFactor;
            if (scale <= 0f)
            {
                return;
            }

            var corners = new Vector3[4]; // 0=좌하 1=좌상 2=우상 3=우하
            Rect.GetWorldCorners(corners);
            float left = corners[0].x;
            float right = corners[2].x;
            float bottom = corners[0].y;
            float top = corners[2].y;

            float dx = 0f;
            if (right < MinVisible) dx = MinVisible - right;
            else if (left > Screen.width - MinVisible) dx = (Screen.width - MinVisible) - left;

            float dy = 0f;
            if (top < MinVisible) dy = MinVisible - top;
            else if (bottom > Screen.height - MinVisible) dy = (Screen.height - MinVisible) - bottom;

            if (dx != 0f || dy != 0f)
            {
                Rect.anchoredPosition += new Vector2(dx, dy) / scale;
            }
        }

        /// <summary>
        /// 패널 루트에 드래그 이동을 붙인다(이미 있으면 저장 키만 맞춘다).
        /// 컨트롤러들이 <c>WireRuntime</c>에서 한 줄로 부를 수 있게 정적 헬퍼로 둔다 —
        /// 프리팹이 이 컴포넌트 없이 구워져 있어도 실행 시 붙는다.
        /// </summary>
        /// <param name="panelRoot">창 본체(PanelRoot).</param>
        /// <param name="key">위치 저장 식별자(창마다 고유). 비우면 위치를 기억하지 않는다.</param>
        public static void Attach(RectTransform panelRoot, string key)
        {
            if (panelRoot == null)
            {
                return;
            }
            var move = panelRoot.GetComponent<PanelDragMove>();
            if (move == null)
            {
                move = panelRoot.gameObject.AddComponent<PanelDragMove>();
                move.prefsKey = key;
                // AddComponent는 OnEnable을 이 시점에 이미 지나쳤을 수 있으므로 복원을 직접 부른다.
                move.RestoreSavedPosition();
                return;
            }
            move.prefsKey = key;
        }
    }
}
