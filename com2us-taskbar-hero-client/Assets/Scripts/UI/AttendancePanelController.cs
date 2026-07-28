using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TaskbarHero.Client.Managers;
using TaskbarHero.Client.MasterData;
using TaskbarHero.Common.Dto;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 출석부 보상 패널(attendance 기획서 §2·§5). 열릴 때마다 서버에서 이번달 출석 현황을 조회해
    /// 1~31일 달력 칸에 그날의 보상(공용 아이템 슬롯)과 수령 여부(체크 표시)를 그리고,
    /// '오늘 보상 받기'로 오늘자 보상을 획득한다(즉시 지급이 아니라 우편함으로 발송됨을 안내).
    /// 오늘 처음 받는 순간에만 그 칸의 체크 표시가 작아졌다가 원래 크기로 돌아오는 연출을 재생한다
    /// (<see cref="ItemSlotView.PlayClaimedPopAnimation"/>). 이미 수령한 과거 일자는 연출 없이 체크만 표시한다.
    /// 정적 계층(캔버스·패널·헤더·달력 그리드 31칸·버튼)은 에디터 빌더(AttendanceUiBuilder)가 프리팹에 굽는다.
    /// </summary>
    public class AttendancePanelController : MonoBehaviour
    {
        private const float CanvasRefWidth = 1080f;
        private const float CanvasRefHeight = 1920f;
        private const int TotalDays = 31;
        private const int Columns = 7;
        private const float CellWidth = 108f;
        private const float CellHeight = 126f;
        private const float CellSpacing = 10f;

        [Header("UI 리소스 (에디터 빌더가 배선)")]
        [Tooltip("공용 아이템 슬롯 프리팹(Assets/Prefabs/UI/ItemSlot). 일자별 보상 표시에 사용하며, 없으면 단색 칸 폴백.")]
        [SerializeField] private GameObject _itemSlotPrefab;
        [Tooltip("받기 버튼 배경(Assets/Art/UI/pixel_rpg_button). 없으면 단색 버튼.")]
        [SerializeField] private Sprite _buttonSprite;

        [Header("구성 참조 (에디터 빌더가 배선)")]
        [SerializeField] private Button _closeButton;
        [SerializeField] private Button _dimButton;
        [SerializeField] private Button _claimButton;
        [SerializeField] private Text _claimButtonLabel;
        [SerializeField] private Text _todayText;
        [SerializeField] private Text _messageText;
        [SerializeField] private RectTransform _gridContent;

        private Font _font;
        private bool _busy;
        private int _todayDay;
        private bool _todayClaimed;
        private readonly List<DayCell> _cells = new List<DayCell>();

        private bool AlreadyBuilt => _gridContent != null;

        /// <summary>달력 칸 1개(1~31일). 정적 계층 생성 시 31칸을 미리 만들어두고, 조회 결과로 채운다.</summary>
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
            if (!AlreadyBuilt)
            {
                Construct(); // 폴백(프리팹 미배선 시)
            }
            WireRuntime();
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

        /// <summary>에디터 빌드 전용: 전체 정적 계층(달력 31칸 포함)을 생성해 프리팹에 굽는다.</summary>
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

        /// <summary>출석부 패널 본체(전용 배경 아트가 없어 단색 패널).</summary>
        private RectTransform BuildPanel()
        {
            var img = NewImage("PanelRoot", (RectTransform)transform, new Color(0.10f, 0.12f, 0.18f, 0.98f));
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(900f, 1040f);
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        private void BuildHeader(RectTransform panel)
        {
            var title = NewText("Title", panel, "출석부", 44, TextAnchor.MiddleCenter);
            title.fontStyle = FontStyle.Bold;
            var trt = title.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.anchoredPosition = new Vector2(0f, -26f);
            trt.sizeDelta = new Vector2(400f, 56f);

            _todayText = NewText("TodayText", panel, string.Empty, 26, TextAnchor.MiddleCenter);
            _todayText.color = new Color(1f, 1f, 1f, 0.75f);
            var ttrt = _todayText.rectTransform;
            ttrt.anchorMin = ttrt.anchorMax = new Vector2(0.5f, 1f);
            ttrt.pivot = new Vector2(0.5f, 1f);
            ttrt.anchoredPosition = new Vector2(0f, -80f);
            ttrt.sizeDelta = new Vector2(700f, 36f);

            var close = NewImage("CloseButton", panel, new Color(0.25f, 0.28f, 0.4f, 1f));
            var crt = close.rectTransform;
            crt.anchorMin = crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(1f, 1f);
            crt.anchoredPosition = new Vector2(-24f, -24f);
            crt.sizeDelta = new Vector2(60f, 60f);
            var xt = NewText("X", close.rectTransform, "X", 32, TextAnchor.MiddleCenter);
            Stretch(xt.rectTransform);
            _closeButton = close.gameObject.AddComponent<Button>();
        }

        /// <summary>1~31일 달력 그리드(7열 고정, 공용 아이템 슬롯 31칸을 미리 만들어둔다).</summary>
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
            _gridContent.anchoredPosition = new Vector2(0f, -190f); // 헤더 아래

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

        /// <summary>달력 칸 1개(일자 라벨 + 공용 아이템 슬롯). 조회 결과가 채워지기 전까지는 숨겨둔다
        /// (마스터에 정의되지 않은 여분 일자 대비 — 이달에 없는 날은 계속 숨김).</summary>
        private DayCell BuildDayCell(int day)
        {
            var cellGo = new GameObject($"Day{day:00}", typeof(RectTransform));
            cellGo.transform.SetParent(_gridContent, false); // 크기·위치는 GridLayoutGroup이 제어

            var dayLabel = NewText("DayLabel", cellGo.transform, day.ToString(), 22, TextAnchor.UpperCenter);
            dayLabel.fontStyle = FontStyle.Bold;
            var lrt = dayLabel.rectTransform;
            lrt.anchorMin = new Vector2(0f, 1f);
            lrt.anchorMax = new Vector2(1f, 1f);
            lrt.pivot = new Vector2(0.5f, 1f);
            lrt.anchoredPosition = Vector2.zero;
            lrt.sizeDelta = new Vector2(0f, 26f);

            GameObject slotGo;
            ItemSlotView slotView = null;
            if (_itemSlotPrefab != null)
            {
                slotGo = Instantiate(_itemSlotPrefab, cellGo.transform);
                slotView = slotGo.GetComponent<ItemSlotView>();
            }
            else
            {
                slotGo = NewImage("Slot", cellGo.transform, new Color(0.12f, 0.14f, 0.22f, 0.95f)).gameObject;
            }
            var srt = (RectTransform)slotGo.transform;
            srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 0f);
            srt.pivot = new Vector2(0.5f, 0f);
            srt.anchoredPosition = Vector2.zero;
            srt.sizeDelta = new Vector2(96f, 96f);

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
            brt.anchoredPosition = new Vector2(0f, 44f);
            brt.sizeDelta = new Vector2(360f, 92f);
            _claimButtonLabel = NewText("Label", btn.rectTransform, "오늘 보상 받기", 32, TextAnchor.MiddleCenter);
            _claimButtonLabel.fontStyle = FontStyle.Bold;
            Stretch(_claimButtonLabel.rectTransform);
            _claimButton = btn.gameObject.AddComponent<Button>();

            _messageText = NewText("Message", panel, string.Empty, 26, TextAnchor.MiddleCenter);
            _messageText.color = new Color(1f, 0.75f, 0.45f);
            var mrt = _messageText.rectTransform;
            mrt.anchorMin = mrt.anchorMax = new Vector2(0.5f, 0f);
            mrt.pivot = new Vector2(0.5f, 0f);
            mrt.anchoredPosition = new Vector2(0f, 146f);
            mrt.sizeDelta = new Vector2(780f, 36f);
        }

        private void WireRuntime()
        {
            if (_closeButton != null) _closeButton.onClick.AddListener(Close);
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

        /// <summary>조회 결과로 31개 달력 칸을 채운다(정의된 일자만 표시, 나머지는 숨김).</summary>
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
            _todayClaimed = data.todayClaimed;

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

            _todayText.text = FormatYearMonth(data.yearMonth) + $" · 오늘 {data.todayDay}일" +
                (data.todayClaimed ? " (오늘 보상 수령 완료)" : string.Empty);

            UpdateClaimButton();
            SetMessage(string.Empty);
        }

        /// <summary>달력 칸 1개에 그날의 보상(공용 아이템 슬롯)과 수령 여부(체크 표시)를 반영한다.</summary>
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
            cell.dayLabel.text = day.day.ToString();
            cell.dayLabel.color = isToday ? new Color(1f, 0.85f, 0.35f) : Color.white;
        }

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

        private void UpdateClaimButton()
        {
            if (_claimButton != null)
            {
                _claimButton.interactable = !_todayClaimed && !_busy;
            }
            if (_claimButtonLabel != null)
            {
                _claimButtonLabel.text = _todayClaimed ? "오늘 보상 수령 완료" : "오늘 보상 받기";
            }
        }

        private static string FormatYearMonth(int yearMonth)
        {
            string s = yearMonth.ToString();
            return s.Length == 6 ? $"{s.Substring(0, 4)}년 {int.Parse(s.Substring(4, 2))}월" : s;
        }

        // ── 오늘자 보상 획득 ──

        /// <summary>오늘자 출석 보상을 획득한다(POST /api/game/attendance/claim). 즉시 지급이 아니라
        /// 우편함으로 발송되므로 안내 모달로 알리고, 오늘 칸의 체크 표시에 획득 연출을 재생한다.</summary>
        private void OnClaim()
        {
            if (_busy || _todayClaimed || NetworkManager.Instance == null || !Session.IsLoggedIn)
            {
                return;
            }
            _busy = true;
            UpdateClaimButton();

            var req = new AuthRequest { userId = Session.UserId, token = Session.Token };
            NetworkManager.Instance.PostToGame<AttendanceClaimResponse>("/api/game/attendance/claim", req, resp =>
            {
                _busy = false;
                _todayClaimed = true;
                var data = resp != null ? resp.data : null;
                Debug.Log($"[Attendance] 출석 보상 획득 완료 day={(data != null ? data.day : 0)} mailId={(data != null ? data.mailId : 0)}");

                var cell = data != null ? FindCell(data.day) : null;
                if (cell != null)
                {
                    cell.root.SetActive(true);
                    if (cell.slotView != null)
                    {
                        cell.slotView.PlayClaimedPopAnimation(); // 방금 획득: 축소→확대 연출
                    }
                }

                UpdateClaimButton();
                ShowClaimedModal(data);
                MailNotifier.Refresh(); // 보상이 메일로 발급됐으므로 미수령 메일 레드닷을 즉시 갱신
            }, OnClaimError);
        }

        /// <summary>출석 보상이 메일로 발송됐음을 모달로 안내한다(즉시 지급이 아님을 명확히 알림).</summary>
        private static void ShowClaimedModal(AttendanceClaimResultData data)
        {
            string body = data != null
                ? $"오늘의 출석 보상이 우편함으로 발송되었습니다.\n{RewardSummary(data.reward)}\n우편함에서 수령해야 계정에 반영됩니다."
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
