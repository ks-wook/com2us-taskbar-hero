using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TaskbarHero.Client.Managers;
using TaskbarHero.Client.MasterData;
using TaskbarHero.Common.Dto;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 출석부 보상 패널(attendance 기획서 §2·§5). 열릴 때마다 서버에서 이번달 출석 진행도를 조회해
    /// <b>1~30일차 보상 사다리</b>에 각 일차의 보상(공용 아이템 슬롯)과 수령 여부(체크 표시)를 그리고,
    /// '오늘 보상 받기'로 오늘자 보상을 획득한다(즉시 지급이 아니라 우편함으로 발송됨을 안내).
    /// <b>일차는 날짜(day-of-month)가 아니라 이번달 누적 출석 순번</b>이다 — 월중 첫 접속이어도 1일차부터
    /// 순서대로 받으며, 앞에서부터 채워진다. 수령 가능 여부는 서버가 판정한 <c>canClaim</c>을 따른다
    /// (30일차를 모두 받으면 1일차부터 순환하므로 사다리 소진으로 인한 실패는 없다).
    /// 오늘 처음 받는 순간에만 그 칸의 체크 표시가 크게 부풀었다가 원래 크기로 돌아오는 연출을 재생한다
    /// (<see cref="ItemSlotView.PlayClaimedPopAnimation"/>). 이미 수령한 과거 일자는 연출 없이 체크만 표시한다.
    /// 외형은 <c>Assets/Art/UI/Attendance</c>의 전용 아트를 쓴다 — 게시판 배경 <c>attendance_board</c>(상단 리본이
    /// 제목 역할), 사다리 칸 슬롯 프레임 <c>attendance_item_slot</c>, 수령 표시 <c>check</c>.
    /// 정적 계층(캔버스·패널·헤더·보상 사다리 30칸·버튼)은 에디터 빌더(AttendanceUiBuilder)가 프리팹에 굽고,
    /// 구워진 칸은 런타임에 <see cref="RebindCells"/>가 목록에 다시 연결한다.
    /// </summary>
    public class AttendancePanelController : MonoBehaviour
    {
        private const float CanvasRefWidth = 1080f;
        private const float CanvasRefHeight = 1920f;
        private const int TotalDays = 30; // 보상 사다리 길이(1~30일차, attendance_master)
        private const int Columns = 6;    // 6열 × 5행 = 30칸 — 사다리 길이와 정확히 맞아 빈 칸이 남지 않는다

        // 패널 크기는 게시판 아트(attendance_board 546x484)의 비율을 유지한다.
        private const float PanelWidth = 1000f;
        private const float PanelHeight = 886f;
        private const float BoardContentTop = 168f;   // 상단 리본(DAILY ATTENDANCE) + 오늘 날짜 줄 아래
        private const float CellWidth = 130f;
        private const float CellHeight = 98f;
        private const float CellSpacing = 8f;
        private const float SlotSize = 74f;

        [Header("UI 리소스 (에디터 빌더가 배선)")]
        [Tooltip("공용 아이템 슬롯 프리팹(Assets/Prefabs/UI/ItemSlot). 일차별 보상 표시에 사용하며, 없으면 단색 칸 폴백.")]
        [SerializeField] private GameObject _itemSlotPrefab;
        [Tooltip("출석부 게시판 배경(Assets/Art/UI/Attendance/attendance_board). 없으면 단색 패널.")]
        [SerializeField] private Sprite _boardSprite;
        [Tooltip("출석부 사다리 칸 슬롯 프레임(Assets/Art/UI/Attendance/attendance_item_slot). 없으면 공용 슬롯 프레임 유지.")]
        [SerializeField] private Sprite _slotFrameSprite;
        [Tooltip("받기 버튼 배경(Assets/Art/UI/pixel_rpg_button). 없으면 단색 버튼.")]
        [SerializeField] private Sprite _buttonSprite;

        [Header("구성 참조 (에디터 빌더가 배선)")]
        // 닫기(X) 버튼은 두지 않는다(미관상 제거) — 패널 닫기는 아래 딤(바깥 영역) 클릭이 담당한다.
        [SerializeField] private Button _dimButton;
        [SerializeField] private Button _claimButton;
        [SerializeField] private Text _claimButtonLabel;
        [SerializeField] private Text _todayText;
        [SerializeField] private Text _messageText;
        [SerializeField] private RectTransform _gridContent;

        private Font _font;
        private bool _busy;
        private int _todayDay;   // 오늘 해당하는 출석 일차(1~30). 이번 달 사다리를 다 채웠으면 0
        private bool _canClaim;  // 서버 판정 수령 가능 여부(오늘 미수령 && 남은 일차 있음)
        private readonly List<DayCell> _cells = new List<DayCell>();

        private bool AlreadyBuilt => _gridContent != null;

        /// <summary>보상 사다리 칸 1개(1~30일차). 정적 계층 생성 시 30칸을 미리 만들어두고, 조회 결과로 채운다.</summary>
        private class DayCell
        {
            public int day;
            public GameObject root;
            public Text dayLabel;
            public ItemSlotView slotView;
        }

        private void Awake()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (AlreadyBuilt)
            {
                RebindCells(); // 프리팹에 구워진 30칸을 런타임 목록에 다시 연결
            }
            else
            {
                Construct(); // 폴백(프리팹 미배선 시)
            }
            WireRuntime();
        }

        /// <summary>
        /// 프리팹에 구워진 사다리 칸(Day01~Day30)을 런타임 <see cref="_cells"/> 목록에 다시 연결한다.
        /// 칸 오브젝트는 프리팹에 <b>비활성</b>으로 저장되고 조회 결과가 있는 날만 켜지는데,
        /// 목록을 채우는 <see cref="BuildGrid"/>는 에디터 빌드(<see cref="Construct"/>) 때만 실행된다.
        /// 이 재연결이 없으면 런타임에 목록이 비어 <see cref="FindCell"/>이 항상 null을 돌려주고,
        /// 결과적으로 어떤 칸도 켜지지 않아 보상 목록이 하나도 보이지 않는다.
        /// </summary>
        private void RebindCells()
        {
            _cells.Clear();
            for (int i = 0; i < _gridContent.childCount; i++)
            {
                var cellGo = _gridContent.GetChild(i).gameObject;
                var dayLabel = cellGo.transform.Find("DayLabel");
                _cells.Add(new DayCell
                {
                    day = ParseDay(cellGo.name, i),
                    root = cellGo,
                    dayLabel = dayLabel != null ? dayLabel.GetComponent<Text>() : null,
                    slotView = cellGo.GetComponentInChildren<ItemSlotView>(true),
                });
            }
        }

        /// <summary>칸 오브젝트 이름("Day07")에서 일자를 얻는다. 이름이 규약과 다르면 자식 순서(1부터)를 쓴다.</summary>
        private static int ParseDay(string cellName, int index)
        {
            if (cellName != null && cellName.StartsWith("Day") &&
                int.TryParse(cellName.Substring(3), out int day))
            {
                return day;
            }
            return index + 1;
        }

        /// <summary>패널이 표시될 때마다 이번달 출석 현황을 새로 조회한다.</summary>
        private void OnEnable()
        {
            if (Application.isPlaying && AlreadyBuilt)
            {
                SetMessage(string.Empty);
                RequestStatus();
            }
        }

        /// <summary>에디터 빌드 전용: 전체 정적 계층(사다리 30칸 포함)을 생성해 프리팹에 굽는다.</summary>
        public void EditorConstruct() => Construct();

        // ── 정적 계층 구성 ──

        private void Construct()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildCanvas();
            BuildDim();
            var panel = BuildPanel();
            BuildHeader(panel);
            BuildGrid(panel);
            BuildFooter(panel);
        }

        /// <summary>패널 전용 오버레이 캔버스를 구성한다(다른 패널과 동일 규격, sortingOrder 100).</summary>
        private void BuildCanvas()
        {
            var canvas = gameObject.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = gameObject.AddComponent<Canvas>();
            }
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = gameObject.GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                scaler = gameObject.AddComponent<CanvasScaler>();
            }
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(CanvasRefWidth, CanvasRefHeight);
            scaler.matchWidthOrHeight = 0.5f;
            if (gameObject.GetComponent<GraphicRaycaster>() == null)
            {
                gameObject.AddComponent<GraphicRaycaster>();
            }
        }

        /// <summary>패널 밖 클릭 시 닫히는 투명 차단막.</summary>
        private void BuildDim()
        {
            var img = NewImage("Dim", (RectTransform)transform, new Color(0f, 0f, 0f, 0f));
            Stretch(img.rectTransform);
            _dimButton = img.gameObject.AddComponent<Button>();
            _dimButton.transition = Selectable.Transition.None;
        }

        /// <summary>출석부 패널 본체. 게시판 아트(attendance_board)를 원본 비율 그대로 배경으로 쓰고,
        /// 아트가 없으면 기존 단색 패널로 폴백한다.</summary>
        private RectTransform BuildPanel()
        {
            var img = NewImage("PanelRoot", (RectTransform)transform, new Color(0.10f, 0.12f, 0.18f, 0.98f));
            if (_boardSprite != null)
            {
                img.sprite = _boardSprite;
                img.type = Image.Type.Simple;
                img.color = Color.white;
            }
            var rt = img.rectTransform;
            rt.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            // 화면 중앙이 아니라 전투 화면 왼쪽 옆에 일정 간격(GameViewLayout.PanelGap)을 두고 붙인다 — 전투를 가리지 않는다.
            SidePanel.Attach(rt, SidePanel.Side.Left);
            return rt;
        }

        /// <summary>헤더: 오늘 날짜 줄. 제목은 게시판 아트 상단 리본("DAILY ATTENDANCE")이
        /// 대신하므로 별도 텍스트를 두지 않는다(아트가 없을 때만 제목 텍스트를 표시).</summary>
        private void BuildHeader(RectTransform panel)
        {
            if (_boardSprite == null)
            {
                var title = NewText("Title", panel, "출석부", 44, TextAnchor.MiddleCenter);
                title.fontStyle = FontStyle.Bold;
                var trt = title.rectTransform;
                trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 1f);
                trt.pivot = new Vector2(0.5f, 1f);
                trt.anchoredPosition = new Vector2(0f, -26f);
                trt.sizeDelta = new Vector2(400f, 56f);
            }

            // 오늘 날짜 줄 — 리본 아래 게시판 안쪽 상단. 게시판의 어두운 갈색 위라 밝은 미색으로 쓴다.
            _todayText = NewText("TodayText", panel, string.Empty, 26, TextAnchor.MiddleCenter);
            _todayText.color = new Color(1f, 0.93f, 0.78f, 0.9f);
            var ttrt = _todayText.rectTransform;
            ttrt.anchorMin = ttrt.anchorMax = new Vector2(0.5f, 1f);
            ttrt.pivot = new Vector2(0.5f, 1f);
            ttrt.anchoredPosition = new Vector2(0f, -112f);
            ttrt.sizeDelta = new Vector2(600f, 36f);

            // 닫기(X) 버튼은 두지 않는다(미관상 제거) — 게시판 아트 위에 얹히면 화면을 해쳐,
            // 닫기는 패널 바깥(딤) 클릭이 담당한다.
        }

        /// <summary>1~30일차 보상 사다리 그리드(6열 × 5행 = 30칸, 공용 아이템 슬롯을 미리 만들어둔다).</summary>
        private void BuildGrid(RectTransform panel)
        {
            int rows = Mathf.CeilToInt(TotalDays / (float)Columns);
            float gridW = Columns * CellWidth + (Columns - 1) * CellSpacing;
            float gridH = rows * CellHeight + (rows - 1) * CellSpacing;

            var gridGo = new GameObject("Grid", typeof(RectTransform));
            gridGo.transform.SetParent(panel, false);
            _gridContent = (RectTransform)gridGo.transform;
            _gridContent.anchorMin = _gridContent.anchorMax = new Vector2(0.5f, 1f);
            _gridContent.pivot = new Vector2(0.5f, 1f);
            _gridContent.sizeDelta = new Vector2(gridW, gridH);
            _gridContent.anchoredPosition = new Vector2(0f, -BoardContentTop); // 리본·오늘 날짜 줄 아래

            var layout = gridGo.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(CellWidth, CellHeight);
            layout.spacing = new Vector2(CellSpacing, CellSpacing);
            layout.startCorner = GridLayoutGroup.Corner.UpperLeft;
            layout.startAxis = GridLayoutGroup.Axis.Horizontal;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = Columns;

            _cells.Clear();
            for (int day = 1; day <= TotalDays; day++)
            {
                _cells.Add(BuildDayCell(day));
            }
        }

        /// <summary>사다리 칸 1개(일차 라벨 + 공용 아이템 슬롯). 조회 결과가 채워지기 전까지는 숨겨둔다
        /// (서버가 주지 않은 일차는 계속 숨김).</summary>
        private DayCell BuildDayCell(int day)
        {
            var cellGo = new GameObject($"Day{day:00}", typeof(RectTransform));
            cellGo.transform.SetParent(_gridContent, false); // 크기·위치는 GridLayoutGroup이 제어

            var dayLabel = NewText("DayLabel", cellGo.transform, DayLabelText(day), 20, TextAnchor.UpperCenter);
            dayLabel.fontStyle = FontStyle.Bold;
            var lrt = dayLabel.rectTransform;
            lrt.anchorMin = new Vector2(0f, 1f);
            lrt.anchorMax = new Vector2(1f, 1f);
            lrt.pivot = new Vector2(0.5f, 1f);
            lrt.anchoredPosition = Vector2.zero;
            lrt.sizeDelta = new Vector2(0f, 22f);

            GameObject slotGo;
            ItemSlotView slotView = null;
            if (_itemSlotPrefab != null)
            {
                slotGo = Instantiate(_itemSlotPrefab, cellGo.transform);
                slotView = slotGo.GetComponent<ItemSlotView>();
                if (slotView != null)
                {
                    slotView.SetFrameSprite(_slotFrameSprite); // 출석부 전용 슬롯 프레임
                }
            }
            else
            {
                slotGo = NewImage("Slot", cellGo.transform, new Color(0.12f, 0.14f, 0.22f, 0.95f)).gameObject;
            }
            var srt = (RectTransform)slotGo.transform;
            srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 0f);
            srt.pivot = new Vector2(0.5f, 0f);
            srt.anchoredPosition = Vector2.zero;
            srt.sizeDelta = new Vector2(SlotSize, SlotSize);

            cellGo.SetActive(false);
            return new DayCell { day = day, root = cellGo, dayLabel = dayLabel, slotView = slotView };
        }

        /// <summary>하단 '오늘 보상 받기' 버튼과 오류/안내 메시지 텍스트.</summary>
        private void BuildFooter(RectTransform panel)
        {
            var btn = NewImage("ClaimButton", panel, new Color(0.22f, 0.5f, 0.3f, 1f));
            ApplyButtonSprite(btn);
            var brt = btn.rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0f);
            brt.pivot = new Vector2(0.5f, 0f);
            brt.anchoredPosition = new Vector2(0f, 56f);   // 게시판 하단 나무 테두리 위
            brt.sizeDelta = new Vector2(340f, 80f);
            _claimButtonLabel = NewText("Label", btn.rectTransform, "오늘 보상 받기", 32, TextAnchor.MiddleCenter);
            _claimButtonLabel.fontStyle = FontStyle.Bold;
            Stretch(_claimButtonLabel.rectTransform);
            _claimButton = btn.gameObject.AddComponent<Button>();

            _messageText = NewText("Message", panel, string.Empty, 26, TextAnchor.MiddleCenter);
            _messageText.color = new Color(1f, 0.75f, 0.45f);
            var mrt = _messageText.rectTransform;
            mrt.anchorMin = mrt.anchorMax = new Vector2(0.5f, 0f);
            mrt.pivot = new Vector2(0.5f, 0f);
            mrt.anchoredPosition = new Vector2(0f, 148f);
            mrt.sizeDelta = new Vector2(780f, 32f);
        }

        private void WireRuntime()
        {
            // 가방·스킬 창처럼 배경의 빈 곳을 잡아 창을 끌어 옮길 수 있게 한다(출석 칸 클릭은 그대로).
            // 한 번 옮기면 그 자리를 기억하고, 열 때마다 하던 자동 도킹도 멈춘다(PanelDragMove 참고).
            PanelDragMove.Attach(transform.Find("PanelRoot") as RectTransform, "Attendance");

            if (_dimButton != null) _dimButton.onClick.AddListener(Close);
            if (_claimButton != null) _claimButton.onClick.AddListener(OnClaim);
        }

        // ── 현황 조회 ──

        /// <summary>이번달 출석 현황을 서버에서 조회한다(POST /api/game/attendance/status).</summary>
        private void RequestStatus()
        {
            if (NetworkManager.Instance == null || !Session.IsLoggedIn)
            {
                SetMessage("로그인이 필요합니다.");
                return;
            }
            var req = new AuthRequest { userId = Session.UserId, token = Session.Token };
            NetworkManager.Instance.PostToGame<AttendanceStatusResponse>("/api/game/attendance/status", req, resp =>
            {
                ApplyStatus(resp != null ? resp.data : null);
            }, OnStatusError);
        }

        /// <summary>조회 결과로 보상 사다리 칸(1~30일차)을 채운다(서버가 준 일차만 표시, 나머지는 숨김).</summary>
        private void ApplyStatus(AttendanceStatusResultData data)
        {
            foreach (var cell in _cells)
            {
                cell.root.SetActive(false);
            }

            if (data == null)
            {
                SetMessage("출석 정보를 불러오지 못했습니다.");
                UpdateClaimButton();
                return;
            }

            _todayDay = data.todayDay;
            _canClaim = data.canClaim;

            if (data.days != null)
            {
                foreach (var day in data.days)
                {
                    if (day == null) continue;
                    var cell = FindCell(day.day);
                    if (cell == null) continue;
                    cell.root.SetActive(true);
                    ApplyDay(cell, day);
                }
            }

            _todayText.text = FormatProgress(data);

            UpdateClaimButton();
            SetMessage(string.Empty);
        }

        /// <summary>상단 진행도 문구. 일차는 날짜가 아니라 누적 출석 순번이므로 "n일차"와 누적 진행도를 보여준다.
        /// 오늘 받을 수 있으면 그 일차를, 이미 받았으면 오늘 받은 일차를, 사다리를 다 채웠으면 완료 문구를 쓴다.</summary>
        private static string FormatProgress(AttendanceStatusResultData data)
        {
            string month = FormatYearMonth(data.yearMonth);
            string progress = $"출석 {data.attendedCount}/{TotalDays}일차";
            if (data.todayDay <= 0)
            {
                return $"{month} · {progress} · 이번 달 보상을 모두 받았습니다";
            }
            return data.todayClaimed
                ? $"{month} · {progress} · 오늘 {data.todayDay}일차 보상 수령 완료"
                : $"{month} · {progress} · 오늘 받을 보상: {data.todayDay}일차";
        }

        /// <summary>사다리 칸 1개에 그 일차의 보상(공용 아이템 슬롯)과 수령 여부(체크 표시)를 반영한다.
        /// 오늘 해당하는 일차는 라벨을 강조해 다음에 받을 칸을 알려준다.</summary>
        private void ApplyDay(DayCell cell, AttendanceDayDto day)
        {
            if (cell.slotView != null)
            {
                if (day.rewardType == 1) // 골드
                {
                    cell.slotView.Setup(1, day.quantity, $"+{day.quantity:N0}", false);
                }
                else
                {
                    cell.slotView.Setup(day.rewardCode, day.quantity);
                }
                cell.slotView.SetClaimed(day.claimed); // 조회 새로고침은 연출 없이 즉시 반영
            }

            bool isToday = day.day == _todayDay;
            cell.dayLabel.text = DayLabelText(day.day);
            cell.dayLabel.color = isToday ? new Color(1f, 0.82f, 0.30f) : new Color(0.96f, 0.90f, 0.78f);
        }

        /// <summary>칸 라벨 문구. 날짜가 아니라 출석 순번임을 드러내기 위해 "n일차"로 쓴다.</summary>
        private static string DayLabelText(int day) => $"{day}일차";

        private DayCell FindCell(int day)
        {
            foreach (var cell in _cells)
            {
                if (cell.day == day)
                {
                    return cell;
                }
            }
            return null;
        }

        /// <summary>수령 버튼 상태를 갱신한다. 활성 조건은 서버 판정값 <c>canClaim</c>(오늘 미수령 &amp;&amp; 남은 일차 있음)이며,
        /// 비활성 사유(오늘 수령 완료 / 이번 달 사다리 소진)에 따라 라벨을 달리 표시한다.</summary>
        private void UpdateClaimButton()
        {
            if (_claimButton != null)
            {
                _claimButton.interactable = _canClaim && !_busy;
            }
            if (_claimButtonLabel != null)
            {
                if (_canClaim)
                {
                    _claimButtonLabel.text = "오늘 보상 받기";
                }
                else
                {
                    _claimButtonLabel.text = _todayDay <= 0 ? "이번 달 보상 모두 수령" : "오늘 보상 수령 완료";
                }
            }
        }

        private static string FormatYearMonth(int yearMonth)
        {
            string s = yearMonth.ToString();
            return s.Length == 6 ? $"{s.Substring(0, 4)}년 {int.Parse(s.Substring(4, 2))}월" : s;
        }

        // ── 오늘자 보상 획득 ──

        /// <summary>오늘자 출석 보상을 획득한다(POST /api/game/attendance/claim). 오늘 칸의 체크 표시에 획득
        /// 연출을 재생하고, <b>연출이 끝난 뒤</b> 안내 모달을 띄운다(모달이 연출을 가리지 않도록 하는 순서다).
        /// 보상은 즉시 지급이 아니라 우편함으로 발송되므로 모달로 그 사실을 알린다.</summary>
        private void OnClaim()
        {
            if (_busy || !_canClaim || NetworkManager.Instance == null || !Session.IsLoggedIn)
            {
                return;
            }
            _busy = true;
            UpdateClaimButton();

            var req = new AuthRequest { userId = Session.UserId, token = Session.Token };
            NetworkManager.Instance.PostToGame<AttendanceClaimResponse>("/api/game/attendance/claim", req, resp =>
            {
                _busy = false;
                _canClaim = false; // 오늘은 더 받을 수 없다(다음 조회에서 서버 판정으로 갱신)
                var data = resp != null ? resp.data : null;
                Debug.Log($"[Attendance] 출석 보상 획득 완료 day={(data != null ? data.day : 0)} mailId={(data != null ? data.mailId : 0)}");
                SoundManager.Sfx(SoundId.RewardClaim); // 출석 도장 수령음(사운드 정의서 §6)

                UpdateClaimButton();
                MailNotifier.Refresh(); // 보상이 메일로 발급됐으므로 미수령 메일 레드닷을 즉시 갱신

                // 오늘 칸에 획득 연출(크게 확대→원래 크기)을 재생하고, 끝난 뒤에 안내 모달을 띄운다.
                var cell = data != null ? FindCell(data.day) : null;
                if (cell != null && cell.slotView != null)
                {
                    cell.root.SetActive(true);
                    cell.slotView.PlayClaimedPopAnimation(() => ShowClaimedModal(data));
                }
                else
                {
                    if (cell != null) cell.root.SetActive(true);
                    ShowClaimedModal(data); // 연출할 슬롯이 없으면 곧바로 안내
                }
            }, OnClaimError);
        }

        /// <summary>출석 보상이 메일로 발송됐음을 모달로 안내한다(즉시 지급이 아님을 명확히 알림).
        /// 받은 일차는 날짜가 아니라 누적 출석 순번이므로 "n일차"로 표기한다.</summary>
        private static void ShowClaimedModal(AttendanceClaimResultData data)
        {
            string body = data != null
                ? $"{data.day}일차 출석 보상이 우편함으로 발송되었습니다.\n{RewardSummary(data.reward)}\n우편함에서 수령해야 계정에 반영됩니다."
                : "출석 보상이 우편함으로 발송되었습니다.";
            ModalManager.Instance?.ShowConfirm("출석 체크 완료", body);
        }

        /// <summary>보상 요약 문자열("골드 +1,000" 또는 "강철 투구 x1"). 골드는 노란색 강조 규칙 적용.</summary>
        private static string RewardSummary(AttendanceRewardDto reward)
        {
            if (reward == null)
            {
                return string.Empty;
            }
            return reward.rewardType == 1
                ? $"골드 {GoldFormat.Highlight(reward.quantity)}"
                : $"{ItemName(reward.rewardCode)} x{reward.quantity}";
        }

        /// <summary>아이템 코드의 표시 이름(마스터 데이터, 없으면 "아이템 {code}").</summary>
        private static string ItemName(int itemCode)
        {
            var db = MasterDataManager.Db;
            return db != null && db.Items.TryGetValue(itemCode, out var im) ? im.name : $"아이템 {itemCode}";
        }

        // ── 오류 처리 ──

        private void OnStatusError(NetworkError error)
        {
            Debug.LogWarning($"[Attendance] 현황 조회 실패: {error}");
            SetMessage(ErrorMessages.ToKorean(error));
        }

        /// <summary>획득 실패: 메시지를 표시하고, 상태 어긋남(이미 수령 등) 복구를 위해
        /// 서버 응답이 있는 오류일 때만 현황을 1회 재조회한다(연결 실패는 재조회 안 함).</summary>
        private void OnClaimError(NetworkError error)
        {
            _busy = false;
            Debug.LogWarning($"[Attendance] 획득 실패: {error}");
            SetMessage(ErrorMessages.ToKorean(error));
            UpdateClaimButton();
            if (!error.IsTransportError)
            {
                RequestStatus();
            }
        }

        private void SetMessage(string message)
        {
            if (_messageText != null)
            {
                _messageText.text = message ?? string.Empty;
            }
        }

        /// <summary>패널을 닫는다(UIManager 우선).</summary>
        public void Close()
        {
            if (UIManager.Instance != null)
            {
                UIManager.Instance.Hide(UIManager.PanelType.Attendance);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }

        // ── UI 헬퍼 ──

        /// <summary>버튼 이미지에 공용 버튼 스프라이트(pixel_rpg_button)를 슬라이스로 적용한다.
        /// 스프라이트 미배선 시 단색 폴백을 유지한다.</summary>
        private void ApplyButtonSprite(Image img)
        {
            if (_buttonSprite == null) return;
            img.sprite = _buttonSprite;
            img.type = Image.Type.Sliced;
            img.color = Color.white;
        }

        private static Image NewImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            return img;
        }

        private Text NewText(string name, Transform parent, string content, int size, TextAnchor anchor)
        {
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = _font;
            t.text = content;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = Color.white;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
