using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// hover 진입/이탈만 콜백으로 넘기는 가벼운 이벤트 중계기.
    /// <para>
    /// <b>스크롤 목록 안의 행에는 <see cref="EventTrigger"/>를 쓰면 안 된다.</b> EventTrigger는 드래그를 포함한
    /// <i>모든</i> 이벤트 인터페이스를 구현하므로, 행 위에서 누른 채 끌면 그 드래그를 자기가 삼켜
    /// 부모 <c>ScrollRect</c>까지 전달되지 않는다(= 행을 잡고는 스크롤이 안 되는 증상).
    /// 이 컴포넌트는 <see cref="IPointerEnterHandler"/>·<see cref="IPointerExitHandler"/>만 구현해
    /// 드래그를 건드리지 않으므로 스크롤이 정상 동작한다.
    /// </para>
    /// </summary>
    public class PointerHoverRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private Action<PointerEventData> _onEnter;
        private Action _onExit;

        /// <summary>hover 진입·이탈 콜백을 등록한다.</summary>
        public void Bind(Action<PointerEventData> onEnter, Action onExit)
        {
            _onEnter = onEnter;
            _onExit = onExit;
        }

        public void OnPointerEnter(PointerEventData eventData) => _onEnter?.Invoke(eventData);

        public void OnPointerExit(PointerEventData eventData) => _onExit?.Invoke();
    }
}
