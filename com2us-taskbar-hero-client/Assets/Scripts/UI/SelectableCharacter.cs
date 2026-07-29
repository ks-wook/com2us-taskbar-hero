using UnityEngine;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 캐릭터 선택 화면에서 클릭 가능한 캐릭터. 직업 코드/표시 이름/성별을 보관한다.
    /// 클릭 감지를 위해 Collider2D가 함께 필요하다(별도 부착).
    /// 성별은 이 프리팹이 어떤 외형인지를 나타내며(1:남 2:여), 캐릭터 생성 시 다른 성별을 고르면
    /// <see cref="CharacterSelectManager"/>가 같은 직업의 반대 성별 프리팹으로 교체해 보여준다.
    /// </summary>
    public class SelectableCharacter : MonoBehaviour
    {
        [SerializeField] private int classCode;
        [SerializeField] private string displayName;
        [Tooltip("이 프리팹의 외형 성별(1:남 2:여).")]
        [SerializeField] private int gender = 1;

        public int ClassCode => classCode;
        public string DisplayName => displayName;
        public int Gender => gender;
    }
}
