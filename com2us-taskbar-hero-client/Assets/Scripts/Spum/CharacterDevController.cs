using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using TaskbarHero.Client.Managers;
using TaskbarHero.Common.MasterData;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// 몬스터 유닛 자동생성 개발 하네스(CharacterDevScene 전용).
/// 기획서: <c>docs/캐릭터-개발씬-기획서.md</c>
///
/// <para>한 화면에서 몬스터의 <b>외형 프리팹</b>(태그 조합 → <c>monster_{code}.prefab</c>)과
/// <b>능력치</b>(<c>monster_master</c> 행)를 함께 만든다. 외형은 레시피(§4.2)의 태그로 파츠를 골라 조합하고,
/// 능력치는 지역·스테이지에서 추천값을 채운 뒤 손으로 고친다(§4.3·§4.4).</para>
///
/// <para><b>SPUM_Scene은 쓰지 않는다.</b> 조합·저장에 필요한 기능만 <see cref="SpumUnitComposer"/>로 뽑아
/// 재구현했고, 이 씬에는 <b>캐릭터(프리뷰 유닛) 하나만</b> 뜬다. SPUM 서드파티 코드는 고치지 않는다(N4).</para>
///
/// <para>이 스크립트는 asmdef 없는 Assembly-CSharp에 둔다 — <c>SPUM_Prefabs</c>·
/// <c>SPUM_ImprovedTagManager</c>의 타입을 직접 참조해야 하기 때문이다(AnimDevController와 같은 제약).</para>
/// </summary>
public partial class CharacterDevController : MonoBehaviour
{
    /// <summary>
    /// 검수용 전투에 "이 몬스터만 스폰"을 넘기는 EditorPrefs 키(§7.3).
    /// <c>BattleDevController</c>는 다른 어셈블리라 이 상수를 참조할 수 없어 같은 문자열을 각자 갖는다.
    /// </summary>
    public const string SpawnCodePrefKey = "TaskbarHero.Dev.SpawnMonsterCode";

    /// <summary>플레이를 멈춘 뒤 '던전 전투 배선'을 실행해 달라는 예약 플래그(F8). 에디터 훅이 읽고 지운다.</summary>
    public const string RunWiringPrefKey = "TaskbarHero.Dev.RunDungeonWiringAfterPlay";

    [Header("데이터")]
    [Tooltip("외형 레시피 JSON(Assets/Dev/monster-appearance-recipe.json). 씬 빌더가 배선한다.")]
    [SerializeField] private TextAsset recipeJson;

    [Header("씬")]
    [Tooltip("하네스 주 카메라. 비어 있으면 Camera.main.")]
    [SerializeField] private Camera cam;
    [Tooltip("프리뷰 캐릭터가 놓이는 자리.")]
    [SerializeField] private Transform previewAnchor;

    [Header("경로")]
    [SerializeField] private string monsterPrefabFolder = "Assets/Prefabs/Character/Monster/";
    [SerializeField] private string backupFolder = "Assets/Dev/MonsterPrefabBackup/";
    [SerializeField] private string masterJsonPath = "Assets/Resources/MasterData/monster_master.json";
    [SerializeField] private string battleDevScenePath = "Assets/Scenes/BattleDevScene.unity";

    /// <summary>목록 한 행 — 마스터 데이터 값 + 산출물 존재 여부 + 기준값·등장 레벨 진단(§5 좌측 패널).</summary>
    private sealed class MonsterRow
    {
        public int Code;
        public string Name;
        public long Hp;
        public long Attack;
        public bool HasPrefab;
        public bool HasRecipe;
        public bool Orphan;      // monster_master에 없는데 프리팹만 있는 경우(§6.4)
        public string BaseNote = "-";    // 통일 기준값과 같은지(§4.3)
        public string LevelNote = "-";   // 그 Act에서 이 몬스터가 등장하는 레벨 범위(§4.4)
    }

    // ── 조합 엔진 · 프리뷰 캐릭터 ──
    private SpumUnitComposer _composer;
    private SPUM_Prefabs _previewUnit;
    /// <summary>
    /// 생성·저장을 할 수 있는 상태인지. <b>bool 플래그가 아니라 실제 참조로 판정한다</b> —
    /// 플레이 중 재컴파일(도메인 리로드)이 일어나면 조합 엔진·프리뷰 참조가 끊기는데,
    /// 플래그만 보면 '준비됨'인 채로 NullReference가 난다(실제로 겪은 문제).
    /// </summary>
    private bool IsReady => _composer != null && _previewUnit != null;
    private string _initError;

    // ── 데이터 ──
    private Dictionary<int, MonsterAppearanceRecipe> _recipes = new Dictionary<int, MonsterAppearanceRecipe>();
    private string _recipeError;
    private readonly List<MonsterRow> _monsterRows = new List<MonsterRow>();

    // ── 현재 프리뷰의 외형 상태 ──
    // 조합·불러오기·파트 교체가 모두 이 목록을 고쳐 다시 적용하는 방식이라, 어디서 왔든 이어서 손볼 수 있다.
    private readonly List<PreviewMatchingElement> _elements = new List<PreviewMatchingElement>();
    private readonly Dictionary<string, string> _partSelection =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // Part → 파일명(콤마 = 듀얼)

    // ── 편집 상태 ──
    private int _selectedCode;
    private int _act = 1;
    private int _stage = 1;
    // Act1·2는 일반 몬스터가 2종이고 강몹이 약몹보다 +1 레벨이다(값 문서 §11-B). Act3~5에는 구분이 없다.
    private bool _strong;
    // 무기 스윙 이펙트(궤적·불티)의 기준색. null이면 기본 불티색이다.
    // 몬스터를 고를 때 레시피·저장된 프리팹에서 읽어 채우고, 저장 시 프리팹에 심는다.
    private Color? _effectColor;
    private bool _batchOnlyMissing = true;
    private bool _busy;
    private readonly List<string> _log = new List<string>();

    private void Awake()
    {
        if (cam == null)
        {
            cam = Camera.main;
        }
    }

    private void Start()
    {
        MasterDataManager.EnsureLoaded();
        LoadRecipes();
        RefreshRows();
        BuildUi();
        Setup();
    }

    // ══════════════════════════════════════════════════════════════════
    //  준비 — 조합 엔진 생성 + 프리뷰 캐릭터 한 명(§5)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 태그 DB·패키지·애니메이터를 에셋에서 읽어 조합 엔진을 만들고, 프리뷰 캐릭터를 세운다.
    /// 실패하면 사유를 상단에 남기고 생성 버튼을 잠근 채 둔다(§6.4).
    /// </summary>
    private void Setup()
    {
        _composer = SpumUnitComposer.Create(out string error);
        if (_composer == null)
        {
            _initError = error;
            Debug.LogError("[CharacterDev] " + error);
            Status(error);
            RefreshUi();
            return;
        }

        if (!SpawnPreviewUnit(out string spawnError))
        {
            _initError = spawnError;
            Debug.LogError("[CharacterDev] " + spawnError);
            Status(spawnError);
            RefreshUi();
            return;
        }

        _initError = null;
        string ready = $"준비 완료 — 파츠 {_composer.Parts.Count}개 / 패키지 {_composer.Packages.Count}개 / "
                       + $"몬스터 {_monsterRows.Count}종 / 레시피 {_recipes.Count}건";
        Status(ready);
        Debug.Log("[CharacterDev] " + ready);
        RefreshUi();
    }

    /// <summary>
    /// 작업 직전에 부르는 준비 확인 — 아직(또는 더 이상) 준비돼 있지 않으면 <see cref="Setup"/>을 한 번 더 돌린다.
    /// <para><b>플레이 중 재컴파일(도메인 리로드)</b>이 일어나면 조합 엔진·프리뷰 참조가 끊기는데,
    /// 그때 버튼이 조용히 죽지 않고 스스로 되살아나게 하기 위한 장치다(실제로 겪은 문제).</para>
    /// </summary>
    private bool EnsureReady(out string error)
    {
        error = null;
        if (IsReady)
        {
            return true;
        }

        Setup();
        if (IsReady)
        {
            return true;
        }

        error = string.IsNullOrEmpty(_initError)
            ? "조합 엔진을 준비하지 못했습니다 — 플레이를 다시 시작해 보세요."
            : _initError;
        return false;
    }

    /// <summary>
    /// 프리뷰 캐릭터(SPUM 유닛 프리팹) 하나를 앵커에 세운다.
    /// 이미 세워져 있으면(도메인 리로드로 참조만 끊긴 경우) 그 오브젝트를 다시 잡아 쓴다.
    /// </summary>
    private bool SpawnPreviewUnit(out string error)
    {
        error = null;
#if UNITY_EDITOR
        var anchor = previewAnchor != null ? previewAnchor : transform;
        var existing = anchor.Find("PreviewUnit");
        if (existing != null)
        {
            _previewUnit = existing.GetComponent<SPUM_Prefabs>();
            if (_previewUnit != null)
            {
                return true;
            }
            DestroyImmediate(existing.gameObject);
        }

        _previewUnit = MonsterUnitFactory.InstantiateUnit(_composer, anchor, "PreviewUnit", out error);
        return _previewUnit != null;
#else
        error = "이 하네스는 에디터에서만 동작한다(N1).";
        return false;
#endif
    }

    // ══════════════════════════════════════════════════════════════════
    //  목록 · 레시피(F1 · §4.2)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>레시피 JSON을 읽어 코드→레시피로 만든다. 실패 사유는 씬 상단에 남긴다.</summary>
    private void LoadRecipes()
    {
        if (recipeJson == null)
        {
            _recipeError = "레시피 TextAsset이 배선되지 않았습니다(Assets/Dev/monster-appearance-recipe.json).";
            _recipes = new Dictionary<int, MonsterAppearanceRecipe>();
            return;
        }

        _recipes = MonsterRecipeBook.Parse(recipeJson.text, out _recipeError);
        if (!string.IsNullOrEmpty(_recipeError))
        {
            Debug.LogWarning("[CharacterDev] " + _recipeError);
        }
    }

    /// <summary>
    /// <c>monster_master</c> + 프리팹 폴더 + 레시피를 합쳐 목록을 다시 만든다.
    /// 마스터에 없는데 프리팹만 있는 코드는 "고아 프리팹"으로 남긴다(삭제하지 않는다 — §6.4).
    /// </summary>
    private void RefreshRows()
    {
        _monsterRows.Clear();

        var db = MasterDataManager.Db;
        var codes = new List<int>();
        if (db != null)
        {
            codes.AddRange(db.Monsters.Keys);
        }

        var prefabCodes = ExistingPrefabCodes();
        foreach (var code in prefabCodes)
        {
            if (!codes.Contains(code))
            {
                codes.Add(code);
            }
        }
        codes.Sort();

        foreach (var code in codes)
        {
            MonsterMaster master = null;
            if (db != null)
            {
                db.Monsters.TryGetValue(code, out master);
            }

            var row = new MonsterRow
            {
                Code = code,
                Name = master != null ? master.name : "(마스터 없음)",
                Hp = master != null ? master.hp : 0,
                Attack = master != null ? master.attack : 0,
                HasPrefab = prefabCodes.Contains(code),
                HasRecipe = _recipes.ContainsKey(code),
                Orphan = master == null,
            };

            // 기준값·등장 레벨 진단(읽기 전용 — §6.3). 코드 규약 밖이면 계산하지 않는다.
            int act = MonsterStatCurve.ActOfCode(code);
            if (act > 0 && master != null)
            {
                bool boss = MonsterStatCurve.IsBossCode(code);
                row.BaseNote = MonsterStatCurve.BaseMatchText(master.hp, master.attack, boss);
                MonsterStatCurve.LevelRange(act, boss, out int minLevel, out int maxLevel);
                row.LevelNote = minLevel == maxLevel ? $"Lv{minLevel}" : $"Lv{minLevel}~{maxLevel}";
            }

            _monsterRows.Add(row);
        }
    }

    /// <summary>프리팹 폴더에 실제로 존재하는 <c>monster_{code}</c> 코드 집합.</summary>
    private HashSet<int> ExistingPrefabCodes()
    {
        var result = new HashSet<int>();
        string folder = EnsureTrailingSlash(monsterPrefabFolder);
        if (!Directory.Exists(folder))
        {
            return result;
        }

        foreach (var path in Directory.GetFiles(folder, "monster_*.prefab", SearchOption.TopDirectoryOnly))
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (int.TryParse(name.Substring("monster_".Length), out int code))
            {
                result.Add(code);
            }
        }
        return result;
    }

    /// <summary>그 코드의 레시피(없으면 코드 시드 랜덤 폴백 — §4.2).</summary>
    private MonsterAppearanceRecipe RecipeFor(int code)
    {
        return _recipes.TryGetValue(code, out var recipe) ? recipe : MonsterAppearanceRecipe.RandomFor(code);
    }

    // ══════════════════════════════════════════════════════════════════
    //  외형 생성(§6.2 1~4)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 레시피대로 파츠를 조합해 프리뷰 캐릭터에 입힌다(저장하지 않는다 — F2).
    /// 순서는 §6.2와 같다: 태그 검증 → 시드 고정 조합 → 요소 변환 → 고정 파츠·색 덮어쓰기 → 적용.
    /// </summary>
    private bool GeneratePreview(int code, out string error)
    {
        if (!EnsureReady(out error))
        {
            return false;
        }

        // 조합 규칙의 정본은 MonsterUnitFactory 하나다 — 에디터 자동 빌더와 같은 코드를 써야
        // "씬에서 만든 외형"과 "자동 생성한 외형"이 갈라지지 않는다.
        var elements = MonsterUnitFactory.BuildAppearance(_composer, RecipeFor(code), out error);
        if (elements == null)
        {
            return false;
        }

        SetElements(elements);
        return true;
    }

    /// <summary>
    /// 현재 외형을 통째로 갈아 끼우고 프리뷰에 반영한다(조합·불러오기 공용).
    /// 파트별 선택 상태(<see cref="_partSelection"/>)도 요소의 스프라이트 경로에서 되읽어 채운다 —
    /// 그래야 이어서 파트를 하나씩 고쳐 나갈 수 있다.
    /// </summary>
    private void SetElements(List<PreviewMatchingElement> elements)
    {
        _elements.Clear();
        _elements.AddRange(elements);
        RebuildPartSelection();
        _composer.ApplyToUnit(_previewUnit, _elements);
        RefreshPartPanel();
    }

    /// <summary>요소 목록에서 "파트 → 파일명" 표를 다시 만든다(듀얼 무기는 콤마로 잇는다).</summary>
    private void RebuildPartSelection()
    {
        _partSelection.Clear();
        foreach (var element in _elements)
        {
            string fileName = SpumUnitComposer.FileNameOf(element);
            if (string.IsNullOrEmpty(element.PartType) || string.IsNullOrEmpty(fileName))
            {
                continue;
            }

            if (!_partSelection.TryGetValue(element.PartType, out string existing))
            {
                _partSelection[element.PartType] = fileName;
            }
            else if (existing != fileName && !existing.Split(',').Contains(fileName))
            {
                _partSelection[element.PartType] = existing + "," + fileName; // 무기 2개 등
            }
        }
    }

    /// <summary>파트 하나만 지정한 파츠로 바꾼다(커스텀 외형 선택 UI). 나머지 파트는 그대로 둔다.</summary>
    private void SetPart(string part, string fileName)
    {
        if (!EnsureReady(out string notReady))
        {
            Log(notReady);
            return;
        }

        var replacements = _composer.BuildElements(fileName, part);
        if (replacements.Count == 0)
        {
            Log($"{part} '{fileName}'의 텍스처를 찾지 못했습니다.");
            return;
        }

        _elements.RemoveAll(e => string.Equals(e.PartType, part, StringComparison.OrdinalIgnoreCase));
        _elements.AddRange(replacements);
        RebuildPartSelection();
        _composer.ApplyToUnit(_previewUnit, _elements);
        RefreshPartPanel();
        Log($"{part} → {fileName}");
    }

    /// <summary>그 파트를 비운다(투구·망토처럼 없어도 되는 파트를 빼는 용도).</summary>
    private void ClearPart(string part)
    {
        if (!EnsureReady(out string notReady))
        {
            Log(notReady);
            return;
        }

        int removed = _elements.RemoveAll(e => string.Equals(e.PartType, part, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
        {
            return;
        }

        RebuildPartSelection();
        _composer.ApplyToUnit(_previewUnit, _elements);
        RefreshPartPanel();
        Log($"{part} 비움");
    }

    /// <summary>
    /// 저장된 <c>monster_{code}.prefab</c>의 외형을 프리뷰로 불러온다(기존 12종 확인·수정용).
    /// 불러온 뒤에는 조합 결과와 똑같이 파트를 고치고 다시 저장할 수 있다.
    /// </summary>
    private bool LoadFromPrefab(int code, out string error)
    {
        error = null;
#if UNITY_EDITOR
        if (!EnsureReady(out error))
        {
            return false;
        }

        string assetPath = EnsureTrailingSlash(monsterPrefabFolder) + "monster_" + code + ".prefab";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (prefab == null)
        {
            error = $"프리팹이 없습니다: {assetPath}";
            return false;
        }

        var elements = SpumUnitComposer.ReadElements(prefab, out error);
        if (elements == null)
        {
            return false;
        }

        SetElements(elements);
        return true;
#else
        error = "에디터에서만 불러올 수 있습니다(N1).";
        return false;
#endif
    }

    // ══════════════════════════════════════════════════════════════════
    //  프리팹 저장(§6.2 5~9 · §7.1)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 현재 프리뷰를 <c>monster_{code}.prefab</c>으로 저장한다(F3).
    /// 같은 이름이 있으면 먼저 백업하고(F7), 저장 직후 3항목을 검증해 실패하면 백업으로 되돌린다(§7.1).
    /// </summary>
    private bool SavePrefab(int code, out string error)
    {
        error = null;
#if UNITY_EDITOR
        if (!EnsureReady(out error))
        {
            return false;
        }
        if (_previewUnit.ImageElement.Count == 0)
        {
            error = "먼저 외형을 생성하세요(프리뷰가 비어 있습니다).";
            return false;
        }

        // 백업·저장·검증·되돌리기는 자동 빌더와 같은 경로를 쓴다(MonsterUnitFactory가 정본).
        if (!MonsterUnitFactory.SaveUnitAsPrefab(
                _composer, _previewUnit, code, monsterPrefabFolder, backupFolder, out error))
        {
            return false;
        }

        // 무기 스윙 이펙트 색을 프리팹에 심는다(씬에서 고른 값이 정본 — null이면 기본 불티색으로 되돌린다).
        MonsterUnitFactory.ApplySwingFxPalette(
            MonsterUnitFactory.PrefabPath(code, monsterPrefabFolder), _effectColor);

        // 저장 후 후처리 훅(N2) — 몬스터는 붙일 것이 없고, 아군 확장 시 여기에 얹는다.
        var saved = AssetDatabase.LoadAssetAtPath<GameObject>(
            MonsterUnitFactory.PrefabPath(code, monsterPrefabFolder));
        PostProcess(saved, UnitKind.Monster, code);
        return true;
#else
        error = "에디터에서만 저장할 수 있습니다(N1).";
        return false;
#endif
    }

    /// <summary>
    /// 저장 후 후처리 훅(N2). 이번 범위(몬스터)는 붙일 컴포넌트가 없다 —
    /// 전투에 필요한 것은 전부 <c>BattleDevController.SpawnMonsterByCode</c>가 런타임에 붙인다(§2.3).
    /// </summary>
    private void PostProcess(GameObject prefabRoot, UnitKind kind, int code)
    {
        var hook = GetComponent<IUnitPostProcess>();
        hook?.Apply(prefabRoot, kind, code);
    }

    /// <summary>기존 에셋을 백업 폴더로 복사한다(마스터 JSON 백업용 — 프리팹 백업은 팩토리가 한다).</summary>
    private string BackupAsset(string assetPath, string baseName)
    {
        return MonsterUnitFactory.BackupAsset(assetPath, baseName, backupFolder);
    }

    // ══════════════════════════════════════════════════════════════════
    //  능력치 산출물(§4.5 · F12)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 클라이언트 번들(<c>Assets/Resources/MasterData/monster_master.json</c>)에 행을 추가/갱신한다.
    /// 고치기 전 원본 사본을 백업 폴더에 남기고, 반영 후 마스터 캐시를 버려 다음 전투에 반영되게 한다.
    /// 정본(서버 문서·DB) 반영은 서버측 작업이다(§7.4).
    /// </summary>
    private bool ApplyStatsToMaster(int code, string name, long hp, long attack, out string error)
    {
        error = null;
#if UNITY_EDITOR
        if (code <= 0)
        {
            error = "몬스터 코드가 올바르지 않습니다.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            error = "이름을 입력하세요.";
            return false;
        }
        if (hp <= 0 || attack <= 0)
        {
            error = "hp·attack은 1 이상의 정수여야 합니다(monster_master는 정수 필드).";
            return false;
        }
        if (!File.Exists(masterJsonPath))
        {
            error = $"마스터 번들을 찾지 못했습니다: {masterJsonPath}";
            return false;
        }

        List<object> rows;
        try
        {
            rows = SPUMJSON.Deserialize(File.ReadAllText(masterJsonPath)) as List<object>;
        }
        catch (Exception e)
        {
            error = $"monster_master.json 파싱 실패: {e.Message}";
            return false;
        }
        if (rows == null)
        {
            error = "monster_master.json이 배열 형식이 아닙니다.";
            return false;
        }

        BackupAsset(masterJsonPath, "monster_master");

        var table = new SortedDictionary<int, (string Name, long Hp, long Attack)>();
        foreach (var entry in rows)
        {
            if (!(entry is Dictionary<string, object> dict))
            {
                continue;
            }
            int c = ToInt(dict, "monsterCode");
            if (c == 0)
            {
                continue;
            }
            table[c] = (dict.TryGetValue("name", out object n) ? n as string : string.Empty,
                        ToInt(dict, "hp"), ToInt(dict, "attack"));
        }

        table[code] = (name.Trim(), hp, attack);

        var sb = new StringBuilder();
        sb.Append("[\n");
        int index = 0;
        foreach (var kv in table)
        {
            sb.Append("  {\n");
            sb.Append($"    \"monsterCode\": {kv.Key},\n");
            sb.Append($"    \"name\": \"{EscapeJson(kv.Value.Name)}\",\n");
            sb.Append($"    \"hp\": {kv.Value.Hp},\n");
            sb.Append($"    \"attack\": {kv.Value.Attack}\n");
            sb.Append(index == table.Count - 1 ? "  }\n" : "  },\n");
            index++;
        }
        sb.Append("]\n");

        File.WriteAllText(masterJsonPath, sb.ToString(), new UTF8Encoding(false));
        AssetDatabase.ImportAsset(masterJsonPath, ImportAssetOptions.ForceUpdate);

        // 캐시를 버려 다음 조회부터 새 값을 읽게 한다(진행 중 전투에는 반영되지 않는다 — §4.5).
        MasterDataManager.Unload();
        MasterDataManager.EnsureLoaded();
        RefreshRows();
        return true;
#else
        error = "에디터에서만 반영할 수 있습니다(N1).";
        return false;
#endif
    }

    /// <summary>
    /// 서버 정본 반영용 스니펫 — §4.5·§7.4.
    /// <para>세 줄이다: ① 번들 JSON 한 줄 ② <c>master-data-값.md</c> §9 표 한 줄 ③ <b>배치 명령</b>.
    /// hp·attack은 통일 기준값이라 몬스터끼리 다르지 않으므로, 실제로 세기를 정하는 값인
    /// <b>등장 레벨</b>은 <c>monster_master</c>가 아니라 <c>stage_spawn</c>에 들어간다(값 문서 §9.4·§11-B).
    /// 그래서 ③이 없으면 스니펫만 반영해도 그 몬스터는 전투에 나오지 않는다.</para>
    /// </summary>
    private static string BuildSnippet(int code, string name, long hp, long attack,
                                       int act, int stage, int level)
    {
        string json = $"{{ \"monsterCode\": {code}, \"name\": \"{EscapeJson(name)}\", \"hp\": {hp}, \"attack\": {attack} }}";
        string table = $"| {code} | {name} | {hp} | {attack} |";
        string spawn = $"# 배치(등장 레벨은 도구가 레벨 곡선에서 넣는다 — Act{act} s{stage} → Lv{level}):\n"
                       + $"python tools/master_monster_tool.py spawn --code {code} --stages \"{stage}:6\"";
        return json + "\n" + table + "\n" + spawn;
    }

    private static int ToInt(Dictionary<string, object> dict, string key)
    {
        if (!dict.TryGetValue(key, out object v) || v == null)
        {
            return 0;
        }
        if (v is long l) return (int)l;
        if (v is double d) return (int)d;
        return int.TryParse(v.ToString(), out int parsed) ? parsed : 0;
    }

    private static string EscapeJson(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    // ══════════════════════════════════════════════════════════════════
    //  일괄 생성(§6.3)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 목록을 순회하며 외형만 생성·저장한다 — 능력치는 건드리지 않는다(§6.3).
    /// 기본 대상은 "프리팹이 없는 코드"이며(N3), 프레임을 나눠 돌려 진행 상황이 화면에 보이게 한다.
    /// </summary>
    private IEnumerator BatchGenerate()
    {
        _busy = true;
        RefreshUi();

        var targets = _monsterRows
            .Where(r => !r.Orphan && (!_batchOnlyMissing || !r.HasPrefab))
            .Select(r => r.Code)
            .ToList();

        Log($"일괄 생성 시작 — 대상 {targets.Count}종 ({(_batchOnlyMissing ? "프리팹 없는 코드만" : "전체")})");

        int ok = 0;
        var failures = new List<string>();

        foreach (var code in targets)
        {
            Status($"일괄 생성 — {code} …");
            yield return null;

            if (!GeneratePreview(code, out string genError))
            {
                failures.Add($"{code}: {genError}");
                continue;
            }
            yield return null;

            if (!SavePrefab(code, out string saveError))
            {
                failures.Add($"{code}: {saveError}");
                continue;
            }

            ok++;
            Log($"  ✔ monster_{code} 저장");
            yield return null;
        }

        RefreshRows();
        RefreshList();
        Log($"일괄 생성 완료 — 성공 {ok} / 실패 {failures.Count}");
        foreach (var f in failures)
        {
            Log("  ✘ " + f);
        }
        if (ok > 0)
        {
            Log("※ '던전 전투 배선'을 다시 실행해야 전투에 반영된다(F8).");
        }

        _busy = false;
        Status($"준비 완료 — 몬스터 {_monsterRows.Count}종 / 레시피 {_recipes.Count}건");
        RefreshUi();
    }

    // ══════════════════════════════════════════════════════════════════
    //  검수(§7.2 · §7.3) · 배선(F8)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>프리뷰 캐릭터로 상태별 대표 클립을 재생한다(F5 · §7.2).</summary>
    private void PlayPreviewAnimation(PlayerState state)
    {
        if (_previewUnit == null)
        {
            Log("프리뷰 캐릭터가 없습니다.");
            return;
        }
        if (_previewUnit._anim == null)
        {
            Log("Animator(_anim)가 없어 재생할 수 없습니다.");
            return;
        }
        if (!_previewUnit.allListsHaveItemsExist())
        {
            _previewUnit.PopulateAnimationLists();
        }
        _previewUnit.PlayAnimation(state, 0);
    }

    /// <summary>대상 몬스터 코드를 EditorPrefs로 넘기고 BattleDevScene으로 넘어간다(§7.3).</summary>
    private void OpenBattleForReview(int code)
    {
#if UNITY_EDITOR
        EditorPrefs.SetInt(SpawnCodePrefKey, code);
        Log($"전투 검수 진입 — monster_{code} 만 스폰합니다.");
        EditorSceneManager.LoadSceneInPlayMode(
            battleDevScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#endif
    }

    /// <summary>
    /// '던전 전투 배선'을 예약하고 플레이를 멈춘다(F8).
    /// 그 빌더는 씬을 열고 저장하므로 플레이 중에는 실행할 수 없어, 에디터 훅이 플레이 종료 후 실행한다.
    /// </summary>
    private void RequestDungeonWiring()
    {
#if UNITY_EDITOR
        EditorPrefs.SetBool(RunWiringPrefKey, true);
        Log("던전 전투 배선을 예약했습니다 — 플레이를 멈추면 자동 실행됩니다.");
        EditorApplication.isPlaying = false;
#endif
    }

    // ══════════════════════════════════════════════════════════════════
    //  공용
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 하네스 자가 점검 — 준비 상태를 콘솔에 남기고, 넘긴 코드로 미리보기 생성을 한 번 돌려 본다.
    /// UI 없이(에디터 스크립트·<c>SendMessage</c>로) 파이프라인이 살아 있는지 확인할 때 쓴다.
    /// </summary>
    public void DevSelfTest(int monsterCode)
    {
        Debug.Log($"[CharacterDev] 자가 점검 — ready={IsReady} / error={_initError ?? "없음"} / "
                  + $"목록 {_monsterRows.Count}종 / 레시피 {_recipes.Count}건");

        if (!EnsureReady(out string notReady))
        {
            Debug.LogWarning("[CharacterDev] 자가 점검 중단 — " + notReady);
            return;
        }

        int code = monsterCode != 0 ? monsterCode : (_monsterRows.Count > 0 ? _monsterRows[0].Code : 0);
        if (code == 0)
        {
            Debug.LogWarning("[CharacterDev] 자가 점검 중단 — 대상 몬스터가 없습니다.");
            return;
        }

        if (GeneratePreview(code, out string error))
        {
            _selectedCode = code;
            SelectMonster(code);
            int drawn = _previewUnit.GetComponentsInChildren<SpriteRenderer>(true).Count(r => r.sprite != null);
            Debug.Log($"[CharacterDev] 자가 점검 통과 — monster_{code} 조합 성공 "
                      + $"(요소 {_previewUnit.ImageElement.Count}개 / 그려진 스프라이트 {drawn}개, "
                      + $"레시피: {RecipeFor(code).Summary()})");
        }
        else
        {
            Debug.LogError($"[CharacterDev] 자가 점검 실패 — monster_{code}: {error}");
        }
    }

    /// <summary>
    /// 코드 하나를 조합해 저장까지 한 번에 수행한다(버튼을 거치지 않는 자동화·점검용 진입점).
    /// 결과는 콘솔에 남기며, 실패해도 다른 산출물을 되돌리지 않는다(§6.2).
    /// </summary>
    public void DevGenerateAndSave(int monsterCode)
    {
        if (!EnsureReady(out string notReady))
        {
            Debug.LogWarning("[CharacterDev] 중단 — " + notReady);
            return;
        }

        if (!GeneratePreview(monsterCode, out string genError))
        {
            Debug.LogError($"[CharacterDev] 조합 실패 — monster_{monsterCode}: {genError}");
            return;
        }

        if (!SavePrefab(monsterCode, out string saveError))
        {
            Debug.LogError($"[CharacterDev] 저장 실패 — monster_{monsterCode}: {saveError}");
            return;
        }

        Debug.Log($"[CharacterDev] 저장 완료 — monster_{monsterCode}.prefab "
                  + "(전투에 반영하려면 '던전 전투 배선'을 다시 실행할 것)");
        RefreshRows();
        RefreshList();
    }

    /// <summary>
    /// 저장된 프리팹의 외형을 프리뷰로 불러온다(버튼을 거치지 않는 자동화·점검용 진입점).
    /// 불러온 파트 구성을 콘솔에 남긴다.
    /// </summary>
    public void DevLoadPrefab(int monsterCode)
    {
        if (LoadFromPrefab(monsterCode, out string error))
        {
            SelectMonster(monsterCode);
            int drawn = _previewUnit.GetComponentsInChildren<SpriteRenderer>(true).Count(r => r.sprite != null);
            string parts = string.Join(" / ", _partSelection.Select(kv => $"{kv.Key} {kv.Value}"));
            Debug.Log($"[CharacterDev] 불러오기 성공 — monster_{monsterCode} "
                      + $"(요소 {_elements.Count}개 / 그려진 스프라이트 {drawn}개)\n  {parts}");
        }
        else
        {
            Debug.LogError($"[CharacterDev] 불러오기 실패 — monster_{monsterCode}: {error}");
        }
    }

    /// <summary>파트 하나를 지정해 갈아 끼운다(자동화·점검용 진입점). 형식: "Part:파일명".</summary>
    public void DevSetPart(string partAndFileName)
    {
        var split = (partAndFileName ?? string.Empty).Split(':');
        if (split.Length != 2)
        {
            Debug.LogWarning("[CharacterDev] 형식은 \"Part:파일명\" 입니다.");
            return;
        }
        SetPart(split[0].Trim(), split[1].Trim());
    }

    private static string EnsureTrailingSlash(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path;
        }
        path = path.Replace("\\", "/");
        return path.EndsWith("/") ? path : path + "/";
    }

    /// <summary>하네스 로그(최근 12줄만 화면에 남긴다).</summary>
    private void Log(string message)
    {
        _log.Add(message);
        if (_log.Count > 12)
        {
            _log.RemoveAt(0);
        }
        UpdateLogText();
    }
}
