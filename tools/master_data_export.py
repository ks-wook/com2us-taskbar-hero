#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
마스터 데이터 추출기 (DB -> Unity 클라이언트 JSON 번들)

흐름:
  1) master-data-schema.sql 을 MySQL(master DB)에 재적용(DROP+CREATE+INSERT) 하여 DB를 SQL 파일 최신 상태로 맞춘다.
  2) 각 마스터 테이블을 조회한다.
  3) 기획서(master-data-기획서.md) §7 규약대로 "테이블별 camelCase JSON 배열"로 직렬화한다.
     - 자식 테이블(skill_coefficient / stage_spawn / cube_recipe_ingredient)은 부모 JSON에 배열로 중첩한다.
     - 스탯 컬럼(hp~cooldown)은 baseStats/statBonus 객체로 묶는다.
  4) 클라이언트의 Assets/Resources/MasterData/*.json 으로 출력한다.

대상 DB — **컨테이너와 로컬 설치본을 모두 갱신하되, 접속되는 것만 한다.**
  로컬 개발에는 MySQL이 두 벌 있을 수 있다(docker compose의 mysql = 호스트 33306, 이 PC에 설치한 MySQL = 3306).
  둘 중 어느 쪽에 붙어 개발하든 정본 SQL이 반영돼 있어야 하므로, 이 스크립트는 **선택된 대상 전부에 schema.sql을
  적용**하고 **접속되지 않는 대상은 건너뛴다**(하나도 못 붙으면 실패로 끝낸다). 클라 JSON 번들은 대상마다 같은
  SQL을 적용해 내용이 같으므로 **한 번만** 쓴다(--export-from 으로 어느 대상에서 뽑을지 고를 수 있다).

정본:
  - 값:    docs/세부/master-data/master-data-값.md
  - 구조:  docs/세부/master-data/master-data-기획서.md  (§7 클라 번들 포맷)
  - SQL:   docs/세부/master-data/master-data-schema.sql

의존성: pymysql (pip install pymysql)
사용:
  python master_data_export.py                          # 컨테이너 + 로컬 중 붙는 것 전부 갱신
  python master_data_export.py --targets local          # 로컬 설치본만
  python master_data_export.py --local-password <비밀번호>   # 로컬 계정 비밀번호가 기본값과 다를 때
  python master_data_export.py --host 127.0.0.1 --port 3306 --user root --password pw
                                                        # 대상을 하나만 직접 지정(옛 사용법 — 그 대상만 갱신)
  python master_data_export.py --no-apply               # schema.sql 재적용 없이 현재 DB에서 번들만 추출
"""
import argparse
import json
import os
import sys
from decimal import Decimal

# Unity 에디터 툴이 stdout/stderr 를 캡처해 표시하므로 한글 깨짐 방지를 위해 UTF-8 로 고정한다.
for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding="utf-8")
    except Exception:
        pass

try:
    import pymysql
    import pymysql.cursors
except ImportError:
    sys.stderr.write("[ERROR] pymysql 가 필요합니다. 설치: python -m pip install pymysql\n")
    sys.exit(2)

# 이 스크립트 위치(repo/tools/) 기준 기본 경로
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.dirname(SCRIPT_DIR)
DEFAULT_SCHEMA = os.path.join(REPO_ROOT, "docs", "세부", "master-data", "master-data-schema.sql")
DEFAULT_OUTPUT = os.path.join(REPO_ROOT, "com2us-taskbar-hero-client", "Assets", "Resources", "MasterData")
DEFAULT_DB = "taskbar_hero_master"

# ---------------------------------------------------------------------------
# 갱신 대상 MySQL
#   로컬 개발에는 MySQL이 두 벌 있을 수 있다 — compose의 컨테이너(호스트 33306)와 이 PC에 설치한 MySQL(3306).
#   어느 쪽에 붙어 개발하든 정본이 반영돼 있어야 하므로 기본값은 **둘 다**이고, 접속되지 않는 쪽은 건너뛴다.
#   비밀번호는 환경 변수로 덮어쓸 수 있다(컨테이너 쪽은 compose에 고정된 개발용 값이라 그대로 둔다).
# ---------------------------------------------------------------------------
TARGET_DEFAULTS = {
    "container": {
        "label": "컨테이너 MySQL",
        "host": "127.0.0.1",
        "port": 33306,
        "user": "root",
        "password": os.environ.get("TASKBAR_HERO_MYSQL_CONTAINER_PASSWORD", "taskbar_hero_dev"),
    },
    "local": {
        "label": "로컬 설치 MySQL",
        "host": "127.0.0.1",
        "port": 3306,
        "user": "root",
        "password": os.environ.get("TASKBAR_HERO_MYSQL_LOCAL_PASSWORD", "root"),
    },
}
TARGET_ORDER = ("container", "local")


# ---------------------------------------------------------------------------
# 값 변환 헬퍼
# ---------------------------------------------------------------------------
def _i(v):
    """정수 컬럼 -> int"""
    return int(v) if v is not None else 0


def _f(v):
    """실수 컬럼(DECIMAL 포함) -> float"""
    if v is None:
        return 0.0
    if isinstance(v, Decimal):
        return float(v)
    return float(v)


def _s(v):
    return "" if v is None else str(v)


def stats(row, prefix=""):
    """행에서 스탯 7종을 baseStats/statBonus 객체로 묶는다. 없는 필드는 0."""
    def g(col):
        return row.get(prefix + col)
    return {
        "hp": _i(g("hp")),
        "atk": _i(g("atk")),
        "def": _i(g("def")),
        "moveSpeed": _f(g("move_speed")),
        "critChance": _f(g("crit_chance")),
        "critDamage": _f(g("crit_damage")),
        "cooldown": _f(g("cooldown")),
    }


# ---------------------------------------------------------------------------
# schema.sql 적용
# ---------------------------------------------------------------------------
def _strip_sql_comments(sql_text):
    """줄 단위로 '--' 주석(전체/인라인)을 제거한다.
    본 프로젝트 시드 SQL은 문자열 리터럴 안에 '--' 를 포함하지 않으므로 안전하다.
    (음수 '-0.1' 은 대시 1개라 영향 없음)."""
    out_lines = []
    for line in sql_text.splitlines():
        idx = line.find("--")
        if idx >= 0:
            line = line[:idx]
        out_lines.append(line)
    return "\n".join(out_lines)


def apply_schema(conn, schema_path):
    with open(schema_path, "r", encoding="utf-8") as f:
        raw = f.read()
    cleaned = _strip_sql_comments(raw)
    statements = [s.strip() for s in cleaned.split(";")]
    statements = [s for s in statements if s]
    with conn.cursor() as cur:
        for stmt in statements:
            cur.execute(stmt)
    conn.commit()
    return len(statements)


# ---------------------------------------------------------------------------
# 테이블별 추출 (기획서 §7 camelCase)
# ---------------------------------------------------------------------------
def q(conn, sql):
    with conn.cursor(pymysql.cursors.DictCursor) as cur:
        cur.execute(sql)
        return cur.fetchall()


def export_equip_slot(conn):
    rows = q(conn, "SELECT slot, name FROM equip_slot_master ORDER BY slot")
    return [{"slot": _i(r["slot"]), "name": _s(r["name"])} for r in rows]


def export_grade(conn):
    rows = q(conn, "SELECT grade, name FROM grade_master ORDER BY grade")
    return [{"grade": _i(r["grade"]), "name": _s(r["name"])} for r in rows]


def export_class(conn):
    rows = q(conn, "SELECT * FROM class_master ORDER BY class_code")
    return [{
        "classCode": _i(r["class_code"]),
        "name": _s(r["name"]),
        # 클라 표시 전용(캐릭터 생성 화면 직업 설명). skill_master.description 과 동일 취급.
        "description": _s(r["description"]),
        "unlockType": _i(r["unlock_type"]),
        "baseStats": stats(r),
    } for r in rows]


def export_level(conn):
    rows = q(conn, "SELECT * FROM level_master ORDER BY level")
    result = []
    for r in rows:
        result.append({
            "level": _i(r["level"]),
            "requiredExp": _i(r["required_exp"]),
            "skillPoints": _i(r["skill_points"]),
            # statBonus = Stats(hp/atk/def 만 값, 나머지 0)
            "statBonus": {
                "hp": _i(r["bonus_hp"]), "atk": _i(r["bonus_atk"]), "def": _i(r["bonus_def"]),
                "moveSpeed": 0.0, "critChance": 0.0, "critDamage": 0.0, "cooldown": 0.0,
            },
        })
    return result


def export_item(conn):
    rows = q(conn, "SELECT * FROM item_master ORDER BY item_code")
    return [{
        "itemCode": _i(r["item_code"]),
        "name": _s(r["name"]),
        "itemType": _i(r["item_type"]),
        "grade": _i(r["grade"]),
        "equipSlot": _i(r["equip_slot"]),
        "classReq": _i(r["class_req"]),
        "levelReq": _i(r["level_req"]),
        "stackMax": _i(r["stack_max"]),
        "baseStats": stats(r),
        "sellable": _i(r["sellable"]),
        "basePrice": _i(r["base_price"]),
    } for r in rows]


def export_enhance(conn):
    """장비 강화 단계별 비용·스탯 배율(enhance_master).

    기획서 5.4의 stat_multiplier(JSON)는 폐기되어 단일 DECIMAL 배율 컬럼이다(JSON 컬럼 금지 규칙).
    클라이언트는 이 배율을 장비 baseStats 전체에 곱해 강화된 장비 스탯을 표시·계산한다.
    """
    rows = q(conn, "SELECT * FROM enhance_master ORDER BY enhance_level")
    return [{
        "enhanceLevel": _i(r["enhance_level"]),
        "cost": _i(r["cost"]),
        "currencyType": _i(r["currency_type"]),
        "statMultiplier": _f(r["stat_multiplier"]),
    } for r in rows]


def export_skill(conn):
    skills = q(conn, "SELECT * FROM skill_master ORDER BY skill_code")
    coefs = q(conn, "SELECT * FROM skill_coefficient ORDER BY skill_code, skill_level, coef_type")
    by_skill = {}
    for c in coefs:
        by_skill.setdefault(_i(c["skill_code"]), []).append({
            "skillLevel": _i(c["skill_level"]),
            "coefType": _i(c["coef_type"]),
            "coef": _f(c["coef"]),
            "duration": _f(c["duration"]),
        })
    return [{
        "skillCode": _i(s["skill_code"]),
        "classCode": _i(s["class_code"]),
        "name": _s(s["name"]),
        "description": _s(s["description"]),
        "skillType": _i(s["skill_type"]),
        "statType": _i(s["stat_type"]),
        "coefs": by_skill.get(_i(s["skill_code"]), []),
        "maxLevel": _i(s["max_level"]),
        "cooldown": _f(s["cooldown"]),
    } for s in skills]


def export_rune(conn):
    runes = q(conn, "SELECT * FROM rune_master ORDER BY rune_code")
    # 레벨별 골드 비용은 자식 테이블 rune_cost 에 명시되어 있다(공식 파생 아님). 부모 JSON 에 costs 배열로 중첩한다.
    costs = q(conn, "SELECT * FROM rune_cost ORDER BY rune_code, level")
    by_rune = {}
    for c in costs:
        by_rune.setdefault(_i(c["rune_code"]), []).append({
            "level": _i(c["level"]),
            "cost": _i(c["cost"]),
        })
    return [{
        "runeCode": _i(r["rune_code"]),
        "name": _s(r["name"]),
        "prereqCode": _i(r["prereq_code"]),
        "costs": by_rune.get(_i(r["rune_code"]), []),
        "maxLevel": _i(r["max_level"]),
        "statType": _i(r["stat_type"]),
        "statValue": _f(r["stat_value"]),
    } for r in runes]


def export_monster(conn):
    rows = q(conn, "SELECT * FROM monster_master ORDER BY monster_code")
    return [{
        "monsterCode": _i(r["monster_code"]),
        "name": _s(r["name"]),
        "hp": _i(r["hp"]),
        "attack": _i(r["attack"]),
    } for r in rows]


def export_stage(conn):
    # stage_spawn 은 일반 몬스터와 보스를 한 테이블에 담는다(is_boss). 클라 번들 StageMaster 는
    # 둘을 나눠 들고 있으므로 여기서 spawns[] 와 bossMonsterCode/bossMonsterLevel 로 갈라 투영한다.
    stages = q(conn, "SELECT * FROM stage_master ORDER BY stage_id")
    spawns = q(conn, "SELECT * FROM stage_spawn ORDER BY stage_id, monster_code")
    by_stage = {}
    boss_by_stage = {}
    for sp in spawns:
        sid = _i(sp["stage_id"])
        if _i(sp["is_boss"]):
            boss_by_stage[sid] = (_i(sp["monster_code"]), _i(sp["monster_level"]))
            continue
        by_stage.setdefault(sid, []).append({
            "monsterCode": _i(sp["monster_code"]),
            "monsterLevel": _i(sp["monster_level"]),
            "count": _i(sp["spawn_count"]),
        })
    result = []
    for s in stages:
        sid = _i(s["stage_id"])
        boss_code, boss_level = boss_by_stage.get(sid, (0, 0))
        result.append({
            "stageId": sid,
            "act": _i(s["act"]),
            "difficulty": _i(s["difficulty"]),
            "stage": _i(s["stage"]),
            "spawns": by_stage.get(sid, []),
            "bossMonsterCode": boss_code,
            "bossMonsterLevel": boss_level,
            "backgroundType": _i(s["background_type"]),
        })
    return result


def export_stage_reward(conn):
    # 등급별 드롭 확률은 자식 테이블 stage_reward_drop 에 정규화되어 있다(확률 0 등급은 행 없음).
    # 클라이언트 StageReward 모델은 평탄한 grade1Prob~grade5Prob 를 기대하므로, 자식 행을 그 형태로 투영한다.
    rewards = q(conn, "SELECT * FROM stage_reward ORDER BY stage_id")
    drops = q(conn, "SELECT * FROM stage_reward_drop ORDER BY stage_id, grade")
    prob_by_stage = {}
    for d in drops:
        prob_by_stage.setdefault(_i(d["stage_id"]), {})[_i(d["grade"])] = _f(d["drop_prob"])
    result = []
    for r in rewards:
        probs = prob_by_stage.get(_i(r["stage_id"]), {})
        result.append({
            "stageId": _i(r["stage_id"]),
            "rewardGold": _i(r["reward_gold"]),
            "rewardExp": _i(r["reward_exp"]),
            "grade1Prob": probs.get(1, 0.0),
            "grade2Prob": probs.get(2, 0.0),
            "grade3Prob": probs.get(3, 0.0),
            "grade4Prob": probs.get(4, 0.0),
            "grade5Prob": probs.get(5, 0.0),
        })
    return result


def export_cube(conn):
    rows = q(conn, "SELECT * FROM cube_master ORDER BY cube_level")
    return [{
        "cubeLevel": _i(r["cube_level"]),
        "requiredExp": _i(r["required_exp"]),
        "combineGradeUp": _i(r["combine_grade_up"]),
        "combineCount": _i(r["combine_count"]),
        "goldPerScrap": _i(r["gold_per_scrap"]),
    } for r in rows]


def export_cube_recipe(conn):
    recipes = q(conn, "SELECT * FROM cube_recipe ORDER BY recipe_code")
    ings = q(conn, "SELECT * FROM cube_recipe_ingredient ORDER BY recipe_code, material_code")
    by_recipe = {}
    for g in ings:
        by_recipe.setdefault(_i(g["recipe_code"]), []).append({
            "materialCode": _i(g["material_code"]),
            "quantity": _i(g["quantity"]),
        })
    return [{
        "recipeCode": _i(r["recipe_code"]),
        "resultItemCode": _i(r["result_item_code"]),
        "resultQuantity": _i(r["result_quantity"]),
        "reqCubeLevel": _i(r["req_cube_level"]),
        "costGold": _i(r["cost_gold"]),
        "ingredients": by_recipe.get(_i(r["recipe_code"]), []),
    } for r in recipes]


def export_attendance(conn):
    rows = q(conn, "SELECT * FROM attendance_master ORDER BY day")
    return [{
        "day": _i(r["day"]),
        "rewardType": _i(r["reward_type"]),
        "rewardCode": _i(r["reward_code"]),
        "quantity": _i(r["quantity"]),
    } for r in rows]


def export_character_create_cost(conn):
    rows = q(conn, "SELECT * FROM character_create_cost ORDER BY character_id")
    return [{
        "characterId": _i(r["character_id"]),
        "goldCost": _i(r["gold_cost"]),
    } for r in rows]


def export_inventory_expand_cost(conn):
    rows = q(conn, "SELECT * FROM inventory_expand_master ORDER BY step")
    return [{
        "step": _i(r["step"]),
        "goldCost": _i(r["gold_cost"]),
    } for r in rows]


def export_gacha(conn):
    """가챠 배너 + 자식 3종(등급 가중치 / 지급 후보 / 천장 규칙)을 배너 JSON에 중첩한다.

    클라이언트는 이 번들로 배너 이름·이미지·비용·등급 확률·후보 목록·천장 기준을 그리고,
    "지금 열려 있는가 · 내 천장이 얼마인가"만 서버(POST /api/game/gacha/banners)에서 받는다.
    """
    banners = q(conn, "SELECT * FROM gacha_master ORDER BY sort_order, gacha_code")
    weights = q(conn, "SELECT * FROM gacha_grade_weight ORDER BY gacha_code, grade")
    pools = q(conn, "SELECT * FROM gacha_item_pool ORDER BY gacha_code, grade, item_code")
    pities = q(conn, "SELECT * FROM gacha_pity_rule ORDER BY gacha_code, grade, pity_type")

    by_weight, by_pool, by_pity = {}, {}, {}
    for w in weights:
        by_weight.setdefault(_i(w["gacha_code"]), []).append({
            "grade": _i(w["grade"]),
            "weight": _i(w["weight"]),
        })
    for p in pools:
        by_pool.setdefault(_i(p["gacha_code"]), []).append({
            "grade": _i(p["grade"]),
            "itemCode": _i(p["item_code"]),
            "quantity": _i(p["quantity"]),
        })
    for r in pities:
        by_pity.setdefault(_i(r["gacha_code"]), []).append({
            "grade": _i(r["grade"]),
            "pityType": _i(r["pity_type"]),
            "threshold": _i(r["threshold"]),
            "probStep": _f(r["prob_step"]),
        })

    result = []
    for b in banners:
        code = _i(b["gacha_code"])
        result.append({
            "gachaCode": code,
            "name": b["name"],
            "bannerImage": b["banner_image"],
            "isActive": _i(b["is_active"]),
            "openAt": _i(b["open_at"]),
            "closeAt": _i(b["close_at"]),
            "sortOrder": _i(b["sort_order"]),
            "costCurrencyCode": _i(b["cost_currency_code"]),
            "costSingle": _i(b["cost_single"]),
            "costMulti": _i(b["cost_multi"]),
            "multiCount": _i(b["multi_count"]),
            "multiGuaranteedGrade": _i(b["multi_guaranteed_grade"]),
            "pickupItemCode": _i(b["pickup_item_code"]),
            "gradeWeights": by_weight.get(code, []),
            "itemPool": by_pool.get(code, []),
            "pityRules": by_pity.get(code, []),
        })
    return result


def export_boss_rush(conn):
    """보스러시 전역 규칙(단일 행)."""
    rows = q(conn, "SELECT * FROM boss_rush_master ORDER BY content_id")
    return [{
        "contentId": _i(r["content_id"]),
        "roundCount": _i(r["round_count"]),
        "unlockStageSequence": _i(r["unlock_stage_sequence"]),
        "seasonPeriodDays": _i(r["season_period_days"]),
        "runExpireSec": _i(r["run_expire_sec"]),
        "rankPageLimit": _i(r["rank_page_limit"]),
    } for r in rows]


def export_boss_rush_round(conn):
    """보스러시 라운드 + 자식 스폰. stage_master 와 같은 방식으로 is_boss 행을 갈라 투영한다.

    라운드 r 은 Act r 의 전투이며 배경도 그 Act 의 스테이지 배경을 재활용한다
    (backgroundType 이 Act r 의 stage_master.background_type 과 같은 값).
    """
    rounds = q(conn, "SELECT * FROM boss_rush_round ORDER BY round")
    spawns = q(conn, "SELECT * FROM boss_rush_spawn ORDER BY round, monster_code")
    by_round, boss_by_round = {}, {}
    for sp in spawns:
        rnd = _i(sp["round"])
        if _i(sp["is_boss"]):
            boss_by_round[rnd] = (_i(sp["monster_code"]), _i(sp["monster_level"]))
            continue
        by_round.setdefault(rnd, []).append({
            "monsterCode": _i(sp["monster_code"]),
            "monsterLevel": _i(sp["monster_level"]),
            "count": _i(sp["spawn_count"]),
        })
    result = []
    for r in rounds:
        rnd = _i(r["round"])
        boss_code, boss_level = boss_by_round.get(rnd, (0, 0))
        result.append({
            "round": rnd,
            "backgroundType": _i(r["background_type"]),
            "spawns": by_round.get(rnd, []),
            "bossMonsterCode": boss_code,
            "bossMonsterLevel": boss_level,
        })
    return result


def export_boss_rush_rank_reward(conn):
    """보스러시 시즌 순위 보상(1~3위, 골드만). 지급 항목이 하나라 자식 테이블이 없다."""
    rows = q(conn, "SELECT * FROM boss_rush_rank_reward ORDER BY rank_group")
    return [{
        "rankGroup": _i(r["rank_group"]),
        "rankFrom": _i(r["rank_from"]),
        "rankTo": _i(r["rank_to"]),
        "rewardGold": _i(r["reward_gold"]),
    } for r in rows]


# 파일명(테이블명) -> 추출 함수
EXPORTERS = [
    ("equip_slot_master", export_equip_slot),
    ("grade_master", export_grade),
    ("class_master", export_class),
    ("level_master", export_level),
    ("skill_master", export_skill),
    ("rune_master", export_rune),
    ("item_master", export_item),
    ("enhance_master", export_enhance),
    ("monster_master", export_monster),
    ("stage_master", export_stage),
    ("stage_reward", export_stage_reward),
    ("cube_master", export_cube),
    ("cube_recipe", export_cube_recipe),
    ("attendance_master", export_attendance),
    ("character_create_cost", export_character_create_cost),
    ("inventory_expand_master", export_inventory_expand_cost),
    ("gacha_master", export_gacha),
    ("boss_rush_master", export_boss_rush),
    ("boss_rush_round", export_boss_rush_round),
    ("boss_rush_rank_reward", export_boss_rush_rank_reward),
]


def connect(target):
    """대상 하나에 접속한다(DB 미선택 — schema.sql이 CREATE DATABASE/USE 를 수행한다)."""
    return pymysql.connect(
        host=target["host"], port=target["port"], user=target["user"], password=target["password"],
        charset="utf8mb4", autocommit=False, connect_timeout=5,
    )


def resolve_targets(args):
    """갱신할 대상 목록을 확정한다.

    --host 등을 하나라도 주면 **그 대상 하나만** 갱신한다(옛 사용법과 같은 동작). 그러지 않으면
    --targets(기본 container,local)의 기본값에 --container-*/--local-* 로 준 값만 덮어쓴다.
    """
    if any(v is not None for v in (args.host, args.port, args.user, args.password)):
        base = TARGET_DEFAULTS["container"]
        return [{
            "key": "custom",
            "label": "지정한 MySQL",
            "host": args.host or base["host"],
            "port": args.port or base["port"],
            "user": args.user or base["user"],
            "password": args.password if args.password is not None else base["password"],
        }]

    keys = TARGET_ORDER if args.targets == "all" else tuple(
        k.strip() for k in args.targets.split(",") if k.strip())
    targets = []
    for key in keys:
        if key not in TARGET_DEFAULTS:
            sys.stderr.write(f"[ERROR] 알 수 없는 대상: {key} (가능한 값: {', '.join(TARGET_ORDER)}, all)\n")
            return None
        t = dict(TARGET_DEFAULTS[key], key=key)
        for field in ("host", "port", "user", "password"):
            override = getattr(args, f"{key}_{field}", None)
            if override is not None:
                t[field] = override
        targets.append(t)
    return targets


def main():
    ap = argparse.ArgumentParser(description="마스터 데이터 DB -> Unity JSON 추출기")
    ap.add_argument("--schema", default=DEFAULT_SCHEMA, help="master-data-schema.sql 경로")
    ap.add_argument("--output", default=DEFAULT_OUTPUT, help="JSON 출력 디렉터리(Assets/Resources/MasterData)")
    ap.add_argument("--targets", default="all",
                    help=f"갱신할 대상(콤마 구분: {', '.join(TARGET_ORDER)} · 기본 all = 둘 다, 접속되는 것만)")
    ap.add_argument("--export-from", default=None, choices=list(TARGET_ORDER) + ["custom"],
                    help="클라 번들을 뽑을 대상(생략하면 접속된 첫 대상)")
    for key in TARGET_ORDER:
        d = TARGET_DEFAULTS[key]
        ap.add_argument(f"--{key}-host", default=None, help=f"{d['label']} 호스트(기본 {d['host']})")
        ap.add_argument(f"--{key}-port", type=int, default=None, help=f"{d['label']} 포트(기본 {d['port']})")
        ap.add_argument(f"--{key}-user", default=None, help=f"{d['label']} 계정(기본 {d['user']})")
        ap.add_argument(f"--{key}-password", default=None, help=f"{d['label']} 비밀번호")
    # 옛 사용법 — 하나라도 주면 그 대상 하나만 갱신한다(기본값을 두지 않아 "줬는지"를 구분한다).
    ap.add_argument("--host", default=None, help="대상을 하나만 직접 지정할 때의 호스트")
    ap.add_argument("--port", type=int, default=None, help="대상을 하나만 직접 지정할 때의 포트")
    ap.add_argument("--user", default=None, help="대상을 하나만 직접 지정할 때의 계정")
    ap.add_argument("--password", default=None, help="대상을 하나만 직접 지정할 때의 비밀번호")
    ap.add_argument("--database", default=DEFAULT_DB)
    ap.add_argument("--no-apply", action="store_true", help="schema.sql 재적용을 생략하고 현재 DB에서만 추출")
    args = ap.parse_args()

    targets = resolve_targets(args)
    if targets is None:
        return 4
    if not args.no_apply and not os.path.isfile(args.schema):
        sys.stderr.write(f"[ERROR] schema 파일을 찾을 수 없습니다: {args.schema}\n")
        return 3

    # ── ① 대상별 접속 → 붙는 것만 갱신한다(안 붙는 대상은 건너뛴다) ──
    applied, skipped, conns = [], [], []
    for t in targets:
        where = f"{t['host']}:{t['port']}"
        try:
            conn = connect(t)
        except Exception as error:
            skipped.append((t, str(error).split("\n")[0]))
            print(f"[skip ] {t['label']}({where}) 접속 불가 — 건너뜁니다: {str(error)[:80]}")
            continue

        conns.append((t, conn))
        if args.no_apply:
            with conn.cursor() as cur:
                cur.execute(f"USE `{args.database}`")
            print(f"[apply] {t['label']}({where}) 생략(--no-apply). 기존 DB `{args.database}` 사용")
        else:
            n = apply_schema(conn, args.schema)
            print(f"[apply] {t['label']}({where}) schema.sql 재적용 완료: {n}개 문 실행")
        applied.append(t)

    try:
        if not applied:
            sys.stderr.write("[ERROR] 접속되는 MySQL이 없습니다 — 갱신한 대상이 없습니다.\n")
            for t, reason in skipped:
                sys.stderr.write(f"        {t['label']}({t['host']}:{t['port']}): {reason}\n")
            sys.stderr.write("        컨테이너는 `docker compose up -d mysql`, 로컬 설치본은 서비스 시작 후 다시 실행하세요.\n")
            return 5

        # ── ② 번들은 한 번만 쓴다(대상마다 같은 SQL을 적용했으므로 내용이 같다) ──
        pick = args.export_from
        source, conn = next(((t, c) for t, c in conns if pick is None or t["key"] == pick), (None, None))
        if conn is None:
            sys.stderr.write(f"[ERROR] --export-from {pick} 대상에 접속하지 못했습니다.\n")
            return 5

        os.makedirs(args.output, exist_ok=True)
        total_rows = 0
        for filename, fn in EXPORTERS:
            data = fn(conn)
            path = os.path.join(args.output, filename + ".json")
            with open(path, "w", encoding="utf-8") as f:
                json.dump(data, f, ensure_ascii=False, indent=2)
                f.write("\n")
            total_rows += len(data)
            print(f"[write] {filename}.json : {len(data)}건")

        print(f"[done] DB 갱신 {len(applied)}곳({' · '.join(t['label'] for t in applied)})"
              + (f" · 건너뜀 {len(skipped)}곳({' · '.join(t['label'] for t, _ in skipped)})" if skipped else "")
              + f" / 번들 {len(EXPORTERS)}개 파일 · 총 {total_rows}행 <- {source['label']} -> {args.output}")
        return 0
    finally:
        for _, conn in conns:
            conn.close()


if __name__ == "__main__":
    sys.exit(main())
