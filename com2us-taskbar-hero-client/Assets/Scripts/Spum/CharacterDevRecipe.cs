using System.Collections.Generic;
using UnityEngine;

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
/// 지역(Act)·스테이지에서 추천 능력치를 뽑는 산출식(§4.4).
/// <para>일반(1~9)은 그 Act 하한에서 다음 Act 하한까지 9칸 기하 보간, 보스(10)는 실측값을 그대로 쓴다.
/// 추천은 <b>확정값이 아니라 시작점</b>이며(§4.3), 값 자체의 조정은 서버 밸런스 결정 사항이다(§8 M9).</para>
/// </summary>
public static class MonsterStatCurve
{
    public const int ActCount = 5;
    public const int StagePerAct = 10;
    public const int BossStage = 10;

    // Act별 일반 몬스터 하한(§2.5 실측).
    private static readonly int[] HpFloor = { 38, 100, 150, 300, 625 };
    private static readonly int[] AtkFloor = { 3, 10, 28, 36, 52 };

    // Act5는 다음 Act가 없어 실측 Act 간 평균 배율로 외삽한다(§4.4).
    private const float Act5HpGrowth = 2.0f;
    private const float Act5AtkGrowth = 1.44f;

    // 보스(스테이지 10) 실측값 — 보간하지 않고 그대로 추천한다.
    private static readonly int[] BossHp = { 500, 1000, 2000, 5000, 10000 };
    private static readonly int[] BossAtk = { 7, 22, 39, 50, 75 };

    /// <summary>
    /// Act(1~5)·스테이지(1~10)의 추천 hp·attack.
    /// <paramref name="difficultyMultiplier"/>는 난이도 배율 자리(기본 1.0)이며, 현재 난이도는 몬스터를
    /// 재사용해 값이 같으므로 UI에 축이 없다(§4.3 · §8 M8).
    /// </summary>
    public static void Recommend(int act, int stage, float difficultyMultiplier, out long hp, out long attack)
    {
        act = Mathf.Clamp(act, 1, ActCount);
        stage = Mathf.Clamp(stage, 1, StagePerAct);
        float mult = difficultyMultiplier > 0f ? difficultyMultiplier : 1f;

        if (stage == BossStage)
        {
            hp = Scale(BossHp[act - 1], mult);
            attack = Scale(BossAtk[act - 1], mult);
            return;
        }

        hp = Scale(Interpolate(HpFloor, act, stage, Act5HpGrowth), mult);
        attack = Scale(Interpolate(AtkFloor, act, stage, Act5AtkGrowth), mult);
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

    /// <summary>추천값 대비 차이를 "+9.7%" 형태로 표기한다(추천이 0이면 "-").</summary>
    public static string DiffText(long value, long recommended)
    {
        if (recommended <= 0)
        {
            return "-";
        }
        float ratio = (value - recommended) / (float)recommended * 100f;
        if (Mathf.Abs(ratio) < 0.05f)
        {
            return "±0%";
        }
        return (ratio > 0f ? "+" : "") + ratio.ToString("0.#") + "%";
    }

    /// <summary>그 Act 하한에서 다음 Act 하한까지 9칸 기하 보간(s = 1..9).</summary>
    private static float Interpolate(int[] floors, int act, int stage, float lastActGrowth)
    {
        float current = floors[act - 1];
        float next = act < ActCount ? floors[act] : current * lastActGrowth;
        if (current <= 0f || next <= 0f)
        {
            return current;
        }
        float r = Mathf.Pow(next / current, 1f / 9f);
        return current * Mathf.Pow(r, stage - 1);
    }

    /// <summary>난이도 배율을 곱해 정수로 반올림한다(0 이하가 되지 않게 1로 보정).</summary>
    private static long Scale(float value, float multiplier)
    {
        long rounded = (long)Mathf.Floor(value * multiplier + 0.5f);
        return rounded < 1 ? 1 : rounded;
    }
}
