using UnityEngine;

namespace TaskbarHero.Client.Battle
{
    /// <summary>
    /// 근접 무기 캐릭터(기사·슬레이어)의 무기 끝에 상시 붙어, 무기를 휘두를 때 궤적에 잔상을 남긴다.
    /// 평소에는 <b>노란색</b>, 무기 강화 버프(기사의 분노·광전사의 힘)가 켜져 있는 동안은
    /// <b>붉은색</b> 잔상 + 무기 끝 붉은 발광점으로 바뀐다(<see cref="SetBuffed"/>).
    ///
    /// 잔상은 <see cref="TrailRenderer"/>로 그린다 — 무기 끝 트랜스폼이 월드에서 이동한 만큼만 궤적이
    /// 생기므로 자세를 유지하는 동안에는 잔상이 남지 않는다. 다만 캐릭터가 전진할 때도 무기 끝이
    /// 함께 움직이므로, 걷는 동안 궤적이 끌리지 않도록 <c>isSwinging</c> 콜백이 true인 프레임에만
    /// 방출한다(공격 모션 재생 중).
    /// </summary>
    public class WeaponAfterimage : MonoBehaviour
    {
        private const float TrailSeconds = 0.22f;     // 잔상이 남아있는 시간
        private const float TrailWidth = 0.17f;       // 잔상 시작 두께(유닛)
        private const float GlowSize = 0.26f;         // 무기 끝 발광점 크기(유닛)
        private const int SortingOrder = 95;          // 캐릭터(5) 위, 스킬 이펙트(100) 아래

        // 평소(노랑): 앞쪽이 밝고 뒤로 갈수록 짙은 금색
        private static readonly Color HotYellow = new Color(1f, 0.95f, 0.62f);
        private static readonly Color DeepYellow = new Color(0.85f, 0.58f, 0.10f);
        // 버프(붉음): 광전사의 힘·기사의 분노 지속 동안
        private static readonly Color HotRed = new Color(1f, 0.42f, 0.30f);
        private static readonly Color DeepRed = new Color(0.72f, 0.06f, 0.08f);

        private static Sprite _glowSprite;

        private TrailRenderer _trail;
        private SpriteRenderer _glow;
        private System.Func<bool> _isSwinging;
        private bool _buffed;

        /// <summary>
        /// 캐릭터의 무기 끝에 잔상 이펙트를 붙여 돌려준다(무기 렌더러가 없으면 null).
        /// 캐릭터 자식으로 붙으므로 캐릭터가 파괴될 때 함께 정리된다 — 스폰 시 1회 생성하면 된다.
        /// </summary>
        /// <param name="ownerRoot">캐릭터 루트(이 아래에서 무기 렌더러를 찾는다)</param>
        /// <param name="isSwinging">이 프레임에 무기를 휘두르는 중인지(공격 모션 재생 중) — 잔상 방출 조건</param>
        public static WeaponAfterimage Create(Transform ownerRoot, System.Func<bool> isSwinging)
        {
            var weapon = FindWeapon(ownerRoot);
            if (weapon == null)
            {
                return null;
            }

            var go = new GameObject("WeaponAfterimage");
            go.transform.SetParent(weapon.transform, false);
            go.transform.localPosition = TipLocalPosition(weapon);
            go.transform.localRotation = Quaternion.identity;

            var fx = go.AddComponent<WeaponAfterimage>();
            fx.Build(isSwinging);
            return fx;
        }

        /// <summary>
        /// 무기 강화 버프 상태를 전환한다. true면 붉은 잔상 + 무기 끝 붉은 발광점,
        /// false면 평소의 노란 잔상(발광점 없음).
        /// </summary>
        public void SetBuffed(bool buffed)
        {
            _buffed = buffed;
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
        /// 무기 스프라이트의 '끝' 로컬 좌표. SPUM 무기는 피벗이 손잡이 쪽에 있고 날이 +Y로 뻗으므로
        /// 로컬 바운즈에서 원점(피벗)에서 가장 먼 축 끝을 끝점으로 삼는다(무기 종류가 바뀌어도 따라간다).
        /// </summary>
        private static Vector3 TipLocalPosition(SpriteRenderer weapon)
        {
            var b = weapon.sprite.bounds;
            float x = Mathf.Abs(b.max.x) >= Mathf.Abs(b.min.x) ? b.max.x : b.min.x;
            float y = Mathf.Abs(b.max.y) >= Mathf.Abs(b.min.y) ? b.max.y : b.min.y;
            // 축 중 더 멀리 뻗은 쪽만 사용(도끼/검은 대개 길이축이 Y)
            if (Mathf.Abs(y) >= Mathf.Abs(x))
            {
                return new Vector3(0f, y * 0.9f, 0f);
            }
            return new Vector3(x * 0.9f, 0f, 0f);
        }

        private void Build(System.Func<bool> isSwinging)
        {
            _isSwinging = isSwinging;

            _trail = gameObject.AddComponent<TrailRenderer>();
            _trail.time = TrailSeconds;
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
                new Keyframe(0f, TrailWidth), new Keyframe(1f, 0f));
            _trail.material = UnlitSpriteMaterial();

            // 무기 끝 발광점(버프 지속 동안만 표시)
            var glowGo = new GameObject("TipGlow");
            glowGo.transform.SetParent(transform, false);
            glowGo.transform.localPosition = Vector3.zero;
            _glow = glowGo.AddComponent<SpriteRenderer>();
            _glow.sprite = GlowSprite();
            _glow.sortingOrder = SortingOrder + 1;
            _glow.material = UnlitSpriteMaterial();
            glowGo.transform.localScale = Vector3.one * GlowSize;

            ApplyPalette(); // 기본은 평소(노랑) + 발광점 숨김
        }

        /// <summary>현재 상태(평소/버프)에 맞는 잔상 색과 무기 끝 발광점 노출을 적용한다.</summary>
        private void ApplyPalette()
        {
            Color hot = _buffed ? HotRed : HotYellow;
            Color deep = _buffed ? DeepRed : DeepYellow;

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
            if (_glow != null)
            {
                // 무기 끝 붉은 효과는 버프 지속 동안만 추가된다.
                _glow.enabled = _buffed;
                _glow.color = new Color(hot.r, hot.g, hot.b, 0.9f);
            }
        }

        private void LateUpdate()
        {
            if (_trail != null)
            {
                // 걷는 동안 궤적이 끌리지 않도록 공격 모션 중에만 방출한다.
                _trail.emitting = _isSwinging == null || _isSwinging();
            }
            if (_glow != null)
            {
                // 발광점이 살짝 맥동해 '기운이 서린' 느낌을 준다.
                float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 9f);
                _glow.transform.localScale = Vector3.one * (GlowSize * pulse);
            }
        }

        /// <summary>2D 조명에 영향받지 않는 스프라이트 머티리얼(잔상/발광점 공용).</summary>
        private static Material UnlitSpriteMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }
            return new Material(shader);
        }

        /// <summary>가장자리로 갈수록 투명해지는 작은 원형 발광 스프라이트를 1회 생성해 재사용한다.</summary>
        private static Sprite GlowSprite()
        {
            if (_glowSprite != null)
            {
                return _glowSprite;
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
            _glowSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            return _glowSprite;
        }
    }
}
