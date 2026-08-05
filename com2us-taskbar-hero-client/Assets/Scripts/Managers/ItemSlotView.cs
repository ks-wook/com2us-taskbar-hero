using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TaskbarHero.Client.Managers
{
    /// <summary>
    /// 공용 아이템 슬롯 뷰(단일 프리팹 <c>Assets/Prefabs/UI/ItemSlot.prefab</c>, ItemSlotBuilder가 생성).
    /// 아이템 아이콘 + 등급 배경 + 수량을 표시하고, hover 시 item_detail_bg 배경의 공용 상세 팝업
    /// (<see cref="ItemDetailPopup"/>)을 띄운다. 스테이지 클리어 보상·우편함 첨부·(추후) 거래소 등
    /// 아이템 아이콘이 노출되는 화면은 이 프리팹 하나로 통일한다.
    /// 내부 위젯은 앵커 비율로 배치되어 프리팹 크기(sizeDelta)만 바꿔도 그대로 스케일된다.
    /// </summary>
    public class ItemSlotView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [Tooltip("슬롯 테두리 프레임(item_slot). hover 레이캐스트 대상.")]
        [SerializeField] private Image _frameImage;
        [Tooltip("등급 배경(테두리 안쪽, 아이콘 뒤).")]
        [SerializeField] private Image _gradeBackground;
        [Tooltip("아이템 아이콘(없으면 등급 폴백 색 사각형).")]
        [SerializeField] private Image _iconImage;
        [Tooltip("수량 텍스트(우하단, BestFit).")]
        [SerializeField] private Text _quantityText;
        [Tooltip("아이콘 대신 표시할 텍스트 라벨(경험치 등 아이템 아이콘이 없는 보상). SetupLabel에서만 사용.")]
        [SerializeField] private Text _iconLabelText;
        [Tooltip("상세 팝업 배경(Assets/Art/UI/item_detail_bg). 없으면 단색 팝업.")]
        [SerializeField] private Sprite _detailBackgroundSprite;
        [Tooltip("획득 완료 표시(Assets/Art/UI/Attendance/check). 슬롯 레이어 가장 위(마지막 자식)에 그려진다. 기본 숨김.")]
        [SerializeField] private Image _claimedOverlay;

        // 강화 단계 배지 규격 — <b>모든 UI가 같은 자리·같은 색·같은 크기</b>로 보이도록 이 상수만 쓴다.
        // (인벤토리는 좌하단 금색 32px, 큐브는 우상단 금색 20px으로 서로 달랐다 — 2026-08-05 통일.)
        // 배지는 이 컴포넌트가 만들고 꾸미므로, 슬롯 프리팹을 쓰는 화면은 자동으로 같은 규격을 얻는다.
        private static readonly Color EnhanceBadgeColor = Color.white;                    // 글자색(흰색)
        private static readonly Color EnhanceBadgeOutlineColor = new Color(0f, 0f, 0f, 0.95f); // 검은 외곽선
        private const float EnhanceBadgeOutlineDistance = 2f;
        private const int EnhanceBadgeMinFontSize = 14;  // 작은 슬롯(거래 72px)에서의 하한
        private const int EnhanceBadgeMaxFontSize = 96;  // 큰 슬롯(보상 150px)에서의 상한 — "크게" 요구
        // 글자 크기는 <b>슬롯 높이에 비례해 직접 정한다</b>(BestFit 금지 — 아래 SetEnhanceLevel 주석 참고).
        // 0.352 = 배지 칸 높이 비율(0.40) × 그 안을 채우는 비율(0.88)로, BestFit이 고르던 값과 같아진다
        // (실측: 150px 슬롯 53 · 118px 슬롯 42 · 72px 슬롯 25).
        private const float EnhanceBadgeFontHeightRatio = 0.352f;
        // 레이아웃 전(부모가 아직 크기를 안 준 상태)에는 이 높이 미만이면 그리지 않는다 — 엉뚱한 크기로
        // 한 프레임 보이는 것을 막고, 크기가 정해지는 순간(OnRectTransformDimensionsChange) 한 번에 띄운다.
        private const float EnhanceBadgeMinSlotHeight = 24f;
        // 슬롯 좌측 하단 영역(칸 비율). 높이 비율(40%)이 수량 표기(30%)보다 커서 강화 단계가 더 크게 읽힌다.
        // 수량 텍스트는 같은 아래쪽이지만 우측 정렬이라 겹치지 않는다(강화 대상인 장비는 stack_max=1이라
        // 수량 표기 자체가 없고, 재료가 우연히 강화 단계를 가져도 좌/우로 갈린다).
        private static readonly Vector2 EnhanceBadgeAnchorMin = new Vector2(0.08f, 0.05f);
        private static readonly Vector2 EnhanceBadgeAnchorMax = new Vector2(0.62f, 0.45f);

        // 수량 표기("xN"·"+N") 글자 크기도 <b>슬롯 높이에 비례</b>해 정한다. 프리팹에 구워진 고정 30은 큰 칸
        // (인벤토리 118px·큐브 114px) 기준이라, <b>우편함 첨부(52px)처럼 작은 칸에서는 숫자가 칸을 뒤덮었다</b>
        // (골드 "+50,000"은 글자 수도 많다 — 2026-08-05 축소).
        // 0.255 = 수량 칸 높이 비율(0.30) × 그 안을 채우는 비율(0.85) → 118px 슬롯에서 30(기존과 동일).
        // 상한을 프리팹 기본값(30)으로 둬서 <b>큰 칸은 지금 크기를 그대로 유지</b>하고 작은 칸만 줄어들게 한다
        // (실측 환산: 118px→30 · 114px→29 · 72px→18 · 52px→13).
        private const float QuantityFontHeightRatio = 0.255f;
        private const int QuantityMinFontSize = 10;
        private const int QuantityMaxFontSize = 30;

        // 획득 연출: 체크 표시가 슬롯 밖으로 크게 부풀었다가 원래 크기로 잦아든다.
        private const float ClaimedPopExpandScale = 3f;   // 최대 크기(원래 크기 배수)
        private const float ClaimedPopDuration = 0.45f;   // 연출 전체 길이
        private const float ClaimedPopExpandRatio = 0.4f; // 전체 길이 중 커지는 구간의 비율(나머지는 되돌아오는 구간)

        private int _itemCode;
        private long _quantity;
        private bool _showDetail;
        private int _enhanceLevel;      // 장비 강화 단계(0 = 미강화). 배지 표시 + 상세 팝업 스탯 배율에 쓴다
        private Text _enhanceBadge;     // "+N" 배지(강화 단계가 있을 때만 생성)
        private Color? _frameBaseColor; // 프레임 원래 색(SetFrameVisible로 감췄다 되살릴 때 기준)
        private float _iconBaseAlpha = 1f; // 구성이 정한 아이콘 알파(SetIconDimmed의 기준값)

        private const float DimmedIconAlphaRatio = 0.35f; // 흐리게 표시할 때의 알파 배수
        private Coroutine _claimedPopRoutine;
        private Canvas _claimedLift; // 획득 연출 동안만 붙는 정렬 덮어쓰기 Canvas(끝나면 제거)

        /// <summary>아이템 코드·수량으로 슬롯을 구성한다(수량 2 이상이면 "xN" 표기, hover 상세 활성).</summary>
        public void Setup(int itemCode, long quantity)
        {
            Setup(itemCode, quantity, quantity > 1 ? $"x{quantity}" : string.Empty, true);
        }

        /// <summary>슬롯을 구성한다. quantityText는 표기 문자열(빈 문자열 = 숨김),
        /// showDetail이 false면 hover 상세 팝업과 레이캐스트를 끈다(골드 등 마스터 데이터 없는 항목).</summary>
        public void Setup(int itemCode, long quantity, string quantityText, bool showDetail)
        {
            _itemCode = itemCode;
            _quantity = quantity;
            _showDetail = showDetail;

            int grade = 1;
            var db = MasterDataManager.Db;
            if (db != null && db.Items.TryGetValue(itemCode, out var im) && im != null)
            {
                grade = im.grade;
            }

            if (_gradeBackground != null)
            {
                _gradeBackground.color = GradeColors.RewardSlotBackground(grade);
            }

            var iconDb = ItemIconDatabase.Load();
            var icon = iconDb != null ? iconDb.Get(itemCode) : null;
            if (_iconImage != null)
            {
                _iconImage.enabled = true;
                _iconImage.sprite = icon;
                _iconImage.color = icon != null
                    ? Color.white
                    : GradeColors.IconFallback(grade) * new Color(1f, 1f, 1f, 0.6f); // 아이콘 없을 때 색 폴백
                _iconBaseAlpha = _iconImage.color.a; // SetIconDimmed가 되돌릴 기준 밝기
            }
            if (_iconLabelText != null)
            {
                _iconLabelText.gameObject.SetActive(false); // 아이콘 모드에서는 텍스트 라벨 숨김(SetupLabel 전용)
            }

            if (_quantityText != null)
            {
                _quantityText.text = quantityText ?? string.Empty;
                _quantityText.gameObject.SetActive(!string.IsNullOrEmpty(quantityText));
                ApplyQuantityFontSize(); // 칸 크기에 맞춘 글자 크기(작은 칸에서 숫자가 칸을 덮지 않게)
            }

            if (_frameImage != null)
            {
                _frameImage.raycastTarget = showDetail; // 상세 없음(골드 등)이면 입력 통과
            }

            ResetClaimedOverlay();
            SetEnhanceLevel(0); // 슬롯은 재사용되므로 구성마다 강화 배지를 초기화한다(필요하면 호출측이 다시 지정)
        }

        /// <summary>마스터 데이터에 없는 재화(경험치 등)를 <b>스프라이트를 직접 지정해</b> 아이템과 동일한
        /// 아이콘 모드로 구성한다(아이템 코드로는 아이콘을 찾을 수 없는 항목용). 등급 배경은 일반 등급 톤,
        /// 수량/획득량은 아이템과 같은 자리(슬롯 안쪽 우하단)에 표시하며 hover 상세 팝업은 제공하지 않는다.</summary>
        public void SetupSprite(Sprite icon, string quantityText)
        {
            _itemCode = 0;
            _quantity = 0;
            _showDetail = false;

            if (_gradeBackground != null)
            {
                _gradeBackground.color = GradeColors.RewardSlotBackground(1);
            }
            if (_iconImage != null)
            {
                _iconImage.enabled = icon != null;
                _iconImage.sprite = icon;
                _iconImage.color = Color.white;
                _iconBaseAlpha = 1f;
            }
            if (_iconLabelText != null)
            {
                _iconLabelText.gameObject.SetActive(false); // 아이콘 모드에서는 텍스트 라벨 숨김
            }
            if (_quantityText != null)
            {
                _quantityText.text = quantityText ?? string.Empty;
                _quantityText.gameObject.SetActive(!string.IsNullOrEmpty(quantityText));
                ApplyQuantityFontSize(); // 칸 크기에 맞춘 글자 크기(작은 칸에서 숫자가 칸을 덮지 않게)
            }
            if (_frameImage != null)
            {
                _frameImage.raycastTarget = false;
            }

            ResetClaimedOverlay();
            SetEnhanceLevel(0); // 슬롯은 재사용되므로 구성마다 강화 배지를 초기화한다(필요하면 호출측이 다시 지정)
        }

        /// <summary>아이템 아이콘이 없는 보상(경험치 등)을 위한 구성. 아이콘 대신 텍스트 라벨을 슬롯 중앙에
        /// 표시하고, 수량은 일반 아이템과 동일하게 슬롯 안쪽(우하단)에 표시해 외형을 통일한다.
        /// 마스터 데이터가 없는 보상이라 hover 상세 팝업은 제공하지 않는다.</summary>
        public void SetupLabel(string label, Color labelColor, string quantityText)
        {
            _itemCode = 0;
            _quantity = 0;
            _showDetail = false;

            if (_gradeBackground != null)
            {
                _gradeBackground.color = GradeColors.RewardSlotBackground(1); // 일반 등급 톤(짙은 남색)으로 통일
            }
            if (_iconImage != null)
            {
                _iconImage.enabled = false;
            }
            if (_iconLabelText != null)
            {
                _iconLabelText.text = label;
                _iconLabelText.color = labelColor;
                _iconLabelText.gameObject.SetActive(true);
            }
            if (_quantityText != null)
            {
                _quantityText.text = quantityText ?? string.Empty;
                _quantityText.gameObject.SetActive(!string.IsNullOrEmpty(quantityText));
                ApplyQuantityFontSize(); // 칸 크기에 맞춘 글자 크기(작은 칸에서 숫자가 칸을 덮지 않게)
            }
            if (_frameImage != null)
            {
                _frameImage.raycastTarget = false;
            }

            ResetClaimedOverlay();
            SetEnhanceLevel(0); // 슬롯은 재사용되므로 구성마다 강화 배지를 초기화한다(필요하면 호출측이 다시 지정)
        }

        /// <summary>
        /// 장비 강화 단계를 표시한다(슬롯 <b>좌측 하단</b>의 <b>흰 "+N"</b> 배지 + 검은 외곽선, 그리고 상세 팝업의
        /// 스탯 배율 기준). 0 이하면 배지를 감춘다. <see cref="Setup(int,long,string,bool)"/> 뒤에 호출한다
        /// (구성이 강화 단계를 알 수 없는 화면 — 뽑기·클리어 보상 등 — 은 호출하지 않으면 그대로 미강화로 표시된다).
        /// <para>자리·색·크기는 <see cref="EnhanceBadgeColor"/> 등 상수로 고정되어 있어, 이 슬롯을 쓰는 모든 화면
        /// (인벤토리·큐브·거래소·우편함·출석부·뽑기·클리어 보상)에서 동일하게 보인다.</para>
        /// <para><b>글자 크기는 BestFit(<c>resizeTextForBestFit</c>)으로 정하지 않는다</b> — BestFit은 rect·overflow
        /// 설정이 바뀔 때마다 크기를 다시 계산하는데, <c>UiTextStyle</c>이 0.4초 주기로 훑으며 줄이 잘릴 것 같은
        /// 텍스트의 <c>verticalOverflow</c>를 Truncate → Overflow로 바꾸기 때문에, 그 순간 BestFit의 기준이 사라져
        /// <b>글자가 작아졌다 커지는 현상</b>이 보였다(큐브에서 타일을 누르면 그리드를 다시 만들어 배지가 새로
        /// 생성되므로 클릭마다 재현됐다). 지금은 슬롯 높이에 비례한 값을 직접 넣고 overflow를 양방향 Overflow로
        /// 두어(그래서 <c>UiTextStyle</c>도 손대지 않는다) 크기가 한 번 정해지면 변하지 않는다.</para>
        /// </summary>
        public void SetEnhanceLevel(int enhanceLevel)
        {
            _enhanceLevel = enhanceLevel > 0 ? enhanceLevel : 0;
            if (_enhanceLevel <= 0)
            {
                if (_enhanceBadge != null)
                {
                    _enhanceBadge.gameObject.SetActive(false);
                }
                return;
            }

            if (_enhanceBadge == null)
            {
                var go = new GameObject("EnhanceBadge", typeof(RectTransform), typeof(Text), typeof(Outline));
                go.transform.SetParent(transform, false);
                _enhanceBadge = go.GetComponent<Text>();
                _enhanceBadge.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                _enhanceBadge.alignment = TextAnchor.LowerLeft;
                _enhanceBadge.fontStyle = FontStyle.Bold;
                _enhanceBadge.color = EnhanceBadgeColor;
                _enhanceBadge.raycastTarget = false;               // 슬롯 hover·클릭 판정을 가로채지 않는다
                _enhanceBadge.resizeTextForBestFit = false;         // 크기는 슬롯 높이로 직접 정한다(위 주석)
                // 양방향 Overflow — 글자 크기를 우리가 정하므로 칸에 맞출 필요가 없고, Truncate였다면
                // UiTextStyle이 overflow를 바꿔 크기 재계산을 유발한다(줄이 사라지는 문제도 함께 피한다).
                _enhanceBadge.horizontalOverflow = HorizontalWrapMode.Overflow;
                _enhanceBadge.verticalOverflow = VerticalWrapMode.Overflow;
                var outline = go.GetComponent<Outline>();          // 밝은 아이콘 위에서도 읽히도록
                outline.effectColor = EnhanceBadgeOutlineColor;
                outline.effectDistance = new Vector2(EnhanceBadgeOutlineDistance, -EnhanceBadgeOutlineDistance);
                var rt = _enhanceBadge.rectTransform;
                rt.anchorMin = EnhanceBadgeAnchorMin;
                rt.anchorMax = EnhanceBadgeAnchorMax;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }
            _enhanceBadge.transform.SetAsLastSibling(); // 아이콘·등급 배경 위에 그린다
            _enhanceBadge.text = $"+{_enhanceLevel}";
            ApplyEnhanceBadgeFontSize();
        }

        /// <summary>
        /// 배지 글자 크기를 <b>슬롯 높이에 비례한 고정값</b>으로 넣고 표시 여부를 정한다.
        /// 부모 레이아웃이 아직 크기를 주지 않았으면(높이 &lt; <see cref="EnhanceBadgeMinSlotHeight"/>) 그리지 않고,
        /// 크기가 정해질 때 <see cref="OnRectTransformDimensionsChange"/>가 다시 불러 한 번에 제 크기로 띄운다 —
        /// 엉뚱한 크기로 한 프레임 보이는 것을 막는다.
        /// </summary>
        private void ApplyEnhanceBadgeFontSize()
        {
            if (_enhanceBadge == null || _enhanceLevel <= 0)
            {
                return;
            }
            float slotHeight = ((RectTransform)transform).rect.height;
            if (slotHeight < EnhanceBadgeMinSlotHeight)
            {
                _enhanceBadge.gameObject.SetActive(false);
                return;
            }
            _enhanceBadge.fontSize = Mathf.Clamp(
                Mathf.RoundToInt(slotHeight * EnhanceBadgeFontHeightRatio),
                EnhanceBadgeMinFontSize, EnhanceBadgeMaxFontSize);
            _enhanceBadge.gameObject.SetActive(true);
        }

        /// <summary>
        /// 수량 표기 글자 크기를 <b>슬롯 높이에 비례한 값</b>으로 넣는다(상한 = 프리팹 기본값).
        /// 레이아웃 전(높이가 아직 없을 때)에는 건드리지 않는다 — 배지와 달리 <b>숨기지는 않는다</b>
        /// (수량은 잠깐 원래 크기로 보이는 게 사라지는 것보다 낫다). 크기가 정해지면
        /// <see cref="OnRectTransformDimensionsChange"/>가 다시 불러 제 크기로 맞춘다.
        /// </summary>
        private void ApplyQuantityFontSize()
        {
            if (_quantityText == null)
            {
                return;
            }
            float slotHeight = ((RectTransform)transform).rect.height;
            if (slotHeight < EnhanceBadgeMinSlotHeight)
            {
                return;
            }
            _quantityText.fontSize = Mathf.Clamp(
                Mathf.RoundToInt(slotHeight * QuantityFontHeightRatio),
                QuantityMinFontSize, QuantityMaxFontSize);
        }

        /// <summary>슬롯 크기가 바뀌면(부모 레이아웃 확정·창 크기 변경) 배지·수량 글자 크기를 다시 맞춘다.</summary>
        private void OnRectTransformDimensionsChange()
        {
            ApplyEnhanceBadgeFontSize();
            ApplyQuantityFontSize();
        }

        /// <summary>
        /// 아이콘을 흐리게 표시한다(착용 불가 장비처럼 "가질 수는 있으나 쓸 수 없는" 상태 표현).
        /// 기준 알파는 <see cref="Setup(int,long,string,bool)"/>이 정한 값(아이콘 있음 1, 폴백 색 0.6)이고
        /// 여기서는 그 값에 배수만 적용하므로, 껐다 켜도 원래 밝기로 정확히 돌아온다.
        /// </summary>
        public void SetIconDimmed(bool dimmed)
        {
            if (_iconImage == null)
            {
                return;
            }
            var c = _iconImage.color;
            _iconImage.color = new Color(c.r, c.g, c.b, _iconBaseAlpha * (dimmed ? DimmedIconAlphaRatio : 1f));
        }

        /// <summary>
        /// 슬롯 테두리 프레임을 감춘다/보인다. <b>이미 자기 칸 프레임을 그리는 UI 안에 이 슬롯을 넣을 때</b>
        /// (인벤토리 격자·장비 부위 칸) 프레임이 이중으로 겹쳐 보이지 않게 끄는 용도다.
        /// 프레임을 꺼도 레이캐스트 대상 여부는 <see cref="Setup(int,long,string,bool)"/>의 showDetail이 정한다
        /// (투명 이미지도 클릭·hover를 받으므로, 끄는 것은 그림뿐이다).
        /// </summary>
        public void SetFrameVisible(bool visible)
        {
            if (_frameImage == null)
            {
                return;
            }
            if (!_frameBaseColor.HasValue)
            {
                _frameBaseColor = _frameImage.color; // 프레임 스프라이트가 없을 때의 폴백 색까지 그대로 되살리기 위해
            }
            var c = _frameBaseColor.Value;
            _frameImage.color = visible ? c : new Color(c.r, c.g, c.b, 0f);
        }

        /// <summary>슬롯 테두리 프레임을 화면 전용 아트로 교체한다(예: 출석부 달력 칸 = attendance_item_slot).
        /// null을 넘기면 공용 프레임(item_slot)을 그대로 둔다. 구성(Setup)과 무관하게 유지된다.</summary>
        public void SetFrameSprite(Sprite frameSprite)
        {
            if (frameSprite == null || _frameImage == null)
            {
                return;
            }
            _frameImage.sprite = frameSprite;
            _frameImage.type = Image.Type.Simple;
            _frameImage.color = Color.white;
        }

        /// <summary>획득 완료 표시(check)를 켜고 끈다(연출 없이 즉시 반영, 예: 출석부 현황 새로고침).</summary>
        public void SetClaimed(bool claimed)
        {
            CancelClaimedPop();
            if (_claimedOverlay != null)
            {
                _claimedOverlay.gameObject.SetActive(claimed);
            }
        }

        /// <summary>방금 획득했음을 알리는 연출: 획득 완료 표시(check)가 원래 크기보다 훨씬 크게
        /// (<see cref="ClaimedPopExpandScale"/>배) 부풀었다가 원래 크기로 돌아온다
        /// (예: 출석부에서 오늘자 보상을 처음 받는 순간).
        /// <paramref name="onComplete"/>는 연출이 끝난 뒤 호출된다(획득 안내 모달처럼 연출 후에 이어질 처리용).
        /// 표시할 오버레이가 없으면 연출 없이 즉시 호출한다. 연출 도중 <see cref="SetClaimed"/> 등으로
        /// 상태가 덮어써지면 호출되지 않는다(그 처리가 무효가 된 것이므로).</summary>
        public void PlayClaimedPopAnimation(Action onComplete = null)
        {
            if (_claimedOverlay == null)
            {
                onComplete?.Invoke();
                return;
            }
            CancelClaimedPop(); // 재생 중이던 연출이 있으면 크기·정렬을 원래대로 되돌리고 새로 시작
            _claimedOverlay.gameObject.SetActive(true);
            _claimedPopRoutine = StartCoroutine(ClaimedPopRoutine(onComplete));
        }

        private IEnumerator ClaimedPopRoutine(Action onComplete)
        {
            var rt = _claimedOverlay.rectTransform;
            LiftClaimedOverlay(); // 커지는 동안 이웃 슬롯에 가리지 않도록 위로 띄운다
            float expandDuration = ClaimedPopDuration * ClaimedPopExpandRatio;
            float settleDuration = ClaimedPopDuration - expandDuration;

            float elapsed = 0f;
            while (elapsed < expandDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(elapsed / expandDuration);
                rt.localScale = Vector3.one * Mathf.Lerp(1f, ClaimedPopExpandScale, Mathf.SmoothStep(0f, 1f, k));
                yield return null;
            }

            elapsed = 0f;
            while (elapsed < settleDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(elapsed / settleDuration);
                rt.localScale = Vector3.one * Mathf.Lerp(ClaimedPopExpandScale, 1f, Mathf.SmoothStep(0f, 1f, k));
                yield return null;
            }

            rt.localScale = Vector3.one;
            DropClaimedOverlay();
            _claimedPopRoutine = null;
            onComplete?.Invoke();
        }

        /// <summary>연출 동안 획득 완료 표시를 같은 캔버스의 다른 UI 위로 올린다(중첩 Canvas의 정렬 덮어쓰기).
        /// 슬롯이 그리드로 나열되는 화면(출석부 달력)에서는 슬롯 밖까지 커진 체크가 뒤에 배치된 이웃 칸에
        /// 가려지므로, 계층/형제 순서를 건드리지 않고(레이아웃 그룹이 자리를 재배치하지 않게) 정렬만
        /// 임시로 끌어올린다. 여기서 붙인 Canvas는 <see cref="DropClaimedOverlay"/>가 제거한다.</summary>
        private void LiftClaimedOverlay()
        {
            var parentCanvas = _claimedOverlay.canvas;
            if (parentCanvas == null || _claimedLift != null || _claimedOverlay.GetComponent<Canvas>() != null)
            {
                return; // 캔버스 밖이거나 이미 자체 Canvas가 있으면 건드리지 않는다
            }
            _claimedLift = _claimedOverlay.gameObject.AddComponent<Canvas>();
            _claimedLift.overrideSorting = true;
            _claimedLift.sortingLayerID = parentCanvas.sortingLayerID;
            _claimedLift.sortingOrder = parentCanvas.sortingOrder + 1;
        }

        /// <summary><see cref="LiftClaimedOverlay"/>가 올린 정렬을 되돌린다(임시 Canvas 제거).
        /// 연출이 끝났을 때와 중간에 취소됐을 때 모두 호출된다.</summary>
        private void DropClaimedOverlay()
        {
            if (_claimedLift != null)
            {
                Destroy(_claimedLift);
                _claimedLift = null;
            }
        }

        /// <summary>재생 중인 획득 연출을 중단하고 크기·정렬을 원래 상태로 되돌린다
        /// (연출 재시작·상태 덮어쓰기·슬롯 재구성 공통 정리).</summary>
        private void CancelClaimedPop()
        {
            if (_claimedPopRoutine != null)
            {
                StopCoroutine(_claimedPopRoutine);
                _claimedPopRoutine = null;
            }
            if (_claimedOverlay != null)
            {
                _claimedOverlay.rectTransform.localScale = Vector3.one;
                DropClaimedOverlay();
            }
        }

        /// <summary>Setup 호출 시 획득 완료 표시를 기본 숨김 상태로 되돌린다(호출측이 필요 시 SetClaimed로 재설정).</summary>
        private void ResetClaimedOverlay()
        {
            CancelClaimedPop();
            if (_claimedOverlay != null)
            {
                _claimedOverlay.gameObject.SetActive(false);
            }
        }

        /// <summary>hover 진입: 공용 상세 팝업을 커서 근처에 표시한다.</summary>
        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_showDetail)
            {
                ItemDetailPopup.Show(_detailBackgroundSprite, _itemCode, _quantity, _enhanceLevel, eventData.position);
            }
        }

        /// <summary>hover 이탈: 상세 팝업을 숨긴다.</summary>
        public void OnPointerExit(PointerEventData eventData)
        {
            if (_showDetail)
            {
                ItemDetailPopup.Hide();
            }
        }

        /// <summary>에디터 빌드 전용: 위젯 참조와 상세 배경 스프라이트를 배선한다(ItemSlotBuilder).</summary>
        public void EditorInit(Image frame, Image gradeBackground, Image icon, Text quantity, Text iconLabel,
            Image claimedOverlay, Sprite detailBackground)
        {
            _frameImage = frame;
            _gradeBackground = gradeBackground;
            _iconImage = icon;
            _quantityText = quantity;
            _iconLabelText = iconLabel;
            _claimedOverlay = claimedOverlay;
            _detailBackgroundSprite = detailBackground;
        }
    }
}
