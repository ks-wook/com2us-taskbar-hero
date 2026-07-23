using System;
using System.Collections.Generic;
using UnityEngine;

namespace TaskbarHero.Client.Managers
{
    /// <summary>
    /// 스킬 코드 → 아이콘 스프라이트 매핑(스크립터블 오브젝트). 아이콘 원본은 Resources 밖
    /// (<c>Assets/Art/Icon/Combat/{직업}/{스킬명}.png</c>)에 있으므로, 이 에셋이 GUID로 참조를 들고
    /// <c>Assets/Resources/SkillIconDatabase.asset</c>에 저장되어 <c>Resources.Load</c>로 로드된다.
    /// 에셋은 <c>SkillIconDatabaseBuilder</c>(에디터)가 skill_master와 아이콘 폴더를 대조해 채운다.
    /// (전투 파티 설정 <c>PartyMemberConfig.skills</c>와 같은 아이콘을 스킬 성장 UI에서도 재사용하기 위한 공용 DB.)
    /// </summary>
    [CreateAssetMenu(fileName = "SkillIconDatabase", menuName = "TaskbarHero/SkillIconDatabase")]
    public class SkillIconDatabase : ScriptableObject
    {
        /// <summary>Resources 경로(확장자 제외).</summary>
        public const string ResourcePath = "SkillIconDatabase";

        [Tooltip("스킬 코드 → 아이콘 스프라이트(Icon/Combat/{직업}/{스킬명}.png).")]
        public IconEntry[] entries = new IconEntry[0];

        /// <summary>스킬 코드와 아이콘 스프라이트의 한 쌍.</summary>
        [Serializable]
        public struct IconEntry
        {
            public int skillCode;
            public Sprite sprite;
        }

        private Dictionary<int, Sprite> _map;

        /// <summary>스킬 코드로 아이콘을 조회한다(없으면 null).</summary>
        public Sprite Get(int skillCode)
        {
            if (_map == null)
            {
                _map = new Dictionary<int, Sprite>(entries != null ? entries.Length : 0);
                if (entries != null)
                {
                    foreach (var e in entries)
                    {
                        if (e.sprite != null && !_map.ContainsKey(e.skillCode))
                        {
                            _map[e.skillCode] = e.sprite;
                        }
                    }
                }
            }
            return _map.TryGetValue(skillCode, out var s) ? s : null;
        }

        private static SkillIconDatabase _cached;

        /// <summary>Resources에서 에셋을 로드해 캐싱한다(없으면 null).</summary>
        public static SkillIconDatabase Load()
        {
            if (_cached == null)
            {
                _cached = Resources.Load<SkillIconDatabase>(ResourcePath);
            }
            return _cached;
        }
    }
}
