using System;
using System.Collections.Generic;
using UnityEngine;

namespace TaskbarHero.Client.Managers
{
    /// <summary>
    /// (직업, 성별) → 캐릭터 프리팹 매핑(스크립터블 오브젝트). 프리팹 원본은 Resources 밖
    /// (<c>Assets/Prefabs/Character/{직업}_{성별}.prefab</c>)에 있으므로, 이 에셋이 GUID로 참조를 들고
    /// <c>Assets/Resources/CharacterPrefabDatabase.asset</c>에 저장되어 <c>Resources.Load</c>로 로드된다.
    /// 에셋은 <c>CharacterPrefabDatabaseBuilder</c>(에디터)가 프리팹 폴더를 스캔해 채운다.
    /// 캐릭터 생성 화면·파티/인벤토리/오프라인 보상 초상화·전투 파티가 모두 이 DB를 통해
    /// 세이브의 <c>gender</c>(1:남 2:여)에 맞는 외형을 고른다.
    /// </summary>
    [CreateAssetMenu(fileName = "CharacterPrefabDatabase", menuName = "TaskbarHero/CharacterPrefabDatabase")]
    public class CharacterPrefabDatabase : ScriptableObject
    {
        /// <summary>Resources 경로(확장자 제외).</summary>
        public const string ResourcePath = "CharacterPrefabDatabase";

        /// <summary>성별 값이 비어 있을 때 사용하는 기본값(서버 기본값과 동일한 1:남).</summary>
        public const int DefaultGender = 1;

        [Tooltip("직업 코드 + 성별(1:남 2:여) → 캐릭터 프리팹(Prefabs/Character/{직업}_{성별}.prefab).")]
        public Entry[] entries = new Entry[0];

        /// <summary>직업·성별과 캐릭터 프리팹의 한 쌍.</summary>
        [Serializable]
        public struct Entry
        {
            public int classCode;
            [Tooltip("1:남 2:여")]
            public int gender;
            public GameObject prefab;
        }

        private Dictionary<int, GameObject> _map;

        private static int KeyOf(int classCode, int gender)
        {
            return classCode * 10 + gender;
        }

        /// <summary>
        /// 직업·성별에 해당하는 캐릭터 프리팹을 조회한다. 정확히 일치하는 항목이 없으면
        /// 같은 직업의 기본 성별(남) → 같은 직업의 아무 성별 순으로 폴백하고, 그래도 없으면 null.
        /// </summary>
        public GameObject Get(int classCode, int gender)
        {
            EnsureMap();
            if (_map.TryGetValue(KeyOf(classCode, gender), out var exact) && exact != null)
            {
                return exact;
            }
            if (_map.TryGetValue(KeyOf(classCode, DefaultGender), out var fallback) && fallback != null)
            {
                return fallback;
            }
            if (entries != null)
            {
                foreach (var e in entries)
                {
                    if (e.prefab != null && e.classCode == classCode)
                    {
                        return e.prefab;
                    }
                }
            }
            return null;
        }

        /// <summary>해당 직업에 지정 성별 프리팹이 실제로 등록돼 있는지(폴백 없이 정확 일치).</summary>
        public bool Has(int classCode, int gender)
        {
            EnsureMap();
            return _map.TryGetValue(KeyOf(classCode, gender), out var p) && p != null;
        }

        /// <summary>조회용 사전을 최초 1회 구성한다(에셋 변경 없이 런타임 캐시).</summary>
        private void EnsureMap()
        {
            if (_map != null)
            {
                return;
            }
            _map = new Dictionary<int, GameObject>(entries != null ? entries.Length : 0);
            if (entries == null)
            {
                return;
            }
            foreach (var e in entries)
            {
                if (e.prefab == null)
                {
                    continue;
                }
                int key = KeyOf(e.classCode, e.gender);
                if (!_map.ContainsKey(key))
                {
                    _map[key] = e.prefab;
                }
            }
        }

        private static CharacterPrefabDatabase _cached;

        /// <summary>Resources에서 에셋을 로드해 캐싱한다(없으면 null).</summary>
        public static CharacterPrefabDatabase Load()
        {
            if (_cached == null)
            {
                _cached = Resources.Load<CharacterPrefabDatabase>(ResourcePath);
            }
            return _cached;
        }

        /// <summary>DB에서 (직업, 성별) 프리팹을 바로 조회하는 단축 헬퍼(에셋이 없으면 null).</summary>
        public static GameObject PrefabOf(int classCode, int gender)
        {
            var db = Load();
            return db != null ? db.Get(classCode, gender) : null;
        }
    }
}
