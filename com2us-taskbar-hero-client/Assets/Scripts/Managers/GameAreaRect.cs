using UnityEngine;

namespace TaskbarHero.Client.Managers
{
    /// <summary>
    /// 이 RectTransform을 <b>전투 화면 밴드</b>(<see cref="GameViewLayout"/>의 가운데 구간)에만 걸치도록
    /// 유지하는 컨테이너 컴포넌트. 화면 가장자리에 붙는 UI(HUD 손잡이·버프 아이콘·파티 스킬 UI·진행 바)를
    /// 이 밑에 두면, GameScene 창이 좌우 패널 여백만큼 넓어져도 전투 화면 안쪽에 남는다.
    ///
    /// <para>레이아웃이 적용되지 않는 씬(BattleDevScene 등)에서는 <b>캔버스 전체 스트레치</b>로 되돌리므로,
    /// 같은 계층을 쓰는 개발용 씬의 배치가 달라지지 않는다. 씬이 바뀔 때마다
    /// <see cref="GameViewLayout"/>이 이 컴포넌트들을 다시 갱신한다.</para>
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class GameAreaRect : MonoBehaviour
    {
        private RectTransform _rect;

        /// <summary>주어진 RectTransform에 이 컴포넌트를 붙이고 즉시 적용한다(이미 있으면 재사용).</summary>
        public static GameAreaRect Attach(RectTransform rt)
        {
            if (rt == null)
            {
                return null;
            }
            var area = rt.GetComponent<GameAreaRect>();
            if (area == null)
            {
                area = rt.gameObject.AddComponent<GameAreaRect>();
            }
            area.Apply();
            return area;
        }

        private void Awake()
        {
            Apply();
        }

        private void OnEnable()
        {
            Apply();
        }

        /// <summary>현재 씬의 레이아웃 상태에 맞춰 앵커를 전투 밴드 또는 전체 스트레치로 맞춘다.</summary>
        public void Apply()
        {
            if (_rect == null)
            {
                _rect = GetComponent<RectTransform>();
            }
            if (GameViewLayout.LayoutActive)
            {
                GameViewLayout.ApplyGameArea(_rect);
            }
            else
            {
                GameViewLayout.ApplyFullArea(_rect);
            }
        }
    }
}
