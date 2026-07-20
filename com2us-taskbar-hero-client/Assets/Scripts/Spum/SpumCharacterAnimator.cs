using UnityEngine;

/// <summary>
/// SPUM 캐릭터 애니메이션 헬퍼. SPUM_Prefabs가 Assembly-CSharp(asmdef 없음)에 있어
/// asmdef 어셈블리에서 직접 참조할 수 없으므로, 같은 Assembly-CSharp에 두고
/// 외부(CharacterSelectManager 등)에서는 SendMessage("PlayAttackOnce")로 호출한다.
///
/// 시작 시 애니메이터를 초기화(IDLE 재생)하고, PlayAttackOnce()로 그 캐릭터 고유의
/// 공격 애니메이션(ATTACK_List[0])을 1회 재생한다(SPUM 애니메이터가 트리거 후 IDLE로 복귀).
/// </summary>
[RequireComponent(typeof(SPUM_Prefabs))]
public class SpumCharacterAnimator : MonoBehaviour
{
    private SPUM_Prefabs _spum;
    private bool _initialized;

    private void Awake()
    {
        _spum = GetComponent<SPUM_Prefabs>();
    }

    private void Start()
    {
        EnsureInitialized();
    }

    private void EnsureInitialized()
    {
        if (_initialized || _spum == null || _spum._anim == null)
        {
            return;
        }

        if (!_spum.allListsHaveItemsExist())
        {
            _spum.PopulateAnimationLists();
        }

        _spum.OverrideControllerInit();

        if (_spum.IDLE_List != null && _spum.IDLE_List.Count > 0)
        {
            _spum.PlayAnimation(PlayerState.IDLE, 0);
        }

        _initialized = true;
    }

    /// <summary>이 캐릭터 고유의 공격 애니메이션을 1회 재생한다.</summary>
    public void PlayAttackOnce()
    {
        EnsureInitialized();

        if (_spum != null && _spum.ATTACK_List != null && _spum.ATTACK_List.Count > 0)
        {
            _spum.PlayAnimation(PlayerState.ATTACK, 0);
        }
    }
}
