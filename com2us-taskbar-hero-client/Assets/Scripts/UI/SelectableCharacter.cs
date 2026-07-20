using UnityEngine;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 캐릭터 선택 화면에서 클릭 가능한 캐릭터. 직업 코드/표시 이름을 보관한다.
    /// 클릭 감지를 위해 Collider2D가 함께 필요하다(별도 부착).
    /// </summary>
    public class SelectableCharacter : MonoBehaviour
    {
        [SerializeField] private int classCode;
        [SerializeField] private string displayName;

        public int ClassCode => classCode;
        public string DisplayName => displayName;
    }
}
