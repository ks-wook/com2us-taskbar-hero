using UnityEngine;
using UnityEngine.UI;

namespace TaskbarHero.Client.Battle
{
    /// <summary>
    /// 캐릭터 초상화를 "카메라 + 렌더텍스처"로 만든다.
    /// 대상 캐릭터(및 자식)를 전용 레이어로 옮기고, 그 레이어만 렌더하는 전용 카메라를
    /// RenderTexture에 그려 UI RawImage로 표시한다(배경은 단색). 대상 이동을 따라간다.
    /// 파티원이 늘어나면 이 리그를 캐릭터별로 하나씩 두면 된다.
    /// </summary>
    public class PortraitCameraRig : MonoBehaviour
    {
        [Tooltip("초상화를 표시할 RawImage")]
        public RawImage targetRawImage;
        [Tooltip("렌더텍스처 한 변 크기(px)")]
        public int renderSize = 256;
        [Tooltip("대상을 격리할 초상화 전용 레이어(0~31)")]
        public int portraitLayer = 31;
        [Tooltip("초상화 카메라 orthographic 크기(작을수록 확대). 얼굴만 담으려면 ~0.27")]
        public float orthoSize = 0.27f;
        [Tooltip("대상 기준 카메라 조준 오프셋(얼굴/머리를 잡도록 y를 올림)")]
        public Vector2 aimOffset = new Vector2(0f, 0.48f);
        [Tooltip("초상화 배경색")]
        public Color background = new Color(0.12f, 0.12f, 0.16f, 1f);
        [Tooltip("대상 GameObject 이름에 포함될 문자열(파티 멤버 구분용). 예: 기사 / 레인저")]
        public string targetNameContains = "Player_";

        [Tooltip("대상 Transform을 코드로 직접 지정(설정 시 이름 탐색 대신 이것을 사용). 동적 파티 UI용")]
        public Transform explicitTarget;

        private Camera _cam;
        private RenderTexture _rt;
        private Transform _target;

        private void Start()
        {
            _rt = new RenderTexture(renderSize, renderSize, 16) { name = "PortraitRT" };

            var camGo = new GameObject("PortraitCamera");
            camGo.transform.SetParent(transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.orthographic = true;
            _cam.orthographicSize = orthoSize;
            _cam.cullingMask = 1 << portraitLayer;   // 초상화 레이어만 렌더
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = background;
            _cam.targetTexture = _rt;
            _cam.depth = -5;

            if (targetRawImage != null)
            {
                targetRawImage.texture = _rt;
                targetRawImage.color = Color.white;
            }
        }

        private void LateUpdate()
        {
            if (_target == null)
            {
                _target = explicitTarget != null ? explicitTarget : FindPlayer();
                if (_target != null)
                {
                    SetLayerRecursive(_target.gameObject, portraitLayer); // 대상만 초상화 카메라에 보이도록
                }
            }
            if (_target == null || _cam == null)
            {
                return;
            }

            Vector3 p = _target.position;
            _cam.transform.position = new Vector3(p.x + aimOffset.x, p.y + aimOffset.y, -10f);
        }

        private void OnDestroy()
        {
            if (_cam != null)
            {
                _cam.targetTexture = null;
            }
            if (_rt != null)
            {
                _rt.Release();
                Destroy(_rt);
            }
        }

        private Transform FindPlayer()
        {
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (t.name.StartsWith("Player_") && t.name.Contains(targetNameContains))
                {
                    return t;
                }
            }
            return null;
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform)
            {
                SetLayerRecursive(c.gameObject, layer);
            }
        }
    }
}
