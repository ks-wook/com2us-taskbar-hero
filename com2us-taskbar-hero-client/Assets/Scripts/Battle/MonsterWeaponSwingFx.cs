using UnityEngine;

namespace TaskbarHero.Client.Battle
{
    /// <summary>
    /// 몬스터가 무기를 휘두를 때만 나타나는 스윙 연출 — <b>무기 끝 궤적 + 흩어지는 스파크 입자</b>.
    /// <para>아군의 <see cref="WeaponAfterimage"/>와 달리 <b>상시 표현이 없다</b>: 몬스터 무기에는
    /// 강화 단계가 없으므로 무기 틴트도, 항상 타오르는 불꽃도 두지 않는다. 가만히 서 있는 몬스터가
    /// 빛나면 아군의 강화 이펙트와 뒤섞여 "이 무기가 강한가?"로 읽히기 때문이다. 그래서 이 연출은
    /// 오직 <b>때리는 순간</b>에만 켜져 공격 타이밍을 알리는 신호로 쓰인다.</para>
    /// <para>색은 아군 강화 팔레트(흰빛→붉은→보라)와 겹치지 않도록 <b>탁한 잿빛에서 주황빛</b>으로
    /// 잡았다 — 강화 이펙트가 아니라 쇠붙이가 스치며 튀는 불티에 가깝게 보이게 한다.</para>
    /// <para>무기 렌더러가 없는 몬스터(맨몸 슬라임 등)에는 아무것도 붙지 않는다
    /// (<see cref="Create"/>가 null을 돌려준다).</para>
    /// </summary>
    public class MonsterWeaponSwingFx : MonoBehaviour
    {
        // 궤적: 아군 근접(0.22초·0.17)보다 짧고 가늘게 — 화면에 몬스터가 여럿이라 굵으면 지저분해진다.
        private const float TrailSeconds = 0.18f;
        private const float TrailWidth = 0.13f;
        private const int SortingOrder = 95;      // 캐릭터(5) 위, 스킬 이펙트(100) 아래 — 아군 잔상과 같은 층

        // 스파크: 휘두르는 프레임에만 방출한다(멈추면 rate = 0).
        private const float SparkRate = 90f;      // 방출량(개/초)
        private const float SparkSizeMin = 0.05f;
        private const float SparkSizeMax = 0.11f;
        private const float SparkLifeMin = 0.12f;
        private const float SparkLifeMax = 0.26f;
        private const float SparkSpeedMin = 0.6f; // 무기 끝에서 사방으로 튄다
        private const float SparkSpeedMax = 1.8f;
        private const float SparkGravity = 0.55f; // 살짝 떨어지며 사그라진다(불티 느낌)
        private const int SparkMaxParticles = 120;

        // 기준색 하나에서 네 가지 색을 파생하는 계수. 궤적 앞쪽은 기준색을 흰 쪽으로 밀어 밝게,
        // 뒤쪽은 어둡게·탁하게 떨어뜨려 "달아오른 날 → 식은 잔상"이 되게 한다.
        private const float HotLift = 0.45f;     // 기준색을 흰색 쪽으로 미는 정도(궤적 앞쪽)
        private const float DeepDrop = 0.42f;    // 기준색을 검정 쪽으로 떨어뜨리는 정도(궤적 뒤쪽)
        private const float DeepDesat = 0.35f;   // 뒤쪽의 채도를 빼는 정도(잿빛으로)
        private const float SparkDimDrop = 0.30f; // 불티 어두운 쪽

        private TrailRenderer _trail;
        private ParticleSystem _spark;
        private System.Func<bool> _isSwinging;
        private bool _emitting;
        private Color _base = MonsterSwingFxPalette.DefaultColor;

        /// <summary>
        /// 몬스터의 무기 끝에 스윙 연출을 붙여 돌려준다(무기 렌더러가 없으면 null).
        /// 몬스터 자식으로 붙으므로 몬스터가 파괴될 때 함께 정리된다.
        /// </summary>
        /// <param name="ownerRoot">몬스터 루트(이 아래에서 무기 렌더러를 찾는다)</param>
        /// <param name="isSwinging">이 프레임에 무기를 휘두르는 중인지 — 궤적·스파크 방출 조건</param>
        /// <param name="baseColor">궤적·불티의 기준색(생략하면 기본 불티색). 지역별 보스 색이 여기로 들어온다.</param>
        public static MonsterWeaponSwingFx Create(
            Transform ownerRoot, System.Func<bool> isSwinging, Color? baseColor = null)
        {
            var weapon = WeaponAfterimage.FindWeapon(ownerRoot);
            if (weapon == null)
            {
                return null;
            }

            var go = new GameObject("MonsterWeaponSwingFx");
            go.transform.SetParent(weapon.transform, false);
            // 끝점 계산은 아군과 같은 규칙을 쓴다(몬스터 무기도 피벗이 손잡이 쪽인 SPUM 파트다).
            go.transform.localPosition = WeaponAfterimage.TipLocalPosition(weapon, WeaponTrailStyle.Melee);
            go.transform.localRotation = Quaternion.identity;

            var fx = go.AddComponent<MonsterWeaponSwingFx>();
            fx._base = baseColor ?? MonsterSwingFxPalette.DefaultColor;
            fx.Build(isSwinging);
            return fx;
        }

        /// <summary>기준색에서 궤적 앞/뒤·불티 밝은/어두운 네 색을 만든다.</summary>
        private void DeriveColors(out Color trailHot, out Color trailDeep,
                                  out Color sparkBright, out Color sparkDim)
        {
            trailHot = Color.Lerp(_base, Color.white, HotLift);
            var dark = Color.Lerp(_base, Color.black, DeepDrop);
            float gray = dark.grayscale;
            trailDeep = Color.Lerp(dark, new Color(gray, gray, gray), DeepDesat);
            sparkBright = _base;
            sparkDim = Color.Lerp(_base, Color.black, SparkDimDrop);
        }

        /// <summary>궤적·스파크를 만들고 방출을 꺼 둔 상태로 대기시킨다(휘두를 때만 켜진다).</summary>
        private void Build(System.Func<bool> isSwinging)
        {
            _isSwinging = isSwinging;

            _trail = gameObject.AddComponent<TrailRenderer>();
            _trail.time = TrailSeconds;
            _trail.minVertexDistance = 0.015f;
            _trail.autodestruct = false;
            _trail.emitting = false;
            _trail.alignment = LineAlignment.View;
            _trail.textureMode = LineTextureMode.Stretch;
            _trail.numCapVertices = 2;
            _trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _trail.receiveShadows = false;
            _trail.sortingOrder = SortingOrder;
            _trail.widthCurve = new AnimationCurve(new Keyframe(0f, TrailWidth), new Keyframe(1f, 0f));
            _trail.material = WeaponAfterimage.UnlitSpriteMaterial();
            DeriveColors(out Color trailHot, out Color trailDeep, out Color sparkBright, out Color sparkDim);
            _trail.colorGradient = new Gradient
            {
                colorKeys = new[]
                {
                    new GradientColorKey(trailHot, 0f),
                    new GradientColorKey(trailDeep, 1f),
                },
                alphaKeys = new[]
                {
                    new GradientAlphaKey(0.8f, 0f),
                    new GradientAlphaKey(0.45f, 0.45f),
                    new GradientAlphaKey(0f, 1f),
                },
            };

            BuildSpark(sparkBright, sparkDim);
        }

        /// <summary>
        /// 무기 끝에서 튀는 불티 입자를 만든다. 방출면은 무기 실루엣 전체가 아니라 <b>끝점 한 곳</b>이다 —
        /// 상시가 아니라 스윙 순간에만 나오는 연출이라, 궤적이 지나간 자리에서 튀어야 스윙으로 읽힌다.
        /// </summary>
        private void BuildSpark(Color bright, Color dim)
        {
            var go = new GameObject("SwingSpark");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;

            _spark = go.AddComponent<ParticleSystem>();
            var main = _spark.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World; // 튄 자리에 남아 사그라진다
            main.startSpeed = new ParticleSystem.MinMaxCurve(SparkSpeedMin, SparkSpeedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(SparkSizeMin, SparkSizeMax);
            main.startLifetime = new ParticleSystem.MinMaxCurve(SparkLifeMin, SparkLifeMax);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(bright.r, bright.g, bright.b, 0.95f),
                new Color(dim.r, dim.g, dim.b, 0.9f));
            main.gravityModifier = SparkGravity;
            main.playOnAwake = false;
            main.loop = true;
            main.maxParticles = SparkMaxParticles;

            var shape = _spark.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.04f; // 끝점 주변에서 사방으로

            var size = _spark.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(1f, 0f)));

            var col = _spark.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(new Gradient
            {
                colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                alphaKeys = new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.7f, 0.5f),
                    new GradientAlphaKey(0f, 1f),
                },
            });

            var emission = _spark.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f; // 스윙 중에만 켠다

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = WeaponAfterimage.GlowParticleMaterial();
            renderer.sortingOrder = SortingOrder + 1; // 궤적보다 위
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            _spark.Play();
        }

        /// <summary>
        /// 남아 있는 궤적·입자를 즉시 지운다. <b>풀에서 재사용된 몬스터</b>가 다른 자리에서 다시 등장할 때
        /// 이전 스윙의 궤적이 두 위치를 잇는 선으로 순간 보이는 것을 막는다(궤적은 월드 좌표에 남는다).
        /// </summary>
        public void ResetEmission()
        {
            _emitting = false;
            if (_trail != null)
            {
                _trail.emitting = false;
                _trail.Clear();
            }
            if (_spark != null)
            {
                var emission = _spark.emission;
                emission.rateOverTime = 0f;
                _spark.Clear();
            }
        }

        /// <summary>휘두르는 프레임에만 궤적·스파크를 방출한다(전진·피격 중에는 꺼진다).</summary>
        private void LateUpdate()
        {
            bool swinging = _isSwinging != null && _isSwinging();
            if (swinging == _emitting)
            {
                return;
            }
            _emitting = swinging;

            if (_trail != null)
            {
                _trail.emitting = swinging;
            }
            if (_spark != null)
            {
                var emission = _spark.emission;
                emission.rateOverTime = swinging ? SparkRate : 0f;
            }
        }
    }
}
