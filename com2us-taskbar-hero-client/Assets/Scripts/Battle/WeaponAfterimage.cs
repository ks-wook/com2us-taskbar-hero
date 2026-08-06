using UnityEngine;

namespace TaskbarHero.Client.Battle
{
    /// <summary>
    /// 무기 잔상의 <b>형태</b>(굵기·지속·궤적이 나가는 지점). 색은 이 값이 아니라
    /// <b>장비 강화 단계</b>가 정한다(<see cref="WeaponAfterimage.SetEnhanceLevel"/>).
    /// <para>근접 무기는 크게 호를 그리며 휘둘러 굵은 잔상이 어울리지만, 활·지팡이는 앞으로 내미는
    /// 동작이라 궤적이 짧고 거의 직선이어서 같은 굵기를 쓰면 <b>막대 얼룩</b>처럼 보인다. 그래서
    /// 원거리 무기는 잔상을 절반 두께·짧은 지속으로 그려 날렵한 선이 되게 한다.</para>
    /// </summary>
    public enum WeaponTrailStyle
    {
        /// <summary>검·도끼 등 근접 무기(기사·슬레이어) — 굵은 궤적, 무기 끝에서 방출.</summary>
        Melee = 0,
        /// <summary>활(궁수) — 가는 궤적, 활 정가운데(그립)에서 방출.</summary>
        Bow = 1,
        /// <summary>지팡이 등 마력 무기(마법사) — 가는 궤적, 무기 끝에서 방출.</summary>
        Staff = 2,
    }

    /// <summary>
    /// 캐릭터의 무기 끝에 상시 붙어, 무기를 휘두를 때 궤적에 잔상을 남긴다.
    /// <para><b>색과 반짝임은 오직 장착 무기의 강화 단계(0~10)만 따른다</b>(<see cref="SetEnhanceLevel"/>) —
    /// 미강화는 흰빛~회색, 단계가 오를수록 붉게, +8부터 보랏빛, +10은 어두운 보라에 암흑이 섞인다.
    /// 일정 단계 이상에서는 무기 궤적을 따라 반짝이는 입자가 함께 흩어진다.
    /// 자버프 스킬(기사의 분노·광전사의 힘)은 이 이펙트를 바꾸지 않는다 — 버프 표현은 스킬 자체의
    /// 발동 이펙트가 맡는다.</para>
    ///
    /// <para>연출은 세 겹이다.</para>
    /// <list type="bullet">
    /// <item><b>궤적(잔상)</b> — <see cref="TrailRenderer"/>. 무기 끝 트랜스폼이 월드에서 이동한 만큼만
    /// 그려지므로 <b>휘두르는 동안에만</b> 보인다. 캐릭터가 전진할 때도 무기 끝이 함께 움직이므로,
    /// 걷는 동안 궤적이 끌리지 않도록 <c>isSwinging</c>이 true인 프레임에만 방출한다.</item>
    /// <item><b>무기 색 틴트</b> — 무기 스프라이트를 그대로 덧입혀 무기 자체를 물들인다. <b>상시</b>이며 천천히 맥동한다.</item>
    /// <item><b>불꽃 입자</b> — <b>상시</b>. 무기 <b>스프라이트 실루엣 전체</b>에서 솟아 위로 타오른다
    /// (<see cref="ParticleSystemShapeType.SpriteRenderer"/>). 휘두르는 동안에는 방출량이 늘어 더 거세진다.</item>
    /// </list>
    /// </summary>
    public class WeaponAfterimage : MonoBehaviour
    {
        private const float TrailSeconds = 0.22f;     // 근접: 잔상이 남아있는 시간
        private const float TrailWidth = 0.17f;       // 근접: 잔상 시작 두께(유닛)
        private const float SlimTrailSeconds = 0.15f; // 원거리(활·지팡이): 짧게 남긴다
        private const float SlimTrailWidth = 0.085f;  // 원거리(활·지팡이): 절반 두께
        private const int SortingOrder = 95;          // 캐릭터(5) 위, 스킬 이펙트(100) 아래

        /// <summary>강화 단계 상한(enhance_master = +10 확정).</summary>
        public const int MaxEnhanceLevel = 10;

        // ── 강화 단계별 색 앵커 ──
        // 각 단계의 색은 아래 앵커 사이를 보간해 만든다(EnhanceColors). hot = 궤적 앞쪽(밝은 쪽),
        // deep = 뒤로 흐려지는 쪽. 앵커만 고치면 중간 단계가 함께 따라온다.
        private static readonly Color HotPlain = new Color(0.97f, 0.97f, 1f);     // +0 흰빛
        private static readonly Color DeepPlain = new Color(0.55f, 0.57f, 0.62f); // +0 회색
        private static readonly Color HotCrimson = new Color(1f, 0.45f, 0.32f);   // +7 붉은색
        private static readonly Color DeepCrimson = new Color(0.72f, 0.07f, 0.08f);
        private static readonly Color HotMagenta = new Color(0.98f, 0.45f, 0.85f); // +8 보랏빛으로 넘어가는 지점
        private static readonly Color DeepMagenta = new Color(0.55f, 0.08f, 0.45f);
        private static readonly Color HotVoid = new Color(0.62f, 0.28f, 0.92f);   // +10 어두운 보라
        private static readonly Color DeepVoid = new Color(0.10f, 0.01f, 0.16f);  // +10 암흑

        // ── 불꽃 입자 ──
        // 무기 전체가 타오르는 표현이라, 입자는 무기 스프라이트 실루엣 전역에서 솟아 위로 올라간다.
        // <b>불처럼 보이려면 입자가 서로 겹쳐 하나의 불길 덩어리로 뭉쳐야 한다</b> — 성기게 뿌리면
        // 타오르는 게 아니라 먼지가 휘날리는 것으로 보인다. 그래서 방출량을 크게 잡고, 대신
        // 상승 속도와 좌우 흔들림은 낮춰(무기 곁에 머무르게) 수명을 짧게 가져간다.
        private const int FlameMinLevel = 3;          // 이 단계부터 불이 붙는다(+0~2는 없음)
        private const float FlameRiseMin = 0.22f;     // 위로 떠오르는 속도(유닛/초) — 느릴수록 무기에 붙어 탄다
        private const float FlameRiseMax = 0.5f;
        private const float FlameDriftX = 0.09f;      // 좌우로 일렁이는 폭(넓으면 흩날리는 먼지가 된다)
        private const float SwingFlameBoost = 1.5f;   // 휘두르는 동안 방출량 배수(불이 거세진다)
        private const int FlameMaxParticles = 700;    // 동시 입자 상한(가장 촘촘한 +10 스윙 기준 여유)

        // ── 무기 색 틴트(상시) ──
        // 강화하지 않은 무기(+0)에는 아무것도 두지 않는다 — 모든 무기가 상시 빛나면 강화 여부가 드러나지 않는다.
        private const float TintPulseSpeed = 3.2f;   // 맥동 속도(느리게 — 빠르면 깜빡임으로 보인다)
        private const float TintPulseDepth = 0.12f;  // 맥동 폭(±비율)

        private static Texture2D _glowTexture;

        private TrailRenderer _trail;
        private ParticleSystem _flame;
        private SpriteRenderer _weaponTint;  // 무기 스프라이트를 덧입힌 색 틴트(상시)
        private SpriteRenderer _weapon;      // 원본 무기 렌더러(틴트·불꽃 방출 형태가 이걸 따라간다)
        private System.Func<bool> _isSwinging;
        private int _enhanceLevel;
        private WeaponTrailStyle _style = WeaponTrailStyle.Melee;

        private float _flameRate;     // 평상시 방출량(휘두를 때는 SwingFlameBoost를 곱한다)
        private float _appliedRate = -1f;
        private float _tintAlpha;     // 현재 단계의 틴트 알파(0 = 이펙트 없음)

        /// <summary>
        /// 캐릭터의 무기 끝에 잔상 이펙트를 붙여 돌려준다(무기 렌더러가 없으면 null).
        /// 캐릭터 자식으로 붙으므로 캐릭터가 파괴될 때 함께 정리된다 — 스폰 시 1회 생성하면 된다.
        /// </summary>
        /// <param name="ownerRoot">캐릭터 루트(이 아래에서 무기 렌더러를 찾는다)</param>
        /// <param name="isSwinging">이 프레임에 무기를 휘두르는 중인지(공격 모션 재생 중) — 잔상 방출 조건</param>
        /// <param name="style">잔상 형태(무기 종류에 맞춰 지정) — 색은 강화 단계가 정한다</param>
        public static WeaponAfterimage Create(
            Transform ownerRoot, System.Func<bool> isSwinging,
            WeaponTrailStyle style = WeaponTrailStyle.Melee)
        {
            var weapon = FindWeapon(ownerRoot);
            if (weapon == null)
            {
                return null;
            }

            var go = new GameObject("WeaponAfterimage");
            go.transform.SetParent(weapon.transform, false);
            go.transform.localPosition = TipLocalPosition(weapon, style);
            go.transform.localRotation = Quaternion.identity;

            var fx = go.AddComponent<WeaponAfterimage>();
            fx._style = style;
            fx._weapon = weapon;
            fx.Build(isSwinging);
            return fx;
        }

        /// <summary>
        /// 장착 무기의 강화 단계(0~<see cref="MaxEnhanceLevel"/>)를 반영한다. 잔상 색과 반짝임 입자의
        /// 세기·색이 함께 바뀐다. 장비를 바꾸거나 강화했을 때 다시 부르면 즉시 반영된다.
        /// </summary>
        public void SetEnhanceLevel(int level)
        {
            int clamped = Mathf.Clamp(level, 0, MaxEnhanceLevel);
            if (clamped == _enhanceLevel)
            {
                return;
            }
            _enhanceLevel = clamped;
            ApplyPalette();
        }

        /// <summary>캐릭터에서 무기 스프라이트 렌더러를 찾는다(오른손 무기 우선).</summary>
        private static SpriteRenderer FindWeapon(Transform ownerRoot)
        {
            SpriteRenderer fallback = null;
            foreach (var sr in ownerRoot.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sr.sprite == null)
                {
                    continue;
                }
                if (sr.gameObject.name == "R_Weapon")
                {
                    return sr;
                }
                if (fallback == null && sr.gameObject.name.ToLower().Contains("weapon"))
                {
                    fallback = sr;
                }
            }
            return fallback;
        }

        /// <summary>
        /// 잔상이 나가는 무기 위 지점의 로컬 좌표.
        /// <para><b>검·도끼·지팡이</b>는 피벗이 손잡이 쪽이고 날이 +Y로 뻗으므로, 로컬 바운즈에서
        /// 원점(피벗)에서 가장 먼 축 끝을 '끝점'으로 삼는다(무기 종류가 바뀌어도 따라간다).</para>
        /// <para><b>활</b>은 위아래 림이 대칭이라 어느 쪽 끝을 잡아도 손에서 멀리 떨어진 림에 궤적이
        /// 생겨 몸통과 겹친 얼룩처럼 보인다. 그래서 활은 끝이 아니라 <b>스프라이트 세로 중앙
        /// (= 시위를 잡는 그립)</b>에서 궤적이 나가게 한다.</para>
        /// </summary>
        private static Vector3 TipLocalPosition(SpriteRenderer weapon, WeaponTrailStyle style)
        {
            var b = weapon.sprite.bounds;
            if (style == WeaponTrailStyle.Bow)
            {
                return new Vector3(0f, b.center.y, 0f);
            }

            float x = Mathf.Abs(b.max.x) >= Mathf.Abs(b.min.x) ? b.max.x : b.min.x;
            float y = Mathf.Abs(b.max.y) >= Mathf.Abs(b.min.y) ? b.max.y : b.min.y;
            // 축 중 더 멀리 뻗은 쪽만 사용(검·도끼·지팡이 모두 대개 길이축이 Y)
            if (Mathf.Abs(y) >= Mathf.Abs(x))
            {
                return new Vector3(0f, y * 0.9f, 0f);
            }
            return new Vector3(x * 0.9f, 0f, 0f);
        }

        private void Build(System.Func<bool> isSwinging)
        {
            _isSwinging = isSwinging;

            // 활·지팡이는 앞으로 내미는 짧은 직선 궤적이라, 근접용 두께를 그대로 쓰면 막대처럼 보인다.
            bool slim = _style != WeaponTrailStyle.Melee;

            _trail = gameObject.AddComponent<TrailRenderer>();
            _trail.time = slim ? SlimTrailSeconds : TrailSeconds;
            _trail.minVertexDistance = 0.015f;
            _trail.autodestruct = false;
            _trail.emitting = false;                 // 휘두를 때만 방출
            _trail.alignment = LineAlignment.View;
            _trail.textureMode = LineTextureMode.Stretch;
            _trail.numCapVertices = 2;
            _trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _trail.receiveShadows = false;
            _trail.sortingOrder = SortingOrder;
            _trail.widthCurve = new AnimationCurve(
                new Keyframe(0f, slim ? SlimTrailWidth : TrailWidth), new Keyframe(1f, 0f));
            _trail.material = UnlitSpriteMaterial();

            BuildTint();
            BuildFlame();

            ApplyPalette(); // 기본은 +0(흰빛~회색) + 불꽃·틴트 없음
        }

        /// <summary>
        /// 무기 스프라이트를 <b>같은 크기·같은 자리</b>에 덧입혀 무기 자체를 물들이는 색 틴트를 만든다.
        /// <para>확대하지 않고 원본과 정확히 겹친다 — 픽셀아트를 1.x배로 키우면 픽셀 그리드가
        /// 어긋나 테두리가 지저분해진다.</para>
        /// </summary>
        private void BuildTint()
        {
            if (_weapon == null)
            {
                return;
            }

            // 무기와 완전히 겹쳐야 하므로 무기 트랜스폼의 자식으로 둔다
            // (이 컴포넌트 자신은 '무기 끝'으로 옮겨져 있어 기준이 다르다).
            var tintGo = new GameObject("WeaponEnhanceTint");
            tintGo.transform.SetParent(_weapon.transform, false);
            _weaponTint = tintGo.AddComponent<SpriteRenderer>();
            _weaponTint.sprite = _weapon.sprite;
            _weaponTint.sortingLayerID = _weapon.sortingLayerID;
            _weaponTint.sortingOrder = _weapon.sortingOrder + 1; // 무기 바로 위
            _weaponTint.material = UnlitSpriteMaterial();
            _weaponTint.enabled = false;
        }

        /// <summary>
        /// 무기가 타오르는 불꽃 입자를 만든다(방출량·색은 <see cref="ApplyPalette"/>가 정한다).
        /// <para>핵심은 방출 형태다 — <see cref="ParticleSystemShapeType.SpriteRenderer"/>로 <b>무기
        /// 스프라이트의 실루엣 전체</b>를 방출면으로 삼아, 무기 끝 한 점이 아니라 날·자루 전역에서 불이 솟게 한다.
        /// 무기가 회전하거나 스프라이트가 바뀌어도 방출면이 자동으로 따라간다.</para>
        /// <para>입자는 <see cref="ParticleSystemSimulationSpace.World"/>에서 <b>위로</b> 떠오르고
        /// (<see cref="FlameRiseMin"/>~<see cref="FlameRiseMax"/>) 좌우로 일렁이며, 수명 동안 작아지고
        /// 투명해진다 — 아래로 떨어지면 불이 아니라 흘러내리는 가루로 보인다.</para>
        /// </summary>
        private void BuildFlame()
        {
            var go = new GameObject("WeaponFlame");
            // 방출면이 무기 스프라이트이므로 무기 트랜스폼 기준에 둔다.
            go.transform.SetParent(_weapon != null ? _weapon.transform : transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;

            _flame = go.AddComponent<ParticleSystem>();
            var main = _flame.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World; // 무기를 따라다니지 않고 그 자리에 남아 타오른다
            main.startSpeed = 0f;                                       // 이동은 velocityOverLifetime이 맡는다
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 0f;
            main.playOnAwake = false;
            main.loop = true;
            main.maxParticles = FlameMaxParticles;

            var shape = _flame.shape;
            if (_weapon != null)
            {
                shape.shapeType = ParticleSystemShapeType.SpriteRenderer;
                shape.spriteRenderer = _weapon;
                shape.meshShapeType = ParticleSystemMeshShapeType.Triangle; // 실루엣 면 전체에서 균일하게
            }
            else
            {
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.05f;
            }

            // 위로 타오르며 좌우로 일렁인다.
            // ⚠️ x·y·z 세 축의 MinMaxCurve 모드가 <b>모두 같아야</b> 한다 — 하나라도 다르면 Unity가
            //    매 프레임 "Particle Velocity curves must all be in the same mode" 오류를 뱉는다.
            //    쓰지 않는 z도 같은 TwoConstants 모드로 0을 넣어 둔다(기본값은 Constant라 모드가 어긋난다).
            var vel = _flame.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.y = new ParticleSystem.MinMaxCurve(FlameRiseMin, FlameRiseMax);
            vel.x = new ParticleSystem.MinMaxCurve(-FlameDriftX, FlameDriftX);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            // 뿌리에서 가장 굵고 위로 갈수록 가늘어진다(불꽃 혀의 모양). 처음부터 큼직해야 서로 겹쳐
            // 불길로 뭉친다 — 작게 시작해 커지면 반짝이는 입자처럼 보인다.
            var size = _flame.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(0.55f, 0.72f), new Keyframe(1f, 0f)));

            // 붙는 순간부터 진하게 보이고, 끝에서만 사그라진다(페이드인이 길면 불이 흐릿해진다).
            var col = _flame.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(new Gradient
            {
                colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                alphaKeys = new[]
                {
                    new GradientAlphaKey(0.85f, 0f),
                    new GradientAlphaKey(1f, 0.25f),
                    new GradientAlphaKey(0f, 1f),
                },
            });

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = GlowParticleMaterial();
            renderer.sortingOrder = SortingOrder + 1; // 잔상보다 위
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            _flame.Play();
        }

        /// <summary>현재 강화 단계에 맞는 잔상 색·불꽃 입자·무기 틴트를 적용한다
        /// (다른 어떤 상태도 이 색에 관여하지 않는다).</summary>
        private void ApplyPalette()
        {
            EnhanceColors(_enhanceLevel, out Color hot, out Color deep);

            ApplyFlame(hot, deep);
            ApplyTint(hot);

            if (_trail != null)
            {
                _trail.colorGradient = new Gradient
                {
                    colorKeys = new[]
                    {
                        new GradientColorKey(hot, 0f),
                        new GradientColorKey(deep, 1f),
                    },
                    alphaKeys = new[]
                    {
                        new GradientAlphaKey(0.85f, 0f),
                        new GradientAlphaKey(0.55f, 0.45f),
                        new GradientAlphaKey(0f, 1f),
                    },
                };
            }
        }

        /// <summary>
        /// 강화 단계(0~10)에 대응하는 잔상 색을 돌려준다. 앵커(+0 흰빛~회색 → +7 붉은색 → +8 보랏빛 →
        /// +10 어두운 보라·암흑) 사이를 보간하므로 중간 단계는 자연히 이어진다.
        /// </summary>
        private static void EnhanceColors(int level, out Color hot, out Color deep)
        {
            if (level <= 0)
            {
                hot = HotPlain;
                deep = DeepPlain;
            }
            else if (level <= 7)
            {
                float t = level / 7f;                       // +1~+6은 회색에서 붉은색으로 서서히
                hot = Color.Lerp(HotPlain, HotCrimson, t);
                deep = Color.Lerp(DeepPlain, DeepCrimson, t);
            }
            else if (level <= 8)
            {
                hot = HotMagenta;                           // +8에서 보랏빛으로 넘어간다
                deep = DeepMagenta;
            }
            else
            {
                float t = (level - 8) / 2f;                 // +9는 중간, +10에서 어두운 보라·암흑
                hot = Color.Lerp(HotMagenta, HotVoid, t);
                deep = Color.Lerp(DeepMagenta, DeepVoid, t);
            }
        }

        /// <summary>
        /// 강화 단계별 불꽃의 세기를 정한다(<see cref="FlameMinLevel"/> 미만은 불이 붙지 않는다).
        /// 방출면이 무기 실루엣 전체라 낮은 방출량으로도 무기를 감싸므로, 단계가 오를수록 촘촘하고
        /// 오래 타오르게만 키운다. 파티가 최대 3인이고 몬스터도 여럿인 화면이라 상한을 두고 늘린다.
        /// </summary>
        private static void FlameTuning(int level, out float rate, out float sizeMin, out float sizeMax,
            out float lifetime)
        {
            if (level >= MaxEnhanceLevel)      // +10: 무기가 온통 타오른다
            {
                rate = 260f; sizeMin = 0.10f; sizeMax = 0.22f; lifetime = 0.35f;
            }
            else if (level >= 8)               // +8~9: 보랏빛 불길
            {
                rate = 180f; sizeMin = 0.095f; sizeMax = 0.19f; lifetime = 0.32f;
            }
            else if (level >= 6)               // +6~7: 중간
            {
                rate = 120f; sizeMin = 0.085f; sizeMax = 0.16f; lifetime = 0.30f;
            }
            else if (level >= FlameMinLevel)   // +3~5: 무기 표면에 잔불이 이는 정도
            {
                rate = 70f; sizeMin = 0.075f; sizeMax = 0.14f; lifetime = 0.26f;
            }
            else                               // +0~2: 없음
            {
                rate = 0f; sizeMin = 0f; sizeMax = 0f; lifetime = 0f;
            }
        }

        /// <summary>
        /// 강화 단계별 무기 색 틴트의 진하기를 정한다. +0은 0이라 미강화 무기는 물들지 않는다.
        /// </summary>
        private static float TintAlphaFor(int level)
        {
            if (level >= MaxEnhanceLevel) return 0.34f;  // +10
            if (level >= 7) return 0.26f;                // +7~9
            if (level >= 4) return 0.18f;                // +4~6
            if (level >= 1) return 0.10f;                // +1~3: 강화했다는 것만 은은히 알린다
            return 0f;                                   // +0
        }

        /// <summary>현재 강화 단계·색에 맞춰 무기 색 틴트를 갱신한다.</summary>
        private void ApplyTint(Color hot)
        {
            _tintAlpha = TintAlphaFor(_enhanceLevel);
            if (_weaponTint != null)
            {
                _weaponTint.enabled = _tintAlpha > 0f;
                _weaponTint.color = new Color(hot.r, hot.g, hot.b, _tintAlpha);
            }
        }

        /// <summary>현재 강화 단계·색에 맞춰 불꽃 입자의 방출량·크기·수명·색을 갱신한다.</summary>
        private void ApplyFlame(Color hot, Color deep)
        {
            if (_flame == null)
            {
                return;
            }

            FlameTuning(_enhanceLevel, out float rate, out float sizeMin, out float sizeMax, out float lifetime);
            bool on = rate > 0f;

            var main = _flame.main;
            if (on)
            {
                main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.6f, lifetime);
                main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
                // 밝은 쪽(hot)과 짙은 쪽(deep) 사이에서 무작위로 뽑아 불길에 명암을 준다 — +10은
                // deep이 거의 검정이라 보랏빛 불꽃에 검은 혀가 섞여 '암흑이 타오르는' 느낌이 난다.
                main.startColor = new ParticleSystem.MinMaxGradient(
                    new Color(hot.r, hot.g, hot.b, 0.95f), new Color(deep.r, deep.g, deep.b, 0.9f));
            }

            _flameRate = rate;
            _appliedRate = -1f; // 다음 LateUpdate에서 방출량을 다시 밀어 넣게 한다

            var emission = _flame.emission;
            emission.enabled = on;
        }

        private void LateUpdate()
        {
            bool swinging = _isSwinging == null || _isSwinging();
            if (_trail != null)
            {
                // 걷는 동안 궤적이 끌리지 않도록 공격 모션 중에만 방출한다.
                _trail.emitting = swinging;
            }
            if (_flame != null && _flameRate > 0f)
            {
                // 불은 상시 타오르고, 휘두르는 동안에만 더 거세진다.
                float target = swinging ? _flameRate * SwingFlameBoost : _flameRate;
                if (!Mathf.Approximately(target, _appliedRate))
                {
                    var emission = _flame.emission;
                    emission.rateOverTime = target;
                    _appliedRate = target;
                }
            }

            UpdateTint();
        }

        /// <summary>
        /// 무기 색 틴트를 매 프레임 유지한다 — 무기 스프라이트(교체·좌우 반전)를 따라가게 하고,
        /// 알파를 천천히 맥동시켜 불빛에 일렁이는 느낌을 준다.
        /// </summary>
        private void UpdateTint()
        {
            if (_tintAlpha <= 0f)
            {
                return; // +0: 틴트 없음
            }

            float pulse = 1f + TintPulseDepth * Mathf.Sin(Time.time * TintPulseSpeed);

            if (_weaponTint != null && _weapon != null)
            {
                // SPUM 애니메이션이 무기 스프라이트를 바꾸거나 뒤집을 수 있으므로 매 프레임 맞춘다.
                if (_weaponTint.sprite != _weapon.sprite)
                {
                    _weaponTint.sprite = _weapon.sprite;
                }
                _weaponTint.flipX = _weapon.flipX;
                _weaponTint.flipY = _weapon.flipY;
                // 무기가 숨겨지는 모션에서는 틴트도 함께 사라져야 한다.
                _weaponTint.enabled = _weapon.enabled && _weapon.sprite != null;

                var c = _weaponTint.color;
                _weaponTint.color = new Color(c.r, c.g, c.b, Mathf.Clamp01(_tintAlpha * pulse));
            }
        }

        /// <summary>2D 조명에 영향받지 않는 스프라이트 머티리얼(잔상용).</summary>
        private static Material UnlitSpriteMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }
            return new Material(shader);
        }

        /// <summary>
        /// 불꽃 입자용 머티리얼. 알파 블렌드라 밝은 불길과 <b>어두운 혀(+10의 암흑)</b>가 모두 표현된다
        /// (가산 블렌드는 어둡게 만들 수 없어 쓰지 않는다). 입자 색은 파티클 시스템이 정점 색으로 넘긴다.
        /// </summary>
        private static Material GlowParticleMaterial()
        {
            var mat = new Material(Shader.Find("Sprites/Default"))
            {
                mainTexture = GlowTexture(),
            };
            return mat;
        }

        /// <summary>중심이 진하고 가장자리가 투명한 원형 텍스처(불꽃 입자용). 1회 생성해 재사용한다.</summary>
        private static Texture2D GlowTexture()
        {
            if (_glowTexture != null)
            {
                return _glowTexture;
            }

            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            float c = (size - 1) * 0.5f;
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                    float a = Mathf.Clamp01(1f - d);
                    px[y * size + x] = new Color(1f, 1f, 1f, a * a); // 중심이 진한 부드러운 원
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            _glowTexture = tex;
            return _glowTexture;
        }
    }
}
