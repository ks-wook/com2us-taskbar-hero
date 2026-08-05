using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TaskbarHero.Client.Managers;

namespace TaskbarHero.Client.Battle
{
    /// <summary>
    /// 획득 보상이 <b>HUD로 날아가 들어가는</b> 연출(스테이지 클리어 오버레이가 닫힐 때 사용).
    /// 보상 칸이 있던 자리에서 목표 HUD 버튼까지 포물선을 그리며 작아지고, 도착하면 목표를 한 번 팝시킨다.
    ///
    /// <para><b>왜 별도 오브젝트인가</b>: 클리어 오버레이는 닫히는 순간 파괴되므로, 그 자식으로 만들면
    /// 날아가는 도중에 함께 사라진다. 그래서 자기 Canvas를 가진 독립 싱글턴으로 두고 오버레이가
    /// 위치·아이콘만 넘겨 준다.</para>
    ///
    /// <para><b>풀링</b>: 보상 칸은 최대 여러 개(경험치·골드·전리품)라 오브를 매번 생성하지 않고 재사용한다.</para>
    /// </summary>
    public class RewardFlyFx : MonoBehaviour
    {
        private const float FlySeconds = 0.55f;
        private const float ArcHeight = 140f;      // 포물선 높이(픽셀) — 직선으로 가면 '이동'이 잘 안 읽힌다
        private const float StartScale = 1f;
        private const float EndScale = 0.45f;      // 도착할수록 작아져 HUD로 '빨려 들어가는' 느낌
        private const float OrbSize = 64f;
        private const float TargetPunchScale = 1.25f;
        private const float TargetPunchSeconds = 0.14f;

        private static RewardFlyFx _instance;

        /// <summary>
        /// 보상이 날아갈 HUD 목표를 돌려주는 공급자(골드·경험치·전리품 모두 같은 목표 = 가방).
        /// <para><b>왜 델리게이트인가</b>: 어셈블리 참조가 <c>UI → Battle</c> 한 방향이라 이 코드(Battle)가
        /// HUD(UI)를 직접 알 수 없다. 그래서 HUD가 자기 목표 제공 함수를 여기 등록하고,
        /// 전투 쪽은 "누가 주는지 모르는 목표"만 받아 쓴다(순환 참조 없음).</para>
        /// </summary>
        public static System.Func<RectTransform> TargetProvider;

        /// <summary>등록된 공급자로 목표를 찾는다(공급자가 없으면 연출을 생략하도록 null).</summary>
        public static RectTransform ResolveTarget()
        {
            return TargetProvider != null ? TargetProvider() : null;
        }

        private Canvas _canvas;
        private RectTransform _root;
        private readonly List<Image> _pool = new List<Image>();

        /// <summary>목표별 도착 팝 상태 — <b>원래 크기</b>와 진행 중인 코루틴(겹칠 때 이전 것을 버린다).</summary>
        private sealed class PunchState
        {
            public Vector3 BaseScale;
            public Coroutine Routine;
        }

        private readonly Dictionary<RectTransform, PunchState> _punches =
            new Dictionary<RectTransform, PunchState>();

        /// <summary>
        /// 보상 오브 하나를 날린다. <paramref name="fromScreen"/>은 시작 화면 좌표(보상 칸 위치),
        /// <paramref name="target"/>은 도착할 HUD 요소, <paramref name="delay"/>는 여러 개를 시차로 보낼 때 쓴다.
        /// 목표가 없으면(HUD가 없는 씬 등) 아무것도 하지 않는다.
        /// </summary>
        public static void Fly(Sprite icon, Color tint, Vector2 fromScreen, RectTransform target, float delay = 0f)
        {
            if (target == null)
            {
                return;
            }
            EnsureInstance();
            _instance.StartCoroutine(_instance.FlyRoutine(icon, tint, fromScreen, target, delay));
        }

        private static void EnsureInstance()
        {
            if (_instance != null)
            {
                return;
            }
            var go = new GameObject("RewardFlyFx", typeof(Canvas), typeof(CanvasScaler));
            _instance = go.AddComponent<RewardFlyFx>();
            _instance._canvas = go.GetComponent<Canvas>();
            _instance._canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _instance._canvas.sortingOrder = UiSortingOrder.Topmost; // 클리어 오버레이 위로 지나간다
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; // 화면 픽셀 좌표와 1:1
            scaler.scaleFactor = 1f;
            _instance._root = (RectTransform)go.transform;
        }

        private void OnDisable()
        {
            // 연출 도중 이 오브젝트가 꺼지면 코루틴이 멈춰 목표가 커진 채 남는다 → 즉시 원래 크기로.
            RestoreAllPunchTargets();
        }

        private void OnDestroy()
        {
            RestoreAllPunchTargets(); // 씬 전환으로 파괴될 때도 HUD 크기를 되돌린다
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private IEnumerator FlyRoutine(Sprite icon, Color tint, Vector2 fromScreen, RectTransform target, float delay)
        {
            if (delay > 0f)
            {
                // 클리어 연출은 슬로우모션 중일 수 있으므로 실시간 대기(§2.2와 같은 기준).
                yield return new WaitForSecondsRealtime(delay);
            }
            if (target == null)
            {
                yield break;
            }

            var orb = GetFreeOrb();
            orb.sprite = icon != null ? icon : BattleFxTextures.SoftEllipse();
            orb.color = tint;
            var rt = orb.rectTransform;
            rt.anchoredPosition = fromScreen;
            rt.localScale = Vector3.one * StartScale;
            orb.gameObject.SetActive(true);

            // 시작점과 도착점 사이 위쪽으로 휘는 제어점(2차 베지어).
            Vector2 to = ToScreen(target);
            Vector2 control = (fromScreen + to) * 0.5f + Vector2.up * ArcHeight;

            float t = 0f;
            while (t < FlySeconds)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / FlySeconds);
                float ease = k * k; // 갈수록 빨라진다(빨려 들어가는 느낌)
                // 목표가 움직일 수 있으므로(메뉴 접힘·해상도 변경) 매 프레임 다시 읽는다.
                to = target != null ? ToScreen(target) : to;
                rt.anchoredPosition = Bezier(fromScreen, control, to, ease);
                rt.localScale = Vector3.one * Mathf.Lerp(StartScale, EndScale, ease);
                yield return null;
            }

            orb.gameObject.SetActive(false); // 풀로 반환
            SoundManager.Sfx(SoundId.RewardGet); // 도착 = 획득음(§5.1 공용 획득음)
            PunchTarget(target);
        }

        /// <summary>
        /// 도착 지점(HUD 요소)을 한 번 크게 튀겼다 되돌린다 — "여기로 들어갔다"를 읽히게 한다.
        /// <para><b>이미 진행 중인 팝이 있으면 그것을 버리고 원래 크기에서 다시 시작한다.</b> 보상은 여러 개가
        /// <b>시차로</b> 같은 목표(가방)에 도착하므로 팝이 겹치는데, 겹칠 때 <c>target.localScale</c>을 기준으로
        /// 새로 잡으면 이전 팝이 키워 둔 값(1.25배)이 기준이 되어 배율이 곱해지고, 마지막 팝이 그 커진 값으로
        /// 되돌리는 탓에 <b>가방 아이콘이 커진 채 고정</b>됐다(2026-08-05 수정). 목표별 원래 크기를
        /// <see cref="_punches"/>에 들고 있어 몇 번 겹쳐도 항상 원래 크기로 돌아온다.</para>
        /// </summary>
        private void PunchTarget(RectTransform target)
        {
            if (target == null)
            {
                return;
            }
            if (!_punches.TryGetValue(target, out var state) || state == null)
            {
                state = new PunchState { BaseScale = target.localScale }; // 첫 팝 = 지금 크기가 원래 크기
                _punches[target] = state;
            }
            else if (state.Routine != null)
            {
                StopCoroutine(state.Routine); // 진행 중이던 팝을 버리고
                state.Routine = null;
                target.localScale = state.BaseScale; // 원래 크기에서 다시 시작(배율 누적 방지)
            }
            state.Routine = StartCoroutine(PunchRoutine(target, state));
        }

        private IEnumerator PunchRoutine(RectTransform target, PunchState state)
        {
            float t = 0f;
            while (t < TargetPunchSeconds && target != null)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / TargetPunchSeconds);
                target.localScale = state.BaseScale * Mathf.Lerp(TargetPunchScale, 1f, k);
                yield return null;
            }
            if (target != null)
            {
                target.localScale = state.BaseScale;
            }
            state.Routine = null;
            _punches.Remove(target); // 끝났으면 추적 해제(다음 팝은 다시 현재 = 원래 크기를 기준으로 잡는다)
        }

        /// <summary>추적 중인 모든 목표를 원래 크기로 되돌리고 팝 추적을 비운다(비활성·파괴 시 안전망).</summary>
        private void RestoreAllPunchTargets()
        {
            foreach (var pair in _punches)
            {
                if (pair.Key != null && pair.Value != null)
                {
                    pair.Key.localScale = pair.Value.BaseScale;
                }
            }
            _punches.Clear();
        }

        /// <summary>오버레이 캔버스 기준 화면 좌표(캔버스가 ConstantPixelSize·배율 1이라 픽셀과 같다).</summary>
        private static Vector2 ToScreen(RectTransform rt)
        {
            Vector3 sp = RectTransformUtility.WorldToScreenPoint(null, rt.position);
            return new Vector2(sp.x, sp.y);
        }

        private static Vector2 Bezier(Vector2 a, Vector2 control, Vector2 b, float k)
        {
            float inv = 1f - k;
            return inv * inv * a + 2f * inv * k * control + k * k * b;
        }

        /// <summary>비활성 오브를 꺼내거나 새로 만든다(풀).</summary>
        private Image GetFreeOrb()
        {
            for (int i = 0; i < _pool.Count; i++)
            {
                if (_pool[i] != null && !_pool[i].gameObject.activeSelf)
                {
                    return _pool[i];
                }
            }
            var go = new GameObject("RewardOrb", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_root, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = Vector2.zero; // 좌하단 기준 = 화면 픽셀 좌표
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(OrbSize, OrbSize);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            img.preserveAspect = true;
            go.SetActive(false);
            _pool.Add(img);
            return img;
        }
    }
}
