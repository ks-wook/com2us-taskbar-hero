using System;
using System.Collections.Generic;
using TaskbarHero.Common.Dto;

namespace TaskbarHero.Client.Managers
{
    /// <summary>
    /// 로그인한 계정 정보와 게임 세이브 스냅샷을 앱 전역에서 캐싱하는 세션.
    /// 로그인 성공 시 채워지며, 이후 인증이 필요한 API 요청(GameServer 등)에서 재사용한다.
    /// 정적 클래스이므로 씬 전환에도 유지된다(도메인 리로드 시 초기화).
    /// </summary>
    public static class Session
    {
        /// <summary>로그인한 유저 ID.</summary>
        public static long UserId { get; private set; }

        /// <summary>발급받은 인증 토큰.</summary>
        public static string Token { get; private set; }

        /// <summary>계정 닉네임(회원가입 시 입력값 또는 세이브의 player.nickname).</summary>
        public static string Nickname { get; set; }

        /// <summary>/api/game/load로 가져온 <b>코어</b> 세이브 스냅샷(플레이어·캐릭터·재화·장착 장비·스킬·룬·큐브).
        /// 가방 아이템은 여기 없다 — 크기가 가변이라 <see cref="Bag"/>(페이징 조회)로 따로 받는다(세이브 기획서 2장).</summary>
        public static LoadDataDto GameData { get; private set; }

        /// <summary>캐릭터별 장착 장비(코어 로드의 equipped). 스탯 계산·장비 슬롯 표시의 입력이다.</summary>
        public static List<EquippedItemDto> Equipped => GameData != null ? GameData.equipped : null;

        /// <summary>가방(비장착) 아이템 캐시. /api/game/inventory/list 페이지를 이어 붙인 결과다.
        /// 창고 격자는 스크롤이 닿은 구간까지만 받으므로(<see cref="BagPager"/>) 지연 로딩 중에는 <b>부분 집합</b>일 수 있고,
        /// 전량이 필요한 화면은 <see cref="InventoryLoader.ReloadBag"/>로 끝까지 받는다.
        /// 장착 장비·재화는 코어 로드로 내려오므로 여기 포함되지 않는다.</summary>
        public static List<InventoryItemDto> Bag { get; private set; } = new List<InventoryItemDto>();

        /// <summary>가방 캐시가 유효한지(한 번 이상 페이징 조회를 끝냈고, 그 뒤 인벤토리를 바꾸는 액션이 없었는지).
        /// false면 가방을 쓰는 화면은 열 때 다시 조회해야 한다.</summary>
        public static bool BagLoaded { get; private set; }

        /// <summary>Login→GameScene 전환 시 정산한 오프라인 보상 결과. GameScene 진입 팝업이 소비(표시 후 <see cref="ConsumePendingOfflineReward"/>)한다.
        /// 정산할 오프라인이 없거나(3001) 이미 팝업을 띄운 뒤에는 null이다.</summary>
        public static OfflineRewardResult PendingOfflineReward { get; private set; }

        /// <summary>로그인 상태 여부.</summary>
        public static bool IsLoggedIn => !string.IsNullOrEmpty(Token);

        /// <summary>CreateCharacterScene에 게임 안(파티 편성 씬의 '캐릭터 추가')에서 진입했는지. true면 '뒤로가기' 노출.
        /// 회원가입 직후 최초 캐릭터 생성 진입은 false(뒤로가기 없음).</summary>
        public static bool CreateCharacterFromGame { get; set; }

        /// <summary>CreateCharacterScene에서 생성 완료·뒤로가기 후 돌아갈 씬 이름. 기본은 게임 화면이며,
        /// 파티 편성 씬(TeamListScene)에서 진입한 경우 그 씬으로 되돌리기 위해 진입 측이 지정한다.</summary>
        public static string CreateCharacterReturnScene { get; set; } = "GameScene";

        /// <summary>인벤토리(장착 상태 포함)가 바뀌었을 때 발생. 전투 스탯 재계산 등에서 구독한다.</summary>
        public static event Action InventoryChanged;

        /// <summary>인벤토리 변경을 알린다(장착·해제·재로드 후 호출).</summary>
        public static void RaiseInventoryChanged() => InventoryChanged?.Invoke();

        /// <summary>로그인 인증 정보를 저장한다.</summary>
        public static void SetAuth(long userId, string token)
        {
            UserId = userId;
            Token = token;
        }

        /// <summary>
        /// 코어 로드 응답을 캐싱한다(닉네임도 있으면 갱신). 코어 로드에는 가방 아이템이 없으므로
        /// 가방 캐시는 여기서 <b>무효화</b>한다 — 재로드를 유발한 액션이 인벤토리를 바꿨을 수 있고,
        /// 낡은 가방 캐시를 그대로 두면 사라진 아이템이 계속 보인다.
        /// </summary>
        public static void SetGameData(LoadDataDto data)
        {
            GameData = data;
            if (data != null && data.player != null && !string.IsNullOrEmpty(data.player.nickname))
            {
                Nickname = data.player.nickname;
            }
            // 코어 로드에 실려 온 활성 버프를 버프 캐시에도 반영한다(응답에 serverTime이 없어 기준점은 갱신하지 않는다 —
            // 정확한 잔여 시간은 BuffManager.Refresh가 serverTime과 함께 보정한다).
            BuffManager.Apply(data != null ? data.activeBuffs : null);
            InvalidateBag();
        }

        /// <summary>
        /// 페이징으로 모두 받은(itemId 기준 병합이 끝난) 가방 아이템을 캐싱한다.
        /// 계약(세이브 기획서 5.2)상 장착 아이템은 인벤 칸을 점유하지 않아 페이지 결과에 오지 않아야 하지만,
        /// 섞여 오면 가방 격자와 장비 슬롯에 같은 아이템이 중복 표시되므로 여기서 한 번 걸러낸다(방어).
        /// </summary>
        public static void SetBag(List<InventoryItemDto> items)
        {
            Bag = ExcludeEquipped(items);
            BagLoaded = true;
        }

        /// <summary>
        /// 가방 <b>한 페이지</b>를 캐시에 이어 붙인다(스크롤 지연 로딩 — <see cref="BagPager"/>가 페이지마다 호출).
        /// 병합은 itemId 키이며 <b>나중 페이지가 이긴다</b>(세이브 기획서 5.2) — 페이지 사이에 아이템이 옮겨 가
        /// 같은 itemId가 두 페이지에 걸쳐도 최종 위치 하나만 남는다. 병합 후 slot 오름차순으로 정렬해
        /// 서버 조회 순서를 유지하고, 캐시를 쓸 수 있는 상태로 표시한다(<see cref="BagLoaded"/>).
        /// <para>지연 로딩 중 캐시는 <b>받은 구간까지만</b> 채워진 부분 집합이다. 가방 전량이 필요한 화면
        /// (큐브·거래 판매 등록)은 열 때 <see cref="InventoryLoader.ReloadBag"/>로 전량을 다시 받으므로 영향받지 않는다.</para>
        /// </summary>
        public static void MergeBagPage(List<InventoryItemDto> items)
        {
            foreach (var item in ExcludeEquipped(items))
            {
                UpsertBagItem(item);
            }
            SortBag();
            BagLoaded = true;
        }

        /// <summary>가방 목록에서 현재 장착 중인 아이템(itemId 일치)을 제외한다. 제외가 생기면 계약 위반이라 경고를 남긴다.</summary>
        private static List<InventoryItemDto> ExcludeEquipped(List<InventoryItemDto> items)
        {
            var result = new List<InventoryItemDto>();
            if (items == null)
            {
                return result;
            }

            var equipped = Equipped;
            int dropped = 0;
            foreach (var item in items)
            {
                if (item == null)
                {
                    continue;
                }
                if (IsEquipped(item.itemId, equipped))
                {
                    dropped++;
                    continue;
                }
                result.Add(item);
            }

            if (dropped > 0)
            {
                UnityEngine.Debug.LogWarning(
                    $"[Inventory] 가방 페이지에 장착 아이템 {dropped}건이 포함돼 제외했다(서버 계약: 장착품은 페이징 대상 아님).");
            }
            return result;
        }

        /// <summary>해당 itemId가 장착 목록에 있는지.</summary>
        private static bool IsEquipped(long itemId, List<EquippedItemDto> equipped)
        {
            if (equipped == null)
            {
                return false;
            }
            foreach (var e in equipped)
            {
                if (e != null && e.itemId == itemId)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>가방 캐시를 무효화한다(인벤토리를 바꾼 액션·코어 재로드 후). 다음에 가방을 쓸 화면이 다시 조회한다.</summary>
        public static void InvalidateBag()
        {
            Bag = new List<InventoryItemDto>();
            BagLoaded = false;
        }

        // ── 액션 응답 반영(재조회 없이 캐시를 서버 확정값으로 맞춘다) ──
        //
        // 가방을 바꾸는 액션은 응답에 변경분(inventoryDelta)을, 재화·용량·큐브·룬은 각자의 필드를 담고 온다
        // (서버 inventory-item-cube 기획서 §5.0). 아래 헬퍼로 그 값만 반영하면 되므로 액션 뒤에
        // /api/game/load·/api/game/inventory/list 를 다시 부르지 않는다.

        /// <summary>
        /// 가방 변경분을 캐시에 적용한다 — <c>removed</c>(itemId 제거) → <c>upserted</c>(itemId 키 덮어쓰기) 순서이며,
        /// 마지막에 slot 오름차순으로 정렬해 서버 조회 결과와 같은 순서를 유지한다. 같은 응답을 두 번 적용해도
        /// 결과가 같다(멱등).
        /// <para><b>서버가 응답한 slot이 최종 위치다</b> — 스택 병합·빈 칸 배정은 서버 권위이므로 클라이언트가
        /// 예상한 칸과 달라도 응답 값을 그대로 따른다.</para>
        /// <para>가방을 한 번도 받지 않은 상태(<see cref="BagLoaded"/>가 false)면 적용하지 않는다 — 캐시가 없어
        /// 델타만 적용하면 일부 아이템만 든 반쪽 가방이 되고, 어차피 가방을 쓰는 화면이 열릴 때 새로 조회한다.</para>
        /// </summary>
        public static void ApplyInventoryDelta(InventoryDeltaDto delta)
        {
            if (delta == null || !BagLoaded)
            {
                return;
            }

            if (delta.removed != null)
            {
                foreach (var itemId in delta.removed)
                {
                    RemoveBagItem(itemId);
                }
            }
            if (delta.upserted != null)
            {
                foreach (var item in delta.upserted)
                {
                    UpsertBagItem(item);
                }
            }
            SortBag();
        }

        /// <summary>가방에서 해당 itemId 행을 제거한다(없으면 무시).</summary>
        private static void RemoveBagItem(long itemId)
        {
            for (int i = 0; i < Bag.Count; i++)
            {
                if (Bag[i] != null && Bag[i].itemId == itemId)
                {
                    Bag.RemoveAt(i);
                    return;
                }
            }
        }

        /// <summary>가방 행을 itemId 키로 덮어쓰거나(있으면) 새로 넣는다(없으면). 신규·수정을 구분하지 않는다.</summary>
        private static void UpsertBagItem(InventoryItemDto item)
        {
            if (item == null)
            {
                return;
            }
            for (int i = 0; i < Bag.Count; i++)
            {
                if (Bag[i] != null && Bag[i].itemId == item.itemId)
                {
                    Bag[i] = item;
                    return;
                }
            }
            Bag.Add(item);
        }

        /// <summary>가방을 slot 오름차순으로 정렬한다(서버 조회 순서와 동일하게 유지).</summary>
        private static void SortBag() => Bag.Sort((a, b) => a.slot.CompareTo(b.slot));

        /// <summary>캐시된 가방에서 해당 아이템의 칸 번호를 서버가 확정한 값으로 갱신한다(배치 이동 저장 후).</summary>
        public static void ApplyBagSlot(long itemId, int slot)
        {
            foreach (var item in Bag)
            {
                if (item != null && item.itemId == itemId)
                {
                    item.slot = slot;
                    SortBag();
                    return;
                }
            }
        }

        /// <summary>응답의 재화 잔액(balance)을 캐시에 반영한다. 서버가 확정한 <b>잔액</b>이므로 가산이 아니라 대입이며,
        /// 캐시에 없던 재화 종류는 새로 추가한다.</summary>
        public static void ApplyBalance(List<CurrencyDto> balance)
        {
            if (balance == null || GameData == null)
            {
                return;
            }
            if (GameData.currencies == null)
            {
                GameData.currencies = new List<CurrencyDto>();
            }
            foreach (var after in balance)
            {
                if (after == null)
                {
                    continue;
                }
                bool found = false;
                foreach (var cur in GameData.currencies)
                {
                    if (cur != null && cur.currencyType == after.currencyType)
                    {
                        cur.amount = after.amount;
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    GameData.currencies.Add(after);
                }
            }
        }

        /// <summary>인벤토리 용량을 응답 값으로 갱신한다(용량 확장 후).</summary>
        public static void ApplyInventoryCapacity(int inventoryCapacity)
        {
            if (GameData != null && GameData.player != null && inventoryCapacity > 0)
            {
                GameData.player.inventoryCapacity = inventoryCapacity;
            }
        }

        /// <summary>큐브 상태(레벨·경험치)를 응답 값으로 교체한다(합성·분해·제작 후).</summary>
        public static void ApplyCube(CubeDto cube)
        {
            if (GameData != null && cube != null)
            {
                GameData.cube = cube;
            }
        }

        /// <summary>룬 1개의 레벨을 응답 값으로 갱신한다(캐시에 없던 룬이면 추가). 룬 강화 후.</summary>
        public static void ApplyRuneLevel(int runeCode, int level)
        {
            if (GameData == null)
            {
                return;
            }
            if (GameData.runes == null)
            {
                GameData.runes = new List<RuneDto>();
            }
            foreach (var rune in GameData.runes)
            {
                if (rune != null && rune.runeCode == runeCode)
                {
                    rune.level = level;
                    return;
                }
            }
            GameData.runes.Add(new RuneDto { runeCode = runeCode, level = level });
        }

        /// <summary>
        /// 장착 응답을 캐시에 반영한다 — 장착한 아이템은 <b>가방에서 빠져</b> 장착 목록으로 옮기고,
        /// 스왑으로 밀려난 기존 장비는 서버가 알려준 <c>unequippedBagSlot</c> 칸으로 되돌린다.
        /// 아이템 코드·강화 수치는 응답에 없지만 각각 가방 행과 장착 목록에 이미 있으므로 그대로 옮겨 쓴다.
        /// </summary>
        public static void ApplyEquipResult(EquipResultData data)
        {
            if (data == null || data.equipped == null || GameData == null)
            {
                return;
            }
            if (GameData.equipped == null)
            {
                GameData.equipped = new List<EquippedItemDto>();
            }

            // 1) 스왑으로 밀려난 기존 장비: 장착 목록에서 빼 가방(unequippedBagSlot)으로 되돌린다.
            if (data.unequipped != null && data.unequipped.itemId != 0)
            {
                var old = TakeEquipped(data.unequipped.itemId);
                if (old != null && BagLoaded)
                {
                    UpsertBagItem(new InventoryItemDto
                    {
                        itemId = old.itemId,
                        slot = data.unequippedBagSlot,
                        itemCode = old.itemCode,
                        quantity = 1,
                        enhanceLevel = old.enhanceLevel,
                    });
                }
            }

            // 2) 장착한 아이템: 가방 행을 근거로 장착 목록에 추가하고 가방에서 제거한다.
            var bagRow = FindBagItem(data.equipped.itemId);
            GameData.equipped.Add(new EquippedItemDto
            {
                itemId = data.equipped.itemId,
                itemCode = bagRow != null ? bagRow.itemCode : 0,
                enhanceLevel = bagRow != null ? bagRow.enhanceLevel : 0,
                equippedCharacterId = data.characterId,
                equippedSlot = data.equipped.slot,
            });
            RemoveBagItem(data.equipped.itemId);
            SortBag();
        }

        /// <summary>장착 해제 응답을 캐시에 반영한다 — 해제한 장비를 장착 목록에서 빼 서버가 알려준
        /// <c>bagSlot</c> 칸의 가방 행으로 되돌린다.</summary>
        public static void ApplyUnequipResult(UnequipResultData data)
        {
            if (data == null || data.itemId == 0)
            {
                return;
            }
            var removed = TakeEquipped(data.itemId);
            if (removed == null || !BagLoaded)
            {
                return;
            }
            UpsertBagItem(new InventoryItemDto
            {
                itemId = removed.itemId,
                slot = data.bagSlot,
                itemCode = removed.itemCode,
                quantity = 1,
                enhanceLevel = removed.enhanceLevel,
            });
            SortBag();
        }

        /// <summary>장착 목록에서 해당 itemId를 빼내 반환한다(없으면 null).</summary>
        private static EquippedItemDto TakeEquipped(long itemId)
        {
            var equipped = Equipped;
            if (equipped == null)
            {
                return null;
            }
            for (int i = 0; i < equipped.Count; i++)
            {
                if (equipped[i] != null && equipped[i].itemId == itemId)
                {
                    var found = equipped[i];
                    equipped.RemoveAt(i);
                    return found;
                }
            }
            return null;
        }

        /// <summary>가방에서 해당 itemId 행을 찾는다(없으면 null).</summary>
        private static InventoryItemDto FindBagItem(long itemId)
        {
            foreach (var item in Bag)
            {
                if (item != null && item.itemId == itemId)
                {
                    return item;
                }
            }
            return null;
        }

        /// <summary>
        /// 오프라인 보상 정산 결과를 캐싱된 세이브에 반영하고, GameScene 진입 팝업이 표시할 수 있게 대기시킨다.
        /// 서버가 이미 지급을 확정한 값이므로(경험치·레벨·골드), 클라이언트 캐시(파티/HUD/인벤토리 표시)를 동일 상태로 맞춘다:
        /// 각 캐릭터의 level·exp를 정산 후 값으로 갱신하고, 계정 골드(재화 타입 1)를 지급분만큼 증가시킨다.
        /// </summary>
        public static void ApplyOfflineReward(OfflineRewardResult result)
        {
            if (result == null)
            {
                return;
            }

            // 정산 후 캐릭터 상태(level·잔여 exp)를 캐시에 반영(characterId로 매칭).
            if (GameData != null && GameData.characters != null && result.characters != null)
            {
                foreach (var after in result.characters)
                {
                    if (after == null)
                    {
                        continue;
                    }
                    foreach (var c in GameData.characters)
                    {
                        if (c != null && c.characterId == after.characterId)
                        {
                            c.level = after.level;
                            c.exp = after.exp;
                            break;
                        }
                    }
                }
            }

            // 지급 골드를 계정 재화(타입 1)에 가산.
            long gold = result.rewards != null ? result.rewards.gold : 0L;
            if (gold > 0 && GameData != null && GameData.currencies != null)
            {
                foreach (var cur in GameData.currencies)
                {
                    if (cur != null && cur.currencyType == 1)
                    {
                        cur.amount += gold;
                        break;
                    }
                }
            }

            PendingOfflineReward = result;
        }

        /// <summary>
        /// 서버가 회신한 보유 캐릭터 전체를 캐시에 반영한다(파티 편성 저장 응답 등).
        /// party/arrange 는 갱신된 캐릭터 목록을 자리 순으로 통째로 돌려주므로, 그대로 교체하면
        /// 편성 자리(slot) 변경이 세션에 반영된다(재로드 불필요 — 세이브 데이터 기획서 5.5).
        /// </summary>
        public static void ApplyCharacters(List<CharacterDto> characters)
        {
            if (GameData == null || characters == null)
            {
                return;
            }
            GameData.characters = characters;
        }

        /// <summary>대기 중인 오프라인 보상 결과를 반환하고 비운다(GameScene 팝업이 1회 소비). 없으면 null.</summary>
        public static OfflineRewardResult ConsumePendingOfflineReward()
        {
            var pending = PendingOfflineReward;
            PendingOfflineReward = null;
            return pending;
        }

        /// <summary>세션을 비운다(로그아웃).</summary>
        public static void Clear()
        {
            UserId = 0;
            Token = null;
            Nickname = null;
            GameData = null;
            PendingOfflineReward = null;
            BuffManager.Clear();
            InvalidateBag();
        }
    }
}
