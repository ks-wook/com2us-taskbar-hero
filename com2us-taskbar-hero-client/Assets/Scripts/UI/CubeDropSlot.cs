using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TaskbarHero.Client.Managers;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 큐브의 <b>등록 칸</b> — 가방에서 아이템을 끌어다 놓는 빈 슬롯이다.
    /// 합성 3칸 · 연금술 9칸 · 강화 1칸이 모두 이 컴포넌트를 쓰고, 강화 탭의 <b>결과 미리보기</b> 칸은
    /// 드롭을 받지 않는 표시 전용으로 쓴다(<see cref="AcceptsDrop"/> = false).
    ///
    /// <para><b>드롭 경로</b> — 가방 아이템(<see cref="InventoryItemView"/>)이 드래그를 끝낼 때 포인터 아래에서
    /// 이 컴포넌트를 찾아 <see cref="CubePanelController.TryRegisterFromInventory"/>로 넘긴다.
    /// 등록 가능 여부(탭별 조건)는 컨트롤러가 판단한다 — 슬롯은 표시와 클릭 해제만 맡는다.</para>
    ///
    /// <para>칸을 <b>클릭하면 등록이 해제</b>된다. 아이템 그림은 다른 화면과 같은 공용 슬롯
    /// (<see cref="ItemSlotView"/>)으로 그려 외형을 통일한다.</para>
    /// </summary>
    public class CubeDropSlot : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private Image _frame;          // 빈 칸 테두리(등록되면 강조색)
        [SerializeField] private Text _placeholder;     // 빈 칸 안내("+")
        [SerializeField] private ItemSlotView _slotView; // 등록된 아이템 그림

        private CubePanelController _controller;
        private long _itemId;      // 등록된 가방 아이템 id(0 = 비어 있음)
        private int _index;        // 이 칸의 순번(탭 안에서)

        /// <summary>이 칸이 드롭을 받는지(결과 미리보기 칸은 false).</summary>
        public bool AcceptsDrop { get; private set; } = true;

        /// <summary>등록된 아이템 id(0이면 빈 칸).</summary>
        public long ItemId => _itemId;

        /// <summary>탭 안에서의 순번.</summary>
        public int Index => _index;

        private static readonly Color EmptyFrame = new Color(1f, 1f, 1f, 0.28f);
        private static readonly Color FilledFrame = new Color(0.98f, 0.82f, 0.35f, 0.95f);
        private static readonly Color HoverFrame = new Color(0.55f, 0.85f, 1f, 0.95f);

        /// <summary>구성 직후 1회 배선한다(컨트롤러가 칸을 만들면서 호출).</summary>
        public void Initialize(CubePanelController controller, int index, bool acceptsDrop,
                               Image frame, Text placeholder, ItemSlotView slotView)
        {
            _controller = controller;
            _index = index;
            AcceptsDrop = acceptsDrop;
            _frame = frame;
            _placeholder = placeholder;
            _slotView = slotView;
            SetEmpty();
        }

        /// <summary>칸을 비운다(빈 테두리 + 안내 표시).</summary>
        public void SetEmpty()
        {
            _itemId = 0;
            if (_slotView != null)
            {
                _slotView.gameObject.SetActive(false);
            }
            if (_placeholder != null)
            {
                _placeholder.gameObject.SetActive(true);
            }
            if (_frame != null)
            {
                _frame.color = EmptyFrame;
            }
        }

        /// <summary>칸에 아이템을 표시한다. <paramref name="itemId"/> 0은 <b>표시 전용</b>(결과 미리보기)이다.</summary>
        public void SetItem(long itemId, int itemCode, int enhanceLevel, string quantityText)
        {
            _itemId = itemId;
            if (_slotView != null)
            {
                _slotView.gameObject.SetActive(true);
                // 칸이 이미 자기 테두리를 그리므로 슬롯 프레임은 감추고, 클릭은 이 칸이 받는다(showDetail = false).
                _slotView.Setup(itemCode, 1L, quantityText, false);
                _slotView.SetFrameVisible(false);
                _slotView.SetEnhanceLevel(enhanceLevel);
            }
            if (_placeholder != null)
            {
                _placeholder.gameObject.SetActive(false);
            }
            if (_frame != null)
            {
                _frame.color = FilledFrame;
            }
        }

        /// <summary>
        /// 클릭하면 칸을 비운다(좌·우 클릭 모두).
        /// 등록 칸은 등록을 해제하고, <b>결과 칸</b>(강화 후 표시)은 그 표시를 치운다.
        /// 빈 칸은 아무 일도 하지 않는다.
        /// </summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (_controller == null)
            {
                return;
            }

            if (!AcceptsDrop)
            {
                // 결과 칸 — 아이템 id가 없으므로(표시 전용) 그림이 떠 있는지로 판단한다.
                if (_slotView == null || !_slotView.gameObject.activeSelf)
                {
                    return;
                }
                SoundManager.Sfx(SoundId.UiSlotSelect);
                _controller.ClearEnhanceResultDisplay();
                return;
            }

            if (_itemId == 0)
            {
                return;
            }
            SoundManager.Sfx(SoundId.UiSlotSelect);
            _controller.UnregisterSlotItem(_itemId);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (AcceptsDrop && _frame != null)
            {
                _frame.color = HoverFrame;
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_frame != null)
            {
                _frame.color = _itemId != 0 ? FilledFrame : EmptyFrame;
            }
        }
    }
}
