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

        // 획득 연출: 체크 표시가 슬롯 밖으로 크게 부풀었다가 원래 크기로 잦아든다.
        private const float ClaimedPopExpandScale = 3f;   // 최대 크기(원래 크기 배수)
        private const float ClaimedPopDuration = 0.45f;   // 연출 전체 길이
        private const float ClaimedPopExpandRatio = 0.4f; // 전체 길이 중 커지는 구간의 비율(나머지는 되돌아오는 구간)

        private int _itemCode;
        private long _quantity;
        private bool _showDetail;
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
            }
            if (_iconLabelText != null)
            {
                _iconLabelText.gameObject.SetActive(false); // 아이콘 모드에서는 텍스트 라벨 숨김(SetupLabel 전용)
            }

            if (_quantityText != null)
            {
                _quantityText.text = quantityText ?? string.Empty;
                _quantityText.gameObject.SetActive(!string.IsNullOrEmpty(quantityText));
            }

            if (_frameImage != null)
            {
                _frameImage.raycastTarget = showDetail; // 상세 없음(골드 등)이면 입력 통과
            }

            ResetClaimedOverlay();
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
            }
            if (_frameImage != null)
            {
                _frameImage.raycastTarget = false;
            }

            ResetClaimedOverlay();
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
                ItemDetailPopup.Show(_detailBackgroundSprite, _itemCode, _quantity, eventData.position);
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
