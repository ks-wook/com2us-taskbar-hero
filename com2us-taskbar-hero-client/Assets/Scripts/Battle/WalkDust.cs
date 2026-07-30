using System;
using System.Collections;
using UnityEngine;

namespace TaskbarHero.Client.Battle
{
    /// <summary>
    /// 캐릭터(아군·몬스터) 발밑에 붙어, 이동(걷기) 애니메이션이 재생되는 동안에만 먼지 프레임을 반복 재생하는 이펙트.
    /// 소유 유닛의 이동 상태를 주입된 델리게이트로 매 프레임 확인해, 걸을 때만 표시하고 멈추면 숨긴다.
    /// 캐릭터 자식으로 붙어 위치를 따라가며, 확대된 부모(예: 보스) 스케일을 보정해 일정한 월드 크기로 표시한다.
    /// 부모의 좌우 반전(바라보는 방향)은 상쇄해, 먼지가 이동 방향에 맞는 좌우로 그려지게 한다.
    /// </summary>
    public class WalkDust : MonoBehaviour
    {
        private Sprite[] _frames;
        private float _fps = 24f;
        private Func<bool> _isMoving;
        private SpriteRenderer _sr;
        private float _timer;
        private int _index;
        private bool _on;

        /// <summary>먼지 이펙트를 초기화한다. isMoving은 소유 유닛의 이동(걷기) 여부를 돌려주는 델리게이트.</summary>
        public void Initialize(Sprite[] frames, float fps, Func<bool> isMoving, float worldWidth)
        {
            _frames = frames;
            _fps = fps > 0f ? fps : 24f;
            _isMoving = isMoving;

            _sr = gameObject.AddComponent<SpriteRenderer>();
            if (_frames != null && _frames.Length > 0)
            {
                _sr.sprite = _frames[0];
            }
            _sr.enabled = false; // 시작은 숨김(멈춘 상태)

            transform.localPosition = new Vector3(0f, 0.03f, 0f); // 발밑
            StartCoroutine(SetupVisual(worldWidth));
        }

        /// <summary>SPUM 파트가 구성될 때까지 잠깐 기다린 뒤, 캐릭터 스프라이트 뒤(정렬)로 두고 월드 크기를 보정한다.</summary>
        private IEnumerator SetupVisual(float worldWidth)
        {
            yield return null;
            yield return null;
            if (this == null || _sr == null)
            {
                yield break;
            }

            // 부모(캐릭터) 스프라이트들의 최소 정렬 순서보다 뒤에 그려 발밑에 깔리게 한다.
            var rends = GetComponentInParent<Transform>().GetComponentsInChildren<SpriteRenderer>(true);
            int minOrder = int.MaxValue;
            int layerId = 0;
            foreach (var r in rends)
            {
                if (r == null || r == _sr) continue;
                if (r.sortingOrder < minOrder) { minOrder = r.sortingOrder; layerId = r.sortingLayerID; }
            }
            if (minOrder != int.MaxValue)
            {
                _sr.sortingLayerID = layerId;
                _sr.sortingOrder = minOrder - 1;
            }

            // 확대된 부모 스케일 보정 → 일정한 월드 폭으로 표시.
            // x는 음수로 둬 부모의 좌우 반전을 상쇄한다 — 캐릭터는 바라보는 방향에 따라 루트
            // localScale.x 부호가 뒤집히므로(SPUM 기본 스프라이트가 왼쪽을 봄 → 오른쪽 = 음수),
            // 자식인 먼지가 그 부호를 그대로 상속하면 이동 방향과 좌우가 반대로 그려진다.
            if (_frames != null && _frames.Length > 0 && _frames[0] != null)
            {
                float spriteW = _frames[0].bounds.size.x;
                float ws = spriteW > 0.001f ? worldWidth / spriteW : 1f;
                Vector3 lossy = transform.lossyScale;
                transform.localScale = new Vector3(
                    -ws / Mathf.Max(0.0001f, Mathf.Abs(lossy.x)),
                    ws / Mathf.Max(0.0001f, Mathf.Abs(lossy.y)),
                    1f);
            }
            else
            {
                // 크기 보정 대상이 없어도 좌우 반전 상쇄는 적용한다.
                Vector3 s = transform.localScale;
                transform.localScale = new Vector3(-Mathf.Abs(s.x), s.y, s.z);
            }
        }

        private void Update()
        {
            bool moving = _isMoving != null && _isMoving();
            if (moving != _on)
            {
                _on = moving;
                if (_sr != null) _sr.enabled = moving;
                if (moving)
                {
                    // 걷기 시작: 처음 프레임부터 재생.
                    _timer = 0f;
                    _index = 0;
                    if (_sr != null && _frames != null && _frames.Length > 0) _sr.sprite = _frames[0];
                }
            }

            if (!_on || _frames == null || _frames.Length == 0)
            {
                return;
            }

            _timer += Time.deltaTime;
            float dur = 1f / Mathf.Max(1f, _fps);
            while (_timer >= dur)
            {
                _timer -= dur;
                _index = (_index + 1) % _frames.Length; // 반복 재생
                if (_sr != null) _sr.sprite = _frames[_index];
            }
        }
    }
}
