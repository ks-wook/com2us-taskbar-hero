using UnityEngine;

namespace TaskbarHero.Client.Managers
{
    /// <summary>
    /// 버튼 하나의 클릭음 상태 표식(<see cref="UiClickSound"/> 전용). 버튼에 붙여 두므로
    /// <b>버튼이 파괴되면 함께 사라진다</b> — 전역 스캔이 인스턴스 ID 목록을 들고 있지 않아도 되고
    /// (목록 방식은 목록에 파괴된 항목이 쌓인다) 프리팹 인스턴스마다 독립적으로 판정된다.
    /// </summary>
    [DisallowMultipleComponent]
    public class UiClickSoundTag : MonoBehaviour
    {
        /// <summary>이 버튼이 낼 클릭음. <see cref="SoundId.None"/>이면 전역 클릭음을 내지 않는다(제외 등록).</summary>
        public SoundId sound = SoundId.UiClick;

        /// <summary>이미 <c>onClick</c>에 클릭음을 붙였는지(중복 등록 방지).</summary>
        public bool bound;
    }
}
