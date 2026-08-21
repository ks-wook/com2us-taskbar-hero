-- ═══════════════════════════════════════════════════════════════════════════
--  logdb — 게임 로그 저장소 스키마(TimescaleDB / PostgreSQL)
--
--  정본: docs/공통/로그-이벤트-정의.md 8장(ERD·인덱스·보존)·9장(fluentd 적재).
--  적재는 fluentd(out_sql)만 한다 — **게임 서버는 이 DB에 접속하지 않는다**(3장 원칙 4).
--
--  적용
--    · docker-compose의 `timescaledb` 서비스가 **첫 기동 시**(데이터 볼륨이 비어 있을 때)
--      /docker-entrypoint-initdb.d 에서 이 파일을 자동 실행한다.
--    · 이미 뜬 인스턴스에 다시 적용하려면:
--        docker exec -i taskbar-hero-timescaledb psql -U fluentd -d logdb < docs/공통/logdb-schema.sql
--      (CREATE TABLE 은 IF NOT EXISTS 가 아니므로 기존 테이블이 있으면 그 문장에서 멈춘다 —
--       완전 초기화는 `docker compose down -v` 후 재기동이 가장 확실하다.)
--
--  설계 규격(8.1) — 이 파일 전체가 공유하는 규칙
--    · 액션 로그는 **PK·FK를 두지 않는다.** append-only 사실 기록이라 갱신·삭제 대상 행을
--      지목할 일이 없고, 자연 키를 잡으면 같은 초에 두 번 일어난 사건이 중복 키로 그 청크
--      전체 INSERT를 실패시킨다.
--    · `timestamp`(PostgreSQL 예약어라 큰따옴표로 감싼다)가 모든 테이블의 **분할 축**이다.
--    · `tag` 컬럼을 두지 않는다(테이블이 곧 이벤트). 예외는 unknown_event_logs 하나.
--    · 히스토리 테이블만 **자연 키 PK**를 갖는다 — 덮어쓰기가 정상 동작이기 때문(7장).
--    · NULL 을 허용하는 컬럼은 **방출 코드가 실제로 생략할 수 있는 것들뿐**이다
--      (`req_id`·`balance_after`·`item_flow.item_id`·`trade_close.buyer_uid`·`mail_id`).
--      나머지를 NOT NULL 로 두는 것이 "서버가 필드를 잘못 냈다"를 적재 단계에서 드러내는 장치다(9.2).
-- ═══════════════════════════════════════════════════════════════════════════

CREATE EXTENSION IF NOT EXISTS timescaledb;

-- ═══════════════════════════════════════════════════════════════════════════
--  1. 액션 로그 — 세션 / 캐릭터 (ERD 8.2)
-- ═══════════════════════════════════════════════════════════════════════════

-- 세션 개시 테이블. 계정 로그가 없으므로 접속·리텐션 분석이 전부 여기서 나온다(5.1·5.2).
CREATE TABLE save_load_logs (
    "timestamp"         timestamptz NOT NULL,
    uid                 bigint      NOT NULL,
    req_id              text,
    is_new              boolean     NOT NULL,
    offline_elapsed_sec int         NOT NULL
);
SELECT create_hypertable('save_load_logs', 'timestamp', chunk_time_interval => INTERVAL '7 days');
CREATE INDEX ON save_load_logs (uid, "timestamp" DESC);

CREATE TABLE player_create_logs (
    "timestamp" timestamptz NOT NULL,
    uid         bigint      NOT NULL,
    req_id      text,
    class_code  smallint    NOT NULL,
    gender      smallint    NOT NULL
);
SELECT create_hypertable('player_create_logs', 'timestamp', chunk_time_interval => INTERVAL '30 days');
CREATE INDEX ON player_create_logs (uid, "timestamp" DESC);

-- ═══════════════════════════════════════════════════════════════════════════
--  2. 액션 로그 — 스테이지 / 오프라인 (ERD 8.3)
-- ═══════════════════════════════════════════════════════════════════════════

-- 실질 난이도(실패/진입)의 분모이자, 보고 없이 사라진 판(이탈)을 세는 기준이다(5.3).
CREATE TABLE stage_enter_logs (
    "timestamp" timestamptz NOT NULL,
    uid         bigint      NOT NULL,
    req_id      text,
    error_code  int         NOT NULL,  -- 0=성공. 진입 거부(잠긴 스테이지 등)도 남긴다
    stage_id    int         NOT NULL,
    act         smallint    NOT NULL,
    difficulty  smallint    NOT NULL,
    stage       smallint    NOT NULL
);
SELECT create_hypertable('stage_enter_logs', 'timestamp', chunk_time_interval => INTERVAL '1 day');
CREATE INDEX ON stage_enter_logs (uid, "timestamp" DESC);
CREATE INDEX ON stage_enter_logs (stage_id, "timestamp" DESC);

-- 이 체계에서 가장 빈번한 이벤트다(10장).
CREATE TABLE stage_clear_logs (
    "timestamp"       timestamptz NOT NULL,
    uid               bigint      NOT NULL,
    req_id            text,
    stage_id          int         NOT NULL,
    act               smallint    NOT NULL,
    difficulty        smallint    NOT NULL,
    stage             smallint    NOT NULL,
    gold              bigint      NOT NULL,
    exp               bigint      NOT NULL,
    is_first_clear    boolean     NOT NULL,
    max_stage_cleared int         NOT NULL
);
SELECT create_hypertable('stage_clear_logs', 'timestamp', chunk_time_interval => INTERVAL '1 day');
CREATE INDEX ON stage_clear_logs (uid, "timestamp" DESC);
CREATE INDEX ON stage_clear_logs (stage_id, "timestamp" DESC);

CREATE TABLE stage_fail_logs (
    "timestamp"             timestamptz NOT NULL,
    uid                     bigint      NOT NULL,
    req_id                  text,
    stage_id                int         NOT NULL,
    act                     smallint    NOT NULL,
    difficulty              smallint    NOT NULL,
    stage                   smallint    NOT NULL,
    elapsed_ms              int         NOT NULL,
    remaining_monster_count smallint    NOT NULL,
    reached_boss            boolean     NOT NULL
);
SELECT create_hypertable('stage_fail_logs', 'timestamp', chunk_time_interval => INTERVAL '7 days');
CREATE INDEX ON stage_fail_logs (uid, "timestamp" DESC);
CREATE INDEX ON stage_fail_logs (stage_id, "timestamp" DESC);

-- 스테이지·오프라인이 공통으로 내며 source 로 갈린다(5.3).
CREATE TABLE character_levelup_logs (
    "timestamp"  timestamptz NOT NULL,
    uid          bigint      NOT NULL,
    req_id       text,
    character_id bigint      NOT NULL,
    class_code   smallint    NOT NULL,
    from_level   int         NOT NULL,
    to_level     int         NOT NULL,
    source       text        NOT NULL   -- stage / offline
);
SELECT create_hypertable('character_levelup_logs', 'timestamp', chunk_time_interval => INTERVAL '7 days');
CREATE INDEX ON character_levelup_logs (uid, "timestamp" DESC);

-- 골드 유입은 원장에도 남지만 **상한에 걸렸는지는 원장이 모른다** — 그 한 컬럼이 이 테이블의 존재 이유다(5.4).
CREATE TABLE offline_claim_logs (
    "timestamp"   timestamptz NOT NULL,
    uid           bigint      NOT NULL,
    req_id        text,
    elapsed_sec   int         NOT NULL,
    effective_sec int         NOT NULL,
    is_capped     boolean     NOT NULL,
    gold          bigint      NOT NULL,
    exp           bigint      NOT NULL
);
SELECT create_hypertable('offline_claim_logs', 'timestamp', chunk_time_interval => INTERVAL '7 days');
CREATE INDEX ON offline_claim_logs (uid, "timestamp" DESC);

-- ═══════════════════════════════════════════════════════════════════════════
--  3. 액션 로그 — 아이템 / 원장 (ERD 8.4)
-- ═══════════════════════════════════════════════════════════════════════════

-- 재화 부족 거부까지 남긴다 — 어느 단계에서 골드가 막히는지가 강화 곡선 조정의 근거다(5.5).
CREATE TABLE item_enhance_logs (
    "timestamp" timestamptz NOT NULL,
    uid         bigint      NOT NULL,
    req_id      text,
    error_code  int         NOT NULL,
    item_id     bigint      NOT NULL,
    item_code   int         NOT NULL,
    grade       smallint    NOT NULL,
    from_level  smallint    NOT NULL,
    to_level    smallint    NOT NULL,
    cost        bigint      NOT NULL
);
SELECT create_hypertable('item_enhance_logs', 'timestamp', chunk_time_interval => INTERVAL '7 days');
CREATE INDEX ON item_enhance_logs (uid, "timestamp" DESC);
CREATE INDEX ON item_enhance_logs (item_code, "timestamp" DESC);

-- 재화가 움직인 모든 지점(6.1). balance_after 는 burn 행에서 null — 소각은 어느 계정의
-- 잔액도 바꾸지 않으므로 숫자를 적으면 같은 변동을 두 번 센 것처럼 읽힌다.
CREATE TABLE currency_flow_logs (
    "timestamp"    timestamptz NOT NULL,
    uid            bigint      NOT NULL,
    req_id         text,
    currency_code  int         NOT NULL,
    direction      text        NOT NULL,  -- gain / spend / burn
    amount         bigint      NOT NULL,  -- 절대값
    balance_after  bigint,                -- burn 은 null
    source         text        NOT NULL,  -- 6.1 고정 집합
    ref_id         bigint      NOT NULL   -- 해석은 source 가 정한다
);
SELECT create_hypertable('currency_flow_logs', 'timestamp', chunk_time_interval => INTERVAL '1 day');
CREATE INDEX ON currency_flow_logs (uid, "timestamp" DESC);
CREATE INDEX ON currency_flow_logs (source, "timestamp" DESC);

-- 아이템이 움직인 모든 지점(6.2). 큐브·소모품처럼 도메인 테이블이 없는 기능은 이 행이 유일한 기록이다.
CREATE TABLE item_flow_logs (
    "timestamp" timestamptz NOT NULL,
    uid         bigint      NOT NULL,
    req_id      text,
    item_code   int         NOT NULL,
    item_type   smallint    NOT NULL,  -- 1=장비 2=재료 4=소모품
    grade       smallint    NOT NULL,
    item_id     bigint,                -- 개체가 유일한 경우(장비)만
    delta       int         NOT NULL,  -- +획득 / -소실
    reason      text        NOT NULL,  -- 6.2 고정 집합
    ref_id      bigint      NOT NULL
);
SELECT create_hypertable('item_flow_logs', 'timestamp', chunk_time_interval => INTERVAL '1 day');
CREATE INDEX ON item_flow_logs (uid, "timestamp" DESC);
CREATE INDEX ON item_flow_logs (item_code, "timestamp" DESC);

-- ═══════════════════════════════════════════════════════════════════════════
--  4. 액션 로그 — 가챠 / 거래소 / 메일 / 출석 (ERD 8.5)
-- ═══════════════════════════════════════════════════════════════════════════

-- 뽑기 결과 1개당 1행. 요청 단위 값(1연/10연·비용)은 같은 pull_id 의 행 수와 원장에서 파생한다(5.6).
CREATE TABLE gacha_pull_item_logs (
    "timestamp"   timestamptz NOT NULL,
    uid           bigint      NOT NULL,
    req_id        text,
    pull_id       bigint      NOT NULL,
    seq           smallint    NOT NULL,
    gacha_code    int         NOT NULL,
    item_code     int         NOT NULL,
    grade         smallint    NOT NULL,
    is_pity       boolean     NOT NULL,
    is_guaranteed boolean     NOT NULL
);
SELECT create_hypertable('gacha_pull_item_logs', 'timestamp', chunk_time_interval => INTERVAL '7 days');
CREATE INDEX ON gacha_pull_item_logs (uid, "timestamp" DESC);
CREATE INDEX ON gacha_pull_item_logs (gacha_code, "timestamp" DESC);

CREATE TABLE trade_register_logs (
    "timestamp"   timestamptz NOT NULL,
    uid           bigint      NOT NULL,
    req_id        text,
    error_code    int         NOT NULL,  -- 가격 범위 거부까지 남긴다
    listing_id    bigint      NOT NULL,
    item_code     int         NOT NULL,
    grade         smallint    NOT NULL,
    enhance_level smallint    NOT NULL,
    price         bigint      NOT NULL,
    base_price    bigint      NOT NULL   -- 마스터 기준가
);
SELECT create_hypertable('trade_register_logs', 'timestamp', chunk_time_interval => INTERVAL '7 days');
CREATE INDEX ON trade_register_logs (uid, "timestamp" DESC);
CREATE INDEX ON trade_register_logs (item_code, "timestamp" DESC);

-- uid 는 항상 **판매자**다. 구매자는 buyer_uid 에 담아 두 계정 축을 섞지 않는다(8.5).
-- 인덱스가 셋인 유일한 테이블이다(8.9) — listing_id 는 등록 행과 잇는 조인(시세비·체결 시간) 축이다.
CREATE TABLE trade_close_logs (
    "timestamp"      timestamptz NOT NULL,
    uid              bigint      NOT NULL,
    req_id           text,
    error_code       int         NOT NULL,
    listing_id       bigint      NOT NULL,
    item_code        int         NOT NULL,
    grade            smallint    NOT NULL,
    outcome          text        NOT NULL,  -- buy / cancel / expire
    price            bigint      NOT NULL,
    fee              bigint      NOT NULL,  -- buy 의 20% 소각, 그 외 0
    seller_proceeds  bigint      NOT NULL,  -- buy 가 아니면 0
    buyer_uid        bigint,                -- buy 만
    listed_sec       int         NOT NULL,
    mail_id          bigint                 -- buy=대금 메일 / expire=반송 메일 / cancel=null
);
SELECT create_hypertable('trade_close_logs', 'timestamp', chunk_time_interval => INTERVAL '7 days');
CREATE INDEX ON trade_close_logs (uid, "timestamp" DESC);
CREATE INDEX ON trade_close_logs (item_code, "timestamp" DESC);
CREATE INDEX ON trade_close_logs (listing_id);

-- 메일이 이 게임의 재화 지급 관문이라, 발급 − 수령의 차액이 곧 아직 경제에 풀리지 않은 재화다(5.8).
-- 배치(거래 만료 반송 등)가 발급한 행은 req_id 가 없다.
CREATE TABLE mail_issue_logs (
    "timestamp"   timestamptz NOT NULL,
    uid           bigint      NOT NULL,
    req_id        text,
    mail_id       bigint      NOT NULL,
    template_code int         NOT NULL,
    category      smallint    NOT NULL,
    source        text        NOT NULL,  -- 5.8 고정 집합
    gold          bigint      NOT NULL,
    item_count    smallint    NOT NULL
);
SELECT create_hypertable('mail_issue_logs', 'timestamp', chunk_time_interval => INTERVAL '7 days');
CREATE INDEX ON mail_issue_logs (uid, "timestamp" DESC);

-- mail.issue 와 겹치지만 이 도메인의 질문인 일차별 이탈의 축(day)이 메일 로그에는 없어 따로 둔다(5.9).
CREATE TABLE attendance_claim_logs (
    "timestamp"  timestamptz NOT NULL,
    uid          bigint      NOT NULL,
    req_id       text,
    attend_date  date        NOT NULL,
    day          smallint    NOT NULL,   -- 출석 일차
    reward_type  smallint    NOT NULL,
    reward_code  int         NOT NULL,
    quantity     bigint      NOT NULL,
    mail_id      bigint      NOT NULL
);
SELECT create_hypertable('attendance_claim_logs', 'timestamp', chunk_time_interval => INTERVAL '7 days');
CREATE INDEX ON attendance_claim_logs (uid, "timestamp" DESC);

-- ═══════════════════════════════════════════════════════════════════════════
--  5. 액션 로그 — 보스러시 / 시스템 (ERD 8.6)
--
--  batch_run_logs·master_load_logs·server_lifecycle_logs 는 **uid·req_id 가 없는**
--  시스템 테이블이다(8.1). 방출자가 GameServer 하나라 server 구분 컬럼도 두지 않는다(5.11).
-- ═══════════════════════════════════════════════════════════════════════════

CREATE TABLE bossrush_enter_logs (
    "timestamp" timestamptz NOT NULL,
    uid         bigint      NOT NULL,
    req_id      text,
    error_code  int         NOT NULL,
    run_id      bigint      NOT NULL,
    season_id   int         NOT NULL
);
SELECT create_hypertable('bossrush_enter_logs', 'timestamp', chunk_time_interval => INTERVAL '7 days');
CREATE INDEX ON bossrush_enter_logs (uid, "timestamp" DESC);

-- 라운드 5개 고정이라 배열이 아니라 컬럼으로 푼다(8.6).
CREATE TABLE bossrush_clear_logs (
    "timestamp"    timestamptz NOT NULL,
    uid            bigint      NOT NULL,
    req_id         text,
    error_code     int         NOT NULL,
    run_id         bigint      NOT NULL,
    season_id      int         NOT NULL,
    clear_ms       int         NOT NULL,
    round1_ms      int         NOT NULL,
    round2_ms      int         NOT NULL,
    round3_ms      int         NOT NULL,
    round4_ms      int         NOT NULL,
    round5_ms      int         NOT NULL,
    is_new_record  boolean     NOT NULL,
    best_clear_ms  int         NOT NULL,
    rank_at_report int         NOT NULL
);
SELECT create_hypertable('bossrush_clear_logs', 'timestamp', chunk_time_interval => INTERVAL '7 days');
CREATE INDEX ON bossrush_clear_logs (uid, "timestamp" DESC);

-- 배치 종류를 테이블이 아니라 batch_key 로 구분한다 — 컬럼 구조가 배치마다 같기 때문(5.11).
CREATE TABLE batch_run_logs (
    "timestamp" timestamptz NOT NULL,
    batch_key   text        NOT NULL,
    processed   int         NOT NULL,
    skipped     int         NOT NULL,
    failed      int         NOT NULL,
    elapsed_ms  int         NOT NULL
);
SELECT create_hypertable('batch_run_logs', 'timestamp', chunk_time_interval => INTERVAL '30 days');

CREATE TABLE master_load_logs (
    "timestamp" timestamptz NOT NULL,
    table_count int         NOT NULL,
    row_count   int         NOT NULL,
    elapsed_ms  int         NOT NULL
);
SELECT create_hypertable('master_load_logs', 'timestamp', chunk_time_interval => INTERVAL '30 days');

-- 대시보드의 배포 시점 표시로 쓴다(5.11).
CREATE TABLE server_lifecycle_logs (
    "timestamp" timestamptz NOT NULL,
    phase       text        NOT NULL,
    version     text        NOT NULL
);
SELECT create_hypertable('server_lifecycle_logs', 'timestamp', chunk_time_interval => INTERVAL '30 days');

-- 전역 예외 처리기가 잡은 미처리 예외(5.11). **미인증 요청도 예외를 낼 수 있어 uid 가 null 이다.**
-- 최근 N건 조회가 전부라 분할 축 기본 인덱스만 둔다(8.9).
CREATE TABLE api_error_logs (
    "timestamp"    timestamptz NOT NULL,
    uid            bigint,
    req_id         text,
    error_code     int         NOT NULL,
    path           text        NOT NULL,
    exception_type text        NOT NULL
);
SELECT create_hypertable('api_error_logs', 'timestamp', chunk_time_interval => INTERVAL '30 days');

-- ═══════════════════════════════════════════════════════════════════════════
--  6. 매핑 없는 태그 수신 테이블 (9.5)
--
--  이 테이블에 행이 생기는 것 자체가 "카탈로그(5·7장)와 방출 코드가 어긋났다"는 신호다.
--  tag 컬럼을 갖는 유일한 테이블이며(8.1의 예외), 고유 필드는 매핑하지 않아 적재되지 않는다.
-- ═══════════════════════════════════════════════════════════════════════════

CREATE TABLE unknown_event_logs (
    "timestamp" timestamptz NOT NULL,
    tag         text,
    uid         bigint
);
SELECT create_hypertable('unknown_event_logs', 'timestamp', chunk_time_interval => INTERVAL '30 days');

-- ═══════════════════════════════════════════════════════════════════════════
--  7. 히스토리 로그 — 주기 스냅샷 (7장 · ERD 8.7)
--
--  액션 로그와 달리 자연 키 PK를 갖는다(같은 주기를 다시 세면 덮어쓰는 것이 정상 동작).
--  TimescaleDB 는 유니크 인덱스에 분할 축을 포함하도록 강제하므로 PK 첫 컬럼이 시간축이다.
--  보존은 무기한 — 원본 액션 로그가 90일 뒤 사라진 구간의 상태를 유일하게 증언한다(8.8).
-- ═══════════════════════════════════════════════════════════════════════════

CREATE TABLE online_user_history (
    "timestamp"  timestamptz NOT NULL PRIMARY KEY,
    online_count int         NOT NULL
);
SELECT create_hypertable('online_user_history', 'timestamp', chunk_time_interval => INTERVAL '30 days');

CREATE TABLE currency_supply_history (
    "timestamp"         timestamptz NOT NULL,
    currency_code       int         NOT NULL,
    total_amount        bigint      NOT NULL,
    holder_count        int         NOT NULL,
    mail_pending_amount bigint      NOT NULL,
    PRIMARY KEY ("timestamp", currency_code)
);
SELECT create_hypertable('currency_supply_history', 'timestamp', chunk_time_interval => INTERVAL '30 days');

CREATE TABLE trade_market_history (
    "timestamp"   timestamptz NOT NULL,
    item_code     int         NOT NULL,
    listing_count int         NOT NULL,
    min_price     bigint      NOT NULL,
    avg_price     bigint      NOT NULL,
    PRIMARY KEY ("timestamp", item_code)
);
SELECT create_hypertable('trade_market_history', 'timestamp', chunk_time_interval => INTERVAL '30 days');

-- 일 단위 6종은 시간축 컬럼이 log_date 다 — 라인의 timestamp 는 적재하지 않는다(9.5).
CREATE TABLE item_supply_history (
    log_date     date   NOT NULL,
    item_code    int    NOT NULL,
    total_count  bigint NOT NULL,
    holder_count int    NOT NULL,
    PRIMARY KEY (log_date, item_code)
);
SELECT create_hypertable('item_supply_history', 'log_date', chunk_time_interval => INTERVAL '30 days');

CREATE TABLE stage_progress_history (
    log_date   date NOT NULL,
    stage_id   int  NOT NULL,
    user_count int  NOT NULL,
    PRIMARY KEY (log_date, stage_id)
);
SELECT create_hypertable('stage_progress_history', 'log_date', chunk_time_interval => INTERVAL '30 days');

CREATE TABLE equip_item_history (
    log_date       date     NOT NULL,
    item_code      int      NOT NULL,
    equip_slot     smallint NOT NULL,
    equipped_count int      NOT NULL,
    holder_count   int      NOT NULL,
    PRIMARY KEY (log_date, item_code, equip_slot)
);
SELECT create_hypertable('equip_item_history', 'log_date', chunk_time_interval => INTERVAL '30 days');

-- 자리 세 컬럼은 정렬하지 않는다 — 슬롯 위치가 편성의 일부라 (1,2,3)과 (3,2,1)은 다른 조합이다(8.7).
CREATE TABLE party_comp_history (
    log_date         date     NOT NULL,
    slot1_class_code smallint NOT NULL,
    slot2_class_code smallint NOT NULL,
    slot3_class_code smallint NOT NULL,
    user_count       int      NOT NULL,
    PRIMARY KEY (log_date, slot1_class_code, slot2_class_code, slot3_class_code)
);
SELECT create_hypertable('party_comp_history', 'log_date', chunk_time_interval => INTERVAL '30 days');

CREATE TABLE skill_build_history (
    log_date            date     NOT NULL,
    class_code          smallint NOT NULL,
    active_skill_code_1 int      NOT NULL,
    active_skill_code_2 int      NOT NULL,
    character_count     int      NOT NULL,
    PRIMARY KEY (log_date, class_code, active_skill_code_1, active_skill_code_2)
);
SELECT create_hypertable('skill_build_history', 'log_date', chunk_time_interval => INTERVAL '30 days');

CREATE TABLE skill_invest_history (
    log_date        date     NOT NULL,
    class_code      smallint NOT NULL,
    skill_code      int      NOT NULL,
    level           smallint NOT NULL,
    character_count int      NOT NULL,
    PRIMARY KEY (log_date, class_code, skill_code, level)
);
SELECT create_hypertable('skill_invest_history', 'log_date', chunk_time_interval => INTERVAL '30 days');

-- ═══════════════════════════════════════════════════════════════════════════
--  7-1. 히스토리 재적재 = 덮어쓰기 (7장 · 8.7)
--
--  히스토리는 **같은 주기를 다시 세면 덮어써야 맞다** — 일 배치가 서버 재기동으로 하루에 두 번 돌면
--  나중 스냅샷이 그날의 값이어야 한다. 그래서 PK 를 자연 키로 뒀다.
--
--  그런데 수집기(fluent-plugin-sql 의 out_sql)는 **순수 INSERT 만** 하고 ON CONFLICT 를 낼 방법이 없다.
--  그대로 두면 두 번째 스냅샷이 유니크 위반으로 통째로 버려진다(실측: 일 단위 6종 368행 유실).
--  수집기가 못 하므로 **덮어쓰기를 DB 가 흡수한다** — BEFORE INSERT 트리거가 같은 자연 키의 기존 행을
--  지우고 새 행을 넣는다(= REPLACE). 서버 코드와 fluentd 설정은 이 문제를 몰라도 된다.
--
--  · **액션 로그에는 걸지 않는다.** 그쪽은 append-only 이고 같은 사건이 두 줄이면 그것 자체가 사실이다.
--  · 시각 키 3종(online_user·currency_supply·trade_market)은 timestamp 가 밀리초라 실제로 충돌하지
--    않지만, "히스토리는 덮어쓴다"를 테이블마다 다르게 두지 않으려고 함께 건다.
--  · 키 컬럼은 트리거 인자로 넘긴다 — 테이블 9개에 같은 함수를 복제하지 않기 위해서다.
--  · 대상 테이블 이름도 인자로 받는다. TimescaleDB 는 BEFORE INSERT 트리거를 **청크에 복제해**
--    실행하므로 TG_TABLE_NAME 이 하이퍼테이블이 아니라 청크 이름이 된다 — 그 값으로 DELETE 하면
--    다른 청크에 있는 같은 키를 놓친다.
-- ═══════════════════════════════════════════════════════════════════════════

CREATE OR REPLACE FUNCTION history_replace_row() RETURNS trigger
LANGUAGE plpgsql AS $$
DECLARE
    target    text := TG_ARGV[0];   -- 하이퍼테이블 이름(TG_ARGV 는 0-based)
    predicate text := '';
    i         int;
BEGIN
    FOR i IN 1 .. TG_NARGS - 1 LOOP
        IF i > 1 THEN
            predicate := predicate || ' AND ';
        END IF;
        predicate := predicate || format('%I = ($1).%I', TG_ARGV[i], TG_ARGV[i]);
    END LOOP;

    EXECUTE format('DELETE FROM public.%I WHERE %s', target, predicate) USING NEW;
    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_online_user_history_replace BEFORE INSERT ON online_user_history
    FOR EACH ROW EXECUTE FUNCTION history_replace_row('online_user_history', 'timestamp');

CREATE TRIGGER trg_currency_supply_history_replace BEFORE INSERT ON currency_supply_history
    FOR EACH ROW EXECUTE FUNCTION history_replace_row('currency_supply_history', 'timestamp', 'currency_code');

CREATE TRIGGER trg_trade_market_history_replace BEFORE INSERT ON trade_market_history
    FOR EACH ROW EXECUTE FUNCTION history_replace_row('trade_market_history', 'timestamp', 'item_code');

CREATE TRIGGER trg_item_supply_history_replace BEFORE INSERT ON item_supply_history
    FOR EACH ROW EXECUTE FUNCTION history_replace_row('item_supply_history', 'log_date', 'item_code');

CREATE TRIGGER trg_stage_progress_history_replace BEFORE INSERT ON stage_progress_history
    FOR EACH ROW EXECUTE FUNCTION history_replace_row('stage_progress_history', 'log_date', 'stage_id');

CREATE TRIGGER trg_equip_item_history_replace BEFORE INSERT ON equip_item_history
    FOR EACH ROW EXECUTE FUNCTION history_replace_row('equip_item_history', 'log_date', 'item_code', 'equip_slot');

CREATE TRIGGER trg_party_comp_history_replace BEFORE INSERT ON party_comp_history
    FOR EACH ROW EXECUTE FUNCTION history_replace_row(
        'party_comp_history', 'log_date', 'slot1_class_code', 'slot2_class_code', 'slot3_class_code');

CREATE TRIGGER trg_skill_build_history_replace BEFORE INSERT ON skill_build_history
    FOR EACH ROW EXECUTE FUNCTION history_replace_row(
        'skill_build_history', 'log_date', 'class_code', 'active_skill_code_1', 'active_skill_code_2');

CREATE TRIGGER trg_skill_invest_history_replace BEFORE INSERT ON skill_invest_history
    FOR EACH ROW EXECUTE FUNCTION history_replace_row(
        'skill_invest_history', 'log_date', 'class_code', 'skill_code', 'level');

-- ═══════════════════════════════════════════════════════════════════════════
--  8. 압축 정책 — 7일 지난 청크부터 (8.8)
--
--  segmentby 는 그 테이블에서 가장 자주 GROUP BY 하는 컬럼이고 orderby 는 timestamp DESC 다.
--  최근 1주는 원본 그대로 두어 임시 분석이 빠르다.
--  · 시스템 테이블 중 master_load_logs·server_lifecycle_logs 는 GROUP BY 축이 없어
--    segmentby 를 비운다(하루 몇 행 규모라 압축 이득도 크지 않다).
--  · **히스토리 테이블은 압축하지 않는다.** 자연 키 덮어쓰기가 정상 동작인 테이블이라
--    압축된 청크에 재적재가 걸리면 곤란하고, 볼륨도 하루 수백~수천 행에 그친다(8.8·7장).
-- ═══════════════════════════════════════════════════════════════════════════

ALTER TABLE save_load_logs         SET (timescaledb.compress, timescaledb.compress_segmentby = 'uid',       timescaledb.compress_orderby = '"timestamp" DESC');
ALTER TABLE player_create_logs     SET (timescaledb.compress, timescaledb.compress_segmentby = 'uid',       timescaledb.compress_orderby = '"timestamp" DESC');
ALTER TABLE stage_enter_logs       SET (timescaledb.compress, timescaledb.compress_segmentby = 'uid',       timescaledb.compress_orderby = '"timestamp" DESC');
ALTER TABLE stage_clear_logs       SET (timescaledb.compress, timescaledb.compress_segmentby = 'stage_id',  timescaledb.compress_orderby = '"timestamp" DESC');
ALTER TABLE stage_fail_logs        SET (timescaledb.compress, timescaledb.compress_segmentby = 'stage_id',  timescaledb.compress_orderby = '"timestamp" DESC');
ALTER TABLE character_levelup_logs SET (timescaledb.compress, timescaledb.compress_segmentby = 'uid',       timescaledb.compress_orderby = '"timestamp" DESC');
ALTER TABLE offline_claim_logs     SET (timescaledb.compress, timescaledb.compress_segmentby = 'uid',       timescaledb.compress_orderby = '"timestamp" DESC');
ALTER TABLE item_enhance_logs      SET (timescaledb.compress, timescaledb.compress_segmentby = 'item_code', timescaledb.compress_orderby = '"timestamp" DESC');
ALTER TABLE currency_flow_logs     SET (timescaledb.compress, timescaledb.compress_segmentby = 'source',    timescaledb.compress_orderby = '"timestamp" DESC');
ALTER TABLE item_flow_logs         SET (timescaledb.compress, timescaledb.compress_segmentby = 'item_code', timescaledb.compress_orderby = '"timestamp" DESC');
ALTER TABLE gacha_pull_item_logs   SET (timescaledb.compress, timescaledb.compress_segmentby = 'gacha_code',timescaledb.compress_orderby = '"timestamp" DESC');
ALTER TABLE trade_register_logs    SET (timescaledb.compress, timescaledb.compress_segmentby = 'item_code', timescaledb.compress_orderby = '"timestamp" DESC');
ALTER TABLE trade_close_logs       SET (timescaledb.compress, timescaledb.compress_segmentby = 'outcome',   timescaledb.compress_orderby = '"timestamp" DESC');
ALTER TABLE mail_issue_logs        SET (timescaledb.compress, timescaledb.compress_segmentby = 'uid',       timescaledb.compress_orderby = '"timestamp" DESC');
ALTER TABLE attendance_claim_logs  SET (timescaledb.compress, timescaledb.compress_segmentby = 'uid',       timescaledb.compress_orderby = '"timestamp" DESC');
ALTER TABLE bossrush_enter_logs    SET (timescaledb.compress, timescaledb.compress_segmentby = 'uid',       timescaledb.compress_orderby = '"timestamp" DESC');
ALTER TABLE bossrush_clear_logs    SET (timescaledb.compress, timescaledb.compress_segmentby = 'uid',       timescaledb.compress_orderby = '"timestamp" DESC');
ALTER TABLE batch_run_logs         SET (timescaledb.compress, timescaledb.compress_segmentby = 'batch_key', timescaledb.compress_orderby = '"timestamp" DESC');
ALTER TABLE master_load_logs       SET (timescaledb.compress, timescaledb.compress_segmentby = '',          timescaledb.compress_orderby = '"timestamp" DESC');
ALTER TABLE server_lifecycle_logs  SET (timescaledb.compress, timescaledb.compress_segmentby = '',          timescaledb.compress_orderby = '"timestamp" DESC');
ALTER TABLE api_error_logs         SET (timescaledb.compress, timescaledb.compress_segmentby = 'uid',       timescaledb.compress_orderby = '"timestamp" DESC');
ALTER TABLE unknown_event_logs     SET (timescaledb.compress, timescaledb.compress_segmentby = 'tag',       timescaledb.compress_orderby = '"timestamp" DESC');

SELECT add_compression_policy('save_load_logs',         INTERVAL '7 days');
SELECT add_compression_policy('player_create_logs',     INTERVAL '7 days');
SELECT add_compression_policy('stage_enter_logs',       INTERVAL '7 days');
SELECT add_compression_policy('stage_clear_logs',       INTERVAL '7 days');
SELECT add_compression_policy('stage_fail_logs',        INTERVAL '7 days');
SELECT add_compression_policy('character_levelup_logs', INTERVAL '7 days');
SELECT add_compression_policy('offline_claim_logs',     INTERVAL '7 days');
SELECT add_compression_policy('item_enhance_logs',      INTERVAL '7 days');
SELECT add_compression_policy('currency_flow_logs',     INTERVAL '7 days');
SELECT add_compression_policy('item_flow_logs',         INTERVAL '7 days');
SELECT add_compression_policy('gacha_pull_item_logs',   INTERVAL '7 days');
SELECT add_compression_policy('trade_register_logs',    INTERVAL '7 days');
SELECT add_compression_policy('trade_close_logs',       INTERVAL '7 days');
SELECT add_compression_policy('mail_issue_logs',        INTERVAL '7 days');
SELECT add_compression_policy('attendance_claim_logs',  INTERVAL '7 days');
SELECT add_compression_policy('bossrush_enter_logs',    INTERVAL '7 days');
SELECT add_compression_policy('bossrush_clear_logs',    INTERVAL '7 days');
SELECT add_compression_policy('batch_run_logs',         INTERVAL '7 days');
SELECT add_compression_policy('master_load_logs',       INTERVAL '7 days');
SELECT add_compression_policy('server_lifecycle_logs',  INTERVAL '7 days');
SELECT add_compression_policy('api_error_logs',         INTERVAL '7 days');
SELECT add_compression_policy('unknown_event_logs',     INTERVAL '7 days');

-- ═══════════════════════════════════════════════════════════════════════════
--  9. 보존 정책 — 액션 로그 90일 / 히스토리 무기한 (8.8)
--
--  90일이면 시즌(7일) 12회 이상, 분기 단위 리텐션·시세 추이를 원본 해상도로 볼 수 있다.
--  히스토리에는 add_retention_policy 를 걸지 않는다(원본이 사라진 구간의 상태를 증언한다).
--  연속 집계(10절)도 무기한 보존이라, 원본 90일 + 집계 영구가 이 저장소의 최종 보존 형태다.
-- ═══════════════════════════════════════════════════════════════════════════

SELECT add_retention_policy('save_load_logs',         INTERVAL '90 days');
SELECT add_retention_policy('player_create_logs',     INTERVAL '90 days');
SELECT add_retention_policy('stage_enter_logs',       INTERVAL '90 days');
SELECT add_retention_policy('stage_clear_logs',       INTERVAL '90 days');
SELECT add_retention_policy('stage_fail_logs',        INTERVAL '90 days');
SELECT add_retention_policy('character_levelup_logs', INTERVAL '90 days');
SELECT add_retention_policy('offline_claim_logs',     INTERVAL '90 days');
SELECT add_retention_policy('item_enhance_logs',      INTERVAL '90 days');
SELECT add_retention_policy('currency_flow_logs',     INTERVAL '90 days');
SELECT add_retention_policy('item_flow_logs',         INTERVAL '90 days');
SELECT add_retention_policy('gacha_pull_item_logs',   INTERVAL '90 days');
SELECT add_retention_policy('trade_register_logs',    INTERVAL '90 days');
SELECT add_retention_policy('trade_close_logs',       INTERVAL '90 days');
SELECT add_retention_policy('mail_issue_logs',        INTERVAL '90 days');
SELECT add_retention_policy('attendance_claim_logs',  INTERVAL '90 days');
SELECT add_retention_policy('bossrush_enter_logs',    INTERVAL '90 days');
SELECT add_retention_policy('bossrush_clear_logs',    INTERVAL '90 days');
SELECT add_retention_policy('batch_run_logs',         INTERVAL '90 days');
SELECT add_retention_policy('master_load_logs',       INTERVAL '90 days');
SELECT add_retention_policy('server_lifecycle_logs',  INTERVAL '90 days');
SELECT add_retention_policy('api_error_logs',         INTERVAL '90 days');
SELECT add_retention_policy('unknown_event_logs',     INTERVAL '90 days');

-- ═══════════════════════════════════════════════════════════════════════════
--  10. 연속 집계 — 대시보드가 원본을 매번 스캔하지 않게 (8.10)
--
--  · 버킷은 **Asia/Seoul 기준 1일**이다. 서버·컨테이너가 모두 KST로 도는데 UTC 버킷을 쓰면
--    "어제 골드 유입"이 09:00 경계로 잘려 운영 감각과 어긋난다(time_bucket 의 timezone 인자).
--  · 갱신 정책은 start_offset 3 days · end_offset 1 hour · schedule_interval 1 hour 로 통일한다
--    (늦게 도착한 로그를 3일까지 흡수).
--  · WITH NO DATA 로 만든다 — 생성 시점에 전 구간을 한 번에 굴리지 않고 정책이 채운다.
--  · DAU 를 count(distinct uid) 로 만들지 않는다. 연속 집계는 DISTINCT 집계를 지원하지 않으므로
--    (날짜, uid) 로 그룹한 session_user_daily 를 두고 **DAU는 그 뷰를 한 번 더 세는 방식**으로 얻는다.
--    재방문율·리텐션도 같은 뷰의 자기 조인으로 나온다(8.10).
-- ═══════════════════════════════════════════════════════════════════════════

CREATE MATERIALIZED VIEW session_user_daily
WITH (timescaledb.continuous) AS
SELECT time_bucket('1 day', "timestamp", 'Asia/Seoul') AS bucket,
       uid,
       count(*) AS session_count
FROM save_load_logs
GROUP BY bucket, uid
WITH NO DATA;

CREATE MATERIALIZED VIEW stage_enter_daily
WITH (timescaledb.continuous) AS
SELECT time_bucket('1 day', "timestamp", 'Asia/Seoul') AS bucket,
       stage_id,
       count(*) AS enter_count
FROM stage_enter_logs
GROUP BY bucket, stage_id
WITH NO DATA;

CREATE MATERIALIZED VIEW stage_clear_daily
WITH (timescaledb.continuous) AS
SELECT time_bucket('1 day', "timestamp", 'Asia/Seoul') AS bucket,
       stage_id,
       count(*)  AS clear_count,
       sum(gold) AS gold_sum,
       sum(exp)  AS exp_sum
FROM stage_clear_logs
GROUP BY bucket, stage_id
WITH NO DATA;

-- stage_enter_daily·stage_clear_daily 와 stage_id 로 조인하면 난이도 패널이 완성된다 —
-- fail / enter 가 실질 난이도, enter − clear − fail 이 이탈(보고 없이 사라진 판)이다(8.10).
CREATE MATERIALIZED VIEW stage_fail_daily
WITH (timescaledb.continuous) AS
SELECT time_bucket('1 day', "timestamp", 'Asia/Seoul') AS bucket,
       stage_id,
       count(*)                       AS fail_count,
       avg(elapsed_ms)                AS avg_elapsed_ms,
       avg(remaining_monster_count)   AS avg_remaining_monster_count,
       sum(reached_boss::int)         AS reached_boss_count
FROM stage_fail_logs
GROUP BY bucket, stage_id
WITH NO DATA;

CREATE MATERIALIZED VIEW currency_flow_daily
WITH (timescaledb.continuous) AS
SELECT time_bucket('1 day', "timestamp", 'Asia/Seoul') AS bucket,
       direction,
       source,
       sum(amount) AS amount_sum,
       count(*)    AS flow_count
FROM currency_flow_logs
GROUP BY bucket, direction, source
WITH NO DATA;

CREATE MATERIALIZED VIEW item_flow_daily
WITH (timescaledb.continuous) AS
SELECT time_bucket('1 day', "timestamp", 'Asia/Seoul') AS bucket,
       item_code,
       reason,
       sum(delta) AS delta_sum,
       count(*)   AS flow_count
FROM item_flow_logs
GROUP BY bucket, item_code, reason
WITH NO DATA;

CREATE MATERIALIZED VIEW gacha_grade_daily
WITH (timescaledb.continuous) AS
SELECT time_bucket('1 day', "timestamp", 'Asia/Seoul') AS bucket,
       gacha_code,
       grade,
       count(*)             AS pull_count,
       sum(is_pity::int)    AS pity_count
FROM gacha_pull_item_logs
GROUP BY bucket, gacha_code, grade
WITH NO DATA;

SELECT add_continuous_aggregate_policy('session_user_daily',  start_offset => INTERVAL '3 days', end_offset => INTERVAL '1 hour', schedule_interval => INTERVAL '1 hour');
SELECT add_continuous_aggregate_policy('stage_enter_daily',   start_offset => INTERVAL '3 days', end_offset => INTERVAL '1 hour', schedule_interval => INTERVAL '1 hour');
SELECT add_continuous_aggregate_policy('stage_clear_daily',   start_offset => INTERVAL '3 days', end_offset => INTERVAL '1 hour', schedule_interval => INTERVAL '1 hour');
SELECT add_continuous_aggregate_policy('stage_fail_daily',    start_offset => INTERVAL '3 days', end_offset => INTERVAL '1 hour', schedule_interval => INTERVAL '1 hour');
SELECT add_continuous_aggregate_policy('currency_flow_daily', start_offset => INTERVAL '3 days', end_offset => INTERVAL '1 hour', schedule_interval => INTERVAL '1 hour');
SELECT add_continuous_aggregate_policy('item_flow_daily',     start_offset => INTERVAL '3 days', end_offset => INTERVAL '1 hour', schedule_interval => INTERVAL '1 hour');
SELECT add_continuous_aggregate_policy('gacha_grade_daily',   start_offset => INTERVAL '3 days', end_offset => INTERVAL '1 hour', schedule_interval => INTERVAL '1 hour');
