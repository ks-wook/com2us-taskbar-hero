using System.Collections.Generic;
using UnityEngine;
using TaskbarHero.Client.MasterData;

/// <summary>
/// CharacterDevScene(몬스터 유닛 자동생성)이 쓰는 순수 데이터·계산부
/// (기획서 <c>docs/캐릭터-개발씬-기획서.md</c> §4).
///
/// <para>SPUM·UI에 의존하지 않는 부분만 모아 둔다 — 외형 레시피 스키마·파싱(§4.2),
/// 능력치 추천 산출식(§4.4), 신규 코드 채번, 저장 후 후처리 훅(N2).
/// 컨트롤러(<see cref="CharacterDevController"/>)와 같은 Assembly-CSharp에 둔다.</para>
/// </summary>

/// <summary>유닛 종류. 이번 범위는 <see cref="Monster"/>만 구현하고 아군은 다음 단계다(N2).</summary>
public enum UnitKind
{
    Monster,
    Ally,
}

/// <summary>
/// 프리팹 저장 직후 실행되는 후처리 훅(N2). 몬스터는 붙일 것이 없어 빈 구현이며,
/// 아군 확장 시 <c>SpumCharacterAnimator</c>·<c>BoxCollider2D</c>·<c>SelectableCharacter</c> 부착과
/// <c>CharacterPrefabDatabase</c> 등록을 여기에 얹는다.
/// </summary>
public interface IUnitPostProcess
{
    /// <summary>저장된 프리팹 에셋의 루트에 유닛 종류별 후처리를 적용한다.</summary>
    void Apply(GameObject prefabRoot, UnitKind kind, int code);
}

/// <summary>몬스터 외형 레시피 1건(§4.2). 값이 비면 그 축은 "무제한"이다.</summary>
public sealed class MonsterAppearanceRecipe
{
    public int monsterCode;
    public string note = string.Empty;
    public string race = string.Empty;
    public string gender = string.Empty;
    public string theme = string.Empty;
    public readonly List<string> classes = new List<string>();
    public int seed;

    /// <summary>태그 조합을 덮어쓰는 고정 파츠. 키 = Part(Weapons·Helmet…), 값 = SPUM 파일명(콤마로 복수).</summary>
    public readonly Dictionary<string, string> fixedParts = new Dictionary<string, string>();

    /// <summary>계열 팔레트. Body·Hair·Cloth 3키만 쓴다(§4.2). 값은 "#RRGGBB".</summary>
    public readonly Dictionary<string, string> colors = new Dictionary<string, string>();

    /// <summary>예약 필드(§4.1). 값이 있으면 태그 조합을 건너뛰고 이 코드로 복원 — 이번 범위에서는 미구현.</summary>
    public string shareCode = string.Empty;

    /// <summary>
    /// 무기 스윙 이펙트(궤적·불티)의 기준색 "#RRGGBB". 비면 기본 불티색을 쓴다.
    /// 보스는 지역(Act) 컬러링을 여기에 적어 둔다(<c>MonsterSwingFxPalette.ActColor</c>와 같은 값).
    /// </summary>
    public string effectColor = string.Empty;

    /// <summary>레시피의 이펙트 색을 Color로 해석한다(비었거나 형식이 틀리면 null).</summary>
    public Color? EffectColorOrNull()
    {
        if (string.IsNullOrEmpty(effectColor))
        {
            return null;
        }
        return ColorUtility.TryParseHtmlString(effectColor, out Color c) ? c : (Color?)null;
    }

    /// <summary>레시피가 지정한 시드(없으면 몬스터 코드). 같은 시드면 같은 외형이 나온다(F6).</summary>
    public int EffectiveSeed => seed != 0 ? seed : monsterCode;

    /// <summary>레시피 없이 코드만으로 만드는 전 축 무제한 랜덤 레시피(§4.2 폴백).</summary>
    public static MonsterAppearanceRecipe RandomFor(int monsterCode)
    {
        return new MonsterAppearanceRecipe
        {
            monsterCode = monsterCode,
            note = "(레시피 없음 — 코드 시드 랜덤)",
            seed = monsterCode,
        };
    }

    /// <summary>씬 우측 패널에 한 줄로 보여 줄 태그 요약.</summary>
    public string Summary()
    {
        string cls = classes.Count > 0 ? string.Join("+", classes) : "-";
        return $"race {Or(race)} / gender {Or(gender)} / theme {Or(theme)} / class {cls} / seed {EffectiveSeed}";
    }

    private static string Or(string v) => string.IsNullOrEmpty(v) ? "무제한" : v;
}

/// <summary>
/// 레시피 JSON(<c>Assets/Dev/monster-appearance-recipe.json</c>)을 읽어 코드→레시피로 만든다.
/// <para>파싱은 SPUM이 이미 갖고 있는 MiniJSON(<c>SPUMJSON</c>)을 <b>읽기 전용으로</b> 쓴다 —
/// Unity <c>JsonUtility</c>는 <c>fixedParts</c>·<c>colors</c> 같은 딕셔너리를 다루지 못하고,
/// 그 형식은 기획서 §4.2에 고정된 스키마이기 때문이다(서드파티 수정 없음 = N4).</para>
/// </summary>
public static class MonsterRecipeBook
{
    /// <summary>JSON 문자열을 코드→레시피 사전으로 파싱한다. 형식이 깨졌으면 빈 사전 + <paramref name="error"/>.</summary>
    public static Dictionary<int, MonsterAppearanceRecipe> Parse(string json, out string error)
    {
        var result = new Dictionary<int, MonsterAppearanceRecipe>();
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "레시피 JSON이 비어 있습니다.";
            return result;
        }

        Dictionary<string, object> root;
        try
        {
            root = SPUMJSON.DeserializeObject(json);
        }
        catch (System.Exception e)
        {
            error = $"레시피 JSON 파싱 실패: {e.Message}";
            return result;
        }

        if (root == null || !root.TryGetValue("recipes", out object recipesObj) || !(recipesObj is List<object> recipes))
        {
            error = "레시피 JSON에 recipes 배열이 없습니다.";
            return result;
        }

        foreach (var entry in recipes)
        {
            if (!(entry is Dictionary<string, object> dict))
            {
                continue;
            }

            int code = ReadInt(dict, "monsterCode", 0);
            if (code == 0)
            {
                continue; // monsterCode만 필수(§4.2) — 없으면 그 항목은 건너뛴다
            }

            var recipe = new MonsterAppearanceRecipe
            {
                monsterCode = code,
                note = ReadString(dict, "note"),
                race = ReadString(dict, "race"),
                gender = ReadString(dict, "gender"),
                theme = ReadString(dict, "theme"),
                seed = ReadInt(dict, "seed", 0),
                shareCode = ReadString(dict, "shareCode"),
                effectColor = ReadString(dict, "effectColor"),
            };

            if (dict.TryGetValue("classes", out object classesObj) && classesObj is List<object> classList)
            {
                foreach (var c in classList)
                {
                    string v = c as string;
                    if (!string.IsNullOrEmpty(v))
                    {
                        recipe.classes.Add(v);
                    }
                }
            }

            ReadStringMap(dict, "fixedParts", recipe.fixedParts);
            ReadStringMap(dict, "colors", recipe.colors);

            result[code] = recipe;
        }

        return result;
    }

    private static void ReadStringMap(Dictionary<string, object> dict, string key, Dictionary<string, string> into)
    {
        if (!dict.TryGetValue(key, out object obj) || !(obj is Dictionary<string, object> map))
        {
            return;
        }

        foreach (var kv in map)
        {
            string v = kv.Value as string;
            if (!string.IsNullOrEmpty(v))
            {
                into[kv.Key] = v;
            }
        }
    }

    private static string ReadString(Dictionary<string, object> dict, string key)
    {
        return dict.TryGetValue(key, out object v) && v is string s ? s : string.Empty;
    }

    private static int ReadInt(Dictionary<string, object> dict, string key, int fallback)
    {
        if (!dict.TryGetValue(key, out object v) || v == null)
        {
            return fallback;
        }
        if (v is long l) return (int)l;
        if (v is double d) return (int)d;
        return int.TryParse(v.ToString(), out int parsed) ? parsed : fallback;
    }
}

/// <summary>
/// 지역(Act)·스테이지에서 <b>그 자리의 등장 레벨</b>을 추천하는 산출식(§4.4).
///
/// <para><b>추천 대상은 hp·attack이 아니다.</b> <c>monster_master</c>의 <c>hp</c>·<c>attack</c>은
/// <b>역할별 통일 기준값</b>(일반 50/5 · 보스 50/1)이라 몬스터마다 고를 값이 아니고, 세기는 전부
/// <c>stage_spawn.monster_level</c>이 만든다(값 문서 §9 · §9.4 · §11-B). 그래서 이 씬이 고를 것은
/// <b>등장 레벨</b>이며, 실제 전투 스탯은 기준값에 <see cref="MonsterStats"/>의 레벨 배율을 곱해 얻는다
/// — <b>배율(1.25·1.18)을 여기서 다시 정의하지 않는다.</b></para>
///
/// <para>산식의 정본은 서버 도구 <c>tools/master_monster_tool.py</c>의
/// <c>BASE_HP</c>·<c>BASE_ATTACK</c>·<c>SPAWN_LEVEL</c>·<c>STRONG_LEVEL_GAP</c>·<c>BOSS_LEVEL</c>·
/// <c>spawn_level()</c>·<c>stats_at()</c>·<c>recommend()</c>이고 이 클래스는 그것을 그대로 옮긴 것이다.
/// 한쪽만 고치면 씬의 추천이 정본과 갈라진다.</para>
///
/// <para>난이도(1·2) 축은 <b>없다</b> — 난이도 2는 난이도 1과 스폰 구성이 같고 보상만 다르므로(값 문서 §11)
/// 몬스터 레벨도 같다. 옛 <c>difficultyMultiplier</c> 자리는 제거했다.</para>
/// </summary>
public static class MonsterStatCurve
{
    public const int ActCount = 5;
    public const int StagePerAct = 10;
    public const int BossStage = 10;

    /// <summary>레벨 1 기준 HP. <b>12종 전부 이 값</b>이며 역할로도 갈리지 않는다(값 문서 §9).</summary>
    public const long BaseHp = 50L;

    /// <summary>레벨 1 기준 공격력(일반 몬스터).</summary>
    public const long NormalBaseAttack = 5L;

    /// <summary>레벨 1 기준 공격력(보스). 같은 레벨 일반 몬스터의 1/5 — 보스는 "한 방이 아픈 적"이 아니라
    /// "오래 버티는 적"이고, 그 버팀은 <c>attack</c>이 아니라 레벨(=체력)이 만든다(값 문서 §9).</summary>
    public const long BossBaseAttack = 1L;

    /// <summary>Act1·2 강몹이 같은 자리 약몹보다 갖는 레벨 차(+1 — 체력 ×1.25 · 공격 ×1.18).</summary>
    public const int StrongLevelGap = 1;

    /// <summary>약몹/강몹이 갈리는 마지막 Act. 일반 몬스터가 2종인 구간(Act1·2)뿐이다.</summary>
    public const int StrongMaxAct = 2;

    /// <summary>일반 몬스터 등장 레벨 — <c>[act-1]</c> = { s1~3, s4~6, s7~10 }.
    /// 지역 안 3스테이지마다 +1, 지역 간 +3~4(값 문서 §11-B).</summary>
    private static readonly int[,] SpawnLevelTable =
    {
        { 1, 2, 3 },
        { 8, 9, 10 },
        { 15, 16, 17 },
        { 19, 20, 21 },
        { 24, 25, 26 },
    };

    /// <summary>보스 등장 레벨(Act1~5). 그 지역 잡몹보다 8~13 위다(값 문서 §11-B).</summary>
    private static readonly int[] BossLevelTable = { 14, 24, 28, 31, 34 };

    /// <summary>역할별 통일 기준 공격력.</summary>
    public static long BaseAttackOf(bool boss) => boss ? BossBaseAttack : NormalBaseAttack;

    /// <summary>그 Act에 약몹/강몹 구분이 있는지(일반 몬스터가 2종인 Act1·2만).</summary>
    public static bool SupportsStrong(int act) => act >= 1 && act <= StrongMaxAct;

    /// <summary>스테이지가 속한 레벨 구간(0 = s1~3 · 1 = s4~6 · 2 = s7~10).</summary>
    public static int BandOf(int stage)
    {
        stage = Mathf.Clamp(stage, 1, StagePerAct);
        return stage <= 3 ? 0 : (stage <= 6 ? 1 : 2);
    }

    /// <summary>
    /// 그 자리(Act · 스테이지 · 역할)의 등장 레벨. 보스는 스테이지와 무관하게 그 Act의 보스 레벨이고,
    /// 일반은 구간 레벨 + (Act1·2 강몹이면 <see cref="StrongLevelGap"/>)이다.
    /// </summary>
    public static int SpawnLevel(int act, int stage, bool boss, bool strong)
    {
        act = Mathf.Clamp(act, 1, ActCount);
        if (boss)
        {
            return BossLevelTable[act - 1];
        }
        int level = SpawnLevelTable[act - 1, BandOf(stage)];
        return level + (strong && SupportsStrong(act) ? StrongLevelGap : 0);
    }

    /// <summary>
    /// 통일 기준값에 그 레벨의 배율을 적용한 실제 전투 스탯.
    /// <para>배율은 <see cref="MonsterStats.Scale(long,long,int,out long,out long)"/>가 정본이다 —
    /// 전투에서 실제로 쓰이는 것과 <b>같은 코드</b>라야 씬이 보여 주는 숫자가 게임 안 값과 일치한다.</para>
    /// </summary>
    public static void StatsAt(int level, bool boss, out long hp, out long attack)
    {
        MonsterStats.Scale(BaseHp, BaseAttackOf(boss), Mathf.Max(1, level), out hp, out attack);
    }

    /// <summary>
    /// 그 자리의 추천 — <paramref name="level"/>(고를 값)과 통일 기준값, 그 레벨에서의 실제 스탯.
    /// <para>기준값은 언제나 통일값이므로 새 몬스터를 추가할 때 사람이 고를 것은 <b>등장 레벨뿐</b>이고,
    /// <paramref name="hp"/>·<paramref name="attack"/>은 "그래서 전투에서 얼마가 되는가"를 보여 주는
    /// 읽기 전용 결과다(마스터 행에 넣는 값이 아니다).</para>
    /// </summary>
    public static void Recommend(int act, int stage, bool boss, bool strong,
                                 out int level, out long baseHp, out long baseAttack,
                                 out long hp, out long attack)
    {
        level = SpawnLevel(act, stage, boss, strong);
        baseHp = BaseHp;
        baseAttack = BaseAttackOf(boss);
        StatsAt(level, boss, out hp, out attack);
    }

    /// <summary>그 Act에서 이 역할이 등장할 수 있는 레벨 범위(목록 표시용). 보스는 한 값이라 min = max다.</summary>
    public static void LevelRange(int act, bool boss, out int min, out int max)
    {
        act = Mathf.Clamp(act, 1, ActCount);
        if (boss)
        {
            min = max = BossLevelTable[act - 1];
            return;
        }
        min = SpawnLevelTable[act - 1, 0];
        max = SpawnLevelTable[act - 1, 2] + (SupportsStrong(act) ? StrongLevelGap : 0);
    }

    /// <summary>몬스터 코드에서 Act를 읽는다(90xx=1 … 94xx=5). 규약 밖 코드는 0(§2.5).</summary>
    public static int ActOfCode(int monsterCode)
    {
        int act = (monsterCode - 9000) / 100 + 1;
        return act >= 1 && act <= ActCount ? act : 0;
    }

    /// <summary>그 대역의 보스 코드(xx99)인지.</summary>
    public static bool IsBossCode(int monsterCode)
    {
        return ActOfCode(monsterCode) > 0 && monsterCode % 100 == 99;
    }

    /// <summary>
    /// 코드만 보고 강몹으로 추정한다 — Act1·2의 두 번째 일반 코드부터가 강몹이다(9002·9102).
    /// <b>산식이 아니라 UI 기본값</b>이며(마스터 데이터에 약/강 컬럼은 없다) 씬에서 토글로 바꿀 수 있다.
    /// </summary>
    public static bool GuessStrongFromCode(int monsterCode)
    {
        int act = ActOfCode(monsterCode);
        return SupportsStrong(act) && !IsBossCode(monsterCode) && monsterCode % 100 >= 2;
    }

    /// <summary>
    /// 지역·스테이지 선택에 맞는 신규 코드를 제안한다(§4.4 채번).
    /// 일반은 그 대역 01부터 비어 있는 번호를 순차로, 보스는 그 대역의 xx99를 쓴다.
    /// 쓸 번호가 없으면 0(호출부가 경고).
    /// </summary>
    public static int SuggestCode(int act, bool boss, ICollection<int> usedCodes)
    {
        act = Mathf.Clamp(act, 1, ActCount);
        int band = 9000 + (act - 1) * 100;

        if (boss)
        {
            return band + 99;
        }

        for (int n = 1; n <= 98; n++)
        {
            int candidate = band + n;
            if (usedCodes == null || !usedCodes.Contains(candidate))
            {
                return candidate;
            }
        }
        return 0;
    }

    /// <summary>입력한 hp·attack이 그 역할의 통일 기준값과 같은지(§9 — 어긋나면 레벨이 무의미해진다).</summary>
    public static bool MatchesBase(long hp, long attack, bool boss)
    {
        return hp == BaseHp && attack == BaseAttackOf(boss);
    }

    /// <summary>기준값 일치 여부를 한 줄로 적는다(목록·우측 패널 공용).</summary>
    public static string BaseMatchText(long hp, long attack, bool boss)
    {
        if (MatchesBase(hp, attack, boss))
        {
            return "기준값 일치";
        }
        return $"기준값 어긋남(→ {BaseHp}/{BaseAttackOf(boss)})";
    }
}
