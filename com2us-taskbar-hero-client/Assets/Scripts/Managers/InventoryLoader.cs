using System;
using System.Collections.Generic;
using UnityEngine;
using TaskbarHero.Common.Dto;

namespace TaskbarHero.Client.Managers
{
    /// <summary>
    /// 가방(인벤토리) 아이템의 지연 로딩 담당. 로드는 2단계로 나뉘어 있어(세이브 데이터 기획서 5.1·5.2)
    /// 코어 로드(<c>/api/game/load</c>)에는 가방 아이템이 없고, 가방은 이 로더가
    /// <c>/api/game/inventory/list</c>를 <b>slot 커서 keyset 페이징</b>으로 끝까지 훑어 <see cref="Session.Bag"/>에 채운다.
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
        /// <summary>페이지 크기(서버가 1~500으로 클램프, 기본 200).</summary>
        private const int PageLimit = 200;

        /// <summary>가방 캐시가 유효하면 즉시 완료 콜백을 호출하고, 아니면 전체 페이지를 조회한 뒤 호출한다.
        /// 가방을 보여주는 화면(창고·큐브·거래 판매 등록)을 열 때 사용한다.</summary>
        public static void EnsureBag(Action onDone, Action<NetworkError> onError = null)
        {
            if (Session.BagLoaded)
            {
                onDone?.Invoke();
                return;
            }
            ReloadBag(onDone, onError);
        }

        /// <summary>가방을 첫 페이지부터 다시 조회해 캐시를 교체한다(캐시가 유효해도 무조건 재조회).</summary>
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
        /// 코어 로드(<c>/api/game/load</c>) → 가방 페이징까지 이어서 다시 받는다.
        /// 인벤토리를 바꾼 액션(장착·해제·이동·큐브·거래·메일 수령 등) 뒤에 세션 전체를 최신화할 때 쓴다.
        /// </summary>
        public static void ReloadAll(Action onDone, Action<NetworkError> onError = null)
        {
            if (!CanRequest())
            {
                onDone?.Invoke();
                return;
            }

            var request = new AuthRequest { userId = Session.UserId, token = Session.Token };
            NetworkManager.Instance.PostToGame<LoadResponse>("/api/game/load", request, response =>
            {
                if (response != null && response.data != null)
                {
                    Session.SetGameData(response.data); // 가방 캐시는 여기서 무효화된다
                }
                ReloadBag(onDone, onError);
            }, onError);
        }

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
    }
}
