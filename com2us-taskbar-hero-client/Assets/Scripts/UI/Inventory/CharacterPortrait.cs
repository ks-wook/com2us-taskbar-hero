using UnityEngine;
using UnityEngine.UI;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 캐릭터 프리팹을 "전용 카메라 + RenderTexture"로 렌더해 UI RawImage에 표시하는 초상화 렌더러.
    /// 대상 프리팹을 화면 밖 격리 위치에 인스턴스화하고 전용 레이어로 옮긴 뒤, 그 레이어만 렌더하는
    /// 직교 카메라로 RenderTexture에 그려 RawImage로 보여준다(배경 단색). 인벤토리 초상화 슬롯 전용.
    /// (전투용 <c>PortraitCameraRig</c>와 별개로, UI가 Battle 어셈블리에 의존하지 않도록 UI 로컬로 둔다.)
    /// </summary>
    public class CharacterPortrait : MonoBehaviour
    {
        private RawImage _target;
        private int _layer;
        private float _orthoSize;
        private Vector2 _aimOffset;
        private Vector3 _stageOrigin;

        private Camera _cam;
        private RenderTexture _rt;
        private GameObject _current; // 현재 표시 중인 캐릭터 인스턴스(this 아래 자식)

        /// <summary>렌더러를 초기화한다: 대상 RawImage·격리 레이어·카메라 파라미터로 전용 카메라와 RenderTexture를 생성해 연결한다.</summary>
        public void Initialize(RawImage target, int layer, int rtWidth, int rtHeight,
            float orthoSize, Vector2 aimOffset, Color background, Vector3 stageOrigin)
        {
            _target = target;
            _layer = layer;
            _orthoSize = orthoSize;
            _aimOffset = aimOffset;
            _stageOrigin = stageOrigin;

            _rt = new RenderTexture(Mathf.Max(8, rtWidth), Mathf.Max(8, rtHeight), 16) { name = "InventoryPortraitRT" };

            var camGo = new GameObject("InventoryPortraitCamera");
            camGo.transform.SetParent(transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.orthographic = true;
            _cam.orthographicSize = _orthoSize;
            _cam.cullingMask = 1 << _layer;   // 초상화 레이어만 렌더
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = background;
            _cam.targetTexture = _rt;
            _cam.depth = -5;
            _cam.transform.position = new Vector3(_stageOrigin.x + _aimOffset.x, _stageOrigin.y + _aimOffset.y, -10f);

            if (_target != null)
            {
                _target.texture = _rt;
            }
        }

        /// <summary>표시할 캐릭터 프리팹을 교체한다(이전 인스턴스 제거 후 새로 인스턴스화하고 격리 레이어에 배치).</summary>
        public void SetCharacter(GameObject prefab)
        {
            if (_current != null)
            {
                Destroy(_current);
                _current = null;
            }
            if (prefab == null)
            {
                return;
            }

            _current = Instantiate(prefab, _stageOrigin, Quaternion.identity, transform);
            _current.name = "PortraitCharacter";
            DisablePhysics(_current);
            SetLayerRecursive(_current, _layer);
        }

        /// <summary>격리 레이어를 매 프레임 재적용한다(SPUM 등이 런타임에 자식을 추가해도 초상화 카메라에 포함되도록).</summary>
        private void LateUpdate()
        {
            if (_current != null)
            {
                SetLayerRecursive(_current, _layer);
            }
        }

        /// <summary>초상화 캐릭터가 낙하·이동하지 않도록 물리 시뮬레이션(Rigidbody2D)을 끈다.</summary>
        private static void DisablePhysics(GameObject go)
        {
            var rb = go.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.simulated = false;
            }
        }

        /// <summary>카메라·RenderTexture 자원을 해제한다.</summary>
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

        /// <summary>GameObject와 모든 자식의 레이어를 재귀적으로 지정한다.</summary>
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
