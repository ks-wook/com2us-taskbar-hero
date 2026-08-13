using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// SPUM 캐릭터 애니메이션 헬퍼. SPUM_Prefabs가 Assembly-CSharp(asmdef 없음)에 있어
/// asmdef 어셈블리에서 직접 참조할 수 없으므로, 같은 Assembly-CSharp에 두고
/// 외부(CharacterSelectManager·BattleDevController 등)에서는 SendMessage(...)로 호출한다.
///
/// 시작 시 애니메이터를 초기화(IDLE 재생)하고, PlayAttackOnce()/PlayDamagedOnce()/PlayDeathOnce()로
/// 그 캐릭터 고유의 상태 애니메이션을 재생한다(SPUM 애니메이터가 트리거 후 IDLE로 복귀).
/// 플레이어(Knight 등)와 몬스터(SPUM BasicPack)가 공용으로 쓴다.
/// </summary>
[RequireComponent(typeof(SPUM_Prefabs))]
public class SpumCharacterAnimator : MonoBehaviour
{
    [Tooltip("돌진(방패 돌진) 시 재생할 웅크린 자세 애니메이션. MOVE 상태 클립을 이 클립으로 오버라이드해 재생한다.")]
    public AnimationClip chargeDashClip;

    [Tooltip("버프 스킬(기사의 분노) 발동 시 재생할 분노/기합 자세 애니메이션. OTHER 상태 클립을 오버라이드해 1회 재생 후 IDLE로 복귀한다.")]
    public AnimationClip rageClip;

    private SPUM_Prefabs _spum;
    private bool _initialized;
    private AnimationClip _savedMoveClip;   // 돌진 중 MOVE 오버라이드 전 원본 걷기 클립
    private int _rageIndex = -1;            // ATTACK_List에 추가한 분노 클립 인덱스(1회 추가)
    private Coroutine _backJump;            // 진행 중인 뒤로 점프(겹치면 이전 것을 버린다)

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

        // 재귀 방지: PlayIdle()이 다시 EnsureInitialized()를 부르므로,
        // 여기서는 플래그를 먼저 세우고 IDLE을 직접 재생한다.
        _initialized = true;
        Play(PlayerState.IDLE, _spum.IDLE_List);
    }

    /// <summary>IDLE 상태로 되돌린다(리스폰·초기화).</summary>
    public void PlayIdle()
    {
        EnsureInitialized();
        Play(PlayerState.IDLE, _spum != null ? _spum.IDLE_List : null);
    }

    /// <summary>이동(MOVE) 애니메이션을 재생한다(루프). 상태 진입 시 1회 호출한다.</summary>
    public void PlayMove()
    {
        EnsureInitialized();
        Play(PlayerState.MOVE, _spum != null ? _spum.MOVE_List : null);
    }

    /// <summary>이 캐릭터 고유의 공격 애니메이션을 1회 재생한다(ATTACK_List[0]).</summary>
    public void PlayAttackOnce()
    {
        EnsureInitialized();
        Play(PlayerState.ATTACK, _spum != null ? _spum.ATTACK_List : null);
    }

    /// <summary>
    /// ATTACK_List에서 이름에 <paramref name="contains"/>가 포함된 공격 클립을 찾아 재생한다.
    /// 예: 레인저는 "Bow"(활 공격, 0_Attack_Bow). 없으면 index 0.
    /// </summary>
    public void PlayAttackByName(string contains)
    {
        EnsureInitialized();
        if (_spum == null || _spum.ATTACK_List == null || _spum.ATTACK_List.Count == 0)
        {
            return;
        }

        int idx = 0;
        if (!string.IsNullOrEmpty(contains))
        {
            string key = contains.ToLower();
            for (int i = 0; i < _spum.ATTACK_List.Count; i++)
            {
                var c = _spum.ATTACK_List[i];
                if (c != null && c.name.ToLower().Contains(key))
                {
                    idx = i;
                    break;
                }
            }
        }
        _spum.PlayAnimation(PlayerState.ATTACK, idx);
    }

    /// <summary>피격 애니메이션을 1회 재생한다(없으면 무시).</summary>
    public void PlayDamagedOnce()
    {
        EnsureInitialized();
        Play(PlayerState.DAMAGED, _spum != null ? _spum.DAMAGED_List : null);
    }

    /// <summary>사망 애니메이션을 재생한다(없으면 무시).</summary>
    public void PlayDeathOnce()
    {
        EnsureInitialized();
        Play(PlayerState.DEATH, _spum != null ? _spum.DEATH_List : null);
    }

    /// <summary>
    /// 돌진(웅크린 자세) 애니메이션을 재생한다. MOVE 상태의 클립을 <see cref="chargeDashClip"/>으로
    /// 오버라이드하고 MOVE를 재생해(1_Move=true) 돌진 내내 자세를 유지한다.
    /// <see cref="StopChargeDash"/>로 원래 걷기 클립을 복원한다.
    /// </summary>
    public void PlayChargeDash()
    {
        EnsureInitialized();
        if (_spum == null)
        {
            return;
        }

        if (chargeDashClip == null)
        {
            Play(PlayerState.MOVE, _spum.MOVE_List); // 폴백: 걷기
            return;
        }

        var pairs = _spum.StateAnimationPairs;
        if (pairs != null && pairs.TryGetValue("MOVE", out var moveList) && moveList != null && moveList.Count > 0)
        {
            if (_savedMoveClip == null)
            {
                _savedMoveClip = moveList[0];
            }
            moveList[0] = chargeDashClip;
            _spum.PlayAnimation(PlayerState.MOVE, 0); // MOVE 상태 유지(크라우치 클립)
        }
    }

    /// <summary>
    /// 뒤로 <b>도약</b>한다 — <paramref name="secondsAndHeight"/>.x초 동안 y로 포물선을 그렸다 제자리 높이로
    /// 착지한다(.y = 정점 높이, 월드 유닛). <b>시간·높이를 호출측이 넘기는 이유</b>: 이 값들은 방패 돌진의
    /// 뒤로 물러나는 거리·시간과 한 벌로 맞춰야 하므로 <c>PlayerCombatant</c>에 모아 둔다. 인스펙터 필드로
    /// 두면 프리팹마다 다른 값이 구워져(기사 0.45 / 몬스터 0.7로 갈렸던 실제 사례) 조용히 어긋난다.
    /// 방패 돌진의 준비 동작(뒤로 물러나기)에 얹어 "주춤 → 튀어나감"이 도약으로 읽히게 하는 연출이다.
    /// <para>뒤로 가는 <b>x 이동은 <c>PlayerCombatant</c>가</b> 같은 시간 동안 처리하므로 여기서는 y만 건드린다
    /// (둘이 같은 transform을 서로 다른 축으로 나눠 쓴다). SPUM에는 점프 클립이 없어, 내려찍기
    /// (<see cref="PlayGroundSlam"/>)와 같은 방식으로 transform을 움직여 도약을 만든다 —
    /// 자세는 이 시점에 이미 걸려 있는 돌진 크라우치(<see cref="PlayChargeDash"/>)가 맡는다.</para>
    /// <para>착지 높이를 <b>시작 높이로</b> 되돌리므로, 이어지는 돌진이 y를 PathY로 되잡을 때 튐이 없다.</para>
    /// </summary>
    public void PlayBackJump(Vector2 secondsAndHeight)
    {
        EnsureInitialized();
        if (_spum == null)
        {
            return;
        }
        if (_backJump != null)
        {
            StopCoroutine(_backJump);
        }
        _backJump = StartCoroutine(BackJumpRoutine(secondsAndHeight.x, secondsAndHeight.y));
    }

    private IEnumerator BackJumpRoutine(float seconds, float height)
    {
        float dur = Mathf.Max(0.05f, seconds);
        float baseY = transform.position.y;
        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            // sin(0→π): 0에서 떠올라 정점을 찍고 다시 0으로 — 한 번의 도약.
            float h = Mathf.Sin(Mathf.Clamp01(t / dur) * Mathf.PI) * height;
            var p = transform.position;
            p.y = baseY + h;
            transform.position = p;
            yield return null;
        }
        {
            var p = transform.position;
            p.y = baseY;
            transform.position = p;
        }
        _backJump = null;
    }

    /// <summary>돌진 애니메이션을 끝내고 걷기 클립을 복원한 뒤 IDLE로 되돌린다.</summary>
    public void StopChargeDash()
    {
        if (_spum != null && _savedMoveClip != null)
        {
            var pairs = _spum.StateAnimationPairs;
            if (pairs != null && pairs.TryGetValue("MOVE", out var moveList) && moveList != null && moveList.Count > 0)
            {
                moveList[0] = _savedMoveClip;
            }
            _savedMoveClip = null;
        }

        // 크라우치 클립이 애니메이터 GameObject(UnitRoot)의 transform을 바꿔놨고
        // IDLE/MOVE 클립은 이 transform을 애니메이션하지 않으므로 수동으로 원복한다.
        if (_spum != null && _spum._anim != null)
        {
            var at = _spum._anim.transform;
            at.localScale = Vector3.one;
            at.localRotation = Quaternion.identity;
        }

        PlayIdle();
    }

    /// <summary>
    /// 버프(기사의 분노) 발동용 분노/기합 애니메이션을 1회 재생한다.
    /// SPUM의 OTHER 상태는 이 캐릭터 애니메이터에 슬롯/트리거가 없어(=idle로 떨어짐) 쓸 수 없으므로,
    /// 검증된 ATTACK 경로를 재사용한다: <see cref="rageClip"/>을 ATTACK_List 끝에 1회 추가하고
    /// 그 인덱스로 ATTACK을 재생한다(기본공격 index 0은 건드리지 않음). ATTACK은 1회 재생 후 IDLE로 복귀하며,
    /// rageClip은 마지막 프레임이 원점(scale 1/rot 0/pos 0)이라 잔상 없이 원복된다.
    /// rageClip이 없으면 기본 공격 모션으로 폴백한다.
    /// </summary>
    public void PlayRage()
    {
        EnsureInitialized();
        if (_spum == null)
        {
            return;
        }
        if (rageClip == null || _spum.ATTACK_List == null || _spum.ATTACK_List.Count == 0)
        {
            PlayAttackOnce();
            return;
        }

        if (_rageIndex < 0 || _rageIndex >= _spum.ATTACK_List.Count || _spum.ATTACK_List[_rageIndex] != rageClip)
        {
            _spum.ATTACK_List.Add(rageClip);
            _rageIndex = _spum.ATTACK_List.Count - 1;
        }
        _spum.PlayAnimation(PlayerState.ATTACK, _rageIndex); // 1회 재생 후 IDLE 복귀
    }

    [Tooltip("캐스터 스킬 홀드 시 정지할 공격 애니 정규화 시점(0~1). 손을 든 프레임")]
    public float castHoldNormalizedTime = 0.14f;

    /// <summary>
    /// 캐스터(마법사) 스킬용: 공격(마법) 애니를 재생하다 손 든 프레임(<see cref="castHoldNormalizedTime"/>)에서
    /// 정지(홀드)하고, <paramref name="holdSeconds"/> 뒤 재개해 애니를 마무리하며 IDLE(차렷)로 복귀한다.
    /// 정지 중에도 스킬 이펙트(별도 GameObject)는 자기 시간축으로 재생된다.
    /// </summary>
    public void PlayCastHold(float holdSeconds)
    {
        EnsureInitialized();
        if (_spum == null || _spum._anim == null) return;
        // 코루틴 핸들로 중단한다 — StopCoroutine(문자열)은 StartCoroutine(문자열)로 시작한 것만 멈추므로
        // 이전 홀드가 살아남아 speed를 0/1로 서로 엎어쓰다 애니메이터가 멈춘 채 남을 수 있다.
        if (_castHold != null)
        {
            StopCoroutine(_castHold);
            _spum._anim.speed = 1f; // 중단 시 정지 상태로 남지 않게 반드시 복구
        }
        _castHold = StartCoroutine(CastHoldRoutine(holdSeconds));
    }

    private Coroutine _castHold;

    private IEnumerator CastHoldRoutine(float holdSeconds)
    {
        var anim = _spum._anim;

        // 마법(또는 index 0) 공격 애니 재생
        int idx = 0;
        if (_spum.ATTACK_List != null)
        {
            for (int i = 0; i < _spum.ATTACK_List.Count; i++)
            {
                var c = _spum.ATTACK_List[i];
                if (c != null && c.name.ToLower().Contains("magic")) { idx = i; break; }
            }
        }
        anim.speed = 1f;
        _spum.PlayAnimation(PlayerState.ATTACK, idx);

        // 공격 상태로 진입(트랜지션 종료 + 공격 클립이 현재)할 때까지 대기
        float g = 0f;
        while (g < 1f)
        {
            g += Time.unscaledDeltaTime;
            if (!anim.IsInTransition(0))
            {
                var ci = anim.GetCurrentAnimatorClipInfo(0);
                if (ci != null && ci.Length > 0 && ci[0].clip != null && ci[0].clip.name.ToLower().Contains("attack"))
                {
                    break;
                }
            }
            yield return null;
        }

        // 손 든 프레임으로 강제 이동 후 정지(캡처로 검증된 방식)
        float target = Mathf.Clamp01(castHoldNormalizedTime);
        int hash = anim.GetCurrentAnimatorStateInfo(0).fullPathHash;
        anim.Play(hash, 0, target);
        anim.Update(0f);
        anim.speed = 0f;

        yield return new WaitForSeconds(Mathf.Max(0.05f, holdSeconds));

        anim.speed = 1f; // 재개 → 공격 애니 마무리 후 IDLE(차렷) 복귀
        _castHold = null;
    }

    [Tooltip("화살비: 점프 높이(유닛)")]
    public float arrowRainJumpHeight = 1.4f;
    [Tooltip("화살비: 활을 하늘로 든 정지 프레임(정규화 0~1). 활 클립상 오른팔이 최대로 올라가는 시점")]
    public float arrowRainBowNormalizedTime = 0.62f;
    [Tooltip("화살비: 점프/착지 각 소요 시간(초)")]
    public float arrowRainJumpTime = 0.12f;
    [Tooltip("화살비: 정점에서의 크기 배율(점프하며 커졌다 착지 시 원상복귀)")]
    public float arrowRainPeakScale = 1.3f;
    [Tooltip("화살비: 점프 중 다른 오브젝트에 가리지 않도록 올릴 정렬 순서(SortingGroup)")]
    public int arrowRainFrontOrder = 1000;

    /// <summary>
    /// 레인저 화살비용: 위로 점프하며 활 공격 애니를 재생하다, 정점에서 활을 하늘로 든 프레임
    /// (<see cref="arrowRainBowNormalizedTime"/>)에서 정지(홀드)한다. holdSeconds 뒤 재개하며 착지한다.
    /// 대상 위치의 화살비 이펙트/데미지는 PlayerCombatant가 담당(정지 중에도 이펙트는 자기 시간축 재생).
    /// </summary>
    public void PlayArrowRain(float holdSeconds)
    {
        EnsureInitialized();
        if (_spum == null || _spum._anim == null) return;
        // 문자열 StopCoroutine은 무효다(PlayCastHold와 같은 이유) — 핸들로 중단하고 speed를 복구한다.
        if (_arrowRain != null)
        {
            StopCoroutine(_arrowRain);
            _spum._anim.speed = 1f;
        }
        _arrowRain = StartCoroutine(ArrowRainRoutine(holdSeconds));
    }

    private Coroutine _arrowRain;

    private IEnumerator ArrowRainRoutine(float holdSeconds)
    {
        var anim = _spum._anim;
        float baseY = transform.position.y;
        float apexY = baseY + arrowRainJumpHeight;
        Vector3 baseScale = transform.localScale;
        Vector3 peakScale = baseScale * Mathf.Max(1f, arrowRainPeakScale); // 부호(좌우 반전) 유지

        // 점프 동안 다른 캐릭터/오브젝트에 가리지 않도록 정렬 순서를 최상단으로
        var sg = GetComponentInChildren<SortingGroup>();
        int origOrder = sg != null ? sg.sortingOrder : 0;
        if (sg != null) sg.sortingOrder = arrowRainFrontOrder;

        // 활 공격 애니 재생
        int idx = 0;
        if (_spum.ATTACK_List != null)
        {
            for (int i = 0; i < _spum.ATTACK_List.Count; i++)
            {
                var c = _spum.ATTACK_List[i];
                if (c != null && c.name.ToLower().Contains("bow")) { idx = i; break; }
            }
        }
        anim.speed = 1f;
        _spum.PlayAnimation(PlayerState.ATTACK, idx);

        // 1) 점프 업(애니 재생하며 위로). 상승 중 공격 상태 진입 대기 겸용.
        float jt = 0f;
        float jd = Mathf.Max(0.01f, arrowRainJumpTime);
        while (jt < jd)
        {
            jt += Time.deltaTime;
            float k = Mathf.Sin(Mathf.Clamp01(jt / jd) * Mathf.PI * 0.5f); // ease-out 상승
            var p = transform.position; p.y = Mathf.Lerp(baseY, apexY, k); transform.position = p;
            transform.localScale = Vector3.Lerp(baseScale, peakScale, k); // 오르며 점점 커짐
            yield return null;
        }
        { var p = transform.position; p.y = apexY; transform.position = p; }
        transform.localScale = peakScale;

        // 공격 상태로 확실히 진입할 때까지(트랜지션 종료 + 공격 클립) 잠깐 더 대기
        float g = 0f;
        while (g < 0.5f)
        {
            if (!anim.IsInTransition(0))
            {
                var ci = anim.GetCurrentAnimatorClipInfo(0);
                if (ci != null && ci.Length > 0 && ci[0].clip != null && ci[0].clip.name.ToLower().Contains("attack"))
                    break;
            }
            g += Time.deltaTime;
            yield return null;
        }

        // 2) 정점에서 활을 하늘로 든 프레임으로 강제 이동 후 정지
        int hash = anim.GetCurrentAnimatorStateInfo(0).fullPathHash;
        anim.Play(hash, 0, Mathf.Clamp01(arrowRainBowNormalizedTime));
        anim.Update(0f);
        anim.speed = 0f;

        // 3) 공중 유지(화살비 지속)
        yield return new WaitForSeconds(Mathf.Max(0.05f, holdSeconds));

        // 4) 재개 + 착지
        anim.speed = 1f;
        float lt = 0f;
        float ld = Mathf.Max(0.01f, arrowRainJumpTime);
        while (lt < ld)
        {
            lt += Time.deltaTime;
            float k = Mathf.Clamp01(lt / ld);
            var p = transform.position; p.y = Mathf.Lerp(apexY, baseY, k); transform.position = p;
            transform.localScale = Vector3.Lerp(peakScale, baseScale, k); // 내려오며 원래 크기로
            yield return null;
        }
        { var p = transform.position; p.y = baseY; transform.position = p; }
        transform.localScale = baseScale; // 원래 크기 복원
        if (sg != null) sg.sortingOrder = origOrder; // 정렬 순서 복원
        _arrowRain = null;
    }

    [Tooltip("내려찍기: 점프 높이(유닛)")]
    public float groundSlamJumpHeight = 1.6f;
    [Tooltip("내려찍기: 공중 체류 시간 중 상승에 쓰는 비율(0~1). 나머지가 낙하 — 낙하가 짧을수록 내리찍는 느낌이 강해진다")]
    public float groundSlamRiseFraction = 0.72f;
    [Tooltip("내려찍기: 정점에서의 크기 배율(오르며 커졌다 착지 시 원상복귀)")]
    public float groundSlamPeakScale = 1.25f;
    [Tooltip("내려찍기: 점프 중 다른 오브젝트에 가리지 않도록 올릴 정렬 순서(SortingGroup)")]
    public int groundSlamFrontOrder = 1000;
    [Tooltip("내려찍기: 정점에서 정지할 공격 애니 정규화 시점(0~1). 도끼를 가장 높이 치켜든 프레임. "
             + "0_Attack_Normal은 클립을 샘플링해 도끼(R_Weapon) 최상단을 재면 nt 0.50에서 최대(1.07)이고 "
             + "바로 뒤 nt 0.60에서 최저(0.35)로 내리친다 → 0.50에서 정지하면 재개 즉시 내리치는 동작이 이어진다")]
    public float groundSlamRaiseNormalizedTime = 0.50f;

    /// <summary>
    /// 내려찍기용: 공격 애니메이션을 재생하며 위로 솟구쳤다가(오르며 커짐), 정점에서
    /// **도끼를 치켜든 프레임(<see cref="groundSlamRaiseNormalizedTime"/>)으로 정지**한 뒤 그 자세로 빠르게 낙하하고,
    /// 착지하는 순간 재생을 재개해 내리치는 구간을 이어서 보여준다.
    /// 착지 시점의 이펙트/데미지는 <c>PlayerCombatant</c>가 같은 airTime으로 맞춰 처리한다.
    /// </summary>
    public void PlayGroundSlam(float airTime)
    {
        EnsureInitialized();
        if (_spum == null || _spum._anim == null) return;
        StopCoroutine(nameof(GroundSlamRoutine));
        StartCoroutine(GroundSlamRoutine(airTime));
    }

    private IEnumerator GroundSlamRoutine(float airTime)
    {
        var anim = _spum._anim;
        float air = Mathf.Max(0.08f, airTime);
        float riseDur = Mathf.Max(0.04f, air * Mathf.Clamp(groundSlamRiseFraction, 0.2f, 0.9f));
        float fallDur = Mathf.Max(0.03f, air - riseDur);

        float baseY = transform.position.y;
        float apexY = baseY + groundSlamJumpHeight;
        Vector3 baseScale = transform.localScale;
        Vector3 peakScale = baseScale * Mathf.Max(1f, groundSlamPeakScale); // 부호(좌우 반전) 유지

        // 점프 동안 다른 캐릭터/오브젝트에 가리지 않도록 정렬 순서를 최상단으로
        var sg = GetComponentInChildren<SortingGroup>();
        int origOrder = sg != null ? sg.sortingOrder : 0;
        if (sg != null) sg.sortingOrder = groundSlamFrontOrder;

        // 상승 동안 클립이 '치켜든 프레임'을 **넘어가지 않도록** 재생 속도를 맞춘다.
        // speed 1로 두면 상승(riseDur)이 끝나기 전에 내리치는 구간까지 지나가 버려서
        // 공중에서 한 번 찍고 착지 후 또 한 번 찍는 이중 스윙이 된다.
        float clipLen = (_spum.ATTACK_List != null && _spum.ATTACK_List.Count > 0
                         && _spum.ATTACK_List[0] != null) ? _spum.ATTACK_List[0].length : 0f;
        float raiseNt = Mathf.Clamp01(groundSlamRaiseNormalizedTime);
        anim.speed = (clipLen > 0f && raiseNt > 0f)
            ? Mathf.Clamp(clipLen * raiseNt / riseDur, 0.01f, 3f) // 하한을 낮게 — 상승이 길어도 넘어가지 않게
            : 1f;
        _spum.PlayAnimation(PlayerState.ATTACK, 0);

        // 1) 상승 — ease-out으로 솟구치며 점점 커진다.
        //    상승 중에 공격 상태(트랜지션 종료)로 진입한 시점의 상태 해시를 잡아둔다
        //    (정점에서 프레임을 강제 이동하려면 필요하고, 여기서 잡아두면 추가 대기가 없어 타이밍이 안 밀린다).
        int attackHash = 0;
        float t = 0f;
        while (t < riseDur)
        {
            t += Time.deltaTime;
            float k = Mathf.Sin(Mathf.Clamp01(t / riseDur) * Mathf.PI * 0.5f);
            var p = transform.position; p.y = Mathf.Lerp(baseY, apexY, k); transform.position = p;
            transform.localScale = Vector3.Lerp(baseScale, peakScale, k);

            if (attackHash == 0 && !anim.IsInTransition(0))
            {
                var ci = anim.GetCurrentAnimatorClipInfo(0);
                if (ci != null && ci.Length > 0 && ci[0].clip != null
                    && ci[0].clip.name.ToLower().Contains("attack"))
                {
                    attackHash = anim.GetCurrentAnimatorStateInfo(0).fullPathHash;
                }
            }
            yield return null;
        }
        { var p = transform.position; p.y = apexY; transform.position = p; }
        transform.localScale = peakScale;

        // 2) 정점에서 도끼를 치켜든 프레임으로 정확히 맞춘 뒤 정지
        //    (상승 중 트랜지션 지연으로 조금 덜 진행됐을 수 있어 프레임을 강제 지정한다)
        if (attackHash != 0)
        {
            anim.Play(attackHash, 0, raiseNt);
            anim.Update(0f);
        }
        anim.speed = 0f;

        // 3) 낙하 — 치켜든 자세를 그대로 유지한 채 ease-in(가속)으로 짧고 빠르게 떨어지며 원래 크기로 복귀
        t = 0f;
        while (t < fallDur)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / fallDur);
            float k = u * u;                       // 가속 낙하
            var p = transform.position; p.y = Mathf.Lerp(apexY, baseY, k); transform.position = p;
            transform.localScale = Vector3.Lerp(peakScale, baseScale, k);
            yield return null;
        }
        { var p = transform.position; p.y = baseY; transform.position = p; }
        transform.localScale = baseScale;           // 원래 크기 복원

        // 4) 착지 — 재생 재개. 정지해 둔 지점부터 내리치는 구간이 이어서 재생되고 IDLE로 복귀한다.
        anim.speed = 1f;
        if (sg != null) sg.sortingOrder = origOrder; // 정렬 순서 복원
    }

    private void Play(PlayerState state, System.Collections.Generic.List<AnimationClip> clips)
    {
        if (_spum != null && clips != null && clips.Count > 0)
        {
            _spum.PlayAnimation(state, 0);
        }
    }
}
