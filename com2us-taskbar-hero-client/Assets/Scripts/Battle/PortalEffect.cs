using UnityEngine;

namespace TaskbarHero.Client.Battle
{
    /// <summary>
    /// 지역 이동 포탈(<c>Assets/Art/Effect/Object/Portal/TravelPortal</c>) 연출.
    /// <para>보스러시에서 <b>라운드 보스를 처치하면 길 앞에 생성</b>되고, 파티가 걸어 들어가면
    /// 다음 라운드의 지역으로 넘어간다(보스러시 UI 기획서 4장). 프레임은 반복 재생하며,
    /// 등장·퇴장은 바닥을 축으로 커졌다 작아지는 스케일 연출이다.</para>
    /// <para>오브젝트 위치는 <b>포탈이 서는 바닥 지점</b>(길 y)이고, 스프라이트는 그 지점에 <b>아랫변이 닿도록</b>
    /// 코드가 정렬한다 — 이 아트의 피벗은 <b>가운데</b>(TravelPortal_NN_0)라 그대로 두면 포탈의 아래 절반이
    /// 바닥 아래로 묻힌다. 피벗이 어떤 값이어도 맞도록 스프라이트 bounds로 오프셋을 계산한다.</para>
    /// <para>시간은 전부 <c>unscaled</c>다 — 보스 처치 직후의 슬로우모션·히트스톱에 연출이 끌려가지 않게 한다.</para>
    /// </summary>
    public class PortalEffect : MonoBehaviour
    {
        /// <summary>캐릭터·몬스터보다 뒤, 배경보다 앞(파티가 포탈 앞을 지나 들어가는 것처럼 보인다).</summary>
        private const int PortalSortingOrder = 5;

        private const float OpenSeconds = 0.45f;
        private const float CloseSeconds = 0.30f;

        private Transform _art;      // 스프라이트(좌우 중심 보정된 자식)
        private SpriteRenderer _sr;
        private float _t;
        private bool _closing;
        private float _baseScale = 1f;
        private Vector2 _artOffset;  // 피벗을 "가로 중앙·아랫변이 바닥" 기준으로 옮기는 보정(스케일에 비례해 적용)

        /// <summary>포탈이 선 바닥 지점(월드).</summary>
        public Vector3 GroundPosition => transform.position;

        /// <summary>
        /// 포탈을 생성해 재생한다. <paramref name="groundPos"/>는 포탈이 서는 <b>바닥 지점</b>(길 y),
        /// <paramref name="worldHeight"/>는 포탈 높이(월드 단위)다. 프레임이 없으면 아무것도 만들지 않고 null.
        /// </summary>
        public static PortalEffect Spawn(Sprite[] frames, Vector3 groundPos, float worldHeight, float fps)
        {
            if (frames == null || frames.Length == 0 || frames[0] == null)
            {
                Debug.LogWarning("[Portal] 포탈 프레임이 배선되지 않아 생성하지 않습니다.");
                return null;
            }

            var go = new GameObject("TravelPortal");
            go.transform.position = groundPos;
            var portal = go.AddComponent<PortalEffect>();
            portal.Build(frames, worldHeight, fps);
            return portal;
        }

        /// <summary>스프라이트 자식을 만들어 높이를 맞추고 좌우 중심을 보정한다(피벗이 좌하단이므로).</summary>
        private void Build(Sprite[] frames, float worldHeight, float fps)
        {
            var first = frames[0];
            float h = Mathf.Max(0.01f, first.bounds.size.y);
            _baseScale = worldHeight / h;

            // 프레임을 배정하기 <b>전에</b> 활성 상태로 두면 SpriteSequenceEffect의 OnEnable → Play()가
            // 빈 프레임 배열로 실행돼 재생이 시작되지 않는다(첫 장만 멈춘 채로 보인다).
            // 그래서 꺼진 채로 구성한 뒤 마지막에 켠다(레벨업 글로우와 같은 방식).
            var art = new GameObject("Art");
            art.SetActive(false);
            art.transform.SetParent(transform, false);
            // 피벗 위치와 무관하게 "가로는 가운데, 세로는 아랫변이 바닥(오브젝트 y)"에 오도록 보정한다.
            var c = first.bounds.center;
            _artOffset = new Vector2(-c.x * _baseScale, (-c.y + first.bounds.size.y * 0.5f) * _baseScale);
            art.transform.localPosition = new Vector3(_artOffset.x, _artOffset.y, 0f);
            art.transform.localScale = Vector3.one * _baseScale;

            _sr = art.AddComponent<SpriteRenderer>();
            _sr.sprite = first;
            _sr.sortingOrder = PortalSortingOrder;

            var seq = art.AddComponent<SpriteSequenceEffect>();
            seq.frames = frames;
            seq.fps = fps;
            seq.loop = true;
            seq.destroyOnFinish = false;

            _art = art.transform;
            ApplyScale(0f);      // 등장 연출 시작 크기(첫 프레임 튐 방지)
            art.SetActive(true); // 프레임이 채워진 상태로 OnEnable → Play()가 돌아 반복 재생이 시작된다
            seq.Play();          // 활성화 순서에 기대지 않도록 한 번 더 명시적으로 시작한다
        }

        /// <summary>포탈을 닫는다 — 작아지며 사라진 뒤 자동 파괴된다(중복 호출 무시).</summary>
        public void Close()
        {
            if (_closing)
            {
                return;
            }
            _closing = true;
            _t = 0f;
        }

        private void Update()
        {
            if (_art == null)
            {
                return;
            }
            _t += Time.unscaledDeltaTime;

            if (_closing)
            {
                float k = Mathf.Clamp01(_t / CloseSeconds);
                ApplyScale(Mathf.Lerp(1f, 0f, k));
                if (k >= 1f)
                {
                    Destroy(gameObject);
                }
                return;
            }

            // 등장: 바닥을 축으로 0 → 1(끝에서 살짝 넘겼다 제자리로).
            float o = Mathf.Clamp01(_t / OpenSeconds);
            float u = o - 1f;
            float ease = 1f + 2.2f * u * u * u + 1.2f * u * u;
            ApplyScale(o >= 1f ? 1f : Mathf.LerpUnclamped(0f, 1f, ease));
        }

        /// <summary>등장·퇴장 배율을 기본 스케일 위에 얹는다.</summary>
        private void ApplyScale(float k)
        {
            float k0 = Mathf.Max(0f, k);
            float s = _baseScale * k0;
            _art.localScale = new Vector3(s, s, 1f);
            // 보정도 배율에 비례해야 <b>바닥을 축으로</b> 커진다(위치가 흔들리지 않는다).
            _art.localPosition = new Vector3(_artOffset.x * k0, _artOffset.y * k0, 0f);
        }
    }
}
