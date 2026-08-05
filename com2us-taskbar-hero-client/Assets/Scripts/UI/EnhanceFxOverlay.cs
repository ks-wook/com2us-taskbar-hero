using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TaskbarHero.Client.Managers;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 장비 강화 연출(인벤토리/아이템/큐브 기획서 §5.3 · 사운드 리소스 정의서 §6). <b>작업 → 완성</b> 2단 구성이다:
    /// <list type="number">
    /// <item>요청 시작과 함께 <b>망치질</b>(<c>Assets/Art/Effect/UI/EquipEnhanceHammer</c> + <see cref="SoundId.EnhanceHammer"/>).
    /// 응답을 기다리지 않고 먼저 시작한다 — 강화는 실패·하락이 없어(확정 상승) 소리를 먼저 내도 결과와 어긋나지 않고,
    /// 응답 왕복 지연 동안 조작이 무반응으로 느껴지는 것을 막는다.</item>
    /// <item>성공 응답이 오면 <b>망치질이 끝나는 시점에</b> 성공 버스트
    /// (<c>Assets/Art/Effect/UI/EnhanceSuccessBurst</c> + <see cref="SoundId.EnhanceSuccess"/>)를 터뜨리고 콜백을 호출한다.
    /// 응답이 망치질보다 빨리 오는 것이 보통이므로 즉시 내지 않고 기다린다(두 소리가 겹치면 타격감이 사라진다).</item>
    /// </list>
    /// <para><b>왜 독립 캔버스 싱글턴인가</b>: 연출 중에 패널이 닫히거나 목록이 다시 그려져도(강화 성공 →
    /// 타일 재생성) 이펙트가 함께 사라지면 안 된다. 그래서 <see cref="Battle.RewardFlyFx"/>와 같은 방식으로
    /// 자기 캔버스를 가진 오브젝트를 두고, 호출측은 화면 좌표만 넘긴다.</para>
    /// </summary>
    public class EnhanceFxOverlay : MonoBehaviour
    {
        // 망치질: 프레임 수(30)와 연출 길이(2.0초)를 맞춘다 — 망치질이 끝나는 순간 성공음이 이어지도록.
        private const float HammerFps = 15f;
        private const float HammerSize = 260f;

        // 망치 이펙트는 10프레임 주기로 <b>3번</b> 내려찍는다. 프레임을 실측해 얻은 타격(스파크 폭발) 프레임이며
        // (1-based), 각 타격마다 sfx_enhance_hammer를 1번씩 재생해 소리도 3타가 되게 한다.
        private static readonly int[] HammerStrikeFrames = { 6, 16, 26 };

        // sfx_enhance_hammer는 파일 앞에 <b>약 0.18초의 무음</b>이 있고 그 뒤에 타격이 터진다(실측).
        // 그래서 타격 프레임에 맞춰 재생하면 소리가 그만큼 늦게 들리므로, 이 시간만큼 미리 재생을 시작한다.
        private const float HammerSoundLeadIn = 0.18f;

        // 이펙트가 배선되지 않았을 때(에셋 미빌드) 소리만으로 3타를 유지하기 위한 타격 간격(10프레임 주기).
        private const float HammerStrikeInterval = 10f / HammerFps;
        // 성공 버스트: 짧고 강하게(30프레임 / 24fps ≈ 1.25초).
        private const float BurstFps = 24f;
        private const float BurstSize = 320f;
        // 실패 시 망치질을 즉시 끊는다(사운드 정의서 §9 — sfx_ui_error가 망치음을 덮는다).
        private static EnhanceFxOverlay _instance;

        private RectTransform _root;
        private Image _hammer;
        private Image _burst;
        private Coroutine _routine;
        private bool _hammerDone;      // 망치질(연출+소리)이 끝났는지 — 성공 버스트 시작 조건
        private bool _cancelled;       // 실패 응답으로 중단됐는지

        /// <summary>강화 요청을 보내는 순간 망치질을 시작한다(화면 좌표 기준). 망치음은 이펙트가 내려찍는
        /// <b>3번의 타격에 각각 맞춰</b> 재생된다(<see cref="HammerStrikeFrames"/>).
        /// 이전 연출이 남아 있으면 끊고 새로 시작한다 — 강화는 연타할 수 있어 망치음이 겹치면 타격 수가 뭉갠다
        /// (사운드 정의서 §9).</summary>
        public static void PlayHammer(Sprite[] hammerFrames, Vector2 screenPos)
        {
            var fx = Ensure();
            fx.StopRoutine();
            fx._cancelled = false;
            fx._hammerDone = false;
            SoundManager.StopAllSfx(); // 이전 강화음 정지
            fx._routine = fx.StartCoroutine(fx.HammerRoutine(hammerFrames, screenPos));
        }

        /// <summary>성공 응답 처리 — 망치질이 끝나는 시점에 버스트를 터뜨리고 <paramref name="onBurst"/>를 호출한다.
        /// (강화 단계 텍스트 갱신을 이 콜백에서 하면 소리·이펙트·숫자가 같은 순간에 바뀐다.)
        /// 이미 실패로 중단된 연출이면 아무것도 하지 않는다.</summary>
        public static void PlaySuccess(Sprite[] burstFrames, Vector2 screenPos, Action onBurst)
        {
            var fx = Ensure();
            if (fx._cancelled)
            {
                onBurst?.Invoke();
                return;
            }
            fx.StartCoroutine(fx.SuccessRoutine(burstFrames, screenPos, onBurst));
        }

        /// <summary>실패 응답 — 망치질을 즉시 끊는다(호출측이 sfx_ui_error를 낸다).</summary>
        public static void Cancel()
        {
            if (_instance == null)
            {
                return;
            }
            _instance._cancelled = true;
            _instance.StopRoutine();
            SoundManager.StopAllSfx();
            _instance.HideAll();
        }

        /// <summary>연출용 오브젝트(자기 캔버스 + 망치·버스트 이미지)를 1회 만든다.</summary>
        private static EnhanceFxOverlay Ensure()
        {
            if (_instance != null)
            {
                return _instance;
            }
            var go = new GameObject("EnhanceFxOverlay", typeof(Canvas), typeof(CanvasScaler));
            _instance = go.AddComponent<EnhanceFxOverlay>();
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = UiSortingOrder.Topmost; // 큐브 패널 위에서 보인다
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; // 화면 픽셀 좌표와 1:1
            scaler.scaleFactor = 1f;
            _instance._root = (RectTransform)go.transform;
            _instance._hammer = _instance.NewLayer("Hammer", HammerSize);
            _instance._burst = _instance.NewLayer("Burst", BurstSize);
            return _instance;
        }

        /// <summary>이펙트 프레임을 그리는 이미지 한 장(좌하단 기준 = 화면 픽셀 좌표, 기본 숨김).</summary>
        private Image NewLayer(string name, float size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_root, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            img.preserveAspect = true;
            go.SetActive(false);
            return img;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        /// <summary>망치질 프레임을 1회 재생하고(슬로우모션과 무관한 unscaled 시간) 끝나면 <see cref="_hammerDone"/>을 세운다.
        /// 재생 중 타격 프레임에 맞춰 망치음을 3번 낸다.</summary>
        private IEnumerator HammerRoutine(Sprite[] frames, Vector2 screenPos)
        {
            if (frames == null || frames.Length == 0)
            {
                // 이펙트가 배선되지 않았어도 소리는 3타를 유지하고, 성공 버스트 타이밍도 흐르게 둔다.
                yield return SoundOnlyStrikes();
                _hammerDone = true;
                yield break;
            }

            _hammer.rectTransform.anchoredPosition = screenPos;
            yield return PlaySequence(_hammer, frames, HammerFps, PlayStrikeSoundAt);
            _hammerDone = true;
        }

        /// <summary>이 프레임이 타격음을 낼 차례면 망치음을 재생한다(프레임 인덱스는 0-based).
        /// 파일 앞 무음(<see cref="HammerSoundLeadIn"/>)만큼 앞당겨 트리거해 소리의 타격이 스파크와 같은 순간에 오게 한다.</summary>
        private static void PlayStrikeSoundAt(int frameIndex)
        {
            int lead = Mathf.RoundToInt(HammerSoundLeadIn * HammerFps);
            foreach (int strike in HammerStrikeFrames)
            {
                if (frameIndex == Mathf.Max(0, strike - 1 - lead))
                {
                    SoundManager.Sfx(SoundId.EnhanceHammer);
                    return;
                }
            }
        }

        /// <summary>[폴백] 이펙트 프레임이 없을 때 망치음만 3번 재생한다(간격은 이펙트 주기와 동일).</summary>
        private IEnumerator SoundOnlyStrikes()
        {
            for (int i = 0; i < HammerStrikeFrames.Length; i++)
            {
                if (_cancelled)
                {
                    yield break;
                }
                SoundManager.Sfx(SoundId.EnhanceHammer);
                yield return new WaitForSecondsRealtime(HammerStrikeInterval);
            }
        }

        /// <summary>망치질이 끝날 때까지 기다린 뒤 성공 버스트를 재생한다(성공음은 버스트 시작에 맞춰 낸다).</summary>
        private IEnumerator SuccessRoutine(Sprite[] frames, Vector2 screenPos, Action onBurst)
        {
            while (!_hammerDone && !_cancelled)
            {
                yield return null;
            }
            if (_cancelled)
            {
                onBurst?.Invoke();
                yield break;
            }

            SoundManager.Sfx(SoundId.EnhanceSuccess);
            onBurst?.Invoke(); // 강화 단계 표시 갱신을 소리·이펙트와 같은 순간에
            if (frames != null && frames.Length > 0)
            {
                _burst.rectTransform.anchoredPosition = screenPos;
                yield return PlaySequence(_burst, frames, BurstFps, null);
            }
        }

        /// <summary>이미지 한 장에 프레임 배열을 1회 재생한다(재생이 끝나면 감춘다).
        /// <paramref name="onFrame"/>이 있으면 각 프레임이 시작될 때 그 인덱스(0-based)로 호출한다 — 망치 타격음처럼
        /// 특정 프레임에 소리를 맞출 때 쓴다.</summary>
        private IEnumerator PlaySequence(Image target, Sprite[] frames, float fps, Action<int> onFrame)
        {
            target.sprite = frames[0];
            target.gameObject.SetActive(true);
            float frameDuration = 1f / Mathf.Max(1f, fps);
            for (int i = 0; i < frames.Length; i++)
            {
                if (_cancelled)
                {
                    break;
                }
                target.sprite = frames[i];
                onFrame?.Invoke(i);
                float t = 0f;
                while (t < frameDuration)
                {
                    t += Time.unscaledDeltaTime;
                    yield return null;
                }
            }
            target.gameObject.SetActive(false);
        }

        /// <summary>진행 중인 망치질 코루틴을 중단한다(연타·실패 시).</summary>
        private void StopRoutine()
        {
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }
        }

        /// <summary>두 이펙트 레이어를 모두 감춘다.</summary>
        private void HideAll()
        {
            if (_hammer != null) _hammer.gameObject.SetActive(false);
            if (_burst != null) _burst.gameObject.SetActive(false);
        }
    }
}
