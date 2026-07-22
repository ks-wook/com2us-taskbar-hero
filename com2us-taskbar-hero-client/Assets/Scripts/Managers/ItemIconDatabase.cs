using System;
using System.Collections.Generic;
using UnityEngine;

namespace TaskbarHero.Client.Managers
{
    /// <summary>
    /// 아이템 코드 → 아이콘 스프라이트 매핑(스크립터블 오브젝트). 아이콘 원본은 Resources 밖
    /// (<c>Assets/Art/Icon/Item/item_{code}.png</c>)에 있으므로, 이 에셋이 GUID로 참조를 들고
    /// <c>Assets/Resources/ItemIconDatabase.asset</c>에 저장되어 <c>Resources.Load</c>로 로드된다.
    /// 골드(코드 1) 아이콘도 포함한다. 에셋은 <c>ItemIconDatabaseBuilder</c>(에디터)가 폴더를 스캔해 채운다.
    /// </summary>
    [CreateAssetMenu(fileName = "ItemIconDatabase", menuName = "TaskbarHero/ItemIconDatabase")]
    public class ItemIconDatabase : ScriptableObject
    {
        /// <summary>Resources 경로(확장자 제외).</summary>
        public const string ResourcePath = "ItemIconDatabase";

        [Tooltip("아이템 코드 → 아이콘 스프라이트(item_{code}.png).")]
        public IconEntry[] entries = new IconEntry[0];

        /// <summary>아이템 코드와 아이콘 스프라이트의 한 쌍.</summary>
        [Serializable]
        public struct IconEntry
        {
            public int code;
            public Sprite sprite;
        }

        private Dictionary<int, Sprite> _map;

        /// <summary>아이템 코드로 아이콘을 조회한다(없으면 null).</summary>
        public Sprite Get(int code)
        {
            if (_map == null)
            {
                _map = new Dictionary<int, Sprite>(entries != null ? entries.Length : 0);
                if (entries != null)
                {
                    foreach (var e in entries)
                    {
                        if (e.sprite != null && !_map.ContainsKey(e.code))
                        {
                            _map[e.code] = e.sprite;
                        }
                    }
                }
            }
            return _map.TryGetValue(code, out var s) ? s : null;
        }

        private static ItemIconDatabase _cached;

        /// <summary>Resources에서 에셋을 로드해 캐싱한다(없으면 null).</summary>
        public static ItemIconDatabase Load()
        {
            if (_cached == null)
            {
                _cached = Resources.Load<ItemIconDatabase>(ResourcePath);
            }
            return _cached;
        }
    }
}
