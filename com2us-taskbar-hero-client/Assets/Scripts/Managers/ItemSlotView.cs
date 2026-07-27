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
        [Tooltip("상세 팝업 배경(Assets/Art/UI/item_detail_bg). 없으면 단색 팝업.")]
        [SerializeField] private Sprite _detailBackgroundSprite;

        private int _itemCode;
        private long _quantity;
        private bool _showDetail;

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
                _iconImage.sprite = icon;
                _iconImage.color = icon != null
                    ? Color.white
                    : GradeColors.IconFallback(grade) * new Color(1f, 1f, 1f, 0.6f); // 아이콘 없을 때 색 폴백
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
        public void EditorInit(Image frame, Image gradeBackground, Image icon, Text quantity, Sprite detailBackground)
        {
            _frameImage = frame;
            _gradeBackground = gradeBackground;
            _iconImage = icon;
            _quantityText = quantity;
            _detailBackgroundSprite = detailBackground;
        }
    }
}
