using System;
using System.Collections.Generic;
using UnityEngine;
using TaskbarHero.Common;
using TaskbarHero.Common.Dto;

namespace TaskbarHero.Client.Managers
{
    /// <summary>
    /// 계정 획득량 버프(경험치·골드)의 클라이언트 캐시. 소모품/버프 기획서 5.2의 세 창구를 모두 받는다 —
    /// 코어 로드(<c>/api/game/load</c>의 activeBuffs) · 소모품 사용 응답 · 재동기화 조회(<c>/api/game/consumable/buffs</c>).
    ///
    /// 규약:
    /// - <b>만료 판정은 서버가 확정한다.</b> 클라이언트는 <c>expiresAt - serverNow</c>로 남은 시간만 표시하고,
    ///   카운트다운이 0에 닿으면 목록에서 지운 뒤 서버에 재동기화를 요청해 확인한다.
    /// - 잔여 시간 기준점은 로컬 시계가 아니라 <b>서버 시각</b>이다. 조회 응답의 <c>serverTime</c>을 받은 순간의
    ///   <see cref="Time.realtimeSinceStartup"/>과 함께 저장해 두고, 이후에는 그 경과분을 더해 서버 시각을 추정한다.
    ///   (동기화 전에는 로컬 UTC로 폴백한다 — 코어 로드 응답에는 serverTime이 없다.)
    /// - <b>주기 폴링은 하지 않는다</b>(기획서 5.2). 버프는 소모품 사용 외에 서버 단독으로 바뀌지 않으므로,
    ///   진입 시 1회·사용 직후·카운트다운 만료 직후에만 재동기화한다.
    /// </summary>
    public static class BuffManager
    {
        private static readonly List<ActiveBuffDto> Buffs = new List<ActiveBuffDto>();

        // 마지막으로 서버 시각을 받은 시점(서버 unix ts + 그때의 realtimeSinceStartup). 미동기화면 _realtimeAtSync < 0.
        private static long _serverTimeAtSync;
        private static float _realtimeAtSync = -1f;

        // 재동기화 중복 요청 방지(연속 만료·패널 재오픈 등으로 겹쳐 호출되는 경우).
        private static bool _refreshing;

        /// <summary>현재 적용 중인 버프 목록(서버가 내려준 그대로, buff_type 오름차순). 없으면 빈 목록.</summary>
        public static IReadOnlyList<ActiveBuffDto> ActiveBuffs => Buffs;

        /// <summary>적용 중인 버프가 하나라도 있는지(버프 아이콘 노출 조건).</summary>
        public static bool HasActiveBuff => Buffs.Count > 0;

        /// <summary>버프 목록이 바뀌었을 때 발생(부여·연장·만료·재조회). 버프 UI가 구독한다.</summary>
        public static event Action Changed;

        /// <summary>추정 서버 시각(Unix ts, 초). 동기화 전에는 로컬 UTC로 폴백한다.</summary>
        public static long ServerNow => _realtimeAtSync < 0f
            ? DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            : _serverTimeAtSync + (long)(Time.realtimeSinceStartup - _realtimeAtSync);

        /// <summary>
        /// 서버가 내려준 활성 버프 목록으로 캐시를 교체한다(코어 로드·사용 응답·재동기화 공용).
        /// <paramref name="serverTime"/>이 0보다 크면 잔여 시간 계산의 기준점(서버 시각)도 함께 갱신한다.
        /// </summary>
        public static void Apply(List<ActiveBuffDto> buffs, long serverTime = 0)
        {
            if (serverTime > 0)
            {
                _serverTimeAtSync = serverTime;
                _realtimeAtSync = Time.realtimeSinceStartup;
            }

            Buffs.Clear();
            if (buffs != null)
            {
                foreach (var b in buffs)
                {
                    if (b != null)
                    {
                        Buffs.Add(b);
                    }
                }
            }
            Buffs.Sort((a, b) => a.buffType.CompareTo(b.buffType));
            Changed?.Invoke();
        }

        /// <summary>남은 시간이 다한 버프를 목록에서 제거한다(로컬 카운트다운 종료). 제거가 있었으면 true.
        /// 실제 만료 확정은 서버 몫이라, 제거 후에는 호출부가 <see cref="Refresh"/>로 확인한다.</summary>
        public static bool PruneExpired()
        {
            long now = ServerNow;
            int removed = Buffs.RemoveAll(b => b == null || b.expiresAt <= now);
            if (removed > 0)
            {
                Changed?.Invoke();
            }
            return removed > 0;
        }

        /// <summary>버프 1건의 남은 시간(초). 이미 만료됐으면 0.</summary>
        public static long RemainingSeconds(ActiveBuffDto buff)
        {
            if (buff == null)
            {
                return 0;
            }
            long remain = buff.expiresAt - ServerNow;
            return remain > 0 ? remain : 0;
        }

        /// <summary>
        /// 서버에서 활성 버프를 다시 받아 캐시를 갱신한다(<c>POST /api/game/consumable/buffs</c>).
        /// 버프 UI를 여는 시점·소모품 사용 직후·카운트다운 만료 직후에만 호출한다(주기 폴링 금지).
        /// 네트워크/세션이 없거나 이미 조회 중이면 아무것도 하지 않는다.
        /// </summary>
        public static void Refresh(Action onDone = null)
        {
            if (_refreshing || NetworkManager.Instance == null || !Session.IsLoggedIn)
            {
                onDone?.Invoke();
                return;
            }

            _refreshing = true;
            var req = new AuthRequest { userId = Session.UserId, token = Session.Token };
            NetworkManager.Instance.PostToGame<ActiveBuffListResponse>("/api/game/consumable/buffs", req,
                resp =>
                {
                    _refreshing = false;
                    if (resp != null && resp.data != null)
                    {
                        Apply(resp.data.activeBuffs, resp.data.serverTime);
                    }
                    onDone?.Invoke();
                },
                error =>
                {
                    _refreshing = false;
                    Debug.LogWarning($"[Buff] 활성 버프 조회 실패: {error}");
                    onDone?.Invoke();
                });
        }

        /// <summary>세션 종료(로그아웃) 시 캐시와 시각 동기화를 비운다 — 다음 계정에 이전 버프가 새지 않도록.</summary>
        public static void Clear()
        {
            bool had = Buffs.Count > 0;
            Buffs.Clear();
            _serverTimeAtSync = 0;
            _realtimeAtSync = -1f;
            if (had)
            {
                Changed?.Invoke();
            }
        }

        /// <summary>버프 종류의 표시 이름(모르는 종류는 그대로 번호로 — 신규 buffType이 늘어도 UI가 깨지지 않게).</summary>
        public static string DisplayName(int buffType)
        {
            switch ((BuffType)buffType)
            {
                case BuffType.ExpGain: return "경험치 획득량";
                case BuffType.GoldGain: return "골드 획득량";
                default: return $"버프 {buffType}";
            }
        }

        /// <summary>배율을 "+50%" 형태의 증가율 문구로 만든다(1.5 → +50%).</summary>
        public static string BonusText(float buffValue)
        {
            float bonus = (buffValue - 1f) * 100f;
            return bonus > 0f ? $"+{bonus:0.#}%" : $"{bonus:0.#}%";
        }

        /// <summary>남은 시간을 "1:02:03"(시:분:초) 또는 "12:34"(분:초)로 표기한다.</summary>
        public static string RemainText(long seconds)
        {
            if (seconds < 0)
            {
                seconds = 0;
            }
            long h = seconds / 3600;
            long m = seconds % 3600 / 60;
            long s = seconds % 60;
            return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m:00}:{s:00}";
        }

        /// <summary>만료 시각(서버 Unix ts)을 로컬 시각 "HH:mm:ss"로 표기한다.</summary>
        public static string ExpireTimeText(long expiresAt)
        {
            return DateTimeOffset.FromUnixTimeSeconds(expiresAt).ToLocalTime().ToString("HH:mm:ss");
        }
    }
}
