using UnityEngine;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 월드 오브젝트(SpriteRenderer 캐릭터 등)를 카메라 화면의 정규화 좌표(0~1)에 고정한다.
    /// 배경이 화면을 꽉 채우는(Screen Space - Camera, preserveAspect off) 구조에서,
    /// 화면 비율이 바뀌어도 캐릭터가 배경의 동일 지점에 유지되도록 매 프레임 위치를 재계산한다.
    /// </summary>
    public class ScreenAnchoredWorldObject : MonoBehaviour
    {
        [Tooltip("화면상의 정규화 위치(0~1). (0,0)=좌하단, (1,1)=우상단.")]
        [SerializeField] private Vector2 normalizedPosition = new Vector2(0.5f, 0.5f);

        [Tooltip("대상 카메라. 비우면 Camera.main을 사용한다.")]
        [SerializeField] private Camera targetCamera;

        private Camera Cam => targetCamera != null ? targetCamera : Camera.main;

        private void OnEnable() => Apply();
        private void LateUpdate() => Apply();

        /// <summary>현재 정규화 위치를 카메라 기준 월드 좌표로 변환해 위치를 갱신한다.</summary>
        public void Apply()
        {
            var cam = Cam;
            if (cam == null)
            {
                return;
            }

            float depth = Mathf.Abs(transform.position.z - cam.transform.position.z);
            var screen = new Vector3(normalizedPosition.x * cam.pixelWidth, normalizedPosition.y * cam.pixelHeight, depth);
            var world = cam.ScreenToWorldPoint(screen);
            transform.position = new Vector3(world.x, world.y, transform.position.z);
        }

        /// <summary>현재 월드 위치를 화면 정규화 좌표로 캡처해 저장한다(에디터 세팅용).</summary>
        public void CaptureFromCurrentPosition()
        {
            var cam = Cam;
            if (cam == null)
            {
                return;
            }

            var sp = cam.WorldToScreenPoint(transform.position);
            normalizedPosition = new Vector2(sp.x / cam.pixelWidth, sp.y / cam.pixelHeight);
        }
    }
}
