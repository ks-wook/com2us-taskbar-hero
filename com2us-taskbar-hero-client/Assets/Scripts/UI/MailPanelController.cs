using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TaskbarHero.Client.Battle;
using TaskbarHero.Client.Managers;
using TaskbarHero.Client.MasterData;
using TaskbarHero.Common.Dto;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 우편함(메일) 패널(mail 기획서 §2·§5). 열릴 때마다 서버에서 메일 목록을 조회해
    /// (조회 = 서버가 읽음 처리) 메일별 봉투 아이콘(미열람/열람)·제목·본문·첨부 요약·만료를 표시하고,
    /// '받기'(단건 수령)·'모두 받기'(일괄 수령)를 제공한다. 수령 성공 시 지급 내역을 <b>스테이지 클리어
    /// 연출 UI를 재활용해</b>(<see cref="StageClearOverlay.ShowRewards"/>) 보여주고, 세이브 스냅샷을
    /// 재로드해 골드·인벤토리를 최신화한다.
    /// 정적 계층(캔버스·패널·헤더·스크롤·버튼)은 에디터 빌더(MailUiBuilder)가 프리팹에 굽고,
    /// 메일 행은 목록 조회 결과로 런타임에 생성한다.
    /// </summary>
    public class MailPanelController : MonoBehaviour
    {
        private const float CanvasRefWidth = 1080f;
        private const float CanvasRefHeight = 1920f;
        private const float RowHeight = 170f;

        // 첨부 슬롯은 행 우측의 '받기' 버튼(또는 상태 라벨) 바로 왼쪽에 오른쪽 정렬로 놓는다.
        private const float AttachmentSlotSize = 52f;
        private const float AttachmentSlotGap = 8f;
        private const float ClaimAreaWidth = 176f;   // 우측 여백(20) + 상태 라벨 폭(140) + 슬롯과의 간격(16)
        private const float RowTextLeft = 136f;      // 봉투 아이콘 오른쪽부터 시작하는 텍스트 영역
        private const float TextToSlotGap = 24f;     // 본문 텍스트와 첨부 슬롯 사이 최소 간격

        [Header("UI 리소스 (Assets/Art/UI/Mail — 에디터 빌더가 배선)")]
        [Tooltip("우편함 패널 배경(mailbox_bg). 없으면 단색 배경.")]
        [SerializeField] private Sprite _backgroundSprite;
        [Tooltip("메일 행 슬롯 배경(mailbox_slot). 없으면 단색 행.")]
        [SerializeField] private Sprite _slotSprite;
        [Tooltip("미열람 봉투 아이콘(mail_unread).")]
        [SerializeField] private Sprite _unreadSprite;
        [Tooltip("열람 봉투 아이콘(mail_readed).")]
        [SerializeField] private Sprite _readSprite;
        [Tooltip("받기/모두 받기 버튼 배경(Assets/Art/UI/pixel_rpg_button). 없으면 단색 버튼.")]
        [SerializeField] private Sprite _buttonSprite;
        [Tooltip("공용 아이템 슬롯 프리팹(Assets/Prefabs/UI/ItemSlot). 첨부 표시에 사용하며, 없으면 텍스트 요약 폴백.")]
        [SerializeField] private GameObject _itemSlotPrefab;

        [Header("구성 참조 (에디터 빌더가 배선)")]
        [SerializeField] private Button _closeButton;
        [SerializeField] private Button _dimButton;
        [SerializeField] private Button _claimAllButton;
        [SerializeField] private RectTransform _listContent;
        [SerializeField] private Text _messageText;
        [SerializeField] private Text _emptyText;

        private Font _font;
        private bool _busy;
        private readonly List<GameObject> _rows = new List<GameObject>();

        private bool AlreadyBuilt => _listContent != null;

        private void Awake()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (!AlreadyBuilt)
            {
                Construct(); // 폴백(프리팹 미배선 시)
            }
            WireRuntime();
        }

        /// <summary>패널이 표시될 때마다 우편함 목록을 새로 조회한다(조회 = 서버 읽음 처리).</summary>
        private void OnEnable()
        {
            if (Application.isPlaying && AlreadyBuilt)
            {
                SetMessage(string.Empty);
                RequestList();
            }
        }

        /// <summary>에디터 빌드 전용: 전체 정적 계층을 생성해 프리팹에 굽는다.</summary>
        public void EditorConstruct() => Construct();

        // ── 정적 계층 구성 ──

        /// <summary>캔버스·딤·패널·헤더·스크롤 목록·모두 받기 버튼·메시지 텍스트를 생성한다.</summary>
        private void Construct()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildCanvas();
            BuildDim();
            var panel = BuildPanel();
            BuildHeader(panel);
            BuildList(panel);
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

        /// <summary>패널 밖 클릭 시 닫히는 투명 차단막(배경을 어둡게 하지 않는다).</summary>
        private void BuildDim()
        {
            var img = NewImage("Dim", (RectTransform)transform, new Color(0f, 0f, 0f, 0f));
            Stretch(img.rectTransform);
            _dimButton = img.gameObject.AddComponent<Button>();
            _dimButton.transition = Selectable.Transition.None;
        }

        /// <summary>우편함 패널 본체(mailbox_bg 배경).</summary>
        private RectTransform BuildPanel()
        {
            var img = NewImage("PanelRoot", (RectTransform)transform, new Color(0.10f, 0.12f, 0.18f, 0.98f));
            if (_backgroundSprite != null)
            {
                img.sprite = _backgroundSprite;
                img.type = Image.Type.Simple;
                img.color = Color.white;
            }
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(880f, 900f);
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        private void BuildHeader(RectTransform panel)
        {
            var title = NewText("Title", panel, "우편함", 44, TextAnchor.MiddleCenter);
            title.fontStyle = FontStyle.Bold;
            var trt = title.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.anchoredPosition = new Vector2(0f, -26f);
            trt.sizeDelta = new Vector2(400f, 56f);

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

        /// <summary>메일 목록 스크롤 뷰(뷰포트 마스크 + 세로 레이아웃 콘텐츠 + 빈 목록 안내).</summary>
        private void BuildList(RectTransform panel)
        {
            var viewportImg = NewImage("Viewport", panel, new Color(0f, 0f, 0f, 0.001f));
            var vrt = viewportImg.rectTransform;
            vrt.anchorMin = new Vector2(0f, 0f);
            vrt.anchorMax = new Vector2(1f, 1f);
            vrt.offsetMin = new Vector2(28f, 200f);   // 하단: 모두 받기·메시지 공간
            vrt.offsetMax = new Vector2(-28f, -100f); // 상단: 헤더 공간
            viewportImg.gameObject.AddComponent<RectMask2D>();

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(vrt, false);
            _listContent = (RectTransform)contentGo.transform;
            _listContent.anchorMin = new Vector2(0f, 1f);
            _listContent.anchorMax = new Vector2(1f, 1f);
            _listContent.pivot = new Vector2(0.5f, 1f);
            _listContent.offsetMin = new Vector2(0f, 0f);
            _listContent.offsetMax = new Vector2(0f, 0f);
            var layout = contentGo.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 14f;
            layout.padding = new RectOffset(4, 4, 4, 4);
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = contentGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = viewportImg.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = vrt;
            scroll.content = _listContent;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            _emptyText = NewText("EmptyText", panel, "받은 메일이 없습니다", 30, TextAnchor.MiddleCenter);
            _emptyText.color = new Color(1f, 1f, 1f, 0.6f);
            var ert = _emptyText.rectTransform;
            ert.anchorMin = ert.anchorMax = new Vector2(0.5f, 0.5f);
            ert.sizeDelta = new Vector2(600f, 60f);
            ert.anchoredPosition = Vector2.zero;
            _emptyText.gameObject.SetActive(false);
        }

        /// <summary>하단 '모두 받기' 버튼과 오류/안내 메시지 텍스트.</summary>
        private void BuildFooter(RectTransform panel)
        {
            var btn = NewImage("ClaimAllButton", panel, new Color(0.22f, 0.5f, 0.3f, 1f));
            ApplyButtonSprite(btn);
            var brt = btn.rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0f);
            brt.pivot = new Vector2(0.5f, 0f);
            brt.anchoredPosition = new Vector2(0f, 44f);
            brt.sizeDelta = new Vector2(340f, 88f);
            var bt = NewText("Label", btn.rectTransform, "모두 받기", 34, TextAnchor.MiddleCenter);
            bt.fontStyle = FontStyle.Bold;
            Stretch(bt.rectTransform);
            _claimAllButton = btn.gameObject.AddComponent<Button>();

            _messageText = NewText("Message", panel, string.Empty, 26, TextAnchor.MiddleCenter);
            _messageText.color = new Color(1f, 0.75f, 0.45f);
            var mrt = _messageText.rectTransform;
            mrt.anchorMin = mrt.anchorMax = new Vector2(0.5f, 0f);
            mrt.pivot = new Vector2(0.5f, 0f);
            mrt.anchoredPosition = new Vector2(0f, 146f);
            mrt.sizeDelta = new Vector2(780f, 40f);
        }

        private void WireRuntime()
        {
            if (_closeButton != null) _closeButton.onClick.AddListener(Close);
            if (_dimButton != null) _dimButton.onClick.AddListener(Close);
            if (_claimAllButton != null) _claimAllButton.onClick.AddListener(OnClaimAll);
        }

        // ── 목록 조회/표시 ──

        /// <summary>우편함 목록을 서버에서 조회해 행을 다시 그린다(POST /api/game/mail/list).
        /// 조회 결과는 레드닷 알림기(<see cref="MailNotifier"/>)의 스냅샷으로도 공유해 미수령 알림을 즉시 최신화하고,
        /// 목록을 그린 뒤에는 사용자가 우편함을 실제로 확인했으므로 미열람 표시를 해제한다.</summary>
        private void RequestList()
        {
            if (NetworkManager.Instance == null || !Session.IsLoggedIn)
            {
                SetMessage("로그인이 필요합니다.");
                return;
            }
            var req = new AuthRequest { userId = Session.UserId, token = Session.Token };
            NetworkManager.Instance.PostToGame<MailListResponse>("/api/game/mail/list", req, resp =>
            {
                var mails = resp != null && resp.data != null ? resp.data.mails : null;
                MailNotifier.ApplySnapshot(mails);
                RebuildRows(mails ?? new List<MailDto>());
                MailNotifier.MarkAllViewed();
            }, OnListError);
        }

        /// <summary>메일 목록으로 행 UI를 재구성한다(최신 발급 순).</summary>
        private void RebuildRows(List<MailDto> mails)
        {
            foreach (var row in _rows)
            {
                if (row != null) Destroy(row);
            }
            _rows.Clear();

            mails.Sort((a, b) => b.createdAt.CompareTo(a.createdAt));
            if (_emptyText != null)
            {
                _emptyText.gameObject.SetActive(mails.Count == 0);
            }
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            foreach (var mail in mails)
            {
                if (mail != null)
                {
                    _rows.Add(BuildRow(mail, now));
                }
            }
        }

        /// <summary>메일 1건의 행을 만든다. 좌측부터 봉투 아이콘 · 제목/본문,
        /// 우측 상단에 만료 표시, 우측에 '받기' 버튼(또는 수령 완료·만료 라벨)과 그 왼쪽에 첨부 아이콘을 둔다.</summary>
        private GameObject BuildRow(MailDto mail, long now)
        {
            bool expired = mail.expiresAt != 0 && now > mail.expiresAt;
            bool hasAttachments = mail.attachments != null && mail.attachments.Count > 0;
            bool claimable = hasAttachments && mail.claimed == 0 && !expired;

            var rowImg = NewImage("MailRow", _listContent, new Color(0.15f, 0.17f, 0.26f, 1f));
            if (_slotSprite != null)
            {
                rowImg.sprite = _slotSprite;
                rowImg.type = Image.Type.Simple;
                rowImg.color = Color.white;
            }
            var le = rowImg.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = RowHeight;
            le.flexibleHeight = 0f;
            var rt = rowImg.rectTransform;

            // 봉투 아이콘(좌측): 미열람 = mail_unread, 열람 = mail_readed.
            // 레드닷 갱신용 배경 조회도 서버에서 읽음 처리되므로, 사용자가 우편함을 열기 전까지의 미열람 여부는
            // MailNotifier가 로컬에 보존한 값(WasUnread)으로 판정한다.
            bool unread = mail.isRead == 0 || MailNotifier.WasUnread(mail.mailId);
            var envelope = NewImage("Envelope", rt, Color.white);
            envelope.sprite = unread ? _unreadSprite : _readSprite;
            envelope.preserveAspect = true;
            envelope.enabled = envelope.sprite != null;
            var ert = envelope.rectTransform;
            ert.anchorMin = ert.anchorMax = new Vector2(0f, 0.5f);
            ert.pivot = new Vector2(0.5f, 0.5f);
            ert.anchoredPosition = new Vector2(72f, 0f);
            ert.sizeDelta = new Vector2(96f, 96f);

            // 제목 / 본문 / 첨부 요약(아이콘 오른쪽, 위에서 아래로).
            var title = NewText("Title", rt, mail.title ?? string.Empty, 30, TextAnchor.UpperLeft);
            title.fontStyle = FontStyle.Bold;
            PlaceTopLeft(title.rectTransform, RowTextLeft, 18f, 480f, 36f);

            // 첨부 슬롯이 본문과 같은 높이에 오므로, 슬롯이 차지하는 폭만큼 본문 오른쪽을 비운다
            // (행 폭은 레이아웃이 정하므로 좌우 늘림 앵커로 배치한다).
            int slotCount = _itemSlotPrefab != null ? CountAttachments(mail) : 0;
            float slotsWidth = slotCount > 0
                ? slotCount * AttachmentSlotSize + (slotCount - 1) * AttachmentSlotGap
                : 0f;
            float bodyRight = slotCount > 0 ? ClaimAreaWidth + slotsWidth + TextToSlotGap : 200f;

            var body = NewText("Body", rt, mail.body ?? string.Empty, 22, TextAnchor.UpperLeft);
            body.color = new Color(1f, 1f, 1f, 0.72f);
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.verticalOverflow = VerticalWrapMode.Truncate;
            PlaceTopStretch(body.rectTransform, RowTextLeft, bodyRight, 58f, 52f);

            // 첨부: 공용 아이템 슬롯(아이콘·수량, hover 시 상세 팝업)로 표시. 프리팹 미배선 시 텍스트 요약 폴백.
            if (slotCount > 0)
            {
                BuildAttachmentSlots(mail, rt, slotCount);
            }
            else
            {
                var attach = NewText("Attachments", rt, AttachmentSummary(mail), 24, TextAnchor.UpperLeft);
                attach.color = new Color(1f, 0.85f, 0.4f);
                attach.fontStyle = FontStyle.Bold;
                PlaceTopLeft(attach.rectTransform, 136f, 116f, 480f, 32f);
            }

            // 만료 표시(우측 상단): 무기한이면 생략, 만료 전이면 남은 기간, 만료면 '만료'.
            var expiry = NewText("Expiry", rt, ExpiryLabel(mail, now, expired), 20, TextAnchor.UpperRight);
            expiry.color = expired ? new Color(1f, 0.45f, 0.4f) : new Color(1f, 1f, 1f, 0.55f);
            var xrt = expiry.rectTransform;
            xrt.anchorMin = xrt.anchorMax = new Vector2(1f, 1f);
            xrt.pivot = new Vector2(1f, 1f);
            xrt.anchoredPosition = new Vector2(-20f, -14f);
            xrt.sizeDelta = new Vector2(180f, 28f);

            // 우측: 받기 버튼 또는 상태 라벨.
            if (claimable)
            {
                var btn = NewImage("ClaimButton", rt, new Color(0.22f, 0.5f, 0.3f, 1f));
                ApplyButtonSprite(btn);
                var brt = btn.rectTransform;
                brt.anchorMin = brt.anchorMax = new Vector2(1f, 0.5f);
                brt.pivot = new Vector2(1f, 0.5f);
                brt.anchoredPosition = new Vector2(-20f, -8f);
                brt.sizeDelta = new Vector2(132f, 64f);
                var bt = NewText("Label", brt, "받기", 28, TextAnchor.MiddleCenter);
                bt.fontStyle = FontStyle.Bold;
                Stretch(bt.rectTransform);
                long mailId = mail.mailId;
                btn.gameObject.AddComponent<Button>().onClick.AddListener(() => OnClaim(mailId));
            }
            else if (hasAttachments)
            {
                var state = NewText("State", rt, mail.claimed == 1 ? "수령 완료" : "만료", 24, TextAnchor.MiddleCenter);
                state.color = mail.claimed == 1 ? new Color(1f, 1f, 1f, 0.5f) : new Color(1f, 0.45f, 0.4f, 0.9f);
                var srt = state.rectTransform;
                srt.anchorMin = srt.anchorMax = new Vector2(1f, 0.5f);
                srt.pivot = new Vector2(1f, 0.5f);
                srt.anchoredPosition = new Vector2(-20f, -8f);
                srt.sizeDelta = new Vector2(140f, 40f);
            }

            return rowImg.gameObject;
        }

        /// <summary>메일 행에 첨부를 공용 아이템 슬롯(ItemSlot 프리팹)로 나열한다(아이콘·수량,
        /// hover 시 item_detail_bg 배경의 공용 상세 팝업). 슬롯은 우측 '받기' 버튼(수령 완료·만료 시엔 상태 라벨)
        /// 바로 왼쪽에 버튼과 같은 높이로 오른쪽 정렬해, 받을 보상과 수령 버튼이 나란히 보이게 한다.
        /// 골드(rewardType 1)는 아이콘 코드 1을 쓰고 상세는 끈다.</summary>
        private void BuildAttachmentSlots(MailDto mail, RectTransform row, int slotCount)
        {
            int index = 0;
            foreach (var a in mail.attachments)
            {
                if (a == null)
                {
                    continue;
                }
                var go = Instantiate(_itemSlotPrefab, row);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
                rt.pivot = new Vector2(1f, 0.5f);
                // 오른쪽(버튼 쪽)부터 역순으로 채워, 첫 첨부가 가장 왼쪽에 오도록 한다.
                float fromRight = ClaimAreaWidth
                    + (slotCount - 1 - index) * (AttachmentSlotSize + AttachmentSlotGap);
                rt.anchoredPosition = new Vector2(-fromRight, -8f); // y: 받기 버튼과 같은 높이
                rt.sizeDelta = new Vector2(AttachmentSlotSize, AttachmentSlotSize);
                var view = go.GetComponent<ItemSlotView>();
                if (view != null)
                {
                    if (a.rewardType == 1)
                    {
                        view.Setup(1, a.quantity, $"+{a.quantity:N0}", false); // 골드
                    }
                    else
                    {
                        view.Setup(a.rewardCode, a.quantity);
                    }
                }
                index++;
            }
        }

        /// <summary>메일의 유효한 첨부 개수(null 항목 제외). 슬롯 배치 폭과 본문 폭 계산에 쓴다.</summary>
        private static int CountAttachments(MailDto mail)
        {
            if (mail.attachments == null)
            {
                return 0;
            }
            int count = 0;
            foreach (var a in mail.attachments)
            {
                if (a != null)
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>첨부 요약 문자열("골드 +5,000 · 강철 대검 x1"). 첨부가 없으면 빈 문자열.</summary>
        private static string AttachmentSummary(MailDto mail)
        {
            if (mail.attachments == null || mail.attachments.Count == 0)
            {
                return string.Empty;
            }
            var parts = new List<string>();
            foreach (var a in mail.attachments)
            {
                if (a == null) continue;
                parts.Add(a.rewardType == 1
                    ? $"골드 +{a.quantity:N0}"
                    : $"{ItemName(a.rewardCode)} x{a.quantity}");
            }
            return string.Join(" · ", parts);
        }

        /// <summary>만료 표시 문자열: 무기한 "", 만료 "만료", 하루 이상 "D-n", 하루 미만 "n시간 남음".</summary>
        private static string ExpiryLabel(MailDto mail, long now, bool expired)
        {
            if (mail.expiresAt == 0)
            {
                return string.Empty;
            }
            if (expired)
            {
                return "만료";
            }
            long remain = mail.expiresAt - now;
            long days = remain / 86400;
            return days >= 1 ? $"D-{days}" : $"{Math.Max(1, remain / 3600)}시간 남음";
        }

        /// <summary>아이템 코드의 표시 이름(마스터 데이터, 없으면 "아이템 {code}").</summary>
        private static string ItemName(int itemCode)
        {
            var db = MasterDataManager.Db;
            return db != null && db.Items.TryGetValue(itemCode, out var im) ? im.name : $"아이템 {itemCode}";
        }

        // ── 수령 ──

        /// <summary>단건 수령(POST /api/game/mail/claim). 성공 시 지급 내역 모달 + 세션 재로드 + 목록 갱신.</summary>
        private void OnClaim(long mailId)
        {
            if (_busy || NetworkManager.Instance == null || !Session.IsLoggedIn)
            {
                return;
            }
            _busy = true;
            var req = new MailClaimRequest
            {
                userId = Session.UserId,
                token = Session.Token,
                data = new MailClaimData { mailId = mailId },
            };
            NetworkManager.Instance.PostToGame<MailClaimResponse>("/api/game/mail/claim", req, resp =>
            {
                Debug.Log($"[Mail] 수령 완료 mailId={mailId}");
                var data = resp != null ? resp.data : null;
                ShowGainedRewards(data != null ? data.gained : null);
                ApplyClaimResult(data != null ? data.inventoryDelta : null, data != null ? data.balance : null);
            }, OnClaimError);
        }

        /// <summary>일괄 수령(POST /api/game/mail/claim-all). 수령 대상이 없으면 안내만 표시.</summary>
        private void OnClaimAll()
        {
            if (_busy || NetworkManager.Instance == null || !Session.IsLoggedIn)
            {
                return;
            }
            _busy = true;
            var req = new AuthRequest { userId = Session.UserId, token = Session.Token };
            NetworkManager.Instance.PostToGame<MailClaimAllResponse>("/api/game/mail/claim-all", req, resp =>
            {
                var data = resp != null ? resp.data : null;
                if (data == null || data.claimedMailIds == null || data.claimedMailIds.Count == 0)
                {
                    _busy = false;
                    SetMessage("수령할 수 있는 메일이 없습니다.");
                    return;
                }
                Debug.Log($"[Mail] 일괄 수령 완료 {data.claimedMailIds.Count}건");
                ShowGainedRewards(data.gained);
                ApplyClaimResult(data.inventoryDelta, data.balance);
            }, OnClaimError);
        }

        /// <summary>지급된 첨부(골드·아이템)를 <b>스테이지 클리어 연출 UI를 재활용</b>해 보여준다
        /// (<see cref="StageClearOverlay.ShowRewards"/> — 팡파레 + 보상 칸이 왼쪽부터 하나씩 등장).
        /// 첨부가 없는 안내 메일을 수령한 경우엔 보여줄 보상이 없으므로 기존 텍스트 모달로 안내한다.</summary>
        private static void ShowGainedRewards(MailGainedDto gained)
        {
            var rewards = ToStageRewards(gained);
            if (rewards.gold <= 0 && rewards.items.Count == 0)
            {
                ModalManager.Instance?.ShowConfirm("우편 수령", "첨부가 없는 메일입니다.");
                return;
            }
            StageClearOverlay.ShowRewards("보상 획득!", rewards);
        }

        /// <summary>메일 지급 내역을 클리어 연출이 쓰는 보상 DTO로 옮긴다(골드 = 재화 타입 1, 나머지는 아이템).
        /// 메일 첨부에는 경험치가 없으므로 exp는 채우지 않는다.</summary>
        private static StageRewardsDto ToStageRewards(MailGainedDto gained)
        {
            var rewards = new StageRewardsDto();
            if (gained == null)
            {
                return rewards;
            }
            if (gained.currencies != null)
            {
                foreach (var c in gained.currencies)
                {
                    if (c != null && c.currencyType == 1 && c.amount > 0)
                    {
                        rewards.gold += c.amount;
                    }
                }
            }
            if (gained.items != null)
            {
                foreach (var it in gained.items)
                {
                    if (it != null && it.quantity > 0)
                    {
                        rewards.items.Add(new RewardItemDto { itemCode = it.itemCode, quantity = it.quantity });
                    }
                }
            }
            return rewards;
        }

        /// <summary>수령 응답으로 세션(가방·골드)을 맞추고 우편함 목록을 다시 그린다.
        /// 첨부 적재는 <c>inventoryDelta</c>, 재화는 <c>balance</c>가 담고 오므로 세이브를 재조회하지 않는다(§5.0 규약).
        /// 목록 재조회는 수령 표시·레드닷 갱신을 위한 것이라 그대로 유지한다(가방 조회가 아니다).</summary>
        private void ApplyClaimResult(InventoryDeltaDto delta, List<CurrencyDto> balance)
        {
            Session.ApplyInventoryDelta(delta);
            Session.ApplyBalance(balance);
            Session.RaiseInventoryChanged(); // 골드·아이템 표시(HUD·패널) 갱신 트리거
            _busy = false;
            RequestList();
        }

        /// <summary>목록 조회/재로드 실패: 메시지만 표시한다(재조회하지 않음 — 실패 시 재조회하면 무한 루프).</summary>
        private void OnListError(NetworkError error)
        {
            _busy = false;
            Debug.LogWarning($"[Mail] 목록 조회 실패: {error}");
            SetMessage(ErrorMessages.ToKorean(error));
        }

        /// <summary>수령 실패: 메시지를 표시하고, 상태 어긋남(이미 수령/만료 등) 복구를 위해
        /// 서버 응답이 있는 오류일 때만 목록을 1회 재조회한다(연결 실패는 재조회 안 함).</summary>
        private void OnClaimError(NetworkError error)
        {
            _busy = false;
            Debug.LogWarning($"[Mail] 수령 실패: {error}");
            SetMessage(ErrorMessages.ToKorean(error));
            if (!error.IsTransportError)
            {
                RequestList();
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
                UIManager.Instance.Hide(UIManager.PanelType.Mail);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }

        // ── UI 헬퍼 ──

        /// <summary>버튼 이미지에 공용 버튼 스프라이트(pixel_rpg_button)를 슬라이스로 적용하고
        /// 단색 배경을 제거(흰색으로 환원)한다. 스프라이트 미배선 시 단색 폴백을 유지한다.</summary>
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

        private static void PlaceTopLeft(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        /// <summary>행 상단 기준으로 좌우를 늘려 배치한다(폭 = 행 폭 − left − right).
        /// 행 폭은 세로 레이아웃이 정하므로, 우측 요소를 피해야 하는 텍스트에 쓴다.</summary>
        private static void PlaceTopStretch(RectTransform rt, float left, float right, float y, float h)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.offsetMin = new Vector2(left, -y - h);
            rt.offsetMax = new Vector2(-right, -y);
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
