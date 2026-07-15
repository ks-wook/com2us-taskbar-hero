-- =====================================================================
-- Taskbar Hero 모작 — 마스터(기획) 데이터 DDL + 시드 INSERT (MySQL 8.x / InnoDB / utf8mb4)
--
-- 정본(single source of truth): docs/세부/master-data/master-data-값.md (실제 값) 및
--   docs/세부/master-data/master-data-기획서.md (테이블 구조·필드·enum).  불일치 시 그 문서를 따른다.
--
-- 범위: master-data-값.md에서 값이 확정된 테이블(1~6번 및 8·9·10·11·13번)을 담는다.
--   1) equip_slot_master  2) class_master  3) level_master  4) skill_master  5) rune_master
--   6) item_master  8) cube_master  8b) cube_recipe  8c) cube_recipe_ingredient
--   9) monster_master  10) stage_reward  11) stage_master  11b) stage_spawn
--   13) attendance_master
--   (7 enhance/12 box는 값 미확정이라 제외)
--
-- 성격 안내(중요)
--   * 마스터 데이터는 런타임에 "클라이언트 번들 + 서버 인메모리 로드"로 쓰이므로
--     게임 플레이 경로에서 이 테이블을 조회하지는 않는다(그래서 db-schema.sql에는 없다).
--   * 본 파일은 그 값을 관계형으로 보관/시드/검수하기 위한 편의용 스크립트다.
--     기획 데이터의 단일 관리처를 DB로 두고 싶을 때, 혹은 번들/JSON을 생성하는 소스로 쓴다.
--
-- ⚠️ 개발 편의(파괴적): 재실행 시 각 테이블을 DROP 후 재생성하고 시드를 다시 넣는다.
--    실행하면 기존 마스터 테이블 데이터가 모두 삭제된다. 초기화 용도로만 쓸 것.
--
-- 공통 규약
--   * JSON 문자열 컬럼을 쓰지 않는다(설계 규칙). 고정 스키마 값은 개별 컬럼으로, 배열·중첩 등
--     반복 구조는 무조건 별도(자식) 테이블로 분리한다(예: stage_master 스폰 → stage_spawn).
--     클라 번들 JSON은 이 컬럼/자식 행들을 baseStats·spawns 등 객체/배열로 묶어 직렬화한다(기획서 5.1·7장).
--     cube의 제작 레시피도 자식 테이블(cube_recipe/cube_recipe_ingredient)로 분리했고,
--     앞으로 추가할 box(grade_weights/item_pool) 등도 같은 방식으로 설계한다.
--   * skill_type: 1=액티브, 2=패시브.  unlock_type: 0=기본 선택(생성 시 선택 가능).
--   * class_code는 class_master를, skill_master.class_code가 이를 참조한다(같은 DB이므로 FK를 건다).
-- =====================================================================

CREATE DATABASE IF NOT EXISTS taskbar_hero_master
    DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE taskbar_hero_master;

-- 재실행 시 초기화(FK 무시하고 DROP → 재생성). 스크립트 끝에서 다시 1로 되돌린다.
SET FOREIGN_KEY_CHECKS = 0;


-- =====================================================================
-- 1. equip_slot_master — 장착 슬롯(6부위 고정)
--    출처: master-data-값.md §1, 기획서 5.2
-- =====================================================================
DROP TABLE IF EXISTS equip_slot_master;
CREATE TABLE equip_slot_master (
    slot  TINYINT     NOT NULL COMMENT '슬롯 번호(1~6)',
    name  VARCHAR(20) NOT NULL COMMENT '슬롯 이름',
    PRIMARY KEY (slot)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='장비 장착 슬롯 정의(6부위)';

INSERT INTO equip_slot_master (slot, name) VALUES
    (1, '무기'),
    (2, '보조무기'),
    (3, '투구'),
    (4, '갑옷'),
    (5, '장갑'),
    (6, '신발');


-- =====================================================================
-- 2. class_master — 직업(3종)과 기본 스탯
--    출처: master-data-값.md §2, 기획서 5.1
-- =====================================================================
DROP TABLE IF EXISTS class_master;
CREATE TABLE class_master (
    class_code   INT         NOT NULL COMMENT '직업 코드',
    name         VARCHAR(20) NOT NULL COMMENT '직업 이름',
    unlock_type  TINYINT      NOT NULL COMMENT '해금 방식(0=기본 선택)',
    hp           BIGINT       NOT NULL COMMENT '기본 체력',
    atk          BIGINT       NOT NULL COMMENT '기본 공격',
    def          BIGINT       NOT NULL COMMENT '기본 방어',
    move_speed   DECIMAL(6,3) NOT NULL COMMENT '이동속도',
    crit_chance  DECIMAL(5,4) NOT NULL COMMENT '치명확률(0~1)',
    crit_damage  DECIMAL(5,3) NOT NULL COMMENT '치명데미지 배율(1.5=150%)',
    cooldown     DECIMAL(5,3) NOT NULL COMMENT '재사용 대기시간(초)',
    PRIMARY KEY (class_code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='캐릭터 직업 정의(기본 스탯 포함)';

INSERT INTO class_master (class_code, name, unlock_type, hp, atk, def, move_speed, crit_chance, crit_damage, cooldown) VALUES
    (1, '기사',   0, 120, 10, 8, 3.0, 0.05, 1.5, 1.2),
    (2, '레인저', 0,  90, 14, 5, 4.0, 0.10, 1.5, 0.9),
    (3, '마법사', 0,  85, 16, 4, 3.2, 0.08, 1.7, 1.5);


-- =====================================================================
-- 3. level_master — 레벨 곡선(1~100)
--    출처: master-data-값.md §3, 기획서 5.12
--    공식: required_exp = (L<100 ? 100*L : 0), skill_points = L,
--          bonus_hp = 10*L, bonus_atk = 2*L, bonus_def = 1*L
-- =====================================================================
DROP TABLE IF EXISTS level_master;
CREATE TABLE level_master (
    level         INT    NOT NULL COMMENT '레벨(1~100)',
    required_exp  BIGINT NOT NULL COMMENT 'L→L+1 요구 경험치(최대 레벨은 0)',
    skill_points  INT    NOT NULL COMMENT '해당 레벨 도달 시 누적 스킬 포인트',
    bonus_hp      BIGINT NOT NULL COMMENT '누적 체력 보너스',
    bonus_atk     BIGINT NOT NULL COMMENT '누적 공격 보너스',
    bonus_def     BIGINT NOT NULL COMMENT '누적 방어 보너스',
    PRIMARY KEY (level)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='레벨별 요구 경험치·스킬 포인트·스탯 보너스';

INSERT INTO level_master (level, required_exp, skill_points, bonus_hp, bonus_atk, bonus_def) VALUES
    (1, 100, 1, 10, 2, 1),
    (2, 200, 2, 20, 4, 2),
    (3, 300, 3, 30, 6, 3),
    (4, 400, 4, 40, 8, 4),
    (5, 500, 5, 50, 10, 5),
    (6, 600, 6, 60, 12, 6),
    (7, 700, 7, 70, 14, 7),
    (8, 800, 8, 80, 16, 8),
    (9, 900, 9, 90, 18, 9),
    (10, 1000, 10, 100, 20, 10),
    (11, 1100, 11, 110, 22, 11),
    (12, 1200, 12, 120, 24, 12),
    (13, 1300, 13, 130, 26, 13),
    (14, 1400, 14, 140, 28, 14),
    (15, 1500, 15, 150, 30, 15),
    (16, 1600, 16, 160, 32, 16),
    (17, 1700, 17, 170, 34, 17),
    (18, 1800, 18, 180, 36, 18),
    (19, 1900, 19, 190, 38, 19),
    (20, 2000, 20, 200, 40, 20),
    (21, 2100, 21, 210, 42, 21),
    (22, 2200, 22, 220, 44, 22),
    (23, 2300, 23, 230, 46, 23),
    (24, 2400, 24, 240, 48, 24),
    (25, 2500, 25, 250, 50, 25),
    (26, 2600, 26, 260, 52, 26),
    (27, 2700, 27, 270, 54, 27),
    (28, 2800, 28, 280, 56, 28),
    (29, 2900, 29, 290, 58, 29),
    (30, 3000, 30, 300, 60, 30),
    (31, 3100, 31, 310, 62, 31),
    (32, 3200, 32, 320, 64, 32),
    (33, 3300, 33, 330, 66, 33),
    (34, 3400, 34, 340, 68, 34),
    (35, 3500, 35, 350, 70, 35),
    (36, 3600, 36, 360, 72, 36),
    (37, 3700, 37, 370, 74, 37),
    (38, 3800, 38, 380, 76, 38),
    (39, 3900, 39, 390, 78, 39),
    (40, 4000, 40, 400, 80, 40),
    (41, 4100, 41, 410, 82, 41),
    (42, 4200, 42, 420, 84, 42),
    (43, 4300, 43, 430, 86, 43),
    (44, 4400, 44, 440, 88, 44),
    (45, 4500, 45, 450, 90, 45),
    (46, 4600, 46, 460, 92, 46),
    (47, 4700, 47, 470, 94, 47),
    (48, 4800, 48, 480, 96, 48),
    (49, 4900, 49, 490, 98, 49),
    (50, 5000, 50, 500, 100, 50),
    (51, 5100, 51, 510, 102, 51),
    (52, 5200, 52, 520, 104, 52),
    (53, 5300, 53, 530, 106, 53),
    (54, 5400, 54, 540, 108, 54),
    (55, 5500, 55, 550, 110, 55),
    (56, 5600, 56, 560, 112, 56),
    (57, 5700, 57, 570, 114, 57),
    (58, 5800, 58, 580, 116, 58),
    (59, 5900, 59, 590, 118, 59),
    (60, 6000, 60, 600, 120, 60),
    (61, 6100, 61, 610, 122, 61),
    (62, 6200, 62, 620, 124, 62),
    (63, 6300, 63, 630, 126, 63),
    (64, 6400, 64, 640, 128, 64),
    (65, 6500, 65, 650, 130, 65),
    (66, 6600, 66, 660, 132, 66),
    (67, 6700, 67, 670, 134, 67),
    (68, 6800, 68, 680, 136, 68),
    (69, 6900, 69, 690, 138, 69),
    (70, 7000, 70, 700, 140, 70),
    (71, 7100, 71, 710, 142, 71),
    (72, 7200, 72, 720, 144, 72),
    (73, 7300, 73, 730, 146, 73),
    (74, 7400, 74, 740, 148, 74),
    (75, 7500, 75, 750, 150, 75),
    (76, 7600, 76, 760, 152, 76),
    (77, 7700, 77, 770, 154, 77),
    (78, 7800, 78, 780, 156, 78),
    (79, 7900, 79, 790, 158, 79),
    (80, 8000, 80, 800, 160, 80),
    (81, 8100, 81, 810, 162, 81),
    (82, 8200, 82, 820, 164, 82),
    (83, 8300, 83, 830, 166, 83),
    (84, 8400, 84, 840, 168, 84),
    (85, 8500, 85, 850, 170, 85),
    (86, 8600, 86, 860, 172, 86),
    (87, 8700, 87, 870, 174, 87),
    (88, 8800, 88, 880, 176, 88),
    (89, 8900, 89, 890, 178, 89),
    (90, 9000, 90, 900, 180, 90),
    (91, 9100, 91, 910, 182, 91),
    (92, 9200, 92, 920, 184, 92),
    (93, 9300, 93, 930, 186, 93),
    (94, 9400, 94, 940, 188, 94),
    (95, 9500, 95, 950, 190, 95),
    (96, 9600, 96, 960, 192, 96),
    (97, 9700, 97, 970, 194, 97),
    (98, 9800, 98, 980, 196, 98),
    (99, 9900, 99, 990, 198, 99),
    (100, 0, 100, 1000, 200, 100);


-- =====================================================================
-- 4. skill_master — 직업별 스킬(액티브/패시브)
--    출처: master-data-값.md §4, 기획서 5.6
--    코드 규약: 기사 1xx / 레인저 2xx / 마법사 3xx (액티브 x01~, 패시브 x10~)
--    category: 1=공격, 2=버프, 3=디버프.  effect_per_level(레벨별 배열)은 폐기하고
--    성격을 category로 구분한 뒤 효과를 개별 컬럼(skill_coef/buff_duration/debuff_duration)으로 담는다.
--    skill_coef: 1레벨(습득) 기준 계수. 공격=공격력 대비 데미지 배율, 버프/디버프=대상 스탯 배율.
--    coef_growth: 레벨당 계수 변화량. 실제 계수 coef(L) = skill_coef + coef_growth*(L-1) (선형).
--      공격/버프는 >=0(레벨↑ 강해짐), 디버프는 <=0(감소 배율이 작아져 강해짐), 0이면 레벨 무관.
--    지속시간: 버프는 buff_duration(초)만, 디버프는 debuff_duration(초)만 사용(해당 없으면 0, 패시브 상시 버프도 0).
-- =====================================================================
DROP TABLE IF EXISTS skill_master;
CREATE TABLE skill_master (
    skill_code       INT          NOT NULL COMMENT '스킬 코드',
    class_code       INT          NOT NULL COMMENT '보유 직업(class_master.class_code)',
    name             VARCHAR(30)  NOT NULL COMMENT '스킬 이름',
    skill_type       TINYINT      NOT NULL COMMENT '1=액티브, 2=패시브',
    category         TINYINT      NOT NULL COMMENT '스킬 분류(1=공격, 2=버프, 3=디버프)',
    skill_coef       DECIMAL(6,3) NOT NULL COMMENT '1레벨 기준 스킬 계수(공격=데미지 배율, 버프/디버프=대상 스탯 배율)',
    coef_growth      DECIMAL(6,3) NOT NULL DEFAULT 0 COMMENT '레벨당 계수 변화량. coef(L)=skill_coef+coef_growth*(L-1). 디버프는 음수',
    buff_duration    DECIMAL(5,2) NOT NULL DEFAULT 0 COMMENT '버프 지속시간(초). 버프가 아니거나 패시브 상시면 0',
    debuff_duration  DECIMAL(5,2) NOT NULL DEFAULT 0 COMMENT '디버프 지속시간(초). 디버프가 아니면 0',
    max_level        INT          NOT NULL COMMENT '최대 스킬 레벨',
    PRIMARY KEY (skill_code),
    KEY idx_skill_class (class_code),
    CONSTRAINT fk_skill_class FOREIGN KEY (class_code)
        REFERENCES class_master (class_code) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='직업별 액티브/패시브 스킬';

INSERT INTO skill_master (skill_code, class_code, name, skill_type, category, skill_coef, coef_growth, buff_duration, debuff_duration, max_level) VALUES
    (101, 1, '방패 강타',    1, 1, 1.200,  0.100, 0, 0.00, 10),
    (102, 1, '도발',         1, 3, 0.800, -0.030, 0, 5.00, 5),
    (110, 1, '강철 피부',    2, 2, 1.150,  0.030, 0, 0.00, 5),
    (201, 2, '정조준 사격',  1, 1, 1.800,  0.100, 0, 0.00, 10),
    (202, 2, '다중 사격',    1, 1, 0.900,  0.050, 0, 0.00, 10),
    (210, 2, '민첩',         2, 2, 1.100,  0.020, 0, 0.00, 5),
    (301, 3, '파이어볼',     1, 1, 2.000,  0.150, 0, 0.00, 10),
    (302, 3, '프로스트 노바', 1, 3, 0.500, -0.020, 0, 3.00, 10),
    (310, 3, '마력 집중',    2, 2, 1.150,  0.030, 0, 0.00, 5);


-- =====================================================================
-- 5. rune_master — 룬(Rune Tree), 계정 공용 장기 성장 축
--    출처: master-data-값.md §5, 기획서 5.7
--    stat_type: 올려주는 능력치(1=공격력 2=방어력 3=체력 4=치명확률 5=치명피해 6=이동속도).
--    stat_value: 레벨당 누적 상승량 %. 총 보너스 = stat_value * 현재 룬 레벨.
--    cost: 레벨업 1회 기준 골드(base). 실제 비용은 현재 룬 레벨에 비례해 증가.
--    prereq_code: 선행 룬(루트면 0). 선행 룬이 레벨 1 이상이어야 해금(0→1).
--      루트는 prereq_code=0(유효 룬 코드 아님)이라 self-FK는 걸지 않고 애플리케이션에서 검증한다.
-- =====================================================================
DROP TABLE IF EXISTS rune_master;
CREATE TABLE rune_master (
    rune_code    INT          NOT NULL COMMENT '룬 코드',
    name         VARCHAR(30)  NOT NULL COMMENT '룬 이름',
    prereq_code  INT          NOT NULL DEFAULT 0 COMMENT '선행 룬 코드(루트면 0). 선행 룬 레벨 1 이상이어야 해금',
    cost         BIGINT       NOT NULL COMMENT '레벨업 1회 기준 골드(base). 실제 비용은 현재 레벨 비례 증가',
    max_level    INT          NOT NULL COMMENT '최대 룬 레벨',
    stat_type    INT          NOT NULL COMMENT '올려주는 능력치(1=공격력 2=방어력 3=체력 4=치명확률 5=치명피해 6=이동속도)',
    stat_value   DECIMAL(6,3) NOT NULL COMMENT '레벨당 누적 상승량 %(총합=stat_value*현재레벨)',
    PRIMARY KEY (rune_code),
    KEY idx_rune_prereq (prereq_code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='룬(Rune Tree) 정의(계정 공용 장기 성장)';

INSERT INTO rune_master (rune_code, name, prereq_code, cost, max_level, stat_type, stat_value) VALUES
    (201, '공격력 I',   0,     5000, 20, 1, 0.020),
    (202, '공격력 II',  201,  20000, 20, 1, 0.030),
    (203, '공격력 III', 202,  60000, 10, 1, 0.050),
    (230, '광폭화',     203, 100000, 5,  1, 0.080),
    (210, '치명확률 I', 0,     8000, 10, 4, 0.010),
    (211, '치명확률 II',210,  30000, 10, 4, 0.015),
    (220, '정밀 사격',  210,  25000, 10, 4, 0.020),
    (231, '필살',       211,  90000, 5,  4, 0.030);


-- =====================================================================
-- 6. item_master — 아이템(장비·재료) + 재화(골드)
--    출처: master-data-값.md §6, 기획서 5.3
--    item_type: 1=장비 2=재료 3=재화(골드=item_code 1). (소모품 타입 없음)
--    스탯(hp~cooldown)은 장비만 값을 가지며 그 외는 0(구 base_stats JSON을 개별 컬럼으로 분리).
--    equip_slot(비장비 0)·class_req(0=공용)는 0 센티널이라 FK를 걸지 않고 애플리케이션에서 검증한다.
--    sellable=0이면 거래소 등록 불가(base_price=0). level_req는 5의 배수.
-- =====================================================================
DROP TABLE IF EXISTS item_master;
CREATE TABLE item_master (
    item_code    INT          NOT NULL COMMENT '아이템 코드(골드=1)',
    name         VARCHAR(40)  NOT NULL COMMENT '아이템 이름',
    item_type    TINYINT      NOT NULL COMMENT '1=장비 2=재료 3=재화',
    grade        INT          NOT NULL COMMENT '등급/희귀도(클수록 고등급)',
    equip_slot   TINYINT      NOT NULL DEFAULT 0 COMMENT '장착 슬롯(equip_slot_master, 비장비 0)',
    class_req    INT          NOT NULL DEFAULT 0 COMMENT '착용 가능 클래스(class_master, 0=공용)',
    level_req    INT          NOT NULL DEFAULT 0 COMMENT '착용 요구 레벨(5의 배수, 0=제한 없음)',
    stack_max    INT          NOT NULL DEFAULT 1 COMMENT '최대 겹침 수량(장비 1)',
    hp           BIGINT       NOT NULL DEFAULT 0 COMMENT '장비 옵션 체력',
    atk          BIGINT       NOT NULL DEFAULT 0 COMMENT '장비 옵션 공격',
    def          BIGINT       NOT NULL DEFAULT 0 COMMENT '장비 옵션 방어',
    move_speed   DECIMAL(6,3) NOT NULL DEFAULT 0 COMMENT '장비 옵션 이동속도',
    crit_chance  DECIMAL(5,4) NOT NULL DEFAULT 0 COMMENT '장비 옵션 치명확률(0~1)',
    crit_damage  DECIMAL(5,3) NOT NULL DEFAULT 0 COMMENT '장비 옵션 치명데미지 배율',
    cooldown     DECIMAL(5,3) NOT NULL DEFAULT 0 COMMENT '장비 옵션 재사용 대기시간(초, 음수=감소)',
    sellable     TINYINT      NOT NULL DEFAULT 0 COMMENT '거래소 판매 가능(0/1)',
    base_price   BIGINT       NOT NULL DEFAULT 0 COMMENT '거래 기준가(골드, 0=거래 불가)',
    PRIMARY KEY (item_code),
    KEY idx_item_type (item_type)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='아이템·재화 정의';

INSERT INTO item_master (item_code, name, item_type, grade, equip_slot, class_req, level_req, stack_max, hp, atk, def, move_speed, crit_chance, crit_damage, cooldown, sellable, base_price) VALUES
    (1,     '골드',          3, 1, 0, 0, 0,  0,   0,  0,  0, 0.0, 0.00, 0.0, 0.0, 0,      0),
    (30001, '낡은 검',       1, 1, 1, 1, 0,  1,   0,  8,  0, 0.0, 0.00, 0.0, 0.0, 1,    500),
    (30012, '강철 대검',     1, 3, 1, 1, 15, 1,   0, 45,  0, 0.0, 0.00, 0.0, -0.1, 1,  50000),
    (30050, '롱보우',        1, 3, 1, 2, 15, 1,   0, 40,  0, 0.0, 0.05, 0.0, 0.0, 1,  50000),
    (30080, '마법 지팡이',   1, 3, 1, 3, 15, 1,   0, 42,  0, 0.0, 0.00, 0.1, 0.0, 1,  52000),
    (30240, '예리한 단검',   1, 4, 2, 0, 20, 1,   0,  0,  0, 0.0, 0.05, 0.2, 0.0, 1,  80000),
    (30110, '강철 투구',     1, 2, 3, 0, 10, 1,  80,  0, 12, 0.0, 0.00, 0.0, 0.0, 1,  12000),
    (30105, '코스믹 투구',   1, 6, 3, 0, 40, 1, 220,  0, 30, 0.0, 0.00, 0.0, 0.0, 1, 200000),
    (30130, '가죽 갑옷',     1, 2, 4, 0, 5,  1,  60,  0,  0, 0.0, 0.00, 0.0, 0.0, 1,   8000),
    (30120, '판금 갑옷',     1, 3, 4, 0, 15, 1, 150,  0, 25, 0.0, 0.00, 0.0, 0.0, 1,  60000),
    (30140, '강철 장갑',     1, 2, 5, 0, 10, 1,   0, 10,  6, 0.0, 0.00, 0.0, 0.0, 1,  10000),
    (30150, '신속의 장화',   1, 3, 6, 0, 15, 1,   0,  0,  8, 0.5, 0.00, 0.0, 0.0, 1,  45000),
    (30160, '마력 부츠',     1, 4, 6, 0, 20, 1,   0,  0,  0, 0.6, 0.00, 0.0, -0.1, 1,  90000),
    (41001, '강화석',        2, 2, 0, 0, 0, 999,  0,  0,  0, 0.0, 0.00, 0.0, 0.0, 1,   1000),
    (41002, '상급 강화석',   2, 3, 0, 0, 0, 999,  0,  0,  0, 0.0, 0.00, 0.0, 0.0, 1,   5000),
    (41010, '마력의 정수',   2, 4, 0, 0, 0, 999,  0,  0,  0, 0.0, 0.00, 0.0, 0.0, 1,  15000),
    (41020, '용의 비늘',     2, 5, 0, 0, 0,  99,  0,  0,  0, 0.0, 0.00, 0.0, 0.0, 1,  40000);


-- =====================================================================
-- 8. cube_master — 큐브(Hero-dric Cube) 레벨별 규칙 (값 문서 §8-A)
--    출처: master-data-값.md §8, 기획서 5.11
--    기획서의 synthesis_rule(JSON)은 폐기(JSON 컬럼 금지 규칙). 레벨별 고정 규칙은 개별 컬럼,
--    제작 레시피(재료 배열)는 자식 테이블(cube_recipe/cube_recipe_ingredient)로 분리한다.
--    combine_grade_up: 합성 등급 상승 허용(0/1). combine_count: 합성 소모 개수.
--    gold_per_scrap: 분해 골드 계수(전환 골드 = gold_per_scrap * 아이템 등급, 서버 산출).
-- =====================================================================
DROP TABLE IF EXISTS cube_master;
CREATE TABLE cube_master (
    cube_level        INT     NOT NULL COMMENT '큐브 레벨',
    required_exp      BIGINT  NOT NULL COMMENT '다음 레벨 요구 경험치(최대 레벨은 0)',
    combine_grade_up  TINYINT NOT NULL DEFAULT 1 COMMENT '합성 등급 상승 허용(0/1)',
    combine_count     INT     NOT NULL COMMENT '합성 소모 개수(같은 등급 N개 → 1등급 상승 1개)',
    gold_per_scrap    BIGINT  NOT NULL COMMENT '분해 골드 계수(전환 골드=gold_per_scrap*아이템 등급)',
    PRIMARY KEY (cube_level)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='큐브 레벨별 합성/분해 규칙';

INSERT INTO cube_master (cube_level, required_exp, combine_grade_up, combine_count, gold_per_scrap) VALUES
    (1, 1000,  1, 3, 100),
    (2, 3000,  1, 3, 150),
    (3, 6000,  1, 3, 220),
    (4, 12000, 1, 3, 320),
    (5, 0,     1, 3, 450);


-- =====================================================================
-- 8b. cube_recipe — 큐브 제작 레시피 헤더 (값 문서 §8-B)
--    출처: master-data-값.md §8, 기획서 5.11 / 인벤토리·큐브 기획서 5.8
--    제작(craft)은 기획서상 보류(우선순위 낮음)이나 레시피 데이터는 미리 채워 둔다.
--    result_item_code는 item_master(제작 결과 아이템)를 참조.
-- =====================================================================
DROP TABLE IF EXISTS cube_recipe;
CREATE TABLE cube_recipe (
    recipe_code       INT    NOT NULL COMMENT '레시피 코드',
    result_item_code  INT    NOT NULL COMMENT '제작 결과 아이템(item_master.item_code)',
    result_quantity   INT    NOT NULL DEFAULT 1 COMMENT '제작 결과 수량',
    req_cube_level    INT    NOT NULL COMMENT '요구 큐브 레벨(cube_master.cube_level)',
    cost_gold         BIGINT NOT NULL DEFAULT 0 COMMENT '제작 비용 골드',
    PRIMARY KEY (recipe_code),
    KEY idx_cube_recipe_result (result_item_code),
    CONSTRAINT fk_cube_recipe_result FOREIGN KEY (result_item_code)
        REFERENCES item_master (item_code) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='큐브 제작 레시피 헤더';

INSERT INTO cube_recipe (recipe_code, result_item_code, result_quantity, req_cube_level, cost_gold) VALUES
    (8001, 30012, 1, 2,  10000),
    (8002, 30120, 1, 2,  12000),
    (8003, 30160, 1, 3,  20000),
    (8004, 30105, 1, 5, 100000);


-- =====================================================================
-- 8c. cube_recipe_ingredient — 큐브 제작 레시피 소모 재료 (cube_recipe 자식, 값 문서 §8-C)
--    출처: master-data-값.md §8. 소모 재료는 전부 item_master의 재료(item_type=2).
--    (recipe_code, material_code) 복합 PK. material_code→item_master 참조.
-- =====================================================================
DROP TABLE IF EXISTS cube_recipe_ingredient;
CREATE TABLE cube_recipe_ingredient (
    recipe_code    INT NOT NULL COMMENT '레시피(cube_recipe.recipe_code)',
    material_code  INT NOT NULL COMMENT '소모 재료(item_master.item_code, item_type=2 재료)',
    quantity       INT NOT NULL COMMENT '소모 수량',
    PRIMARY KEY (recipe_code, material_code),
    KEY idx_cube_ingredient_material (material_code),
    CONSTRAINT fk_cube_ingredient_recipe FOREIGN KEY (recipe_code)
        REFERENCES cube_recipe (recipe_code) ON DELETE CASCADE,
    CONSTRAINT fk_cube_ingredient_material FOREIGN KEY (material_code)
        REFERENCES item_master (item_code) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='큐브 제작 레시피 소모 재료(자식)';

INSERT INTO cube_recipe_ingredient (recipe_code, material_code, quantity) VALUES
    (8001, 41001, 5),
    (8001, 41002, 2),
    (8002, 41001, 5),
    (8002, 41002, 2),
    (8003, 41002, 3),
    (8003, 41010, 1),
    (8004, 41010, 5),
    (8004, 41020, 2);


-- =====================================================================
-- 9. monster_master — 몬스터 전투 스탯 (값 문서 §9)
--    출처: master-data-값.md §9, 기획서 5.8
--    코드 규약: Act1 90xx / Act2 91xx / Act3 92xx, 각 Act 보스는 xx99.
--    보상은 스테이지 단위(stage_reward)로 일원화되어 몬스터 개별 드롭 컬럼은 없다.
-- =====================================================================
DROP TABLE IF EXISTS monster_master;
CREATE TABLE monster_master (
    monster_code     INT         NOT NULL COMMENT '몬스터 코드(Act1 90xx/Act2 91xx/Act3 92xx, 보스 xx99)',
    name             VARCHAR(40) NOT NULL COMMENT '몬스터 이름',
    hp               BIGINT      NOT NULL COMMENT '체력',
    attack           BIGINT      NOT NULL COMMENT '공격력',
    PRIMARY KEY (monster_code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='몬스터 전투 스탯';

INSERT INTO monster_master (monster_code, name, hp, attack) VALUES
    (9001, '슬라임',              500,   20),
    (9002, '고블린',              800,   35),
    (9003, '늑대',                1200,  55),
    (9099, '오크 군주 (Act1 보스)', 25000, 180),
    (9101, '스켈레톤',            3000,  120),
    (9102, '가고일',              4500,  170),
    (9103, '리치 견습',           6000,  240),
    (9199, '본 드래곤 (Act2 보스)', 120000, 600),
    (9201, '데몬',                15000, 500),
    (9299, '마왕 (Act3 보스)',     500000, 1500);


-- =====================================================================
-- 11. stage_master — 스테이지 구성(보스) (값 문서 §11)
--    출처: master-data-값.md §11, 기획서 5.9
--    stage_id = act*1000000 + difficulty*10000 + stage.
--    보상은 분리되어 stage_reward가 담당한다(구 reward_gold/reward_exp/drop_table_code 제거).
--    스폰(등장 일반 몬스터)은 JSON 컬럼을 쓰지 않고 stage_spawn 자식 테이블로 분리한다(설계 규칙).
--    3 Act × 2 난이도 × 3 스테이지 = 18종. 각 Act 스테이지 3이 보스(boss_monster_code), 나머지 0.
-- =====================================================================
DROP TABLE IF EXISTS stage_master;
CREATE TABLE stage_master (
    stage_id           INT     NOT NULL COMMENT '스테이지 키(act*1000000+difficulty*10000+stage)',
    act                TINYINT NOT NULL COMMENT 'Act(1~3)',
    difficulty         TINYINT NOT NULL COMMENT '난이도(1~2)',
    stage              INT     NOT NULL COMMENT '스테이지 번호',
    boss_monster_code  INT     NOT NULL DEFAULT 0 COMMENT '보스 몬스터(monster_master), 없으면 0',
    PRIMARY KEY (stage_id),
    KEY idx_stage_ads (act, difficulty, stage)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='스테이지 구성(보스). 스폰은 stage_spawn, 보상은 stage_reward';

INSERT INTO stage_master (stage_id, act, difficulty, stage, boss_monster_code) VALUES
    (1010001, 1, 1, 1, 0),
    (1010002, 1, 1, 2, 0),
    (1010003, 1, 1, 3, 9099),
    (1020001, 1, 2, 1, 0),
    (1020002, 1, 2, 2, 0),
    (1020003, 1, 2, 3, 9099),
    (2010001, 2, 1, 1, 0),
    (2010002, 2, 1, 2, 0),
    (2010003, 2, 1, 3, 9199),
    (2020001, 2, 2, 1, 0),
    (2020002, 2, 2, 2, 0),
    (2020003, 2, 2, 3, 9199),
    (3010001, 3, 1, 1, 0),
    (3010002, 3, 1, 2, 0),
    (3010003, 3, 1, 3, 9299),
    (3020001, 3, 2, 1, 0),
    (3020002, 3, 2, 2, 0),
    (3020003, 3, 2, 3, 9299);


-- =====================================================================
-- 11b. stage_spawn — 스테이지별 등장 일반 몬스터 (stage_master 자식, 값 문서 §11-B)
--    구 stage_master.spawns(JSON 배열)를 대체하는 자식 테이블(JSON 컬럼 미사용 규칙).
--    (stage_id, monster_code) 복합 PK. 보스는 여기 넣지 않고 stage_master.boss_monster_code로 둔다.
-- =====================================================================
DROP TABLE IF EXISTS stage_spawn;
CREATE TABLE stage_spawn (
    stage_id      INT NOT NULL COMMENT '스테이지(stage_master.stage_id)',
    monster_code  INT NOT NULL COMMENT '등장 일반 몬스터(monster_master.monster_code)',
    spawn_count   INT NOT NULL COMMENT '등장 마리 수',
    PRIMARY KEY (stage_id, monster_code),
    KEY idx_stage_spawn_monster (monster_code),
    CONSTRAINT fk_stage_spawn_stage FOREIGN KEY (stage_id)
        REFERENCES stage_master (stage_id) ON DELETE CASCADE,
    CONSTRAINT fk_stage_spawn_monster FOREIGN KEY (monster_code)
        REFERENCES monster_master (monster_code) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='스테이지별 등장 일반 몬스터(스폰)';

INSERT INTO stage_spawn (stage_id, monster_code, spawn_count) VALUES
    (1010001, 9001, 8),
    (1010001, 9002, 4),
    (1010002, 9002, 6),
    (1010002, 9003, 3),
    (1010003, 9003, 5),
    (1020001, 9001, 8),
    (1020001, 9002, 4),
    (1020002, 9002, 6),
    (1020002, 9003, 3),
    (1020003, 9003, 5),
    (2010001, 9101, 8),
    (2010001, 9102, 4),
    (2010002, 9102, 6),
    (2010002, 9103, 3),
    (2010003, 9103, 5),
    (2020001, 9101, 8),
    (2020001, 9102, 4),
    (2020002, 9102, 6),
    (2020002, 9103, 3),
    (2020003, 9103, 5),
    (3010001, 9201, 10),
    (3010002, 9201, 8),
    (3010003, 9201, 6),
    (3020001, 9201, 10),
    (3020002, 9201, 8),
    (3020003, 9201, 6);


-- =====================================================================
-- 10. stage_reward — 스테이지 클리어 보상 (값 문서 §10, 구 drop_table_master)
--    출처: master-data-값.md §10, 기획서 5.10
--    스테이지 1개당 1행. reward_gold/reward_exp + 등급 1~6 아이템 드롭 확률(0~1).
--    클리어 시 등급을 추첨해 그 등급의 item_master 아이템 하나 지급, 확률 합<1이면 미드롭.
--    stage_id는 stage_master를 참조(FK).
-- =====================================================================
DROP TABLE IF EXISTS stage_reward;
CREATE TABLE stage_reward (
    stage_id     INT          NOT NULL COMMENT '참조 스테이지(stage_master.stage_id)',
    reward_gold  BIGINT       NOT NULL COMMENT '클리어 획득 골드',
    reward_exp   BIGINT       NOT NULL COMMENT '클리어 획득 경험치(3캐릭터 공통)',
    grade1_prob  DECIMAL(5,4) NOT NULL DEFAULT 0 COMMENT '등급1 아이템 드롭 확률(0~1)',
    grade2_prob  DECIMAL(5,4) NOT NULL DEFAULT 0 COMMENT '등급2 아이템 드롭 확률(0~1)',
    grade3_prob  DECIMAL(5,4) NOT NULL DEFAULT 0 COMMENT '등급3 아이템 드롭 확률(0~1)',
    grade4_prob  DECIMAL(5,4) NOT NULL DEFAULT 0 COMMENT '등급4 아이템 드롭 확률(0~1)',
    grade5_prob  DECIMAL(5,4) NOT NULL DEFAULT 0 COMMENT '등급5 아이템 드롭 확률(0~1)',
    grade6_prob  DECIMAL(5,4) NOT NULL DEFAULT 0 COMMENT '등급6 아이템 드롭 확률(0~1)',
    PRIMARY KEY (stage_id),
    CONSTRAINT fk_stage_reward_stage FOREIGN KEY (stage_id)
        REFERENCES stage_master (stage_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='스테이지 클리어 보상(골드·경험치·등급별 드롭 확률)';

INSERT INTO stage_reward (stage_id, reward_gold, reward_exp, grade1_prob, grade2_prob, grade3_prob, grade4_prob, grade5_prob, grade6_prob) VALUES
    (1010001, 100, 50, 0.30, 0.10, 0.02, 0.00, 0.00, 0.00),
    (1010002, 120, 60, 0.30, 0.12, 0.03, 0.00, 0.00, 0.00),
    (1010003, 500, 250, 0.20, 0.25, 0.10, 0.03, 0.00, 0.00),
    (1020001, 250, 125, 0.25, 0.15, 0.05, 0.00, 0.00, 0.00),
    (1020002, 300, 150, 0.25, 0.15, 0.06, 0.00, 0.00, 0.00),
    (1020003, 1200, 600, 0.00, 0.25, 0.15, 0.05, 0.00, 0.00),
    (2010001, 800, 400, 0.00, 0.20, 0.10, 0.03, 0.00, 0.00),
    (2010002, 1000, 500, 0.00, 0.20, 0.12, 0.05, 0.00, 0.00),
    (2010003, 4000, 2000, 0.00, 0.00, 0.20, 0.12, 0.05, 0.01),
    (2020001, 2000, 1000, 0.00, 0.15, 0.15, 0.05, 0.00, 0.00),
    (2020002, 2500, 1250, 0.00, 0.00, 0.20, 0.10, 0.03, 0.00),
    (2020003, 10000, 5000, 0.00, 0.00, 0.15, 0.15, 0.08, 0.02),
    (3010001, 6000, 3000, 0.00, 0.00, 0.15, 0.15, 0.05, 0.00),
    (3010002, 7000, 3500, 0.00, 0.00, 0.15, 0.15, 0.06, 0.00),
    (3010003, 30000, 15000, 0.00, 0.00, 0.00, 0.20, 0.12, 0.05),
    (3020001, 15000, 7500, 0.00, 0.00, 0.00, 0.15, 0.10, 0.02),
    (3020002, 17000, 8500, 0.00, 0.00, 0.00, 0.15, 0.12, 0.03),
    (3020003, 70000, 35000, 0.00, 0.00, 0.00, 0.15, 0.15, 0.08);


-- =====================================================================
-- 13. attendance_master — 출석부 일자별(day-of-month) 보상 (값 문서 §13)
--    출처: master-data-값.md §13, 기획서 5.14
--    reward_type: 1=골드 2=아이템 3=재료 (item_master.item_type와 별개의 enum).
--    골드는 reward_code=0(수량이 골드량), 아이템·재료는 reward_code가 item_master.item_code.
--      reward_code는 0 센티널(골드)이 섞이므로 FK를 걸지 않고 애플리케이션에서 검증한다.
--    day 1~31 전부 정의. 주간 마일스톤(7·14·21·28)과 15·31일에 고가치 보상 배치.
-- =====================================================================
DROP TABLE IF EXISTS attendance_master;
CREATE TABLE attendance_master (
    day          TINYINT NOT NULL COMMENT '이달 며칠차(1~31)',
    reward_type  TINYINT NOT NULL COMMENT '보상 종류(1=골드 2=아이템 3=재료)',
    reward_code  INT     NOT NULL DEFAULT 0 COMMENT '아이템/재료 코드(item_master.item_code, 골드면 0)',
    quantity     BIGINT  NOT NULL COMMENT '지급 수량(골드면 골드량)',
    PRIMARY KEY (day)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='출석부 일자별 보상 정의';

INSERT INTO attendance_master (day, reward_type, reward_code, quantity) VALUES
    (1,  1, 0,     1000),
    (2,  1, 0,     1500),
    (3,  3, 41001, 3),
    (4,  1, 0,     2000),
    (5,  1, 0,     2500),
    (6,  3, 41001, 5),
    (7,  3, 41002, 3),
    (8,  1, 0,     3000),
    (9,  1, 0,     3500),
    (10, 3, 41001, 5),
    (11, 1, 0,     4000),
    (12, 1, 0,     4500),
    (13, 3, 41002, 3),
    (14, 3, 41010, 2),
    (15, 2, 30110, 1),
    (16, 1, 0,     5000),
    (17, 1, 0,     5500),
    (18, 3, 41002, 5),
    (19, 1, 0,     6000),
    (20, 1, 0,     6500),
    (21, 3, 41010, 3),
    (22, 1, 0,     7000),
    (23, 1, 0,     7500),
    (24, 3, 41002, 5),
    (25, 1, 0,     8000),
    (26, 1, 0,     8500),
    (27, 3, 41010, 3),
    (28, 3, 41020, 1),
    (29, 1, 0,     9000),
    (30, 3, 41010, 5),
    (31, 2, 30105, 1);


SET FOREIGN_KEY_CHECKS = 1;

-- =====================================================================
-- 끝. (7 enhance/12 box는 값 확정 후 추가한다.)
-- =====================================================================
