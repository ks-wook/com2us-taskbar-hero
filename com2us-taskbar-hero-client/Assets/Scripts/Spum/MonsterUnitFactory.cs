using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// 몬스터 외형 레시피 → 프리팹 조합·저장의 <b>단일 정본</b>(기획서 <c>docs/캐릭터-개발씬-기획서.md</c> §4.2·§6.2·§7.1).
///
/// <para><see cref="CharacterDevController"/>(하네스 UI)와 <c>MonsterPrefabBuilder</c>(에디터 자동 빌더)가
/// <b>모두 이 클래스를 통해</b> 외형을 만든다. 한쪽만 고치면 "씬에서 손으로 만든 외형"과 "자동 생성한 외형"이
/// 조용히 갈라지므로, 조합 규칙(태그 검증 · 시드 고정 · 고정 파츠 · 색)은 여기 한 곳에만 둔다.</para>
///
/// <para>모든 경로가 <c>AssetDatabase</c> 기반이라 <b>플레이 모드가 필요 없다</b> — 에디터 메뉴·CI에서
/// 곧바로 <see cref="BuildPrefab"/>을 호출해 프리팹을 만들 수 있다.</para>
/// </summary>
public static class MonsterUnitFactory
{
    public const string DefaultPrefabFolder = "Assets/Prefabs/Character/Monster/";
    public const string DefaultBackupFolder = "Assets/Dev/MonsterPrefabBackup/";
    public const string RecipeJsonPath = "Assets/Dev/monster-appearance-recipe.json";

    /// <summary>폴더 경로 끝에 슬래시를 보장한다(빈 값이면 기본 폴더).</summary>
    public static string EnsureTrailingSlash(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return DefaultPrefabFolder;
        }
        return path.EndsWith("/") ? path : path + "/";
    }

    /// <summary><c>monster_{code}.prefab</c> 에셋 경로.</summary>
    public static string PrefabPath(int code, string folder = null)
    {
        return EnsureTrailingSlash(string.IsNullOrEmpty(folder) ? DefaultPrefabFolder : folder)
               + "monster_" + code + ".prefab";
    }

    // ══════════════════════════════════════════════════════════════════
    //  무기 스윙 이펙트 색(MonsterSwingFxPalette)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 저장된 몬스터 프리팹에 무기 스윙 이펙트 색을 심는다(<see cref="TaskbarHero.Client.Battle.MonsterSwingFxPalette"/>).
    /// <para><paramref name="color"/>가 null이면 컴포넌트를 <b>떼어</b> 기본 불티색으로 되돌린다 —
    /// 색을 지운 레시피가 옛 색을 남기지 않게 하기 위해서다.</para>
    /// <para>런타임이 몬스터 코드로 색을 유추하지 않고 프리팹에 담긴 값을 읽으므로, 이 함수가
    /// 프리팹을 만드는 두 경로(자동 빌더·CharacterDevScene)에서 모두 불려야 색이 갈라지지 않는다.</para>
    /// </summary>
    /// <returns>프리팹을 실제로 고쳤으면 true.</returns>
    public static bool ApplySwingFxPalette(string assetPath, Color? color)
    {
#if UNITY_EDITOR
        if (string.IsNullOrEmpty(assetPath))
        {
            return false;
        }
        var root = PrefabUtility.LoadPrefabContents(assetPath);
        if (root == null)
        {
            return false;
        }

        bool changed = false;
        try
        {
            var palette = root.GetComponent<TaskbarHero.Client.Battle.MonsterSwingFxPalette>();
            if (color.HasValue)
            {
                if (palette == null)
                {
                    palette = root.AddComponent<TaskbarHero.Client.Battle.MonsterSwingFxPalette>();
                    changed = true;
                }
                if (palette.BaseColor != color.Value)
                {
                    palette.SetBaseColor(color.Value);
                    changed = true;
                }
            }
            else if (palette != null)
            {
                UnityEngine.Object.DestroyImmediate(palette, true);
                changed = true;
            }

            if (changed)
            {
                PrefabUtility.SaveAsPrefabAsset(root, assetPath);
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        return changed;
#else
        return false;
#endif
    }

    /// <summary>
    /// 레시피가 정한 스윙 이펙트 색. 레시피에 <c>effectColor</c>가 있으면 그 값이고,
    /// 없으면 <b>보스는 지역(Act) 컬러링</b>, 일반 몬스터는 null(기본 불티색)이다.
    /// </summary>
    public static Color? SwingFxColorFor(MonsterAppearanceRecipe recipe, int code)
    {
        var explicitColor = recipe != null ? recipe.EffectColorOrNull() : null;
        if (explicitColor.HasValue)
        {
            return explicitColor;
        }
        if (!TaskbarHero.Client.Battle.MonsterSwingFxPalette.IsBossCode(code))
        {
            return null;
        }
        int act = TaskbarHero.Client.Battle.MonsterSwingFxPalette.ActOf(code);
        return act > 0 ? TaskbarHero.Client.Battle.MonsterSwingFxPalette.ActColor(act) : (Color?)null;
    }

    // ══════════════════════════════════════════════════════════════════
    //  레시피 검증(§6.4)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>레시피의 태그 값과 고정 파츠 파일명이 태그 DB에 실재하는지 검사한다(§6.4).</summary>
    public static bool ValidateRecipe(SpumUnitComposer composer, MonsterAppearanceRecipe recipe, out string error)
    {
        error = null;
        if (composer == null)
        {
            error = "조합 엔진이 준비되지 않았습니다.";
            return false;
        }
        if (recipe == null)
        {
            error = "레시피가 없습니다.";
            return false;
        }

        var problems = new List<string>();

        if (!string.IsNullOrEmpty(recipe.race) && !KnownValues(composer, i => new[] { i.Race }).Contains(recipe.race))
        {
            problems.Add($"race '{recipe.race}'");
        }
        if (!string.IsNullOrEmpty(recipe.gender) && !KnownValues(composer, i => new[] { i.Gender }).Contains(recipe.gender))
        {
            problems.Add($"gender '{recipe.gender}'");
        }
        if (!string.IsNullOrEmpty(recipe.theme) && !KnownValues(composer, i => i.Theme).Contains(recipe.theme))
        {
            problems.Add($"theme '{recipe.theme}'");
        }

        var knownClasses = KnownValues(composer, i => i.Class);
        foreach (var c in recipe.classes)
        {
            if (!knownClasses.Contains(c))
            {
                problems.Add($"class '{c}'");
            }
        }

        foreach (var kv in recipe.fixedParts)
        {
            foreach (var fileName in SpumUnitComposer.SplitNames(kv.Value))
            {
                if (FindPartItem(composer, fileName, kv.Key) == null)
                {
                    problems.Add($"fixedParts {kv.Key} = '{fileName}'");
                }
            }
        }

        if (!string.IsNullOrEmpty(recipe.shareCode))
        {
            problems.Add("shareCode는 예약 필드이며 아직 구현되지 않았습니다(§4.1)");
        }

        if (problems.Count > 0)
        {
            error = "레시피 값이 태그 DB에 없습니다 — " + string.Join(", ", problems);
            return false;
        }
        return true;
    }

    /// <summary>태그 DB에서 그 축의 실제 값 집합을 모은다(레시피 검증용).</summary>
    public static HashSet<string> KnownValues(
        SpumUnitComposer composer, Func<SPUM_ImprovedTagManager.ImprovedCharacterDataItem, string[]> selector)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (composer == null)
        {
            return set;
        }
        foreach (var item in composer.Parts)
        {
            var values = selector(item);
            if (values == null)
            {
                continue;
            }
            foreach (var v in values)
            {
                if (!string.IsNullOrEmpty(v))
                {
                    set.Add(v);
                }
            }
        }
        return set;
    }

    private static SPUM_ImprovedTagManager.ImprovedCharacterDataItem FindPartItem(
        SpumUnitComposer composer, string fileName, string part)
    {
        if (composer == null)
        {
            return null;
        }
        return composer.Parts.FirstOrDefault(i =>
            string.Equals(i.FileName, fileName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(i.Part, part, StringComparison.OrdinalIgnoreCase));
    }

    // ══════════════════════════════════════════════════════════════════
    //  외형 조합(§6.2 1~4)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 레시피대로 파츠를 조합해 외형 요소 목록을 만든다(유닛에 적용하지는 않는다).
    /// 순서는 §6.2와 같다 — 태그 검증 → 시드 고정 조합 → 요소 변환 → 고정 파츠·색 덮어쓰기.
    /// 실패하면 <c>null</c>과 사유를 돌려준다.
    /// </summary>
    public static List<PreviewMatchingElement> BuildAppearance(
        SpumUnitComposer composer, MonsterAppearanceRecipe recipe, out string error)
    {
        if (!ValidateRecipe(composer, recipe, out error))
        {
            return null;
        }

        // 시드를 고정해 같은 레시피가 항상 같은 결과를 내게 한다(F6).
        // 이 호출과 Compose 사이에 다른 난수 소비를 끼우지 않는다.
        UnityEngine.Random.InitState(recipe.EffectiveSeed);
        var composed = composer.Compose(recipe);

        if (composed.Count == 0)
        {
            error = "태그 조합 결과가 0개입니다(필터가 너무 좁습니다).";
            return null;
        }
        if (!composed.ContainsKey("Body"))
        {
            error = "필수 파츠(Body)를 찾지 못했습니다 — 레시피 태그를 넓히세요.";
            return null;
        }

        var elements = composer.BuildElements(composed);
        if (elements.Count == 0)
        {
            error = "조합 결과에 해당하는 텍스처를 패키지에서 찾지 못했습니다.";
            return null;
        }

        ApplyFixedParts(composer, elements, recipe);
        ApplyColors(elements, recipe);
        return elements;
    }

    /// <summary>
    /// 레시피의 고정 파츠로 그 Part의 요소를 통째로 갈아 끼운다(§4.2).
    /// 값은 SPUM 파일명이며 무기 2개는 콤마로 나열한다.
    /// </summary>
    public static void ApplyFixedParts(
        SpumUnitComposer composer, List<PreviewMatchingElement> elements, MonsterAppearanceRecipe recipe)
    {
        foreach (var kv in recipe.fixedParts)
        {
            string part = kv.Key;
            var replacements = new List<PreviewMatchingElement>();
            foreach (var fileName in SpumUnitComposer.SplitNames(kv.Value))
            {
                replacements.AddRange(composer.BuildElements(fileName, part));
            }

            if (replacements.Count == 0)
            {
                continue; // 검증에서 이미 걸러지지만, 일괄 실행 중이면 조용히 건너뛴다
            }

            elements.RemoveAll(e => string.Equals(e.PartType, part, StringComparison.OrdinalIgnoreCase));
            elements.AddRange(replacements);
        }
    }

    /// <summary>레시피의 계열 팔레트를 해당 Part 요소의 색에 얹는다(§4.2 — Body·Hair·Cloth 3키).</summary>
    public static void ApplyColors(List<PreviewMatchingElement> elements, MonsterAppearanceRecipe recipe)
    {
        foreach (var kv in recipe.colors)
        {
            if (!ColorUtility.TryParseHtmlString(kv.Value, out Color color))
            {
                continue;
            }
            foreach (var element in elements)
            {
                if (string.Equals(element.PartType, kv.Key, StringComparison.OrdinalIgnoreCase))
                {
                    element.Color = color;
                }
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  저장·검증(§6.2 5~9 · §7.1)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>저장물이 전투에서 동작할 수 있는 형태인지 3항목을 확인한다(§2.3 · §7.1).</summary>
    public static bool ValidateSavedPrefab(GameObject prefabRoot, out string error)
    {
        error = null;
        if (prefabRoot == null)
        {
            error = "검증 실패 — 저장된 프리팹이 없습니다.";
            return false;
        }
        var spum = prefabRoot.GetComponent<SPUM_Prefabs>();
        if (spum == null)
        {
            error = "검증 실패 — 루트에 SPUM_Prefabs가 없습니다.";
            return false;
        }
        if (spum._anim == null)
        {
            error = "검증 실패 — _anim(자식 Animator)이 배선되지 않았습니다.";
            return false;
        }
        if (!spum.allListsHaveItemsExist())
        {
            error = "검증 실패 — 상태별 애니메이션 클립 리스트가 비어 있습니다.";
            return false;
        }
        return true;
    }

    /// <summary>기존 에셋을 백업 폴더로 복사한다(원본 GUID를 유지하려 이동이 아니라 복사다). 없으면 빈 문자열.</summary>
    public static string BackupAsset(string assetPath, string baseName, string backupFolder)
    {
#if UNITY_EDITOR
        if (!File.Exists(assetPath))
        {
            return string.Empty;
        }

        string folder = EnsureTrailingSlash(string.IsNullOrEmpty(backupFolder) ? DefaultBackupFolder : backupFolder);
        SpumUnitComposer.EnsureAssetFolder(folder.TrimEnd('/'));
        string ext = Path.GetExtension(assetPath);
        string backupPath = $"{folder}{baseName}_{DateTime.Now:yyyyMMdd_HHmmss}{ext}";
        AssetDatabase.CopyAsset(assetPath, backupPath);
        return backupPath;
#else
        return string.Empty;
#endif
    }

    /// <summary>검증 실패 시 백업본을 원래 자리로 되돌린다(백업이 없으면 깨진 저장물을 지운다).</summary>
    public static void RestoreBackup(string backupPath, string assetPath)
    {
#if UNITY_EDITOR
        if (string.IsNullOrEmpty(backupPath) || !File.Exists(backupPath))
        {
            AssetDatabase.DeleteAsset(assetPath);
            return;
        }
        AssetDatabase.DeleteAsset(assetPath);
        AssetDatabase.CopyAsset(backupPath, assetPath);
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
#endif
    }

    /// <summary>
    /// 프리뷰 유닛 프리팹(SPUM <c>SpritePrefab</c>)을 하나 세운다.
    /// <paramref name="parent"/>가 <c>null</c>이면 현재 씬 루트에 만든다(호출자가 정리·이동을 책임진다).
    ///
    /// <para><b>hideFlags를 붙이지 않는다.</b> <c>HideFlags.DontSave</c>가 걸린 오브젝트는
    /// <c>PrefabUtility.SaveAsPrefabAsset</c>이 "No objects were found for saving into prefab"으로 거부하고,
    /// <c>HideInHierarchy</c>는 저장된 프리팹에 그대로 박혀 전투에서 몬스터가 보이지 않게 된다.
    /// 씬을 더럽히지 않으려면 <see cref="BuildPrefab"/>처럼 <b>프리뷰 씬</b>으로 옮겨 쓸 것.</para>
    /// </summary>
    public static SPUM_Prefabs InstantiateUnit(
        SpumUnitComposer composer, Transform parent, string name, out string error)
    {
        error = null;
#if UNITY_EDITOR
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SpumUnitComposer.PreviewUnitPrefabPath);
        if (prefab == null)
        {
            error = $"프리뷰 유닛 프리팹을 찾지 못했습니다: {SpumUnitComposer.PreviewUnitPrefabPath}";
            return null;
        }

        var go = parent != null
            ? UnityEngine.Object.Instantiate(prefab, parent)
            : UnityEngine.Object.Instantiate(prefab);
        go.name = string.IsNullOrEmpty(name) ? "PreviewUnit" : name;

        // SPUM 유닛 루트는 RectTransform이라 캔버스 밖에 둘 때 앵커를 중립화해야 자리가 잡힌다(§2.6).
        if (go.transform is RectTransform rt)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition3D = Vector3.zero;
        }
        else
        {
            go.transform.localPosition = Vector3.zero;
        }

        var unit = go.GetComponent<SPUM_Prefabs>();
        if (unit == null)
        {
            error = "프리뷰 유닛 프리팹에 SPUM_Prefabs가 없습니다.";
            UnityEngine.Object.DestroyImmediate(go);
            return null;
        }

        composer.InitUnit(unit);
        return unit;
#else
        error = "에디터에서만 유닛을 만들 수 있습니다(N1).";
        return null;
#endif
    }

    /// <summary>
    /// 이미 외형이 입혀진 유닛을 <c>monster_{code}.prefab</c>으로 저장한다.
    /// 같은 이름이 있으면 먼저 백업하고, 저장 직후 3항목을 검증해 실패하면 백업으로 되돌린다(§7.1).
    /// </summary>
    public static bool SaveUnitAsPrefab(
        SpumUnitComposer composer, SPUM_Prefabs unit, int code,
        string prefabFolder, string backupFolder, out string error)
    {
        error = null;
#if UNITY_EDITOR
        if (unit == null)
        {
            error = "저장할 유닛이 없습니다.";
            return false;
        }
        if (unit.ImageElement.Count == 0)
        {
            error = "먼저 외형을 생성하세요(프리뷰가 비어 있습니다).";
            return false;
        }

        string prefabName = "monster_" + code;
        string assetPath = PrefabPath(code, prefabFolder);
        SpumUnitComposer.EnsureAssetFolder(EnsureTrailingSlash(
            string.IsNullOrEmpty(prefabFolder) ? DefaultPrefabFolder : prefabFolder).TrimEnd('/'));
        string backupPath = BackupAsset(assetPath, prefabName, backupFolder);

        var saved = composer.SaveAsPrefab(unit, assetPath, prefabName, out error);
        if (saved == null)
        {
            error = string.IsNullOrEmpty(error) ? "프리팹 저장에 실패했습니다." : error;
            return false;
        }

        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        saved = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (saved == null)
        {
            error = $"저장된 프리팹을 찾지 못했습니다: {assetPath}";
            return false;
        }

        if (!ValidateSavedPrefab(saved, out string validationError))
        {
            error = validationError;
            RestoreBackup(backupPath, assetPath);
            return false;
        }
        return true;
#else
        error = "에디터에서만 저장할 수 있습니다(N1).";
        return false;
#endif
    }

    /// <summary>
    /// <b>원샷 생성</b> — 레시피로 외형을 조합해 곧바로 <c>monster_{code}.prefab</c>까지 저장한다.
    /// 씬·플레이 모드가 필요 없으며, 임시 유닛은 성공·실패와 무관하게 반드시 정리한다.
    /// </summary>
    public static bool BuildPrefab(
        SpumUnitComposer composer, MonsterAppearanceRecipe recipe, int code,
        string prefabFolder, string backupFolder, out string error)
    {
        error = null;
#if UNITY_EDITOR
        var elements = BuildAppearance(composer, recipe, out error);
        if (elements == null)
        {
            return false;
        }

        // 열려 있는 씬을 건드리지 않도록 프리뷰 씬에서 조립한다 — 하이어라키에 뜨지 않고,
        // 씬을 dirty로 만들지도 않으며, 프리팹 저장은 정상적으로 된다(프리팹 스테이지와 같은 방식).
        var scene = EditorSceneManager.NewPreviewScene();
        SPUM_Prefabs unit = null;
        try
        {
            unit = InstantiateUnit(composer, null, "MonsterBuild_" + code, out error);
            if (unit == null)
            {
                return false;
            }
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(unit.gameObject, scene);

            composer.ApplyToUnit(unit, elements);
            if (!SaveUnitAsPrefab(composer, unit, code, prefabFolder, backupFolder, out error))
            {
                return false;
            }
            // 저장 직후 스윙 이펙트 색을 심는다(레시피 effectColor, 없으면 보스는 지역 색).
            ApplySwingFxPalette(PrefabPath(code, prefabFolder), SwingFxColorFor(recipe, code));
            return true;
        }
        finally
        {
            if (unit != null)
            {
                UnityEngine.Object.DestroyImmediate(unit.gameObject);
            }
            EditorSceneManager.ClosePreviewScene(scene);
        }
#else
        error = "에디터에서만 생성할 수 있습니다(N1).";
        return false;
#endif
    }

    /// <summary>레시피 JSON(<see cref="RecipeJsonPath"/>)을 읽어 코드→레시피 표로 만든다(에디터 자동 빌더용).</summary>
    public static Dictionary<int, MonsterAppearanceRecipe> LoadRecipeBook(out string error)
    {
        error = null;
#if UNITY_EDITOR
        var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(RecipeJsonPath);
        if (asset == null)
        {
            error = $"레시피 JSON을 찾지 못했습니다: {RecipeJsonPath}";
            return new Dictionary<int, MonsterAppearanceRecipe>();
        }
        return MonsterRecipeBook.Parse(asset.text, out error);
#else
        error = "에디터에서만 읽을 수 있습니다(N1).";
        return new Dictionary<int, MonsterAppearanceRecipe>();
#endif
    }

    /// <summary>프리팹 폴더에 실제로 존재하는 <c>monster_{code}</c> 코드 집합.</summary>
    public static HashSet<int> ExistingPrefabCodes(string prefabFolder = null)
    {
        var result = new HashSet<int>();
        string folder = EnsureTrailingSlash(string.IsNullOrEmpty(prefabFolder) ? DefaultPrefabFolder : prefabFolder);
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
}
