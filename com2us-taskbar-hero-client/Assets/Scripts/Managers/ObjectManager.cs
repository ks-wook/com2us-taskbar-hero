using System.Collections.Generic;
using UnityEngine;

namespace TaskbarHero.Client.Managers
{
    /// <summary>
    /// 게임 오브젝트의 생성/추적/파괴를 담당하는 범용 매니저. 다른 매니저들과 마찬가지로
    /// <b>씬이 바뀌어도 유지되는 DontDestroyOnLoad 싱글턴</b>이다.
    ///
    /// 카테고리(문자열) 단위로 스폰한 오브젝트를 등록·조회·정리한다. 무엇을·언제·어떻게 생성할지는
    /// 각 게임 로직(예: 개발용 <c>BattleDevController</c>)이 결정하고, 이 매니저는 인스턴스화와
    /// 수명(추적/파괴)만 관리한다 — 특정 게임플레이 타입에 의존하지 않아 씬/모드에 걸쳐 재사용된다.
    /// </summary>
    public class ObjectManager : MonoBehaviour
    {
        public static ObjectManager Instance { get; private set; }

        private readonly Dictionary<string, List<GameObject>> _categories = new Dictionary<string, List<GameObject>>();

        /// <summary>씬이 바뀌어도 유지되는 인스턴스를 보장한다(없으면 생성). 부트스트랩용.</summary>
        public static ObjectManager EnsureInstance()
        {
            if (Instance == null)
            {
                var go = new GameObject("ObjectManager");
                Instance = go.AddComponent<ObjectManager>();
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
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>카테고리 목록을 얻는다(없으면 생성).</summary>
        private List<GameObject> Category(string category)
        {
            if (!_categories.TryGetValue(category, out var list))
            {
                list = new List<GameObject>();
                _categories[category] = list;
            }
            return list;
        }

        /// <summary>프리팹을 생성해 카테고리에 등록하고 반환한다.</summary>
        public GameObject Spawn(string category, GameObject prefab, Vector3 position, Quaternion rotation, Transform parent = null)
        {
            if (prefab == null) return null;
            var go = Instantiate(prefab, position, rotation, parent);
            Category(category).Add(go);
            return go;
        }

        /// <summary>이미 만든 오브젝트를 카테고리에 등록한다.</summary>
        public void Register(string category, GameObject go)
        {
            if (go != null) Category(category).Add(go);
        }

        /// <summary>카테고리의 살아있는(파괴되지 않은) 오브젝트 목록(널 정리 후 반환).</summary>
        public IReadOnlyList<GameObject> Active(string category)
        {
            var list = Category(category);
            list.RemoveAll(o => o == null);
            return list;
        }

        /// <summary>카테고리의 살아있는 오브젝트 수.</summary>
        public int Count(string category) => Active(category).Count;

        /// <summary>특정 오브젝트를 모든 카테고리에서 제거하고 파괴한다.</summary>
        public void Despawn(GameObject go)
        {
            if (go == null) return;
            foreach (var kv in _categories) kv.Value.Remove(go);
            Destroy(go);
        }

        /// <summary>카테고리의 모든 오브젝트를 파괴하고 목록을 비운다.</summary>
        public void Clear(string category)
        {
            var list = Category(category);
            foreach (var go in list) if (go != null) Destroy(go);
            list.Clear();
        }
    }
}
