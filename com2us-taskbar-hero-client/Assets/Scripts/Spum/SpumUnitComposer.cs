using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// CharacterDevScene이 쓰는 <b>SPUM 조합·저장 엔진</b>(기획서 <c>docs/캐릭터-개발씬-기획서.md</c> §2.1·§6.2).
///
/// <para><b>SPUM_Scene을 재활용하지 않는다.</b> 그 씬은 편집 UI·페이지네이션·프리셋까지 통째로 딸려 오고,
/// 조합 진입점이 UI Toggle·파츠 버튼에 묶여 있어 하네스가 UI 계층에 종속된다. 그래서 자동생성에 필요한
/// 네 가지 기능만 여기로 뽑아 재구현했다 — <b>태그 DB 읽기 · 파츠 조합 · 프리뷰 적용 · 프리팹 저장</b>.
/// 화면에는 <b>캐릭터(프리뷰 유닛) 하나만</b> 뜬다.</para>
///
/// <para>SPUM 서드파티 코드는 여전히 손대지 않는다(N4). 데이터(<c>SpumPackage</c>·
/// <c>ImprovedCharacterDataItem</c>·<c>PreviewMatchingElement</c>)와 프리팹 에셋을 <b>읽기만</b> 하며,
/// 그 데이터를 다루는 절차만 이 클래스가 직접 구현한다:</para>
/// <list type="bullet">
/// <item>태그 DB(파츠 652개) — <c>SPUM_PromptTagManager.prefab</c> 에셋의 <c>allCharacterData</c>를
///       인스턴스화 없이 필드로 읽는다(그 컴포넌트의 Awake/Start는 돌지 않는다).</item>
/// <item>패키지 — <c>Resources</c>의 <c>*Index*</c> JSON을 직접 파싱한다(SPUM_Manager.LoadPackages와 동일).</item>
/// <item>애니메이터 — <c>SPUM_Manager.prefab</c>에 직렬화된 유닛 타입별 컨트롤러를 읽는다.</item>
/// <item>프리뷰 유닛 — <c>SpritePrefab.prefab</c>(SPUM_Prefabs + 파츠별 SPUM_MatchingList) 인스턴스 하나.</item>
/// </list>
/// </summary>
public sealed class SpumUnitComposer
{
    public const string TagDataPrefabPath =
        "Assets/Thirdparty/SPUM/Core/Basic_Resources/Prefab/SPUM_PromptTagManager.prefab";
    public const string ManagerPrefabPath =
        "Assets/Thirdparty/SPUM/Core/Basic_Resources/Prefab/SPUM_Manager.prefab";
    /// <summary>
    /// 프리뷰 캐릭터 프리팹. SPUM 편집 씬이 프리뷰로 쓰는 그 유닛이며(<c>ChracterPivot</c>의 자식),
    /// 루트가 <c>SPUM_Prefabs</c> + <c>UnitRoot</c>/<c>HorseRoot</c> 구성이라 저장하면
    /// 기존 몬스터 프리팹(루트 + UnitRoot)과 같은 형태가 된다.
    /// </summary>
    public const string PreviewUnitPrefabPath =
        "Assets/Thirdparty/SPUM/Core/Basic_Resources/Prefab/SpritePrefab.prefab";

    /// <summary>이 하네스가 만드는 유닛 타입. 몬스터·아군 모두 SPUM의 "Unit"이다(Horse 등은 쓰지 않는다).</summary>
    public const string UnitTypeName = "Unit";

    /// <summary>파츠 조합 순서(SPUM의 우선순위와 동일). Body·Eye는 필수, 나머지는 있으면 채운다.</summary>
    private static readonly string[] PartPriority =
    {
        "Body", "Eye", "Hair", "FaceHair", "Cloth", "Pant", "Armor", "Helmet", "Weapons", "Back",
    };

    /// <summary>태그 DB의 파츠 목록(읽기 전용). 레시피 검증·필터링에 쓴다.</summary>
    public List<SPUM_ImprovedTagManager.ImprovedCharacterDataItem> Parts { get; private set; }
        = new List<SPUM_ImprovedTagManager.ImprovedCharacterDataItem>();

    /// <summary>Resources에서 읽은 패키지 마스터 목록(텍스처·애니메이션 인덱스).</summary>
    public List<SpumPackage> Packages { get; private set; } = new List<SpumPackage>();

    /// <summary>유닛 타입별 애니메이터 컨트롤러(SPUM_Manager.prefab에서 읽음).</summary>
    private readonly Dictionary<string, RuntimeAnimatorController> _animators =
        new Dictionary<string, RuntimeAnimatorController>();

    /// <summary>저장물에 기록할 SPUM 버전(SPUM_Manager.prefab의 값을 그대로 따른다).</summary>
    public float Version { get; private set; }

    // ══════════════════════════════════════════════════════════════════
    //  준비
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 태그 DB·패키지·애니메이터를 프로젝트 에셋에서 읽어 조합 엔진을 만든다.
    /// 하나라도 없으면 <paramref name="error"/>에 사유를 담아 null을 돌려준다(하네스가 생성 버튼을 잠근다).
    /// </summary>
    public static SpumUnitComposer Create(out string error)
    {
        error = null;
#if UNITY_EDITOR
        var composer = new SpumUnitComposer();

        var tagPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TagDataPrefabPath);
        if (tagPrefab == null)
        {
            error = $"태그 DB 프리팹을 찾지 못했습니다: {TagDataPrefabPath}";
            return null;
        }

        // 컴포넌트를 씬에 인스턴스화하지 않고 프리팹 에셋의 직렬화 값만 읽는다(Awake/Start가 돌지 않는다).
        var tagManager = tagPrefab.GetComponent<SPUM_ImprovedTagManager>();
        if (tagManager == null || tagManager.allCharacterData == null || tagManager.allCharacterData.Count == 0)
        {
            error = "태그 DB(allCharacterData)가 비어 있습니다.";
            return null;
        }
        composer.Packages = LoadPackages();
        if (composer.Packages.Count == 0)
        {
            error = "Resources에서 SPUM 패키지 인덱스(*Index*.json)를 찾지 못했습니다.";
            return null;
        }

        // 태그 DB는 SPUM이 파는 애드온 전체를 담고 있어, 이 프로젝트에 설치되지 않은 파츠까지 들어 있다
        // (예: Elf·Undead 애드온). 그런 파츠를 고르면 스프라이트가 없어 몸통이 통째로 비므로,
        // 설치된 패키지에 텍스처가 실재하는 항목만 남긴다. SPUM 원본은 경고만 남기고 빈 파츠를 만든다.
        composer.Parts = tagManager.allCharacterData
            .Where(item => item != null && composer.HasTexture(item.FileName, item.Part))
            .ToList();
        if (composer.Parts.Count == 0)
        {
            error = "설치된 SPUM 패키지에 대응하는 파츠가 없습니다(태그 DB와 패키지가 어긋납니다).";
            return null;
        }

        var managerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ManagerPrefabPath);
        var manager = managerPrefab != null ? managerPrefab.GetComponent<SPUM_Manager>() : null;
        if (manager == null || manager.SPUM_Animator == null || manager.SPUM_Animator.Length == 0)
        {
            error = $"애니메이터 정의를 찾지 못했습니다: {ManagerPrefabPath}";
            return null;
        }
        foreach (var entry in manager.SPUM_Animator)
        {
            if (entry != null && !string.IsNullOrEmpty(entry.Type) && entry.RuntimeAnimator != null)
            {
                composer._animators[entry.Type] = entry.RuntimeAnimator;
            }
        }
        composer.Version = manager._version;

        if (!composer._animators.ContainsKey(UnitTypeName))
        {
            error = $"'{UnitTypeName}' 애니메이터 컨트롤러가 없습니다.";
            return null;
        }

        return composer;
#else
        error = "이 엔진은 에디터에서만 동작한다(N1).";
        return null;
#endif
    }

    /// <summary>
    /// Resources의 <c>*Index*</c> JSON을 모두 읽어 패키지 목록을 만든다(SPUM_Manager.LoadPackages 재구현).
    /// 생성일 순으로 정렬해 SPUM과 같은 순서를 유지한다.
    /// </summary>
    private static List<SpumPackage> LoadPackages()
    {
        var packages = new List<SpumPackage>();
        foreach (var asset in Resources.LoadAll<TextAsset>(""))
        {
            if (asset == null || !asset.name.Contains("Index"))
            {
                continue;
            }
            var package = JsonUtility.FromJson<SpumPackage>(asset.text);
            if (package != null)
            {
                packages.Add(package);
            }
        }

        return packages
            .OrderBy(p => ParseDate(p.CreationDate))
            .ToList();
    }

    private static DateTime ParseDate(string value)
    {
        return DateTime.TryParseExact(value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out DateTime parsed)
            ? parsed
            : DateTime.MinValue;
    }

    /// <summary>
    /// 프리뷰 유닛을 조합 가능한 초기 상태로 만든다(SPUM_Manager의 SetType + 패키지 초기화 재구현).
    /// <para>유닛 타입에 맞는 자식만 남기고(나머지는 비활성 — 저장 시 제거된다), Animator를 배선하며,
    /// 애니메이션 클립이 채워지도록 Legacy 패키지의 클립을 <c>HasData</c>로 표시해 넣는다
    /// (기존 12종 몬스터 프리팹의 클립 구성과 같은 경로다).</para>
    /// </summary>
    public void InitUnit(SPUM_Prefabs unit)
    {
        if (unit == null)
        {
            return;
        }

        unit.UnitType = UnitTypeName;
        foreach (Transform child in unit.transform)
        {
            child.gameObject.SetActive(child.name.Contains(UnitTypeName));
        }
        unit._version = Version;
        unit.ImageElement.Clear();
        unit.spumPackages = ClonePackagesWithLegacyEnabled();

        // 애니메이터까지 배선해 두면 저장 전에도 그 자리에서 애니메이션을 확인할 수 있다(F5).
        unit._anim = unit.GetComponentInChildren<Animator>(true);
        if (unit._anim != null && _animators.TryGetValue(UnitTypeName, out var controller))
        {
            unit._anim.runtimeAnimatorController = controller;
        }
        unit.PopulateAnimationLists();
    }

    /// <summary>패키지 마스터를 깊은 복사하고 Legacy 패키지의 클립을 사용 상태로 표시한다.</summary>
    private List<SpumPackage> ClonePackagesWithLegacyEnabled()
    {
        var clones = Packages.Select(p => (SpumPackage)p.Clone()).ToList();
        foreach (var package in clones)
        {
            if (!string.Equals(package.Name, "Legacy", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            foreach (var clip in package.SpumAnimationData)
            {
                clip.HasData = true;
            }
        }
        return clones;
    }

    // ══════════════════════════════════════════════════════════════════
    //  조합(§6.2 1~2)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 레시피의 태그로 파츠를 걸러 파트별로 하나씩 고른다.
    /// <para>호출 직전에 <c>UnityEngine.Random.InitState(seed)</c>를 부르면 같은 레시피가 항상 같은 결과를 낸다(F6).
    /// 무기는 SPUM과 같은 규칙을 따른다 — 방패 50%, 방패가 있으면 70% 확률로 듀얼, 둘 다 없으면 최소 하나.</para>
    /// </summary>
    public Dictionary<string, SPUM_ImprovedTagManager.ImprovedCharacterDataItem> Compose(
        MonsterAppearanceRecipe recipe)
    {
        var result = new Dictionary<string, SPUM_ImprovedTagManager.ImprovedCharacterDataItem>();
        var filtered = Filter(recipe);
        if (filtered.Count == 0)
        {
            return result;
        }

        var byPart = filtered
            .GroupBy(item => item.Part)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var part in PartPriority)
        {
            if (string.Equals(part, "Weapons", StringComparison.OrdinalIgnoreCase))
            {
                ComposeWeapons(byPart, result);
                continue;
            }
            if (byPart.TryGetValue(part, out var items) && items.Count > 0)
            {
                result[part] = items[UnityEngine.Random.Range(0, items.Count)];
            }
        }

        return result;
    }

    /// <summary>무기 조합 — 방패(Left)와 일반 무기(Right)를 SPUM과 같은 확률 규칙으로 고른다.</summary>
    private static void ComposeWeapons(
        Dictionary<string, List<SPUM_ImprovedTagManager.ImprovedCharacterDataItem>> byPart,
        Dictionary<string, SPUM_ImprovedTagManager.ImprovedCharacterDataItem> result)
    {
        if (!byPart.TryGetValue("Weapons", out var weapons) || weapons.Count == 0)
        {
            return;
        }

        var shields = weapons.Where(IsShield).ToList();
        var others = weapons.Where(w => !IsShield(w)).ToList();

        SPUM_ImprovedTagManager.ImprovedCharacterDataItem shield = null;
        SPUM_ImprovedTagManager.ImprovedCharacterDataItem weapon = null;

        if (shields.Count > 0 && UnityEngine.Random.value < 0.5f)
        {
            shield = shields[UnityEngine.Random.Range(0, shields.Count)];
        }
        if (others.Count > 0 && (shield == null || UnityEngine.Random.value < 0.7f))
        {
            weapon = others[UnityEngine.Random.Range(0, others.Count)];
        }
        if (shield == null && weapon == null)
        {
            if (others.Count > 0)
            {
                weapon = others[UnityEngine.Random.Range(0, others.Count)];
            }
            else if (shields.Count > 0)
            {
                shield = shields[UnityEngine.Random.Range(0, shields.Count)];
            }
        }

        if (shield != null && weapon != null)
        {
            // 듀얼은 SPUM 규약대로 파일명을 콤마로 잇는다(레시피 fixedParts도 같은 형식이다).
            result["Weapons"] = new SPUM_ImprovedTagManager.ImprovedCharacterDataItem
            {
                FileName = $"{shield.FileName},{weapon.FileName}",
                Part = "Weapons",
                Theme = shield.Theme,
                Race = shield.Race,
                Gender = shield.Gender,
                Type = shield.Type,
                Properties = shield.Properties,
                Class = shield.Class,
                Style = shield.Style,
            };
        }
        else if (shield != null || weapon != null)
        {
            result["Weapons"] = shield ?? weapon;
        }
    }

    /// <summary>
    /// 레시피 태그로 파츠를 거른다(AND). 축 값이 비어 있으면 그 축은 무제한이고,
    /// 태그가 없는 <b>범용 파츠</b>(Race·Theme·Class가 비어 있는 항목)는 통과시킨다 —
    /// 그렇지 않으면 Body·Eye 같은 공용 파츠가 전부 걸러져 조합이 만들어지지 않는다.
    /// <para>Helmet·FaceHair·Hair는 SPUM과 같은 예외를 둔다 — 지정 종족과 다른 종족 전용은 제외한다.</para>
    /// </summary>
    public List<SPUM_ImprovedTagManager.ImprovedCharacterDataItem> Filter(MonsterAppearanceRecipe recipe)
    {
        string race = recipe.race;
        string gender = recipe.gender;
        string theme = recipe.theme;
        var classes = recipe.classes;

        return Parts.Where(item =>
        {
            if (item == null || string.IsNullOrEmpty(item.Part))
            {
                return false;
            }
            if (!MatchesSingle(item.Race, race))
            {
                return false;
            }
            if (!MatchesSingle(item.Gender, gender))
            {
                return false;
            }
            if (!MatchesArray(item.Theme, theme))
            {
                return false;
            }
            if (classes != null && classes.Count > 0
                && item.Class != null && item.Class.Length > 0
                && !classes.All(c => item.Class.Contains(c, StringComparer.OrdinalIgnoreCase)))
            {
                return false;
            }

            // 종족 지정 시 머리·수염·투구는 다른 종족 전용을 배제한다(SPUM과 동일한 예외).
            if (!string.IsNullOrEmpty(race) && !string.IsNullOrEmpty(item.Race)
                && (item.Part == "Helmet" || item.Part == "FaceHair" || item.Part == "Hair")
                && !string.Equals(item.Race, race, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }).ToList();
    }

    private static bool MatchesSingle(string itemValue, string wanted)
    {
        return string.IsNullOrEmpty(wanted)
               || string.IsNullOrEmpty(itemValue)
               || string.Equals(itemValue, wanted, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesArray(string[] itemValues, string wanted)
    {
        return string.IsNullOrEmpty(wanted)
               || itemValues == null || itemValues.Length == 0
               || itemValues.Contains(wanted, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsShield(SPUM_ImprovedTagManager.ImprovedCharacterDataItem item)
    {
        if (!string.IsNullOrEmpty(item.Type) && item.Type.Equals("shield", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (!string.IsNullOrEmpty(item.FileName) && item.FileName.ToLower().Contains("shield"))
        {
            return true;
        }
        return item.Properties != null
               && item.Properties.Any(p => p.Equals("shield", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsShieldTexture(SpumTextureData data)
    {
        return Contains(data.SubType) || Contains(data.PartSubType) || Contains(data.Name) || Contains(data.Path);

        bool Contains(string value) => !string.IsNullOrEmpty(value) && value.ToLower().Contains("shield");
    }

    // ══════════════════════════════════════════════════════════════════
    //  프리뷰 요소 생성·적용(§6.2 3~4)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 조합 결과를 프리뷰 요소 목록으로 바꾼다.
    /// 파일명이 콤마로 이어져 있으면(듀얼 무기) 각각을 찾아 모두 넣는다.
    /// </summary>
    public List<PreviewMatchingElement> BuildElements(
        Dictionary<string, SPUM_ImprovedTagManager.ImprovedCharacterDataItem> composed)
    {
        var elements = new List<PreviewMatchingElement>();
        foreach (var kv in composed)
        {
            foreach (var fileName in SplitNames(kv.Value.FileName))
            {
                elements.AddRange(BuildElements(fileName, kv.Key));
            }
        }
        return elements;
    }

    /// <summary>
    /// 파일명 + 파트로 프리뷰 요소를 만든다(SPUM의 CreatePreviewMatchingElements 재구현).
    /// 하나의 파일이 여러 텍스처(구조)로 나뉘어 있으면 그 수만큼 요소가 생긴다.
    /// </summary>
    public List<PreviewMatchingElement> BuildElements(string fileName, string part)
    {
        var elements = new List<PreviewMatchingElement>();

        foreach (var package in Packages)
        {
            foreach (var texture in package.SpumTextureData)
            {
                if (!string.Equals(texture.Name, fileName, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(texture.PartType, part, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(texture.UnitType, UnitTypeName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var element = new PreviewMatchingElement
                {
                    UnitType = texture.UnitType,
                    PartType = texture.PartType,
                    PartSubType = texture.PartSubType,
                    ItemPath = texture.Path,
                    Structure = string.Equals(texture.SubType, texture.Name) ? texture.PartType : texture.SubType,
                    Index = 0,
                    Dir = string.Empty,
                    MaskIndex = 0,
                    Color = DefaultColorFor(texture.PartType),
                };

                // 무기·투구는 붙는 자리가 정해져 있다(SPUM과 동일).
                if (element.PartType == "Weapons")
                {
                    element.Structure = "Weapons";
                    element.Dir = IsShieldTexture(texture) ? "Left" : "Right";
                }
                else if (element.PartType == "Helmet")
                {
                    element.Structure = "Helmet";
                    element.Dir = "Front";
                }

                elements.Add(element);
            }
        }

        return elements;
    }

    /// <summary>그 파트에서 고를 수 있는 파츠 목록(파일명 오름차순) — 커스텀 외형 선택 UI가 쓴다.</summary>
    public List<SPUM_ImprovedTagManager.ImprovedCharacterDataItem> PartsOf(string part)
    {
        return Parts
            .Where(item => string.Equals(item.Part, part, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 저장된 프리팹의 외형을 그대로 읽어 온다(불러오기).
    /// <para>프리팹 에셋의 요소를 <b>복사해서</b> 돌려준다 — 참조를 그대로 쓰면 프리뷰를 만질 때
    /// 원본 에셋이 수정된다.</para>
    /// </summary>
    public static List<PreviewMatchingElement> ReadElements(GameObject prefabAsset, out string error)
    {
        error = null;
        var unit = prefabAsset != null ? prefabAsset.GetComponent<SPUM_Prefabs>() : null;
        if (unit == null)
        {
            error = "프리팹 루트에 SPUM_Prefabs가 없습니다.";
            return null;
        }
        if (unit.ImageElement == null || unit.ImageElement.Count == 0)
        {
            error = "그 프리팹에는 외형 정보(ImageElement)가 없습니다.";
            return null;
        }
        return unit.ImageElement.Select(CloneElement).ToList();
    }

    private static PreviewMatchingElement CloneElement(PreviewMatchingElement source)
    {
        return new PreviewMatchingElement
        {
            UnitType = source.UnitType,
            PartType = source.PartType,
            PartSubType = source.PartSubType,
            Index = source.Index,
            Dir = source.Dir,
            Structure = source.Structure,
            ItemPath = source.ItemPath,
            MaskIndex = source.MaskIndex,
            Color = source.Color,
        };
    }

    /// <summary>요소의 스프라이트 경로에서 파츠 파일명을 되읽는다(불러온 외형의 파트별 선택 상태 복원용).</summary>
    public static string FileNameOf(PreviewMatchingElement element)
    {
        if (element == null || string.IsNullOrEmpty(element.ItemPath))
        {
            return string.Empty;
        }
        var parts = element.ItemPath.Replace("\\", "/").Split('/');
        return parts[parts.Length - 1];
    }

    /// <summary>설치된 패키지에 그 파일명·파트의 유닛 텍스처가 실재하는지(태그 DB 정리에 쓴다).</summary>
    public bool HasTexture(string fileName, string part)
    {
        if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(part))
        {
            return false;
        }

        // 듀얼 무기처럼 콤마로 이어진 이름은 낱개 모두가 있어야 한다.
        foreach (var name in SplitNames(fileName))
        {
            bool found = Packages.Any(package => package.SpumTextureData.Any(texture =>
                string.Equals(texture.Name, name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(texture.PartType, part, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(texture.UnitType, UnitTypeName, StringComparison.OrdinalIgnoreCase)));
            if (!found)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>파트별 기본 색(SPUM의 기본값 — 눈만 짙은 갈색이고 나머지는 흰색).</summary>
    private static Color DefaultColorFor(string partType)
    {
        return string.Equals(partType, "Eye", StringComparison.OrdinalIgnoreCase)
            ? (Color)new Color32(71, 26, 26, 255)
            : Color.white;
    }

    /// <summary>
    /// 프리뷰 유닛의 스프라이트를 요소 목록대로 갈아 끼운다(SPUM_Manager.SetSprite 재구현).
    /// <para>SPUM 원본은 씬의 파츠 버튼(<c>SPUM_SpriteButtonST</c>)에서 색 예외 목록을 읽지만,
    /// 여기서는 버튼이 없으므로 요소가 들고 있는 색을 그대로 쓴다 — 하네스가 UI 계층에 매이지 않게 하는
    /// 핵심 차이다.</para>
    /// </summary>
    public void ApplyToUnit(SPUM_Prefabs unit, List<PreviewMatchingElement> elements)
    {
        if (unit == null || elements == null)
        {
            return;
        }

        var slots = unit.GetComponentsInChildren<SPUM_MatchingList>(true)
            .SelectMany(list => list.matchingTables)
            .ToList();

        // 1) 이번 조합에 없는 자리는 비운다(이전 조합의 잔상이 남지 않게).
        foreach (var slot in slots)
        {
            if (slot.renderer == null)
            {
                continue;
            }
            slot.renderer.sprite = null;
            slot.ItemPath = string.Empty;
        }

        // 2) 요소를 자리에 채운다.
        foreach (var slot in slots)
        {
            if (slot.renderer == null)
            {
                continue;
            }

            var match = elements.FirstOrDefault(e => Matches(e, slot));

            if (match == null)
            {
                continue;
            }

            slot.renderer.sprite = LoadSprite(match.ItemPath, match.Structure);
            slot.renderer.maskInteraction = (SpriteMaskInteraction)match.MaskIndex;
            slot.renderer.color = match.Color;
            slot.ItemPath = match.ItemPath;
            slot.Color = match.Color;
        }

        // 3) 저장 시 패키지 동기화에 쓰이는 요소 목록을 유닛에 기록한다.
        unit.ImageElement = new List<PreviewMatchingElement>(elements);
    }

    /// <summary>
    /// 요소를 이 자리에 그릴 수 있는지 판정한다.
    /// <para><b>PartSubType은 양쪽 다 값이 있을 때만 비교한다.</b> 무기 자리(<c>L_Weapon</c>·<c>R_Weapon</c>)는
    /// <c>PartSubType</c>이 비어 있는데 무기 텍스처는 <c>Axe</c>·<c>Sword</c> 같은 값을 갖고 있어서,
    /// 무조건 같은지 비교하면 <b>무기가 한 번도 매칭되지 않아 손이 빈 채로 나온다</b>(실제로 겪은 문제).
    /// 자리를 특정하는 것은 <c>PartType</c>·<c>Dir</c>(Left/Right)·<c>Structure</c>이며,
    /// <c>PartSubType</c>은 같은 자리를 더 쪼개는 보조 값일 때만 의미가 있다.</para>
    /// </summary>
    private static bool Matches(PreviewMatchingElement element, MatchingElement slot)
    {
        if (!Same(element.UnitType, slot.UnitType)
            || !Same(element.PartType, slot.PartType)
            || !Same(element.Dir, slot.Dir)
            || !Same(element.Structure, slot.Structure))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(slot.PartSubType)
               || string.IsNullOrWhiteSpace(element.PartSubType)
               || Same(element.PartSubType, slot.PartSubType);
    }

    /// <summary>
    /// 자리 비교용 문자열 동등성 — <b>앞뒤 공백을 무시한다</b>.
    /// SPUM 프리팹에 구워진 값에는 공백이 섞인 것이 있어서(투구 앞면 자리의 <c>Structure</c>가
    /// <c>"Helmet "</c>이다), 그대로 비교하면 <b>투구가 영영 매칭되지 않는다</b>(실제로 겪은 문제).
    /// </summary>
    private static bool Same(string a, string b)
    {
        return string.Equals((a ?? string.Empty).Trim(), (b ?? string.Empty).Trim(),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Resources의 멀티플 스프라이트에서 이름으로 하나를 고른다(없으면 첫 장 — SPUM과 동일).</summary>
    private static Sprite LoadSprite(string path, string spriteName)
    {
        var sprites = Resources.LoadAll<Sprite>(path);
        if (sprites == null || sprites.Length == 0)
        {
            return null;
        }
        return Array.Find(sprites, s => s.name == spriteName) ?? sprites[0];
    }

    // ══════════════════════════════════════════════════════════════════
    //  저장(§6.2 5~8)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 프리뷰 유닛을 프리팹 에셋으로 저장한다(IPrefabFileHandler.Save 재구현).
    /// <para>복제 → 비활성 자식(다른 유닛 타입) 제거 → Animator 배선 → 사용 패키지만 남기기 →
    /// 상태별 클립 리스트 채우기 → 에셋 저장. 저장물은 "루트에 SPUM_Prefabs + UnitRoot 하나"인
    /// 기존 몬스터 프리팹과 같은 구성이 된다(§2.3).</para>
    /// </summary>
    public GameObject SaveAsPrefab(SPUM_Prefabs unit, string assetPath, string unitCode, out string error)
    {
        error = null;
#if UNITY_EDITOR
        if (unit == null)
        {
            error = "프리뷰 유닛이 없습니다.";
            return null;
        }

        var copy = UnityEngine.Object.Instantiate(unit.gameObject);
        try
        {
            // 프리뷰가 프리팹 인스턴스에서 나온 사본이면 연결을 끊어 둔다 —
            // 연결된 채로 SaveAsPrefabAsset을 부르면 Unity가 "원본/변형(Variant) 중 무엇으로 저장할지"
            // 대화상자를 띄워 일괄 생성이 멈춘다.
            if (PrefabUtility.IsPartOfPrefabInstance(copy))
            {
                PrefabUtility.UnpackPrefabInstance(copy, PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction);
            }

            var saved = copy.GetComponent<SPUM_Prefabs>();
            saved.ImageElement = new List<PreviewMatchingElement>(unit.ImageElement);
            saved.spumPackages = unit.spumPackages;
            saved._code = unitCode;
            saved._version = Version;
            saved.UnitType = UnitTypeName;

            // 다른 유닛 타입(Horse 등) 자식은 비활성 상태이므로 그대로 제거한다.
            var inactive = copy.transform.Cast<Transform>()
                .Where(child => !child.gameObject.activeSelf)
                .Select(child => child.gameObject)
                .ToList();
            foreach (var go in inactive)
            {
                UnityEngine.Object.DestroyImmediate(go);
            }

            copy.transform.localScale = Vector3.one;
            saved._anim = copy.GetComponentInChildren<Animator>(true);
            if (saved._anim == null)
            {
                error = "저장 대상에 Animator가 없습니다.";
                return null;
            }
            saved._anim.runtimeAnimatorController = _animators[UnitTypeName];

            SyncPackages(saved);
            saved.PopulateAnimationLists();

            EnsureAssetFolder(System.IO.Path.GetDirectoryName(assetPath).Replace("\\", "/"));
            return PrefabUtility.SaveAsPrefabAsset(copy, assetPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(copy);
        }
#else
        error = "에디터에서만 저장할 수 있습니다(N1).";
        return null;
#endif
    }

    /// <summary>
    /// 실제로 쓰는 패키지만 남긴다(SPUM_Manager.SyncPackagesWithImageElement 재구현).
    /// 스프라이트 경로에서 쓰는 패키지 + 이 유닛 타입의 애니메이션을 가진 패키지가 대상이며,
    /// 클립의 <c>index</c>·<c>HasData</c>는 이전 값을 유지한다(이게 있어야 클립 리스트가 채워진다).
    /// </summary>
    private void SyncPackages(SPUM_Prefabs unit)
    {
        var used = new HashSet<string>();
        foreach (var element in unit.ImageElement)
        {
            if (string.IsNullOrEmpty(element.ItemPath))
            {
                continue;
            }
            var parts = element.ItemPath.Replace("\\", "/").Split('/');
            string packageName = parts[0];
            if (packageName == "Addons" && parts.Length >= 3)
            {
                packageName = parts[1];
            }
            used.Add(packageName);
        }

        var previous = unit.spumPackages ?? new List<SpumPackage>();
        foreach (var package in previous)
        {
            if (package.SpumAnimationData.Any(a => a.HasData && a.UnitType == unit.UnitType))
            {
                used.Add(package.Name);
            }
        }

        unit.spumPackages = Packages
            .Where(p => used.Contains(p.Name))
            .Select(p => (SpumPackage)p.Clone())
            .ToList();

        foreach (var package in unit.spumPackages)
        {
            var prev = previous.FirstOrDefault(p => p.Name == package.Name);
            foreach (var clip in package.SpumAnimationData)
            {
                var prevClip = prev?.SpumAnimationData.FirstOrDefault(a => a.Name == clip.Name);
                clip.index = prevClip?.index ?? -1;
                clip.HasData = prevClip?.HasData ?? false;
            }
        }
    }

    /// <summary>
    /// 에셋 폴더가 없으면 만든다.
    /// <para><c>Directory.CreateDirectory</c> + <c>AssetDatabase.Refresh</c>를 쓰지 않는 이유:
    /// <b>플레이 중 Refresh는 "스크립트를 다시 불러올까?" 대화상자를 띄워 하네스를 멈춘다.</b>
    /// <c>AssetDatabase.CreateFolder</c>는 대화상자 없이 폴더를 만들고 곧바로 임포트한다.</para>
    /// </summary>
    public static void EnsureAssetFolder(string folderPath)
    {
#if UNITY_EDITOR
        if (string.IsNullOrEmpty(folderPath) || AssetDatabase.IsValidFolder(folderPath))
        {
            return;
        }

        var segments = folderPath.Replace("\\", "/").TrimEnd('/').Split('/');
        string current = segments[0]; // "Assets"
        for (int i = 1; i < segments.Length; i++)
        {
            string next = current + "/" + segments[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, segments[i]);
            }
            current = next;
        }
#endif
    }

    /// <summary>콤마로 이어진 파일명을 낱개로 나눈다(듀얼 무기·레시피 fixedParts 공용).</summary>
    public static IEnumerable<string> SplitNames(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            yield break;
        }
        foreach (var name in value.Split(','))
        {
            string trimmed = name.Trim();
            if (trimmed.Length > 0)
            {
                yield return trimmed;
            }
        }
    }
}
