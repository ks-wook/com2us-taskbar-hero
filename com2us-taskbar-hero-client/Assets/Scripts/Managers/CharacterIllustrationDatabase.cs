using System;
using System.Collections.Generic;
using UnityEngine;

namespace TaskbarHero.Client.Managers
{
    /// <summary>
    /// (직업, 성별) → **캐릭터 일러스트 + 얼굴 위치** 매핑(스크립터블 오브젝트).
    /// 편성창 파티 카드처럼 일러스트의 <b>얼굴만 확대</b>해 보여주는 화면이 쓴다.
    ///
    /// <para><b>왜 얼굴 좌표를 데이터로 두는가</b> — 일러스트가 전신 그림이고 캐릭터마다 머리 위치·크기가
    /// 다르므로(활을 든 레인저는 상체가 기울어 있고, 슬레이어는 투구가 크다) 한 가지 크롭으로는 맞출 수 없다.
    /// 그림마다 <see cref="Entry.faceCenter"/>·<see cref="Entry.cropHeight"/>를 데이터로 들고 있으면
    /// 표시하는 쪽은 "창에 얼굴을 채워라"만 알면 되고, 그림을 교체할 때 코드를 고치지 않는다.</para>
    ///
    /// <para>참조 대상은 <c>Assets/Art/Character/Image/*.png</c> 8종이다. 이 그림들은 <b>배경이 제거되고
    /// 캐릭터에 맞춰 크롭</b>되어 있어 그대로 겹쳐 쓸 수 있다(예전의 초록 크로마키 원본 + <c>Cutout/</c> 파생본
    /// 구조는 폐기됐다). 에셋 배선은 <c>CharacterIllustrationDatabaseBuilder</c>(에디터)가 한다.</para>
    /// </summary>
    [CreateAssetMenu(fileName = "CharacterIllustrationDatabase", menuName = "TaskbarHero/CharacterIllustrationDatabase")]
    public class CharacterIllustrationDatabase : ScriptableObject
    {
        /// <summary>Resources 경로(확장자 제외).</summary>
        public const string ResourcePath = "CharacterIllustrationDatabase";

        /// <summary>성별 값이 비어 있을 때 사용하는 기본값(서버 기본값과 동일한 1:남).</summary>
        public const int DefaultGender = 1;

        [Tooltip("직업 코드 + 성별(1:남 2:여) → 일러스트와 얼굴 위치.")]
        public Entry[] entries = new Entry[0];

        /// <summary>일러스트 한 장과 그 안에서의 얼굴 위치.</summary>
        [Serializable]
        public struct Entry
        {
            public int classCode;
            [Tooltip("1:남 2:여")]
            public int gender;
            [Tooltip("배경을 지운 캐릭터 일러스트(Assets/Art/Character/Image).")]
            public Sprite sprite;
            [Tooltip("얼굴 중심의 normalized 좌표. x는 왼쪽부터, **y는 위에서부터** 0~1.")]
            public Vector2 faceCenter;
            [Tooltip("화면에 보일 크롭의 높이(이미지 높이 대비 0~1). 작을수록 얼굴이 크게 보인다.")]
            public float cropHeight;

            /// <summary>스프라이트가 있고 크롭 높이가 유효한 항목인지.</summary>
            public bool IsValid => sprite != null && cropHeight > 0f;
        }

        private Dictionary<int, Entry> _map;

        private static int KeyOf(int classCode, int gender)
        {
            return classCode * 10 + gender;
        }

        /// <summary>
        /// 직업·성별의 일러스트 항목을 조회한다. 정확히 일치하는 항목이 없으면
        /// 같은 직업의 기본 성별(남) → 같은 직업의 아무 성별 순으로 폴백한다
        /// (<see cref="CharacterPrefabDatabase.Get"/>와 같은 규칙).
        /// </summary>
        public bool TryGet(int classCode, int gender, out Entry entry)
        {
            EnsureMap();
            if (_map.TryGetValue(KeyOf(classCode, gender), out entry) && entry.IsValid)
            {
                return true;
            }
            if (_map.TryGetValue(KeyOf(classCode, DefaultGender), out entry) && entry.IsValid)
            {
                return true;
            }
            if (entries != null)
            {
                foreach (var e in entries)
                {
                    if (e.IsValid && e.classCode == classCode)
                    {
                        entry = e;
                        return true;
                    }
                }
            }
            entry = default;
            return false;
        }

        /// <summary>조회용 사전을 최초 1회 구성한다(에셋 변경 없이 런타임 캐시).</summary>
        private void EnsureMap()
        {
            if (_map != null)
            {
                return;
            }
            _map = new Dictionary<int, Entry>(entries != null ? entries.Length : 0);
            if (entries == null)
            {
                return;
            }
            foreach (var e in entries)
            {
                if (!e.IsValid)
                {
                    continue;
                }
                int key = KeyOf(e.classCode, e.gender);
                if (!_map.ContainsKey(key))
                {
                    _map[key] = e;
                }
            }
        }

        private static CharacterIllustrationDatabase _cached;

        /// <summary>Resources에서 에셋을 로드해 캐싱한다(없으면 null).</summary>
        public static CharacterIllustrationDatabase Load()
        {
            if (_cached == null)
            {
                _cached = Resources.Load<CharacterIllustrationDatabase>(ResourcePath);
            }
            return _cached;
        }

        /// <summary>DB에서 (직업, 성별) 항목을 바로 조회하는 단축 헬퍼(에셋이 없으면 false).</summary>
        public static bool TryGetIllustration(int classCode, int gender, out Entry entry)
        {
            var db = Load();
            if (db != null)
            {
                return db.TryGet(classCode, gender, out entry);
            }
            entry = default;
            return false;
        }
    }
}
