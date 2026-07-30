using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using TaskbarHero.Client.Managers;
using TaskbarHero.Client.UI;

namespace TaskbarHero.ClientEditor
{
    /// <summary>
    /// <c>Assets/Prefabs/Character/{직업}_{성별}.prefab</c>을 스캔해
    /// <c>Assets/Resources/CharacterPrefabDatabase.asset</c>((직업,성별) → 프리팹)을 만들거나 갱신하고,
    /// 각 캐릭터 프리팹이 캐릭터 생성 화면에서 쓰이도록 <see cref="SelectableCharacter"/>·<c>BoxCollider2D</c>·
    /// SPUM 애니메이터를 갖추게 보정한다(멱등 — 여러 번 실행해도 결과가 같다).
    /// 메뉴: TaskbarHero/캐릭터/캐릭터 프리팹 DB 배선
    /// </summary>
    public static class CharacterPrefabDatabaseBuilder
    {
        private const string PrefabDir = "Assets/Prefabs/Character";
        private const string AssetPath = "Assets/Resources/CharacterPrefabDatabase.asset";
        private const string SpumAnimatorType = "SpumCharacterAnimator"; // Assembly-CSharp(asmdef 없음)

        /// <summary>파일명 앞부분(직업) → class_master 코드.</summary>
        private static readonly Dictionary<string, int> ClassCodes = new Dictionary<string, int>
        {
            { "Knight", 1 },
            { "Archer", 2 },
            { "Mage", 3 },
            { "Slayer", 4 },
        };

        /// <summary>파일명 뒷부분(성별) → gender 값(1:남 2:여).</summary>
        private static readonly Dictionary<string, int> Genders = new Dictionary<string, int>
        {
            { "Male", 1 },
            { "Female", 2 },
        };

        /// <summary>직업 코드 → 선택 화면 표시 이름의 기본값(기존 프리팹에 이름이 없을 때만 사용).</summary>
        private static readonly Dictionary<int, string> DefaultDisplayNames = new Dictionary<int, string>
        {
            { 1, "기사" },
            { 2, "궁수" },
            { 3, "마법사" },
            { 4, "슬레이어" },
        };

        [MenuItem("TaskbarHero/캐릭터/캐릭터 프리팹 DB 배선")]
        public static void Build()
        {
            var targets = new List<(string path, int classCode, int gender)>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabDir }))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetDirectoryName(assetPath).Replace('\\', '/') != PrefabDir)
                {
                    continue; // 하위 폴더(Monster 등) 제외
                }

                string fileName = Path.GetFileNameWithoutExtension(assetPath);
                if (!TryParse(fileName, out int classCode, out int gender))
                {
                    Debug.LogWarning($"[CharacterPrefabDatabaseBuilder] 이름 규칙(직업_성별)에 맞지 않아 건너뜀: {assetPath}");
                    continue;
                }
                targets.Add((assetPath, classCode, gender));
            }

            // 이미 지정돼 있는 직업 표시 이름을 먼저 모아, 새로 추가된 성별 프리팹이 같은 이름을 쓰게 한다.
            var namesByClass = CollectDisplayNames(targets);

            var entries = new List<CharacterPrefabDatabase.Entry>();
            foreach (var (assetPath, classCode, gender) in targets)
            {
                string displayName = namesByClass.TryGetValue(classCode, out var n) && !string.IsNullOrEmpty(n)
                    ? n
                    : (DefaultDisplayNames.TryGetValue(classCode, out var d) ? d : string.Empty);

                EnsureSelectable(assetPath, classCode, gender, displayName);

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (prefab != null)
                {
                    entries.Add(new CharacterPrefabDatabase.Entry
                    {
                        classCode = classCode,
                        gender = gender,
                        prefab = prefab,
                    });
                }
            }

            entries.Sort((a, b) => a.classCode != b.classCode ? a.classCode - b.classCode : a.gender - b.gender);
            WriteAsset(entries);
        }

        /// <summary>기존 프리팹에 지정된 직업별 표시 이름을 모은다(직업 코드 → 이름).</summary>
        private static Dictionary<int, string> CollectDisplayNames(List<(string path, int classCode, int gender)> targets)
        {
            var names = new Dictionary<int, string>();
            foreach (var (assetPath, classCode, _) in targets)
            {
                if (names.ContainsKey(classCode))
                {
                    continue;
                }
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                var selectable = prefab != null ? prefab.GetComponent<SelectableCharacter>() : null;
                if (selectable != null && !string.IsNullOrEmpty(selectable.DisplayName))
                {
                    names[classCode] = selectable.DisplayName;
                }
            }
            return names;
        }

        /// <summary>"Knight_Male" 형태의 파일명을 직업 코드·성별로 해석한다(규칙에 맞지 않으면 false).</summary>
        private static bool TryParse(string fileName, out int classCode, out int gender)
        {
            classCode = 0;
            gender = 0;
            int sep = fileName.IndexOf('_');
            if (sep <= 0 || sep >= fileName.Length - 1)
            {
                return false;
            }
            string classPart = fileName.Substring(0, sep);
            string genderPart = fileName.Substring(sep + 1);
            return ClassCodes.TryGetValue(classPart, out classCode) && Genders.TryGetValue(genderPart, out gender);
        }

        /// <summary>
        /// 캐릭터 프리팹에 선택 화면용 구성 요소를 보정한다.
        /// <see cref="SelectableCharacter"/>(직업·표시 이름·성별)를 채우고, 클릭 판정용 <c>BoxCollider2D</c>와
        /// SPUM 애니메이터가 없으면 렌더러 바운드를 기준으로 추가한다.
        /// </summary>
        private static void EnsureSelectable(string assetPath, int classCode, int gender, string displayName)
        {
            var root = PrefabUtility.LoadPrefabContents(assetPath);
            if (root == null)
            {
                return;
            }

            try
            {
                bool dirty = false;

                var selectable = root.GetComponent<SelectableCharacter>();
                if (selectable == null)
                {
                    selectable = root.AddComponent<SelectableCharacter>();
                    dirty = true;
                }
                var so = new SerializedObject(selectable);
                var classProp = so.FindProperty("classCode");
                var nameProp = so.FindProperty("displayName");
                var genderProp = so.FindProperty("gender");
                // 이미 이름이 지정돼 있으면 존중하고(사용자가 정한 표기), 비어 있을 때만 채운다.
                string name = string.IsNullOrEmpty(nameProp.stringValue) ? displayName : nameProp.stringValue;
                if (classProp.intValue != classCode || genderProp.intValue != gender || nameProp.stringValue != name)
                {
                    classProp.intValue = classCode;
                    genderProp.intValue = gender;
                    nameProp.stringValue = name;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    dirty = true;
                }

                if (root.GetComponent<Collider2D>() == null)
                {
                    var box = root.AddComponent<BoxCollider2D>();
                    if (TryRendererBounds(root, out var size, out var center))
                    {
                        box.size = size;
                        box.offset = center;
                    }
                    dirty = true;
                }

                if (EnsureSpumAnimator(root))
                {
                    dirty = true;
                }

                if (dirty)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, assetPath);
                    Debug.Log($"[CharacterPrefabDatabaseBuilder] 프리팹 보정: {assetPath} (class={classCode}, gender={gender})");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>SPUM 공격 애니메이션 헬퍼(Assembly-CSharp)를 붙인다. 이미 있거나 타입이 없으면 false.</summary>
        private static bool EnsureSpumAnimator(GameObject root)
        {
            var type = FindType(SpumAnimatorType);
            if (type == null || root.GetComponent(type) != null)
            {
                return false;
            }
            root.AddComponent(type);
            return true;
        }

        /// <summary>로드된 어셈블리에서 이름으로 MonoBehaviour 타입을 찾는다(없으면 null).</summary>
        private static System.Type FindType(string typeName)
        {
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(typeName, throwOnError: false);
                if (t != null && typeof(MonoBehaviour).IsAssignableFrom(t))
                {
                    return t;
                }
            }
            return null;
        }

        /// <summary>자식 렌더러 전체를 감싸는 바운드를 루트 로컬 기준 크기/중심으로 돌려준다(렌더러가 없으면 false).</summary>
        private static bool TryRendererBounds(GameObject root, out Vector2 size, out Vector2 center)
        {
            size = Vector2.one;
            center = Vector2.zero;
            var renderers = root.GetComponentsInChildren<SpriteRenderer>(includeInactive: false);
            bool any = false;
            Bounds bounds = new Bounds();
            foreach (var r in renderers)
            {
                if (r == null || r.sprite == null)
                {
                    continue;
                }
                if (!any)
                {
                    bounds = r.bounds;
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(r.bounds);
                }
            }
            if (!any)
            {
                return false;
            }

            Vector3 scale = root.transform.lossyScale;
            float sx = Mathf.Approximately(scale.x, 0f) ? 1f : Mathf.Abs(scale.x);
            float sy = Mathf.Approximately(scale.y, 0f) ? 1f : Mathf.Abs(scale.y);
            size = new Vector2(bounds.size.x / sx, bounds.size.y / sy);
            Vector3 local = bounds.center - root.transform.position;
            center = new Vector2(local.x / sx, local.y / sy);
            return true;
        }

        /// <summary>스캔 결과를 Resources의 스크립터블 오브젝트로 저장한다(없으면 생성).</summary>
        private static void WriteAsset(List<CharacterPrefabDatabase.Entry> entries)
        {
            var dir = Path.GetDirectoryName(AssetPath);
            if (!AssetDatabase.IsValidFolder(dir))
            {
                Directory.CreateDirectory(dir);
                AssetDatabase.Refresh();
            }

            var db = AssetDatabase.LoadAssetAtPath<CharacterPrefabDatabase>(AssetPath);
            bool created = db == null;
            if (created)
            {
                db = ScriptableObject.CreateInstance<CharacterPrefabDatabase>();
            }

            db.entries = entries.ToArray();
            if (created)
            {
                AssetDatabase.CreateAsset(db, AssetPath);
            }
            else
            {
                EditorUtility.SetDirty(db);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var sb = new System.Text.StringBuilder();
            foreach (var e in entries)
            {
                sb.Append($"\n  class={e.classCode} gender={e.gender} → {(e.prefab != null ? e.prefab.name : "null")}");
            }
            Debug.Log($"[CharacterPrefabDatabaseBuilder] {(created ? "생성" : "갱신")} 완료: {AssetPath} ({entries.Count}건){sb}");
        }
    }
}
