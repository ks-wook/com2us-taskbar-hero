-- =====================================================================
-- Taskbar Hero 모작 — 메일(우편함) 기능 테스트용 시드 데이터 (개발 전용)
--
-- 대상 테이블: taskbar_hero_game.player_mail · player_mail_reward
--   스키마 정본은 docs/공통/db-schema.sql, 동작 규약은 docs/세부/mail-기획서.md.
--
-- 사용법
--   docker exec -i taskbar-hero-mysql mysql -uroot -ptaskbar_hero_dev < docs/공통/mail-테스트-데이터.sql
--   대상 계정을 바꾸려면 아래 @uid 값을 수정한다(game_player에 존재하는 user_id여야 한다 — FK).
--
-- ⚠️ 개발 편의(파괴적): 재실행 시 @uid 계정의 기존 메일을 전부 삭제하고 다시 넣는다.
--    첨부(player_mail_reward)는 FK CASCADE로 함께 삭제된다.
--
-- 보관 GC 주의(mail 기획서 6.5)
--   GameServer의 MailGcBatchService가 created_at 기준 7일이 지난 메일을 주기적으로 삭제한다.
--   그래서 "만료된 메일" 케이스도 created_at은 7일 이내로 두고 expires_at만 과거로 둔다
--   (created_at을 7일 이전으로 두면 배치가 지워버려 테스트가 불가능하다).
--
-- 코드 규약
--   category    1:운영 2:거래 3:출석 4:시스템
--   reward_type 1:골드(reward_code=0) 2:아이템 3:재료
--   사용 코드: 41001 강화석(stack 999) · 41002 상급 강화석(999) · 41010 마력의 정수(999)
--              41020 용의 비늘(99) · 33021 강철 투구(장비) · 33051 코스믹 투구(장비)
-- =====================================================================

SET NAMES utf8mb4;
USE taskbar_hero_game;

SET @uid = 2;
SET @now = UNIX_TIMESTAMP();
SET @day = 86400;

-- 재실행 대비 초기화(해당 계정 메일만).
DELETE FROM player_mail WHERE user_id = @uid;


-- 1) 운영 · 골드 단일 첨부 · 미열람 미수령 — 기본 수령 경로
INSERT INTO player_mail (user_id, category, title, body, is_read, claimed, created_at, expires_at, claimed_at)
VALUES (@uid, 1, '신규 모험가 환영 선물', '타이니 히어로 세계에 오신 것을 환영합니다. 골드를 받아 모험을 시작하세요.',
        0, 0, @now - 3600, @now + 7 * @day, 0);
INSERT INTO player_mail_reward (mail_id, seq, reward_type, reward_code, quantity)
VALUES (LAST_INSERT_ID(), 1, 1, 0, 50000);

-- 2) 출석 · 재료 단일 첨부 · 미열람 미수령 — 출석 보상 발급 메일과 동일 형태
INSERT INTO player_mail (user_id, category, title, body, is_read, claimed, created_at, expires_at, claimed_at)
VALUES (@uid, 3, '출석 보상', '21일차 출석 보상이 도착했습니다.',
        0, 0, @now - 7200, @now + 7 * @day, 0);
INSERT INTO player_mail_reward (mail_id, seq, reward_type, reward_code, quantity)
VALUES (LAST_INSERT_ID(), 1, 3, 41010, 3);

-- 3) 거래 · 골드 첨부 · 미열람 미수령 — 거래소 판매 대금 수령 형태
INSERT INTO player_mail (user_id, category, title, body, is_read, claimed, created_at, expires_at, claimed_at)
VALUES (@uid, 2, '거래소 판매 대금', '등록하신 아이템이 판매되어 대금을 보내드립니다.',
        0, 0, @now - 10800, @now + 5 * @day, 0);
INSERT INTO player_mail_reward (mail_id, seq, reward_type, reward_code, quantity)
VALUES (LAST_INSERT_ID(), 1, 1, 0, 12000);

-- 4) 운영 · 다중 첨부(골드 + 장비 + 재료) · 미열람 미수령 — 첨부 여러 건 지급/UI 확인
INSERT INTO player_mail (user_id, category, title, body, is_read, claimed, created_at, expires_at, claimed_at)
VALUES (@uid, 1, '이벤트 보상 꾸러미', '여름 이벤트 참여 보상입니다. 첨부를 모두 수령하세요.',
        0, 0, @now - 14400, @now + 7 * @day, 0);
SET @m4 = LAST_INSERT_ID();
INSERT INTO player_mail_reward (mail_id, seq, reward_type, reward_code, quantity) VALUES
    (@m4, 1, 1, 0,     30000),
    (@m4, 2, 2, 33021, 1),
    (@m4, 3, 3, 41001, 10);

-- 5) 시스템 · 첨부 없음 · 미열람 — 공지성 메일(수령 시 빈 지급)
INSERT INTO player_mail (user_id, category, title, body, is_read, claimed, created_at, expires_at, claimed_at)
VALUES (@uid, 4, '서버 점검 안내', '금주 목요일 오전 2시부터 4시까지 정기 점검이 진행됩니다. 첨부된 보상은 없습니다.',
        0, 0, @now - 18000, @now + 7 * @day, 0);

-- 6) 운영 · 이미 수령함(claimed=1) — 재수령 시 MailAlreadyClaimed(8002) 확인용
INSERT INTO player_mail (user_id, category, title, body, is_read, claimed, created_at, expires_at, claimed_at)
VALUES (@uid, 1, '이미 수령한 보상', '이 메일의 첨부는 이미 수령했습니다.',
        1, 1, @now - 2 * @day, @now + 5 * @day, @now - @day);
INSERT INTO player_mail_reward (mail_id, seq, reward_type, reward_code, quantity)
VALUES (LAST_INSERT_ID(), 1, 1, 0, 1000);

-- 7) 운영 · 만료됨(expires_at 과거, 미수령) — 수령 시 MailExpired(8003) 확인용
--    created_at은 6일 전 = 보관 7일 이내라 GC 대상이 아니다.
INSERT INTO player_mail (user_id, category, title, body, is_read, claimed, created_at, expires_at, claimed_at)
VALUES (@uid, 1, '기간 만료 보상', '수령 기간이 지난 보상입니다. 더 이상 받을 수 없습니다.',
        1, 0, @now - 6 * @day, @now - 3600, 0);
INSERT INTO player_mail_reward (mail_id, seq, reward_type, reward_code, quantity)
VALUES (LAST_INSERT_ID(), 1, 3, 41002, 5);

-- 8) 시스템 · 무기한 보관(expires_at=0) · 스택 상한 초과 수량 — 스택 분할 적재 확인용
--    용의 비늘 stack_max=99 → 150개는 99 + 51 두 칸으로 나뉘어 적재되어야 한다.
INSERT INTO player_mail (user_id, category, title, body, is_read, claimed, created_at, expires_at, claimed_at)
VALUES (@uid, 4, '무기한 보관 보상', '만료 없이 보관되는 보상입니다. 재료가 스택 상한을 넘게 들어 있습니다.',
        0, 0, @now - 21600, 0, 0);
INSERT INTO player_mail_reward (mail_id, seq, reward_type, reward_code, quantity)
VALUES (LAST_INSERT_ID(), 1, 3, 41020, 150);

-- 9) 운영 · 열람했지만 미수령 · 장비 첨부 — isRead 표시와 수령 분리 확인용
INSERT INTO player_mail (user_id, category, title, body, is_read, claimed, created_at, expires_at, claimed_at)
VALUES (@uid, 1, '읽었지만 받지 않은 보상', '열람 여부와 첨부 수령 여부는 별개입니다.',
        1, 0, @now - 25200, @now + 3 * @day, 0);
INSERT INTO player_mail_reward (mail_id, seq, reward_type, reward_code, quantity)
VALUES (LAST_INSERT_ID(), 1, 2, 33051, 1);


-- 확인용 요약.
SELECT m.mail_id, m.category, m.title, m.is_read, m.claimed,
       CASE WHEN m.expires_at = 0 THEN '무기한'
            WHEN m.expires_at < @now THEN '만료됨'
            ELSE CONCAT(FLOOR((m.expires_at - @now) / @day), '일 남음') END AS 만료,
       COUNT(r.seq) AS 첨부수
FROM player_mail m
LEFT JOIN player_mail_reward r ON r.mail_id = m.mail_id
WHERE m.user_id = @uid
GROUP BY m.mail_id
ORDER BY m.mail_id;
