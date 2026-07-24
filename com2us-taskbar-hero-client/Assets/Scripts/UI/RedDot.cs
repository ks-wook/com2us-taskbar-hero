using System;
using UnityEngine;
using UnityEngine.UI;
using TaskbarHero.Client.Managers;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// UI 요소 <b>우측 상단</b>에 붙는 빨간 알림 점(레드닷). 바인딩한 조건(<see cref="Func{Boolean}"/>)이 참일 때만 표시된다.
    /// 세션 변경(<see cref="Session.InventoryChanged"/>) 시 자동으로 조건을 다시 평가하므로, 데이터가 바뀌면
    /// 별도 호출 없이 표시/숨김이 갱신된다. 어떤 버튼·아이콘에도 붙일 수 있는 재사용 공용 컴포넌트다.
    /// </summary>
    [RequireComponent(typeof(Image))]
    public class RedDot : MonoBehaviour
    {
        private Image _img;
        private Func<bool> _condition;

        private static Sprite _circleSprite;

        /// <summary>안티에일리어싱된 흰색 원형 스프라이트(색은 Image.color로 지정). 빌트인 리소스에 의존하지 않도록 1회 생성·캐싱한다.</summary>
        private static Sprite CircleSprite()
        {
            if (_circleSprite != null)
            {
                return _circleSprite;
            }
            const int s = 32;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float r = s / 2f;
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    float dx = x + 0.5f - r, dy = y + 0.5f - r;
                    float a = Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy)); // 가장자리 1px 부드럽게
                    px[y * s + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            _circleSprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
            return _circleSprite;
        }

        /// <summary>대상 RectTransform의 우측 상단 모서리에 레드닷을 생성해 반환한다(조건 바인딩 전엔 숨김).</summary>
        public static RedDot AttachTopRight(RectTransform target, float size = 26f, Vector2 offset = default)
        {
            var go = new GameObject("RedDot", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(target, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f); // 우측 상단 모서리
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = offset;                      // 기본 (0,0): 모서리 중앙

            var img = go.GetComponent<Image>();
            img.sprite = CircleSprite(); // 런타임 생성 원형(빌트인 리소스 비의존)
            img.color = new Color(0.9f, 0.14f, 0.14f, 1f);
            img.raycastTarget = false;

            var rd = go.AddComponent<RedDot>();
            rd._img = img;
            img.enabled = false; // 조건 바인딩/평가 전엔 숨김
            return rd;
        }

        /// <summary>표시 조건을 바인딩하고 즉시 평가한다(조건 참 → 점 표시).</summary>
        public void Bind(Func<bool> condition)
        {
            _condition = condition;
            Refresh();
        }

        /// <summary>조건을 재평가해 표시/숨김을 갱신한다.</summary>
        public void Refresh()
        {
            if (_img != null)
            {
                _img.enabled = _condition != null && _condition();
            }
        }

        private void Awake()
        {
            if (_img == null)
            {
                _img = GetComponent<Image>();
            }
        }

        private void OnEnable()
        {
            Session.InventoryChanged += Refresh; // 세이브/인벤토리 변경 시 자동 재평가
            Refresh();
        }

        private void OnDisable()
        {
            Session.InventoryChanged -= Refresh;
        }
    }
}
