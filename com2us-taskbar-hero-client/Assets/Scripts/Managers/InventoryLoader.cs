using System;
using System.Collections.Generic;
using UnityEngine;
using TaskbarHero.Common.Dto;

namespace TaskbarHero.Client.Managers
{
    /// <summary>
    /// 가방(인벤토리) 아이템의 지연 로딩 담당. 로드는 2단계로 나뉘어 있어(세이브 데이터 기획서 5.1·5.2)
    /// 코어 로드(<c>/api/game/load</c>)에는 가방 아이템이 없고, 가방은 <c>/api/game/inventory/list</c>를
    /// <b>slot 커서 keyset 페이징</b>으로 받아 <see cref="Session.Bag"/>에 채운다.
    ///
    /// 받는 방식은 두 가지다:
    /// - <see cref="ReloadBag"/> — 전량 조회. 가방 <b>전체</b>를 알아야 하는 화면(큐브 재료 목록·거래 판매 등록)이 쓴다.
    ///   끝까지 페이지를 이어 받아 캐시를 통째로 교체한다.
    /// - <see cref="BeginPaged"/> — 스크롤 지연 로딩용 커서(<see cref="BagPager"/>). 창고 격자처럼
    ///   <b>보이는 만큼만</b> 필요한 화면이 쓴다. 열 때 첫 페이지만 받고, 스크롤이 아직 받지 않은 칸에 닿을 때마다
    ///   한 페이지씩 이어 받는다(<see cref="Session.MergeBagPage"/>로 캐시를 넓힌다).
    ///
    /// 규약:
    /// - 첫 페이지는 <c>cursor=-1</c>(slot이 0-based라 slot &gt; -1이 곧 처음부터), 이후는 직전 응답의 <c>nextCursor</c>.
    /// - <b>서버는 페이지 사이의 인벤토리 변경을 감지하지 않는다.</b> 그래서 페이지를 이어붙일 때
    ///   <b>itemId를 키로 중복을 제거하고 나중 페이지를 우선</b>하는 병합이 클라이언트 몫의 계약이다(기획서 5.2).
    ///   이동으로 같은 아이템이 두 페이지에 걸쳐도 최종 위치 하나만 남는다.
    /// - 남는 오차(지나간 칸으로 이동한 아이템 누락·소모된 유령 아이템)는 창고를 다시 열면 해소되고,
    ///   유령 아이템 조작은 서버가 <see cref="TaskbarHero.Common.ErrorCode.ItemNotFound"/>(4001)로 거부한다
    ///   → 그 에러를 받은 화면이 가방을 새로 고친다.
    /// - 신규 계정(<c>isNew</c>)은 인벤토리가 없으므로 조회하지 않는다.
    /// </summary>
    public static class InventoryLoader
    {
        /// <summary>전량 조회의 페이지 크기(서버가 1~500으로 클램프, 기본 200).</summary>
        private const int PageLimit = 200;

        /// <summary>스크롤 지연 로딩의 기본 페이지 크기(칸 수). 창고 격자 몇 줄 분량만 먼저 받는다.</summary>
        public const int ScrollPageLimit = 20;

        /// <summary>가방을 첫 페이지부터 다시 조회해 캐시를 교체한다(캐시가 유효해도 무조건 재조회).
        /// 가방을 보여주는 화면(창고·큐브·거래 판매 등록)을 <b>열 때</b>와, 가방 캐시가 서버와 어긋났을 때
        /// (<c>ItemNotFound</c>·배치 이동 저장 실패) 호출한다.</summary>
        public static void ReloadBag(Action onDone, Action<NetworkError> onError = null)
        {
            if (!CanRequest())
            {
                onDone?.Invoke(); // 네트워크/세션이 없으면 빈 가방으로 진행(오프라인 편집·에디터 재생 대비)
                return;
            }
            if (IsNewAccount())
            {
                Session.SetBag(new List<InventoryItemDto>()); // 세이브 없는 계정은 조회 대상이 없다(기획서 5.1)
                onDone?.Invoke();
                return;
            }
            FetchPage(new Dictionary<long, InventoryItemDto>(), new List<long>(), -1, onDone, onError);
        }

        /// <summary>
        /// 스크롤 지연 로딩용 커서를 새로 만든다. 가방 캐시를 비우고(창고를 열 때마다 처음부터 다시 받는다 —
        /// 자동 전투로 전리품이 계속 쌓이므로 로컬 캐시를 신뢰하지 않는다) 첫 페이지를 받을 준비만 한다.
        /// 실제 요청은 호출 측이 <see cref="BagPager.LoadNext"/>로 필요한 시점에 한 페이지씩 낸다.
        /// </summary>
        /// <param name="pageLimit">한 페이지에 받을 칸 수(0 이하면 <see cref="ScrollPageLimit"/>).</param>
        public static BagPager BeginPaged(int pageLimit)
        {
            Session.InvalidateBag();
            // 네트워크·세션이 없거나(오프라인 편집·에디터 재생) 세이브 없는 신규 계정이면 받을 페이지가 없다.
            bool nothingToFetch = !CanRequest() || IsNewAccount();
            if (nothingToFetch)
            {
                Session.MergeBagPage(new List<InventoryItemDto>()); // 빈 가방으로 확정(기획서 5.1)
            }
            return new BagPager(pageLimit > 0 ? pageLimit : ScrollPageLimit, nothingToFetch);
        }

        // 인벤토리를 바꾼 액션 뒤에 코어+가방을 통째로 다시 받던 ReloadAll은 제거했다 —
        // 서버가 변경분(inventoryDelta)·잔액·큐브 상태를 응답에 담아 주므로(§5.0 규약) 재조회가 필요 없고,
        // 남겨 두면 액션마다 다시 호출되는 회귀가 생기기 쉽다. 캐시 재동기화가 필요하면 ReloadBag만 쓴다.

        /// <summary>
        /// 가방 한 페이지를 조회해 누적 결과에 병합하고, 다음 페이지가 있으면 재귀적으로 이어 받는다.
        /// 병합은 itemId 키 + 나중 페이지 우선이며(같은 itemId면 뒤에 온 행이 이긴다), 마지막 페이지에서
        /// slot 오름차순으로 정리해 세션 캐시에 반영한다.
        /// </summary>
        private static void FetchPage(Dictionary<long, InventoryItemDto> merged, List<long> order, int cursor,
            Action onDone, Action<NetworkError> onError)
        {
            var request = new InventoryListRequest
            {
                userId = Session.UserId,
                token = Session.Token,
                data = new InventoryListData { cursor = cursor, limit = PageLimit },
            };

            NetworkManager.Instance.PostToGame<InventoryListResponse>("/api/game/inventory/list", request, response =>
            {
                var page = response != null ? response.data : null;
                if (page == null)
                {
                    Session.SetBag(Collect(merged, order));
                    onDone?.Invoke();
                    return;
                }

                Merge(merged, order, page.items);

                if (page.hasMore && page.items != null && page.items.Count > 0)
                {
                    FetchPage(merged, order, page.nextCursor, onDone, onError);
                    return;
                }

                var items = Collect(merged, order);
                Session.SetBag(items);
                Debug.Log($"[Inventory] 가방 로드 완료: {items.Count}개(서버 집계 {page.total}개)");
                onDone?.Invoke();
            }, error =>
            {
                Debug.LogWarning($"[Inventory] 가방 조회 실패: {error}");
                onError?.Invoke(error);
            });
        }

        /// <summary>페이지 항목을 누적 결과에 병합한다. 같은 itemId가 이미 있으면 <b>나중 페이지 값으로 덮는다</b>(기획서 5.2).</summary>
        private static void Merge(Dictionary<long, InventoryItemDto> merged, List<long> order,
            List<InventoryItemDto> items)
        {
            if (items == null)
            {
                return;
            }
            foreach (var item in items)
            {
                if (item == null)
                {
                    continue;
                }
                if (!merged.ContainsKey(item.itemId))
                {
                    order.Add(item.itemId);
                }
                merged[item.itemId] = item;
            }
        }

        /// <summary>병합 결과를 slot 오름차순 목록으로 만든다(서버 정렬과 같은 순서로 화면·목록이 안정되게).</summary>
        private static List<InventoryItemDto> Collect(Dictionary<long, InventoryItemDto> merged, List<long> order)
        {
            var result = new List<InventoryItemDto>(merged.Count);
            foreach (var itemId in order)
            {
                InventoryItemDto item;
                if (merged.TryGetValue(itemId, out item))
                {
                    result.Add(item);
                }
            }
            result.Sort((a, b) => a.slot.CompareTo(b.slot));
            return result;
        }

        /// <summary>요청 가능한 상태인지(네트워크 매니저 + 로그인 세션).</summary>
        private static bool CanRequest() => NetworkManager.Instance != null && Session.IsLoggedIn;

        /// <summary>세이브가 없는 신규 계정인지(코어 로드가 isNew만 돌려준 상태).</summary>
        private static bool IsNewAccount() => Session.GameData != null && Session.GameData.isNew;

        /// <summary>요청 가능한 상태인지(<see cref="BagPager"/>가 페이지를 낼 때 확인).</summary>
        internal static bool CanRequestPage() => CanRequest();
    }

    /// <summary>
    /// 가방을 <b>스크롤에 맞춰 한 페이지씩</b> 받기 위한 slot 커서 상태(<c>/api/game/inventory/list</c>).
    /// 창고를 열 때 <see cref="InventoryLoader.BeginPaged"/>로 만들고, 스크롤이 아직 받지 않은 칸에 닿을 때마다
    /// <see cref="LoadNext"/>를 호출하면 다음 페이지를 받아 <see cref="Session.MergeBagPage"/>로 캐시를 넓힌다.
    /// <para>중복 요청(<see cref="IsLoading"/>)과 마지막 페이지(<see cref="HasMore"/>)는 이 클래스가 걸러내므로
    /// 호출 측은 스크롤 이벤트마다 부담 없이 <see cref="LoadNext"/>를 불러도 된다.</para>
    /// </summary>
    public sealed class BagPager
    {
        private readonly int _limit;
        private int _cursor = -1; // 첫 페이지는 -1(slot이 0-based라 slot > -1이 곧 처음부터)
        private bool _hasMore;
        private bool _loading;

        /// <summary>페이지 크기와 초기 완료 여부를 받아 커서를 만든다(생성은 <see cref="InventoryLoader.BeginPaged"/> 전용).</summary>
        /// <param name="limit">한 페이지에 받을 칸 수.</param>
        /// <param name="complete">받을 페이지가 애초에 없으면 true(오프라인·신규 계정).</param>
        internal BagPager(int limit, bool complete)
        {
            _limit = limit;
            _hasMore = !complete;
        }

        /// <summary>더 받을 페이지가 남았는지(마지막 페이지를 받으면 false).</summary>
        public bool HasMore => _hasMore;

        /// <summary>페이지 요청이 진행 중인지(응답 전 중복 요청 방지).</summary>
        public bool IsLoading => _loading;

        /// <summary>지금까지 받은 <b>마지막 칸 번호</b>. 이 번호보다 뒤의 칸을 보려면 다음 페이지가 필요하다(아직 없으면 -1).</summary>
        public int LoadedSlot => _cursor;

        /// <summary>서버가 알려준 가방 아이템 총 행 수(응답의 total, 아직 받기 전이면 0).</summary>
        public int Total { get; private set; }

        /// <summary>
        /// 다음 페이지를 요청한다. 이미 요청 중이거나 마지막 페이지까지 받았으면 아무것도 하지 않는다.
        /// 응답이 오면 <see cref="Session.MergeBagPage"/>로 캐시를 넓히고 커서를 이 페이지 마지막 slot으로 옮긴 뒤
        /// <paramref name="onPage"/>를 호출한다(호출 측이 격자를 다시 그린다).
        /// </summary>
        public void LoadNext(Action onPage, Action<NetworkError> onError = null)
        {
            if (_loading || !_hasMore)
            {
                return;
            }
            if (!InventoryLoader.CanRequestPage())
            {
                _hasMore = false; // 네트워크/세션이 없으면 더 받을 수 없다
                onPage?.Invoke();
                return;
            }

            _loading = true;
            var request = new InventoryListRequest
            {
                userId = Session.UserId,
                token = Session.Token,
                data = new InventoryListData { cursor = _cursor, limit = _limit },
            };
            Debug.Log($"[Inventory] 가방 페이지 요청 cursor={_cursor} limit={_limit}");

            NetworkManager.Instance.PostToGame<InventoryListResponse>("/api/game/inventory/list", request, response =>
            {
                _loading = false;
                var page = response != null ? response.data : null;
                if (page == null)
                {
                    _hasMore = false;
                    onPage?.Invoke();
                    return;
                }

                Total = page.total;
                Session.MergeBagPage(page.items);

                // 빈 페이지를 받으면 커서가 제자리라 같은 요청이 무한 반복되므로 여기서 끝낸다.
                bool advanced = page.items != null && page.items.Count > 0;
                if (advanced)
                {
                    _cursor = page.nextCursor;
                }
                _hasMore = page.hasMore && advanced;

                Debug.Log($"[Inventory] 가방 페이지 도착 {(page.items != null ? page.items.Count : 0)}개 " +
                          $"(누적 {Session.Bag.Count}/{Total}, nextCursor={_cursor}, hasMore={_hasMore})");
                onPage?.Invoke();
            }, error =>
            {
                _loading = false;
                Debug.LogWarning($"[Inventory] 가방 페이지 조회 실패: {error}");
                onError?.Invoke(error);
            });
        }

        /// <summary>더 받을 페이지가 없다고 표시한다(전량 재조회로 캐시를 갈아끼운 뒤 — 커서가 낡아 이어 받으면 안 된다).</summary>
        public void MarkComplete()
        {
            _hasMore = false;
            _loading = false;
        }
    }
}
