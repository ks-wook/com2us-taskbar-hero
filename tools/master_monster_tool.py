#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
마스터 몬스터(monster_master) 생성·검증 도구

CharacterDevScene(몬스터 유닛 자동생성)에서 만든 몬스터를 **정본 세 곳에 한 번에** 반영한다.
씬은 클라이언트 번들(Assets/Resources/MasterData/monster_master.json)만 고치므로, 그대로 두면
다음번 `master_data_export.py`(DB -> 번들 단방향 재생성)에서 **씬이 고친 값이 덮어써진다**.
이 도구가 서버 정본을 먼저 맞춰 그 역전을 막는다.

정본:
  - 값:    docs/세부/master-data/master-data-값.md  §9 monster_master 표
  - SQL:   docs/세부/master-data/master-data-schema.sql  monster_master INSERT
  - 외형:  com2us-taskbar-hero-client/Assets/Dev/monster-appearance-recipe.json  (클라 개발 씬 전용)

산식·채번 규약은 클라이언트 `MonsterStatCurve`(CharacterDevRecipe.cs)와 **같은 값을 내도록** 옮겨 둔 것이다.
한쪽을 고치면 다른 쪽도 고쳐야 한다(추천값이 갈리면 씬 표시와 정본이 어긋난다).

사용:
  python tools/master_monster_tool.py verify
  python tools/master_monster_tool.py recommend --act 2 --stage 5
  python tools/master_monster_tool.py next-code --act 2 [--boss]
  python tools/master_monster_tool.py add --name "스켈레톤 궁수" --act 2 --stage 5 \
      --race undead --classes ranged,physical [--boss] [--hp 130 --attack 16] [--dry-run]
"""
import argparse
import json
import os
import re
import sys
import unicodedata

for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding="utf-8")
    except Exception:
        pass

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(SCRIPT_DIR)
CLIENT = os.path.join(REPO, "com2us-taskbar-hero-client")

SCHEMA_SQL = os.path.join(REPO, "docs", "세부", "master-data", "master-data-schema.sql")
VALUES_MD = os.path.join(REPO, "docs", "세부", "master-data", "master-data-값.md")
RECIPE_JSON = os.path.join(CLIENT, "Assets", "Dev", "monster-appearance-recipe.json")
PREFAB_DIR = os.path.join(CLIENT, "Assets", "Prefabs", "Character", "Monster")
CLIENT_BUNDLE = os.path.join(CLIENT, "Assets", "Resources", "MasterData", "monster_master.json")

ACT_COUNT = 5
STAGE_PER_ACT = 10
BOSS_STAGE = 10

# MonsterStatCurve 와 동일한 앵커(§4.4). 값 문서 §9 실측에서 온다.
HP_FLOOR = [42, 240, 2150, 4300, 8400]
ATK_FLOOR = [5, 15, 61, 148, 347]
ACT5_HP_GROWTH = 2.0
ACT5_ATK_GROWTH = 2.3
BOSS_HP = [1000, 7900, 20800, 40600, 80000]
BOSS_ATK = [7, 31, 77, 241, 522]


class ToolError(Exception):
    pass


# ── 산식·채번 (MonsterStatCurve 이식) ──

def interpolate(floors, act, stage, last_growth):
    """그 Act 하한에서 다음 Act 하한까지 9칸 기하 보간(s = 1..9)."""
    current = float(floors[act - 1])
    nxt = float(floors[act]) if act < ACT_COUNT else current * last_growth
    if current <= 0 or nxt <= 0:
        return current
    r = (nxt / current) ** (1.0 / 9.0)
    return current * (r ** (stage - 1))


def scale(value, multiplier):
    """난이도 배율을 곱해 정수로 반올림(0 이하가 되지 않게 1로 보정)."""
    rounded = int((value * multiplier) + 0.5)
    return max(1, rounded)


def recommend(act, stage, multiplier=1.0):
    """Act(1~5)·스테이지(1~10)의 추천 hp·attack. 스테이지 10은 보스 실측값을 그대로 쓴다."""
    act = min(max(act, 1), ACT_COUNT)
    stage = min(max(stage, 1), STAGE_PER_ACT)
    mult = multiplier if multiplier > 0 else 1.0
    if stage == BOSS_STAGE:
        return scale(BOSS_HP[act - 1], mult), scale(BOSS_ATK[act - 1], mult)
    return (scale(interpolate(HP_FLOOR, act, stage, ACT5_HP_GROWTH), mult),
            scale(interpolate(ATK_FLOOR, act, stage, ACT5_ATK_GROWTH), mult))


def act_of_code(code):
    """몬스터 코드에서 Act를 읽는다(90xx=1 … 94xx=5). 규약 밖 코드는 0."""
    act = (code - 9000) // 100 + 1
    return act if 1 <= act <= ACT_COUNT else 0


def is_boss_code(code):
    return act_of_code(code) > 0 and code % 100 == 99


def next_code(act, boss, used):
    """일반은 그 대역 01부터 비어 있는 번호를 순차로, 보스는 그 대역의 xx99."""
    act = min(max(act, 1), ACT_COUNT)
    band = 9000 + (act - 1) * 100
    if boss:
        code = band + 99
        if code in used:
            raise ToolError(f"Act{act} 보스 코드 {code}는 이미 사용 중입니다. 기존 보스를 고치려면 --code {code}로 갱신하세요.")
        return code
    for n in range(1, 99):
        candidate = band + n
        if candidate not in used:
            return candidate
    raise ToolError(f"Act{act} 대역({band + 1}~{band + 98})에 빈 코드가 없습니다.")


# ── 파싱 ──

def read(path):
    with open(path, "r", encoding="utf-8") as f:
        return f.read()


def write(path, text):
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)


SQL_ROW_RE = re.compile(r"^\s*\((\d+),\s*'((?:[^']|'')*)',\s*(\d+),\s*(\d+)\)\s*[,;]\s*$")


def parse_sql_monsters(text):
    """monster_master INSERT 를 파싱해 (행 목록, 값 첫 줄 index, 값 마지막 줄 index)."""
    lines = text.split("\n")
    start = None
    for i, line in enumerate(lines):
        if line.startswith("INSERT INTO monster_master "):
            start = i
            break
    if start is None:
        raise ToolError("master-data-schema.sql 에서 monster_master INSERT 를 찾지 못했습니다.")

    rows = []
    end = start
    for i in range(start + 1, len(lines)):
        stripped = lines[i].strip()
        if not stripped:
            break
        m = SQL_ROW_RE.match(lines[i])
        if not m:
            raise ToolError(f"monster_master INSERT {i + 1}행을 해석하지 못했습니다: {lines[i]}")
        rows.append({
            "code": int(m.group(1)),
            "name": m.group(2).replace("''", "'"),
            "hp": int(m.group(3)),
            "attack": int(m.group(4)),
        })
        end = i
        if stripped.endswith(";"):
            break
    if not rows:
        raise ToolError("monster_master INSERT 에 행이 없습니다.")
    return rows, start + 1, end


MD_ROW_RE = re.compile(r"^\|\s*(\d+)\s*\|\s*(.+?)\s*\|\s*(\d+)\s*\|\s*(\d+)\s*\|\s*$")


def parse_md_monsters(text):
    """값 문서 §9 표를 파싱해 (행 목록, 표 첫 데이터 줄 index, 마지막 줄 index)."""
    lines = text.split("\n")
    head = None
    for i, line in enumerate(lines):
        if line.startswith("## 9. monster_master"):
            head = i
            break
    if head is None:
        raise ToolError("master-data-값.md 에서 '## 9. monster_master' 절을 찾지 못했습니다.")

    first = None
    for i in range(head, len(lines)):
        if lines[i].startswith("| monster_code | name | hp | attack |"):
            first = i + 2  # 헤더 + 구분선
            break
        if lines[i].startswith("## 10."):
            break
    if first is None:
        raise ToolError("§9 의 monster_master 표 헤더를 찾지 못했습니다.")

    rows = []
    last = first - 1
    for i in range(first, len(lines)):
        m = MD_ROW_RE.match(lines[i])
        if not m:
            break
        rows.append({
            "code": int(m.group(1)),
            "name": m.group(2).strip(),
            "hp": int(m.group(3)),
            "attack": int(m.group(4)),
        })
        last = i
    if not rows:
        raise ToolError("§9 monster_master 표에 데이터 행이 없습니다.")
    return rows, first, last


SPAWN_ROW_RE = re.compile(r"^\s*\((\d+),\s*(\d+),\s*(\d+)\)\s*[,;]\s*$")


def stage_id(act, difficulty, stage):
    """stage_id = act*1000000 + difficulty*10000 + stage (값 문서 §11)."""
    return act * 1000000 + difficulty * 10000 + stage


def parse_sql_spawns(text):
    """stage_spawn INSERT 를 파싱해 (행 목록, 값 첫 줄 index, 값 마지막 줄 index)."""
    lines = text.split("\n")
    start = None
    for i, line in enumerate(lines):
        if line.startswith("INSERT INTO stage_spawn "):
            start = i
            break
    if start is None:
        raise ToolError("master-data-schema.sql 에서 stage_spawn INSERT 를 찾지 못했습니다.")

    rows = []
    end = start
    for i in range(start + 1, len(lines)):
        stripped = lines[i].strip()
        if not stripped:
            break
        m = SPAWN_ROW_RE.match(lines[i])
        if not m:
            raise ToolError(f"stage_spawn INSERT {i + 1}행을 해석하지 못했습니다: {lines[i]}")
        rows.append({"stage_id": int(m.group(1)), "code": int(m.group(2)), "count": int(m.group(3))})
        end = i
        if stripped.endswith(";"):
            break
    if not rows:
        raise ToolError("stage_spawn INSERT 에 행이 없습니다.")
    return rows, start + 1, end


def spawn_line(row, last):
    """스폰 값 줄 하나(기존 표기: 마리 수는 폭 2로 우측 정렬)."""
    return f"    ({row['stage_id']}, {row['code']}, {row['count']:>2})" + (";" if last else ",")


def apply_spawn_rows(block, new_rows):
    """스폰 값 줄 목록에 새 행들을 (stage_id, monster_code) 순으로 넣거나 같은 키를 교체한다."""
    out = list(block)
    for row in new_rows:
        key = (row["stage_id"], row["code"])
        replaced = False
        for i, line in enumerate(out):
            m = SPAWN_ROW_RE.match(line)
            if m and (int(m.group(1)), int(m.group(2))) == key:
                out[i] = spawn_line(row, last=line.rstrip().endswith(";"))
                replaced = True
                break
        if replaced:
            continue

        pos = 0
        for i, line in enumerate(out):
            m = SPAWN_ROW_RE.match(line)
            if m and (int(m.group(1)), int(m.group(2))) < key:
                pos = i + 1
        is_last = pos == len(out)
        if is_last and out:
            out[-1] = out[-1].rstrip().rstrip(";").rstrip(",") + ","
        out.insert(pos, spawn_line(row, last=is_last))
    return out


SPAWN_TOTAL_RE = re.compile(r"(그 결과가 )\*\*\d+행\*\*(이다)")
SPAWN_LIST_RE = re.compile(r"(일반 몬스터\()Act1 [^)]*(\)뿐이며)")


def spawn_monster_list_text(monster_rows, spawn_rows):
    """"Act1 `9001`·`9002`, Act2 …" 형태의 스폰 몬스터 목록 문구."""
    used = {r["code"] for r in spawn_rows}
    parts = []
    for act in range(1, ACT_COUNT + 1):
        codes = sorted(c for c in used if act_of_code(c) == act)
        if codes:
            parts.append(f"Act{act} " + "·".join(f"`{c}`" for c in codes))
    return ", ".join(parts)


def load_recipes():
    """외형 레시피 JSON 을 그대로 읽는다(없으면 빈 골격)."""
    if not os.path.exists(RECIPE_JSON):
        return {"version": 1, "recipes": []}
    return json.loads(read(RECIPE_JSON))


def load_bundle():
    """클라이언트 번들 monster_master.json (없으면 None)."""
    if not os.path.exists(CLIENT_BUNDLE):
        return None
    return json.loads(read(CLIENT_BUNDLE))


def existing_prefab_codes():
    if not os.path.isdir(PREFAB_DIR):
        return set()
    codes = set()
    for name in os.listdir(PREFAB_DIR):
        m = re.fullmatch(r"monster_(\d+)\.prefab", name)
        if m:
            codes.add(int(m.group(1)))
    return codes


# ── 출력 형식 ──

def display_width(s):
    """한글·전각 문자를 2칸으로 세는 표시 폭(SQL 정렬용)."""
    return sum(2 if unicodedata.east_asian_width(ch) in ("W", "F") else 1 for ch in s)


def sql_escape(name):
    return name.replace("'", "''")


def _mode(values, fallback):
    """최빈값(동률이면 큰 쪽). 기존 줄들의 정렬 열을 흉내 내는 데 쓴다."""
    if not values:
        return fallback
    best, best_n = fallback, 0
    for v in set(values):
        n = values.count(v)
        if n > best_n or (n == best_n and v > best):
            best, best_n = v, n
    return best


def sql_columns(lines):
    """기존 값 줄들에서 hp·attack 이 시작하는 표시 열을 읽어 정렬 기준으로 삼는다.

    **블록 전체를 다시 포맷하지 않는다** — 기존 12줄은 손으로 맞춘 것이라 어떤 규칙으로도
    똑같이 재현되지 않아, 재포맷하면 값이 그대로인 줄까지 diff 에 섞인다.
    """
    hp_cols, atk_cols = [], []
    for line in lines:
        m = re.match(r"^(\s*\(\d+,\s*'(?:[^']|'')*',\s*)(\d+)(,\s*)(\d+)\)", line)
        if m:
            hp_cols.append(display_width(m.group(1)))
            atk_cols.append(display_width(m.group(1) + m.group(2) + m.group(3)))
    return _mode(hp_cols, 30), _mode(atk_cols, 37)


def sql_line(row, hp_col, atk_col, last):
    """값 줄 하나를 기존 열 정렬에 맞춰 만든다(열을 넘치면 최소 1칸만 띄운다)."""
    head = f"    ({row['code']}, '{sql_escape(row['name'])}',"
    pad1 = " " * max(1, hp_col - display_width(head))
    mid = f"{head}{pad1}{row['hp']},"
    pad2 = " " * max(1, atk_col - display_width(mid))
    return f"{mid}{pad2}{row['attack']})" + (";" if last else ",")


def apply_sql_rows(block, new_row, hp_col, atk_col):
    """값 줄 목록에 새 행을 코드 순으로 끼워 넣거나, 같은 코드 줄을 교체한다."""
    out = list(block)
    code = new_row["code"]
    for i, line in enumerate(out):
        m = re.match(r"^\s*\((\d+),", line)
        if m and int(m.group(1)) == code:
            out[i] = sql_line(new_row, hp_col, atk_col, last=line.rstrip().endswith(";"))
            return out, out[i]
    pos = 0
    for i, line in enumerate(out):
        m = re.match(r"^\s*\((\d+),", line)
        if m and int(m.group(1)) < code:
            pos = i + 1
    is_last = pos == len(out)
    if is_last and out:
        out[-1] = out[-1].rstrip().rstrip(";").rstrip(",") + ","
    rendered = sql_line(new_row, hp_col, atk_col, last=is_last)
    out.insert(pos, rendered)
    return out, rendered


def md_line(row):
    name = row["name"].replace("|", "\\|")
    return f"| {row['code']} | {name} | {row['hp']} | {row['attack']} |"


def counts_of(rows):
    """총 종수·일반·보스·Act별 일반 수."""
    boss = [r for r in rows if is_boss_code(r["code"])]
    normal = [r for r in rows if not is_boss_code(r["code"])]
    per_act = []
    for act in range(1, ACT_COUNT + 1):
        per_act.append(sum(1 for r in normal if act_of_code(r["code"]) == act))
    return len(rows), len(normal), len(boss), per_act


SCALE_MD_RE = re.compile(
    r"(- \*\*규모/현황\*\*: )\*\*\d+종\*\*\(\d+ Act 구성: 일반 \d+ \+ 보스 \d+ — [^)]*\)")
SCALE_SQL_RE = re.compile(
    r"(--\s+)\d+종\(일반 \d+ \+ 보스 \d+\): 일반 [^\n]*\.")


def scale_sentence_md(rows):
    total, normal, boss, per_act = counts_of(rows)
    per = "·".join(f"Act{i + 1} 일반 {n}" for i, n in enumerate(per_act))
    return f"**{total}종**({ACT_COUNT} Act 구성: 일반 {normal} + 보스 {boss} — {per})"


def scale_sentence_sql(rows):
    total, normal, boss, _ = counts_of(rows)
    normals = "·".join(str(r["code"]) for r in sorted(rows, key=lambda r: r["code"])
                       if not is_boss_code(r["code"]))
    bosses = "·".join(str(r["code"]) for r in sorted(rows, key=lambda r: r["code"])
                      if is_boss_code(r["code"]))
    return f"{total}종(일반 {normal} + 보스 {boss}): 일반 {normals}, 보스 {bosses}."


# ── 레시피 직렬화 (기존 파일 형식을 그대로 유지한다) ──

RECIPE_KEY_ORDER = ["monsterCode", "note", "race", "gender", "theme",
                    "classes", "seed", "fixedParts", "colors", "shareCode"]


def dump_recipes(data):
    """recipes 를 코드 순으로 정렬해 기존 파일과 같은 모양(배열은 한 줄)으로 직렬화한다."""
    recipes = sorted(data.get("recipes", []), key=lambda r: r.get("monsterCode", 0))
    out = ["{", f'  "version": {data.get("version", 1)},', '  "recipes": [']
    for idx, recipe in enumerate(recipes):
        out.append("    {")
        keys = [k for k in RECIPE_KEY_ORDER if k in recipe]
        keys += [k for k in recipe if k not in RECIPE_KEY_ORDER]
        for j, key in enumerate(keys):
            value = recipe[key]
            if isinstance(value, list):
                body = ", ".join(json.dumps(v, ensure_ascii=False) for v in value)
                rendered = f"[{body}]"
            elif isinstance(value, dict):
                body = ", ".join(f"{json.dumps(k, ensure_ascii=False)}: {json.dumps(v, ensure_ascii=False)}"
                                 for k, v in value.items())
                rendered = "{ " + body + " }" if body else "{}"
            else:
                rendered = json.dumps(value, ensure_ascii=False)
            comma = "" if j == len(keys) - 1 else ","
            out.append(f'      {json.dumps(key, ensure_ascii=False)}: {rendered}{comma}')
        out.append("    }" + ("" if idx == len(recipes) - 1 else ","))
    out.append("  ]")
    out.append("}")
    return "\n".join(out) + "\n"


# ── 검증 ──

def verify(quiet=False):
    """정본 3곳 + 클라 번들·프리팹의 정합성을 본다. (오류 목록, 경고 목록)"""
    errors, warns = [], []

    sql_rows, _, _ = parse_sql_monsters(read(SCHEMA_SQL))
    md_rows, _, _ = parse_md_monsters(read(VALUES_MD))

    sql_map = {r["code"]: r for r in sql_rows}
    md_map = {r["code"]: r for r in md_rows}

    if len(sql_map) != len(sql_rows):
        errors.append("schema.sql monster_master 에 중복 코드가 있습니다.")
    if len(md_map) != len(md_rows):
        errors.append("값.md §9 표에 중복 코드가 있습니다.")

    for code in sorted(set(sql_map) | set(md_map)):
        if code not in md_map:
            errors.append(f"{code}: schema.sql 에만 있고 값.md §9 표에 없습니다.")
            continue
        if code not in sql_map:
            errors.append(f"{code}: 값.md §9 표에만 있고 schema.sql 에 없습니다.")
            continue
        s, m = sql_map[code], md_map[code]
        for field in ("name", "hp", "attack"):
            if s[field] != m[field]:
                errors.append(f"{code}: {field} 불일치 — schema.sql {s[field]!r} vs 값.md {m[field]!r}")
        if act_of_code(code) == 0:
            errors.append(f"{code}: 코드 규약(9000~9499) 밖입니다.")

    # 규모/현황 문장
    md_text = read(VALUES_MD)
    m = SCALE_MD_RE.search(md_text)
    if not m:
        warns.append("값.md §9 '규모/현황' 문장을 찾지 못해 개수 검사를 건너뜁니다.")
    elif m.group(0) != m.group(1) + scale_sentence_md(sql_rows):
        errors.append("값.md §9 '규모/현황' 문장이 실제 개수와 다릅니다 "
                      f"(정답: {scale_sentence_md(sql_rows)}).")
    sql_text = read(SCHEMA_SQL)
    m = SCALE_SQL_RE.search(sql_text)
    if not m:
        warns.append("schema.sql monster_master 머리 주석의 종수 목록을 찾지 못해 검사를 건너뜁니다.")
    elif m.group(0) != m.group(1) + scale_sentence_sql(sql_rows):
        errors.append("schema.sql monster_master 머리 주석의 종수 목록이 실제와 다릅니다 "
                      f"(정답: {scale_sentence_sql(sql_rows)}).")

    # 외형 레시피·프리팹 (없어도 정본은 성립하므로 경고)
    recipes = {r.get("monsterCode") for r in load_recipes().get("recipes", [])}
    prefabs = existing_prefab_codes()
    for code in sorted(sql_map):
        if code not in recipes:
            warns.append(f"{code}: 외형 레시피가 없습니다(시드 랜덤으로 생성됩니다).")
        if code not in prefabs:
            warns.append(f"{code}: monster_{code}.prefab 이 없습니다(CharacterDevScene 에서 생성 필요).")
    for code in sorted(recipes - set(sql_map)):
        warns.append(f"{code}: 레시피에만 있고 monster_master 에 없습니다.")

    # stage_spawn (실제 전투 등장 여부가 여기서 갈린다)
    spawn_rows, _, _ = parse_sql_spawns(sql_text)
    spawned = set()
    for r in spawn_rows:
        spawned.add(r["code"])
        if r["code"] not in sql_map:
            errors.append(f"stage_spawn {r['stage_id']}: 몬스터 {r['code']}가 monster_master 에 없습니다.")
        elif is_boss_code(r["code"]):
            errors.append(f"stage_spawn {r['stage_id']}: 보스 {r['code']}가 들어 있습니다 "
                          "(보스는 stage_master.boss_monster_code 로 배치한다).")
    for code in sorted(sql_map):
        if not is_boss_code(code) and code not in spawned:
            warns.append(f"{code}: 어느 스테이지에도 배치되지 않아 전투에 등장하지 않습니다 "
                         f"(`spawn --code {code} --stages \"...\"`).")

    m = SPAWN_TOTAL_RE.search(md_text)
    if not m:
        warns.append("값.md §11 스폰 총 행수 문장을 찾지 못해 검사를 건너뜁니다.")
    elif f"{m.group(1)}**{len(spawn_rows)}행**{m.group(2)}" != m.group(0):
        errors.append(f"값.md §11 스폰 총 행수가 실제({len(spawn_rows)}행)와 다릅니다.")

    m = SPAWN_LIST_RE.search(md_text)
    if m:
        expected = m.group(1) + spawn_monster_list_text(sql_rows, spawn_rows) + m.group(2)
        if m.group(0) != expected:
            errors.append("값.md §11 '스폰에 쓰이는 몬스터' 목록이 실제와 다릅니다.")

    # (B) 요약 표는 "Act1·2는 2종 / Act3~5는 1종"을 전제로 그려져 있다 — 종수가 달라지면 사람이 손봐야 한다.
    for act in range(1, ACT_COUNT + 1):
        kinds = sorted(c for c in spawned if act_of_code(c) == act)
        expected_kinds = 2 if act <= 2 else 1
        if len(kinds) != expected_kinds:
            warns.append(f"Act{act} 스폰 몬스터가 {len(kinds)}종({', '.join(str(c) for c in kinds)})이라 "
                         f"값.md §11 (B) 요약 표의 전제({expected_kinds}종)와 다릅니다 — 표에 열/절을 손으로 맞추세요.")

    # 클라 번들 (export 전이면 어긋나는 것이 정상 — 경고로만)
    bundle = load_bundle()
    if bundle is None:
        warns.append("클라이언트 번들 monster_master.json 이 없습니다.")
    else:
        bundle_map = {int(r["monsterCode"]): r for r in bundle}
        stale = []
        for code, s in sql_map.items():
            b = bundle_map.get(code)
            if b is None or b.get("name") != s["name"] or int(b.get("hp", 0)) != s["hp"] \
                    or int(b.get("attack", 0)) != s["attack"]:
                stale.append(code)
        for code in sorted(set(bundle_map) - set(sql_map)):
            stale.append(code)
        if stale:
            warns.append("클라이언트 번들이 정본과 다릅니다(코드 "
                         + ", ".join(str(c) for c in sorted(set(stale)))
                         + ") — `python tools/master_data_export.py` 로 재생성하세요.")

    if not quiet:
        print(f"[verify] monster_master {len(sql_rows)}종 — 오류 {len(errors)} · 경고 {len(warns)}")
        for e in errors:
            print(f"  [ERROR] {e}")
        for w in warns:
            print(f"  [WARN ] {w}")
    return errors, warns


# ── 정본 쓰기 ──

def apply_rows(new_rows):
    """새/바뀐 행들을 값.md·schema.sql 두 정본에 반영한 (새 sql 텍스트, 새 md 텍스트, 렌더된 sql 줄들)."""
    sql_text = read(SCHEMA_SQL)
    md_text = read(VALUES_MD)
    sql_rows, sql_first, sql_last = parse_sql_monsters(sql_text)
    _, md_first, md_last = parse_md_monsters(md_text)

    sql_lines = sql_text.split("\n")
    block = sql_lines[sql_first:sql_last + 1]
    hp_col, atk_col = sql_columns(block)
    rendered = []
    for row in new_rows:
        block, line = apply_sql_rows(block, row, hp_col, atk_col)
        rendered.append(line)
    sql_lines[sql_first:sql_last + 1] = block
    new_sql = "\n".join(sql_lines)

    merged = {r["code"]: r for r in sql_rows}
    for row in new_rows:
        merged[row["code"]] = row
    rows = sorted(merged.values(), key=lambda r: r["code"])

    new_sql = SCALE_SQL_RE.sub(lambda m: m.group(1) + scale_sentence_sql(rows), new_sql, count=1)

    md_lines = md_text.split("\n")
    md_lines[md_first:md_last + 1] = [md_line(r) for r in rows]
    new_md = "\n".join(md_lines)
    new_md = SCALE_MD_RE.sub(lambda m: m.group(1) + scale_sentence_md(rows), new_md, count=1)
    return new_sql, new_md, rendered


def apply_spawns(new_rows):
    """스폰 행들을 schema.sql stage_spawn + 값.md 요약 문장에 반영한 (새 sql, 새 md, 전체 스폰 행)."""
    sql_text = read(SCHEMA_SQL)
    md_text = read(VALUES_MD)
    spawn_rows, first, last = parse_sql_spawns(sql_text)
    monster_rows, _, _ = parse_sql_monsters(sql_text)

    lines = sql_text.split("\n")
    lines[first:last + 1] = apply_spawn_rows(lines[first:last + 1], new_rows)
    new_sql = "\n".join(lines)

    merged = {(r["stage_id"], r["code"]): r for r in spawn_rows}
    for r in new_rows:
        merged[(r["stage_id"], r["code"])] = r
    all_rows = sorted(merged.values(), key=lambda r: (r["stage_id"], r["code"]))

    new_md = SPAWN_TOTAL_RE.sub(
        lambda m: f"{m.group(1)}**{len(all_rows)}행**{m.group(2)}", md_text, count=1)
    new_md = SPAWN_LIST_RE.sub(
        lambda m: m.group(1) + spawn_monster_list_text(monster_rows, all_rows) + m.group(2),
        new_md, count=1)
    return new_sql, new_md, all_rows


def parse_stages(raw, default_count):
    """"5:6,6:7" -> [(stage, count, is_delta), ...].

    마리 수를 생략하면 <paramref>default_count</paramref>. **`5:+3`처럼 부호를 붙이면 증분**이며
    ("3마리 더"), 부호가 없으면 그 스테이지의 마리 수를 그 값으로 확정한다("3마리로").
    """
    result = []
    for item in raw.split(","):
        item = item.strip()
        if not item:
            continue
        if ":" in item:
            s, c = item.split(":", 1)
            c = c.strip()
            is_delta = c.startswith(("+", "-"))
            stage, count = int(s), int(c)
        else:
            stage, count, is_delta = int(item), default_count, False
        if not 1 <= stage <= STAGE_PER_ACT:
            raise ToolError(f"스테이지는 1~{STAGE_PER_ACT} 여야 합니다: {stage}")
        if not is_delta and count < 1:
            raise ToolError(f"마리 수는 1 이상이어야 합니다: {count}")
        if is_delta and count == 0:
            raise ToolError("증분이 0이면 바뀌는 것이 없습니다.")
        result.append((stage, count, is_delta))
    if not result:
        raise ToolError("배치할 스테이지가 없습니다.")
    return result


def spawn_count_of(spawn_rows, sid, code):
    """그 (stage_id, monster_code)의 현재 마리 수(없으면 0)."""
    for r in spawn_rows:
        if r["stage_id"] == sid and r["code"] == code:
            return r["count"]
    return 0


def stage_total(spawn_rows, sid, override=None):
    """그 스테이지의 일반 몬스터 총 마리 수. override = {code: count} 로 일부를 바꿔 계산한다."""
    counts = {r["code"]: r["count"] for r in spawn_rows if r["stage_id"] == sid}
    if override:
        counts.update(override)
    return sum(counts.values())


def build_spawn_rows(code, stages, difficulties, spawn_rows=None):
    """(스테이지, 마리 수, 증분여부) 목록을 난이도별 stage_spawn 행으로 펼친다.

    증분(`+N`)은 **난이도별 현재 값에 각각** 더한다(난이도 1·2가 다른 값일 수도 있으므로).
    """
    act = act_of_code(code)
    rows = []
    for stage, count, is_delta in stages:
        for d in difficulties:
            sid = stage_id(act, d, stage)
            if is_delta:
                current = spawn_count_of(spawn_rows or [], sid, code)
                final = current + count
                if final < 1:
                    raise ToolError(
                        f"스테이지 {stage}(난이도 {d})의 {code} 마리 수가 {current}{count:+d} = {final} 이 되어 "
                        "1 미만입니다. 배치를 없애려면 별도로 행을 지우세요.")
            else:
                final = count
            rows.append({"stage_id": sid, "code": code, "count": final})
    return rows


def print_spawn_plan(code, stages, difficulties, spawn_rows, new_rows):
    """스테이지별 "이전 → 이후 마리 수 (그 스테이지 총 N마리)" 를 출력한다.

    총 마리 수는 클리어 시간을 좌우하므로(값 문서 §11-B 기준 일반 스테이지 8~19마리),
    바뀐 뒤의 총량을 함께 보여 준다.
    """
    act = act_of_code(code)
    for stage, _, _ in stages:
        sid = stage_id(act, difficulties[0], stage)
        before = spawn_count_of(spawn_rows, sid, code)
        after = next(r["count"] for r in new_rows if r["stage_id"] == sid)
        total = stage_total(spawn_rows, sid, {code: after})
        before_total = stage_total(spawn_rows, sid)
        arrow = f"{before} → {after}마리" if before else f"{after}마리"
        print(f"  · 스테이지 {stage}: {arrow}  (그 스테이지 총 {before_total} → {total}마리)")
        if not 8 <= total <= 16:
            print(f"    ! 총 {total}마리는 값 문서 §11-B의 통상 범위(8~19)를 벗어납니다 — 의도한 값인지 확인하세요.")


def cmd_spawn(args):
    """몬스터를 스테이지에 배치한다(난이도 1·2 양쪽이 기본 — 현행 데이터가 동일 구성이다)."""
    monster_rows, _, _ = parse_sql_monsters(read(SCHEMA_SQL))
    codes = {r["code"] for r in monster_rows}
    if args.code not in codes:
        raise ToolError(f"{args.code}는 monster_master 에 없습니다. 먼저 `add` 로 추가하세요.")
    if is_boss_code(args.code):
        raise ToolError(f"{args.code}는 보스입니다 — 보스는 stage_spawn 이 아니라 "
                        "stage_master.boss_monster_code 로 배치합니다(값 문서 §11).")
    if act_of_code(args.code) == 0:
        raise ToolError(f"{args.code}는 코드 규약(9000~9499) 밖입니다.")

    difficulties = [1, 2] if args.difficulty == 0 else [args.difficulty]
    stages = parse_stages(args.stages, args.count)
    spawn_rows, _, _ = parse_sql_spawns(read(SCHEMA_SQL))
    new_rows = build_spawn_rows(args.code, stages, difficulties, spawn_rows)

    name = next(r["name"] for r in monster_rows if r["code"] == args.code)
    print(f"[스폰] {args.code} {name} — Act{act_of_code(args.code)} "
          f"난이도 {'1·2' if len(difficulties) == 2 else difficulties[0]}")
    print_spawn_plan(args.code, stages, difficulties, spawn_rows, new_rows)

    new_sql, new_md, all_rows = apply_spawns(new_rows)
    if args.dry_run:
        print("\n--- schema.sql (해당 줄) ---")
        for row in new_rows:
            print(spawn_line(row, last=False))
        print(f"\n스폰 총 행수: {len(all_rows)}")
        print("(--dry-run: 파일을 쓰지 않았습니다)")
        return 0

    write(SCHEMA_SQL, new_sql)
    write(VALUES_MD, new_md)
    print(f"  → {rel(SCHEMA_SQL)}\n  → {rel(VALUES_MD)}  (스폰 총 {len(all_rows)}행)")

    errors, _ = verify()
    if errors:
        print("\n[!] 반영 직후 검증에서 오류가 남았습니다. 위 메시지를 확인하세요.")
        return 1

    print("\n다음 단계: python tools/master_data_export.py  (DB 재적용 + 클라 번들 재생성)")
    return 0


def cmd_reskin(args):
    """능력치는 그대로 두고 **외형 레시피만** 새 컨셉으로 갈아 끼운다.

    `monster_master`(hp·attack·이름)와 `stage_spawn`(배치)은 건드리지 않으므로 서버 정본·DB·클라 번들은
    그대로다 — 바뀌는 것은 레시피 JSON과, 그것으로 다시 만드는 프리팹뿐이다(`export` 불필요).
    """
    monster_rows, _, _ = parse_sql_monsters(read(SCHEMA_SQL))
    row = next((r for r in monster_rows if r["code"] == args.code), None)
    if row is None:
        raise ToolError(f"{args.code}는 monster_master 에 없습니다. 먼저 `add` 로 추가하세요.")

    data = load_recipes()
    old = next((r for r in data.get("recipes", []) if r.get("monsterCode") == args.code), None)

    if args.reroll:
        if old is None:
            raise ToolError(f"{args.code}에 기존 레시피가 없어 다시 뽑을 것이 없습니다. 태그를 지정해 주세요.")
        entry = dict(old)
        base = entry.get("seed", args.code)
        entry["seed"] = base + args.reroll
        changed = f"시드 {base} → {entry['seed']} (태그는 그대로)"
    else:
        if not (args.race or args.classes or args.gender or args.theme
                or args.fixed_parts or args.colors):
            raise ToolError("바꿀 외형이 지정되지 않았습니다 — --race/--classes/--gender/--theme/"
                            "--fixed-parts/--colors 중 하나 이상, 또는 --reroll 을 쓰세요.")
        entry = {"monsterCode": args.code, "note": args.note or row["name"]}
        if args.race:
            entry["race"] = args.race
        if args.gender:
            entry["gender"] = args.gender
        entry["theme"] = args.theme or "fantasy"
        if args.classes:
            entry["classes"] = [c.strip() for c in args.classes.split(",") if c.strip()]
        entry["seed"] = args.seed if args.seed is not None else args.code
        fixed = parse_kv(args.fixed_parts, "--fixed-parts")
        if fixed:
            entry["fixedParts"] = fixed
        colors = parse_kv(args.colors, "--colors")
        if colors:
            entry["colors"] = colors
        changed = "외형 태그 교체"

    print(f"[외형 교체] {args.code} {row['name']} — {changed}")
    print(f"  · 능력치 유지: hp {row['hp']} / attack {row['attack']} (건드리지 않음)")
    if old is not None:
        print(f"  · 이전 레시피: {json.dumps(old, ensure_ascii=False)}")
    print(f"  · 새 레시피:   {json.dumps(entry, ensure_ascii=False)}")

    if args.dry_run:
        print("\n(--dry-run: 파일을 쓰지 않았습니다)")
        return 0

    data["recipes"] = [r for r in data.get("recipes", []) if r.get("monsterCode") != args.code] + [entry]
    write(RECIPE_JSON, dump_recipes(data))
    print(f"  → {rel(RECIPE_JSON)}")

    errors, _ = verify()
    if errors:
        print("\n[!] 반영 직후 검증에서 오류가 남았습니다. 위 메시지를 확인하세요.")
        return 1

    print("\n다음 단계 (프리팹을 다시 만들어야 실제 외형이 바뀝니다):")
    print(f"  · Unity 메뉴 TaskbarHero/몬스터/외형 교체 반영 — 대상 코드 EditorPrefs 키 "
          f"'TaskbarHero.Dev.MonsterRebuildCodes' = \"{args.code}\"")
    print("  · 능력치·배치는 그대로이므로 master_data_export.py 는 돌릴 필요가 없습니다.")
    return 0


def cmd_adopt_bundle(args):
    """CharacterDevScene(F12)이 클라 번들에만 반영한 값을 서버 정본으로 끌어올린다.

    씬에서 능력치를 먼저 고친 뒤 이걸 돌려야 다음 `master_data_export.py`가 그 값을 덮어쓰지 않는다.
    """
    bundle = load_bundle()
    if bundle is None:
        raise ToolError(f"클라이언트 번들을 찾지 못했습니다: {rel(CLIENT_BUNDLE)}")
    sql_rows, _, _ = parse_sql_monsters(read(SCHEMA_SQL))
    sql_map = {r["code"]: r for r in sql_rows}

    changed = []
    for entry in bundle:
        code = int(entry["monsterCode"])
        row = {"code": code, "name": str(entry.get("name", "")).strip(),
               "hp": int(entry.get("hp", 0)), "attack": int(entry.get("attack", 0))}
        if act_of_code(code) == 0:
            raise ToolError(f"번들의 코드 {code}는 규약(9000~9499) 밖입니다.")
        if row["hp"] < 1 or row["attack"] < 1:
            raise ToolError(f"{code}: 번들의 hp·attack 이 1 미만입니다.")
        old = sql_map.get(code)
        if old is None or (old["name"], old["hp"], old["attack"]) != (row["name"], row["hp"], row["attack"]):
            changed.append((old, row))

    dropped = sorted(set(sql_map) - {int(e["monsterCode"]) for e in bundle})
    if dropped:
        print("[주의] 정본에만 있고 번들에 없는 코드: "
              + ", ".join(str(c) for c in dropped) + " (삭제하지 않고 그대로 둡니다)")

    if not changed:
        print("[adopt-bundle] 번들과 정본이 이미 같습니다. 반영할 것이 없습니다.")
        return 0

    for old, row in changed:
        if old is None:
            print(f"  + {row['code']} {row['name']} — hp {row['hp']} / attack {row['attack']} (신규)")
        else:
            print(f"  ~ {row['code']} {row['name']} — "
                  f"hp {old['hp']}→{row['hp']} / attack {old['attack']}→{row['attack']}"
                  + ("" if old["name"] == row["name"] else f" / 이름 {old['name']}→{row['name']}"))

    new_sql, new_md, rendered = apply_rows([row for _, row in changed])
    if args.dry_run:
        print("\n--- schema.sql (해당 줄) ---")
        for line in rendered:
            print(line)
        print("\n(--dry-run: 파일을 쓰지 않았습니다)")
        return 0

    write(SCHEMA_SQL, new_sql)
    write(VALUES_MD, new_md)
    print(f"  → {rel(SCHEMA_SQL)}\n  → {rel(VALUES_MD)}")

    errors, _ = verify()
    if errors:
        print("\n[!] 반영 직후 검증에서 오류가 남았습니다. 위 메시지를 확인하세요.")
        return 1

    new_codes = [row["code"] for old, row in changed if old is None]
    print("\n다음 단계:")
    if new_codes:
        print("  · 외형 레시피가 없는 신규 코드는 `add --code {코드} ...` 로 레시피를 채우세요: "
              + ", ".join(str(c) for c in new_codes))
    print("  · python tools/master_data_export.py  (DB 재적용 + 클라 번들 재생성)")
    return 0


# ── add ──

def build_recipe_entry(args, code, name):
    entry = {"monsterCode": code, "note": args.note or name}
    if args.race:
        entry["race"] = args.race
    if args.gender:
        entry["gender"] = args.gender
    entry["theme"] = args.theme
    if args.classes:
        entry["classes"] = [c.strip() for c in args.classes.split(",") if c.strip()]
    entry["seed"] = args.seed if args.seed is not None else code
    fixed = parse_kv(args.fixed_parts, "--fixed-parts")
    if fixed:
        entry["fixedParts"] = fixed
    colors = parse_kv(args.colors, "--colors")
    if colors:
        entry["colors"] = colors
    return entry


def parse_kv(raw, flag):
    """"a=b,c=d" -> {"a": "b", "c": "d"}. 값에 콤마가 필요하면 세미콜론으로 항목을 나눈다."""
    if not raw:
        return {}
    result = {}
    items = raw.split(";") if ";" in raw else raw.split(",")
    for item in items:
        if "=" not in item:
            raise ToolError(f"{flag} 형식이 잘못되었습니다: {item!r} (키=값)")
        k, v = item.split("=", 1)
        result[k.strip()] = v.strip()
    return result


def cmd_add(args):
    sql_rows, _, _ = parse_sql_monsters(read(SCHEMA_SQL))
    used = {r["code"] for r in sql_rows}
    boss = args.boss or (args.stage == BOSS_STAGE)

    if args.code:
        code = args.code
        if act_of_code(code) == 0:
            raise ToolError(f"코드 {code}는 규약(9000~9499) 밖입니다.")
        boss = is_boss_code(code)
    else:
        code = next_code(args.act, boss, used)

    stage = BOSS_STAGE if boss else (args.stage or 1)
    rec_hp, rec_atk = recommend(args.act, stage, args.difficulty_multiplier)
    hp = args.hp if args.hp is not None else rec_hp
    attack = args.attack if args.attack is not None else rec_atk
    if hp < 1 or attack < 1:
        raise ToolError("hp·attack 은 1 이상의 정수여야 합니다.")

    name = args.name.strip()
    if not name:
        raise ToolError("--name 이 비었습니다.")
    dup = [r for r in sql_rows if r["name"] == name and r["code"] != code]
    if dup:
        raise ToolError(f"이름 {name!r} 은 이미 {dup[0]['code']} 가 쓰고 있습니다.")

    updating = code in used
    new_row = {"code": code, "name": name, "hp": hp, "attack": attack}

    # 1)·2) 정본 두 곳 — 기존 줄은 그대로 두고 새 줄만 코드 순으로 끼워 넣는다.
    new_sql, new_md, rendered = apply_rows([new_row])
    sql_rendered = rendered[0]

    # 3) 외형 레시피
    recipe_data = load_recipes()
    entry = build_recipe_entry(args, code, name)
    kept = [r for r in recipe_data.get("recipes", []) if r.get("monsterCode") != code]
    if updating and not args.replace_recipe:
        old = next((r for r in recipe_data.get("recipes", []) if r.get("monsterCode") == code), None)
        if old is not None:
            entry = old  # 갱신 시 외형은 건드리지 않는다(--replace-recipe 로 덮어쓰기)
    recipe_data["recipes"] = kept + [entry]
    new_recipe = dump_recipes(recipe_data)

    verb = "갱신" if updating else "추가"
    print(f"[{verb}] {code} {name} — hp {hp} / attack {attack} "
          f"(Act{args.act} {'보스' if boss else f'스테이지 {stage}'} 추천 {rec_hp}/{rec_atk})")
    if args.hp is not None and args.hp != rec_hp:
        print(f"  · hp 를 추천({rec_hp})에서 {args.hp} 로 지정했습니다.")
    if args.attack is not None and args.attack != rec_atk:
        print(f"  · attack 을 추천({rec_atk})에서 {args.attack} 로 지정했습니다.")

    if args.dry_run:
        print("\n--- schema.sql (해당 줄) ---")
        print(sql_rendered)
        print("--- 값.md §9 (해당 줄) ---")
        print(md_line(new_row))
        print("--- 레시피 항목 ---")
        print(json.dumps(entry, ensure_ascii=False, indent=2))
        if args.spawn and not boss:
            print("--- stage_spawn (배치될 줄) ---")
            difficulties = [1, 2] if args.spawn_difficulty == 0 else [args.spawn_difficulty]
            spawn_rows, _, _ = parse_sql_spawns(read(SCHEMA_SQL))
            stages = parse_stages(args.spawn, args.spawn_count)
            preview_rows = build_spawn_rows(code, stages, difficulties, spawn_rows)
            for row in preview_rows:
                print(spawn_line(row, last=False))
            print_spawn_plan(code, stages, difficulties, spawn_rows, preview_rows)
        print("\n(--dry-run: 파일을 쓰지 않았습니다)")
        return 0

    write(SCHEMA_SQL, new_sql)
    write(VALUES_MD, new_md)
    write(RECIPE_JSON, new_recipe)
    print(f"  → {rel(SCHEMA_SQL)}\n  → {rel(VALUES_MD)}\n  → {rel(RECIPE_JSON)}")

    # 스폰 배치 — "그 지역 그 스테이지에 등장"을 실제로 만드는 단계다(정본을 쓴 뒤에 얹는다).
    if args.spawn:
        if boss:
            print("  · 보스는 stage_spawn 이 아니라 stage_master.boss_monster_code 로 배치합니다 "
                  "(--spawn 을 무시했습니다).")
        else:
            stages = parse_stages(args.spawn, args.spawn_count)
            difficulties = [1, 2] if args.spawn_difficulty == 0 else [args.spawn_difficulty]
            spawn_rows, _, _ = parse_sql_spawns(read(SCHEMA_SQL))
            new_spawn_rows = build_spawn_rows(code, stages, difficulties, spawn_rows)
            spawn_sql, spawn_md, all_rows = apply_spawns(new_spawn_rows)
            write(SCHEMA_SQL, spawn_sql)
            write(VALUES_MD, spawn_md)
            print(f"  → 스폰 배치 (난이도 {'1·2' if len(difficulties) == 2 else difficulties[0]}, "
                  f"스폰 총 {len(all_rows)}행)")
            print_spawn_plan(code, stages, difficulties, spawn_rows, new_spawn_rows)

    errors, _ = verify()
    if errors:
        print("\n[!] 반영 직후 검증에서 오류가 남았습니다. 위 메시지를 확인하세요.")
        return 1

    print("\n다음 단계:")
    if not args.spawn and not boss:
        print(f"  0) 전투에 등장시키려면 배치가 필요합니다: "
              f"python tools/master_monster_tool.py spawn --code {code} --stages \"5:6\"")
    print("  1) Unity 메뉴 TaskbarHero/몬스터/신규 몬스터 전투 반영 (프리팹 생성 + 전투 배선)")
    print("  2) python tools/master_data_export.py  (DB 재적용 + 클라 번들 재생성)")
    return 0


def rel(path):
    return os.path.relpath(path, REPO).replace("\\", "/")


def cmd_verify(args):
    errors, _ = verify()
    return 1 if errors else 0


def cmd_recommend(args):
    hp, attack = recommend(args.act, args.stage, args.difficulty_multiplier)
    print(f"Act{args.act} 스테이지 {args.stage} 추천 — hp {hp} / attack {attack}")
    return 0


def cmd_next_code(args):
    sql_rows, _, _ = parse_sql_monsters(read(SCHEMA_SQL))
    print(next_code(args.act, args.boss, {r["code"] for r in sql_rows}))
    return 0


def cmd_list(args):
    sql_rows, _, _ = parse_sql_monsters(read(SCHEMA_SQL))
    recipes = {r.get("monsterCode") for r in load_recipes().get("recipes", [])}
    prefabs = existing_prefab_codes()
    print(f"{'code':>6}  {'name':<24} {'hp':>7} {'atk':>5}  레시피 프리팹")
    for r in sorted(sql_rows, key=lambda x: x["code"]):
        pad = " " * max(0, 24 - display_width(r["name"]))
        print(f"{r['code']:>6}  {r['name']}{pad} {r['hp']:>7} {r['attack']:>5}"
              f"   {'O' if r['code'] in recipes else '-'}      {'O' if r['code'] in prefabs else '-'}")
    return 0


def main():
    ap = argparse.ArgumentParser(description="마스터 몬스터(monster_master) 생성·검증 도구")
    sub = ap.add_subparsers(dest="cmd", required=True)

    v = sub.add_parser("verify", help="정본(값.md·schema.sql)·레시피·프리팹·클라 번들 정합성 검사")
    v.set_defaults(func=cmd_verify)

    li = sub.add_parser("list", help="현재 몬스터 목록과 레시피·프리팹 보유 현황")
    li.set_defaults(func=cmd_list)

    r = sub.add_parser("recommend", help="Act·스테이지의 추천 hp·attack 출력")
    r.add_argument("--act", type=int, required=True)
    r.add_argument("--stage", type=int, default=1)
    r.add_argument("--difficulty-multiplier", type=float, default=1.0)
    r.set_defaults(func=cmd_recommend)

    n = sub.add_parser("next-code", help="다음으로 쓸 monster_code 출력")
    n.add_argument("--act", type=int, required=True)
    n.add_argument("--boss", action="store_true")
    n.set_defaults(func=cmd_next_code)

    sp = sub.add_parser("spawn", help="몬스터를 스테이지에 배치한다(stage_spawn) — 전투 등장 여부를 정한다")
    sp.add_argument("--code", type=int, required=True, help="배치할 몬스터 코드(보스는 불가)")
    sp.add_argument("--stages", required=True,
                    help='"5:6" = 5스테이지를 6마리로 확정 / "5:+3" = 3마리 더 / "5:6,6:7" 처럼 여러 개. '
                         '마리 수를 빼면 --count 를 쓴다')
    sp.add_argument("--count", type=int, default=6, help="마리 수를 생략한 스테이지의 기본값(기본 6)")
    sp.add_argument("--difficulty", type=int, default=0,
                    help="0 = 난이도 1·2 모두(기본, 현행 데이터가 동일 구성) / 1 또는 2 = 그 난이도만")
    sp.add_argument("--dry-run", action="store_true", help="파일을 쓰지 않고 삽입될 줄만 출력")
    sp.set_defaults(func=cmd_spawn)

    rs = sub.add_parser("reskin",
                        help="능력치는 그대로 두고 외형만 새 컨셉으로 교체한다(레시피만 바뀐다)")
    rs.add_argument("--code", type=int, required=True, help="교체할 몬스터 코드")
    rs.add_argument("--race", help="새 Race 태그 (human·undead·devil·orc·elf·highelf 등)")
    rs.add_argument("--gender", help="새 Gender 태그 (male·female). 생략 시 무제한")
    rs.add_argument("--theme", help="새 Theme 태그 (지정하지 않으면 fantasy)")
    rs.add_argument("--classes", help="새 Class 태그 콤마 구분 (melee,damage 등)")
    rs.add_argument("--seed", type=int, help="파츠 선택 시드(생략 시 monsterCode)")
    rs.add_argument("--fixed-parts", help='고정 파츠 "Weapons=Sword_1;Back=Cape_2"')
    rs.add_argument("--colors", help='계열 색 "Body=#8FB7A8" (Body·Hair·Cloth 만)')
    rs.add_argument("--note", help="레시피 메모(생략 시 몬스터 이름)")
    rs.add_argument("--reroll", type=int, nargs="?", const=1, default=0,
                    help="태그는 그대로 두고 시드만 옮겨 외형을 다시 뽑는다(기본 +1, 숫자로 폭 지정)")
    rs.add_argument("--dry-run", action="store_true", help="파일을 쓰지 않고 바뀔 내용만 출력")
    rs.set_defaults(func=cmd_reskin)

    b = sub.add_parser("adopt-bundle",
                       help="CharacterDevScene(F12)이 클라 번들에만 반영한 값을 서버 정본으로 끌어올린다")
    b.add_argument("--dry-run", action="store_true", help="파일을 쓰지 않고 바뀔 줄만 출력")
    b.set_defaults(func=cmd_adopt_bundle)

    a = sub.add_parser("add", help="몬스터를 정본 2곳 + 외형 레시피에 추가/갱신")
    a.add_argument("--name", required=True, help="몬스터 이름")
    a.add_argument("--act", type=int, required=True, help="지역 1~5")
    a.add_argument("--stage", type=int, default=1, help="추천 산출 기준 스테이지 1~10 (10 = 보스)")
    a.add_argument("--boss", action="store_true", help="보스로 채번(xx99)")
    a.add_argument("--code", type=int, help="코드를 직접 지정(기존 몬스터 갱신용). 없으면 자동 채번")
    a.add_argument("--hp", type=int, help="추천값 대신 쓸 hp")
    a.add_argument("--attack", type=int, help="추천값 대신 쓸 attack")
    a.add_argument("--difficulty-multiplier", type=float, default=1.0)
    a.add_argument("--race", help="외형 Race 태그 (human·undead·devil·orc·elf·highelf 등)")
    a.add_argument("--gender", help="외형 Gender 태그 (male·female). 생략 시 무제한")
    a.add_argument("--theme", default="fantasy", help="외형 Theme 태그 (기본 fantasy)")
    a.add_argument("--classes", help="외형 Class 태그 콤마 구분 (melee,physical 등 AND 필터)")
    a.add_argument("--seed", type=int, help="파츠 선택 시드(생략 시 monsterCode)")
    a.add_argument("--fixed-parts", help='고정 파츠 "Weapons=Sword_1;Back=Cape_2"')
    a.add_argument("--colors", help='계열 색 "Body=#8FB7A8;Hair=#333333" (Body·Hair·Cloth 만)')
    a.add_argument("--note", help="레시피 메모(생략 시 이름)")
    a.add_argument("--spawn", help='추가 직후 배치할 스테이지 "5:6" / 증분은 "5:+3" / 여러 개는 "5:6,6:7" '
                                   '(생략하면 배치하지 않는다 — 전투에 등장하지 않는다)')
    a.add_argument("--spawn-count", type=int, default=6, help="--spawn 에서 마리 수를 생략한 스테이지의 기본값")
    a.add_argument("--spawn-difficulty", type=int, default=0, help="0 = 난이도 1·2 모두(기본)")
    a.add_argument("--replace-recipe", action="store_true",
                   help="기존 코드를 갱신할 때 외형 레시피도 새 값으로 덮어쓴다")
    a.add_argument("--dry-run", action="store_true", help="파일을 쓰지 않고 삽입될 줄만 출력")
    a.set_defaults(func=cmd_add)

    args = ap.parse_args()
    try:
        return args.func(args)
    except ToolError as e:
        sys.stderr.write(f"[ERROR] {e}\n")
        return 2


if __name__ == "__main__":
    sys.exit(main())
