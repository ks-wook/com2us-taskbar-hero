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

        [Tooltip("보상 아이템 칸 테두리 프레임(Assets/Art/UI/item_slot.png). 없으면 색 사각형 폴백.")]
        public Sprite itemSlotFrame;

        [Tooltip("공용 아이템 슬롯 프리팹(Assets/Prefabs/UI/ItemSlot.prefab). 보상 아이템 칸에 사용하며, 없으면 코드 구성 폴백.")]
        public GameObject itemSlotPrefab;

        [Tooltip("버프 적용 표시 마크(Assets/Art/Icon/Etc/버프마크.png). 활성 버프로 늘어난 보상 칸 모서리에 작게 붙는다. 없으면 마크를 붙이지 않는다.")]
        public Sprite buffMarkIcon;

        [Tooltip("경험치 아이콘(Assets/Art/Icon/Etc/경험치.png). 경험치 보상이 HUD로 날아가는 연출에 쓴다(마스터 데이터에 없는 재화라 아이템 아이콘 매핑으로는 구할 수 없다).")]
        public Sprite expIcon;

        [Tooltip("스테이지 클리어 연출 오버레이 프리팹(Assets/Prefabs/UI/StageClearOverlay.prefab). 없으면 런타임 코드 구성 폴백.")]
        public GameObject overlayPrefab;

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
