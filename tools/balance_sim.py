#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
밸런스 시뮬레이터 — 스테이지 클리어 여부 · 클리어 소요 시간 산출기 (오프라인)

무엇을 하는가
  마스터 데이터(정본 SQL)와 클라이언트 전투 규칙을 그대로 써서 **한 스테이지 전투를 고정 스텝으로
  압축 실행**하고, 클리어 성공/실패와 소요 시간을 초 단위로 산출한다. DB·서버·Unity가 필요 없다.
  전투 안의 이동(파티 전진 · 몬스터 접근 · 대형 행 배치)과 스폰 주기까지 모사하므로,
  "몹을 몇 초에 녹이는가"가 아니라 **"스테이지를 몇 초에 미는가"** 가 나온다.

왜 필요한가 (밸런스 시뮬레이터 기획서 §2.1)
  마스터 수치를 고쳤을 때 "그래서 몇 초에 깨지는가"를 **측정할 수단이 없었다**. 이 도구가 그 측정을
  담당하며, `master-data-값.md` §9의 목표 시간(일반 30~50초 · 보스 60초)과 기준 파티(지역별 인원·
  레벨·장비)가 실제로 성립하는지 100 스테이지 전수로 확인한다.

목표와 기준 (값 문서 §9.1 — 이 파일의 TARGET_SEC · ACT_STANDARD · ACT_FLOOR가 그 사본이다)
  · 목표 시간 : 한 지역 안에서 1스테이지 30초 → 9스테이지 50초, 보스 스테이지 60초 (±20% 허용)
  · 기준 파티 : 1지역 1명(신규 계정 Lv1·등급1 무기·액티브 1종·투자 없음) → 2지역 2명 → 3지역부터 3명.
                2지역부터는 **장비작(등급 상승 + 강화)과 스킬작(액티브·패시브 레벨)이 기준에 포함**된다.
  · 긴장도   : 기준 파티의 **최저 체력**(전투 중 떨어진 최저값)이 판정 지표다. 아슬아슬하게 깨지도록
                지역이 오를수록 좁힌다 — 9스테이지 46→30% · 보스 23→15%.
  · 하한 파티 : 한 등급 낮은 장비 · 강화/스킬 0인 '투자 안 한 계정'(`--floor`). 1지역은 끝까지
                클리어하고 **2지역 중반부터 무너지도록** 잡혀 있다 — 그 지점이 투자를 요구하는 벽이다.
  · 기준 레벨 : 문서에 적지 않고 **마스터 데이터에서 계산한다** — 난이도1→2 순으로 100 스테이지를
                한 번씩 미는 최소 진행의 누적 경험치(stage_reward + level_master). 보상을 고치면
                기준 레벨도 자동으로 따라간다(`standard_level`).

정본 / 데이터 출처
  · 마스터 수치 : docs/세부/master-data/master-data-schema.sql  (INSERT를 직접 파싱 — DB·번들 불필요)
  · 전투 상수   : com2us-taskbar-hero-client/Assets/Scenes/GameScene.unity 의 BattleDevController
                  (스폰 주기·동시 상한·적 이동속도·피해 배수·대형 간격·파티별 사거리 등을 **읽어서** 쓴다)
  · 코드 상수   : Assets/Scripts/Battle/*.cs 에서 전사(transcribe)한 값 — CODE_CONSTANTS 참조
                  (각 항목에 `파일:줄` 출처를 달아 두었다. 클라 코드를 고치면 여기도 고쳐야 한다)
  · 이펙트 길이/반경 : Assets/Prefabs/Effects/*.prefab 의 프레임 수·fps·스케일과 프레임 PNG 크기에서 산출
                  (스킬 모션 시간과 광역 판정 반경이 여기서 나온다)

  ⚠️ 이 스크립트는 **전투 규칙의 사본**이다(기획서 §4는 코어 추출·공유를 요구한다). 사본인 동안은
     클라이언트 전투 코드를 고치면 이 파일도 함께 고쳐야 어긋나지 않는다. 어긋남을 빨리 알아채도록
     ① 씬·프리팹에서 읽을 수 있는 값은 전부 읽고(하드코딩 최소화)
     ② `baseline` 명령이 값 문서 §9의 손계산 기준표를 재현해 대조한다.

사용법 (옵션을 주지 않으면 그 지역의 기준 파티·기준 레벨·기준 장비로 돈다)
  python tools/balance_sim.py stage 1-1-1                   # 1지역 난이도1 1스테이지
  python tools/balance_sim.py stage 1-1-1 --party 3         # 같은 스테이지를 마법사 솔로로
  python tools/balance_sim.py act 2 --floor                 # 투자 안 한 계정이 어디서 막히는지
  python tools/balance_sim.py act 2                         # 한 Act 10스테이지 표
  python tools/balance_sim.py all --brief                   # 100스테이지 전수 중 목표를 벗어난 것만
  python tools/balance_sim.py baseline                      # 값.md §9 기준표 재현·대조
  python tools/balance_sim.py cycle 1-1-9                   # 실사이클 vs 오프라인 60초 가정
  python tools/balance_sim.py constants                     # 읽어 온 상수·이펙트 표 확인

  주요 옵션
    --party auto|1,4,3   파티 직업(기사1·레인저2·마법사3·슬레이어4). auto = 지역별 기준 인원
    --level auto|N       캐릭터 레벨(auto = 마스터 데이터에서 계산한 그 스테이지의 기준 레벨)
    --gear auto|none|rough|weapon[:G]|full[:G]   장비(auto = 지역별 기준 장비)
    --enhance auto|N       장비 강화 단계(0~10) · --skill-level auto|N 액티브 스킬 레벨
    --passive-level auto|N 패시브 스킬 레벨 · --rune-level N 룬 레벨(기본 0)
    --skills-per-class auto|N 장착 액티브 수
    --floor           하한 파티(장비작·스킬작을 전혀 하지 않은 계정)로 돌린다
    --no-crit         치명타 없음(난수 영향 제거)
    --repeat N        치명타 난수 때문에 결과가 흔들리므로 N회 반복해 중위/최소/최대를 낸다
    --dt              고정 스텝(기본 1/60 = 실게임 프레임)  --aspect 화면 비(스폰 거리에 영향)
    --json            기계 판독용 JSON 출력   --verbose 전투 로그

의존성: 표준 라이브러리만 (설치 불필요)
"""

import argparse
import json
import math
import os
import random
import re
import struct
import sys
from dataclasses import dataclass, field

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding="utf-8")
    except Exception:
        pass

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.dirname(SCRIPT_DIR)
CLIENT_ROOT = os.path.join(REPO_ROOT, "com2us-taskbar-hero-client")
DEFAULT_SCHEMA = os.path.join(REPO_ROOT, "docs", "세부", "master-data", "master-data-schema.sql")
DEFAULT_SCENE = os.path.join(CLIENT_ROOT, "Assets", "Scenes", "GameScene.unity")
EFFECTS_DIR = os.path.join(CLIENT_ROOT, "Assets", "Prefabs", "Effects")
ART_EFFECT_DIR = os.path.join(CLIENT_ROOT, "Assets", "Art", "Effect")


# =============================================================================
# 1. 클라이언트 코드에서 전사한 상수 (씬·프리팹에서 읽을 수 없는 것들)
#    값을 바꿀 때는 반드시 아래 출처 파일의 상수도 함께 본다.
# =============================================================================
CODE_CONSTANTS = {
    # 방어력 경감 반감점 — 피해 = raw × K/(K+def)
    "defense_mitigation_k": (100.0, "Battle/BattleDevController.cs:223 DefenseMitigationK"),
    # 교전 히스테리시스: 최전방 거리가 (사거리+이 값)을 넘으면 다시 전진
    "engage_hysteresis": (0.3, "Battle/BattleDevController.cs:679 UpdatePhase"),
    # 이동 판정 임계값 / idle 전환 디바운스
    "move_epsilon": (0.02, "Battle/PlayerCombatant.cs:655 UpdateMovement"),
    "move_catchup_gain": (0.6, "Battle/PlayerCombatant.cs:657 (뒤처질수록 가속)"),
    # 몬스터 정지 판정 임계값
    "monster_stop_epsilon": (0.02, "Battle/MonsterUnit.cs:655 Update"),
    # 보스: 체력 70%·30% 통과 시 공격 주기 × 0.78, 공격 전 0.6초 예고
    "boss_phase_thresholds": ((0.7, 0.3), "Battle/MonsterUnit.cs:53 BossPhaseThresholds"),
    "boss_phase_interval_factor": (0.78, "Battle/MonsterUnit.cs:54 BossPhaseIntervalFactor"),
    "boss_telegraph_seconds": (0.6, "Battle/MonsterUnit.cs:35 TelegraphSeconds"),
    # 넉백(스킬 0.9 / 돌진 3.2, 보스는 0.35배). 밀린 뒤 3배 속도로 전선 복귀
    "skill_knockback": (0.9, "Battle/MonsterUnit.cs:94 SkillKnockback"),
    "charge_knockback": (3.2, "Battle/MonsterUnit.cs:96 ChargeKnockback"),
    "boss_knockback_factor": (0.35, "Battle/MonsterUnit.cs:98 BossKnockbackFactor"),
    "knockback_speed": (9.0, "Battle/MonsterUnit.cs:99 KnockbackSpeed"),
    "knockback_max_seconds": (0.28, "Battle/MonsterUnit.cs:100 KnockbackMaxSeconds"),
    "knockback_limit": (3.5, "Battle/MonsterUnit.cs:101 KnockbackLimit"),
    "knockback_return_speed": (3.0, "Battle/MonsterUnit.cs:102 KnockbackReturnSpeed"),
    # 돌진: 자세 최소 유지 시간 / 유효 속도 하한(걷기 1.5배)
    "min_charge_motion": (0.45, "Battle/PlayerCombatant.cs:70 MinChargeMotion"),
    "charge_speed_move_factor": (1.5, "Battle/PlayerCombatant.cs:1214 ChargeSpeedEffective"),
    # 치명피해 기본값(마스터에 0일 때) / 화살 투사체 속도
    "default_crit_damage": (1.5, "Battle/PlayerCombatant.cs:119 DefaultCritDamage"),
    "arrow_speed": (9.0, "Battle/PlayerCombatant.cs:48 _arrowSpeed"),
    # 공격 모션 길이(SPUM 클립). 이 모션이 끝나기 전에는 이동·다음 행동을 하지 않는다.
    #   0_Attack_Normal 0.4167s · 0_Attack_Bow 0.8333s · 0_Attack_Magic 0.4167s (m_StopTime)
    "attack_motion_default": (0.4167, "SPUM .../0_Attack_Normal.anim m_StopTime"),
    "attack_motion_bow": (0.8333, "SPUM .../0_Attack_Bow.anim m_StopTime"),
    "attack_motion_magic": (0.4167, "SPUM .../0_Attack_Magic.anim m_StopTime"),
    # 화살비: 모션 + 0.4초(점프·홀드·착지) 동안 다음 행동 금지
    "arrow_rain_extra_busy": (0.4, "Battle/PlayerCombatant.cs:1068"),
    # 버프 기본 지속(마스터 duration이 0일 때)
    "buff_default_duration": (5.0, "Battle/PlayerCombatant.cs:1029"),
    # 연출이 실시간을 먹는 양(게임 시간과 벽시계 시간의 차이 — §9.4 "연출이 만드는 시간")
    # 히트스톱·슬로우는 WaitForSecondsRealtime으로 기다린다 — 아래 초는 **실시간**이다
    "hitstop_seconds": (0.06, "Battle/BattleDevController.cs:118 HitStopSeconds"),
    "hitstop_boss_seconds": (0.09, "Battle/BattleDevController.cs:119 BossHitStopSeconds"),
    "hitstop_scale": (0.05, "Battle/BattleDevController.cs:117 HitStopScale"),
    "hitstop_cooldown": (0.3, "Battle/BattleDevController.cs:120 HitStopCooldown"),
    "wave_clear_slow_scale": (0.3, "Battle/BattleDevController.cs:122 WaveClearSlowScale"),
    "wave_clear_slow_seconds": (0.15, "Battle/BattleDevController.cs:123 WaveClearSlowSeconds"),
    # 스테이지 사이 오버헤드(전투 밖) — 실사이클 추정에 쓴다
    "clear_overlay_seconds": (5.0, "Battle/StageClearOverlay.cs:31 AutoCloseSeconds"),
    "defeat_overlay_seconds": (2.5, "Battle/BattleDefeatOverlay.cs:16 AutoCloseSeconds"),
    # 서버가 가정하는 클리어 주기 — 이 도구가 검증하려는 대상
    "assumed_clear_interval_sec": (60.0, "GameServer/Services/OfflineService.cs:25 AssumedClearIntervalSec"),
    "offline_efficiency_divisor": (2.0, "GameServer/Services/OfflineService.cs:26"),
}


def K(name):
    """CODE_CONSTANTS 값 조회(출처 문자열 제외)."""
    return CODE_CONSTANTS[name][0]


# 목표 클리어 시간(값 문서 §9.1) — 한 지역 안에서 30초 → 50초로 점진 상승, 보스(10)는 60초.
#   스테이지 1~10을 인덱스로 읽는다. `verdict`가 ±20% 밴드로 판정한다.
TARGET_SEC = [30.0, 32.5, 35.0, 37.5, 40.0, 42.5, 45.0, 47.5, 50.0, 60.0]
TARGET_TOLERANCE = 0.20


def target_sec(stage):
    return TARGET_SEC[min(max(stage, 1), len(TARGET_SEC)) - 1]


# 기준 파티(값 문서 §9.1) — 지역이 오를수록 인원·장비·투자가 함께 오른다.
#   party    : 직업 코드(1 기사 · 2 레인저 · 3 마법사 · 4 슬레이어). Act1은 신규 계정의 캐릭터 1명.
#   gear     : 그 지역에서 실제로 구할 수 있는 등급의 6부위(1지역만 생성 시 지급되는 등급1 무기).
#   skills   : 장착 액티브 수(신규 캐릭터는 1종만 습득한 상태로 시작한다).
#   enhance  : 장비 강화 단계 — **장비작**의 크기
#   skill    : 액티브 스킬 레벨 · passive: 패시브 스킬 레벨 — **스킬작**의 크기
#   레벨은 여기 적지 않는다 — 마스터 데이터(stage_reward 경험치 + level_master)에서 계산한다
#   (`standard_level`). 보상을 조정하면 기준 레벨도 자동으로 따라간다.
ACT_STANDARD = {
    1: {"party": [1], "gear": "weapon:1", "skills": 1, "enhance": 0, "skill": 1, "passive": 0},
    2: {"party": [1, 2], "gear": "full:2", "skills": 2, "enhance": 2, "skill": 3, "passive": 3},
    3: {"party": [1, 2, 3], "gear": "full:3", "skills": 2, "enhance": 3, "skill": 5, "passive": 5},
    4: {"party": [1, 2, 3], "gear": "full:4", "skills": 2, "enhance": 4, "skill": 7, "passive": 7},
    5: {"party": [1, 2, 3], "gear": "full:5", "skills": 2, "enhance": 5, "skill": 9, "passive": 9},
}

# 하한 파티(값 문서 §9.1) — **투자를 전혀 하지 않은 계정**. 한 등급 낮은 장비 · 강화/스킬/패시브 0.
#   이 파티가 어디까지 버티는지가 "언제부터 장비작·스킬작이 필요한가"의 답이다.
#   설계 의도: 1지역은 끝까지 클리어(아슬아슬), 2지역 중반부터 전사·전멸이 시작된다.
ACT_FLOOR = {
    1: {"party": [1], "gear": "weapon:1", "skills": 1, "enhance": 0, "skill": 1, "passive": 0},
    2: {"party": [1, 2], "gear": "full:1", "skills": 2, "enhance": 0, "skill": 1, "passive": 0},
    3: {"party": [1, 2, 3], "gear": "full:2", "skills": 2, "enhance": 0, "skill": 1, "passive": 0},
    4: {"party": [1, 2, 3], "gear": "full:3", "skills": 2, "enhance": 0, "skill": 1, "passive": 0},
    5: {"party": [1, 2, 3], "gear": "full:4", "skills": 2, "enhance": 0, "skill": 1, "passive": 0},
}

# 긴장도(값 문서 §9.3) — 전투 중 파티가 떨어진 최저 체력 비율의 현재 실측 곡선.
#   "아슬아슬하게 깨는 맛"을 위해 지역이 오를수록 좁히는 것이 목표이며,
#   2지역이 헐겁고(0.47) 4지역이 아슬아슬한(0.07) 불균형은 미결이다(기획서 11장 9번).
TENSION_S9 = {1: 0.22, 2: 0.47, 3: 0.20, 4: 0.07, 5: 0.14}
TENSION_BOSS = {1: 0.59, 2: 0.61, 3: 0.43, 4: 0.23, 5: 0.21}


# =============================================================================
# 2. 마스터 데이터 로더 — schema.sql의 INSERT를 직접 파싱한다
#    (DB·클라 번들이 아니라 정본 SQL을 읽으므로 master_monster_tool로 값을 고친 직후 바로 반영된다)
# =============================================================================
def _split_top_level(text, sep=","):
    """따옴표·괄호 깊이를 무시하지 않고 최상위 구분자로만 자른다."""
    out, buf, depth, quote = [], [], 0, False
    i = 0
    while i < len(text):
        c = text[i]
        if quote:
            if c == "'":
                if i + 1 < len(text) and text[i + 1] == "'":
                    buf.append("''")
                    i += 2
                    continue
                quote = False
            buf.append(c)
        elif c == "'":
            quote = True
            buf.append(c)
        elif c == "(":
            depth += 1
            buf.append(c)
        elif c == ")":
            depth -= 1
            buf.append(c)
        elif c == sep and depth == 0:
            out.append("".join(buf))
            buf = []
        else:
            buf.append(c)
        i += 1
    if buf:
        out.append("".join(buf))
    return out


def _sql_value(tok):
    """SQL 리터럴 → python 값."""
    t = tok.strip()
    if t.upper() == "NULL":
        return None
    if t.startswith("'"):
        return t[1:-1].replace("''", "'")
    try:
        return int(t)
    except ValueError:
        pass
    try:
        return float(t)
    except ValueError:
        return t


def parse_sql_inserts(path):
    """schema.sql의 모든 INSERT를 {테이블: [행 dict, ...]}로 읽는다(주석·DDL 무시)."""
    with open(path, "r", encoding="utf-8") as f:
        raw = f.read()
    # 줄 주석 제거(따옴표 안의 '--'는 이 스키마에 없다)
    lines = []
    for line in raw.splitlines():
        s = line.split("--")[0] if line.lstrip().startswith("--") else line
        lines.append(s)
    text = "\n".join(lines)

    tables = {}
    pattern = re.compile(r"INSERT\s+INTO\s+(\w+)\s*\(([^)]*)\)\s*VALUES", re.IGNORECASE)
    for m in pattern.finditer(text):
        table = m.group(1)
        cols = [c.strip() for c in m.group(2).split(",")]
        # VALUES 이후 첫 ';' 까지(따옴표 안의 ';'는 없다)
        body_start = m.end()
        depth, quote, end = 0, False, None
        i = body_start
        while i < len(text):
            c = text[i]
            if quote:
                if c == "'":
                    if i + 1 < len(text) and text[i + 1] == "'":
                        i += 2
                        continue
                    quote = False
            elif c == "'":
                quote = True
            elif c == "(":
                depth += 1
            elif c == ")":
                depth -= 1
            elif c == ";" and depth == 0:
                end = i
                break
            i += 1
        body = text[body_start:end if end else len(text)]
        rows = tables.setdefault(table, [])
        for chunk in _split_top_level(body):
            chunk = chunk.strip()
            # 주석 줄이 섞여 있으면 걷어낸다
            chunk = "\n".join(ln for ln in chunk.splitlines() if not ln.strip().startswith("--")).strip()
            if not chunk.startswith("("):
                chunk = chunk[chunk.find("("):] if "(" in chunk else ""
            if not chunk.startswith("(") or not chunk.endswith(")"):
                continue
            vals = [_sql_value(v) for v in _split_top_level(chunk[1:-1])]
            if len(vals) != len(cols):
                continue
            rows.append(dict(zip(cols, vals)))
    return tables


@dataclass
class ClassMaster:
    code: int
    name: str
    hp: int
    atk: int
    dfn: int
    move_speed: float
    crit_chance: float
    crit_damage: float
    cooldown: float


@dataclass
class SkillMaster:
    code: int
    class_code: int
    name: str
    skill_type: int      # 1=액티브 2=패시브
    stat_type: int
    max_level: int
    cooldown: float
    coefs: dict = field(default_factory=dict)  # level -> [(coef_type, coef, duration)]


@dataclass
class ItemMaster:
    code: int
    name: str
    item_type: int
    grade: int
    equip_slot: int
    class_req: int
    level_req: int
    hp: int
    atk: int
    dfn: int
    move_speed: float
    crit_chance: float
    crit_damage: float
    cooldown: float


@dataclass
class StageMaster:
    act: int
    difficulty: int
    stage: int
    stage_id: int
    boss_monster_code: int
    spawns: list = field(default_factory=list)   # [(monster_code, count)]
    reward_gold: int = 0
    reward_exp: int = 0


class MasterData:
    def __init__(self, schema_path):
        self.schema_path = schema_path
        t = parse_sql_inserts(schema_path)

        self.classes = {}
        for r in t.get("class_master", []):
            self.classes[r["class_code"]] = ClassMaster(
                r["class_code"], r["name"], int(r["hp"]), int(r["atk"]), int(r["def"]),
                float(r["move_speed"]), float(r["crit_chance"]), float(r["crit_damage"]),
                float(r["cooldown"]))

        # level_master의 bonus_*는 누적값이다(클라 LoadStats가 그대로 더한다)
        self.levels = {r["level"]: r for r in t.get("level_master", [])}
        self.max_level = max(self.levels) if self.levels else 1

        self.skills = {}
        for r in t.get("skill_master", []):
            self.skills[r["skill_code"]] = SkillMaster(
                r["skill_code"], r["class_code"], r["name"], int(r["skill_type"]),
                int(r["stat_type"]), int(r["max_level"]), float(r["cooldown"]))
        for r in t.get("skill_coefficient", []):
            sk = self.skills.get(r["skill_code"])
            if sk is None:
                continue
            sk.coefs.setdefault(int(r["skill_level"]), []).append(
                (int(r["coef_type"]), float(r["coef"]), float(r["duration"])))

        self.monsters = {r["monster_code"]: (r["name"], int(r["hp"]), int(r["attack"]))
                         for r in t.get("monster_master", [])}

        self.items = {}
        for r in t.get("item_master", []):
            self.items[r["item_code"]] = ItemMaster(
                r["item_code"], r["name"], int(r["item_type"]), int(r["grade"]), int(r["equip_slot"]),
                int(r["class_req"]), int(r["level_req"]), int(r["hp"]), int(r["atk"]), int(r["def"]),
                float(r["move_speed"]), float(r["crit_chance"]), float(r["crit_damage"]),
                float(r["cooldown"]))

        self.enhance = {int(r["enhance_level"]): float(r["stat_multiplier"])
                        for r in t.get("enhance_master", [])}
        self.runes = {r["rune_code"]: (int(r["stat_type"]), float(r["stat_value"]), int(r["max_level"]))
                      for r in t.get("rune_master", [])}

        self.stages = {}
        by_id = {}
        for r in t.get("stage_master", []):
            st = StageMaster(int(r["act"]), int(r["difficulty"]), int(r["stage"]), int(r["stage_id"]),
                             int(r["boss_monster_code"]))
            self.stages[(st.act, st.difficulty, st.stage)] = st
            by_id[st.stage_id] = st
        for r in t.get("stage_spawn", []):
            st = by_id.get(r["stage_id"])
            if st:
                st.spawns.append((int(r["monster_code"]), int(r["spawn_count"])))
        for r in t.get("stage_reward", []):
            st = by_id.get(r["stage_id"])
            if st:
                st.reward_gold = int(r["reward_gold"])
                st.reward_exp = int(r["reward_exp"])
        # 스폰 순서는 서버가 stage_spawn을 읽은 순서(=코드 오름차순)를 그대로 내려준다
        for st in self.stages.values():
            st.spawns.sort(key=lambda kv: kv[0])

    def level_bonus(self, level):
        r = self.levels.get(min(max(level, 1), self.max_level))
        if not r:
            return (0, 0, 0)
        return (int(r["bonus_hp"]), int(r["bonus_atk"]), int(r["bonus_def"]))

    def enhance_multiplier(self, lv):
        if lv <= 0:
            return 1.0
        return self.enhance.get(min(lv, max(self.enhance) if self.enhance else 0), 1.0)

    def actives_of(self, class_code):
        return sorted([s for s in self.skills.values()
                       if s.class_code == class_code and s.skill_type == 1],
                      key=lambda s: s.code)

    def passives_of(self, class_code):
        return sorted([s for s in self.skills.values()
                       if s.class_code == class_code and s.skill_type == 2],
                      key=lambda s: s.code)


# =============================================================================
# 3. 클라이언트 씬에서 전투 상수 읽기 (BattleDevController + PartyMemberConfig)
# =============================================================================
@dataclass
class SkillVisual:
    code: int
    effect_guid: str = ""
    scale: float = 1.0
    offset_x: float = 0.0
    offset_y: float = 0.0
    hit_time_ratio: float = 1.0


@dataclass
class PartyMemberCfg:
    order: int
    label: str = ""
    class_code: int = 0
    ranged: bool = False
    attack_range: float = 1.5
    attack_anim: str = ""
    cast_hold: bool = False
    charge_skill_code: int = 0
    charge_range: float = 4.0
    charge_speed: float = 12.0
    rain_skill_code: int = 0
    slam_skill_code: int = 0
    slam_air_time: float = 0.46
    aoe_skill_codes: list = field(default_factory=list)
    basic_attack_aoe: bool = False
    self_effect_x_offset: float = 0.0
    all_skills_aoe: bool = False
    has_basic_projectile: bool = False
    skills: dict = field(default_factory=dict)   # code -> SkillVisual


@dataclass
class BattleConfig:
    """씬에서 읽은 컨트롤러 필드(읽기 실패 시 기본값 = BattleDevScene 실측값)."""
    source: str = "(내장 기본값)"
    enemy_spawn_interval: float = 1.5
    max_concurrent_enemies: int = 5
    enemy_offscreen_margin: float = 2.0
    enemy_move_speed: float = 1.2
    enemy_front_stop_gap: float = 1.2
    enemy_attack_interval: float = 1.5
    enemy_damage_multiplier: float = 2.0
    boss_move_speed_factor: float = 0.6
    cam_offset_x: float = 2.5
    cam_rear_margin: float = 1.2
    fallback_move_speed: float = 3.0
    range_group_size: float = 3.0
    row_spacing_x: float = 1.6
    row_y_spacing: float = 0.8
    basic_attack_hit_delay: float = 0.35
    effect_y_offset: float = 0.6
    skill_cooldown_fallback: float = 10.0
    ortho_size: float = 4.0
    party: list = field(default_factory=list)


_SCALAR_FIELDS = {
    "enemySpawnInterval": ("enemy_spawn_interval", float),
    "maxConcurrentEnemies": ("max_concurrent_enemies", int),
    "enemyOffscreenMargin": ("enemy_offscreen_margin", float),
    "enemyMoveSpeed": ("enemy_move_speed", float),
    "enemyFrontStopGap": ("enemy_front_stop_gap", float),
    "enemyAttackInterval": ("enemy_attack_interval", float),
    "enemyDamageMultiplier": ("enemy_damage_multiplier", float),
    "bossMoveSpeedFactor": ("boss_move_speed_factor", float),
    "camOffsetX": ("cam_offset_x", float),
    "camRearMargin": ("cam_rear_margin", float),
    "fallbackMoveSpeed": ("fallback_move_speed", float),
    "rangeGroupSize": ("range_group_size", float),
    "rowSpacingX": ("row_spacing_x", float),
    "rowYSpacing": ("row_y_spacing", float),
    "basicAttackHitDelay": ("basic_attack_hit_delay", float),
    "effectYOffset": ("effect_y_offset", float),
    "skillCooldown": ("skill_cooldown_fallback", float),
}

_MEMBER_FIELDS = {
    "classCode": ("class_code", int),
    "ranged": ("ranged", bool),
    "attackRange": ("attack_range", float),
    "attackAnim": ("attack_anim", str),
    "castHold": ("cast_hold", bool),
    "chargeSkillCode": ("charge_skill_code", int),
    "chargeRange": ("charge_range", float),
    "chargeSpeed": ("charge_speed", float),
    "rainSkillCode": ("rain_skill_code", int),
    "slamSkillCode": ("slam_skill_code", int),
    "slamAirTime": ("slam_air_time", float),
    "basicAttackAoe": ("basic_attack_aoe", bool),
    "selfEffectXOffset": ("self_effect_x_offset", float),
    "allSkillsAoe": ("all_skills_aoe", bool),
}


def _unity_int_array(hexstr):
    """Unity가 int 리스트를 직렬화한 리틀엔디안 hex 문자열(예: '9101000093010000') → [401, 403]."""
    s = (hexstr or "").strip()
    out = []
    for i in range(0, len(s) - 7, 8):
        word = s[i:i + 8]
        try:
            out.append(int.from_bytes(bytes.fromhex(word), "little"))
        except ValueError:
            continue
    return out


def _unity_string(value):
    """Unity YAML 문자열(한글은 "\\uAE30\\uC0AC" 형태로 이스케이프된다) → 사람이 읽는 문자열."""
    s = (value or "").strip()
    if s.startswith('"') and s.endswith('"'):
        try:
            return json.loads(s)
        except Exception:
            return s[1:-1]
    return s


def _guid_of(value):
    m = re.search(r"guid:\s*([0-9a-f]{32})", value or "")
    return m.group(1) if m else ""


def load_battle_config(scene_path):
    """씬(GameScene/BattleDevScene)의 BattleDevController 필드와 party 구성을 읽는다."""
    cfg = BattleConfig()
    if not os.path.isfile(scene_path):
        return cfg
    with open(scene_path, "r", encoding="utf-8", errors="replace") as f:
        lines = f.read().splitlines()

    # party: 블록이 있는 MonoBehaviour(BattleDevController)를 찾는다
    start = None
    for i, ln in enumerate(lines):
        if ln.rstrip() == "  party:":
            start = i
            break
    if start is None:
        return cfg
    cfg.source = os.path.relpath(scene_path, REPO_ROOT)

    # 카메라 orthographic size (전투 카메라). 값이 여러 개면 첫 값을 쓴다.
    for ln in lines:
        m = re.match(r"\s*orthographic size:\s*([0-9.]+)", ln)
        if m:
            cfg.ortho_size = float(m.group(1))
            break

    # --- party 리스트 파싱 ---
    i = start + 1
    members, cur, cur_skill = [], None, None
    in_skills = False
    while i < len(lines):
        ln = lines[i]
        if ln.startswith("--- !u!"):
            break
        m = re.match(r"  - label:\s*(.*)$", ln)
        if m:
            cur = PartyMemberCfg(order=len(members), label=_unity_string(m.group(1)))
            members.append(cur)
            in_skills = False
            i += 1
            continue
        if cur is None:
            if re.match(r"  \w+:", ln):     # party가 비어 있으면 바로 스칼라 필드
                break
            i += 1
            continue
        if re.match(r"    skills:\s*$", ln):
            in_skills = True
            i += 1
            continue
        m = re.match(r"    - skillCode:\s*(-?\d+)", ln)
        if m and in_skills:
            cur_skill = SkillVisual(code=int(m.group(1)))
            cur.skills[cur_skill.code] = cur_skill
            i += 1
            continue
        if in_skills and cur_skill is not None:
            m = re.match(r"      (\w+):\s*(.*)$", ln)
            if m:
                key, val = m.group(1), m.group(2).strip()
                if key == "effect":
                    cur_skill.effect_guid = _guid_of(val)
                elif key == "effectScale":
                    cur_skill.scale = float(val)
                elif key == "hitTimeRatio":
                    cur_skill.hit_time_ratio = float(val)
                elif key == "effectOffset":
                    mo = re.search(r"x:\s*(-?[0-9.]+),\s*y:\s*(-?[0-9.]+)", val)
                    if mo:
                        cur_skill.offset_x = float(mo.group(1))
                        cur_skill.offset_y = float(mo.group(2))
                i += 1
                continue
        m = re.match(r"    (\w+):\s*(.*)$", ln)
        if m:
            key, val = m.group(1), m.group(2).strip()
            in_skills = False
            if key in _MEMBER_FIELDS:
                attr, typ = _MEMBER_FIELDS[key]
                try:
                    setattr(cur, attr, (val != "0") if typ is bool else typ(val) if typ is not str else val)
                except ValueError:
                    pass
            elif key == "aoeSkillCodes":
                cur.aoe_skill_codes = _unity_int_array(val)
            elif key == "basicAttackProjectile":
                cur.has_basic_projectile = "guid:" in val
            i += 1
            continue
        # party 리스트가 끝나고 컨트롤러 스칼라 필드 구간으로 넘어갔다
        if re.match(r"  \w+:", ln):
            break
        i += 1

    cfg.party = members

    # --- 컨트롤러 스칼라 필드 파싱(같은 컴포넌트 블록 안) ---
    while i < len(lines):
        ln = lines[i]
        if ln.startswith("--- !u!"):
            break
        m = re.match(r"  (\w+):\s*(-?[0-9.]+)\s*$", ln)
        if m and m.group(1) in _SCALAR_FIELDS:
            attr, typ = _SCALAR_FIELDS[m.group(1)]
            try:
                setattr(cfg, attr, typ(float(m.group(2))))
            except ValueError:
                pass
        i += 1
    return cfg


# =============================================================================
# 4. 이펙트 프리팹 → 스킬 모션 길이 · 광역 판정 반경
#    모션 길이 = 프레임 수 / fps  (PlayerCombatant.EffectDuration)
#    광역 반경 = max(가로, 세로) / 2 (렌더 바운즈 extents — PlayerCombatant.EffectRadius)
# =============================================================================
@dataclass
class EffectInfo:
    name: str = ""
    frames: int = 0
    fps: float = 0.0
    scale: float = 1.0          # 프리팹 루트 localScale.x
    px_w: int = 0
    px_h: int = 0
    ppu: float = 100.0

    @property
    def duration(self):
        return self.frames / self.fps if self.frames and self.fps > 0 else 0.0

    def radius(self, effect_scale=1.0):
        if not self.px_w or not self.px_h or self.ppu <= 0:
            return 0.0
        s = self.scale * (effect_scale if effect_scale > 0 else 1.0)
        return max(self.px_w / self.ppu * s, self.px_h / self.ppu * s) / 2.0


def _png_size(path):
    try:
        with open(path, "rb") as f:
            head = f.read(24)
        if head[:8] != b"\x89PNG\r\n\x1a\n":
            return (0, 0)
        w, h = struct.unpack(">II", head[16:24])
        return (int(w), int(h))
    except Exception:
        return (0, 0)


class VisualLibrary:
    """이펙트 프리팹(길이·스케일)과 첫 프레임 PNG(크기)를 읽어 스킬 시각 정보를 만든다."""

    def __init__(self):
        self.by_guid = {}     # 이펙트 프리팹 guid -> EffectInfo
        self.errors = []
        self._png_by_guid = self._index_guids(ART_EFFECT_DIR, ".png")
        for path in sorted(self._prefab_paths()):
            guid = self._meta_guid(path + ".meta")
            if not guid:
                continue
            info = self._parse_prefab(path)
            if info:
                self.by_guid[guid] = info

    @staticmethod
    def _prefab_paths():
        if not os.path.isdir(EFFECTS_DIR):
            return []
        return [os.path.join(EFFECTS_DIR, n) for n in os.listdir(EFFECTS_DIR) if n.endswith(".prefab")]

    @staticmethod
    def _meta_guid(meta_path):
        try:
            with open(meta_path, "r", encoding="utf-8", errors="replace") as f:
                for ln in f:
                    m = re.match(r"guid:\s*([0-9a-f]{32})", ln.strip())
                    if m:
                        return m.group(1)
        except Exception:
            return ""
        return ""

    def _index_guids(self, root, ext):
        """{guid: (파일 경로, ppu)} — 프레임 PNG 조회용."""
        out = {}
        if not os.path.isdir(root):
            return out
        for dirpath, _dirs, files in os.walk(root):
            for name in files:
                if not name.endswith(ext + ".meta"):
                    continue
                meta = os.path.join(dirpath, name)
                guid, ppu = "", 100.0
                try:
                    with open(meta, "r", encoding="utf-8", errors="replace") as f:
                        for ln in f:
                            s = ln.strip()
                            m = re.match(r"guid:\s*([0-9a-f]{32})", s)
                            if m:
                                guid = m.group(1)
                            m = re.match(r"spritePixelsToUnits:\s*([0-9.]+)", s)
                            if m:
                                ppu = float(m.group(1))
                except Exception:
                    continue
                if guid:
                    out[guid] = (meta[: -len(".meta")], ppu)
        return out

    def _parse_prefab(self, path):
        info = EffectInfo(name=os.path.splitext(os.path.basename(path))[0])
        try:
            with open(path, "r", encoding="utf-8", errors="replace") as f:
                lines = f.read().splitlines()
        except Exception as e:
            self.errors.append(f"{path}: {e}")
            return None
        in_frames = False
        first_frame_guid = ""
        for ln in lines:
            m = re.match(r"\s*m_LocalScale:\s*\{x:\s*(-?[0-9.]+)", ln)
            if m and info.scale == 1.0:
                info.scale = abs(float(m.group(1)))
                continue
            if re.match(r"\s*frames:\s*$", ln):
                in_frames = True
                continue
            if in_frames:
                m = re.match(r"\s*-\s*\{fileID:.*guid:\s*([0-9a-f]{32})", ln)
                if m:
                    info.frames += 1
                    if not first_frame_guid:
                        first_frame_guid = m.group(1)
                    continue
                in_frames = False
            m = re.match(r"\s*fps:\s*([0-9.]+)", ln)
            if m:
                info.fps = float(m.group(1))
        if first_frame_guid in self._png_by_guid:
            png, ppu = self._png_by_guid[first_frame_guid]
            info.px_w, info.px_h = _png_size(png)
            info.ppu = ppu
        return info

    def get(self, guid):
        return self.by_guid.get(guid)


# =============================================================================
# 5. 파티 스탯 계산 — PlayerCombatant.LoadStats / ApplyEquipStats 이식
# =============================================================================
@dataclass
class AllyStats:
    class_code: int
    name: str
    level: int
    atk: int
    dfn: int
    max_hp: int
    crit_chance: float
    crit_damage: float
    cooldown: float
    move_speed: float
    equipped: list = field(default_factory=list)   # 표시용 [(이름, 강화)]


def _round_half_even(x):
    """C# Math.Round(double)와 같은 규칙(중간값은 짝수로)."""
    return int(round(x))


def build_ally_stats(md, class_code, level, equipment, enhance_level,
                     passive_level, rune_level):
    """직업 기본 + 레벨 보너스 + 장비(강화 배율) → 패시브·룬 배율 적용."""
    cm = md.classes[class_code]
    hp, atk, dfn = cm.hp, cm.atk, cm.dfn
    crit_chance, crit_damage = cm.crit_chance, cm.crit_damage
    if crit_damage <= 0:
        crit_damage = K("default_crit_damage")
    cooldown = cm.cooldown if cm.cooldown > 0 else 1.2
    move_speed = cm.move_speed if cm.move_speed > 0 else 3.0

    bhp, batk, bdef = md.level_bonus(level)
    hp += bhp
    atk += batk
    dfn += bdef

    em = md.enhance_multiplier(enhance_level)
    equipped_desc = []
    for code in equipment:
        it = md.items.get(code)
        if it is None:
            continue
        atk += _round_half_even(it.atk * em)
        hp += _round_half_even(it.hp * em)
        dfn += _round_half_even(it.dfn * em)
        crit_chance += it.crit_chance * em
        crit_damage += it.crit_damage * em
        equipped_desc.append((it.name, enhance_level))

    # 패시브(습득 레벨의 coef를 곱) — skill_type 2, stat_type별
    passive_mult = {t: 1.0 for t in range(1, 8)}
    if passive_level > 0:
        for sk in md.passives_of(class_code):
            lv = min(max(passive_level, 1), max(1, sk.max_level))
            for (ct, coef, _dur) in sk.coefs.get(lv, []):
                if sk.stat_type in passive_mult:
                    passive_mult[sk.stat_type] *= coef
                break

    # 룬(계정 공용): 보너스 = stat_value × 레벨, statType 7은 감소 방향
    rune_mult = {t: 1.0 for t in range(1, 8)}
    if rune_level > 0:
        for (stat_type, stat_value, max_lv) in md.runes.values():
            lv = min(rune_level, max_lv)
            if lv < 1 or stat_type not in rune_mult:
                continue
            bonus = stat_value * lv
            rune_mult[stat_type] *= max(0.05, 1.0 - bonus) if stat_type == 7 else (1.0 + bonus)

    f_atk = max(1, _round_half_even(atk * passive_mult[1] * rune_mult[1]))
    f_def = max(0, _round_half_even(dfn * passive_mult[2] * rune_mult[2]))
    f_hp = max(1, _round_half_even(hp * passive_mult[3] * rune_mult[3]))
    f_cc = min(1.0, max(0.0, crit_chance * passive_mult[4] * rune_mult[4]))
    f_cd = max(1.0, crit_damage * passive_mult[5] * rune_mult[5])
    f_ms = move_speed * passive_mult[6] * rune_mult[6]
    f_cool = max(0.1, cooldown * rune_mult[7])

    return AllyStats(class_code, cm.name, level, f_atk, f_def, f_hp, f_cc, f_cd,
                     f_cool, f_ms, equipped_desc)


def pick_equipment(md, class_code, level, gear):
    """장비 프리셋 → 아이템 코드 목록.
    none        : 없음
    rough       : 무기만, 등급 = (레벨로 착용 가능한 최고 등급 − 2)  ← 값.md §9 "무기만 두 티어 낮은 등급"
    weapon:G    : 무기만 G등급
    full[:G]    : 6부위 전부 G등급(없는 부위는 건너뜀). G 생략 시 착용 가능한 최고 등급
    """
    if not gear or gear == "none":
        return []
    equips = [it for it in md.items.values()
              if it.item_type == 1 and it.equip_slot > 0
              and it.class_req in (0, class_code)]
    wearable = [it for it in equips if it.level_req <= level]
    top_grade = max([it.grade for it in wearable], default=1)

    def best(slot, grade):
        cands = [it for it in equips if it.equip_slot == slot and it.grade == grade]
        if not cands:
            return None
        # 같은 등급 내에서는 공격력이 높은 것(동률이면 코드 낮은 것)
        cands.sort(key=lambda it: (-it.atk, it.code))
        return cands[0]

    if gear == "rough":
        it = best(1, max(1, top_grade - 2))
        return [it.code] if it else []
    m = re.match(r"weapon(?::(\d))?$", gear)
    if m:
        it = best(1, int(m.group(1)) if m.group(1) else top_grade)
        return [it.code] if it else []
    m = re.match(r"full(?::(\d))?$", gear)
    if m:
        grade = int(m.group(1)) if m.group(1) else top_grade
        out = []
        for slot in range(1, 7):
            it = best(slot, grade)
            if it:
                out.append(it.code)
        return out
    raise SystemExit(f"[ERROR] 알 수 없는 --gear 값: {gear}")


# =============================================================================
# 6. 전투 시뮬레이터 (고정 스텝) — 클라이언트 Update 순서를 그대로 따른다
# =============================================================================
@dataclass
class SimSkill:
    code: int
    name: str
    coef_type: int
    coef: float
    duration: float
    stat_type: int
    hp_cost_ratio: float
    lifesteal_ratio: float
    lifesteal_duration: float
    cooldown: float
    timer: float
    motion: float
    hit_time_ratio: float
    is_aoe: bool
    radius: float
    casts: int = 0
    whiffs: int = 0          # 광역인데 반경 안에 적이 없어 한 대도 못 때린 횟수
    hits: int = 0


class Ally:
    """PlayerCombatant 1인."""

    def __init__(self, stats, cfg, skills, attack_motion):
        self.stats = stats
        self.cfg = cfg
        self.skills = skills
        self.attack_motion = attack_motion

        self.hp = stats.max_hp
        self.dead = False
        self.x = 0.0
        self.y = 0.0
        self.row = 0
        self.form_y = 0.0
        self.target = (0.0, 0.0)

        self.attack_timer = 0.0
        self.busy = 0.0
        self.motion_timer = 0.0      # IsAttackMotionPlaying 대체(공격 클립 재생 중)
        self.still_time = 0.0
        self.atk_buff = 1.0
        self.cooldown_buff = 1.0
        self.buff_timer = 0.0
        self.lifesteal_ratio = 0.0
        self.lifesteal_timer = 0.0

        self.charging = False
        self.charge_elapsed = 0.0
        self.charge_motion = 0.0
        self.charge_impacted = False
        self.charge_skill = next((s for s in skills if s.code == cfg.charge_skill_code), None)

        # 계측
        self.damage_dealt = 0
        self.damage_taken = 0
        self.min_hp_ratio = 1.0
        self.basic_attacks = 0

    @property
    def name(self):
        return self.stats.name

    @property
    def attack_cooldown(self):
        return max(0.05, self.stats.cooldown * self.cooldown_buff)

    def take_damage(self, dmg):
        if self.dead:
            return
        self.hp -= dmg
        self.damage_taken += dmg
        self.min_hp_ratio = min(self.min_hp_ratio, max(0.0, self.hp) / self.stats.max_hp)
        if self.hp <= 0:
            self.hp = 0
            self.dead = True

    def heal(self, amount):
        if self.dead or amount <= 0:
            return
        self.hp = min(self.stats.max_hp, self.hp + amount)


class Monster:
    def __init__(self, code, name, hp, atk, move_speed, is_boss, attack_interval, x):
        self.code = code
        self.name = name
        self.max_hp = max(1, hp)
        self.hp = self.max_hp
        self.atk = atk
        self.move_speed = move_speed
        self.is_boss = is_boss
        self.attack_interval = attack_interval
        self.x = x
        self.target_x = x
        self.alive = True
        self.engaged = False
        self.attack_timer = 0.0
        self.telegraph_timer = 0.0
        self.boss_phase = 0
        self.knockback_remaining = 0.0
        self.knockback_speed = K("knockback_speed")
        self.returning = False
        self.attacks = 0


@dataclass
class BattleResult:
    cleared: bool = False
    defeated: bool = False
    timeout: bool = False
    battle_seconds: float = 0.0
    engage_seconds: float = 0.0      # 첫 교전까지 걸린 시간(= 이동 시간)
    total_monsters: int = 0
    killed: int = 0
    spawn_idle_seconds: float = 0.0  # 적이 하나도 없어 대기한 시간(스폰 주기 병목)
    advance_wait_seconds: float = 0.0  # 교전 시작 후 다음 적이 걸어오길 기다린 시간
    theory_dps: float = 0.0          # 평타만 끊김 없이 넣었을 때의 이론 DPS(Σ 공격력/주기)
    whiffs: dict = field(default_factory=dict)
    party_dps: float = 0.0
    damage_dealt: int = 0
    damage_taken: int = 0
    min_hp_ratio: float = 1.0
    dead_allies: int = 0
    wallclock_seconds: float = 0.0   # 히트스톱·웨이브 슬로우 등 연출 지연을 더한 추정
    hitstops: int = 0
    per_ally: list = field(default_factory=list)
    skill_casts: dict = field(default_factory=dict)
    log: list = field(default_factory=list)
    ally_stats: list = field(default_factory=list)
    spawn_distance: float = 0.0


class Battle:
    """BattleDevController(serverMode) + PlayerCombatant + MonsterUnit의 고정 스텝 모사."""

    def __init__(self, md, cfg, visuals, stage, allies_spec, *, dt=1.0 / 60.0, aspect=16.0 / 9.0,
                 seed=1234, max_seconds=600.0, no_crit=False, verbose=False):
        self.md = md
        self.cfg = cfg
        self.visuals = visuals
        self.stage = stage
        self.dt = dt
        self.aspect = aspect
        self.rng = random.Random(seed)
        self.max_seconds = max_seconds
        self.no_crit = no_crit
        self.verbose = verbose
        self.log = []

        self.time = 0.0
        self.members = list(allies_spec)          # 씬 party 순서로 정렬돼 들어온다
        self._all_allies = list(allies_spec)      # 전사자도 결과에 남기기 위한 원본 목록
        self.monsters = []
        self.events = []                          # (발동 시각, 콜백) — 코루틴 지연 대체
        self.phase_fighting = False
        self.party_x = 0.0
        self.path_y = 0.0                         # y의 절대값은 결과에 영향이 없다(상대 오프셋만 쓴다)
        self.cam_x = 0.0
        self.spawn_timer = cfg.enemy_spawn_interval   # BeginServerBattle: 곧 첫 스폰
        self.cleared = False
        self.defeated = False
        self.spawned = 0
        self.killed = 0
        self.first_engage_time = None
        self.spawn_idle = 0.0
        self.advance_wait = 0.0      # 첫 교전 이후 '전진(비교전)' 상태로 보낸 시간 = 다음 적을 기다린 시간
        self.hitstop_count = 0
        self.hitstop_boss_count = 0
        self.wave_clear_count = 0
        self._hitstop_ready_at = -1.0
        self.damage_dealt = 0
        self.spawn_distance = 0.0

        # 스폰 계획: stage_spawn 순서대로, 보스는 마지막
        self.queue = []
        for code, count in stage.spawns:
            self.queue.extend([code] * count)
        self.boss_code = stage.boss_monster_code
        if self.boss_code:
            self.queue.append(self.boss_code)
        self.total_monsters = len(self.queue)

        self.compute_formation()
        for a in self.members:
            a.x = self.party_x - a.row * cfg.row_spacing_x
            a.y = self.path_y + a.form_y
        self.update_camera()

    # ---- 대형 (ComputeFormation) ----
    def compute_formation(self):
        n = len(self.members)
        if n == 0:
            self.engage_range = 1.5
            self.party_speed = self.cfg.fallback_move_speed
            return
        keys = [int(math.floor(a.cfg.attack_range / max(0.01, self.cfg.range_group_size)))
                for a in self.members]
        distinct = sorted(set(keys))
        row_total, row_slot = {}, {}
        for k in keys:
            ri = distinct.index(k)
            row_total[ri] = row_total.get(ri, 0) + 1
        for a, k in zip(self.members, keys):
            ri = distinct.index(k)
            a.row = ri
            slot = row_slot.get(ri, 0)
            row_slot[ri] = slot + 1
            a.form_y = (slot - (row_total[ri] - 1) * 0.5) * self.cfg.row_y_spacing
        self.engage_range = min(a.cfg.attack_range for a in self.members)
        self.party_speed = max(0.1, self.members[0].stats.move_speed)

    # ---- 조회 헬퍼 ----
    def alive_monsters(self):
        return [m for m in self.monsters if m.alive]

    def front_monster(self):
        alive = self.alive_monsters()
        return min(alive, key=lambda m: m.x) if alive else None

    def front_ally(self):
        alive = [a for a in self.members if not a.dead]
        return max(alive, key=lambda a: a.x) if alive else None

    def front_x(self):
        f = self.party_x
        for a in self.members:
            if not a.dead and a.x > f:
                f = a.x
        return f

    def is_fighting(self):
        return self.phase_fighting and self.front_monster() is not None

    def say(self, msg):
        if self.verbose:
            self.log.append(f"{self.time:7.2f}s  {msg}")

    def schedule(self, delay, fn):
        self.events.append((self.time + max(0.0, delay), fn))

    # ---- 연출이 먹는 실시간(게임 시간이 아니라 벽시계 시간) ----
    def request_hitstop(self, is_boss):
        if self.time < self._hitstop_ready_at:
            return
        self._hitstop_ready_at = self.time + K("hitstop_cooldown")
        if is_boss:
            self.hitstop_boss_count += 1
        else:
            self.hitstop_count += 1

    # ---- 데미지 (BattleDevController.DoDamageAfter / DoAreaDamageAfter) ----
    @staticmethod
    def mitigated(raw, defense):
        if raw <= 0:
            return 0
        k = K("defense_mitigation_k")
        return max(1, _round_half_even(raw * (k / (k + max(0, defense)))))

    def deal_single(self, attacker, target, dmg, crit, big_hit, label):
        if target is None or not target.alive:
            return
        killed = self.apply_damage(target, dmg)
        attacker.damage_dealt += dmg
        self.damage_dealt += dmg
        if crit or big_hit or target.is_boss:
            self.request_hitstop(target.is_boss)
        self.say(f"{label} → {target.name} -{dmg}{' (치명)' if crit else ''} "
                 f"(HP {max(0, target.hp)}/{target.max_hp})")
        self.lifesteal(attacker, dmg)
        if killed:
            self.on_monster_killed(target)

    def deal_area(self, attacker, center, radius, dmg, crit, big_hit, label, sk=None):
        hit, killed_list = 0, []
        r2 = radius * radius
        for m in list(self.alive_monsters()):
            dx = m.x - center[0]
            dy = self.path_y - center[1]
            if dx * dx + dy * dy <= r2:
                killed = self.apply_damage(m, dmg)
                attacker.damage_dealt += dmg
                self.damage_dealt += dmg
                hit += 1
                if killed:
                    killed_list.append(m)
        if hit:
            if crit or big_hit:
                self.request_hitstop(any(m.is_boss for m in killed_list))
            self.say(f"{label} (광역 r{radius:.1f}) → {hit}체 -{dmg}{' (치명)' if crit else ''}")
            self.lifesteal(attacker, dmg)
        else:
            self.say(f"{label} (광역 r{radius:.1f}) → 헛침(반경 안에 적 없음)")
        if sk is not None:
            if hit:
                sk.hits += 1
            else:
                sk.whiffs += 1
        for m in killed_list:
            self.on_monster_killed(m)

    def apply_damage(self, monster, dmg):
        """MonsterUnit.TakeDamage — 죽었으면 True."""
        if not monster.alive:
            return False
        monster.hp -= dmg
        if monster.hp <= 0:
            monster.hp = 0
            monster.alive = False
            return True
        # 보스 페이즈(70%·30%) — 공격 주기 단축
        thresholds = K("boss_phase_thresholds")
        if monster.is_boss and monster.boss_phase < len(thresholds):
            if monster.hp / monster.max_hp <= thresholds[monster.boss_phase]:
                monster.boss_phase += 1
                monster.attack_interval *= K("boss_phase_interval_factor")
                self.say(f"{monster.name} 페이즈 {monster.boss_phase} — 공격 주기 "
                         f"{monster.attack_interval:.2f}s")
        return False

    def lifesteal(self, ally, dmg):
        if ally.lifesteal_timer <= 0 or ally.lifesteal_ratio <= 0 or dmg <= 0:
            return
        ally.heal(max(1, int(dmg * ally.lifesteal_ratio)))

    def on_monster_killed(self, monster):
        self.killed += 1
        self.say(f"{monster.name} 처치 ({self.killed}/{self.total_monsters})")
        if not self.alive_monsters():
            self.wave_clear_count += 1

    def knockback(self, monster, distance):
        if distance <= 0 or not monster.alive:
            return
        dist = distance * (K("boss_knockback_factor") if monster.is_boss else 1.0)
        limit = max(K("knockback_limit"), dist + 0.6)
        offset = monster.x - monster.target_x + monster.knockback_remaining
        room = limit - offset
        if room <= 0:
            return
        monster.knockback_remaining += min(dist, room)
        monster.knockback_speed = max(K("knockback_speed"),
                                      monster.knockback_remaining / K("knockback_max_seconds"))
        monster.returning = True

    # ---- 웨이브 (TickServerWave) ----
    def tick_wave(self):
        if self.queue and self.spawn_timer >= max(0.1, self.cfg.enemy_spawn_interval) \
                and len(self.alive_monsters()) < max(1, self.cfg.max_concurrent_enemies):
            self.spawn_timer = 0.0
            self.spawn_monster(self.queue.pop(0))
            self.spawned += 1
        # UpdateQueue: 살아있는 적 전부를 파티 앞 라인으로
        line = self.front_x() + self.cfg.enemy_front_stop_gap
        for m in self.alive_monsters():
            m.target_x = line
        if not self.cleared and self.spawned > 0 and not self.queue and not self.alive_monsters():
            self.cleared = True

    def spawn_monster(self, code):
        name, hp, atk = self.md.monsters.get(code, (f"Monster({code})", 100, 5))
        is_boss = self.boss_code != 0 and code == self.boss_code
        speed = self.cfg.enemy_move_speed * (max(0.05, self.cfg.boss_move_speed_factor) if is_boss else 1.0)
        right_edge = self.cam_x + self.cfg.ortho_size * self.aspect
        x = right_edge + self.cfg.enemy_offscreen_margin
        self.spawn_distance = max(self.spawn_distance, x - self.front_x())
        self.monsters.append(Monster(code, name, hp, atk, speed, is_boss,
                                     self.cfg.enemy_attack_interval, x))
        self.say(f"스폰 {name}{' [보스]' if is_boss else ''} x={x:.2f} "
                 f"(hp {hp} atk {atk})")

    # ---- 전진/교전 (UpdatePhase) ----
    def update_phase(self):
        front = self.front_monster()
        if front is not None:
            dist = front.x - self.party_x
            if self.phase_fighting:
                if dist > self.engage_range + K("engage_hysteresis"):
                    self.phase_fighting = False
            elif dist <= self.engage_range:
                self.phase_fighting = True
                self.party_x = front.x - self.engage_range
                if self.first_engage_time is None:
                    self.first_engage_time = self.time
                    self.say(f"교전 시작 (이동 {self.time:.2f}s)")
        if not self.phase_fighting:
            self.party_x += self.party_speed * self.dt

    def update_camera(self):
        alive = [a for a in self.members if not a.dead]
        if not alive:
            return
        front = max(a.x for a in alive)
        rear = min(a.x for a in alive)
        half_w = self.cfg.ortho_size * self.aspect
        self.cam_x = min(front + self.cfg.cam_offset_x,
                         rear - self.cfg.cam_rear_margin + half_w)

    # ---- 아군 (PlayerCombatant.Update) ----
    def update_ally(self, a):
        dt = self.dt
        if a.dead:
            return
        for sk in a.skills:
            if sk.timer < sk.cooldown:
                sk.timer += dt
        if a.buff_timer > 0:
            a.buff_timer -= dt
            if a.buff_timer <= 0:
                a.atk_buff = 1.0
                a.cooldown_buff = 1.0
        if a.lifesteal_timer > 0:
            a.lifesteal_timer -= dt
            if a.lifesteal_timer <= 0:
                a.lifesteal_ratio = 0.0
        if a.motion_timer > 0:
            a.motion_timer -= dt
        if a.busy > 0:
            a.busy -= dt

        if a.charging:
            self.update_charge(a)
            return

        attacking = a.motion_timer > 0
        if a.busy <= 0 and not attacking and self.try_start_charge(a):
            return

        if self.is_fighting():
            front = self.front_monster()
            ready = front is not None
            if ready:
                a.attack_timer += dt
            dist = (front.x - a.x) if ready else float("inf")
            in_range = ready and 0.0 < dist <= a.cfg.attack_range
            if a.busy <= 0 and not attacking:
                if a.cfg.all_skills_aoe:
                    # 마법사: 대상 유무·거리와 무관하게 준비된 스킬을 시전
                    if not self.try_cast(a) and in_range and a.attack_timer >= a.attack_cooldown:
                        a.attack_timer = 0.0
                        self.basic_attack(a)
                elif in_range:
                    if not self.try_cast(a) and a.attack_timer >= a.attack_cooldown:
                        a.attack_timer = 0.0
                        self.basic_attack(a)
        self.update_movement(a)

    def update_movement(self, a):
        if a.busy > 0 or a.motion_timer > 0:
            return
        tx, ty = a.target
        gap = math.hypot(tx - a.x, ty - a.y)
        if gap > K("move_epsilon"):
            spd = max(0.5, a.stats.move_speed) * (1.0 + gap * K("move_catchup_gain"))
            step = spd * self.dt
            if step >= gap:
                a.x, a.y = tx, ty
            else:
                a.x += (tx - a.x) / gap * step
                a.y += (ty - a.y) / gap * step
            a.still_time = 0.0
        else:
            a.still_time += self.dt

    def roll_damage(self, a, coef):
        """PlayerCombatant.Damage — 실게임(serverMode)이라 개발 배수는 1이다."""
        crit = (not self.no_crit) and a.stats.crit_chance > 0 and self.rng.random() < a.stats.crit_chance
        dmg = a.stats.atk * coef * a.atk_buff
        if crit:
            dmg *= a.stats.crit_damage
        return max(1, int(dmg)), crit

    def basic_attack(self, a):
        a.basic_attacks += 1
        dmg, crit = self.roll_damage(a, 1.0)
        delay = self.cfg.basic_attack_hit_delay
        a.motion_timer = a.attack_motion
        target = self.front_monster()
        if a.cfg.has_basic_projectile or (a.cfg.ranged and not a.cfg.basic_attack_aoe):
            # 투사체(마법사 볼트·레인저 화살): 대상까지 비행한 뒤 명중
            if target is not None:
                flight = max(0.0, (target.x - a.x)) / K("arrow_speed")
                self.schedule(flight, lambda t=target: self.deal_single(a, t, dmg, crit, False,
                                                                       f"[{a.name}] 투사체"))
            a.busy = delay
        elif a.cfg.basic_attack_aoe:
            # 기사: 대상 위치를 중심으로 사거리 안 모든 적(= 앞 라인에 겹쳐 있는 무리 전체)
            center = (target.x if target else a.x + max(1.0, a.cfg.attack_range * 0.5),
                      self.path_y if target else a.y + self.cfg.effect_y_offset)
            self.schedule(delay, lambda c=center: self.deal_area(a, c, a.cfg.attack_range, dmg, crit,
                                                                 False, f"[{a.name}] 광역 평타"))
            a.busy = delay
        else:
            self.schedule(delay, lambda t=target: self.deal_single(a, t, dmg, crit, False,
                                                                  f"[{a.name}] 평타"))
            a.busy = delay

    def try_cast(self, a):
        for sk in a.skills:
            if sk.code == a.cfg.charge_skill_code:
                continue
            if sk.timer >= sk.cooldown:
                self.cast_skill(a, sk)
                sk.timer = 0.0
                return True
        return False

    def cast_skill(self, a, sk):
        sk.casts += 1
        if sk.coef_type == 2:      # 자기 버프
            if sk.stat_type == 1:
                a.atk_buff = max(1.0, sk.coef)
            elif sk.stat_type == 7:
                a.cooldown_buff = min(1.0, max(0.1, sk.coef))
            a.buff_timer = sk.duration if sk.duration > 0 else K("buff_default_duration")
            if sk.hp_cost_ratio > 0:
                cost = int(a.hp * sk.hp_cost_ratio)
                if cost > 0:
                    a.hp = max(1, a.hp - cost)
            if sk.lifesteal_ratio > 0:
                a.lifesteal_ratio = sk.lifesteal_ratio
                a.lifesteal_timer = sk.lifesteal_duration if sk.lifesteal_duration > 0 \
                    else max(0.1, sk.duration)
            a.busy = self.cfg.basic_attack_hit_delay
            self.say(f"[{a.name}] 버프 {sk.name} (×{sk.coef:.2f}, {a.buff_timer:.1f}s)")
            return

        dmg, crit = self.roll_damage(a, sk.coef)
        motion = sk.motion if sk.motion > 0 else self.cfg.basic_attack_hit_delay
        hit_delay = motion * (sk.hit_time_ratio if sk.hit_time_ratio > 0 else 1.0)
        label = f"[{a.name}] 스킬 {sk.name} ×{sk.coef:.2f}"
        target = self.front_monster()

        if a.cfg.rain_skill_code and sk.code == a.cfg.rain_skill_code:
            self.schedule(hit_delay, lambda t=target: self.deal_single(a, t, dmg, crit, True, label))
            if target is not None:
                self.schedule(hit_delay, lambda t=target: self.knockback(t, K("skill_knockback")))
            a.busy = motion + K("arrow_rain_extra_busy")
        elif a.cfg.slam_skill_code and sk.code == a.cfg.slam_skill_code:
            # 내려찍기: 공중 체류 후 착지 시점에 타격
            air = a.cfg.slam_air_time
            if sk.is_aoe:
                center = (a.x + a.cfg.self_effect_x_offset + sk_offset_x(sk),
                          a.y + self.cfg.effect_y_offset + sk_offset_y(sk))
                self.schedule(air, lambda c=center: self.deal_area(a, c, sk.radius, dmg, crit, True, label, sk))
            else:
                self.schedule(air, lambda t=target: self.deal_single(a, t, dmg, crit, True, label))
            a.busy = air + motion
        elif a.cfg.all_skills_aoe:
            # 마법사: 이펙트를 자기 위치에 띄우고 그 반경 안의 적을 때린다(대상이 없어도 시전)
            center = (a.x + a.cfg.self_effect_x_offset + sk_offset_x(sk),
                      a.y + self.cfg.effect_y_offset + sk_offset_y(sk))
            self.schedule(hit_delay, lambda c=center: self.deal_area(a, c, sk.radius, dmg, crit, True, label, sk))
            a.busy = motion
        elif a.cfg.ranged and target is not None:
            flight = max(0.0, target.x - a.x) / K("arrow_speed")
            self.schedule(flight, lambda t=target: self.deal_single(a, t, dmg, crit, True, label))
            self.schedule(flight, lambda t=target: self.knockback(t, K("skill_knockback")))
            a.busy = motion
        else:
            if sk.is_aoe:
                center = (a.x + a.cfg.self_effect_x_offset + sk_offset_x(sk),
                          a.y + self.cfg.effect_y_offset + sk_offset_y(sk))
                self.schedule(hit_delay, lambda c=center: self.deal_area(a, c, sk.radius, dmg, crit, True, label, sk))
            else:
                self.schedule(hit_delay, lambda t=target: self.deal_single(a, t, dmg, crit, True, label))
                if target is not None:
                    self.schedule(hit_delay, lambda t=target: self.knockback(t, K("skill_knockback")))
            a.busy = motion
            a.motion_timer = a.attack_motion if not a.cfg.cast_hold else motion

    # ---- 돌진 (TryStartCharge / UpdateCharge) ----
    def try_start_charge(self, a):
        sk = a.charge_skill
        if sk is None or sk.timer < sk.cooldown:
            return False
        front = self.front_monster()
        if front is None:
            return False
        dist = front.x - a.x
        if dist <= 0 or dist > a.cfg.charge_range:
            return False
        a.charging = True
        sk.timer = 0.0
        sk.casts += 1
        a.charge_impacted = False
        a.charge_elapsed = 0.0
        a.charge_motion = max(K("min_charge_motion"), sk.motion)
        self.say(f"[{a.name}] 돌진 {sk.name} 개시 (거리 {dist:.2f})")
        return True

    def update_charge(self, a):
        a.charge_elapsed += self.dt
        sk = a.charge_skill
        if not a.charge_impacted:
            front = self.front_monster()
            if front is None:
                a.charging = False
                return
            dist = front.x - a.x
            if dist > a.cfg.attack_range:
                speed = max(a.cfg.charge_speed, a.stats.move_speed * K("charge_speed_move_factor"))
                a.x += speed * self.dt
                a.y = self.path_y
                return
            a.charge_impacted = True
            dmg, crit = self.roll_damage(a, sk.coef)
            impact_delay = max(0.0, a.charge_motion - a.charge_elapsed)
            self.schedule(impact_delay, lambda t=front: self.deal_single(
                a, t, dmg, crit, True, f"[{a.name}] 돌진 {sk.name} ×{sk.coef:.2f}"))
            self.schedule(impact_delay, lambda t=front: self.knockback(t, K("charge_knockback")))
        if a.charge_elapsed >= a.charge_motion:
            a.charging = False
            # RequestFighting: 돌진 도달 → 교전 진입
            front = self.front_monster()
            if front is not None and not self.phase_fighting:
                self.phase_fighting = True
                self.party_x = front.x - self.engage_range
                if self.first_engage_time is None:
                    self.first_engage_time = self.time

    # ---- 몬스터 (MonsterUnit.Update) ----
    def update_monster(self, m):
        if not m.alive:
            return
        if m.engaged:
            m.attack_timer += self.dt
        # 넉백 중에는 전진하지 않는다
        if m.knockback_remaining > 0:
            step = min(m.knockback_remaining, m.knockback_speed * self.dt)
            m.knockback_remaining -= step
            m.x += step
            return
        if m.telegraph_timer > 0:
            self.tick_monster_attack(m)
            return
        if m.x - m.target_x > K("monster_stop_epsilon"):
            speed = m.move_speed * (K("knockback_return_speed") if m.returning else 1.0)
            m.x = max(m.target_x, m.x - speed * self.dt)
        else:
            m.returning = False
            m.engaged = True
            self.tick_monster_attack(m)

    def tick_monster_attack(self, m):
        if m.attack_interval <= 0:
            return
        if m.telegraph_timer > 0:
            m.telegraph_timer -= self.dt
            if m.telegraph_timer <= 0:
                self.monster_strike(m)
            return
        if m.attack_timer >= m.attack_interval:
            m.attack_timer = 0.0
            if m.is_boss:
                m.telegraph_timer = K("boss_telegraph_seconds")
            else:
                self.monster_strike(m)

    def monster_strike(self, m):
        """OnMonsterAttack — 최전방 생존 아군 1명에게 공격력×배수를 방어력으로 경감해 적용."""
        target = self.front_ally()
        if target is None:
            return
        raw = int(m.atk * max(1.0, self.cfg.enemy_damage_multiplier))
        dmg = self.mitigated(raw, target.stats.dfn)
        m.attacks += 1
        target.take_damage(dmg)
        self.say(f"{m.name} → {target.name} -{dmg} (HP {target.hp}/{target.stats.max_hp})")
        if target.dead:
            self.say(f"{target.name} 전사")
            self.on_ally_killed(target)

    def on_ally_killed(self, a):
        self.members = [m for m in self.members if m is not a]
        if self.members:
            self.compute_formation()
        else:
            self.defeated = True

    # ---- 메인 루프 ----
    def run(self):
        while self.time < self.max_seconds and not self.cleared and not self.defeated:
            # 코루틴(지연 데미지) 처리
            if self.events:
                due = [e for e in self.events if e[0] <= self.time]
                if due:
                    self.events = [e for e in self.events if e[0] > self.time]
                    for _t, fn in sorted(due, key=lambda e: e[0]):
                        fn()
                    if self.cleared or self.defeated:
                        break

            no_enemy = not self.alive_monsters()
            if no_enemy and self.spawned > 0 and self.queue:
                self.spawn_idle += self.dt

            self.spawn_timer += self.dt
            self.tick_wave()
            if self.cleared:
                break
            self.update_phase()
            for a in self.members:
                a.target = (self.party_x - a.row * self.cfg.row_spacing_x, self.path_y + a.form_y)
            for a in list(self.members):
                self.update_ally(a)
            for m in list(self.monsters):
                self.update_monster(m)
            self.update_camera()
            if self.first_engage_time is not None and not self.phase_fighting:
                self.advance_wait += self.dt
            self.time += self.dt

        return self.result()

    def result(self):
        r = BattleResult()
        r.cleared = self.cleared
        r.defeated = self.defeated
        r.timeout = not self.cleared and not self.defeated
        r.battle_seconds = self.time
        r.engage_seconds = self.first_engage_time if self.first_engage_time is not None else 0.0
        r.total_monsters = self.total_monsters
        r.killed = self.killed
        r.spawn_idle_seconds = self.spawn_idle
        r.advance_wait_seconds = self.advance_wait
        r.theory_dps = sum(a.stats.atk / max(0.05, a.stats.cooldown) for a in self._all_allies)
        r.damage_dealt = self.damage_dealt
        r.spawn_distance = self.spawn_distance
        r.party_dps = self.damage_dealt / self.time if self.time > 0 else 0.0
        r.hitstops = self.hitstop_count + self.hitstop_boss_count
        # 벽시계 추정: 히트스톱·웨이브 클리어 슬로우가 실시간을 먹는다(게임 시간은 그만큼 덜 흐른다).
        #   HitStopRoutine은 `WaitForSecondsRealtime(seconds)`로 기다린다 — 즉 seconds는 **실시간**이고
        #   그 동안 게임 시간은 seconds × scale 만큼만 흐른다. 따라서 추가되는 실시간은
        #   seconds × (1 − scale)이다(게임 시간 환산인 seconds × (1/scale − 1)이 아니다).
        hs = (self.hitstop_count * K("hitstop_seconds") + self.hitstop_boss_count * K("hitstop_boss_seconds")) \
            * (1.0 - K("hitstop_scale"))
        ws = self.wave_clear_count * K("wave_clear_slow_seconds") * (1.0 - K("wave_clear_slow_scale"))
        r.wallclock_seconds = self.time + hs + ws

        casts = {}
        for a in self._all_allies:
            r.per_ally.append({
                "name": a.name, "level": a.stats.level, "dead": a.dead,
                "hp": a.hp, "max_hp": a.stats.max_hp,
                "atk": a.stats.atk, "def": a.stats.dfn,
                "min_hp_ratio": a.min_hp_ratio,
                "damage_dealt": a.damage_dealt, "damage_taken": a.damage_taken,
                "basic_attacks": a.basic_attacks,
                "dps": a.damage_dealt / self.time if self.time > 0 else 0.0,
            })
            for sk in a.skills:
                if sk.casts:
                    casts[f"{a.name}/{sk.name}"] = sk.casts
                if sk.whiffs:
                    r.whiffs[f"{a.name}/{sk.name}"] = (sk.whiffs, sk.hits, round(sk.radius, 2))
        r.skill_casts = casts
        r.damage_taken = sum(p["damage_taken"] for p in r.per_ally)
        r.min_hp_ratio = min((p["min_hp_ratio"] for p in r.per_ally), default=1.0)
        r.dead_allies = sum(1 for p in r.per_ally if p["dead"])
        r.log = self.log
        return r


def sk_offset_x(sk):
    return getattr(sk, "offset_x", 0.0)


def sk_offset_y(sk):
    return getattr(sk, "offset_y", 0.0)


# =============================================================================
# 7. 파티 조립 — 씬 party 설정 + 마스터 스탯 + 스킬(장착)
# =============================================================================
def build_party(md, cfg, visuals, class_codes, *, level, gear, enhance, skill_level,
                skills_per_class, passive_level, rune_level, skill_codes=None):
    # 캐릭터 슬롯은 3개이고 직업 중복이 없다(세이브 데이터 기획서 5.5)
    if len(set(class_codes)) != len(class_codes):
        raise SystemExit(f"[ERROR] --party에 같은 직업이 중복됐습니다: {class_codes}")
    if len(class_codes) > 3:
        raise SystemExit(f"[ERROR] 파티는 최대 3인입니다(편성 자리 1~3): {class_codes}")
    by_class = {m.class_code: m for m in cfg.party}
    missing = [c for c in class_codes if c not in by_class]
    if missing:
        raise SystemExit(f"[ERROR] 씬 party에 없는 직업 코드: {missing} "
                         f"(있는 코드: {sorted(by_class)})")
    # _members 순서 = 씬 party 리스트 순서(파티 이동속도는 members[0]이 정한다)
    ordered = sorted(class_codes, key=lambda c: by_class[c].order)

    allies = []
    for code in ordered:
        mcfg = by_class[code]
        equipment = pick_equipment(md, code, level, gear)
        stats = build_ally_stats(md, code, level, equipment, enhance, passive_level, rune_level)

        actives = md.actives_of(code)
        if skill_codes:
            chosen = [s for s in actives if s.code in skill_codes]
        else:
            chosen = actives[:max(0, skills_per_class)]
        sim_skills = []
        # 초기 쿨다운 오프셋(BuildSkills): 첫 스킬은 2초 뒤, 다음은 4·6·8초 뒤 준비
        init_ready = [2.0, 4.0, 6.0, 8.0]
        for idx, s in enumerate(chosen):
            lv = min(max(skill_level, 1), max(1, s.max_level))
            coef, dur, ct = 0.0, 0.0, 1
            hp_cost, steal, steal_dur = 0.0, 0.0, 0.0
            primary = False
            for (c_type, c_coef, c_dur) in s.coefs.get(lv, []):
                if c_type == 4:
                    hp_cost = c_coef
                elif c_type == 5:
                    steal, steal_dur = c_coef, c_dur
                elif not primary:
                    coef, dur, ct = c_coef, c_dur, c_type
                    primary = True
            cd = s.cooldown if s.cooldown > 0 else cfg.skill_cooldown_fallback
            visual = mcfg.skills.get(s.code)
            info = visuals.get(visual.effect_guid) if visual else None
            motion = info.duration if info else cfg.basic_attack_hit_delay
            radius = info.radius(visual.scale) if (info and visual) else 0.0
            if radius <= 0:
                radius = mcfg.attack_range
            sk = SimSkill(
                code=s.code, name=s.name, coef_type=ct, coef=coef, duration=dur,
                stat_type=s.stat_type, hp_cost_ratio=hp_cost, lifesteal_ratio=steal,
                lifesteal_duration=steal_dur, cooldown=cd,
                timer=max(0.0, cd - (init_ready[idx] if idx < len(init_ready) else 2.0)),
                motion=motion, hit_time_ratio=visual.hit_time_ratio if visual else 1.0,
                is_aoe=(s.code in mcfg.aoe_skill_codes) or mcfg.all_skills_aoe,
                radius=radius)
            sk.offset_x = (visual.offset_x if visual else 0.0)
            sk.offset_y = (visual.offset_y if visual else 0.0)
            sim_skills.append(sk)

        anim = (mcfg.attack_anim or "").lower()
        if "bow" in anim:
            motion_len = K("attack_motion_bow")
        elif "magic" in anim:
            motion_len = K("attack_motion_magic")
        else:
            motion_len = K("attack_motion_default")
        allies.append(Ally(stats, mcfg, sim_skills, motion_len))
    return allies


def run_stage(md, cfg, visuals, act, difficulty, stage, opts, seed=None):
    st = md.stages.get((act, difficulty, stage))
    if st is None:
        raise SystemExit(f"[ERROR] 스테이지 {act}-{difficulty}-{stage} 가 stage_master에 없습니다.")
    table = ACT_FLOOR if getattr(opts, "floor", False) else ACT_STANDARD
    std = table.get(act, table[5])
    level = standard_level(md, act, difficulty, stage) if opts.level == "auto" else int(opts.level)
    party = std["party"] if opts.party == "auto" else opts.party
    gear = std["gear"] if opts.gear == "auto" else opts.gear
    skills_per_class = std["skills"] if opts.skills_per_class == "auto" else int(opts.skills_per_class)
    enhance = std["enhance"] if opts.enhance == "auto" else int(opts.enhance)
    skill_level = std["skill"] if opts.skill_level == "auto" else int(opts.skill_level)
    passive_level = std["passive"] if opts.passive_level == "auto" else int(opts.passive_level)
    allies = build_party(md, cfg, visuals, party, level=level, gear=gear,
                         enhance=enhance, skill_level=skill_level,
                         skills_per_class=skills_per_class,
                         passive_level=passive_level, rune_level=opts.rune_level,
                         skill_codes=opts.skill_codes)
    battle = Battle(md, cfg, visuals, st, allies,
                    dt=opts.dt, aspect=opts.aspect, seed=seed if seed is not None else opts.seed,
                    max_seconds=opts.max_seconds, no_crit=opts.no_crit, verbose=opts.verbose)
    res = battle.run()
    res.ally_stats = [
        {"name": a.stats.name, "level": a.stats.level, "atk": a.stats.atk, "def": a.stats.dfn,
         "max_hp": a.stats.max_hp, "crit": a.stats.crit_chance, "cooldown": a.stats.cooldown,
         "move_speed": a.stats.move_speed, "range": a.cfg.attack_range,
         "equipped": a.stats.equipped,
         "skills": [(s.name, s.coef, s.cooldown, round(s.motion, 2), round(s.radius, 2)) for s in a.skills]}
        for a in allies]
    return st, res, level


_LEVEL_CACHE = {}


def standard_level(md, act, difficulty, stage):
    """그 스테이지에 도달한 계정의 기준 레벨.

    난이도1 → 난이도2 순으로 100 스테이지를 **한 번씩** 미는 최소 진행을 가정해,
    직전까지 받은 `stage_reward.reward_exp`를 누적하고 `level_master.required_exp`로 레벨을 센다.
    반복 파밍하면 이보다 높아지므로 이 값이 **가장 불리한 조건**이다(값 문서 §9.1).
    보상·레벨 곡선을 조정하면 기준 레벨도 자동으로 따라간다.
    """
    key = id(md)
    table = _LEVEL_CACHE.get(key)
    if table is None:
        table, total, level = {}, 0, 1
        for coord in sorted(md.stages):
            table[coord] = level
            total += md.stages[coord].reward_exp
            while level < md.max_level:
                need = md.levels.get(level, {}).get("required_exp", 0)
                if need <= 0 or total < need:
                    break
                total -= need
                level += 1
        _LEVEL_CACHE[key] = table
    return table.get((act, difficulty, stage), 1)


def repeat_stage(md, cfg, visuals, act, difficulty, stage, opts):
    """치명타 난수 영향을 보려면 여러 번 돌린다 → (대표 결과, 시간 목록)."""
    times, results = [], []
    for i in range(max(1, opts.repeat)):
        st, res, level = run_stage(md, cfg, visuals, act, difficulty, stage, opts,
                                   seed=opts.seed + i)
        results.append(res)
        times.append(res.battle_seconds)
    mid = sorted(range(len(times)), key=lambda i: times[i])[len(times) // 2]
    return st, results[mid], level, times


# =============================================================================
# 8. 출력
# =============================================================================
def fmt_stage(st):
    tag = "보스" if st.boss_monster_code else "일반"
    return f"{st.act}-{st.difficulty}-{st.stage}({tag})"


def verdict(st, res):
    """목표 시간(§9.1) ±20% 밴드로 판정한다."""
    if res.defeated:
        return "패배(전멸)"
    if res.timeout:
        return "미클리어(시간 초과)"
    tgt = target_sec(st.stage)
    if res.battle_seconds > tgt * (1.0 + TARGET_TOLERANCE):
        return "클리어(목표 초과)"
    if res.battle_seconds < tgt * (1.0 - TARGET_TOLERANCE):
        return "클리어(목표보다 빠름)"
    return "클리어(목표 내)"


def print_stage_report(md, cfg, st, res, level, times, opts):
    comp = ", ".join(f"{md.monsters.get(c, ('?',))[0]}×{n}" for c, n in st.spawns)
    if st.boss_monster_code:
        comp += f" + [보스]{md.monsters.get(st.boss_monster_code, ('?',))[0]}×1"
    print(f"■ 스테이지 {fmt_stage(st)}  —  {comp}  (총 {res.total_monsters}마리)")
    print(f"  파티: " + " / ".join(
        f"{a['name']} Lv{a['level']}(공{a['atk']} 방{a['def']} 체{a['max_hp']}"
        f"{' 치명%.0f%%' % (a['crit'] * 100) if a['crit'] else ''})" for a in res.ally_stats))
    gear_desc = " / ".join(
        f"{a['name']}: " + (", ".join(f"{n}+{e}" if e else n for (n, e) in a["equipped"]) or "없음")
        for a in res.ally_stats) if any(a["equipped"] for a in res.ally_stats) else "장비 없음"
    print(f"  장비: {gear_desc}")
    std = (ACT_FLOOR if getattr(opts, "floor", False) else ACT_STANDARD).get(st.act, {})
    print(f"  성장{'(하한 파티)' if getattr(opts, 'floor', False) else ''}: "
          f"강화 +{std.get('enhance', opts.enhance)}"
          f" · 스킬 Lv{std.get('skill', opts.skill_level)}"
          f"({len(res.ally_stats[0]['skills']) if res.ally_stats else 0}종 장착)"
          f" · 패시브 Lv{std.get('passive', opts.passive_level)} · 룬 Lv{opts.rune_level}"
          f"{' · 치명타 없음' if opts.no_crit else ''}")
    print()
    tgt = target_sec(st.stage)
    print(f"  결과: {verdict(st, res)} — 전투 {res.battle_seconds:.2f}초 "
          f"(목표 {tgt:.1f}초의 {res.battle_seconds / tgt * 100:.0f}%, "
          f"처치 {res.killed}/{res.total_monsters})")
    print(f"    · 첫 교전까지 이동: {res.engage_seconds:.2f}초 "
          f"(스폰 거리 {res.spawn_distance:.1f}유닛, 접근 속도 "
          f"{cfg.enemy_move_speed:.1f}+파티 이동 → 합류)")
    print(f"    · 교전 시간: {max(0.0, res.battle_seconds - res.engage_seconds):.2f}초"
          f"    · 그중 다음 적 기다린 시간: {res.advance_wait_seconds:.2f}초"
          f"    · 적 전멸 대기: {res.spawn_idle_seconds:.2f}초")
    print(f"    · 파티 실효 DPS: {res.party_dps:.1f} / 이론 DPS(평타 무중단) {res.theory_dps:.1f}"
          f" = 가동률 {res.party_dps / res.theory_dps * 100 if res.theory_dps else 0:.0f}%"
          f"  (총 피해 {res.damage_dealt:,})")
    print(f"    · 받은 피해 {res.damage_taken:,} · 최저 체력 비율 {res.min_hp_ratio * 100:.0f}%"
          f" · 전사 {res.dead_allies}명")
    per = " / ".join(f"{p['name']} {p['dps']:.0f}dps(체력 {p['min_hp_ratio'] * 100:.0f}%)"
                     for p in res.per_ally)
    print(f"    · 멤버별: {per}")
    if res.skill_casts:
        print("    · 스킬 발동: " + ", ".join(f"{k}×{v}" for k, v in res.skill_casts.items()))
    if res.whiffs:
        print("    · 광역 헛침(반경 밖): " + ", ".join(
            f"{k} {w}회 헛침/{h}회 명중(반경 {r})" for k, (w, h, r) in res.whiffs.items()))
    # 병목 진단 — 시간이 어디로 갔는지 한 줄로 말해 준다
    fight = max(1e-9, res.battle_seconds - res.engage_seconds)
    if res.advance_wait_seconds / fight > 0.3:
        print(f"    ▶ 병목: 다음 몬스터가 걸어오는 시간이 교전 시간의 "
              f"{res.advance_wait_seconds / fight * 100:.0f}% — 파티 화력이 아니라 "
              f"스폰 거리·적 이동속도({cfg.enemy_move_speed}u/s)·스폰 주기"
              f"({cfg.enemy_spawn_interval}s)가 클리어 시간을 정한다")
    elif res.party_dps / max(1e-9, res.theory_dps) < 0.6:
        print("    ▶ 병목: 스킬 모션·헛침으로 공격 가동률이 낮다(스킬 계수·모션 길이·광역 반경 확인)")
    else:
        print("    ▶ 병목: 몬스터 체력(파티 화력이 소요 시간을 지배한다)")
    print(f"    · 연출 포함 벽시계 추정: {res.wallclock_seconds:.2f}초 "
          f"(히트스톱 {res.hitstops}회 등)")
    if res.cleared:
        overhead = K("clear_overlay_seconds")
        cycle = res.wallclock_seconds + overhead
        assumed = K("assumed_clear_interval_sec")
        print(f"    · 실사이클 추정: {cycle:.1f}초 = 전투 {res.wallclock_seconds:.1f} + 클리어 연출 "
              f"{overhead:.1f} → 서버 가정 {assumed:.0f}초의 {cycle / assumed:.2f}배")
    else:
        print(f"    · 클리어하지 못했으므로 사이클은 산출하지 않는다"
              f"(패배 시 재입장까지 {K('defeat_overlay_seconds'):.1f}초 소요)")
    if len(times) > 1:
        print(f"    · {len(times)}회 반복: 중위 {sorted(times)[len(times) // 2]:.2f}초 "
              f"(최소 {min(times):.2f} / 최대 {max(times):.2f})")
    if opts.verbose:
        print("\n  --- 전투 로그 ---")
        for ln in res.log:
            print("   " + ln)


def print_table(rows, headers):
    widths = [max(len(str(h)), max((len(str(r[i])) for r in rows), default=0))
              for i, h in enumerate(headers)]
    print("  " + " | ".join(str(h).ljust(widths[i]) for i, h in enumerate(headers)))
    print("  " + "-+-".join("-" * w for w in widths))
    for r in rows:
        print("  " + " | ".join(str(c).ljust(widths[i]) for i, c in enumerate(r)))


# =============================================================================
# 9. 명령
# =============================================================================
def cmd_stage(md, cfg, visuals, opts):
    act, difficulty, stage = opts.coord
    st, res, level, times = repeat_stage(md, cfg, visuals, act, difficulty, stage, opts)
    if opts.json:
        print(json.dumps({"stage": [act, difficulty, stage], "level": level,
                          "cleared": res.cleared, "defeated": res.defeated,
                          "timeout": res.timeout,
                          "battle_seconds": round(res.battle_seconds, 3),
                          "engage_seconds": round(res.engage_seconds, 3),
                          "spawn_idle_seconds": round(res.spawn_idle_seconds, 3),
                          "wallclock_seconds": round(res.wallclock_seconds, 3),
                          "cycle_seconds": round(res.wallclock_seconds + K("clear_overlay_seconds"), 3),
                          "total_monsters": res.total_monsters, "killed": res.killed,
                          "party_dps": round(res.party_dps, 2),
                          "damage_taken": res.damage_taken,
                          "min_hp_ratio": round(res.min_hp_ratio, 3),
                          "dead_allies": res.dead_allies,
                          "repeat_times": [round(t, 3) for t in times],
                          "party": res.ally_stats}, ensure_ascii=False, indent=2))
        return 0
    print_stage_report(md, cfg, st, res, level, times, opts)
    return 0 if res.cleared else 1


def _summary_row(md, st, res, level):
    tgt = target_sec(st.stage)
    return [fmt_stage(st), level, len(res.ally_stats), res.total_monsters,
            f"{tgt:.1f}", f"{res.battle_seconds:.1f}",
            f"{res.battle_seconds / tgt * 100:.0f}%", f"{res.engage_seconds:.1f}",
            f"{res.party_dps:.0f}",
            f"{res.min_hp_ratio * 100:.0f}%", res.dead_allies, verdict(st, res)]


SUMMARY_HEADERS = ["스테이지", "Lv", "인원", "마리", "목표s", "전투s", "달성", "이동s",
                   "DPS", "최저체력", "전사", "판정"]


def cmd_act(md, cfg, visuals, opts):
    rows, data = [], []
    for difficulty in (opts.difficulties or [1]):
        for stage in range(1, 11):
            if (opts.act, difficulty, stage) not in md.stages:
                continue
            st, res, level, _t = repeat_stage(md, cfg, visuals, opts.act, difficulty, stage, opts)
            rows.append(_summary_row(md, st, res, level))
            data.append({"stage": [opts.act, difficulty, stage], "level": level,
                         "cleared": res.cleared, "battle_seconds": round(res.battle_seconds, 2),
                         "engage_seconds": round(res.engage_seconds, 2),
                         "min_hp_ratio": round(res.min_hp_ratio, 3),
                         "dead_allies": res.dead_allies, "verdict": verdict(st, res)})
    if opts.json:
        print(json.dumps(data, ensure_ascii=False, indent=2))
        return 0
    print(f"■ Act {opts.act} 스테이지별 결과 "
          f"(목표: 1스테이지 {TARGET_SEC[0]:.0f}초 → 9스테이지 {TARGET_SEC[8]:.0f}초 · "
          f"보스 {TARGET_SEC[9]:.0f}초, 허용 ±{TARGET_TOLERANCE * 100:.0f}%)")
    print_table(rows, SUMMARY_HEADERS)
    return 0


def cmd_all(md, cfg, visuals, opts):
    rows, data, fails = [], [], 0
    for (act, difficulty, stage) in sorted(md.stages):
        if opts.difficulties and difficulty not in opts.difficulties:
            continue
        st, res, level, _t = repeat_stage(md, cfg, visuals, act, difficulty, stage, opts)
        if not res.cleared:
            fails += 1
        if not opts.brief or not res.cleared or verdict(st, res) != "클리어(목표 내)":
            rows.append(_summary_row(md, st, res, level))
        data.append({"stage": [act, difficulty, stage], "level": level,
                     "cleared": res.cleared, "battle_seconds": round(res.battle_seconds, 2),
                     "verdict": verdict(st, res)})
    if opts.json:
        print(json.dumps(data, ensure_ascii=False, indent=2))
        return 0
    title = "목표를 벗어난 스테이지" if opts.brief else "전체 스테이지"
    print(f"■ {title} ({len(data)}개 실행, 클리어 실패 {fails}개)")
    if rows:
        print_table(rows, SUMMARY_HEADERS)
    else:
        print("  (모두 목표 범위 안)")
    return 0 if fails == 0 else 1


CLASS_NAMES = {1: "기사", 2: "레인저", 3: "마법사", 4: "슬레이어"}


def _health_tag(res):
    if res is None:
        return "-"
    if res.defeated:
        return "전멸"
    if res.dead_allies:
        return f"전사{res.dead_allies}"
    return f"{res.min_hp_ratio * 100:.0f}%"


def cmd_baseline(md, cfg, visuals, opts):
    """값 문서 §9의 기준표를 재현한다 — 기준 파티(투자)와 하한 파티(무투자)를 함께 대조."""
    print("■ 값 문서 §9 기준표 재현 — 기준 파티(투자) / 하한 파티(무투자)")
    print(f"   시간 목표: 1스테이지 {TARGET_SEC[0]:.0f}초 → 9스테이지 {TARGET_SEC[8]:.0f}초 · "
          f"보스 {TARGET_SEC[9]:.0f}초 (허용 ±{TARGET_TOLERANCE * 100:.0f}%)")
    print("   긴장도 목표: 기준 파티의 최저 체력이 지역이 오를수록 좁아진다(§9.3)")
    opts.level = opts.party = opts.gear = "auto"
    opts.skills_per_class = opts.enhance = opts.skill_level = opts.passive_level = "auto"

    def measure(act, stages, floor):
        opts.floor = floor
        out = {}
        for stage in stages:
            if (act, 1, stage) in md.stages:
                _st, res, lv, _t = repeat_stage(md, cfg, visuals, act, 1, stage, opts)
                out[stage] = (res, lv)
        return out

    rows, floor_rows = [], []
    for act in sorted(ACT_STANDARD):
        std = ACT_STANDARD[act]
        got = measure(act, (1, 9, 10), floor=False)
        if not got:
            continue

        def cell(stage, table=got):
            if stage not in table:
                return "-"
            res = table[stage][0]
            return f"{res.battle_seconds:.1f} ({res.battle_seconds / target_sec(stage) * 100:.0f}%)"

        rows.append([
            act, len(std["party"]),
            "·".join(CLASS_NAMES.get(c, str(c)) for c in std["party"]),
            f"{got[1][1]}→{got.get(9, got[1])[1]}", std["gear"],
            f"+{std['enhance']}", f"Lv{std['skill']}/Lv{std['passive']}",
            cell(1), cell(9), cell(10),
            _health_tag(got.get(9, (None,))[0]), _health_tag(got.get(10, (None,))[0]),
        ])

        flo = measure(act, range(1, 11), floor=True)
        broke = next((s for s in range(1, 11)
                      if s in flo and (flo[s][0].defeated or flo[s][0].dead_allies)), None)
        floor_rows.append([
            act, ACT_FLOOR[act]["gear"],
            " ".join(_health_tag(flo[s][0]) for s in range(1, 11) if s in flo),
            f"{broke}스테이지" if broke else "끝까지 클리어",
        ])
    opts.floor = False

    print_table(rows, ["Act", "인원", "기준 파티", "Lv(1→9스)", "장비", "강화", "스킬/패시브",
                       "1스테이지", "9스테이지", "보스", "9스 최저체력", "보스 최저체력"])
    print()
    print("■ 하한 파티 — 장비작·스킬작을 전혀 하지 않은 계정(한 등급 낮은 장비 · 강화0 · 스킬 Lv1)")
    print_table(floor_rows, ["Act", "장비", "스테이지 1~10 최저 체력", "무너지는 지점"])
    print()
    print("  ※ 시간 달성률이 80~120% 밖이면 그 지역의 몬스터 hp 또는 스폰 마리 수(§11-B)를 조정한다.")
    print("  ※ 최저 체력이 §9.3의 attack 판정 기준이다. 하한 파티가 무너지는 지점이")
    print("     '언제부터 장비작·스킬작이 필요한가'이며, 설계 의도는 2지역 중반이다.")
    return 0


def cmd_cycle(md, cfg, visuals, opts):
    """오프라인 보상의 60초 가정을 실측 사이클과 대조한다(기획서 §2.1 정량화)."""
    act, difficulty, stage = opts.coord
    st, res, level, times = repeat_stage(md, cfg, visuals, act, difficulty, stage, opts)
    overhead = K("clear_overlay_seconds")
    cycle = res.wallclock_seconds + overhead
    assumed = K("assumed_clear_interval_sec")
    div = assumed * K("offline_efficiency_divisor")
    gold_h, exp_h = st.reward_gold * 3600.0 / max(1e-9, cycle), st.reward_exp * 3600.0 / max(1e-9, cycle)
    off_gold_h, off_exp_h = st.reward_gold * 3600.0 / div, st.reward_exp * 3600.0 / div
    print(f"■ 실사이클 vs 오프라인 가정 — {fmt_stage(st)} (Lv{level})")
    print(f"  전투(게임 시간)   : {res.battle_seconds:.2f}초  "
          f"[이동 {res.engage_seconds:.2f} + 교전 {res.battle_seconds - res.engage_seconds:.2f}]")
    print(f"  전투(연출 포함)   : {res.wallclock_seconds:.2f}초  "
          f"(히트스톱 {res.hitstops}회 · 웨이브 슬로우 포함)")
    print(f"  클리어 연출·전환  : {overhead:.1f}초  (StageClearOverlay 자동 닫힘)")
    print(f"  실사이클          : {cycle:.1f}초  ← 서버 가정 {assumed:.0f}초의 "
          f"{cycle / assumed:.2f}배")
    print()
    print(f"  온라인 파밍 시간당 : 골드 {gold_h:,.0f} · 경험치 {exp_h:,.0f}")
    print(f"  오프라인 지급 시간당: 골드 {off_gold_h:,.0f} · 경험치 {off_exp_h:,.0f}"
          f"  (÷{assumed:.0f}초 ÷효율{K('offline_efficiency_divisor'):.0f})")
    ratio = off_gold_h / gold_h if gold_h else 0.0
    print(f"  오프라인/온라인 비 : {ratio * 100:.1f}%   "
          f"(기획 의도 50% 대비 {ratio / 0.5:.2f}배)")
    print()
    print(f"  → 가정을 실측으로 바꾸려면 AssumedClearIntervalSec ≈ {cycle:.0f}"
          f" (OfflineService.cs:25)")
    if opts.json:
        print(json.dumps({"stage": [act, difficulty, stage],
                          "battle_seconds": round(res.battle_seconds, 3),
                          "wallclock_seconds": round(res.wallclock_seconds, 3),
                          "cycle_seconds": round(cycle, 3),
                          "assumed_clear_interval_sec": assumed,
                          "offline_over_online": round(ratio, 4)},
                         ensure_ascii=False, indent=2))
    return 0


def cmd_constants(md, cfg, visuals, opts):
    print(f"■ 마스터 데이터: {os.path.relpath(md.schema_path, REPO_ROOT)}")
    print(f"   직업 {len(md.classes)} · 스킬 {len(md.skills)} · 몬스터 {len(md.monsters)} · "
          f"스테이지 {len(md.stages)} · 아이템 {len(md.items)} · 레벨 1~{md.max_level}")
    print()
    print(f"■ 전투 상수(씬에서 읽음): {cfg.source}")
    for key in ("enemy_spawn_interval", "max_concurrent_enemies", "enemy_offscreen_margin",
                "enemy_move_speed", "enemy_front_stop_gap", "enemy_attack_interval",
                "enemy_damage_multiplier", "boss_move_speed_factor", "cam_offset_x",
                "cam_rear_margin", "range_group_size", "row_spacing_x", "row_y_spacing",
                "basic_attack_hit_delay", "effect_y_offset", "skill_cooldown_fallback",
                "ortho_size"):
        print(f"   {key:26} {getattr(cfg, key)}")
    print()
    print("■ 파티 멤버 설정(씬 party)")
    rows = [[m.order, m.label, m.class_code, f"{m.attack_range:.1f}",
             "O" if m.ranged else "-", "O" if m.basic_attack_aoe else "-",
             "O" if m.all_skills_aoe else "-", m.charge_skill_code or "-",
             m.slam_skill_code or "-", m.rain_skill_code or "-",
             ",".join(str(c) for c in m.aoe_skill_codes) or "-"]
            for m in cfg.party]
    print_table(rows, ["#", "라벨", "직업", "사거리", "원거리", "평타광역", "전스킬광역",
                       "돌진", "내려찍기", "화살비", "광역스킬"])
    print()
    print("■ 스킬 이펙트(모션 길이·광역 반경) — 프리팹 프레임/fps·첫 프레임 PNG 크기에서 산출")
    rows = []
    for m in cfg.party:
        for code, sv in sorted(m.skills.items()):
            info = visuals.get(sv.effect_guid)
            sk = md.skills.get(code)
            rows.append([code, sk.name if sk else "?", m.label,
                         info.name if info else "(미해결)",
                         f"{info.frames}f/{info.fps:g}fps" if info else "-",
                         f"{info.duration:.2f}s" if info else "-",
                         f"{sv.scale:g}",
                         f"{info.radius(sv.scale):.2f}" if info else "-",
                         f"{sv.hit_time_ratio:g}"])
    print_table(rows, ["코드", "스킬", "직업", "이펙트", "프레임", "모션", "배율", "반경", "타격비율"])
    print()
    print("■ 코드 전사 상수(클라 코드를 고치면 이 파일도 고쳐야 한다)")
    for k, (v, src) in CODE_CONSTANTS.items():
        print(f"   {k:28} {str(v):>10}   ← {src}")
    if visuals.errors:
        print("\n  [경고] 이펙트 읽기 실패: " + "; ".join(visuals.errors))
    return 0


# =============================================================================
# 10. CLI
# =============================================================================
def parse_coord(text):
    parts = [p for p in re.split(r"[-,\s]+", text.strip()) if p]
    if len(parts) == 2:
        act, stage = parts
        difficulty = "1"
    elif len(parts) == 3:
        act, difficulty, stage = parts
    else:
        raise SystemExit("[ERROR] 스테이지 좌표는 'act-difficulty-stage'(예: 3-1-10) 또는 "
                         "'act-stage'(예: 3-10) 형식입니다.")
    return (int(act), int(difficulty), int(stage))


def build_parser():
    # 공통 옵션은 부모 파서에 둬서 `stage 1-1-1 --gear none`(뒤)와 `--gear none stage 1-1-1`(앞)이
    # 모두 동작하게 한다.
    p = argparse.ArgumentParser(add_help=False)
    p.add_argument("--schema", default=DEFAULT_SCHEMA, help="마스터 데이터 정본 SQL")
    p.add_argument("--scene", default=DEFAULT_SCENE, help="전투 상수를 읽을 씬(기본 GameScene)")
    p.add_argument("--party", default="auto",
                   help="파티 직업 코드(1 기사·2 레인저·3 마법사·4 슬레이어). "
                        "auto = 지역별 기준 파티(§9.1 — 1지역 1명 → 2지역 2명 → 3지역부터 3명)")
    p.add_argument("--level", default="auto",
                   help="캐릭터 레벨(정수 또는 auto). auto = 그 스테이지까지 한 번씩 밀었을 때의 기준 레벨")
    p.add_argument("--gear", default="auto",
                   help="auto | none | rough | weapon[:G] | full[:G]. auto = 지역별 기준 장비(§9.1)")
    p.add_argument("--enhance", default="auto", help="장비 강화 단계(0~10). auto = 지역별 기준 투자")
    p.add_argument("--skill-level", default="auto", help="액티브 스킬 레벨. auto = 지역별 기준 투자")
    p.add_argument("--skills-per-class", default="auto",
                   help="장착 액티브 수(growth §5.3 최대 2). auto = 1지역 1종 · 그 외 2종")
    p.add_argument("--skill-codes", default="", help="장착 스킬 코드 직접 지정(예: 101,103,301)")
    p.add_argument("--passive-level", default="auto", help="패시브 스킬 레벨. auto = 지역별 기준 투자")
    p.add_argument("--rune-level", type=int, default=0, help="룬 레벨(0=미투자)")
    p.add_argument("--no-crit", action="store_true", help="치명타 없음(난수 영향 제거)")
    p.add_argument("--dt", type=float, default=1.0 / 60.0, help="고정 스텝(초). 기본 1/60")
    p.add_argument("--aspect", type=float, default=16.0 / 9.0, help="화면 가로/세로 비(스폰 거리에 영향)")
    p.add_argument("--seed", type=int, default=20260811, help="난수 시드")
    p.add_argument("--repeat", type=int, default=1, help="반복 실행 횟수(치명타 분포 확인)")
    p.add_argument("--max-seconds", type=float, default=600.0, help="이 시간을 넘기면 미클리어 처리")
    p.add_argument("--difficulty", default="", help="act/all에서 볼 난이도(예: 1 또는 1,2)")
    p.add_argument("--brief", action="store_true", help="all에서 목표를 벗어난 스테이지만 출력")
    p.add_argument("--floor", action="store_true",
                   help="하한 파티(장비작·스킬작을 전혀 하지 않은 계정)로 돌린다 — 값 문서 §9.1")
    p.add_argument("--json", action="store_true", help="JSON 출력")
    p.add_argument("--verbose", action="store_true", help="전투 로그 출력")

    root = argparse.ArgumentParser(
        parents=[p], prog="balance_sim.py",
        description="밸런스 시뮬레이터 — 스테이지 클리어 여부·소요 시간 산출(오프라인)",
        formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = root.add_subparsers(dest="cmd", required=True)
    s = sub.add_parser("stage", parents=[p], help="한 스테이지 시뮬레이션")
    s.add_argument("coord", help="예: 1-1-1 / 3-10")
    s = sub.add_parser("act", parents=[p], help="한 Act의 10스테이지")
    s.add_argument("act", type=int)
    sub.add_parser("all", parents=[p], help="전체 스테이지")
    sub.add_parser("baseline", parents=[p], help="값.md §9 기준표 재현·대조")
    s = sub.add_parser("cycle", parents=[p], help="실사이클 vs 오프라인 60초 가정")
    s.add_argument("coord", help="예: 1-1-9")
    sub.add_parser("constants", parents=[p], help="읽어 온 상수·이펙트 확인")
    return root


def main(argv):
    opts = build_parser().parse_args(argv)
    if opts.party != "auto":
        opts.party = [int(c) for c in re.split(r"[,\s]+", opts.party) if c]
    opts.skill_codes = [int(c) for c in re.split(r"[,\s]+", opts.skill_codes) if c] or None
    opts.difficulties = [int(c) for c in re.split(r"[,\s]+", opts.difficulty) if c] or None
    if hasattr(opts, "coord"):
        opts.coord = parse_coord(opts.coord)
    if opts.dt <= 0:
        raise SystemExit("[ERROR] --dt는 0보다 커야 합니다.")

    if not os.path.isfile(opts.schema):
        raise SystemExit(f"[ERROR] 마스터 SQL을 찾을 수 없습니다: {opts.schema}")
    md = MasterData(opts.schema)
    cfg = load_battle_config(opts.scene)
    if not cfg.party:
        print(f"[경고] 씬에서 party 설정을 읽지 못했습니다({opts.scene}). 전투 상수는 내장 기본값을 씁니다.",
              file=sys.stderr)
    visuals = VisualLibrary()

    if opts.cmd == "stage":
        return cmd_stage(md, cfg, visuals, opts)
    if opts.cmd == "act":
        return cmd_act(md, cfg, visuals, opts)
    if opts.cmd == "all":
        return cmd_all(md, cfg, visuals, opts)
    if opts.cmd == "baseline":
        return cmd_baseline(md, cfg, visuals, opts)
    if opts.cmd == "cycle":
        return cmd_cycle(md, cfg, visuals, opts)
    if opts.cmd == "constants":
        return cmd_constants(md, cfg, visuals, opts)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
