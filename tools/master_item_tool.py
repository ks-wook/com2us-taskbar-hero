#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
마스터 아이템 생성·검증 도구 (item_master 전용)

아이템 하나를 추가하려면 지금까지 사람이 두 곳을 손으로 맞춰야 했다 —
  · 값 정본:  docs/세부/master-data/master-data-값.md  §6 (A) 공통 속성 표 + (B) 장비 스탯 표
  · SQL 정본: docs/세부/master-data/master-data-schema.sql  INSERT INTO item_master ...
이 도구가 그 둘을 한 번에 만들고(add), 서로 어긋나지 않았는지 검사한다(verify).

DB 적재와 클라이언트 JSON 번들 생성은 기존 `master_data_export.py`가 담당한다
(이 도구는 두 정본 파일만 다루고 DB에 접속하지 않는다).

사용:
  python tools/master_item_tool.py verify
  python tools/master_item_tool.py next-code --item-type 1 --slot 1 --class-req 4 --grade 5
  python tools/master_item_tool.py add --name "심연의 대검" --item-type 1 --grade 5 \
      --slot 1 --class-req 1 --atk 120 --crit-chance 0.05
  python tools/master_item_tool.py add --json new_items.json         # 배열/단일 객체 모두 가능
  python tools/master_item_tool.py add ... --dry-run                 # 파일을 고치지 않고 결과만 출력

의존성 없음(표준 라이브러리만).
"""
import argparse
import json
import os
import re
import sys

for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding="utf-8")
    except Exception:
        pass

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
VALUES_MD = os.path.join(REPO, "docs", "세부", "master-data", "master-data-값.md")
SCHEMA_SQL = os.path.join(REPO, "docs", "세부", "master-data", "master-data-schema.sql")
ICON_ROOT = os.path.join(REPO, "com2us-taskbar-hero-client", "Assets", "Art", "Icon")
ICON_DIR = os.path.join(ICON_ROOT, "Item")
# 아이콘 작업 지시서. `add`가 새로 만든 아이템을, `manifest`가 아이콘이 없는 아이템을 여기에 적는다.
MANIFEST = os.path.join(REPO, "tools", "item-icon-manifest.json")

# ── 코드 체계·파생값 (master-data-값.md §6 규약) ──

ITEM_TYPES = {1: "장비", 2: "재료", 3: "재화", 4: "소모품"}
GRADES = {1: "노말", 2: "고급", 3: "희귀", 4: "영웅", 5: "전설"}
EQUIP_SLOTS = {0: "", 1: "무기", 2: "보조무기", 3: "투구", 4: "갑옷", 5: "장갑", 6: "신발"}
CLASSES = {0: "공용", 1: "기사", 2: "레인저", 3: "마법사", 4: "슬레이어"}

# 등급 → 파생값(§6 "등급 기반 임시값"). 장비에만 적용한다.
LEVEL_REQ_BY_GRADE = {1: 0, 2: 10, 3: 20, 4: 30, 5: 40}
BASE_PRICE_BY_GRADE = {1: 500, 2: 5_000, 3: 50_000, 4: 150_000, 5: 400_000}

MATERIAL_BLOCK = 41_000   # 재료 41xxx
CONSUMABLE_BLOCK = 42_000  # 소모품 42xxx

# SQL 컬럼 순서(INSERT 문과 동일해야 한다)
COLUMNS = ["item_code", "name", "item_type", "grade", "equip_slot", "class_req", "level_req",
           "stack_max", "hp", "atk", "def", "move_speed", "crit_chance", "crit_damage",
           "cooldown", "sellable", "base_price"]
INT_COLS = {"item_code", "item_type", "grade", "equip_slot", "class_req", "level_req",
            "stack_max", "hp", "atk", "def", "sellable", "base_price"}
# 소수 컬럼별 기본 자릿수(기존 SQL 표기와 맞춘다. 값이 더 정밀하면 그만큼 늘린다)
DECIMALS = {"move_speed": 1, "crit_chance": 2, "crit_damage": 1, "cooldown": 1}
STAT_COLS = ["hp", "atk", "def", "move_speed", "crit_chance", "crit_damage", "cooldown"]

MD_A_HEADER = "| item_code | name | item_type | grade | equip_slot | class_req | level_req | stack_max | sellable | base_price |"
MD_B_HEADER = "| item_code | name | hp | atk | def | move_speed | crit_chance | crit_damage | cooldown |"


class ToolError(Exception):
    """사용자 입력·데이터 문제로 작업을 중단할 때 던진다."""


# ── 값 포맷 ──

def fmt_num(col, value):
    """SQL·표에 쓸 숫자 문자열. 정수 컬럼은 정수로, 소수 컬럼은 컬럼별 자릿수로 찍는다."""
    if col in INT_COLS:
        return str(int(value))
    places = DECIMALS.get(col, 1)
    text = f"{float(value):.6f}".rstrip("0")
    frac = len(text.split(".")[1]) if "." in text else 0
    return f"{float(value):.{max(places, frac)}f}"


def labeled(value, table, plain_zero=False):
    """값 문서 표의 `1 무기` 형태 라벨. plain_zero면 0은 라벨 없이 `0`으로 쓴다(equip_slot)."""
    value = int(value)
    if plain_zero and value == 0:
        return "0"
    name = table.get(value, "")
    return f"{value} {name}".strip()


# ── 파싱 ──

def split_sql_tuple(body):
    """SQL VALUES 튜플 내부 문자열을 쉼표로 쪼갠다(따옴표 안 쉼표는 무시)."""
    parts, buf, in_str = [], [], False
    i = 0
    while i < len(body):
        ch = body[i]
        if in_str:
            if ch == "'":
                if i + 1 < len(body) and body[i + 1] == "'":  # 이스케이프된 따옴표
                    buf.append("''")
                    i += 2
                    continue
                in_str = False
            buf.append(ch)
        else:
            if ch == "'":
                in_str = True
                buf.append(ch)
            elif ch == ",":
                parts.append("".join(buf).strip())
                buf = []
            else:
                buf.append(ch)
        i += 1
    parts.append("".join(buf).strip())
    return parts


def parse_sql_items(text):
    """schema.sql의 INSERT INTO item_master 블록을 파싱해 (행 목록, 시작줄, 끝줄)을 돌려준다."""
    lines = text.split("\n")
    start = None
    for i, line in enumerate(lines):
        if line.startswith("INSERT INTO item_master"):
            start = i
            break
    if start is None:
        raise ToolError("schema.sql에서 `INSERT INTO item_master`를 찾지 못했습니다.")

    rows, end = [], None
    for i in range(start + 1, len(lines)):
        stripped = lines[i].strip()
        if not stripped or stripped.startswith("--"):
            continue  # 그룹 구분 주석·빈 줄은 건너뛴다(값 블록 안에 섞여 있다)
        if not stripped.startswith("("):
            raise ToolError(f"schema.sql {i+1}행: VALUES 튜플이 아닌 줄을 만났습니다 — {stripped!r}")
        inner = stripped.rstrip(";").rstrip(",").strip()
        if not (inner.startswith("(") and inner.endswith(")")):
            raise ToolError(f"schema.sql {i+1}행: 괄호 짝이 맞지 않습니다 — {stripped!r}")
        values = split_sql_tuple(inner[1:-1])
        if len(values) != len(COLUMNS):
            raise ToolError(f"schema.sql {i+1}행: 컬럼 수 {len(values)}개(기대 {len(COLUMNS)}개)")
        row = {}
        for col, raw in zip(COLUMNS, values):
            if col == "name":
                row[col] = raw.strip("'").replace("''", "'")
            elif col in INT_COLS:
                row[col] = int(raw)
            else:
                row[col] = float(raw)
        row["_line"] = i + 1
        rows.append(row)
        if stripped.endswith(";"):
            end = i
            break
    if end is None:
        raise ToolError("schema.sql: item_master INSERT 블록이 `;`로 끝나지 않았습니다.")
    return rows, start, end


def parse_md_table(lines, header):
    """값 문서에서 지정 헤더의 표를 찾아 (셀 목록, 첫 데이터줄, 마지막 데이터줄)을 돌려준다."""
    try:
        h = next(i for i, line in enumerate(lines) if line.strip() == header)
    except StopIteration:
        raise ToolError(f"값 문서에서 표 헤더를 찾지 못했습니다:\n  {header}")
    first = h + 2  # 헤더 + 구분선
    rows, i = [], first
    while i < len(lines) and lines[i].startswith("| "):
        cells = [c.strip() for c in lines[i].strip().strip("|").split("|")]
        rows.append((cells, i))
        i += 1
    return rows, first, i - 1


def lead_int(cell, field, line_no):
    """`3 희귀`·`0` 같은 셀에서 앞의 정수를 뽑는다."""
    m = re.match(r"^(-?\d+)", cell.strip())
    if not m:
        raise ToolError(f"값 문서 {line_no}행: {field} 칸을 숫자로 읽을 수 없습니다 — {cell!r}")
    return int(m.group(1))


def parse_values_md(text):
    """값 문서의 (A)·(B) 표를 파싱해 코드→행 딕셔너리 둘을 돌려준다."""
    lines = text.split("\n")
    a_rows, a_first, a_last = parse_md_table(lines, MD_A_HEADER)
    b_rows, b_first, b_last = parse_md_table(lines, MD_B_HEADER)

    a = {}
    for cells, ln in a_rows:
        code = lead_int(cells[0], "item_code", ln + 1)
        a[code] = {
            "item_code": code, "name": cells[1],
            "item_type": lead_int(cells[2], "item_type", ln + 1),
            "grade": lead_int(cells[3], "grade", ln + 1),
            "equip_slot": lead_int(cells[4], "equip_slot", ln + 1),
            "class_req": lead_int(cells[5], "class_req", ln + 1),
            "level_req": lead_int(cells[6], "level_req", ln + 1),
            "stack_max": lead_int(cells[7], "stack_max", ln + 1),
            "sellable": lead_int(cells[8], "sellable", ln + 1),
            "base_price": lead_int(cells[9].replace(",", ""), "base_price", ln + 1),
            "_line": ln + 1,
        }
    b = {}
    for cells, ln in b_rows:
        code = lead_int(cells[0], "item_code", ln + 1)
        row = {"item_code": code, "name": cells[1], "_line": ln + 1}
        for col, cell in zip(STAT_COLS, cells[2:9]):
            row[col] = float(cell.replace(",", ""))
        b[code] = row
    return a, b, (a_first, a_last), (b_first, b_last)


# ── 검증 ──

def expected_equip_code(slot, class_req, grade, seq):
    """장비 코드 = 30000 + 슬롯×1000 + 클래스×100 + 등급×10 + 순번."""
    return 30000 + slot * 1000 + class_req * 100 + grade * 10 + seq


def verify(sql_rows, md_a, md_b, check_icons=True):
    """두 정본과 아이콘 파일을 교차 검증해 (오류, 경고) 문자열 목록을 돌려준다."""
    errors, warnings = [], []
    sql = {r["item_code"]: r for r in sql_rows}

    # 1) 코드 집합 일치
    only_sql = sorted(set(sql) - set(md_a))
    only_md = sorted(set(md_a) - set(sql))
    for code in only_sql:
        errors.append(f"[집합] {code}({sql[code]['name']}): SQL에만 있고 값 문서 (A) 표에 없음")
    for code in only_md:
        errors.append(f"[집합] {code}({md_a[code]['name']}): 값 문서 (A) 표에만 있고 SQL에 없음")

    # 2) 공통 속성 값 일치
    for code in sorted(set(sql) & set(md_a)):
        s, m = sql[code], md_a[code]
        for col in ["name", "item_type", "grade", "equip_slot", "class_req",
                    "level_req", "stack_max", "sellable", "base_price"]:
            if s[col] != m[col]:
                errors.append(f"[불일치] {code}({s['name']}) {col}: SQL={s[col]!r} / 값 문서={m[col]!r}")

    # 3) 장비 스탯 표 (B)
    equips = {c for c, r in sql.items() if r["item_type"] == 1}
    for code in sorted(equips - set(md_b)):
        errors.append(f"[스탯] {code}({sql[code]['name']}): 장비인데 값 문서 (B) 표에 없음")
    for code in sorted(set(md_b) - equips):
        errors.append(f"[스탯] {code}: (B) 표에 있으나 장비가 아님(또는 SQL에 없음)")
    for code in sorted(equips & set(md_b)):
        s, m = sql[code], md_b[code]
        if s["name"] != m["name"]:
            errors.append(f"[스탯] {code} name: SQL={s['name']!r} / (B) 표={m['name']!r}")
        for col in STAT_COLS:
            if abs(float(s[col]) - float(m[col])) > 1e-9:
                errors.append(f"[스탯] {code}({s['name']}) {col}: SQL={s[col]} / (B) 표={m[col]}")

    # 4) enum·규칙 검사(SQL 기준)
    for code in sorted(sql):
        r = sql[code]
        tag = f"{code}({r['name']})"
        if r["item_type"] not in ITEM_TYPES:
            errors.append(f"[값] {tag} item_type={r['item_type']} 정의되지 않음")
        if r["grade"] not in GRADES:
            errors.append(f"[값] {tag} grade={r['grade']} 는 1~5 범위 밖")
        if r["equip_slot"] not in EQUIP_SLOTS:
            errors.append(f"[값] {tag} equip_slot={r['equip_slot']} 정의되지 않음")
        if r["class_req"] not in CLASSES:
            errors.append(f"[값] {tag} class_req={r['class_req']} 정의되지 않음")
        if r["level_req"] % 5 != 0:
            errors.append(f"[값] {tag} level_req={r['level_req']} 는 5의 배수가 아님")
        if r["sellable"] == 0 and r["base_price"] != 0:
            errors.append(f"[값] {tag} sellable=0 인데 base_price={r['base_price']}(거래 불가면 0이어야 함)")
        if r["item_type"] == 1:
            if r["equip_slot"] == 0:
                errors.append(f"[값] {tag} 장비인데 equip_slot=0")
            if r["stack_max"] != 1:
                errors.append(f"[값] {tag} 장비인데 stack_max={r['stack_max']}(장비는 1)")
            expected = expected_equip_code(r["equip_slot"], r["class_req"], r["grade"],
                                           code - expected_equip_code(r["equip_slot"], r["class_req"], r["grade"], 0))
            if expected != code:
                errors.append(f"[코드] {tag} 코드 체계 위반(3·슬롯·클래스·등급·순번)")
            seq = code - expected_equip_code(r["equip_slot"], r["class_req"], r["grade"], 0)
            if not 1 <= seq <= 9:
                errors.append(f"[코드] {tag} 순번 {seq} 가 1~9 밖")
            if r["level_req"] != LEVEL_REQ_BY_GRADE[r["grade"]]:
                warnings.append(f"[파생] {tag} level_req={r['level_req']} "
                                f"(등급 {r['grade']} 기준값 {LEVEL_REQ_BY_GRADE[r['grade']]})")
            if r["sellable"] == 1 and r["base_price"] != BASE_PRICE_BY_GRADE[r["grade"]]:
                warnings.append(f"[파생] {tag} base_price={r['base_price']} "
                                f"(등급 {r['grade']} 기준값 {BASE_PRICE_BY_GRADE[r['grade']]})")
        else:
            if any(float(r[c]) != 0 for c in STAT_COLS):
                errors.append(f"[값] {tag} 비장비인데 스탯이 0이 아님")

    # 5) 이름 중복
    by_name = {}
    for code in sorted(sql):
        by_name.setdefault(sql[code]["name"], []).append(code)
    for name, codes in by_name.items():
        if len(codes) > 1:
            errors.append(f"[중복] 이름 {name!r} 이 여러 코드에 있음: {codes}")

    # 6) 아이콘 파일(클라이언트가 `item_{code}.png` 규칙으로 스캔한다)
    if check_icons:
        if not os.path.isdir(ICON_DIR):
            warnings.append(f"[아이콘] 아이콘 폴더가 없습니다: {ICON_DIR}")
        else:
            existing = {f for f in os.listdir(ICON_DIR) if f.endswith(".png")}
            for code in sorted(sql):
                if f"item_{code}.png" not in existing:
                    warnings.append(f"[아이콘] {code}({sql[code]['name']}): item_{code}.png 없음")
            known = {f"item_{c}.png" for c in sql}
            for f in sorted(existing - known):
                warnings.append(f"[아이콘] {f}: 대응하는 item_master 행이 없음")

    # 7) 문서의 규모 문장
    counts = {t: sum(1 for r in sql.values() if r["item_type"] == t) for t in ITEM_TYPES}
    return errors, warnings, counts


def scale_sentence(counts, total):
    """§6 규모/현황 문장을 현재 개수로 다시 만든다."""
    return (f"- **규모/현황**: **{total}종 확정**(재화 {counts[3]} + 장비 {counts[1]} + "
            f"재료 {counts[2]} + **소모품 {counts[4]}**).")


def check_scale_sentence(md_text, counts):
    """규모/현황 문장이 실제 개수와 맞는지 확인한다."""
    m = re.search(r"- \*\*규모/현황\*\*: \*\*(\d+)종 확정\*\*\(재화 (\d+) \+ 장비 (\d+) \+ "
                  r"재료 (\d+) \+ \*\*소모품 (\d+)\*\*\)", md_text)
    if not m:
        return ["[문서] §6 규모/현황 문장을 찾지 못했습니다(형식이 바뀌었는지 확인 필요)"]
    total, cur, eq, mat, con = (int(g) for g in m.groups())
    real_total = sum(counts.values())
    if (total, cur, eq, mat, con) != (real_total, counts[3], counts[1], counts[2], counts[4]):
        return [f"[문서] §6 규모/현황이 실제와 다름 — 문서: {total}종(재화 {cur}·장비 {eq}·재료 {mat}·소모품 {con}) "
                f"/ 실제: {real_total}종(재화 {counts[3]}·장비 {counts[1]}·재료 {counts[2]}·소모품 {counts[4]})"]
    return []


# ── 추가 ──

def next_code(sql, item_type, slot=0, class_req=0, grade=1):
    """다음으로 쓸 item_code를 계산한다. 장비는 코드 체계의 빈 순번, 재료·소모품은 블록 내 다음 번호."""
    if item_type == 1:
        base = expected_equip_code(slot, class_req, grade, 0)
        for seq in range(1, 10):
            if base + seq not in sql:
                return base + seq
        raise ToolError(f"슬롯 {slot}·클래스 {class_req}·등급 {grade} 순번이 9개를 모두 썼습니다.")
    if item_type == 2:
        block = MATERIAL_BLOCK
    elif item_type == 4:
        block = CONSUMABLE_BLOCK
    else:
        raise ToolError("재화(item_type=3)는 골드 1종 고정이라 이 도구로 추가하지 않습니다.")
    used = [c for c in sql if block < c < block + 1000]
    return max(used) + 1 if used else block + 1


def build_row(spec, sql):
    """입력 명세를 검증·보정해 완전한 아이템 행으로 만든다."""
    name = str(spec.get("name", "")).strip()
    if not name:
        raise ToolError("name 은 필수입니다.")
    item_type = int(spec.get("item_type", 0))
    if item_type not in (1, 2, 4):
        raise ToolError("item_type 은 1(장비)·2(재료)·4(소모품) 중 하나여야 합니다.")
    grade = int(spec.get("grade", 0))
    if grade not in GRADES:
        raise ToolError("grade 는 1~5 여야 합니다.")

    slot = int(spec.get("equip_slot", 0))
    class_req = int(spec.get("class_req", 0))
    if item_type == 1:
        if slot not in EQUIP_SLOTS or slot == 0:
            raise ToolError("장비는 equip_slot 이 1~6 이어야 합니다.")
        if class_req not in CLASSES:
            raise ToolError("class_req 는 0~4 여야 합니다.")
    else:
        slot, class_req = 0, 0

    code = int(spec["item_code"]) if spec.get("item_code") else next_code(sql, item_type, slot, class_req, grade)
    if code in sql:
        raise ToolError(f"item_code {code} 는 이미 사용 중입니다({sql[code]['name']}).")
    if any(r["name"] == name for r in sql.values()):
        raise ToolError(f"이름 {name!r} 은 이미 사용 중입니다.")

    if item_type == 1:
        level_req = int(spec.get("level_req", LEVEL_REQ_BY_GRADE[grade]))
        sellable = int(spec.get("sellable", 1))
        base_price = int(spec.get("base_price", BASE_PRICE_BY_GRADE[grade] if sellable else 0))
        stack_max = int(spec.get("stack_max", 1))
    else:
        level_req = int(spec.get("level_req", 0))
        sellable = int(spec.get("sellable", 0))
        base_price = int(spec.get("base_price", 0))
        stack_max = int(spec.get("stack_max", 99))

    row = {"item_code": code, "name": name, "item_type": item_type, "grade": grade,
           "equip_slot": slot, "class_req": class_req, "level_req": level_req,
           "stack_max": stack_max, "sellable": sellable, "base_price": base_price}
    for col in STAT_COLS:
        value = float(spec.get(col, 0))
        if item_type != 1 and value != 0:
            raise ToolError(f"비장비({ITEM_TYPES[item_type]})에는 스탯을 넣을 수 없습니다: {col}={value}")
        row[col] = value
    return row


def sql_line(row, last):
    """schema.sql VALUES 한 줄을 만든다."""
    parts = []
    for col in COLUMNS:
        if col == "name":
            parts.append("'" + row[col].replace("'", "''") + "'")
        else:
            parts.append(fmt_num(col, row[col]))
    return "    (" + ", ".join(parts) + (");" if last else "),")


def md_a_line(row):
    """값 문서 (A) 표 한 줄."""
    return ("| {code} | {name} | {itype} | {grade} | {slot} | {cls} | {lv} | {stack} | {sell} | {price} |"
            .format(code=row["item_code"], name=row["name"],
                    itype=labeled(row["item_type"], ITEM_TYPES),
                    grade=labeled(row["grade"], GRADES),
                    slot=labeled(row["equip_slot"], EQUIP_SLOTS, plain_zero=True),
                    cls=labeled(row["class_req"], CLASSES),
                    lv=row["level_req"], stack=row["stack_max"],
                    sell=row["sellable"], price=row["base_price"]))


def md_b_line(row):
    """값 문서 (B) 장비 스탯 표 한 줄."""
    stats = " | ".join(fmt_stat_md(row[c]) for c in STAT_COLS)
    return f"| {row['item_code']} | {row['name']} | {stats} |"


def fmt_stat_md(value):
    """(B) 표는 0과 정수를 그대로, 소수는 필요한 자릿수만 쓴다(예: 0, 40, 0.05, -0.1)."""
    value = float(value)
    if value == int(value):
        return str(int(value))
    return ("%.6f" % value).rstrip("0").rstrip(".")


def insert_sorted(lines, first, last, new_line, code):
    """코드 오름차순을 유지하며 표/튜플 목록에 한 줄을 끼워 넣고, 삽입 위치를 돌려준다.

    **자기보다 작은 마지막 코드 바로 뒤**에 넣는다 — SQL 값 블록에는 그룹 구분 주석이 섞여 있어,
    "자기보다 큰 첫 코드 앞"에 넣으면 다음 그룹의 주석 위로 올라가 버린다.
    """
    pos = first
    for i in range(first, last + 1):
        m = re.match(r"^\s*[|(]\s*(\d+)", lines[i])
        if m and int(m.group(1)) < code:
            pos = i + 1
    lines.insert(pos, new_line)
    return pos


def apply_add(rows, dry_run=False):
    """새 행들을 두 정본 파일에 반영한다. rows는 build_row 결과 목록."""
    sql_text = read(SCHEMA_SQL)
    md_text = read(VALUES_MD)
    sql_lines = sql_text.split("\n")
    md_lines = md_text.split("\n")

    # SQL: 마지막 튜플의 `;`를 `,`로 바꾸고 새 줄을 코드 순서에 맞춰 삽입한다.
    _, sql_start, sql_end = parse_sql_items(sql_text)
    for row in rows:
        sql_lines[sql_end] = sql_lines[sql_end].rstrip()
        if sql_lines[sql_end].endswith(";"):
            sql_lines[sql_end] = sql_lines[sql_end][:-1] + ","
        pos = insert_sorted(sql_lines, sql_start + 1, sql_end, sql_line(row, last=False), row["item_code"])
        sql_end += 1
        # 항상 마지막 줄이 `;`로 끝나게 정리
        sql_lines[sql_end] = sql_lines[sql_end].rstrip().rstrip(",") + ";"
        if pos == sql_end:  # 새 줄이 마지막이 된 경우 이전 줄의 `;`를 `,`로
            sql_lines[sql_end - 1] = sql_lines[sql_end - 1].rstrip().rstrip(";").rstrip(",") + ","

    # 값 문서 (A)·(B)
    _, _, (a_first, a_last), (b_first, b_last) = parse_values_md("\n".join(md_lines))
    for row in rows:
        _, _, (a_first, a_last), (b_first, b_last) = parse_values_md("\n".join(md_lines))
        insert_sorted(md_lines, a_first, a_last, md_a_line(row), row["item_code"])
        if row["item_type"] == 1:
            _, _, (a_first, a_last), (b_first, b_last) = parse_values_md("\n".join(md_lines))
            insert_sorted(md_lines, b_first, b_last, md_b_line(row), row["item_code"])

    # 규모/현황 문장 갱신
    new_sql_text = "\n".join(sql_lines)
    new_rows, _, _ = parse_sql_items(new_sql_text)
    counts = {t: sum(1 for r in new_rows if r["item_type"] == t) for t in ITEM_TYPES}
    new_md_text = re.sub(
        r"- \*\*규모/현황\*\*: \*\*\d+종 확정\*\*\(재화 \d+ \+ 장비 \d+ \+ 재료 \d+ \+ \*\*소모품 \d+\*\*\)\.",
        scale_sentence(counts, sum(counts.values())), "\n".join(md_lines), count=1)

    if dry_run:
        return new_sql_text, new_md_text, counts
    write(SCHEMA_SQL, new_sql_text)
    write(VALUES_MD, new_md_text)
    return new_sql_text, new_md_text, counts


# ── 입출력 ──

def rel(path):
    """리포 루트 기준 슬래시 경로(매니페스트·안내 출력용)."""
    return os.path.relpath(path, REPO).replace("\\", "/")


def manifest_entry(row):
    """아이콘 작업 지시서 한 항목. `iconSourceName`은 pixel-item-icons 스킬이 저장할 한글 파일명이다."""
    icon_path = os.path.join(ICON_DIR, f"item_{row['item_code']}.png")
    return {
        "itemCode": row["item_code"],
        "name": row["name"],
        "itemType": row["item_type"],
        "itemTypeName": ITEM_TYPES[row["item_type"]],
        "grade": row["grade"],
        "gradeName": GRADES[row["grade"]],
        "equipSlot": row["equip_slot"],
        "equipSlotName": EQUIP_SLOTS.get(row["equip_slot"], ""),
        "classReq": row["class_req"],
        "classReqName": CLASSES.get(row["class_req"], ""),
        "iconSourceName": f"{row['name']}.png",
        "iconFile": f"item_{row['item_code']}.png",
        "iconPath": rel(icon_path),
        "iconExists": os.path.isfile(icon_path),
    }


def write_manifest(rows, path, note):
    """작업 지시서를 JSON으로 쓴다(항상 덮어쓴다 — 직전 작업분만 담는 임시 지시서다)."""
    data = {
        "_note": note,
        "_generatedBy": "tools/master_item_tool.py",
        "_iconDir": rel(ICON_DIR),
        "_iconSourceDirs": [rel(ICON_ROOT), rel(ICON_DIR)],
        "items": [manifest_entry(r) for r in rows],
    }
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
        f.write("\n")
    return data


def find_source_icon(entry):
    """스킬이 한글 이름으로 저장해 둔 PNG를 찾는다(Icon/ 루트와 Icon/Item/ 둘 다 본다)."""
    for base in (ICON_DIR, ICON_ROOT):
        candidate = os.path.join(base, entry["iconSourceName"])
        if os.path.isfile(candidate):
            return candidate
    return None


def read(path):
    with open(path, "r", encoding="utf-8") as f:
        return f.read()


def write(path, text):
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)


def cmd_verify(args):
    sql_text, md_text = read(SCHEMA_SQL), read(VALUES_MD)
    sql_rows, _, _ = parse_sql_items(sql_text)
    md_a, md_b, _, _ = parse_values_md(md_text)
    errors, warnings, counts = verify(sql_rows, md_a, md_b, check_icons=not args.no_icons)
    errors += check_scale_sentence(md_text, counts)

    total = sum(counts.values())
    print(f"item_master {total}종 — 장비 {counts[1]} · 재료 {counts[2]} · 재화 {counts[3]} · 소모품 {counts[4]}")
    for w in warnings:
        print(f"  경고 {w}")
    for e in errors:
        print(f"  오류 {e}")
    print(f"\n오류 {len(errors)}건 · 경고 {len(warnings)}건")
    return 1 if errors else 0


def cmd_next_code(args):
    sql_rows, _, _ = parse_sql_items(read(SCHEMA_SQL))
    sql = {r["item_code"]: r for r in sql_rows}
    code = next_code(sql, args.item_type, args.slot, args.class_req, args.grade)
    print(code)
    return 0


def cmd_add(args):
    sql_text = read(SCHEMA_SQL)
    sql_rows, _, _ = parse_sql_items(sql_text)
    sql = {r["item_code"]: r for r in sql_rows}

    if args.json:
        data = json.loads(read(args.json))
        specs = data if isinstance(data, list) else [data]
    else:
        specs = [{
            "name": args.name, "item_type": args.item_type, "grade": args.grade,
            "equip_slot": args.slot, "class_req": args.class_req,
            "item_code": args.item_code, "level_req": args.level_req,
            "stack_max": args.stack_max, "sellable": args.sellable, "base_price": args.base_price,
            "hp": args.hp, "atk": args.atk, "def": getattr(args, "def"),
            "move_speed": args.move_speed, "crit_chance": args.crit_chance,
            "crit_damage": args.crit_damage, "cooldown": args.cooldown,
        }]
        specs = [{k: v for k, v in specs[0].items() if v is not None}]

    built = []
    for spec in specs:
        row = build_row(spec, sql)
        sql[row["item_code"]] = row  # 같은 배치 안에서의 코드·이름 중복도 막는다
        built.append(row)

    for row in built:
        print(f"+ {row['item_code']}  {row['name']}  "
              f"({ITEM_TYPES[row['item_type']]}·{GRADES[row['grade']]}"
              + (f"·{EQUIP_SLOTS[row['equip_slot']]}·{CLASSES[row['class_req']]}" if row["item_type"] == 1 else "")
              + f")  level_req={row['level_req']} base_price={row['base_price']}")

    new_sql, new_md, counts = apply_add(built, dry_run=args.dry_run)
    if args.dry_run:
        print("\n[dry-run] 파일을 고치지 않았습니다. 삽입될 줄:")
        for row in built:
            print("  SQL  " + sql_line(row, last=False))
            print("  (A)  " + md_a_line(row))
            if row["item_type"] == 1:
                print("  (B)  " + md_b_line(row))
        return 0

    # 반영 후 곧바로 재검증
    sql_rows2, _, _ = parse_sql_items(new_sql)
    md_a2, md_b2, _, _ = parse_values_md(new_md)
    errors, warnings, counts2 = verify(sql_rows2, md_a2, md_b2, check_icons=False)
    errors += check_scale_sentence(new_md, counts2)
    if errors:
        print("\n반영 후 검증 실패 — 아래 오류를 확인하세요(파일은 이미 수정됨).")
        for e in errors:
            print(f"  오류 {e}")
        return 1

    manifest_path = args.manifest or MANIFEST
    write_manifest(built, manifest_path,
                   "이번에 추가한 아이템의 아이콘 작업 지시서. pixel-item-icons 스킬로 "
                   "iconSourceName(한글) 파일을 만든 뒤 `link-icons`로 iconFile 이름으로 옮긴다.")

    print(f"\n반영 완료 — item_master {sum(counts.values())}종")
    print(f"아이콘 작업 지시서: {rel(manifest_path)}")
    print("다음 단계:")
    print("  1) 아이콘 생성(pixel-item-icons 스킬) — 지시서의 iconSourceName 이름 그대로 저장:")
    for row in built:
        print(f"       {rel(ICON_DIR)}/{row['name']}.png   → item_{row['item_code']}.png 로 연결됨")
    print("  2) 이름 연결: python tools/master_item_tool.py link-icons")
    print("  3) DB 적재 + 클라 JSON 번들: python tools/master_data_export.py")
    print("  4) Unity 에디터: TaskbarHero/UI/아이템 아이콘 DB 빌드")
    return 0


def cmd_manifest(args):
    """아이콘이 없는 아이템(또는 전체)의 작업 지시서를 만든다."""
    sql_rows, _, _ = parse_sql_items(read(SCHEMA_SQL))
    rows = sorted(sql_rows, key=lambda r: r["item_code"])
    if not args.all:
        rows = [r for r in rows
                if not os.path.isfile(os.path.join(ICON_DIR, f"item_{r['item_code']}.png"))]
    path = args.manifest or MANIFEST
    note = ("item_master 전체 아이콘 목록." if args.all
            else "아이콘 파일이 아직 없는 아이템 목록.")
    write_manifest(rows, path, note)
    print(f"{len(rows)}종 → {rel(path)}")
    for r in rows[:20]:
        print(f"  {r['item_code']}  {r['name']}")
    if len(rows) > 20:
        print(f"  … 외 {len(rows) - 20}종")
    return 0


def cmd_link_icons(args):
    """스킬이 한글 이름으로 만든 PNG를 `item_{code}.png`로 옮긴다(.meta가 있으면 함께 옮겨 GUID를 보존)."""
    path = args.manifest or MANIFEST
    if not os.path.isfile(path):
        raise ToolError(f"작업 지시서가 없습니다: {rel(path)} — 먼저 `add` 또는 `manifest`를 실행하세요.")
    data = json.loads(read(path))
    entries = data.get("items", [])
    if not entries:
        print("지시서가 비어 있습니다.")
        return 0

    os.makedirs(ICON_DIR, exist_ok=True)
    moved, skipped, missing = [], [], []
    for entry in entries:
        target = os.path.join(ICON_DIR, entry["iconFile"])
        if os.path.isfile(target):
            skipped.append(entry)
            continue
        source = find_source_icon(entry)
        if source is None:
            missing.append(entry)
            continue
        if not args.dry_run:
            os.replace(source, target)
            # .meta가 있으면 같이 옮긴다 — GUID가 유지돼 기존 참조가 끊기지 않는다.
            if os.path.isfile(source + ".meta"):
                os.replace(source + ".meta", target + ".meta")
        moved.append((entry, source, target))

    for entry, source, target in moved:
        print(f"{'[dry-run] ' if args.dry_run else ''}옮김  {rel(source)}  →  {rel(target)}")
    for entry in skipped:
        print(f"건너뜀  {entry['iconFile']} (이미 있음)")
    for entry in missing:
        print(f"없음    {entry['iconSourceName']} — {rel(ICON_DIR)} 또는 {rel(ICON_ROOT)} 에서 찾지 못함")

    print(f"\n옮김 {len(moved)} · 건너뜀 {len(skipped)} · 미생성 {len(missing)}")
    if missing:
        print("미생성 항목은 pixel-item-icons 스킬로 iconSourceName 이름 그대로 먼저 만드세요.")
        return 1
    return 0


def main():
    ap = argparse.ArgumentParser(description="마스터 아이템(item_master) 생성·검증 도구")
    sub = ap.add_subparsers(dest="cmd", required=True)

    v = sub.add_parser("verify", help="값 문서·SQL·아이콘 정합성 검사")
    v.add_argument("--no-icons", action="store_true", help="아이콘 파일 존재 검사를 건너뛴다")
    v.set_defaults(func=cmd_verify)

    n = sub.add_parser("next-code", help="다음으로 쓸 item_code 출력")
    n.add_argument("--item-type", type=int, required=True, help="1 장비 2 재료 4 소모품")
    n.add_argument("--slot", type=int, default=0, help="장비 슬롯 1~6")
    n.add_argument("--class-req", type=int, default=0, help="0 공용 1 기사 2 레인저 3 마법사 4 슬레이어")
    n.add_argument("--grade", type=int, default=1, help="등급 1~5")
    n.set_defaults(func=cmd_next_code)

    a = sub.add_parser("add", help="아이템을 두 정본 파일에 추가")
    a.add_argument("--json", help="아이템 명세 JSON 파일(객체 또는 배열)")
    a.add_argument("--name")
    a.add_argument("--item-type", type=int)
    a.add_argument("--grade", type=int)
    a.add_argument("--slot", type=int, dest="slot")
    a.add_argument("--class-req", type=int)
    a.add_argument("--item-code", type=int, help="직접 지정(생략 시 자동 채번)")
    a.add_argument("--level-req", type=int)
    a.add_argument("--stack-max", type=int)
    a.add_argument("--sellable", type=int)
    a.add_argument("--base-price", type=int)
    for stat in STAT_COLS:
        a.add_argument(f"--{stat.replace('_', '-')}", type=float, dest=stat)
    a.add_argument("--manifest", help=f"아이콘 작업 지시서 출력 경로(기본 {rel(MANIFEST)})")
    a.add_argument("--dry-run", action="store_true")
    a.set_defaults(func=cmd_add)

    m = sub.add_parser("manifest", help="아이콘 작업 지시서 생성(기본: 아이콘 없는 아이템만)")
    m.add_argument("--all", action="store_true", help="아이콘 유무와 무관하게 전체")
    m.add_argument("--manifest", help=f"출력 경로(기본 {rel(MANIFEST)})")
    m.set_defaults(func=cmd_manifest)

    l = sub.add_parser("link-icons", help="한글 이름 PNG를 item_{code}.png 로 옮긴다")
    l.add_argument("--manifest", help=f"작업 지시서 경로(기본 {rel(MANIFEST)})")
    l.add_argument("--dry-run", action="store_true")
    l.set_defaults(func=cmd_link_icons)

    args = ap.parse_args()
    try:
        return args.func(args)
    except ToolError as e:
        sys.stderr.write(f"오류: {e}\n")
        return 2


if __name__ == "__main__":
    sys.exit(main())
