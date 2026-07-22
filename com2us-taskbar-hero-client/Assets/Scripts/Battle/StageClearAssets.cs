using System;
using System.Collections.Generic;
using UnityEngine;

namespace TaskbarHero.Client.Battle
{
    /// <summary>
    /// 스테이지 클리어 연출에 필요한 런타임 에셋 묶음(스크립터블 오브젝트).
    /// - 클리어 팡파레 프레임 시퀀스(<see cref="fanfareFrames"/>)
    /// - 아이템 코드→아이콘 스프라이트 매핑(<see cref="itemIcons"/>, 보상 노출용)
    /// 아이콘/프레임 원본은 Resources 밖(Art/)에 있으므로, 이 에셋이 GUID로 참조를 들고
    /// <c>Assets/Resources/StageClearAssets.asset</c>에 저장되어 <c>Resources.Load</c>로 로드된다.
    /// 에셋은 <c>StageClearAssetsBuilder</c>(에디터)가 폴더를 스캔해 채운다.
    /// </summary>
    [CreateAssetMenu(fileName = "StageClearAssets", menuName = "TaskbarHero/StageClearAssets")]
    public class StageClearAssets : ScriptableObject
    {
        /// <summary>Resources 경로(확장자 제외).</summary>
        public const string ResourcePath = "StageClearAssets";

        [Tooltip("클리어 팡파레 프레임(StageClearFanfare_01~30 순서).")]
        public Sprite[] fanfareFrames = new Sprite[0];

        [Tooltip("아이템 코드 → 아이콘 스프라이트(item_{code}.png).")]
        public IconEntry[] itemIcons = new IconEntry[0];

        /// <summary>아이템 코드와 아이콘 스프라이트의 한 쌍.</summary>
        [Serializable]
        public struct IconEntry
        {
            public int code;
            public Sprite sprite;
        }

        private Dictionary<int, Sprite> _iconMap;

        /// <summary>아이템 코드로 아이콘을 조회한다(없으면 null).</summary>
        public Sprite GetIcon(int code)
        {
            if (_iconMap == null)
            {
                _iconMap = new Dictionary<int, Sprite>(itemIcons != null ? itemIcons.Length : 0);
                if (itemIcons != null)
                {
                    foreach (var e in itemIcons)
                    {
                        if (e.sprite != null && !_iconMap.ContainsKey(e.code))
                        {
                            _iconMap[e.code] = e.sprite;
                        }
                    }
                }
            }
            return _iconMap.TryGetValue(code, out var s) ? s : null;
        }

        /// <summary>Resources에서 에셋을 로드한다(없으면 null).</summary>
        public static StageClearAssets Load()
        {
            return Resources.Load<StageClearAssets>(ResourcePath);
        }
    }
}
