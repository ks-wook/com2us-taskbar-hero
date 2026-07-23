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

정본:
  - 값:    docs/세부/master-data/master-data-값.md
  - 구조:  docs/세부/master-data/master-data-기획서.md  (§7 클라 번들 포맷)
  - SQL:   docs/세부/master-data/master-data-schema.sql

의존성: pymysql (pip install pymysql)
사용: python master_data_export.py  [--schema PATH] [--output DIR] [--host] [--port] [--user] [--password] [--no-apply]
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
        "skillType": _i(s["skill_type"]),
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
    stages = q(conn, "SELECT * FROM stage_master ORDER BY stage_id")
    spawns = q(conn, "SELECT * FROM stage_spawn ORDER BY stage_id, monster_code")
    by_stage = {}
    for sp in spawns:
        by_stage.setdefault(_i(sp["stage_id"]), []).append({
            "monsterCode": _i(sp["monster_code"]),
            "count": _i(sp["spawn_count"]),
        })
    return [{
        "stageId": _i(s["stage_id"]),
        "act": _i(s["act"]),
        "difficulty": _i(s["difficulty"]),
        "stage": _i(s["stage"]),
        "spawns": by_stage.get(_i(s["stage_id"]), []),
        "bossMonsterCode": _i(s["boss_monster_code"]),
        "backgroundType": _i(s["background_type"]),
    } for s in stages]


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


# 파일명(테이블명) -> 추출 함수
EXPORTERS = [
    ("equip_slot_master", export_equip_slot),
    ("grade_master", export_grade),
    ("class_master", export_class),
    ("level_master", export_level),
    ("skill_master", export_skill),
    ("rune_master", export_rune),
    ("item_master", export_item),
    ("monster_master", export_monster),
    ("stage_master", export_stage),
    ("stage_reward", export_stage_reward),
    ("cube_master", export_cube),
    ("cube_recipe", export_cube_recipe),
    ("attendance_master", export_attendance),
]


def main():
    ap = argparse.ArgumentParser(description="마스터 데이터 DB -> Unity JSON 추출기")
    ap.add_argument("--schema", default=DEFAULT_SCHEMA, help="master-data-schema.sql 경로")
    ap.add_argument("--output", default=DEFAULT_OUTPUT, help="JSON 출력 디렉터리(Assets/Resources/MasterData)")
    ap.add_argument("--host", default="localhost")
    ap.add_argument("--port", type=int, default=3306)
    ap.add_argument("--user", default="root")
    ap.add_argument("--password", default="taskbar_hero_dev")
    ap.add_argument("--database", default=DEFAULT_DB)
    ap.add_argument("--no-apply", action="store_true", help="schema.sql 재적용을 생략하고 현재 DB에서만 추출")
    args = ap.parse_args()

    # schema.sql 재적용은 DB 미선택 상태로 연결(스크립트가 CREATE DATABASE/USE 를 수행)
    conn = pymysql.connect(
        host=args.host, port=args.port, user=args.user, password=args.password,
        charset="utf8mb4", autocommit=False,
    )
    try:
        if not args.no_apply:
            if not os.path.isfile(args.schema):
                sys.stderr.write(f"[ERROR] schema 파일을 찾을 수 없습니다: {args.schema}\n")
                return 3
            n = apply_schema(conn, args.schema)
            print(f"[apply] schema.sql 재적용 완료: {n}개 문 실행 ({args.schema})")
        else:
            with conn.cursor() as cur:
                cur.execute(f"USE `{args.database}`")
            print(f"[apply] 생략(--no-apply). 기존 DB `{args.database}` 사용")

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

        print(f"[done] {len(EXPORTERS)}개 파일 / 총 {total_rows}행 -> {args.output}")
        return 0
    finally:
        conn.close()


if __name__ == "__main__":
    sys.exit(main())
