using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TaskbarHero.Client.Battle;
using TaskbarHero.Client.Managers;

/// <summary>
/// 캐릭터 프리팹 애니메이션 확인용 개발 하네스(AnimDevScene 전용).
///
/// <para>왼쪽 패널의 목록에서 애니메이션을 하나 고르고(선택만 함), <b>캐릭터를 클릭하면</b> 그 애니메이션이 재생된다.
/// 목록은 대상 프리팹의 <see cref="SPUM_Prefabs"/> 상태별 클립 리스트(IDLE·MOVE·ATTACK·DAMAGED·DEBUFF·DEATH·OTHER)를
/// 런타임에 그대로 읽어 만들므로, 프리팹의 클립이 늘거나 줄면 UI도 따라 바뀐다.
/// 여기에 <see cref="SpumCharacterAnimator"/>가 제공하는 특수 모션(분노·돌진·캐스트 홀드·화살비·내려찍기)도 함께 노출한다.</para>
///
/// <para>이 스크립트는 asmdef 없는 Assembly-CSharp에 둔다 — <see cref="SPUM_Prefabs"/>와
/// <see cref="SpumCharacterAnimator"/>가 같은 어셈블리에 있어 클립 목록을 직접 열거하려면 여기 있어야 한다
/// (asmdef 어셈블리에서는 SendMessage로만 호출할 수 있어 목록을 읽을 수 없다).</para>
/// </summary>
public class AnimDevController : MonoBehaviour
{
    [Header("대상")]
    [Tooltip("애니메이션을 재생할 캐릭터(씬의 Knight_Male 인스턴스). 비어 있으면 씬에서 자동으로 찾는다.")]
    [SerializeField] private SPUM_Prefabs target;

    [Tooltip("클릭 판정에 쓰는 카메라. 비어 있으면 Camera.main.")]
    [SerializeField] private Camera cam;

    [Header("추가 클립")]
    [Tooltip("프리팹의 SPUM 클립 리스트에 없는 .anim을 목록에 얹어 테스트한다. "
             + "씬 생성 도구가 Assets/Animations의 클립을 여기에 자동 배선한다.")]
    [SerializeField] private ExtraClip[] extraClips = new ExtraClip[0];

    [Header("특수 모션 파라미터(하네스)")]
    [Tooltip("돌진(웅크린 자세) 유지 시간(초). 이 시간이 지나면 걷기 클립을 복원하고 IDLE로 돌아간다.")]
    [SerializeField] private float chargeDashSeconds = 1.2f;
    [Tooltip("마법 캐스트 홀드(손 든 자세 정지) 유지 시간(초).")]
    [SerializeField] private float castHoldSeconds = 1.0f;
    [Tooltip("화살비 공중 체류 시간(초).")]
    [SerializeField] private float arrowRainSeconds = 1.0f;
    [Tooltip("내려찍기 공중 체류 시간(초).")]
    [SerializeField] private float groundSlamSeconds = 0.6f;

    [Header("카메라 프레이밍")]
    [Tooltip("캐릭터가 놓일 화면 가로 위치(0=왼쪽,1=오른쪽). 0.5 = 화면 정중앙(좌우 패널 사이).")]
    [SerializeField] private float characterScreenX = 0.5f;
    [Tooltip("점프하는 특수 모션(화살비·내려찍기) 동안 적용할 줌아웃 배율 — 공중 동작이 화면 밖으로 나가지 않게 한다.")]
    [SerializeField] private float jumpZoomOutScale = 2.3f;

    [Header("무기 강화 이펙트(오른쪽 패널)")]
    [Tooltip("잔상 형태. 대상 프리팹의 무기 종류에 맞춘다(검·도끼=Melee / 활=Bow / 지팡이=Staff).")]
    [SerializeField] private WeaponTrailStyle weaponTrailStyle = WeaponTrailStyle.Melee;
    [Tooltip("씬 진입 시 적용할 강화 단계(0~10).")]
    [SerializeField] private int startEnhanceLevel = 0;

    /// <summary>
    /// 프리팹 클립 리스트 밖에서 테스트할 애니메이션 한 개.
    /// 재생 방식은 <see cref="playAs"/> 상태가 결정한다(ATTACK·DAMAGED·OTHER는 1회 재생 후 IDLE 복귀, MOVE·DEBUFF는 루프).
    /// </summary>
    [System.Serializable]
    public class ExtraClip
    {
        [Tooltip("재생할 애니메이션 클립(.anim).")]
        public AnimationClip clip;

        [Tooltip("어떤 SPUM 상태로 재생할지. ATTACK·DAMAGED·OTHER=1회 재생 후 IDLE 복귀, MOVE·DEBUFF=루프, DEATH=자세 고정.")]
        public PlayerState playAs = PlayerState.ATTACK;
    }

    /// <summary>목록에 노출할 상태와 표시 이름(SPUM 상태 순서).</summary>
    private static readonly (PlayerState State, string Label)[] StateGroups =
    {
        (PlayerState.IDLE, "IDLE — 대기"),
        (PlayerState.MOVE, "MOVE — 이동"),
        (PlayerState.ATTACK, "ATTACK — 공격/스킬"),
        (PlayerState.DAMAGED, "DAMAGED — 피격"),
        (PlayerState.DEBUFF, "DEBUFF — 디버프"),
        (PlayerState.DEATH, "DEATH — 사망"),
        (PlayerState.OTHER, "OTHER — 기타"),
    };

    /// <summary>SpumCharacterAnimator가 제공하는 특수 모션 종류(클립 리스트에 없는 코루틴 연출).</summary>
    private enum SpecialMotion
    {
        Rage,        // 분노(기합) — rageClip
        ChargeDash,  // 돌진(웅크린 자세) — chargeDashClip
        CastHold,    // 마법 캐스트 홀드
        ArrowRain,   // 화살비(점프 + 활 든 자세 정지)
        GroundSlam,  // 내려찍기(점프 + 치켜든 자세로 낙하)
    }

    private static readonly (SpecialMotion Motion, string Label)[] SpecialMotions =
    {
        (SpecialMotion.Rage, "분노(기합) — rageClip"),
        (SpecialMotion.ChargeDash, "돌진(웅크림) — chargeDashClip"),
        (SpecialMotion.CastHold, "마법 캐스트 홀드"),
        (SpecialMotion.ArrowRain, "화살비(점프 + 활 정지)"),
        (SpecialMotion.GroundSlam, "내려찍기(점프 + 낙하)"),
    };

    private static readonly List<RaycastResult> s_uiHits = new List<RaycastResult>();

    private SpumCharacterAnimator _helper;      // 특수 모션 호출용(대상 프리팹에 붙어 있음)
    private float _baseCamSize;                 // 줌아웃 복원용 기본 orthographicSize
    private Vector3 _baseCamPos;                // 줌아웃 복원용 기본 카메라 위치
    private Coroutine _zoomRoutine;             // 점프 모션 줌아웃(진행 중 하나만)
    private Coroutine _dashRoutine;             // 돌진 자세 유지(진행 중 하나만)
    private Text _selectionText;                // "선택: …" 라벨
    private Text _playingText;                  // "재생: …" 라벨
    private readonly List<(Button Button, Image Background)> _rows = new List<(Button, Image)>();
    private int _selectedRow = -1;              // 현재 선택된 행 인덱스(_rows 기준)

    // 선택 상태 — 특수 모션이면 _selectedSpecial, 추가 클립이면 _selectedExtra가 값을 갖고,
    // 둘 다 비어 있으면 프리팹 클립 리스트의 상태+인덱스를 쓴다.
    private PlayerState _selectedState = PlayerState.IDLE;
    private int _selectedClipIndex;
    private SpecialMotion? _selectedSpecial;
    private ExtraClip _selectedExtra;
    private string _selectedLabel = "-";
    private bool _deathLatched;                 // DEATH 재생으로 isDeath가 걸려 있는지

    // 추가 클립을 상태 리스트 끝에 붙여 둔 인덱스(클립당 1회만 추가한다).
    private readonly Dictionary<AnimationClip, int> _extraClipIndex = new Dictionary<AnimationClip, int>();

    // 무기 강화 이펙트 확인용(오른쪽 패널).
    private WeaponAfterimage _weaponFx;
    private Animator _animator;                 // 공격 모션 판정용(대상 프리팹의 Animator)

    // 캐릭터 선택(상단 중앙 패널).
    /// <summary>선택할 수 있는 직업과, 그 직업 무기에 맞는 잔상 형태.</summary>
    private static readonly (int Code, string Label, WeaponTrailStyle Style)[] ClassChoices =
    {
        (1, "기사", WeaponTrailStyle.Melee),
        (2, "궁수", WeaponTrailStyle.Bow),
        (3, "마법사", WeaponTrailStyle.Staff),
        (4, "슬레이어", WeaponTrailStyle.Melee),
    };
    private const int GenderMale = 1;
    private const int GenderFemale = 2;

    private int _classCode = 1;
    private int _gender = GenderMale;
    private readonly List<Image> _classRows = new List<Image>();
    private readonly List<Image> _genderRows = new List<Image>();
    private GameObject _canvasGo;               // 캐릭터를 바꾸면 통째로 다시 만든다
    private int _enhanceLevel;
    private Text _enhanceText;
    private readonly List<Image> _enhanceRows = new List<Image>();

    private static readonly Color RowNormal = new Color(0.18f, 0.20f, 0.26f, 0.95f);
    private static readonly Color RowSelected = new Color(0.24f, 0.45f, 0.72f, 1f);

    private void Awake()
    {
        if (cam == null)
        {
            cam = Camera.main;
        }
        if (target == null)
        {
            target = Object.FindFirstObjectByType<SPUM_Prefabs>();
        }
        if (target != null)
        {
            _helper = target.GetComponent<SpumCharacterAnimator>();
            _animator = target.GetComponentInChildren<Animator>();
        }
    }

    private void Start()
    {
        if (target == null)
        {
            Debug.LogError("[AnimDev] 대상 캐릭터(SPUM_Prefabs)를 찾지 못했습니다. 씬에 캐릭터 프리팹이 있는지 확인하세요.");
            return;
        }

        DetectCurrentCharacter();  // 씬에 놓인 프리팹이 어느 직업·성별인지 이름으로 알아낸다
        _enhanceLevel = Mathf.Clamp(startEnhanceLevel, 0, WeaponAfterimage.MaxEnhanceLevel);
        SetupCurrentTarget();
    }

    /// <summary>
    /// 현재 <see cref="target"/>을 기준으로 UI·무기 이펙트·카메라를 구성한다.
    /// 최초 진입과 캐릭터 교체(<see cref="SwapCharacter"/>) 양쪽에서 같은 경로를 탄다.
    /// </summary>
    private void SetupCurrentTarget()
    {
        // 프리팹에 클립 리스트가 비어 있는 경우(구버전 프리팹)만 채운다 — 목록 UI가 클립 이름을 읽어야 한다.
        if (!target.allListsHaveItemsExist())
        {
            target.PopulateAnimationLists();
        }

        BuildUi();
        AttachWeaponFx();
        FrameCamera();
        SelectClip(0, PlayerState.IDLE, 0, ClipLabel(PlayerState.IDLE, 0));
        SetEnhanceLevel(_enhanceLevel);
        StartCoroutine(ReframeWhenPosed());
    }

    /// <summary>
    /// 애니메이터가 첫 포즈를 적용한 뒤 카메라를 다시 맞춘다.
    /// <para>스폰/교체 직후에는 SPUM 파트가 아직 제자리에 놓이지 않아 렌더 바운즈가 원점 근처로 잡힌다
    /// (실측: 카메라 y가 0으로 잡혀 캐릭터가 화면 위쪽 뷰포트 0.67에 걸렸다). 몇 프레임 뒤 실제 포즈로
    /// 다시 재면 캐릭터가 화면 정중앙에 온다.</para>
    /// </summary>
    private IEnumerator ReframeWhenPosed()
    {
        yield return null;
        yield return null;
        FrameCamera();
    }

    /// <summary>
    /// 씬에 놓여 있는 캐릭터 프리팹의 이름(<c>{직업}_{성별}</c>)에서 현재 직업·성별을 알아낸다.
    /// 상단 선택 패널의 초기 선택 표시를 실제 캐릭터와 맞추기 위한 것이다.
    /// </summary>
    private void DetectCurrentCharacter()
    {
        string n = target != null ? target.gameObject.name : string.Empty;
        if (n.StartsWith("Archer")) _classCode = 2;
        else if (n.StartsWith("Mage")) _classCode = 3;
        else if (n.StartsWith("Slayer")) _classCode = 4;
        else _classCode = 1;
        _gender = n.Contains("Female") ? GenderFemale : GenderMale;
        ApplyStyleForClass();
    }

    /// <summary>현재 직업의 무기에 맞는 잔상 형태를 고른다(검·도끼 Melee / 활 Bow / 지팡이 Staff).</summary>
    private void ApplyStyleForClass()
    {
        foreach (var c in ClassChoices)
        {
            if (c.Code == _classCode)
            {
                weaponTrailStyle = c.Style;
                return;
            }
        }
    }

    /// <summary>
    /// 대상 캐릭터를 다른 직업·성별 프리팹으로 교체한다. 같은 자리·같은 부모에 새로 만들고,
    /// 애니메이션 목록·무기 이펙트·카메라 프레이밍을 새 프리팹 기준으로 다시 구성한다.
    /// 강화 단계는 유지되므로 직업만 바꿔 가며 같은 단계의 이펙트를 비교할 수 있다.
    /// </summary>
    private void SwapCharacter(int classCode, int gender)
    {
        if (classCode == _classCode && gender == _gender)
        {
            return;
        }

        var prefab = CharacterPrefabDatabase.PrefabOf(classCode, gender);
        if (prefab == null)
        {
            Debug.LogWarning($"[AnimDev] 직업 {classCode} / 성별 {gender} 프리팹을 찾지 못했습니다.");
            return;
        }

        // 진행 중인 특수 모션(점프 줌아웃·돌진 자세)을 끊고 카메라를 원래대로 돌린다.
        StopAllCoroutines();
        _zoomRoutine = null;
        _dashRoutine = null;
        if (cam != null && _baseCamSize > 0f)
        {
            cam.orthographicSize = _baseCamSize;
        }

        Vector3 pos = target.transform.position;
        Quaternion rot = target.transform.rotation;
        Transform parent = target.transform.parent;
        var old = target.gameObject;
        old.SetActive(false); // Destroy는 프레임 끝에 처리되므로 즉시 화면에서 치운다
        Destroy(old);

        var go = Instantiate(prefab, pos, rot, parent);
        go.name = prefab.name;

        _classCode = classCode;
        _gender = gender;
        target = go.GetComponent<SPUM_Prefabs>();
        _helper = go.GetComponent<SpumCharacterAnimator>();
        _animator = go.GetComponentInChildren<Animator>();
        _weaponFx = null;          // 새 무기에 다시 붙인다
        _deathLatched = false;
        _extraClipIndex.Clear();   // 추가 클립은 새 프리팹의 리스트에 다시 얹어야 한다
        ApplyStyleForClass();

        if (target == null)
        {
            Debug.LogError($"[AnimDev] {prefab.name}에 SPUM_Prefabs가 없습니다.");
            return;
        }
        SetupCurrentTarget();
    }

    /// <summary>
    /// 캐릭터가 화면 가로 <see cref="characterScreenX"/> 지점에 오도록 카메라 x를 맞추고,
    /// <b>세로는 캐릭터 렌더 바운즈 중앙</b>에 맞춘다.
    /// 목록 패널 폭은 픽셀 고정이라 Game View 비율에 따라 세계 좌표로 차지하는 폭이 달라지므로,
    /// 실제 종횡비를 보고 캐릭터를 오른쪽으로 밀어 패널에 가리지 않게 한다.
    /// <para>세로를 캐릭터 기준으로 잡는 이유: 캐릭터 프리팹 루트가 UI용 <c>RectTransform</c>이라
    /// 월드 배치 값이 프리팹 수정에 따라 크게 달라질 수 있다(앵커 좌표 y −170로 내려가 화면에서
    /// 사라진 적이 있다). 카메라를 캐릭터에 맞추면 그런 변화에도 캐릭터가 화면에 남는다.</para>
    /// </summary>
    private void FrameCamera()
    {
        if (cam == null || target == null || !cam.orthographic)
        {
            return;
        }

        float halfWidth = cam.orthographicSize * cam.aspect;
        float offset = (Mathf.Clamp01(characterScreenX) - 0.5f) * 2f * halfWidth;
        var pos = cam.transform.position;
        pos.x = target.transform.position.x - offset;
        pos.y = CharacterCenterY();
        cam.transform.position = pos;

        _baseCamSize = cam.orthographicSize;
        _baseCamPos = pos;
    }

    /// <summary>캐릭터 스프라이트 전체를 감싸는 렌더 바운즈의 세로 중심을 돌려준다(카메라 세로 프레이밍용).
    /// 켜져 있는 렌더러가 없으면 캐릭터 트랜스폼의 y를 그대로 쓴다.
    /// <para><b>스프라이트 렌더러만</b> 센다 — 무기 잔상(<c>TrailRenderer</c>)·불꽃(<c>ParticleSystemRenderer</c>)·
    /// <c>SpriteMask</c>도 Renderer라서 함께 세면 바운즈가 이펙트 쪽으로 끌려가 프레이밍이 흔들린다
    /// (실측: 전체 기준 중심 y 0.396 vs 스프라이트만 0.338).</para></summary>
    private float CharacterCenterY()
    {
        bool has = false;
        Bounds bounds = default;
        foreach (var r in target.GetComponentsInChildren<SpriteRenderer>())
        {
            if (r == null || !r.enabled || r.sprite == null)
            {
                continue;
            }
            if (!has)
            {
                bounds = r.bounds;
                has = true;
            }
            else
            {
                bounds.Encapsulate(r.bounds);
            }
        }
        return has ? bounds.center.y : target.transform.position.y;
    }

    private void Update()
    {
        var pointer = Pointer.current;
        if (pointer == null || !pointer.press.wasPressedThisFrame)
        {
            return;
        }

        Vector2 screen = pointer.position.ReadValue();
        if (IsPointerOverUi(screen))
        {
            return; // 목록 패널 클릭은 선택이므로 재생 판정에서 제외
        }

        if (cam == null || target == null)
        {
            return;
        }

        Vector3 world = cam.ScreenToWorldPoint(screen);
        var hit = Physics2D.OverlapPoint(world);
        if (hit == null || hit.GetComponentInParent<SPUM_Prefabs>() != target)
        {
            return; // 캐릭터(Collider2D) 밖을 클릭
        }

        PlaySelected();
    }

    // ────────────────────────────── 재생 ──────────────────────────────

    /// <summary>목록에서 고른 애니메이션을 대상 캐릭터에 재생한다(캐릭터 클릭 시 호출).</summary>
    private void PlaySelected()
    {
        EnsurePlayable();
        RestoreCameraFraming(); // 직전 점프 모션의 줌아웃이 남아 있으면 되돌린다

        if (_selectedSpecial.HasValue)
        {
            ClearDeathLatch();
            PlaySpecial(_selectedSpecial.Value);
        }
        else if (_selectedExtra != null)
        {
            PlayExtraClip(_selectedExtra);
        }
        else
        {
            if (_selectedState != PlayerState.DEATH)
            {
                ClearDeathLatch(); // 사망으로 굳은 상태에서 다른 애니로 넘어가려면 isDeath를 먼저 풀어야 한다
            }
            target.PlayAnimation(_selectedState, _selectedClipIndex);
            _deathLatched = _selectedState == PlayerState.DEATH;
        }

        if (_playingText != null)
        {
            _playingText.text = $"재생: {_selectedLabel}";
        }
        Debug.Log($"[AnimDev] 재생 — {_selectedLabel}");
    }

    /// <summary>
    /// 프리팹 리스트 밖의 추가 클립을 재생한다.
    /// SPUM은 상태 리스트의 인덱스로만 재생할 수 있으므로(<see cref="SPUM_Prefabs.PlayAnimation"/>),
    /// 지정한 상태 리스트 <b>끝에</b> 클립을 1회 추가하고 그 인덱스로 재생한다
    /// (기존 인덱스는 건드리지 않아 전투가 쓰는 index 0 등이 그대로 유지된다).
    /// 런타임 리스트만 바꾸므로 프리팹 에셋에는 남지 않는다.
    /// </summary>
    private void PlayExtraClip(ExtraClip extra)
    {
        if (extra.clip == null)
        {
            return;
        }

        var clips = ClipsOf(extra.playAs);
        if (clips == null)
        {
            return;
        }

        if (!_extraClipIndex.TryGetValue(extra.clip, out int index)
            || index >= clips.Count || clips[index] != extra.clip)
        {
            clips.Add(extra.clip);
            index = clips.Count - 1;
            _extraClipIndex[extra.clip] = index;
        }

        if (extra.playAs != PlayerState.DEATH)
        {
            ClearDeathLatch();
        }
        target.PlayAnimation(extra.playAs, index);
        _deathLatched = extra.playAs == PlayerState.DEATH;
    }

    /// <summary>SpumCharacterAnimator의 특수 모션을 호출한다(돌진은 유지 시간 뒤 자동 복원).</summary>
    private void PlaySpecial(SpecialMotion motion)
    {
        if (_helper == null)
        {
            Debug.LogWarning("[AnimDev] 대상에 SpumCharacterAnimator가 없어 특수 모션을 재생할 수 없습니다.");
            return;
        }

        switch (motion)
        {
            case SpecialMotion.Rage:
                _helper.PlayRage();
                break;
            case SpecialMotion.ChargeDash:
                if (_dashRoutine != null)
                {
                    StopCoroutine(_dashRoutine);
                    _helper.StopChargeDash(); // 이전 돌진이 남아 있으면 걷기 클립을 먼저 복원
                }
                _dashRoutine = StartCoroutine(ChargeDashRoutine());
                break;
            case SpecialMotion.CastHold:
                _helper.PlayCastHold(castHoldSeconds);
                break;
            case SpecialMotion.ArrowRain:
                _helper.PlayArrowRain(arrowRainSeconds);
                ZoomOutWhileJumping(arrowRainSeconds + 1.2f);
                break;
            case SpecialMotion.GroundSlam:
                _helper.PlayGroundSlam(groundSlamSeconds);
                ZoomOutWhileJumping(groundSlamSeconds + 1.2f);
                break;
        }
    }

    /// <summary>점프하는 특수 모션 동안만 카메라를 줌아웃했다가(공중 동작이 화면을 벗어나지 않게) 원래 프레이밍으로 되돌린다.</summary>
    private void ZoomOutWhileJumping(float seconds)
    {
        if (cam == null || target == null)
        {
            return;
        }
        if (_zoomRoutine != null)
        {
            StopCoroutine(_zoomRoutine);
        }
        _zoomRoutine = StartCoroutine(ZoomOutRoutine(seconds));
    }

    /// <summary>줌아웃 중이던 카메라를 기본 프레이밍으로 즉시 되돌린다(다른 애니메이션을 재생할 때).</summary>
    private void RestoreCameraFraming()
    {
        if (cam == null || _zoomRoutine == null)
        {
            return;
        }
        StopCoroutine(_zoomRoutine);
        _zoomRoutine = null;
        cam.orthographicSize = _baseCamSize;
        cam.transform.position = _baseCamPos;
    }

    private IEnumerator ZoomOutRoutine(float seconds)
    {
        float zoomSize = _baseCamSize * Mathf.Max(1f, jumpZoomOutScale);
        cam.orthographicSize = zoomSize;
        // 줌아웃하면 화면에 담기는 폭·높이가 늘어나므로 캐릭터 화면 위치와 지면 여백을 다시 맞춘다.
        float halfWidth = zoomSize * cam.aspect;
        float offset = (Mathf.Clamp01(characterScreenX) - 0.5f) * 2f * halfWidth;
        cam.transform.position = new Vector3(target.transform.position.x - offset,
                                            zoomSize * 0.65f, _baseCamPos.z);

        yield return new WaitForSeconds(Mathf.Max(0.2f, seconds));

        cam.orthographicSize = _baseCamSize;
        cam.transform.position = _baseCamPos;
        _zoomRoutine = null;
    }

    /// <summary>돌진 자세를 유지 시간만큼 보여준 뒤 걷기 클립을 복원하고 IDLE로 돌린다(돌진은 스스로 끝나지 않는다).</summary>
    private IEnumerator ChargeDashRoutine()
    {
        _helper.PlayChargeDash();
        yield return new WaitForSeconds(Mathf.Max(0.1f, chargeDashSeconds));
        _helper.StopChargeDash();
        _dashRoutine = null;
    }

    /// <summary>DEATH로 걸린 isDeath 래치를 IDLE 재생으로 풀어 다른 애니메이션이 나올 수 있게 한다.</summary>
    private void ClearDeathLatch()
    {
        if (!_deathLatched)
        {
            return;
        }
        target.PlayAnimation(PlayerState.IDLE, 0);
        _deathLatched = false;
    }

    /// <summary>PlayAnimation 호출에 필요한 초기화(클립 리스트 + AnimatorOverrideController)를 보장한다.</summary>
    private void EnsurePlayable()
    {
        if (!target.allListsHaveItemsExist())
        {
            target.PopulateAnimationLists();
        }
        if (target.OverrideController == null)
        {
            // 보통은 프리팹의 SpumCharacterAnimator.Start()가 이미 초기화해 둔다(여기서 중복 초기화하지 않음).
            target.OverrideControllerInit();
        }
    }

    // ────────────────────────────── 선택 ──────────────────────────────

    /// <summary>상태+클립 인덱스를 선택 상태로 저장하고 행 하이라이트를 갱신한다.</summary>
    private void SelectClip(int row, PlayerState state, int clipIndex, string label)
    {
        _selectedSpecial = null;
        _selectedExtra = null;
        _selectedState = state;
        _selectedClipIndex = clipIndex;
        _selectedLabel = label;
        Highlight(row);
    }

    /// <summary>특수 모션을 선택 상태로 저장하고 행 하이라이트를 갱신한다.</summary>
    private void SelectSpecial(int row, SpecialMotion motion, string label)
    {
        _selectedSpecial = motion;
        _selectedExtra = null;
        _selectedLabel = label;
        Highlight(row);
    }

    /// <summary>추가 클립을 선택 상태로 저장하고 행 하이라이트를 갱신한다.</summary>
    private void SelectExtra(int row, ExtraClip extra, string label)
    {
        _selectedSpecial = null;
        _selectedExtra = extra;
        _selectedLabel = label;
        Highlight(row);
    }

    /// <summary>선택된 행만 강조색으로 칠하고 "선택: …" 라벨을 갱신한다.</summary>
    private void Highlight(int row)
    {
        if (_selectedRow >= 0 && _selectedRow < _rows.Count && _rows[_selectedRow].Background != null)
        {
            _rows[_selectedRow].Background.color = RowNormal;
        }
        _selectedRow = row;
        if (row >= 0 && row < _rows.Count && _rows[row].Background != null)
        {
            _rows[row].Background.color = RowSelected;
        }
        if (_selectionText != null)
        {
            _selectionText.text = $"선택: {_selectedLabel}";
        }
    }

    /// <summary>상태별 클립 리스트에서 표시용 이름("ATTACK[2] 0_Attack_Bow")을 만든다.</summary>
    private string ClipLabel(PlayerState state, int index)
    {
        var clips = ClipsOf(state);
        string clipName = clips != null && index < clips.Count && clips[index] != null
            ? clips[index].name
            : "(없음)";
        return $"{state}[{index}] {clipName}";
    }

    /// <summary>상태에 해당하는 클립 리스트를 돌려준다.</summary>
    private List<AnimationClip> ClipsOf(PlayerState state)
    {
        switch (state)
        {
            case PlayerState.IDLE: return target.IDLE_List;
            case PlayerState.MOVE: return target.MOVE_List;
            case PlayerState.ATTACK: return target.ATTACK_List;
            case PlayerState.DAMAGED: return target.DAMAGED_List;
            case PlayerState.DEBUFF: return target.DEBUFF_List;
            case PlayerState.DEATH: return target.DEATH_List;
            case PlayerState.OTHER: return target.OTHER_List;
            default: return null;
        }
    }

    /// <summary>현재 포인터 위치가 uGUI(목록 패널) 위인지 판정한다 — 패널 클릭이 재생으로 새지 않게 한다.</summary>
    private static bool IsPointerOverUi(Vector2 screenPosition)
    {
        var es = EventSystem.current;
        if (es == null)
        {
            return false;
        }
        var ped = new PointerEventData(es) { position = screenPosition };
        s_uiHits.Clear();
        es.RaycastAll(ped, s_uiHits);
        return s_uiHits.Count > 0;
    }

    // ────────────────────────────── UI 구성 ──────────────────────────────

    /// <summary>
    /// 왼쪽 애니메이션 목록 · 상단 캐릭터 선택 · 오른쪽 강화 단계 패널을 런타임에 만든다.
    /// 계층을 씬에 굽지 않고 매번 생성하므로 버튼 리스너 소실(비영구 리스너) 문제가 없고,
    /// 프리팹의 클립 구성이 바뀌면 목록도 자동으로 따라간다.
    /// <para>캐릭터를 교체하면 클립 목록이 통째로 달라지므로 <b>캔버스를 버리고 다시 만든다</b>.</para>
    /// </summary>
    private void BuildUi()
    {
        if (_canvasGo != null)
        {
            Destroy(_canvasGo);
        }
        _rows.Clear();
        _enhanceRows.Clear();
        _classRows.Clear();
        _genderRows.Clear();
        _selectedRow = -1;

        // 개발 하네스라 해상도 스케일링 없이 픽셀 고정(ConstantPixelSize)으로 둔다 — 어떤 Game View 비율에서도 크기가 같다.
        var canvasGo = new GameObject("AnimDevCanvas", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        _canvasGo = canvasGo;
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        canvasGo.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

        // 패널 — 화면 왼쪽에 세로로 꽉 차게.
        var panel = NewUi("Panel", canvasGo.transform, out Image panelBg);
        panelBg.color = new Color(0.07f, 0.08f, 0.11f, 0.92f);
        panel.anchorMin = new Vector2(0f, 0f);
        panel.anchorMax = new Vector2(0f, 1f);
        panel.pivot = new Vector2(0f, 0.5f);
        panel.offsetMin = new Vector2(8f, 8f);
        panel.offsetMax = new Vector2(336f, -8f);

        // 제목 + 안내 + 선택/재생 라벨(패널 상단 고정).
        MakeLabel(panel, "Title", "애니메이션 목록", 18, FontStyle.Bold, new Color(1f, 0.87f, 0.45f),
                  topOffset: -6f, height: 26f);
        MakeLabel(panel, "Hint", "목록에서 고른 뒤 캐릭터를 클릭하면 재생됩니다.", 13, FontStyle.Normal,
                  new Color(0.75f, 0.78f, 0.85f), topOffset: -34f, height: 20f);
        _selectionText = MakeLabel(panel, "Selection", "선택: -", 13, FontStyle.Bold, Color.white,
                                   topOffset: -56f, height: 20f);
        _playingText = MakeLabel(panel, "Playing", "재생: -", 13, FontStyle.Normal,
                                 new Color(0.55f, 0.85f, 0.6f), topOffset: -76f, height: 20f);

        // 스크롤 영역(클립이 많아 패널 높이를 넘길 수 있다).
        var scrollRt = NewUi("Scroll", panel.transform, out Image scrollBg);
        scrollBg.color = new Color(0f, 0f, 0f, 0.25f);
        scrollRt.anchorMin = Vector2.zero;
        scrollRt.anchorMax = Vector2.one;
        scrollRt.offsetMin = new Vector2(6f, 6f);
        scrollRt.offsetMax = new Vector2(-6f, -100f);
        var scroll = scrollRt.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 24f;

        var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        var viewport = (RectTransform)viewportGo.transform;
        viewport.SetParent(scrollRt, false);
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = Vector2.zero;
        viewport.offsetMax = Vector2.zero;
        scroll.viewport = viewport;

        var contentGo = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup),
            typeof(ContentSizeFitter));
        var content = (RectTransform)contentGo.transform;
        content.SetParent(viewport, false);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.offsetMin = Vector2.zero;
        content.offsetMax = Vector2.zero;
        var layout = contentGo.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 3f;
        layout.padding = new RectOffset(4, 4, 4, 4);
        layout.childForceExpandHeight = false;
        layout.childControlHeight = true;
        contentGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = content;

        // 상태별 클립 버튼.
        foreach (var group in StateGroups)
        {
            var clips = ClipsOf(group.State);
            MakeHeader(content, group.Label + (clips == null || clips.Count == 0 ? " (클립 없음)" : ""));
            if (clips == null)
            {
                continue;
            }
            for (int i = 0; i < clips.Count; i++)
            {
                PlayerState state = group.State;
                int index = i;
                string label = ClipLabel(state, i);
                int row = _rows.Count;
                MakeRowButton(content, label, () => SelectClip(row, state, index, label));
            }
        }

        // 추가 클립(프리팹 리스트 밖의 .anim — 씬 생성 도구가 Assets/Animations에서 배선).
        if (extraClips != null && extraClips.Length > 0)
        {
            MakeHeader(content, "추가 클립 — Assets/Animations");
            foreach (var extra in extraClips)
            {
                if (extra == null || extra.clip == null)
                {
                    continue;
                }
                ExtraClip captured = extra;
                string label = $"{extra.clip.name} ({extra.playAs}로 재생)";
                int row = _rows.Count;
                MakeRowButton(content, label, () => SelectExtra(row, captured, label));
            }
        }

        // 특수 모션(SpumCharacterAnimator 코루틴 연출).
        MakeHeader(content, "특수 모션 — SpumCharacterAnimator");
        foreach (var special in SpecialMotions)
        {
            SpecialMotion motion = special.Motion;
            string label = special.Label;
            int row = _rows.Count;
            MakeRowButton(content, label, () => SelectSpecial(row, motion, label));
        }

        BuildCharacterUi(canvasGo.transform); // 상단 중앙: 직업 · 성별 선택
        BuildEnhanceUi(canvasGo.transform);   // 오른쪽: 무기 강화 단계별 이펙트
    }

    // ─────────────────────── 캐릭터 선택 (상단 중앙 패널) ───────────────────────

    /// <summary>
    /// 화면 중앙 상단에 직업(4)·성별(2) 선택 패널을 만든다. 버튼을 누르면 그 자리에서 캐릭터 프리팹이
    /// 교체되고 애니메이션 목록도 새 프리팹 기준으로 다시 만들어진다.
    /// 폭은 좌우 패널(왼쪽 336 · 오른쪽 212)과 겹치지 않도록 좁게 잡는다.
    /// </summary>
    private void BuildCharacterUi(Transform canvas)
    {
        const float panelWidth = 452f;
        const float panelHeight = 92f;

        var panel = NewUi("CharacterPanel", canvas, out Image panelBg);
        panelBg.color = new Color(0.07f, 0.08f, 0.11f, 0.92f);
        panel.anchorMin = new Vector2(0.5f, 1f);
        panel.anchorMax = new Vector2(0.5f, 1f);
        panel.pivot = new Vector2(0.5f, 1f);
        panel.sizeDelta = new Vector2(panelWidth, panelHeight);
        panel.anchoredPosition = new Vector2(0f, -8f);

        MakeLabel(panel, "Title", "캐릭터", 14, FontStyle.Bold, new Color(1f, 0.87f, 0.45f),
                  topOffset: -4f, height: 18f);

        // 1행: 직업.
        var classRow = MakeRowContainer(panel, "Classes", topOffset: -24f, height: 28f);
        _classRows.Clear();
        foreach (var choice in ClassChoices)
        {
            int code = choice.Code;
            _classRows.Add(MakeChoiceButton(classRow, choice.Label, () => SwapCharacter(code, _gender)));
        }

        // 2행: 성별.
        var genderRow = MakeRowContainer(panel, "Genders", topOffset: -58f, height: 28f);
        _genderRows.Clear();
        _genderRows.Add(MakeChoiceButton(genderRow, "남", () => SwapCharacter(_classCode, GenderMale)));
        _genderRows.Add(MakeChoiceButton(genderRow, "여", () => SwapCharacter(_classCode, GenderFemale)));

        RefreshCharacterSelection();
    }

    /// <summary>현재 직업·성별에 해당하는 버튼을 선택 색으로 표시한다.</summary>
    private void RefreshCharacterSelection()
    {
        for (int i = 0; i < _classRows.Count && i < ClassChoices.Length; i++)
        {
            if (_classRows[i] != null)
            {
                _classRows[i].color = ClassChoices[i].Code == _classCode ? RowSelected : RowNormal;
            }
        }
        for (int i = 0; i < _genderRows.Count; i++)
        {
            if (_genderRows[i] != null)
            {
                int g = i == 0 ? GenderMale : GenderFemale;
                _genderRows[i].color = g == _gender ? RowSelected : RowNormal;
            }
        }
    }

    /// <summary>패널 상단에서 <paramref name="topOffset"/>만큼 내려온 자리에 가로 배치 컨테이너를 만든다.</summary>
    private static RectTransform MakeRowContainer(RectTransform parent, string name, float topOffset, float height)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(6f, -height);
        rt.offsetMax = new Vector2(-6f, 0f);
        rt.anchoredPosition = new Vector2(0f, topOffset);

        var layout = go.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 4f;
        layout.childForceExpandWidth = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = true;
        layout.childControlHeight = true;
        return rt;
    }

    /// <summary>가로 배치용 선택 버튼(가운데 정렬 라벨)을 만들고 배경 Image를 돌려준다.</summary>
    private static Image MakeChoiceButton(RectTransform parent, string label,
                                          UnityEngine.Events.UnityAction onClick)
    {
        var rt = NewUi("Choice", parent, out Image bg);
        bg.color = RowNormal;

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
        var lrt = (RectTransform)labelGo.transform;
        lrt.SetParent(rt, false);
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero;
        lrt.offsetMax = Vector2.zero;
        var text = labelGo.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = label;
        text.fontSize = 13;
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false;

        var button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = bg;
        button.transition = Selectable.Transition.None; // 하이라이트는 선택 상태로만 표시
        button.onClick.AddListener(onClick);
        return bg;
    }

    // ─────────────────────── 무기 강화 이펙트 (오른쪽 패널) ───────────────────────

    /// <summary>
    /// 대상 캐릭터의 무기에 강화 이펙트(<see cref="WeaponAfterimage"/>)를 붙인다.
    /// 궤적 방출 조건은 <b>전투와 동일하게</b> 공격 모션 재생 여부로 둔다(<see cref="IsAttackMotionPlaying"/>) —
    /// 항상 방출로 두면 IDLE에서도 무기 끝에 궤적이 끌려 상시 이펙트와 구분되지 않는다.
    /// </summary>
    private void AttachWeaponFx()
    {
        if (target == null || _weaponFx != null)
        {
            return;
        }
        _weaponFx = WeaponAfterimage.Create(target.transform, IsAttackMotionPlaying, weaponTrailStyle);
        if (_weaponFx == null)
        {
            Debug.LogWarning("[AnimDev] 대상에서 무기 렌더러를 찾지 못해 강화 이펙트를 붙이지 못했습니다.");
        }
    }

    /// <summary>
    /// 공격 애니(SPUM "…Attack…" 클립)가 아직 재생 중인지. 전투의
    /// <c>PlayerCombatant.IsAttackMotionPlaying</c>과 같은 규칙이라, 하네스에서 보이는 궤적이
    /// 실제 전투에서 보이는 궤적과 일치한다.
    /// </summary>
    private bool IsAttackMotionPlaying()
    {
        if (_animator == null)
        {
            return false;
        }
        var ci = _animator.GetCurrentAnimatorClipInfo(0);
        if (ci == null || ci.Length == 0 || ci[0].clip == null)
        {
            return false;
        }
        if (ci[0].clip.name.ToLower().IndexOf("attack") < 0)
        {
            return false;
        }
        return _animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1f;
    }

    /// <summary>강화 단계를 바꿔 무기 이펙트에 즉시 반영하고, 오른쪽 패널의 표시를 갱신한다.</summary>
    private void SetEnhanceLevel(int level)
    {
        _enhanceLevel = Mathf.Clamp(level, 0, WeaponAfterimage.MaxEnhanceLevel);
        if (_weaponFx != null)
        {
            _weaponFx.SetEnhanceLevel(_enhanceLevel);
        }
        if (_enhanceText != null)
        {
            _enhanceText.text = $"현재: +{_enhanceLevel}  ({EnhanceDescription(_enhanceLevel)})";
        }
        for (int i = 0; i < _enhanceRows.Count; i++)
        {
            if (_enhanceRows[i] != null)
            {
                _enhanceRows[i].color = i == _enhanceLevel ? RowSelected : RowNormal;
            }
        }
    }

    /// <summary>단계별로 무엇이 달라지는지 한 줄로 알려 준다(색 구간 + 반짝임 유무).</summary>
    private static string EnhanceDescription(int level)
    {
        if (level >= WeaponAfterimage.MaxEnhanceLevel) return "어두운 보라 · 암흑 · 반짝임 최대";
        if (level >= 8) return "보랏빛 · 반짝임 강함";
        if (level >= 6) return "붉은색 · 반짝임 중간";
        if (level >= 3) return "붉은 기 · 반짝임 약함";
        if (level >= 1) return "흰빛에서 붉은 기 · 반짝임 없음";
        return "흰빛~회색 · 서린 빛 없음";
    }

    /// <summary>
    /// 오른쪽에 강화 단계(0~10) 선택 패널을 만든다. 버튼을 누르면 그 단계의 잔상 색·서린 빛·반짝임이
    /// 즉시 적용되므로, 애니메이션을 재생하며 단계별 차이를 바로 비교할 수 있다.
    /// </summary>
    private void BuildEnhanceUi(Transform canvas)
    {
        var panel = NewUi("EnhancePanel", canvas, out Image panelBg);
        panelBg.color = new Color(0.07f, 0.08f, 0.11f, 0.92f);
        panel.anchorMin = new Vector2(1f, 0f);
        panel.anchorMax = new Vector2(1f, 1f);
        panel.pivot = new Vector2(1f, 0.5f);
        panel.offsetMin = new Vector2(-212f, 8f);
        panel.offsetMax = new Vector2(-8f, -8f);

        MakeLabel(panel, "Title", "무기 강화 이펙트", 18, FontStyle.Bold, new Color(1f, 0.87f, 0.45f),
                  topOffset: -6f, height: 26f);
        MakeLabel(panel, "Hint", "단계를 누르면 즉시 반영됩니다.", 12, FontStyle.Normal,
                  new Color(0.75f, 0.78f, 0.85f), topOffset: -34f, height: 18f);
        MakeLabel(panel, "Hint2", "IDLE에서 보이는 것이 상시 이펙트.", 12, FontStyle.Normal,
                  new Color(0.75f, 0.78f, 0.85f), topOffset: -52f, height: 18f);
        _enhanceText = MakeLabel(panel, "Current", "현재: +0", 13, FontStyle.Bold, Color.white,
                                 topOffset: -74f, height: 34f);

        // 단계 버튼(0~10)을 세로로 쌓는다 — 11개라 스크롤 없이 들어간다.
        var listGo = new GameObject("Levels", typeof(RectTransform), typeof(VerticalLayoutGroup));
        var list = (RectTransform)listGo.transform;
        list.SetParent(panel, false);
        list.anchorMin = Vector2.zero;
        list.anchorMax = Vector2.one;
        list.offsetMin = new Vector2(6f, 6f);
        list.offsetMax = new Vector2(-6f, -112f);
        var layout = listGo.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 3f;
        layout.childForceExpandHeight = true;
        layout.childControlHeight = true;

        _enhanceRows.Clear();
        for (int lv = 0; lv <= WeaponAfterimage.MaxEnhanceLevel; lv++)
        {
            int captured = lv;
            string label = lv == 0 ? "+0 (미강화)" : $"+{lv}";
            _enhanceRows.Add(MakeEnhanceButton(list, label, () => SetEnhanceLevel(captured)));
        }
    }

    /// <summary>강화 단계 버튼 한 개를 만들고 배경 Image(선택 하이라이트용)를 돌려준다.</summary>
    private static Image MakeEnhanceButton(RectTransform parent, string label,
                                           UnityEngine.Events.UnityAction onClick)
    {
        var rt = NewUi("Level", parent, out Image bg);
        bg.color = RowNormal;
        rt.gameObject.AddComponent<LayoutElement>().minHeight = 26f;

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
        var lrt = (RectTransform)labelGo.transform;
        lrt.SetParent(rt, false);
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = new Vector2(8f, 0f);
        lrt.offsetMax = new Vector2(-6f, 0f);
        var text = labelGo.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = label;
        text.fontSize = 13;
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleLeft;
        text.raycastTarget = false;

        var button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = bg;
        button.transition = Selectable.Transition.None; // 하이라이트는 선택 상태로만 표시
        button.onClick.AddListener(onClick);
        return bg;
    }

    /// <summary>배경 Image를 가진 빈 UI 오브젝트를 만든다.</summary>
    private static RectTransform NewUi(string name, Transform parent, out Image image)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        image = go.GetComponent<Image>();
        return rt;
    }

    /// <summary>패널 상단에 고정되는 한 줄 라벨을 만든다.</summary>
    private static Text MakeLabel(RectTransform parent, string name, string content, int fontSize,
                                  FontStyle style, Color color, float topOffset, float height)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(8f, -height);
        rt.offsetMax = new Vector2(-8f, 0f);
        rt.anchoredPosition = new Vector2(0f, topOffset);

        var text = go.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = content;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = color;
        text.alignment = TextAnchor.MiddleLeft;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.raycastTarget = false;
        return text;
    }

    /// <summary>목록의 상태 구분 머리글 행을 만든다(선택 대상 아님).</summary>
    private static void MakeHeader(RectTransform content, string label)
    {
        var go = new GameObject("Header", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
        go.transform.SetParent(content, false);
        var text = go.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = "▍" + label;
        text.fontSize = 13;
        text.fontStyle = FontStyle.Bold;
        text.color = new Color(0.98f, 0.75f, 0.35f);
        text.alignment = TextAnchor.MiddleLeft;
        text.raycastTarget = false;
        go.GetComponent<LayoutElement>().minHeight = 22f;
    }

    /// <summary>클릭하면 그 애니메이션을 '선택'하는 목록 행 버튼을 만든다(재생은 캐릭터 클릭으로).</summary>
    private void MakeRowButton(RectTransform content, string label, UnityEngine.Events.UnityAction onClick)
    {
        var rt = NewUi("Row", content, out Image bg);
        bg.color = RowNormal;
        rt.gameObject.AddComponent<LayoutElement>().minHeight = 26f;

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
        var lrt = (RectTransform)labelGo.transform;
        lrt.SetParent(rt, false);
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = new Vector2(8f, 0f);
        lrt.offsetMax = new Vector2(-6f, 0f);
        var text = labelGo.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = label;
        text.fontSize = 13;
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleLeft;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.raycastTarget = false;

        var button = rt.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None; // 선택 하이라이트를 직접 칠하므로 기본 색 전이는 끈다
        button.onClick.AddListener(onClick);
        _rows.Add((button, bg));
    }
}
