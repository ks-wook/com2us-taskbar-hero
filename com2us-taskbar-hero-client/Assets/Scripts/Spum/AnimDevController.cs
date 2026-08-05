using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

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
    [Tooltip("캐릭터가 놓일 화면 가로 위치(0=왼쪽,1=오른쪽). 왼쪽 목록 패널에 가리지 않도록 오른쪽에 둔다.")]
    [SerializeField] private float characterScreenX = 0.68f;
    [Tooltip("점프하는 특수 모션(화살비·내려찍기) 동안 적용할 줌아웃 배율 — 공중 동작이 화면 밖으로 나가지 않게 한다.")]
    [SerializeField] private float jumpZoomOutScale = 2.3f;

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
        }
    }

    private void Start()
    {
        if (target == null)
        {
            Debug.LogError("[AnimDev] 대상 캐릭터(SPUM_Prefabs)를 찾지 못했습니다. 씬에 캐릭터 프리팹이 있는지 확인하세요.");
            return;
        }

        // 프리팹에 클립 리스트가 비어 있는 경우(구버전 프리팹)만 채운다 — 목록 UI가 클립 이름을 읽어야 한다.
        if (!target.allListsHaveItemsExist())
        {
            target.PopulateAnimationLists();
        }

        BuildUi();
        FrameCamera();
        SelectClip(0, PlayerState.IDLE, 0, ClipLabel(PlayerState.IDLE, 0));
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
    /// 켜져 있는 렌더러가 없으면 캐릭터 트랜스폼의 y를 그대로 쓴다.</summary>
    private float CharacterCenterY()
    {
        bool has = false;
        Bounds bounds = default;
        foreach (var r in target.GetComponentsInChildren<Renderer>())
        {
            if (r == null || !r.enabled)
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
    /// 왼쪽 애니메이션 목록 패널을 런타임에 만든다.
    /// 계층을 씬에 굽지 않고 매번 생성하므로 버튼 리스너 소실(비영구 리스너) 문제가 없고,
    /// 프리팹의 클립 구성이 바뀌면 목록도 자동으로 따라간다.
    /// </summary>
    private void BuildUi()
    {
        // 개발 하네스라 해상도 스케일링 없이 픽셀 고정(ConstantPixelSize)으로 둔다 — 어떤 Game View 비율에서도 크기가 같다.
        var canvasGo = new GameObject("AnimDevCanvas", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
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
