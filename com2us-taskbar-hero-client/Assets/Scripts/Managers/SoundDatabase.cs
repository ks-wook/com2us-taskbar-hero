using System;
using System.Collections.Generic;
using UnityEngine;

namespace TaskbarHero.Client.Managers
{
    /// <summary>
    /// <see cref="SoundId"/> → <see cref="AudioClip"/> 매핑 에셋(<c>Assets/Resources/SoundDatabase.asset</c>).
    /// 아이템/스킬 아이콘 DB와 같은 패턴이며, 에디터 빌더(SoundDatabaseBuilder)가
    /// <c>Assets/Sound</c>를 훑어 파일명으로 채운다. 없는 ID는 클립이 비어 있고 재생 시 조용히 무시된다.
    /// </summary>
    [CreateAssetMenu(fileName = "SoundDatabase", menuName = "TaskbarHero/Sound Database")]
    public class SoundDatabase : ScriptableObject
    {
        /// <summary>Resources 기준 경로(확장자 없음).</summary>
        public const string ResourcePath = "SoundDatabase";

        [Serializable]
        public class Entry
        {
            public SoundId id;
            public AudioClip clip;
        }

        [Tooltip("에디터 빌더가 채운다. 수동 편집도 가능하지만 재빌드 시 덮어써진다.")]
        public List<Entry> entries = new List<Entry>();

        private Dictionary<SoundId, AudioClip> _lookup;
        private static SoundDatabase _cached;

        /// <summary>Resources에서 DB를 1회 로드해 캐싱한다(없으면 null).</summary>
        public static SoundDatabase Load()
        {
            if (_cached == null)
            {
                _cached = Resources.Load<SoundDatabase>(ResourcePath);
            }
            return _cached;
        }

        /// <summary>ID에 해당하는 클립(없으면 null). 첫 호출 때 조회용 사전을 만든다.</summary>
        public AudioClip Get(SoundId id)
        {
            if (id == SoundId.None)
            {
                return null;
            }
            if (_lookup == null)
            {
                _lookup = new Dictionary<SoundId, AudioClip>();
                foreach (var e in entries)
                {
                    if (e != null && e.clip != null)
                    {
                        _lookup[e.id] = e.clip;
                    }
                }
            }
            return _lookup.TryGetValue(id, out var clip) ? clip : null;
        }

        /// <summary>에디터 빌더 전용: 매핑을 통째로 교체한다(캐시된 사전도 폐기).</summary>
        public void EditorSetEntries(List<Entry> newEntries)
        {
            entries = newEntries;
            _lookup = null;
        }
    }
}
