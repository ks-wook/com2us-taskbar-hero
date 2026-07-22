using UnityEngine;

namespace TaskbarHero.Client.Battle
{
    /// <summary>
    /// 배경 스프라이트를 가로로 여러 장 이어 붙이고, 카메라가 오른쪽으로 이동하면
    /// 화면 왼쪽으로 완전히 벗어난 타일을 오른쪽 끝으로 재배치(recycle)해
    /// 길이 끝없이 이어지는 것처럼 보이게 하는 무한 가로 스크롤 배경.
    ///
    /// 개발용 씬(BattleDevScene)에서 플레이어가 오른쪽으로 전진할 때 사용한다.
    /// </summary>
    public class ScrollingBackground : MonoBehaviour
    {
        [Tooltip("이어 붙일 배경 스프라이트(dungeon_bg_1 등)")]
        public Sprite sprite;
        [Tooltip("생성할 타일 수(뷰 너비를 덮고도 남게 3장 권장)")]
        public int tileCount = 3;
        [Tooltip("배경 정렬 순서(캐릭터보다 뒤)")]
        public int sortingOrder = -100;
        [Tooltip("배경이 채울 세로 높이(카메라 orthographicSize×2)")]
        public float worldHeight = 8f;
        [Tooltip("배경 세로 중심 y(카메라 y와 맞춤)")]
        public float centerY = 1f;
        [Tooltip("true면 worldHeight/centerY 대신 카메라 뷰 높이·중심에 맞춰 화면을 꽉 채운다.")]
        public bool autoFitCamera = false;

        private Camera _cam;
        private Transform[] _tiles;
        private float _tileWidth;

        private void Start()
        {
            Build();
        }

        /// <summary>배경 스프라이트를 바꾸고 타일을 다시 만든다(런타임, 예: 스테이지 배경 타입 반영).</summary>
        public void SetSprite(Sprite s)
        {
            sprite = s;
            if (_tiles != null)
            {
                foreach (var t in _tiles)
                {
                    if (t != null) Destroy(t.gameObject);
                }
                _tiles = null;
            }
            Build();
        }

        private void Build()
        {
            _cam = Camera.main;
            if (sprite == null)
            {
                Debug.LogWarning("[ScrollingBackground] sprite 미지정");
                return;
            }

            // 화면 꽉 채우기: 카메라 뷰 높이(orthographicSize×2)·중심 y에 맞춘다(약간 여유).
            float h = worldHeight;
            float cy = centerY;
            if (autoFitCamera && _cam != null && _cam.orthographic)
            {
                h = _cam.orthographicSize * 2f * 1.02f;
                cy = _cam.transform.position.y;
            }

            float scale = h / sprite.bounds.size.y;
            _tileWidth = sprite.bounds.size.x * scale;

            int count = Mathf.Max(2, tileCount);
            _tiles = new Transform[count];

            float startX = (_cam != null ? _cam.transform.position.x : 0f) - _tileWidth;
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("BGTile_" + i);
                go.transform.SetParent(transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.sortingOrder = sortingOrder;
                go.transform.localScale = new Vector3(scale, scale, 1f);
                go.transform.position = new Vector3(startX + i * _tileWidth, cy, 0f);
                _tiles[i] = go.transform;
            }
        }

        private void LateUpdate()
        {
            if (_tiles == null || _cam == null)
            {
                return;
            }

            float camX = _cam.transform.position.x;
            float viewHalf = _cam.orthographicSize * _cam.aspect;
            float total = _tileWidth * _tiles.Length;

            foreach (var t in _tiles)
            {
                // 타일의 오른쪽 끝이 뷰 왼쪽 밖으로 나가면 맨 오른쪽으로 재배치
                if (t.position.x + _tileWidth * 0.5f < camX - viewHalf)
                {
                    t.position += new Vector3(total, 0f, 0f);
                }
            }
        }
    }
}
