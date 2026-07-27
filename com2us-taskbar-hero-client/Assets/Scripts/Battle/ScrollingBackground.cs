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
        [Tooltip("배경을 커버 영역 하단에서 이 비율 높이까지만 노출한다(스프라이트 아래쪽을 같은 비율로 잘라 사용). 1이면 전체 노출.")]
        [Range(0.05f, 1f)]
        public float visibleBottomFrac = 1f / 3f;

        private Camera _cam;
        private Transform[] _tiles;
        private float _tileWidth;
        private Sprite _croppedSprite; // Build()가 생성한 하단 크롭 스프라이트(재생성/파괴 시 정리)

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
            DestroyCroppedSprite();
            Build();
        }

        private void OnDestroy()
        {
            DestroyCroppedSprite();
        }

        /// <summary>Build()가 만든 하단 크롭 스프라이트를 파괴한다(재생성·오브젝트 파괴 시 누수 방지).</summary>
        private void DestroyCroppedSprite()
        {
            if (_croppedSprite != null)
            {
                Destroy(_croppedSprite);
                _croppedSprite = null;
            }
        }

        /// <summary>하단 크롭 반영 후 배경이 실제로 노출되는 영역의 상단 월드 y(스킬 UI 도킹 기준선).</summary>
        public float VisibleTopY
        {
            get
            {
                if (_cam == null) _cam = Camera.main;
                ComputeVisibleArea(out _, out float top);
                return top;
            }
        }

        /// <summary>현재 설정(autoFit·하단 크롭 반영)의 배경 노출 영역(월드 y 구간)을 계산한다.</summary>
        private void ComputeVisibleArea(out float bottomY, out float topY)
        {
            float h = worldHeight;
            float cy = centerY;
            if (autoFitCamera && _cam != null && _cam.orthographic)
            {
                // 화면 꽉 채우기: 카메라 뷰 높이(orthographicSize×2)·중심 y에 맞춘다(약간 여유).
                h = _cam.orthographicSize * 2f * 1.02f;
                cy = _cam.transform.position.y;
            }
            bottomY = cy - h * 0.5f;
            topY = bottomY + h * Mathf.Clamp(visibleBottomFrac, 0.05f, 1f);
        }

        /// <summary>원본 스프라이트의 아래쪽 frac 비율 영역만 참조하는 서브 렉트 스프라이트를 만든다(텍스처 복사 없음).</summary>
        private static Sprite CreateBottomCroppedSprite(Sprite src, float frac)
        {
            Rect r = src.rect;
            var cropped = new Rect(r.x, r.y, r.width, Mathf.Max(1f, r.height * frac));
            var s = Sprite.Create(src.texture, cropped, new Vector2(0.5f, 0.5f), src.pixelsPerUnit);
            s.name = src.name + "_bottomCrop";
            return s;
        }

        private void Build()
        {
            _cam = Camera.main;
            if (sprite == null)
            {
                Debug.LogWarning("[ScrollingBackground] sprite 미지정");
                return;
            }

            // 노출 영역(하단 크롭 반영)을 계산하고, 창 제어기에 배경 밴드로 보고한다
            // (밴드 위의 커서는 게임 콘텐츠로 취급 → 클릭 통과 제외·창 드래그 그립).
            ComputeVisibleArea(out float visBottom, out float visTop);
            float h = visTop - visBottom;
            float cy = (visBottom + visTop) * 0.5f;
            TaskbarHero.Client.Managers.TaskbarWindow.ReportContentBand(visBottom, visTop);

            // 하단 크롭: 스프라이트도 같은 비율로 아래쪽만 잘라 스케일(픽셀 밀도)을 유지한 채 그 자리에 배치한다.
            Sprite drawSprite = sprite;
            float frac = Mathf.Clamp(visibleBottomFrac, 0.05f, 1f);
            if (frac < 1f)
            {
                _croppedSprite = CreateBottomCroppedSprite(sprite, frac);
                drawSprite = _croppedSprite;
            }

            float scale = h / drawSprite.bounds.size.y;
            _tileWidth = drawSprite.bounds.size.x * scale;

            int count = Mathf.Max(2, tileCount);
            _tiles = new Transform[count];

            float startX = (_cam != null ? _cam.transform.position.x : 0f) - _tileWidth;
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("BGTile_" + i);
                go.transform.SetParent(transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = drawSprite;
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
