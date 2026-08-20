using System.Collections.Generic;
using UnityEngine;
using TaskbarHero.Client.Managers;

namespace TaskbarHero.Client.Battle
{
    /// <summary>
    /// 데미지 숫자(<see cref="DamageNumber"/>)를 재사용하는 오브젝트 풀. 매 피격마다 생성/파괴하지 않고
    /// 비활성 오브젝트를 꺼내 쓰고 애니 종료 시 되돌려받아 GC/인스턴스화 비용을 줄인다.
    /// 씬에 미리 배치하지 않아도 첫 사용 시 <see cref="GetOrCreate"/>로 자동 생성되는 지연 싱글턴이다.
    /// <para><b>폰트는 호출부가 넘긴다</b>(<see cref="SetFont"/>) — 이 풀은 런타임에 스스로 생기는 오브젝트라
    /// 인스펙터 배선을 받을 수 없으므로, 씬에 배선된 <see cref="BattleDevController"/>가 들고 있는 폰트를
    /// 첫 호출 때 넘겨받는다. 넘기지 않으면 빌트인 기본 폰트를 쓴다.</para>
    /// <para><b>선명도는 래스터 크기를 화면에 맞추는 것으로 잡는다</b>(<see cref="ApplyRasterForScreen"/>).
    /// 동적 폰트는 <c>fontSize</c>로 요청한 크기로 아틀라스에 글리프를 굽고, TextMesh는 그것을
    /// <c>characterSize / 10</c> 배로 월드에 붙인다(실측: fontSize 64 · characterSize 0.09 → 줄 높이 0.576월드).
    /// 구운 크기와 화면에 그려지는 크기가 다르면 확대·축소 필터링이 들어가 흐려지므로, 카메라 배율에서
    /// <b>화면 픽셀 크기를 계산해 그 크기로 다시 굽고</b> 월드 크기는 그대로 유지한다. 여기에
    /// <see cref="PixelFontAtlas"/>로 아틀라스 필터를 Point로 고정해 픽셀 폰트의 획이 뭉개지지 않게 한다.</para>
    /// </summary>
    public class DamageNumberPool : MonoBehaviour
    {
        [SerializeField] private int prewarmCount = 16;
        [SerializeField] private int textFontSize = 64;
        [SerializeField] private float characterSize = 0.06f;
        [SerializeField] private int sortingOrder = 1000;

        /// <summary>TextMesh의 월드 크기 = 래스터 픽셀 × <c>characterSize</c> / 10 (실측 확인).</summary>
        private const float RasterPixelToWorld = 0.1f;
        /// <summary>표시 중 최대 배율. <see cref="DamageNumber"/>가 팝(1.35)·홀드 성장(1.05)까지 키우므로
        /// 그 크기에서 선명해야 한다 — 래스터를 이 배율 기준으로 굽는다(작게 굽고 늘리면 흐려진다).</summary>
        private const float MaxDisplayScale = 1.5f;
        private const int MinRaster = 16;
        private const int MaxRaster = 160;   // 아틀라스가 커지는 것을 막는 상한

        public static DamageNumberPool Instance { get; private set; }

        private readonly Queue<DamageNumber> _free = new Queue<DamageNumber>();
        private Font _font;
        private float _sizeScale = 1f;
        private float _lineWorld;      // 유지할 월드 줄 높이(인스펙터 기준값에서 구한다)
        private int _raster;           // 현재 굽는 크기(fontSize)
        private float _charSize;       // 현재 characterSize(_lineWorld를 지키는 값)
        private int _screenH;          // 마지막으로 반영한 화면 높이
        private float _orthoSize;      // 마지막으로 반영한 카메라 크기

        /// <summary>싱글턴이 없으면 생성해 반환한다.
        /// <paramref name="font"/>를 주면 데미지 숫자 폰트를 그것으로 바꾼다(<paramref name="sizeScale"/>는 글자 크기 보정).</summary>
        public static DamageNumberPool GetOrCreate(Font font = null, float sizeScale = 1f)
        {
            if (Instance == null)
            {
                var go = new GameObject("DamageNumberPool");
                go.AddComponent<DamageNumberPool>();
            }
            Instance.SetFont(font, sizeScale);
            Instance.ApplyRasterForScreen();
            return Instance;
        }

        /// <summary>데미지 숫자 폰트를 바꾼다 — <b>이미 만들어 둔 오브젝트(대기·표시 중)에도 즉시 적용</b>한다.
        /// <para><paramref name="sizeScale"/>는 글리프 크기 보정이다. 폰트마다 em 대비 숫자 높이가 달라
        /// 같은 <c>characterSize</c>로 두면 크기가 확 바뀐다(픽셀 폰트는 기본 폰트의 0.66배 — 실측 em 100에서
        /// 48px 대 73px). 같은 값이 다시 들어오면 아무것도 하지 않는다.</para></summary>
        public void SetFont(Font font, float sizeScale = 1f)
        {
            if (font == null || (_font == font && Mathf.Approximately(_sizeScale, sizeScale)))
            {
                return;
            }
            _font = font;
            _sizeScale = sizeScale > 0f ? sizeScale : 1f;
            _lineWorld = textFontSize * characterSize * _sizeScale * RasterPixelToWorld;
            _raster = 0;   // 새 폰트 기준으로 다시 계산하게 한다
            PixelFontAtlas.KeepCrisp(_font);

            foreach (Transform child in transform)
            {
                var tm = child.GetComponent<TextMesh>();
                if (tm == null)
                {
                    continue;
                }
                tm.font = _font;
                var mr = child.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    mr.material = _font.material;
                }
            }
            ApplyRasterForScreen();
        }

        /// <summary>화면 배율(카메라 크기·화면 높이)에서 <b>화면에 그려질 픽셀 크기</b>를 구해 그 크기로 글리프를
        /// 굽게 한다 — 월드 크기(<c>_lineWorld</c>)는 그대로 두고 <c>fontSize</c>와 <c>characterSize</c>를 함께 바꾼다.
        /// 창 크기나 카메라 배율이 바뀌지 않았으면 아무것도 하지 않는다.</summary>
        private void ApplyRasterForScreen()
        {
            var cam = Camera.main;
            float ortho = cam != null && cam.orthographic ? cam.orthographicSize : 0f;
            int screenH = Screen.height;
            if (_raster > 0 && screenH == _screenH && Mathf.Approximately(ortho, _orthoSize))
            {
                return;
            }
            _screenH = screenH;
            _orthoSize = ortho;

            // 직교 카메라: 화면 높이(px) / 보이는 월드 높이 = 월드 1당 픽셀 수.
            float pixelsPerWorld = ortho > 0f ? screenH / (2f * ortho) : 100f;
            int raster = Mathf.Clamp(Mathf.RoundToInt(_lineWorld * pixelsPerWorld * MaxDisplayScale), MinRaster, MaxRaster);
            if (raster == _raster)
            {
                return;
            }
            _raster = raster;
            _charSize = _lineWorld / (raster * RasterPixelToWorld);   // 월드 크기 보존

            foreach (Transform child in transform)
            {
                var tm = child.GetComponent<TextMesh>();
                if (tm != null)
                {
                    tm.fontSize = _raster;
                    tm.characterSize = _charSize;
                }
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _lineWorld = textFontSize * characterSize * RasterPixelToWorld;
            _raster = textFontSize;
            _charSize = characterSize;
            for (int i = 0; i < prewarmCount; i++)
            {
                _free.Enqueue(CreateNumber());
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>지정 월드 위치에 데미지 숫자를 띄운다(풀에서 재사용, 부족하면 새로 생성).
        /// <paramref name="crit"/>이면 치명타 연출(노란색·큰 팝·<c>!</c>)로 표시한다.
        /// <paramref name="sizeMul"/>은 피해 비중에 따른 크기 배수, <paramref name="delay"/>는 광역 시차다.</summary>
        public void Spawn(long damage, Vector3 worldPos, bool crit = false, float sizeMul = 1f, float delay = 0f)
        {
            ApplyRasterForScreen();   // 창 크기·카메라 배율이 바뀌면 그 크기로 다시 굽는다
            var dn = _free.Count > 0 ? _free.Dequeue() : CreateNumber();
            dn.Play(damage, worldPos, crit, sizeMul, delay);
        }

        /// <summary>애니가 끝난 숫자를 풀로 되돌린다.</summary>
        public void Release(DamageNumber dn)
        {
            if (dn != null)
            {
                _free.Enqueue(dn);
            }
        }

        /// <summary>TextMesh 기반 데미지 숫자 오브젝트를 1개 만든다(비활성 상태).</summary>
        private DamageNumber CreateNumber()
        {
            var go = new GameObject("DamageNumber", typeof(TextMesh), typeof(DamageNumber));
            go.transform.SetParent(transform, false);

            var tm = go.GetComponent<TextMesh>();
            tm.font = _font;
            tm.fontSize = _raster > 0 ? _raster : textFontSize;
            tm.characterSize = _charSize > 0f ? _charSize : characterSize;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            // 볼드를 쓰지 않는다 — 합성 볼드는 글리프를 밀어 겹쳐 그려 획이 뭉개지고 너무 두꺼워 보인다.
            tm.fontStyle = FontStyle.Normal;
            tm.color = Color.white; // 실제 색은 DamageNumber가 매 프레임 지정(일반 흰색 / 치명타 노란색)

            var mr = go.GetComponent<MeshRenderer>();
            mr.material = _font.material;         // 빌트인 폰트 머티리얼
            mr.sortingOrder = sortingOrder;       // 스프라이트 위에 표시

            var dn = go.GetComponent<DamageNumber>();
            dn.Init(this);
            go.SetActive(false);
            return dn;
        }
    }
}
