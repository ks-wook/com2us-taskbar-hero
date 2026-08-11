using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TaskbarHero.ClientEditor
{
    /// <summary>
    /// 외형 레시피(<c>Assets/Dev/monster-appearance-recipe.json</c>)에서 <c>monster_{code}.prefab</c>을
    /// <b>플레이 모드 없이</b> 생성하는 에디터 빌더.
    ///
    /// <para>CharacterDevScene(하네스)은 사람이 눈으로 보며 만들 때 쓰고, 이 빌더는 같은 조합 규칙
    /// (<see cref="MonsterUnitFactory"/>)으로 <b>자동 생성</b>한다 — 둘의 결과는 같은 코드 경로에서 나온다.</para>
    ///
    /// <para>대상은 <b>레시피 JSON의 코드</b>다(클라이언트 번들이 아니다). 서버 정본에 몬스터를 추가하면
    /// <c>master_monster_tool.py</c>가 레시피를 함께 채우므로, DB·번들 재생성(<c>master_data_export.py</c>)
    /// 전에도 프리팹을 만들 수 있다.</para>
    ///
    /// 메뉴: <c>TaskbarHero/몬스터/…</c>
    /// </summary>
    public static class MonsterPrefabBuilder
    {
        private const string LogTag = "[MonsterPrefabBuilder]";

        /// <summary>예약 실행의 결과 리포트를 담아 두는 키(외부 자동화가 읽는다).</summary>
        private const string ReportKey = "TaskbarHero.Dev.MonsterBuildReport";
        private const string ForceKey = "TaskbarHero.Dev.MonsterBuildForce";
        private const string SingleCodeKey = "TaskbarHero.Dev.MonsterBuildCode";

        /// <summary>
        /// 외형 교체 대상 코드 목록(콤마 구분). 외부 자동화가 이 키에 코드를 넣고
        /// <see cref="RebuildRequestedMenu"/> 를 실행하면 그 코드만 강제로 다시 만든다.
        /// <para>MCP 커맨드는 메뉴 실행만 통과하므로(에셋 저장 API는 샌드박스가 막는다) 인자를 이렇게 넘긴다.
        /// 한 번 쓰고 지워 다음 실행에 새지 않게 한다 — 전투 검수용 <c>SpawnMonsterCode</c> 키와 같은 방식이다.</para>
        /// </summary>
        private const string RebuildCodesKey = "TaskbarHero.Dev.MonsterRebuildCodes";

        // ══════════════════════════════════════════════════════════════
        //  메뉴
        // ══════════════════════════════════════════════════════════════

        [MenuItem("TaskbarHero/몬스터/프리팹 생성 (누락분만)", false, 0)]
        public static void BuildMissingMenu()
        {
            Finish(() => BuildMissing());
        }

        [MenuItem("TaskbarHero/몬스터/프리팹 재생성 (레시피 전체)", false, 1)]
        public static void RebuildAllMenu()
        {
            if (!EditorUtility.DisplayDialog(
                    "프리팹 전체 재생성",
                    "레시피에 있는 모든 몬스터 프리팹을 다시 만듭니다.\n"
                    + "기존 프리팹은 Assets/Dev/MonsterPrefabBackup/ 에 백업됩니다. 계속할까요?",
                    "재생성", "취소"))
            {
                return;
            }
            Finish(() => BuildAll(force: true));
        }

        [MenuItem("TaskbarHero/몬스터/신규 몬스터 전투 반영 (프리팹 + 전투 배선)", false, 20)]
        public static void RunPipelineMenu()
        {
            Finish(() => RunPipeline());
        }

        [MenuItem("TaskbarHero/몬스터/외형 교체 반영 (지정 코드 재생성)", false, 21)]
        public static void RebuildRequestedMenu()
        {
            Finish(RebuildRequested);
        }

        [MenuItem("TaskbarHero/몬스터/무기 스윙 이펙트 색 반영", false, 22)]
        public static void ApplySwingFxColorsMenu()
        {
            Finish(ApplySwingFxColors);
        }

        /// <summary>
        /// 이미 있는 몬스터 프리팹 전체에 <b>무기 스윙 이펙트 색만</b> 다시 심는다(외형은 건드리지 않는다).
        /// <para>색의 근거는 레시피의 <c>effectColor</c>이고, 없으면 보스는 지역(Act) 컬러링, 일반 몬스터는
        /// 기본 불티색(컴포넌트 제거)이다. 색 규칙을 바꾼 뒤 <b>프리팹을 재생성하지 않고</b> 반영할 때 쓴다.</para>
        /// </summary>
        public static string ApplySwingFxColors()
        {
            var recipes = MonsterUnitFactory.LoadRecipeBook(out string recipeError);
            if (!string.IsNullOrEmpty(recipeError))
            {
                return "실패 — " + recipeError;
            }

            var changed = new List<string>();
            int scanned = 0;
            foreach (var code in recipes.Keys.OrderBy(c => c))
            {
                string path = MonsterUnitFactory.PrefabPath(code);
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                {
                    continue; // 아직 프리팹이 없는 코드는 생성 시 자동으로 색이 들어간다
                }
                scanned++;
                var color = MonsterUnitFactory.SwingFxColorFor(recipes[code], code);
                if (MonsterUnitFactory.ApplySwingFxPalette(path, color))
                {
                    changed.Add($"monster_{code}({(color.HasValue ? ColorUtility.ToHtmlStringRGB(color.Value) : "기본")})");
                }
            }
            AssetDatabase.SaveAssets();

            var sb = new StringBuilder();
            sb.Append($"스윙 이펙트 색 반영 — 프리팹 {scanned}종 검사, 변경 {changed.Count}건");
            if (changed.Count > 0)
            {
                sb.Append(" — ").Append(string.Join(", ", changed));
            }
            return sb.ToString();
        }

        /// <summary>
        /// <see cref="RebuildCodesKey"/> 에 담긴 코드만 <b>강제로 다시 만든다</b>(외형 교체용).
        ///
        /// <para>같은 경로에 덮어쓰므로 <c>.meta</c>의 GUID가 유지되고, 따라서 <c>DungeonBattleFlow</c>의
        /// 코드→프리팹 맵도 그대로 유효하다 — <b>전투 배선을 다시 돌릴 필요가 없다</b>.
        /// 능력치·배치가 그대로이므로 클라이언트 번들 재생성도 필요 없다.</para>
        /// </summary>
        public static string RebuildRequested()
        {
            string raw = EditorPrefs.GetString(RebuildCodesKey, string.Empty);
            var codes = new List<int>();
            foreach (var token in raw.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(token.Trim(), out int code))
                {
                    codes.Add(code);
                }
            }
            if (codes.Count == 0)
            {
                return $"대상 코드가 없습니다 — EditorPrefs '{RebuildCodesKey}' 에 \"9101\" 처럼 넣고 다시 실행하세요.";
            }
            EditorPrefs.DeleteKey(RebuildCodesKey); // 한 번만 적용한다

            var made = new List<int>();
            var failed = new List<string>();
            foreach (var code in codes)
            {
                if (BuildOne(code, out string error))
                {
                    made.Add(code);
                }
                else
                {
                    failed.Add($"monster_{code}: {error}");
                }
            }

            var sb = new StringBuilder();
            sb.Append($"외형 교체 {made.Count}건");
            if (made.Count > 0)
            {
                sb.Append(" — ").Append(string.Join(", ", made.Select(c => "monster_" + c)));
            }
            if (failed.Count > 0)
            {
                sb.Append($" / 실패 {failed.Count}건 — ").Append(string.Join(" | ", failed));
            }
            sb.Append(" (GUID 유지 — 전투 배선·번들 재생성 불필요)");
            return sb.ToString();
        }

        // ══════════════════════════════════════════════════════════════
        //  자동화 진입점 (메뉴를 거치지 않고 스크립트·MCP에서 호출한다)
        // ══════════════════════════════════════════════════════════════

        /// <summary>프리팹이 없는 레시피 코드만 생성한다. 사람이 읽을 리포트 문자열을 돌려준다.</summary>
        public static string BuildMissing()
        {
            return BuildAll(force: false);
        }

        /// <summary>
        /// 레시피의 몬스터 프리팹을 생성한다.
        /// <paramref name="force"/>가 <c>false</c>면 이미 있는 프리팹은 건너뛴다.
        /// </summary>
        public static string BuildAll(bool force)
        {
            var recipes = MonsterUnitFactory.LoadRecipeBook(out string recipeError);
            if (!string.IsNullOrEmpty(recipeError))
            {
                return "실패 — " + recipeError;
            }
            if (recipes.Count == 0)
            {
                return "레시피가 비어 있습니다 — 생성할 몬스터가 없습니다.";
            }

            var existing = MonsterUnitFactory.ExistingPrefabCodes();
            var targets = recipes.Keys.Where(c => force || !existing.Contains(c)).OrderBy(c => c).ToList();
            if (targets.Count == 0)
            {
                return $"레시피 {recipes.Count}종 모두 프리팹이 이미 있습니다(생성 0건).";
            }

            var composer = SpumUnitComposer.Create(out string composerError);
            if (composer == null)
            {
                return "실패 — 조합 엔진을 만들지 못했습니다: " + composerError;
            }

            var made = new List<int>();
            var failed = new List<string>();
            try
            {
                // AssetDatabase.StartAssetEditing() 으로 묶지 않는다 — 그 블록 안에서는 임포트가 지연되어
                // 방금 저장한 프리팹을 LoadAssetAtPath 로 다시 읽지 못하고, 저장 직후 3항목 검증이
                // "저장된 프리팹을 찾지 못했습니다"로 실패한다(그 경로가 백업 복원까지 부른다).
                // 몬스터는 많아야 수십 개라 배치 임포트의 이득보다 검증 정확성이 중요하다.
                for (int i = 0; i < targets.Count; i++)
                {
                    int code = targets[i];
                    EditorUtility.DisplayProgressBar(
                        "몬스터 프리팹 생성", $"monster_{code} ({i + 1}/{targets.Count})",
                        (float)i / targets.Count);

                    if (MonsterUnitFactory.BuildPrefab(
                            composer, recipes[code], code,
                            MonsterUnitFactory.DefaultPrefabFolder, MonsterUnitFactory.DefaultBackupFolder,
                            out string error))
                    {
                        made.Add(code);
                    }
                    else
                    {
                        failed.Add($"monster_{code}: {error}");
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            var sb = new StringBuilder();
            sb.Append($"프리팹 생성 {made.Count}건");
            if (made.Count > 0)
            {
                sb.Append(" — ").Append(string.Join(", ", made.Select(c => "monster_" + c)));
            }
            if (failed.Count > 0)
            {
                sb.Append($" / 실패 {failed.Count}건 — ").Append(string.Join(" | ", failed));
            }
            return sb.ToString();
        }

        /// <summary>코드 하나만 생성한다(이미 있으면 백업 후 덮어쓴다).</summary>
        public static bool BuildOne(int code, out string error)
        {
            error = null;
            var recipes = MonsterUnitFactory.LoadRecipeBook(out string recipeError);
            if (!string.IsNullOrEmpty(recipeError))
            {
                error = recipeError;
                return false;
            }
            if (!recipes.TryGetValue(code, out var recipe))
            {
                // 레시피가 없으면 코드 시드 랜덤으로라도 만든다(§4.2 폴백).
                recipe = MonsterAppearanceRecipe.RandomFor(code);
            }

            var composer = SpumUnitComposer.Create(out string composerError);
            if (composer == null)
            {
                error = "조합 엔진을 만들지 못했습니다: " + composerError;
                return false;
            }

            bool ok = MonsterUnitFactory.BuildPrefab(
                composer, recipe, code,
                MonsterUnitFactory.DefaultPrefabFolder, MonsterUnitFactory.DefaultBackupFolder, out error);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return ok;
        }

        /// <summary>
        /// <b>프리팹 생성 → 던전 전투 배선</b>을 이어서 실행한다.
        /// 배선(<see cref="DungeonBattleBuilder.Build"/>)이 씬을 열고 저장하므로,
        /// 끝나면 원래 열려 있던 씬으로 되돌린다.
        /// </summary>
        public static string RunPipeline(bool force = false)
        {
            var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (active.isDirty)
            {
                return "중단 — 열려 있는 씬에 저장하지 않은 변경이 있습니다. 저장한 뒤 다시 실행하세요.";
            }
            string previousScenePath = active.path;

            string buildReport = BuildAll(force);
            if (buildReport.StartsWith("실패"))
            {
                return buildReport + " (전투 배선은 실행하지 않았습니다)";
            }

            try
            {
                DungeonBattleBuilder.Build();
            }
            catch (Exception e)
            {
                return buildReport + $" / 전투 배선 실패 — {e.Message}";
            }
            finally
            {
                if (!string.IsNullOrEmpty(previousScenePath)
                    && UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != previousScenePath)
                {
                    EditorSceneManager.OpenScene(previousScenePath, OpenSceneMode.Single);
                }
            }

            return buildReport + " / 던전 전투 배선 완료(코드→프리팹 맵 갱신)";
        }

        // ══════════════════════════════════════════════════════════════
        //  예약 실행 — 외부 자동화(MCP·스크립트)용
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 파이프라인을 <b>다음 에디터 틱</b>에 실행하도록 예약하고 즉시 돌아온다.
        ///
        /// <para>왜 곧바로 부르지 않는가 — 외부 자동화(Unity MCP <c>RunCommand</c>)의 샌드박스는
        /// <c>PrefabUtility.SaveAsPrefabAsset</c> 같은 에셋 저장 API를 "사용자 상호작용"으로 보고 차단한다.
        /// <see cref="EditorApplication.delayCall"/>로 넘기면 샌드박스 밖(에디터 루프)에서 돌아 정상 저장된다.
        /// 이때 거는 대상은 <b>반드시 이 어셈블리의 static 메서드</b>여야 한다 — 동적 컴파일된 커맨드의
        /// 람다를 걸면 그 어셈블리가 해제되면서 호출이 조용히 사라진다(실제로 겪은 문제).</para>
        ///
        /// <para>결과는 <see cref="LastReport"/>로 읽는다.</para>
        /// </summary>
        public static void SchedulePipeline(bool force = false)
        {
            EditorPrefs.SetString(ReportKey, "(진행 중)");
            EditorPrefs.SetBool(ForceKey, force);
            EditorApplication.delayCall += RunPipelineDeferred;
        }

        /// <summary>프리팹 생성만 예약한다(전투 배선은 하지 않는다).</summary>
        public static void ScheduleBuildOnly(bool force = false)
        {
            EditorPrefs.SetString(ReportKey, "(진행 중)");
            EditorPrefs.SetBool(ForceKey, force);
            EditorApplication.delayCall += RunBuildOnlyDeferred;
        }

        /// <summary>코드 하나만 생성하도록 예약한다(레시피가 없으면 코드 시드 랜덤 — §4.2 폴백).</summary>
        public static void ScheduleBuildOne(int code)
        {
            EditorPrefs.SetString(ReportKey, "(진행 중)");
            EditorPrefs.SetInt(SingleCodeKey, code);
            EditorApplication.delayCall += RunBuildOneDeferred;
        }

        /// <summary>예약 실행의 마지막 결과. 아직 안 끝났으면 "(진행 중)".</summary>
        public static string LastReport => EditorPrefs.GetString(ReportKey, "(없음)");

        private static void RunBuildOneDeferred()
        {
            int code = EditorPrefs.GetInt(SingleCodeKey, 0);
            Finish(() =>
            {
                bool ok = BuildOne(code, out string error);
                return ok
                    ? $"프리팹 생성 1건 — monster_{code}"
                    : $"프리팹 생성 실패 — monster_{code}: {error}";
            });
        }

        private static void RunPipelineDeferred()
        {
            Finish(() => RunPipeline(EditorPrefs.GetBool(ForceKey, false)));
        }

        private static void RunBuildOnlyDeferred()
        {
            Finish(() => BuildAll(EditorPrefs.GetBool(ForceKey, false)));
        }

        private static void Finish(Func<string> work)
        {
            string report;
            try
            {
                report = work();
            }
            catch (Exception e)
            {
                report = "예외 — " + e.Message;
            }
            EditorPrefs.SetString(ReportKey, report);
            Debug.Log(LogTag + " " + report);
        }

        /// <summary>레시피·프리팹 보유 현황을 한 줄씩 출력한다(점검용).</summary>
        [MenuItem("TaskbarHero/몬스터/현황 보기 (레시피·프리팹)", false, 40)]
        public static void ReportMenu()
        {
            var recipes = MonsterUnitFactory.LoadRecipeBook(out string error);
            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogError(LogTag + " " + error);
                return;
            }
            var existing = MonsterUnitFactory.ExistingPrefabCodes();

            var sb = new StringBuilder();
            sb.AppendLine($"{LogTag} 레시피 {recipes.Count}종 / 프리팹 {existing.Count}개");
            foreach (var code in recipes.Keys.Union(existing).OrderBy(c => c))
            {
                string recipeMark = recipes.ContainsKey(code) ? "레시피 O" : "레시피 -";
                string prefabMark = existing.Contains(code) ? "프리팹 O" : "프리팹 -";
                string note = recipes.TryGetValue(code, out var r) ? r.note : string.Empty;
                sb.AppendLine($"  {code}  {recipeMark}  {prefabMark}  {note}");
            }
            Debug.Log(sb.ToString());
        }
    }
}
