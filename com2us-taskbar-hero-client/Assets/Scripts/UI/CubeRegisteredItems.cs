using System;
using System.Collections.Generic;
using UnityEngine;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// <b>큐브 등록 칸에 올려 둔 가방 아이템 id</b> 모음. 큐브(<see cref="CubePanelController"/>)가 쓰고,
    /// 가방(<see cref="InventoryPanelController"/>)이 읽어 그 아이템을 격자에서 <b>감춘다</b>.
    ///
    /// <para>등록은 <b>순수 클라이언트 상태</b>다 — 서버는 합성·연금술·강화를 <b>실행</b>할 때 비로소 가방을 바꾼다.
    /// 그래서 끌어다 올리기만 한 아이템은 서버 가방(<c>Session.Bag</c>)에 그대로 남아 있고, 그 결과 가방 격자에도
    /// 계속 보여 <b>같은 아이템이 두 군데 있는 것처럼</b> 보였다. 이 목록이 그 사이를 메운다.</para>
    ///
    /// <para>실행하지 않고 큐브를 닫으면 등록은 무효이므로 목록이 비워지고, 감춰졌던 아이템이 가방에 <b>다시
    /// 나타난다</b>(<see cref="Clear"/> — 큐브의 <c>OnDisable</c>이 호출).</para>
    /// </summary>
    public static class CubeRegisteredItems
    {
        private static readonly HashSet<long> Ids = new HashSet<long>();

        /// <summary>등록 목록이 바뀌었다 — 가방 격자를 다시 그려야 한다.</summary>
        public static event Action Changed;

        /// <summary>그 아이템이 지금 큐브에 올라가 있는지(= 가방 격자에서 감출 대상인지).</summary>
        public static bool Contains(long itemId)
        {
            return itemId != 0 && Ids.Contains(itemId);
        }

        /// <summary>등록 목록을 통째로 갈아 끼운다. 내용이 그대로면 <see cref="Changed"/>를 올리지 않는다
        /// (큐브는 화면을 다시 그릴 때마다 이 메서드를 부르므로, 그때마다 가방을 다시 그리면 낭비다).</summary>
        public static void Set(IEnumerable<long> itemIds)
        {
            var next = new HashSet<long>();
            if (itemIds != null)
            {
                foreach (long id in itemIds)
                {
                    if (id != 0)
                    {
                        next.Add(id);
                    }
                }
            }

            if (next.SetEquals(Ids))
            {
                return;
            }

            Ids.Clear();
            foreach (long id in next)
            {
                Ids.Add(id);
            }
            Changed?.Invoke();
        }

        /// <summary>등록을 모두 비운다(큐브를 닫았거나 실행을 마쳤을 때 — 감춘 아이템이 가방에 되돌아온다).</summary>
        public static void Clear()
        {
            Set(null);
        }

        /// <summary>
        /// 플레이 시작마다 정적 상태를 비운다. 에디터의 <b>도메인 리로드 끄기</b> 설정에서는 플레이를 껐다 켜도
        /// 정적 필드가 살아남아, 지난 세션에 등록해 둔 id가 남으면 그 아이템이 가방에서 계속 사라진 것처럼 보인다.
        /// 구독자(<see cref="Changed"/>)도 이미 파괴된 옛 패널을 가리키므로 함께 끊는다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            Ids.Clear();
            Changed = null;
        }
    }
}
