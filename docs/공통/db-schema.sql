-- =====================================================================
-- Taskbar Hero 모작 — 데이터베이스 DDL (MySQL 8.x / InnoDB / utf8mb4)
--
-- 정본(single source of truth): docs/공통/db-erd-통합.md 및 각 세부 기획서.
-- 본 파일은 그 ERD를 실제 생성 스크립트로 옮긴 것이며, 불일치 시 기획서를 따른다.
--
-- ⚠️ 개발 편의(파괴적): 본 스크립트는 재실행 시 각 테이블을 DROP 후 재생성한다.
--    실행하면 해당 DB의 기존 데이터가 모두 삭제된다. 개발/초기화 용도로만 쓰고 운영에서 실행하지 말 것.
--    FK 의존성 순서를 신경 쓰지 않도록 DB 구역마다 SET FOREIGN_KEY_CHECKS=0/1로 감싼다.
--
-- 공통 규약
--   * 시간 값은 모두 Unix timestamp(초) 단위의 BIGINT다(created_at/updated_at/expires_at 등).
--   * user_id는 AccountServer의 users.user_id와 "동일 식별자"다.
--     단, Account DB와 Game DB는 물리적으로 분리되므로 크로스 DB 외래키는 걸지 않는다
--     (Game DB 테이블의 user_id는 논리적 공유 키).
--   * 마스터(기획) 데이터(item_master, class_master 등)는 관계형 영속 테이블이 아니라
--     "클라이언트 번들 + 서버 인메모리 로드"이므로 DDL이 없다. 세이브 테이블의 코드 컬럼
--     (code, class_code, skill_code 등)이 마스터를 참조하지만 FK가 아닌 애플리케이션 검증으로 보장한다.
--   * 0/1 플래그·소규모 enum은 TINYINT, 코드/레벨/수량은 INT, 큰 수(재화·경험치·시각)는 BIGINT.
--   * InnoDB 사용(트랜잭션·행 잠금 기반 원자성 보장이 기획 전반의 전제).
-- =====================================================================


-- =====================================================================
-- 1. AccountServer — 계정/인증 DB
--    출처: 계정/로그인 기획서 3장, db-erd-통합.md 2장
-- =====================================================================
CREATE DATABASE IF NOT EXISTS taskbar_hero_account
    DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE taskbar_hero_account;

-- 재실행 시 초기화(FK 무시하고 DROP → 재생성). 구역 끝에서 다시 1로 되돌린다.
SET FOREIGN_KEY_CHECKS = 0;


-- 계정 1건. 이메일(로그인 ID) + BCrypt 비밀번호 해시.
DROP TABLE IF EXISTS users;
CREATE TABLE users (
    user_id     BIGINT       NOT NULL AUTO_INCREMENT COMMENT '계정 고유 ID(전 서버 공유 식별자)',
    email       VARCHAR(255) NOT NULL                COMMENT '로그인 ID(이메일). 계정 간 유니크',
    password    VARCHAR(255) NOT NULL                COMMENT 'BCrypt 해시(평문 저장 금지)',
    nickname    VARCHAR(50)  NOT NULL                COMMENT '표시 닉네임',
    created_at  BIGINT       NOT NULL                COMMENT '가입 시각(Unix ts, 초)',
    updated_at  BIGINT       NOT NULL                COMMENT '최종 수정 시각(Unix ts, 초)',
    PRIMARY KEY (user_id),
    UNIQUE KEY uq_users_email (email)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='계정(로그인 자격)';


-- 발급된 인증 토큰. 사용자당 1행(단일 세션) — 재로그인 시 UPSERT로 덮어써 기존 기기를 무효화한다.
-- 런타임 인증은 Redis(auth:token:{userId}) 대조가 담당하고, 본 테이블은 영속 백업이다.
DROP TABLE IF EXISTS user_auth_token;
CREATE TABLE user_auth_token (
    user_id     BIGINT       NOT NULL COMMENT 'users.user_id. 사용자당 1행(PK=단일 세션)',
    token       VARCHAR(255) NOT NULL COMMENT '발급 토큰(HMAC-SHA256)',
    created_at  BIGINT       NOT NULL COMMENT '발급 시각(Unix ts, 초)',
    expired_at  BIGINT       NOT NULL COMMENT '만료 시각(Unix ts, 초)',
    PRIMARY KEY (user_id),
    CONSTRAINT fk_token_user FOREIGN KEY (user_id)
        REFERENCES users (user_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='인증 토큰(사용자당 1행, 단일 세션)';

SET FOREIGN_KEY_CHECKS = 1;


-- =====================================================================
-- 2. GameServer — 세이브 데이터 DB
--    출처: 세이브 데이터 기획서 3장(정본), db-erd-통합.md 3장,
--          인벤토리/성장/메일/출석부/거래소 각 기획서
-- =====================================================================
CREATE DATABASE IF NOT EXISTS taskbar_hero_game
    DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE taskbar_hero_game;

-- 재실행 시 초기화(FK 무시하고 DROP → 재생성). 구역 끝에서 다시 1로 되돌린다.
SET FOREIGN_KEY_CHECKS = 0;


-- 계정(파티) 루트. 계정당 1행이며 진행도·인벤토리 용량 등 파티 공용 값을 담는다.
-- user_id는 Account DB users.user_id와 같은 값(크로스 DB FK 없음).
DROP TABLE IF EXISTS game_player;
CREATE TABLE game_player (
    user_id            BIGINT NOT NULL          COMMENT '계정 user_id(= Account DB users.user_id)',
    nickname           VARCHAR(50) NOT NULL     COMMENT '표시 닉네임(계정 생성 시 확정)',
    act                INT    NOT NULL DEFAULT 1 COMMENT '현재 Act(파티 공용)',
    stage              INT    NOT NULL DEFAULT 1 COMMENT '현재 스테이지(파티 공용)',
    difficulty         INT    NOT NULL DEFAULT 1 COMMENT '난이도 티어(1~2)',
    max_stage_cleared  INT    NOT NULL DEFAULT 0 COMMENT '최고 클리어 스테이지',
    inventory_capacity INT    NOT NULL          COMMENT '인벤토리 최대 용량(점유 slot 수). 골드로 확장',
    last_active_at     BIGINT NOT NULL          COMMENT '마지막 활동 시각(Unix ts). 5분 주기 갱신, 오프라인 보상 기준',
    created_at         BIGINT NOT NULL          COMMENT '생성 시각(Unix ts)',
    updated_at         BIGINT NOT NULL          COMMENT '최종 수정 시각(Unix ts)',
    PRIMARY KEY (user_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='계정/파티 루트(계정당 1행)';


-- 캐릭터 슬롯. 계정당 최대 3개(3인 파티). 직업(class_code)은 계정 내 중복 불가.
DROP TABLE IF EXISTS player_character;
CREATE TABLE player_character (
    user_id      BIGINT NOT NULL          COMMENT '계정 user_id',
    character_id INT    NOT NULL          COMMENT '캐릭터 슬롯(1~3)',
    class_code   INT    NOT NULL          COMMENT '직업(class_master 참조). 계정 내 중복 불가',
    level        INT    NOT NULL DEFAULT 1 COMMENT '캐릭터 레벨',
    exp          BIGINT NOT NULL DEFAULT 0 COMMENT '누적 경험치',
    PRIMARY KEY (user_id, character_id),
    UNIQUE KEY uq_char_class (user_id, class_code) COMMENT '한 계정에서 같은 직업 중복 생성 방지',
    CONSTRAINT fk_char_player FOREIGN KEY (user_id)
        REFERENCES game_player (user_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='캐릭터별 직업/레벨/경험치(계정당 3슬롯)';


-- 보유 아이템·재화 통합 테이블(계정 공유). row_type으로 아이템/재화를 구분한다.
--   * 장착은 별도 테이블 없이 equipped_character_id/equipped_slot로 이 행에 직접 표기한다.
--   * 재화(row_type=2)는 slot=NULL(용량 미집계), code=재화 item_code(골드=1), quantity=잔액.
--   * 재화의 (user_id, code) "계정당 종류별 1행" 유일성은 MySQL 부분 유니크 인덱스 미지원으로
--     인덱스가 아닌 애플리케이션(서버)이 보장한다. 아이템은 스택 분할로 (user_id, code) 중복 가능.
DROP TABLE IF EXISTS player_item;
CREATE TABLE player_item (
    item_id               BIGINT NOT NULL AUTO_INCREMENT COMMENT '아이템 행 고유 ID(개체 식별자)',
    user_id               BIGINT NOT NULL          COMMENT '계정 user_id',
    row_type              TINYINT NOT NULL          COMMENT '1:아이템 2:재화',
    code                  INT    NOT NULL          COMMENT 'item_master.item_code(재화 item_type=3 포함, 골드=1)',
    quantity              BIGINT NOT NULL DEFAULT 1 COMMENT '수량(아이템) / 잔액(재화). 재화가 커 BIGINT',
    slot                  INT    NULL              COMMENT '인벤토리 배치 칸(0-based). 재화는 NULL(용량 미집계)',
    enhance_level         INT    NOT NULL DEFAULT 0 COMMENT '장비 강화 단계. 재화/비장비는 0',
    equipped_character_id INT    NULL              COMMENT '장착 캐릭터(1~3). NULL=미장착/재화',
    equipped_slot         INT    NULL              COMMENT '장착 슬롯(equip_slot_master). NULL=미장착/재화',
    acquired_at           BIGINT NOT NULL          COMMENT '획득 시각(Unix ts)',
    PRIMARY KEY (item_id),
    KEY idx_item_user (user_id) COMMENT '세이브 로드 시 계정 아이템 조회',
    -- 한 인벤토리 칸에는 한 행만. slot이 NULL인 재화 행은 유니크 대상에서 제외(MySQL은 NULL 다중 허용).
    UNIQUE KEY uq_item_slot (user_id, slot),
    -- 한 캐릭터-장착슬롯에 아이템 하나. 미장착/재화는 NULL이라 무제한 공존(MySQL NULL 다중 허용).
    UNIQUE KEY uq_item_equip (user_id, equipped_character_id, equipped_slot),
    CONSTRAINT fk_item_player FOREIGN KEY (user_id)
        REFERENCES game_player (user_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='보유 아이템/재화(계정 공유, 장착 상태 내장)';


-- 캐릭터별 스킬 레벨·액티브 장착 여부.
DROP TABLE IF EXISTS player_skill;
CREATE TABLE player_skill (
    user_id      BIGINT  NOT NULL          COMMENT '계정 user_id',
    character_id INT     NOT NULL          COMMENT '캐릭터 슬롯(1~3)',
    skill_code   INT     NOT NULL          COMMENT '스킬(skill_master 참조)',
    level        INT     NOT NULL DEFAULT 0 COMMENT '스킬 레벨(0=미습득)',
    equipped     TINYINT NOT NULL DEFAULT 0 COMMENT '액티브 장착 여부(0/1). 캐릭터당 최대 2개',
    PRIMARY KEY (user_id, character_id, skill_code),
    CONSTRAINT fk_skill_player FOREIGN KEY (user_id)
        REFERENCES game_player (user_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='캐릭터별 스킬(레벨/액티브 장착)';


-- 룬(Rune Tree). 계정 공용이라 character_id를 두지 않는다.
DROP TABLE IF EXISTS player_rune;
CREATE TABLE player_rune (
    user_id   BIGINT NOT NULL          COMMENT '계정 user_id',
    rune_code INT    NOT NULL          COMMENT '룬(rune_master 참조)',
    level     INT    NOT NULL DEFAULT 0 COMMENT '룬 레벨',
    PRIMARY KEY (user_id, rune_code),
    CONSTRAINT fk_rune_player FOREIGN KEY (user_id)
        REFERENCES game_player (user_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='룬 트리(계정 공용)';


-- 큐브(Hero-dric Cube) 성장 상태. 계정당 1행.
DROP TABLE IF EXISTS player_cube;
CREATE TABLE player_cube (
    user_id    BIGINT NOT NULL          COMMENT '계정 user_id',
    cube_level INT    NOT NULL DEFAULT 1 COMMENT '큐브 레벨(cube_master 참조)',
    cube_exp   BIGINT NOT NULL DEFAULT 0 COMMENT '큐브 누적 경험치',
    PRIMARY KEY (user_id),
    CONSTRAINT fk_cube_player FOREIGN KEY (user_id)
        REFERENCES game_player (user_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='큐브 성장 상태(계정당 1행)';


-- 우편함 메일 1건(계정 소속). 첨부는 player_mail_reward에 0~N개.
DROP TABLE IF EXISTS player_mail;
CREATE TABLE player_mail (
    mail_id     BIGINT       NOT NULL AUTO_INCREMENT COMMENT '메일 고유 ID',
    user_id     BIGINT       NOT NULL                COMMENT '수신 계정 user_id',
    category    TINYINT      NOT NULL                COMMENT '1:운영 2:거래 3:출석 4:시스템',
    title       VARCHAR(255) NOT NULL                COMMENT '메일 제목',
    body        VARCHAR(2000) NOT NULL DEFAULT ''     COMMENT '메일 본문',
    is_read     TINYINT      NOT NULL DEFAULT 0       COMMENT '열람 여부(0/1)',
    claimed     TINYINT      NOT NULL DEFAULT 0       COMMENT '첨부 수령 여부(0/1)',
    created_at  BIGINT       NOT NULL                COMMENT '발급 시각(Unix ts)',
    expires_at  BIGINT       NOT NULL DEFAULT 0       COMMENT '만료 시각(Unix ts). 0이면 무기한',
    claimed_at  BIGINT       NOT NULL DEFAULT 0       COMMENT '수령 시각(Unix ts). 미수령 0',
    PRIMARY KEY (mail_id),
    KEY idx_mail_user (user_id) COMMENT '계정 우편함 조회',
    CONSTRAINT fk_mail_player FOREIGN KEY (user_id)
        REFERENCES game_player (user_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='우편함 메일(계정 소속)';


-- 메일 첨부(재화·아이템). 메일당 0~N개. reward_type/reward_code 규약은 드롭·출석부와 공유.
DROP TABLE IF EXISTS player_mail_reward;
CREATE TABLE player_mail_reward (
    mail_id     BIGINT  NOT NULL          COMMENT '소속 메일(player_mail.mail_id)',
    seq         INT     NOT NULL          COMMENT '메일 내 첨부 번호(1부터)',
    reward_type TINYINT NOT NULL          COMMENT '1:골드 2:아이템 3:재료',
    reward_code INT     NOT NULL DEFAULT 0 COMMENT '대상 코드(item_master). 골드면 0',
    quantity    INT     NOT NULL          COMMENT '지급 수량',
    PRIMARY KEY (mail_id, seq),
    CONSTRAINT fk_mailreward_mail FOREIGN KEY (mail_id)
        REFERENCES player_mail (mail_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='메일 첨부 보상(0~N)';


-- 출석 기록. 행이 존재하면 그날(KST) 출석 보상을 수령한 것. 하루 1회 제한을 PK가 보장.
DROP TABLE IF EXISTS player_attendance;
CREATE TABLE player_attendance (
    user_id     BIGINT NOT NULL COMMENT '계정 user_id',
    attend_date INT    NOT NULL COMMENT '출석 일자 YYYYMMDD(서버 KST 기준)',
    claimed_at  BIGINT NOT NULL COMMENT '출석/보상 메일 발급 시각(Unix ts)',
    PRIMARY KEY (user_id, attend_date),
    CONSTRAINT fk_attend_player FOREIGN KEY (user_id)
        REFERENCES game_player (user_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='출석 기록(일자별 1행, 하루 1회 보장)';


-- 거래소 등록(전역). 에스크로 방식 — 등록 시 아이템을 player_item에서 빼 여기 스냅샷으로 보관한다.
--   status=1(판매중)만 목록/구매 대상. 판매/취소/만료는 status로 닫고 이력 보관.
DROP TABLE IF EXISTS trade_listing;
CREATE TABLE trade_listing (
    listing_id     BIGINT  NOT NULL AUTO_INCREMENT COMMENT '거래 등록 고유 ID',
    seller_user_id BIGINT  NOT NULL          COMMENT '판매자 user_id',
    item_code      INT     NOT NULL          COMMENT '판매 아이템(item_master). 에스크로 스냅샷',
    enhance_level  INT     NOT NULL DEFAULT 0 COMMENT '장비 강화 단계 스냅샷',
    quantity       INT     NOT NULL DEFAULT 1 COMMENT '수량(장비 1, 스택형은 전체 수량)',
    price          BIGINT  NOT NULL          COMMENT '구매가(골드). 기준가 ±20% 범위',
    status         TINYINT NOT NULL DEFAULT 1 COMMENT '1:판매중 2:판매완료 3:취소(만료 포함)',
    buyer_user_id  BIGINT  NOT NULL DEFAULT 0 COMMENT '구매자 user_id. 미판매 0(FK 아님)',
    created_at     BIGINT  NOT NULL          COMMENT '등록 시각(Unix ts)',
    expires_at     BIGINT  NOT NULL          COMMENT '만료 시각(= created_at + 3일)',
    closed_at      BIGINT  NOT NULL DEFAULT 0 COMMENT '판매/취소 시각(Unix ts). 미완료 0',
    PRIMARY KEY (listing_id),
    KEY idx_trade_seller (seller_user_id)      COMMENT '내 판매 목록 조회',
    KEY idx_trade_browse (status, item_code)   COMMENT '판매중 목록·아이템 코드 검색',
    CONSTRAINT fk_trade_seller FOREIGN KEY (seller_user_id)
        REFERENCES game_player (user_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='거래소 등록(전역, 에스크로)';

SET FOREIGN_KEY_CHECKS = 1;
