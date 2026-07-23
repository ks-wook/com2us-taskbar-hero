using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using TaskbarHero.Client.Managers;
using TaskbarHero.Client.MasterData;
using TaskbarHero.Common.MasterData;

namespace TaskbarHero.ClientEditor
{
    /// <summary>
    /// <see cref="SkillIconDatabase"/>(Resources)를 생성·갱신하는 에디터 도구.
    /// skill_master(번들 JSON)의 스킬명을 Assets/Art/Icon/Combat/{직업}/{스킬명}.png 파일명과
    /// 대조(공백 무시)해 스킬 코드→아이콘 매핑을 채운다. 전투 아이콘을 성장 UI에서 재사용한다.
    /// 메뉴: TaskbarHero/UI/스킬 아이콘 DB 빌드
    /// </summary>
    public static class SkillIconDatabaseBuilder
    {
        private const string IconRoot = "Assets/Art/Icon/Combat";
        private const string SkillMasterPath = "Assets/Resources/MasterData/skill_master.json";
        private const string AssetPath = "Assets/Resources/SkillIconDatabase.asset";

        [MenuItem("TaskbarHero/UI/스킬 아이콘 DB 빌드")]
        public static void Build()
        {
            var skills = LoadSkills();
            if (skills == null || skills.Length == 0)
            {
                Debug.LogWarning($"[SkillIconDatabaseBuilder] skill_master를 읽지 못했습니다: {SkillMasterPath}");
                return;
            }

            // 아이콘 폴더의 png들을 "공백 제거 파일명" → 스프라이트로 색인.
            var byName = new Dictionary<string, Sprite>();
            if (Directory.Exists(IconRoot))
            {
                foreach (var file in Directory.GetFiles(IconRoot, "*.png", SearchOption.AllDirectories))
                {
                    string path = file.Replace('\\', '/');
                    string key = Normalize(Path.GetFileNameWithoutExtension(path));
                    var sp = LoadSprite(path);
                    if (sp != null && !byName.ContainsKey(key))
                    {
                        byName[key] = sp;
                    }
                }
            }
            else
            {
                Debug.LogWarning($"[SkillIconDatabaseBuilder] 아이콘 폴더가 없습니다: {IconRoot}");
            }

            var entries = new List<SkillIconDatabase.IconEntry>();
            int matched = 0;
            foreach (var s in skills)
            {
                if (byName.TryGetValue(Normalize(s.name), out var sp))
                {
                    entries.Add(new SkillIconDatabase.IconEntry { skillCode = s.skillCode, sprite = sp });
                    matched++;
                }
                else
                {
                    Debug.LogWarning($"[SkillIconDatabaseBuilder] 아이콘 미발견: {s.skillCode} '{s.name}'");
                }
            }

            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }

            var asset = AssetDatabase.LoadAssetAtPath<SkillIconDatabase>(AssetPath);
            bool isNew = asset == null;
            if (isNew)
            {
                asset = ScriptableObject.CreateInstance<SkillIconDatabase>();
            }
            asset.entries = entries.ToArray();

            if (isNew)
            {
                AssetDatabase.CreateAsset(asset, AssetPath);
            }
            else
            {
                EditorUtility.SetDirty(asset);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[SkillIconDatabaseBuilder] 완료: 스킬 {skills.Length}종 중 {matched}종 아이콘 매핑 → {AssetPath}");
        }

        /// <summary>skill_master.json을 파싱해 스킬 배열을 반환한다(없으면 null).</summary>
        private static SkillMaster[] LoadSkills()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(SkillMasterPath);
            if (asset == null)
            {
                return null;
            }
            return JsonHelper.FromJsonArray<SkillMaster>(asset.text);
        }

        /// <summary>이름 비교용 정규화: 모든 공백 제거(예: "파이어 볼" == "파이어볼").</summary>
        private static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return string.Empty;
            }
            return s.Replace(" ", string.Empty).Replace("\t", string.Empty);
        }

        /// <summary>Multiple 스프라이트 모드 텍스처에서 첫 Sprite 서브에셋을 로드한다.</summary>
        private static Sprite LoadSprite(string path)
        {
            var direct = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (direct != null)
            {
                return direct;
            }
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (obj is Sprite sp)
                {
                    return sp;
                }
            }
            return null;
        }
    }
}
