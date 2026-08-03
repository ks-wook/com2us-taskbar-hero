using System.Collections.Generic;
using UnityEngine;

namespace TaskbarHero.Client.Battle
{
    /// <summary>
    /// 데미지 숫자(<see cref="DamageNumber"/>)를 재사용하는 오브젝트 풀. 매 피격마다 생성/파괴하지 않고
    /// 비활성 오브젝트를 꺼내 쓰고 애니 종료 시 되돌려받아 GC/인스턴스화 비용을 줄인다.
    /// 씬에 미리 배치하지 않아도 첫 사용 시 <see cref="GetOrCreate"/>로 자동 생성되는 지연 싱글턴이다.
    /// </summary>
    public class DamageNumberPool : MonoBehaviour
    {
        [SerializeField] private int prewarmCount = 16;
        [SerializeField] private int textFontSize = 64;
        [SerializeField] private float characterSize = 0.06f;
        [SerializeField] private int sortingOrder = 1000;

        public static DamageNumberPool Instance { get; private set; }

        private readonly Queue<DamageNumber> _free = new Queue<DamageNumber>();
        private Font _font;

        /// <summary>싱글턴이 없으면 생성해 반환한다.</summary>
        public static DamageNumberPool GetOrCreate()
        {
            if (Instance == null)
            {
                var go = new GameObject("DamageNumberPool");
                go.AddComponent<DamageNumberPool>();
            }
            return Instance;
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
        /// <paramref name="crit"/>이면 치명타 연출(노란색·큰 팝·<c>!</c>)로 표시한다.</summary>
        public void Spawn(long damage, Vector3 worldPos, bool crit = false)
        {
            var dn = _free.Count > 0 ? _free.Dequeue() : CreateNumber();
            dn.Play(damage, worldPos, crit);
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
            tm.fontSize = textFontSize;
            tm.characterSize = characterSize;
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
