using UnityEngine;

namespace TaskbarHero.Client.Battle
{
    /// <summary>
    /// 전투 연출용 <b>절차적 생성 스프라이트</b> 모음(부드러운 타원 · 비네트).
    /// <para>아트로 만들지 않고 코드로 굽는 이유: 둘 다 <b>반경에 따른 알파 그라디언트</b>일 뿐이라
    /// 아트 파일·임포트 설정·에디터 배선이 필요 없고, 텍스처가 없어 연출이 조용히 빠지는 일도 없다.
    /// 크기도 작아(64²·96²) 메모리 비용이 사실상 없다.</para>
    /// <para>한 번 만들면 <b>정적으로 캐시</b>해 모든 전투가 공유한다(몬스터마다 굽지 않는다).</para>
    /// </summary>
    public static class BattleFxTextures
    {
        private static Sprite _softEllipse;
        private static Sprite _vignette;

        /// <summary>가운데가 진하고 가장자리로 갈수록 투명해지는 원형 스프라이트(보스 예고 마커용).</summary>
        public static Sprite SoftEllipse()
        {
            if (_softEllipse != null)
            {
                return _softEllipse;
            }
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "FxSoftEllipse"
            };
            var pixels = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    // 중심 1 → 경계 0. 가장자리를 부드럽게(제곱) 떨어뜨려 원의 테두리가 딱 잘리지 않게 한다.
                    float a = Mathf.Clamp01(1f - r);
                    a *= a;
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            _softEllipse = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
            _softEllipse.name = "FxSoftEllipse"; // 인스펙터·로그에서 알아보기 위한 이름
            return _softEllipse;
        }

        /// <summary>가운데가 투명하고 <b>가장자리로 갈수록 진해지는</b> 비네트 스프라이트(보스 페이즈 전환 플래시용).</summary>
        public static Sprite Vignette()
        {
            if (_vignette != null)
            {
                return _vignette;
            }
            const int size = 96;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "FxVignette"
            };
            var pixels = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float r = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy));
                    // 화면 중앙(캐릭터가 있는 곳)은 가리지 않고 테두리만 물들인다.
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, r));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            _vignette = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
            _vignette.name = "FxVignette";
            return _vignette;
        }
    }
}
