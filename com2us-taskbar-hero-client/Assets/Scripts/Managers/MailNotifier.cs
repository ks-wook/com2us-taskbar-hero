using System;
using System.Collections.Generic;
using UnityEngine;
using TaskbarHero.Common.Dto;

namespace TaskbarHero.Client.Managers
{
    /// <summary>
    /// 우편함 스냅샷을 전역에 캐싱해 <b>미수령 보상 알림(레드닷)</b>의 데이터 소스가 되는 정적 알림기.
    /// 우편함을 열지 않아도 "만료 전에 받아야 할 첨부가 남아 있는지"를 알 수 있도록 주기적으로 목록을 갱신한다.
    /// <para>
    /// 서버의 <c>POST /api/game/mail/list</c>는 <b>조회 = 읽음 처리</b>라, 알림 목적의 갱신만으로도 미열람 봉투 표시가
    /// 사라져 버린다. 이를 막기 위해 응답에서 <c>isRead == 0</c>이었던 메일 ID를 로컬에 기억해 두고
    /// (<see cref="WasUnread"/>), 사용자가 실제로 우편함을 연 뒤(<see cref="MarkAllViewed"/>)에야 열람으로 간주한다.
    /// </para>
    /// <para>
    /// 레드닷 조건은 '열람 여부'가 아니라 <b>수령 여부</b>다 — 첨부가 있고 아직 수령하지 않았으며 만료되지 않은
    /// 메일이 하나라도 있으면 표시한다(<see cref="HasUnclaimedReward"/>).
    /// </para>
    /// </summary>
    public static class MailNotifier
    {
        private static readonly List<MailDto> SnapshotMails = new List<MailDto>();
        private static readonly HashSet<long> UnreadMailIds = new HashSet<long>();
        private static readonly HashSet<long> AliveMailIds = new HashSet<long>();

        private static long _snapshotUserId; // 스냅샷을 받은 유저(계정 전환 시 이전 계정 캐시를 쓰지 않도록)
        private static bool _notifiedUnclaimed; // 미수령 보상 알림음을 이미 울렸는지(점등 순간에만 1회)
        private static bool _fetching;

        /// <summary>스냅샷이 갱신돼 알림 상태가 바뀔 수 있을 때 발생한다(레드닷이 구독해 자동 재평가한다).</summary>
        public static event Action Changed;

        /// <summary>마지막으로 조회한 우편함 목록(읽기 전용).</summary>
        public static IReadOnlyList<MailDto> Mails => SnapshotMails;

        /// <summary>
        /// 수령하지 않은 보상이 남은 메일이 하나라도 있으면 true(레드닷 표시 조건).
        /// 판정: 첨부가 있고 · <c>claimed == 0</c> 이고 · 만료되지 않은 메일. 만료는 캐시된 <c>expiresAt</c>과
        /// 현재 시각으로 로컬 판정하므로, 재조회 없이도 만료된 메일은 알림에서 제외된다.
        /// 스냅샷이 현재 로그인 유저의 것이 아니면(로그아웃·계정 전환) 항상 false.
        /// </summary>
        public static bool HasUnclaimedReward
        {
            get
            {
                if (!Session.IsLoggedIn || _snapshotUserId != Session.UserId)
                {
                    return false;
                }
                return SnapshotHasUnclaimedReward();
            }
        }

        /// <summary>
        /// 스냅샷만 보고 미수령 보상 메일이 있는지 판정한다(유저 일치 검사 없음).
        /// <see cref="ApplySnapshot"/>이 <b>스냅샷 주인을 기록하기 전에</b> 점등 여부를 비교해야 하므로
        /// 그 검사를 뺀 계산을 따로 둔다(알림음이 로그인 직후 한 박자 늦게 울리는 것을 막는다).
        /// </summary>
        private static bool SnapshotHasUnclaimedReward()
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            foreach (var mail in SnapshotMails)
            {
                if (mail.claimed != 0 || mail.attachments == null || mail.attachments.Count == 0)
                {
                    continue;
                }
                if (mail.expiresAt != 0 && now > mail.expiresAt)
                {
                    continue; // 만료된 메일은 수령할 수 없으므로 알리지 않는다
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// 서버에서 우편함 목록을 다시 받아 스냅샷을 갱신한다(POST /api/game/mail/list).
        /// 비로그인·요청 중복(이미 조회 중)이면 아무것도 하지 않고 즉시 <paramref name="onDone"/>을 호출한다.
        /// 실패는 경고 로그만 남기고 이전 스냅샷을 유지한다(알림은 보조 정보라 사용자에게 오류를 노출하지 않는다).
        /// </summary>
        public static void Refresh(Action onDone = null)
        {
            if (_fetching || NetworkManager.Instance == null || !Session.IsLoggedIn)
            {
                onDone?.Invoke();
                return;
            }

            _fetching = true;
            var req = new AuthRequest { userId = Session.UserId, token = Session.Token };
            NetworkManager.Instance.PostToGame<MailListResponse>("/api/game/mail/list", req, resp =>
            {
                _fetching = false;
                ApplySnapshot(resp != null && resp.data != null ? resp.data.mails : null);
                onDone?.Invoke();
            }, error =>
            {
                _fetching = false;
                Debug.LogWarning($"[MailNotifier] 우편함 알림 갱신 실패: {error}");
                onDone?.Invoke();
            });
        }

        /// <summary>
        /// 이미 받아 둔 목록 응답으로 스냅샷을 교체한다(우편함 패널이 조회한 결과를 재사용해 중복 요청을 막는다).
        /// 이번 응답에서 미열람이던 메일 ID를 기억하고(서버는 이 조회로 읽음 처리했으므로 로컬 보존이 필요),
        /// 우편함에서 사라진 메일 ID는 정리한 뒤 <see cref="Changed"/>를 발생시킨다.
        /// </summary>
        public static void ApplySnapshot(List<MailDto> mails)
        {
            SnapshotMails.Clear();
            AliveMailIds.Clear();
            if (mails != null)
            {
                foreach (var mail in mails)
                {
                    if (mail == null)
                    {
                        continue;
                    }
                    SnapshotMails.Add(mail);
                    AliveMailIds.Add(mail.mailId);
                    if (mail.isRead == 0)
                    {
                        UnreadMailIds.Add(mail.mailId);
                    }
                }
            }
            UnreadMailIds.IntersectWith(AliveMailIds); // 삭제·만료 정리된 메일의 미열람 표시 제거

            // 레드닷이 **새로 점등되는 순간**(미수령 보상 메일이 없다가 생김)에만 알림음을 울린다
            // (사운드 정의서 §4.1 — 폴링마다 울리면 상주 창에서 청각 피로가 크다).
            // 로그인 직후 첫 스냅샷은 "이전 상태"가 없으므로 소리를 내지 않고 상태만 기록한다
            // (기록하지 않으면 다음 폴링에서 새로 도착한 것처럼 울린다).
            bool hasUnclaimed = SnapshotHasUnclaimedReward();
            bool hadPreviousSnapshot = _snapshotUserId == Session.UserId;
            if (hasUnclaimed && hadPreviousSnapshot && !_notifiedUnclaimed)
            {
                SoundManager.Sfx(SoundId.UiNotify);
            }
            _notifiedUnclaimed = hasUnclaimed;

            _snapshotUserId = Session.UserId;
            Changed?.Invoke();
        }

        /// <summary>해당 메일이 사용자가 우편함을 열기 전 시점 기준으로 미열람이었는지(봉투 아이콘 판정용).</summary>
        public static bool WasUnread(long mailId) => UnreadMailIds.Contains(mailId);

        /// <summary>사용자가 우편함을 실제로 열어 목록을 확인했음을 기록한다(이후 모든 메일을 열람으로 간주).</summary>
        public static void MarkAllViewed()
        {
            if (UnreadMailIds.Count > 0)
            {
                UnreadMailIds.Clear();
                Changed?.Invoke();
            }
        }

        /// <summary>캐시를 비운다(로그아웃·계정 전환).</summary>
        public static void Clear()
        {
            SnapshotMails.Clear();
            UnreadMailIds.Clear();
            AliveMailIds.Clear();
            _snapshotUserId = 0;
            _notifiedUnclaimed = false; // 계정이 바뀌면 다음 점등에서 다시 알린다
            _fetching = false;
            Changed?.Invoke();
        }
    }
}
