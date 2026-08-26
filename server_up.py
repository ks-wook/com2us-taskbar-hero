#!/usr/bin/env python
"""도커 없이 서버 2개만 dotnet watch(핫 리로드)로 띄우는 부트스트랩 — 랭킹 캐시 적재까지.

이 스크립트가 전제하는 것 (server_up_with_docker.py 와 다른 점이 이것뿐이다)
    **MySQL·Redis가 이 PC에서 이미 돌고 있다**(스크립트가 만들어 주지 않는다). 컨테이너는 만들지 않고
    **AccountServer·GameServer 두 개만** 띄운다 — 의존 서비스는 응답만 확인하고, 없으면 거기서 멈춘다.
    도커를 쓰는 쪽은 `python server_up_with_docker.py`다(컨테이너 5~8개 + 서버까지 전부 컨테이너로).

사용법 (저장소 루트에서 실행한다 — 프로젝트 경로를 이 위치 기준으로 찾는다)
    python server_up.py                          # MySQL·Redis 접속 정보를 물어본 뒤(엔터=기본값)
                                                 # 선빌드 → 두 서버 watch 기동(각자 새 콘솔) → 랭킹 캐시 적재
    python server_up.py --mysql-user root --mysql-password pw
                                                 # 계정·비밀번호를 미리 줘서 묻지 않게 한다
    python server_up.py --mysql-port 33306 --redis 127.0.0.1:36379
                                                 # 컨테이너 MySQL·Redis에 붙이고 서버만 watch로 띄운다
    python server_up.py --no-prompt           # 묻지 않고 기본값으로 진행(스크립트 호출용)
    python server_up.py --use-appsettings     # 접속 정보를 손대지 않고 appsettings 값 그대로 쓴다
    python server_up.py --init-schema         # 묻지 않고 테이블을 새로 세팅한다(DROP 후 재생성)
    python server_up.py --no-init-schema      # 묻지 않고 테이블 세팅을 건너뛴다
    python server_up.py --warmup-only         # 이미 떠 있는 GameServer에 랭킹 캐시 적재만 지시
    python server_up.py --stop                # 이 방식으로 띄운 서버(watch 래퍼 + 앱)를 종료

기동 절차(스크립트가 순서대로 한다)
    ⓪ **접속 정보 입력** — MySQL 서버 주소·포트·계정·비밀번호, Redis 서버 주소·포트를 차례로 묻는다.
       엔터만 치면 기본값(localhost · 3306 · root / 127.0.0.1 · 6379)이 들어가고, **비밀번호만 기본값이
       없다**(그 PC의 계정을 스크립트가 알 수 없다. 입력값은 화면에 찍히지 않고, 엔터는 "비밀번호 없음").
       마지막으로 **테이블을 새로 세팅할지**(기본 y) 묻는다 — 로컬 MySQL에는 컨테이너의 init SQL 같은
       장치가 없어 처음 한 번은 사람이 넣어야 한다. y면 ②에서 docs/공통/db-schema.sql →
       docs/세부/master-data/master-data-schema.sql 을 그 순서로 실행한다. **두 SQL은 DROP TABLE 후
       재생성하는 파괴적 스크립트라 기존 세이브 데이터가 지워진다** — 이어서 개발하던 데이터가 있으면 n.
       옵션으로 준 값은 묻지 않고, 입력이 터미널이 아니면(파이프·CI) 아무것도 묻지 않는다 —
       프롬프트에서 멈추지 않게 하려는 것이다(그 경로에서 계정·비밀번호는 appsettings 값을 쓰고,
       테이블 세팅은 --init-schema를 명시하지 않으면 하지 않는다).
    ① 사전 점검 — dotnet SDK · 토큰 서명 키 · 이벤트 로그 디렉터리 · **MySQL·Redis 응답** ·
                   **서버 포트(5160·5247) 선점**
    ② 테이블 세팅(⓪에서 y였으면) → MySQL 스키마 확인. 계정·게임·마스터 DB 테이블 수를 센다.
       접속 자체가 안 되면(계정·비밀번호 불일치) 여기서 멈춘다 — 그대로 띄우면 서버는 뜨고
       모든 요청이 DB 오류로 죽는다. 테이블이 하나도 없으면 무엇을 실행하면 되는지 알려 주고 멈춘다.
    ③ 선(先) 빌드 — 솔루션을 직렬로 한 번 빌드한다(아래 「왜 선빌드가 필요한가」)
    ④ AccountServer(:5160) · GameServer(:5247)를 각각 `dotnet watch run`으로 **새 콘솔 창**에 띄운다
    ⑤ 두 서버 준비 대기(OpenAPI 문서 응답)
    ⑥ 보스러시 랭킹 캐시 적재(관리 API 1회 호출)

접속 정보를 어떻게 정하나 — 정본은 appsettings, 이 스크립트는 **호스트·포트만 갈아 끼운다**
    두 서버의 appsettings.json은 컨테이너 매핑(MySQL 33306 · Redis 36379)을 가리킨다. 로컬 설치본은
    보통 3306·6379라서 그대로는 붙지 못한다. 그래서 이 스크립트는 appsettings에서 접속 문자열을
    **읽어** Server·Port(그리고 준 경우 Uid·Pwd)만 바꾼 뒤 **환경 변수로 덮어써** 자식 프로세스에
    넘긴다(ASP.NET Core 설정 우선순위상 환경 변수가 파일을 이긴다):
        ConnectionStrings__AccountDb · ConnectionStrings__GameDb · ConnectionStrings__MasterDb
        Redis__ConnectionString
    DB 이름 같은 나머지 값은 appsettings에서 그대로 가져온다 — 스크립트에 접속 문자열을 통째로 박아
    두면 서버 설정을 고쳤을 때 둘이 조용히 어긋난다. **파일은 고치지 않는다**(git 변경이 남지 않는다).
    appsettings 값을 그대로 쓰고 싶으면 --use-appsettings 로 이 덮어쓰기를 끈다.

왜 선(先) 빌드가 필요한가
    두 서버는 TaskbarHero.Common(net10.0)을 ProjectReference로 참조하고, Common의 obj/bin은 Unity
    로컬 패키지 격리를 위해 artifacts/ 로 재배치되어 **두 서버가 같은 출력 폴더를 공유한다.** 두
    watch를 동시에 띄우면 두 MSBuild가 그 폴더에 Common을 동시 빌드하다 파일 잠금(MSB3713/CS2012)이
    난다. 먼저 직렬로 한 번 빌드해 Common을 최신으로 만들어 두면 이후 두 watch의 빌드는 no-op이 되어
    경합이 사라진다.

왜 랭킹 캐시 적재가 여기 있나
    보스러시 랭킹은 MySQL(boss_rush_record)이 정본이고 Redis Sorted Set은 조회용 파생 인덱스다.
    Redis가 비어 있으면 정본에서 다시 만들어야 하는데, 서버는 그 일을 스스로 하지 않는다 — 기동
    절차의 명시적인 한 단계로 관리 API를 한 번 호출한다(POST /api/admin/boss-rush/rank/warmup).
    적재 안 하고 넘어가면 랭킹 조회가 조용히 MySQL 폴백으로 돌아 캐시가 있는 것처럼 보인다.

주의
    · 서버는 **각자 새 콘솔 창**으로 뜬다(로그가 사용자에게 그대로 보인다). 이 스크립트는 창을 띄운
      뒤 종료하며, 서버를 멈추려면 그 창을 닫거나 `python server_up.py --stop` 을 쓴다.
    · 컨테이너 서버(`server_up_with_docker.py`)와 같은 5160·5247을 쓰므로 **동시에 띄울 수 없다.** 사전 점검이
      먼저 막는다:  docker compose stop accountserver gameserver
    · **스키마는 저절로 만들어지지 않는다.** 컨테이너는 빈 볼륨 첫 기동에 init SQL이 돌지만(compose가
      /docker-entrypoint-initdb.d 로 마운트한다) 로컬 네이티브 MySQL에는 그런 장치가 없다 — 그래서
      ⓪에서 테이블 세팅을 묻는다(docs/공통/db-schema.sql → docs/세부/master-data/master-data-schema.sql
      순서. 두 파일이 CREATE DATABASE부터 하므로 **빈 MySQL에서도 그대로 된다**).
      적용·확인은 `mysql` CLI가 PATH에 있으면 그것을, 없으면 pymysql을 쓴다(둘 다 없으면 무엇을
      실행하면 되는지 알려 준다).
      db-schema.sql이 **첫 보스러시 시즌 1행**도 심는다 — 그 행이 없으면 보스러시가 영구히 닫힌
      상태로 남으므로, 새 로컬 MySQL은 이 세팅을 반드시 한 번 거쳐야 한다.
    · **MySQL·Redis는 이 스크립트가 띄우지도, 끄지도 않는다**(--stop은 서버 2개만 끊는다). 이 PC에
      설치본을 켜 두거나 컨테이너로 띄워 두고(docker compose up -d mysql redis) 주소만 알려 준다.
      Redis를 저장소 바이너리로 쓰려면 Redis-8.8.0-.../start.bat 을 직접 실행한다.
    · Redis 인스턴스는 **하나만** 띄운다 — 서버가 보는 것은 Redis 주소 하나뿐이라, 컨테이너(호스트
      36379)와 로컬(6379)이 함께 떠 있으면 어느 쪽에 썼는지 헷갈린다.
    · 끝나면 **엔터를 누를 때까지 창을 닫지 않는다**(더블클릭 실행에서 실패 사유가 사라지지 않게).
      바로 닫으려면 --no-pause. 콘솔이 아닌 실행(파이프·CI)에서는 기다리지 않는다.
    · 마스터 데이터는 서버 기동 시 **1회만** 인메모리로 적재된다. 값을 고쳤으면 watch 재시작이
      아니라 서버를 다시 띄워야 반영된다(코드 변경은 watch가 알아서 다시 올린다).
"""

from __future__ import annotations

import argparse
import getpass
import json
import os
import re
import shutil
import socket
import subprocess
import sys
import time
import traceback
import urllib.error
import urllib.request
from pathlib import Path

# ── 저장소 구조 ─────────────────────────────────────────────────────────
#   이 스크립트는 저장소 루트에 있다(솔루션·프로젝트 경로를 이 자리 기준으로 찾는다).
ROOT = Path(__file__).resolve().parent
SOLUTION = ROOT / "com2us-taskbar-hero.slnx"

# ── 띄울 서버 ───────────────────────────────────────────────────────────
#   (프로젝트 폴더, 표시 이름, 호스트 포트). watch는 프로젝트 1개 대상이라 각자 콘솔을 하나 쓴다.
SERVERS = (
    ("AccountServer", "AccountServer", 5160),
    ("GameServer", "GameServer", 5247),
)
LAUNCH_PROFILE = "http"

# ── 로컬 의존 서비스 기본값 ─────────────────────────────────────────────
#   설치본의 기본 주소다(컨테이너는 충돌을 피해 호스트 쪽을 33306·36379로 옮겨 두었다).
#   기동할 때 이 값들을 기본값으로 **물어본다** — 엔터만 치면 그대로 쓴다.
DEFAULT_MYSQL_HOST = "localhost"
DEFAULT_MYSQL_PORT = 3306
DEFAULT_MYSQL_USER = "root"
DEFAULT_REDIS_HOST = "127.0.0.1"
DEFAULT_REDIS_PORT = 6379
DEFAULT_REDIS = f"{DEFAULT_REDIS_HOST}:{DEFAULT_REDIS_PORT}"

# 접속 문자열 정본. 파일에서 읽어 호스트·포트만 갈아 끼우고, 환경 변수 이름으로 자식에게 넘긴다.
#   (서버 폴더, appsettings의 키 경로, 덮어쓸 환경 변수 이름)
MYSQL_SETTINGS = (
    ("AccountServer", ("ConnectionStrings", "AccountDb"), "ConnectionStrings__AccountDb"),
    ("GameServer", ("ConnectionStrings", "GameDb"), "ConnectionStrings__GameDb"),
    ("GameServer", ("ConnectionStrings", "MasterDb"), "ConnectionStrings__MasterDb"),
)
REDIS_ENV = "Redis__ConnectionString"

# 스키마 확인 대상. init SQL이 만드는 DB 3개다(compose가 첫 기동에 마운트하는 것과 같은 목록).
REQUIRED_DATABASES = ("taskbar_hero_account", "taskbar_hero_game", "taskbar_hero_master")
# --init-schema가 적용할 SQL. 순서가 중요하다(스키마 → 마스터 데이터).
SCHEMA_FILES = (
    Path("docs") / "공통" / "db-schema.sql",
    Path("docs") / "세부" / "master-data" / "master-data-schema.sql",
)

DEFAULT_GAME_URL = "http://localhost:5247"
HEALTH_PATH = "/openapi/v1.json"
WARMUP_PATH = "/api/admin/boss-rush/rank/warmup"

# 서버가 돌려주는 워밍업 결과 상태값(GameServer/Constants.cs 의 RankWarmupStatus 와 같은 문자열).
WARMUP_CACHE_UNAVAILABLE = "cache-unavailable"
WARMUP_NO_SEASON = "no-season"

_COLORS = {
    "dim": "\033[90m",
    "ok": "\033[92m",
    "warn": "\033[93m",
    "err": "\033[91m",
    "info": "\033[96m",
}
_USE_COLOR = False


# ── 출력 ────────────────────────────────────────────────────────────────
def setup_console() -> None:
    """한글·기호가 깨지지 않게 콘솔 출력을 UTF-8로 맞춘다(server_up_with_docker.py가 하는 일과 같다)."""
    if os.name == "nt":
        try:
            import ctypes

            ctypes.windll.kernel32.SetConsoleOutputCP(65001)
        except Exception:
            pass

    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except Exception:
            pass


def say(message: str, tone: str | None = None) -> None:
    """진행 상황 한 줄. tone은 색만 바꾸며, 색을 못 쓰는 환경에서는 그냥 무시된다."""
    if _USE_COLOR and tone in _COLORS:
        print(_COLORS[tone] + message + "\033[0m", flush=True)
    else:
        print(message, flush=True)


# ── 설정 읽기 ───────────────────────────────────────────────────────────
def read_json_setting(path: Path, keys: tuple[str, ...]) -> str | None:
    """
    appsettings 계열 JSON에서 중첩 키 하나를 문자열로 읽는다. 없거나 못 읽으면 None.

    ASP.NET Core 설정 파서는 주석을 허용하므로 파싱 전에 `//` 줄을 걷어낸다. 스크립트가 설정을
    **읽기만** 하는 이유는 값의 정본을 한 곳(서버 설정)에 두기 위해서다 — 스크립트에 기본값을 박아
    두면 서버 설정을 바꿨을 때 둘이 조용히 어긋난다.
    """
    if not path.exists():
        return None

    try:
        text = re.sub(r"^\s*//.*$", "", path.read_text(encoding="utf-8-sig"), flags=re.MULTILINE)
        node = json.loads(text)
    except (OSError, ValueError):
        return None

    for key in keys:
        if not isinstance(node, dict):
            return None
        node = node.get(key)

    return node if isinstance(node, str) and node.strip() else None


def replace_conn_value(conn: str, key: str, value: str) -> str:
    """
    MySQL 접속 문자열에서 키 하나의 값만 바꾼다(그 키가 없으면 끝에 붙인다).

    문자열을 새로 조립하지 않고 **있는 값을 갈아 끼우는** 이유는 DB 이름처럼 스크립트가 알 필요 없는
    나머지 설정을 appsettings 정본에서 그대로 물려받기 위해서다.
    """
    pattern = re.compile(rf"(?i)(\b{re.escape(key)}\s*=\s*)([^;]*)")
    if pattern.search(conn):
        return pattern.sub(lambda m: m.group(1) + value, conn, count=1)

    separator = "" if conn.endswith(";") or not conn else ";"
    return f"{conn}{separator}{key}={value};"


def read_conn_value(conn: str, key: str) -> str | None:
    """MySQL 접속 문자열에서 키 하나의 값을 읽는다(스키마 확인용 계정을 정본에서 가져오는 데 쓴다)."""
    match = re.search(rf"(?i)\b{re.escape(key)}\s*=\s*([^;]*)", conn)
    return match.group(1).strip() if match else None


def build_mysql_conn(base: str, host: str, port: int, user: str | None, password: str | None) -> str:
    """appsettings의 접속 문자열에서 Server·Port(+ 준 경우 Uid·Pwd)만 로컬 값으로 바꾼다."""
    conn = replace_conn_value(base, "Server", host)
    conn = replace_conn_value(conn, "Port", str(port))
    if user is not None:
        conn = replace_conn_value(conn, "Uid", user)
    if password is not None:
        conn = replace_conn_value(conn, "Pwd", password)
    return conn


def resolve_child_env(args: argparse.Namespace) -> dict[str, dict[str, str]]:
    """
    서버별로 자식 프로세스에 넘길 환경 변수를 만든다(빈 dict면 appsettings 값을 그대로 쓴다).

    --use-appsettings면 아무것도 덮어쓰지 않는다. 그 외에는 MySQL 접속 문자열 3개와 Redis 주소를
    로컬 설치본 기준으로 바꿔 넣는다. 접속 문자열을 못 읽으면(설정 파일이 깨졌거나 키가 사라졌으면)
    조용히 기본값으로 진행하지 않고 실패시킨다 — 어긋난 주소로 뜨면 원인이 SQL 오류로만 드러난다.
    """
    env_by_server: dict[str, dict[str, str]] = {name: {} for name, _, _ in SERVERS}
    if args.use_appsettings:
        return env_by_server

    for server, keys, env_name in MYSQL_SETTINGS:
        base = read_json_setting(ROOT / server / "appsettings.json", keys)
        if not base:
            say(f"{server}/appsettings.json 에서 {':'.join(keys)} 를 읽지 못했습니다.", "err")
            raise SystemExit(1)
        env_by_server[server][env_name] = build_mysql_conn(
            base, args.mysql_host, args.mysql_port, args.mysql_user, args.mysql_password
        )

    for server in env_by_server:
        env_by_server[server][REDIS_ENV] = args.redis

    return env_by_server


# ── 대화형 입력 ─────────────────────────────────────────────────────────
def ask_text(question: str, default: str) -> str:
    """한 줄 물어본다. **빈 입력(엔터)은 기본값**이고, 입력이 끊기면(EOF·파이프) 기본값으로 진행한다."""
    try:
        answer = input(f"  {question} [{default}] ").strip()
    except EOFError:
        return default

    return answer or default


def stdin_is_console() -> bool:
    """
    표준 입력이 **진짜 콘솔**인지 — 물어봐도 되는 상황인지 판정한다.

    isatty()만 보면 안 된다: 윈도우에서 `< NUL`(Git Bash의 /dev/null)은 문자 장치라서 isatty()가
    True를 돌려준다. 그 상태로 비밀번호를 물으면 getpass가 **stdin이 아니라 콘솔을 직접** 읽어(윈도우
    구현이 msvcrt를 쓴다) EOF가 오지 않아 영원히 멈춘다. 그래서 콘솔 핸들인지를 직접 확인한다 —
    GetConsoleMode는 콘솔 핸들에만 성공한다.
    """
    if sys.stdin is None or not sys.stdin.isatty():
        return False

    if os.name != "nt":
        return True

    try:
        import ctypes

        handle = ctypes.windll.kernel32.GetStdHandle(-10)  # STD_INPUT_HANDLE
        mode = ctypes.c_uint()
        return bool(ctypes.windll.kernel32.GetConsoleMode(handle, ctypes.byref(mode)))
    except Exception:
        # 판정 수단이 없으면 묻지 않는 쪽으로 기운다 — 멈춘 프롬프트보다 기본값 진행이 낫다.
        return False


def ask_secret(question: str) -> str:
    """
    비밀번호를 물어본다 — **기본값이 없고, 입력값은 화면에 찍히지 않는다**(getpass).

    비밀번호에 기본값을 두지 않는 이유는 그 PC의 MySQL 계정을 스크립트가 알 수 없기 때문이고,
    입력을 가리는 이유는 콘솔·스크롤백에 비밀번호가 남지 않게 하려는 것이다(로깅 규칙과 같은 이유).
    비밀번호 없이 쓰는 로컬 설치본도 있으므로 **엔터(빈 입력)는 "비밀번호 없음"으로 받는다.**
    """
    try:
        return getpass.getpass(f"  {question}: ")
    except (EOFError, getpass.GetPassWarning):
        return ""


def ask_yes_no(question: str, default: bool) -> bool:
    """y/n을 묻는다. 빈 입력은 기본값이고, 입력이 끊기면(EOF) 기본값으로 진행한다."""
    suffix = "[Y/n]" if default else "[y/N]"
    while True:
        try:
            answer = input(f"  {question} {suffix} ").strip().lower()
        except EOFError:
            return default

        if not answer:
            return default
        if answer in ("y", "yes"):
            return True
        if answer in ("n", "no"):
            return False
        say("  y 또는 n으로 답해 주세요.", "warn")


def ask_port(question: str, default: int) -> int:
    """포트를 물어본다. 엔터는 기본값이고, 숫자가 아니거나 범위를 벗어나면 다시 묻는다."""
    while True:
        answer = ask_text(question, str(default))
        try:
            port = int(answer)
        except ValueError:
            say("  숫자로 입력해 주세요.", "warn")
            continue

        if 1 <= port <= 65535:
            return port
        say("  1~65535 사이의 포트를 입력해 주세요.", "warn")


def resolve_connection_info(args: argparse.Namespace) -> None:
    """
    MySQL·Redis 접속 정보(주소·포트·계정·비밀번호)를 확정해 args에 채운다 — **명시하지 않은 것만 묻는다.**

    옵션으로 준 값은 절대 묻지 않는다(스크립트로 호출하는 쪽이 프롬프트에 걸려 멈추지 않게 하려는
    것이고, 같은 이유로 입력이 터미널이 아니면 아무것도 묻지 않는다). --use-appsettings면 이 값을
    쓰지 않으므로 묻지 않고, --no-prompt는 묻지 않고 기본값(계정·비밀번호는 appsettings)으로 간다.
    어느 경로로 와도 결과는 같은 자리(args.mysql_host·mysql_port·mysql_user·mysql_password·redis)에
    담겨, 이후 단계는 물어봤는지 여부를 알 필요가 없다.
    """
    given_mysql_host = args.mysql_host is not None
    given_mysql_port = args.mysql_port is not None
    given_mysql_user = args.mysql_user is not None
    given_mysql_password = args.mysql_password is not None
    given_redis = args.redis is not None

    prompt = not (args.no_prompt or args.use_appsettings) and stdin_is_console()
    prompt = prompt and not (given_mysql_host and given_mysql_port and given_mysql_user
                             and given_mysql_password and given_redis)

    if prompt:
        say("접속할 MySQL·Redis 정보를 입력하세요(엔터 = 기본값. 비밀번호는 기본값이 없습니다).", "info")

    if not given_mysql_host:
        args.mysql_host = ask_text("MySQL 서버 주소", DEFAULT_MYSQL_HOST) if prompt else DEFAULT_MYSQL_HOST
    if not given_mysql_port:
        args.mysql_port = ask_port("MySQL 포트", DEFAULT_MYSQL_PORT) if prompt else DEFAULT_MYSQL_PORT

    # 계정은 기본값(root)이 있고, 비밀번호는 없다 — 물어보지 않는 경로(--no-prompt)에서는 둘 다 None으로
    # 남겨 appsettings의 Uid·Pwd를 그대로 쓴다(resolve_mysql_credentials가 그 폴백을 담당한다).
    if prompt:
        if not given_mysql_user:
            args.mysql_user = ask_text("MySQL 계정", DEFAULT_MYSQL_USER)
        if not given_mysql_password:
            args.mysql_password = ask_secret("MySQL 비밀번호(입력값 숨김)")

    if not given_redis:
        if prompt:
            redis_host = ask_text("Redis 서버 주소", DEFAULT_REDIS_HOST)
            redis_port = ask_port("Redis 포트", DEFAULT_REDIS_PORT)
            args.redis = f"{redis_host}:{redis_port}"
        else:
            args.redis = DEFAULT_REDIS

    # 테이블 세팅 여부. 로컬 MySQL에는 컨테이너의 init SQL 같은 장치가 없어 처음 한 번은 사람이 넣어야
    # 하므로 기본값을 y로 둔다. **적용 SQL이 DROP TABLE 후 재생성하는 파괴적 스크립트**라 세이브 데이터가
    # 지워진다 — 그래서 물음에 그 사실을 적어 둔다(옵션으로 준 경우는 묻지 않는다).
    # 물어볼 수 없는 실행(파이프·CI·--no-prompt)에서는 적용하지 않는다 — 아무도 보지 않는 자리에서
    # DB를 지우는 쪽이 안 지우는 쪽보다 나쁘다. 그 경로에서 적용하려면 --init-schema를 명시한다.
    if args.init_schema is None:
        args.init_schema = ask_yes_no("MySQL 테이블을 새로 세팅할까요?(기존 데이터가 모두 지워집니다)",
                                      True) if prompt else False

    if prompt:
        print()


# ── 사전 점검 ───────────────────────────────────────────────────────────
def port_listening(host: str, port: int, timeout: float = 0.5) -> bool:
    """그 주소가 접속을 받아 주는지 — 로컬 MySQL·Redis가 실제로 돌고 있는지 보는 데 쓴다."""
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as probe:
        probe.settimeout(timeout)
        return probe.connect_ex((host, port)) == 0


def port_taken(port: int) -> bool:
    """그 포트를 이미 누군가 쥐고 있는지 — ①접속을 걸어 보고, 안 되면 ②직접 bind를 시도해 본다.

    접속만 보면 **연결을 받아 주지 않는 점유자**를 놓친다(백로그가 찬 채 멈춘 프로세스). 그러면 사전
    점검이 조용히 통과하고 watch 콘솔에서 바인딩 실패로만 스쳐 지나간다. SO_REUSEADDR는 켜지 않는다 —
    윈도우에서 그 옵션은 이미 잡힌 주소에도 bind를 허용해 판정을 뒤집는다.
    """
    if port_listening("127.0.0.1", port, timeout=0.3):
        return True

    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as binder:
        try:
            binder.bind(("127.0.0.1", port))
        except OSError:
            return True

    return False


def split_host_port(value: str, default_port: int) -> tuple[str, int]:
    """`host:port` 문자열을 (호스트, 포트)로 나눈다. 포트가 없으면 기본값을 쓴다."""
    host, _, port = value.partition(":")
    host = host.strip() or "127.0.0.1"
    try:
        return host, int(port) if port.strip() else default_port
    except ValueError:
        say(f"Redis 주소를 이해할 수 없습니다: {value} (형식: host:port)", "err")
        raise SystemExit(1)


def check_dotnet() -> bool:
    """dotnet SDK가 PATH에 있는지 확인한다(없으면 watch를 띄울 방법이 없다)."""
    if shutil.which("dotnet"):
        return True

    say("dotnet 을 찾을 수 없습니다. .NET SDK 10을 설치하고 PATH에 등록하세요.", "err")
    return False


def check_account_secret() -> bool:
    """
    AccountServer의 토큰 서명 키를 어디서든 얻을 수 있는지 확인한다.

    정상 경로는 AccountServer/appsettings.json 의 Security:SecretKey다(저장소에 든 로컬 개발용
    기본값). 그래서 새 PC는 클론만 하면 되고 준비물이 없다. 다른 키로 돌리려면 호스트 환경에
    `Security__SecretKey`를 두면 그 값이 파일 기본값을 이긴다.
    """
    if read_json_setting(ROOT / "AccountServer" / "appsettings.json", ("Security", "SecretKey")):
        return True
    if os.environ.get("Security__SecretKey"):
        return True

    say("AccountServer 토큰 서명 키(Security:SecretKey)를 찾을 수 없습니다.", "err")
    say("AccountServer/appsettings.json의 Security:SecretKey를 되돌리거나,", "warn")
    say("환경 변수 Security__SecretKey=<임의의 긴 문자열> 을 두세요.", "warn")
    return False


def prepare_event_log_dir() -> None:
    """GameServer가 이벤트 로그를 쓰는 디렉터리를 미리 만든다(서버를 한 번도 안 띄웠으면 없다)."""
    event_dir = ROOT / "GameServer" / "logs" / "event"
    if not event_dir.exists():
        event_dir.mkdir(parents=True, exist_ok=True)
        say("이벤트 로그 디렉터리 생성: GameServer/logs/event", "dim")


def check_dependencies(args: argparse.Namespace) -> bool:
    """
    MySQL·Redis가 응답하는지 확인한다 — **둘 다 이 스크립트가 띄우지 않고, 없으면 막는다.**

    이 스크립트가 하는 일은 서버 2개를 띄우는 것뿐이다. 의존 서비스는 이 PC에 설치·서비스 등록해
    두거나 컨테이너로 띄워 두는 것이 전제다. 응답이 없을 때 경고로 넘기지 않고 막는 이유는, 넘어가면
    서버는 그대로 뜨고 **첫 요청에서야 접속 오류로 드러나** 원인을 찾기가 더 어렵기 때문이다.
    """
    mysql_ok = port_listening(args.mysql_host, args.mysql_port)

    redis_host, redis_port = split_host_port(args.redis, DEFAULT_REDIS_PORT)
    redis_ok = port_listening(redis_host, redis_port)

    if mysql_ok and redis_ok:
        say(f"의존 서비스 확인: MySQL {args.mysql_host}:{args.mysql_port} · Redis {redis_host}:{redis_port} 응답 중.", "dim")
        return True

    missing = []
    if not mysql_ok:
        missing.append(f"MySQL({args.mysql_host}:{args.mysql_port})")
    if not redis_ok:
        missing.append(f"Redis({redis_host}:{redis_port})")
    say(f"의존 서비스가 응답하지 않습니다: {', '.join(missing)}", "err")

    if not mysql_ok:
        say("· MySQL은 이 스크립트가 띄우지 않습니다 — 로컬 서비스를 시작하거나 포트·계정을 확인하세요", "warn")
        say("  (--mysql-host · --mysql-port · --mysql-user · --mysql-password).", "warn")
    if not redis_ok:
        say("· Redis도 이 스크립트가 띄우지 않습니다 — 로컬 인스턴스를 먼저 켜고 주소를 맞추세요(--redis).", "warn")
        say("  (저장소 바이너리를 쓸 경우:  Redis-8.8.0-Windows-x64-cygwin-with-Service/start.bat)", "warn")
    say("· 컨테이너로 띄우려면:  docker compose up -d mysql redis", "warn")
    say("  (그 경우 --mysql-port 33306 --redis 127.0.0.1:36379)", "warn")
    say("· 확인만 건너뛰려면 --skip-deps-check (접속 실패는 서버 로그에서 드러납니다).", "warn")
    return False


def check_server_ports() -> bool:
    """서버 포트(5160·5247)가 비어 있는지 확인한다 — 컨테이너 서버와 동시에 띄울 수 없다."""
    busy = [f"{port}({label})" for _, label, port in SERVERS if port_taken(port)]
    if not busy:
        return True

    say(f"이미 사용 중인 포트: {', '.join(busy)}", "err")
    say("컨테이너 서버가 떠 있다면 먼저 내리세요:  docker compose stop accountserver gameserver", "warn")
    say("콘솔로 띄운 서버가 남아 있으면:  python server_up.py --stop", "warn")
    return False


# ── MySQL 스키마 ────────────────────────────────────────────────────────
def resolve_mysql_credentials(args: argparse.Namespace) -> tuple[str, str]:
    """
    스키마 작업에 쓸 (계정, 비밀번호)를 정한다: 인자로 준 값 -> appsettings의 Uid·Pwd.

    서버가 붙는 계정과 **같은 계정**으로 확인해야 의미가 있다. 그래서 인자가 없으면 접속 문자열
    정본에서 그대로 가져온다(스크립트에 계정을 박아 두면 설정을 바꿨을 때 조용히 어긋난다).
    """
    base = read_json_setting(ROOT / "GameServer" / "appsettings.json", ("ConnectionStrings", "GameDb")) or ""
    user = args.mysql_user if args.mysql_user is not None else (read_conn_value(base, "Uid") or "root")
    password = args.mysql_password if args.mysql_password is not None else (read_conn_value(base, "Pwd") or "")
    return user, password


def mysql_backend() -> str | None:
    """스키마 작업에 쓸 수단을 고른다 — `mysql` CLI가 있으면 그것, 없으면 pymysql, 둘 다 없으면 None."""
    if shutil.which("mysql"):
        return "cli"

    try:
        import pymysql  # noqa: F401
    except ImportError:
        return None
    return "pymysql"


def mysql_query(args: argparse.Namespace, sql: str) -> tuple[bool, str, str]:
    """질의 1건을 돌린다. (성공 여부, 탭 구분 출력, 실패 이유) — CLI와 pymysql 중 있는 쪽을 쓴다."""
    user, password = resolve_mysql_credentials(args)
    backend = mysql_backend()

    if backend == "cli":
        argv = ["mysql", f"-h{args.mysql_host}", f"-P{args.mysql_port}", f"-u{user}"]
        if password:
            argv.append(f"-p{password}")
        result = subprocess.run(argv + ["-N", "-B", "-e", sql],
                                cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace")
        return result.returncode == 0, result.stdout.strip(), result.stderr.strip()

    if backend == "pymysql":
        import pymysql

        try:
            with pymysql.connect(host=args.mysql_host, port=args.mysql_port, user=user,
                                 password=password, charset="utf8mb4", connect_timeout=5) as conn:
                with conn.cursor() as cursor:
                    cursor.execute(sql)
                    rows = cursor.fetchall()
        except Exception as error:
            return False, "", str(error)

        # CLI의 `-N -B` 출력(헤더 없는 탭 구분)과 같은 모양으로 맞춘다 — 호출부가 한 가지만 다루게.
        output = "\n".join("\t".join("" if cell is None else str(cell) for cell in row) for row in rows)
        return True, output, ""

    return False, "", "MySQL 접속 수단이 없습니다(`mysql` CLI·pymysql 모두 없음)."


def apply_schema(args: argparse.Namespace) -> bool:
    """
    로컬 MySQL에 스키마·마스터 데이터 SQL을 순서대로 적용한다(--init-schema).

    컨테이너는 첫 기동에 init SQL이 돌지만 로컬 설치본에는 그런 장치가 없다 — 처음 한 번은 사람이
    넣어야 하는데, 두 파일의 **적용 순서**(스키마 → 마스터 데이터)를 틀리면 FK·참조에서 깨지므로
    여기서 순서를 고정해 준다.
    """
    backend = mysql_backend()
    if backend is None:
        say("MySQL에 접속할 수단이 없어 스키마를 적용하지 못했습니다.", "err")
        say("`mysql` CLI를 PATH에 두거나(`pip install pymysql`도 가능), 두 파일을 순서대로 직접 실행하세요:", "warn")
        for path in SCHEMA_FILES:
            say(f"  mysql -h... -u... -p... < \"{path}\"", "warn")
        return False

    missing_files = [path for path in SCHEMA_FILES if not (ROOT / path).exists()]
    if missing_files:
        say(f"스키마 파일이 없습니다: {', '.join(str(path) for path in missing_files)}", "err")
        return False

    user, password = resolve_mysql_credentials(args)
    for path in SCHEMA_FILES:
        full = ROOT / path
        say(f"스키마 적용 중: {path}", "dim")

        if backend == "cli":
            argv = ["mysql", f"-h{args.mysql_host}", f"-P{args.mysql_port}", f"-u{user}"]
            if password:
                argv.append(f"-p{password}")
            with full.open("rb") as stream:
                result = subprocess.run(argv, cwd=ROOT, stdin=stream, capture_output=True,
                                        text=True, encoding="utf-8", errors="replace")
            if result.returncode != 0:
                say(f"스키마 적용 실패: {path}", "err")
                say(result.stderr.strip() or "(오류 출력 없음)", "err")
                return False
            continue

        # pymysql 경로 — 파일 하나를 여러 문장으로 그대로 넘긴다(두 SQL 파일에 DELIMITER 블록이
        # 없으므로 문장을 손으로 쪼갤 필요가 없다. 프로시저·트리거가 생기면 CLI를 써야 한다).
        import pymysql
        from pymysql.constants import CLIENT

        try:
            with pymysql.connect(host=args.mysql_host, port=args.mysql_port, user=user, password=password,
                                 charset="utf8mb4", autocommit=True, connect_timeout=10,
                                 client_flag=CLIENT.MULTI_STATEMENTS) as conn:
                with conn.cursor() as cursor:
                    cursor.execute(full.read_text(encoding="utf-8-sig"))
                    while cursor.nextset():
                        pass
        except Exception as error:
            say(f"스키마 적용 실패: {path}", "err")
            say(str(error), "err")
            return False

    say("스키마·마스터 데이터 적용 완료.", "ok")
    return True


def verify_schema(args: argparse.Namespace) -> bool:
    """
    계정·게임·마스터 DB의 테이블이 실제로 있는지 센다. 없으면 무엇을 실행해야 하는지 알려 준다.

    접속 수단이 아예 없으면 확인을 건너뛰고 진행한다(확인 수단이 없는 것이 곧 실패는 아니다).
    테이블이 비어 있는 상태는 스스로 낫지 않으므로, 그때는 막는다 — 그대로 넘어가면 서버가 뜬 뒤
    SQL 오류로만 드러난다.
    """
    if mysql_backend() is None:
        say("MySQL 접속 수단이 없어(`mysql` CLI·pymysql 모두 없음) 스키마 확인을 건너뜁니다.", "warn")
        return True

    ok, output, error = mysql_query(
        args,
        "SELECT table_schema, COUNT(*) FROM information_schema.tables "
        f"WHERE table_schema IN ({', '.join(repr(db) for db in REQUIRED_DATABASES)}) "
        "GROUP BY table_schema;",
    )
    if not ok:
        # 포트는 열려 있는데(사전 점검을 통과했다) 질의가 안 된다 = 계정·비밀번호가 맞지 않는다.
        # 그대로 진행하면 서버는 정상으로 뜨고 **모든 요청이 DB 오류로 죽는다** — 그래서 여기서 막는다.
        user, _ = resolve_mysql_credentials(args)
        say(f"MySQL에 접속하지 못했습니다({args.mysql_host}:{args.mysql_port}, 계정 {user}).", "err")
        if error:
            say(f"  {error}", "err")
        say("계정·비밀번호를 확인하세요(--mysql-user · --mysql-password, 또는 프롬프트에서 입력).", "warn")
        return False

    counts: dict[str, int] = {}
    for line in output.splitlines():
        parts = line.split("\t")
        if len(parts) == 2 and parts[1].isdigit():
            counts[parts[0]] = int(parts[1])

    missing = [db for db in REQUIRED_DATABASES if counts.get(db, 0) == 0]
    if missing:
        say(f"MySQL에 테이블이 없습니다: {', '.join(missing)}", "err")
        say("로컬 네이티브 MySQL은 스키마를 저절로 만들지 않습니다(컨테이너의 init SQL이 없으므로).", "warn")
        say("다시 실행해 「MySQL 테이블을 새로 세팅할까요?」에 y로 답하거나(기본값 y),", "warn")
        say("묻지 않고 적용하려면:  python server_up.py --init-schema", "warn")
        return False

    say("MySQL 스키마 확인: " + " · ".join(f"{db.split('_')[-1]} {counts[db]}개 테이블" for db in REQUIRED_DATABASES), "dim")

    # 마스터 시드와 보스러시 시즌은 없어도 서버가 뜨긴 하므로 경고만 남긴다(원인은 대부분 같다).
    ok, output, _ = mysql_query(
        args,
        "SELECT (SELECT COUNT(*) FROM taskbar_hero_master.item_master), "
        "(SELECT COUNT(*) FROM taskbar_hero_game.boss_rush_season WHERE status = 1);",
    )
    if ok:
        parts = output.split("\t")
        if len(parts) == 2 and parts[0] == "0":
            say("마스터 데이터가 비어 있습니다 — 게임 로직이 MasterDataNotLoaded로 거부됩니다.", "warn")
        if len(parts) == 2 and parts[1] == "0":
            say("진행 중 보스러시 시즌이 없습니다 — 도전·랭킹이 닫힌 상태입니다.", "warn")

    return True


# ── 빌드 · 기동 ─────────────────────────────────────────────────────────
def prebuild() -> bool:
    """두 watch를 띄우기 전에 솔루션을 직렬로 한 번 빌드한다(공유 출력 폴더 경합 방지 — 상단 설명)."""
    say("선 빌드 중(TaskbarHero.Common 공유 출력 경합 방지)...", "dim")
    result = subprocess.run(["dotnet", "build", str(SOLUTION), "-v", "q"], cwd=ROOT)
    if result.returncode != 0:
        say("선 빌드 실패. 위 오류를 확인하세요(watch는 띄우지 않습니다).", "err")
        return False

    say("선 빌드 완료.", "ok")
    return True


def start_watch(project: str, label: str, extra_env: dict[str, str]) -> bool:
    """
    프로젝트 하나를 `dotnet watch run`으로 띄운다 — 윈도우에서는 **새 콘솔 창**으로 띄운다.

    창을 따로 띄우는 이유는 두 서버의 로그가 섞이지 않게 하고, 무엇이 돌고 있는지 사용자에게 그대로
    보이게 하려는 것이다(숨김 기동은 세션이 끝나도 남아 bin/ 파일을 잠근다). 윈도우가 아니면 새 창을
    여는 표준 방법이 없어 현재 터미널에 붙인다(로그가 섞인다).
    """
    env = os.environ.copy()
    env.update(extra_env)

    argv = ["dotnet", "watch", "run", "--launch-profile", LAUNCH_PROFILE]
    kwargs: dict[str, object] = {"cwd": str(ROOT / project), "env": env}
    if os.name == "nt":
        kwargs["creationflags"] = subprocess.CREATE_NEW_CONSOLE

    try:
        subprocess.Popen(argv, **kwargs)  # type: ignore[arg-type]
    except OSError as error:
        say(f"{label} 기동 실패: {error}", "err")
        return False

    say(f"  {label} 기동 — dotnet watch run (프로젝트: {project})", "dim")
    return True


def wait_health(label: str, port: int, timeout: int) -> bool:
    """서버가 요청을 받을 준비가 됐는지 OpenAPI 문서로 확인한다(Development에서만 열린다)."""
    url = f"http://localhost:{port}{HEALTH_PATH}"
    deadline = time.monotonic() + timeout

    while True:
        try:
            with urllib.request.urlopen(url, timeout=5) as response:
                if response.status == 200:
                    say(f"  OK  {label:<14} http://localhost:{port}/swagger", "ok")
                    return True
        except (urllib.error.URLError, TimeoutError, OSError):
            pass

        if time.monotonic() >= deadline:
            say(f"  ..  {label:<14} 준비되지 않았습니다({url})", "warn")
            return False
        time.sleep(2)


# ── 랭킹 캐시 적재 ──────────────────────────────────────────────────────
def read_admin_key(explicit: str | None) -> str | None:
    """관리 API 키를 찾는다: 인자 -> 환경변수 -> GameServer/appsettings.json.

    설정 파일에서도 읽는 이유는 키의 정본을 한 곳(서버 설정)에 두기 위해서다 — 스크립트에 기본값을
    박아 두면 서버 설정을 바꿨을 때 조용히 401이 난다.
    """
    if explicit:
        return explicit

    from_env = os.environ.get("TASKBAR_HERO_ADMIN_KEY")
    if from_env:
        return from_env

    return read_json_setting(ROOT / "GameServer" / "appsettings.json", ("Admin", "ApiKey"))


def wait_game_server(base_url: str, timeout: int) -> bool:
    """GameServer가 요청을 받을 준비가 됐는지 OpenAPI 문서로 확인한다(--warmup-only 경로에서 쓴다)."""
    deadline = time.monotonic() + timeout
    url = base_url.rstrip("/") + HEALTH_PATH

    while True:
        try:
            with urllib.request.urlopen(url, timeout=5) as response:
                if response.status == 200:
                    return True
        except (urllib.error.URLError, TimeoutError, OSError):
            pass

        if time.monotonic() >= deadline:
            return False
        time.sleep(2)


def warm_up_rank_cache(base_url: str, admin_key: str, force: bool) -> bool:
    """관리 API로 보스러시 랭킹 캐시 최초 적재를 지시한다(적재 자체는 서버가 한다)."""
    url = base_url.rstrip("/") + WARMUP_PATH + ("?force=true" if force else "")
    request = urllib.request.Request(
        url,
        data=b"",
        method="POST",
        headers={"X-Admin-Key": admin_key, "Content-Type": "application/json"},
    )

    try:
        with urllib.request.urlopen(request, timeout=180) as response:
            status_code = response.status
            payload = json.loads(response.read().decode("utf-8"))
    except urllib.error.HTTPError as error:
        body = error.read().decode("utf-8", errors="replace")
        if error.code == 404:
            say("랭킹 캐시 적재 실패: 관리 API가 닫혀 있습니다(GameServer 설정 Admin:ApiKey 확인).", "err")
        elif error.code == 401:
            say("랭킹 캐시 적재 실패: 관리 API 키가 맞지 않습니다(--admin-key 확인).", "err")
        else:
            say(f"랭킹 캐시 적재 실패: HTTP {error.code} {body}", "err")
        return False
    except (urllib.error.URLError, TimeoutError, OSError, ValueError) as error:
        say(f"랭킹 캐시 적재 실패: {error}", "err")
        return False

    data = payload.get("data") or {}
    state = data.get("status", "?")
    season = data.get("seasonId", 0)
    restored = data.get("restored", 0)
    members = data.get("members", 0)

    if status_code != 200 or not payload.get("success") or state == WARMUP_CACHE_UNAVAILABLE:
        say(f"랭킹 캐시 적재 실패: status={state} seasonId={season} (Redis 접근 확인)", "err")
        return False

    say(f"랭킹 캐시 적재: status={state} seasonId={season} 적재 {restored}건 · 등재 {members}명", "ok")

    # 시즌이 없으면 적재할 것도 없지만, 그 상태는 보스러시가 닫혀 있다는 뜻이라 조용히 넘기지 않는다.
    if state == WARMUP_NO_SEASON:
        say("  진행 중 보스러시 시즌이 없습니다 — 도전·랭킹이 닫힌 상태입니다(boss_rush_season 확인).", "warn")

    return True


def run_warmup(args: argparse.Namespace) -> int:
    """서버 준비 대기 -> 랭킹 캐시 적재. 실패하면 그대로 종료 코드를 올려 보낸다."""
    admin_key = read_admin_key(args.admin_key)
    if not admin_key:
        say("관리 API 키를 찾을 수 없습니다(--admin-key 또는 appsettings.json의 Admin:ApiKey).", "err")
        return 1

    say("GameServer 준비 대기 중...", "dim")
    if not wait_game_server(args.game_url, args.timeout):
        say(f"GameServer가 준비되지 않았습니다: {args.game_url}{HEALTH_PATH}", "err")
        return 1

    return 0 if warm_up_rank_cache(args.game_url, admin_key, args.force_warmup) else 1


# ── 종료 ────────────────────────────────────────────────────────────────
def stop_servers() -> int:
    """
    이 방식으로 띄운 서버를 종료한다 — **앱 프로세스와 watch/run 래퍼를 모두** 잡는다.

    래퍼(dotnet.exe)가 살아 있으면 자식(GameServer.exe·AccountServer.exe)을 다시 띄워, 종료했다고
    본 직후에 서버가 되살아난다. 그래서 둘을 함께 끊고 남은 수를 다시 센다.
    """
    if os.name != "nt":
        say("--stop 은 윈도우에서만 지원합니다. 서버 콘솔 창에서 Ctrl+C 로 종료하세요.", "warn")
        return 1

    script = (
        "$names = 'GameServer','AccountServer';"
        "Get-Process -Name $names -ErrorAction SilentlyContinue | Stop-Process -Force;"
        "Get-CimInstance Win32_Process -Filter \"Name='dotnet.exe'\" |"
        "  Where-Object { $_.CommandLine -match 'GameServer|AccountServer' } |"
        "  ForEach-Object { try { Stop-Process -Id $_.ProcessId -Force } catch {} };"
        "Start-Sleep -Milliseconds 500;"
        "$app = @(Get-Process -Name $names -ErrorAction SilentlyContinue).Count;"
        "$wrap = @(Get-CimInstance Win32_Process -Filter \"Name='dotnet.exe'\" |"
        "  Where-Object { $_.CommandLine -match 'GameServer|AccountServer' }).Count;"
        "Write-Output \"$app $wrap\""
    )
    result = subprocess.run(["powershell", "-NoProfile", "-Command", script],
                            capture_output=True, text=True, encoding="utf-8", errors="replace")
    counts = result.stdout.strip().split()
    app, wrapper = (counts + ["?", "?"])[:2]

    if app == "0" and wrapper == "0":
        say("서버 종료 완료(앱 0 · watch 래퍼 0).", "ok")
        return 0

    say(f"잔여 프로세스가 남아 있습니다(앱 {app} · watch 래퍼 {wrapper}). 콘솔 창을 직접 닫으세요.", "warn")
    return 1


# ── 진입점 ──────────────────────────────────────────────────────────────
def parse_args() -> argparse.Namespace:
    """명령행 인자를 읽는다(기본: 선빌드 + 두 서버 watch 기동 + 랭킹 캐시 적재)."""
    parser = argparse.ArgumentParser(
        prog="server_up.py",
        description="도커 없이 서버 2개만 dotnet watch로 띄운다(로컬 MySQL·Redis 전제) + 랭킹 캐시 적재.",
    )
    parser.add_argument("--mysql-host", default=None, help=f"로컬 MySQL 호스트(생략하면 물어본다. 기본 {DEFAULT_MYSQL_HOST})")
    parser.add_argument("--mysql-port", type=int, default=None, help=f"로컬 MySQL 포트(생략하면 물어본다. 기본 {DEFAULT_MYSQL_PORT})")
    parser.add_argument("--mysql-user", default=None,
                        help=f"MySQL 계정(생략하면 물어본다. 기본 {DEFAULT_MYSQL_USER} · --no-prompt면 appsettings의 Uid)")
    parser.add_argument("--mysql-password", default=None,
                        help="MySQL 비밀번호(생략하면 물어본다 — 기본값 없음. --no-prompt면 appsettings의 Pwd)")
    parser.add_argument("--redis", default=None, help=f"로컬 Redis 주소 host:port(생략하면 물어본다. 기본 {DEFAULT_REDIS})")
    parser.add_argument("--no-prompt", action="store_true", help="주소를 묻지 않고 기본값으로 진행한다(스크립트 호출용)")
    parser.add_argument("--no-pause", action="store_true",
                        help="끝날 때 엔터를 기다리지 않고 바로 닫는다(기본은 출력을 읽을 수 있게 기다린다)")
    parser.add_argument("--use-appsettings", action="store_true",
                        help="접속 정보를 덮어쓰지 않고 appsettings.json 값을 그대로 쓴다")
    # 세 가지 상태가 필요하다 — 묻지 않고 적용 / 묻지 않고 건너뜀 / 물어보기(기본, y).
    parser.add_argument("--init-schema", dest="init_schema", action="store_const", const=True, default=None,
                        help="묻지 않고 스키마·마스터 데이터 SQL을 적용한다(기존 테이블을 DROP 후 재생성)")
    parser.add_argument("--no-init-schema", dest="init_schema", action="store_const", const=False,
                        help="묻지 않고 테이블 세팅을 건너뛴다")
    parser.add_argument("--skip-deps-check", action="store_true", help="로컬 MySQL·Redis 응답 확인을 건너뛴다")
    parser.add_argument("--no-build", action="store_true", help="선 빌드를 건너뛴다(공유 출력 경합 위험 — 상단 설명)")
    parser.add_argument("--warmup-only", action="store_true", help="서버를 띄우지 않고 랭킹 캐시 적재만 지시한다")
    parser.add_argument("--no-warmup", action="store_true", help="랭킹 캐시 적재 단계를 건너뛴다")
    parser.add_argument("--force-warmup", action="store_true", help="리더보드가 이미 채워져 있어도 다시 적재한다")
    parser.add_argument("--stop", action="store_true", help="이 방식으로 띄운 서버(앱 + watch 래퍼)를 종료한다")
    parser.add_argument("--admin-key", help="관리 API 키(기본: TASKBAR_HERO_ADMIN_KEY -> appsettings.json)")
    parser.add_argument("--game-url", default=DEFAULT_GAME_URL, help=f"GameServer 주소(기본 {DEFAULT_GAME_URL})")
    parser.add_argument("--timeout", type=int, default=180, help="준비 대기 상한(초, 기본 180)")
    return parser.parse_args()


def print_summary(args: argparse.Namespace) -> None:
    """무엇이 어디에 떠 있는지, 서버가 어디에 붙었는지만 안내한다."""
    print()
    say("  AccountServer: http://localhost:5160/swagger", "dim")
    say("  GameServer   : http://localhost:5247/swagger", "dim")
    if args.use_appsettings:
        say("  DB·Redis     : appsettings.json 값 그대로(덮어쓰지 않음)", "dim")
    else:
        user, password = resolve_mysql_credentials(args)
        say(f"  MySQL        : {args.mysql_host}:{args.mysql_port} ({user}{'' if password else ' · 비밀번호 없음'})", "dim")
        say(f"  Redis        : {args.redis}", "dim")
    print()
    say("코드를 고치면 watch가 자동으로 다시 올립니다(마스터 데이터 값은 서버 재기동이 필요).", "dim")
    say("서버를 멈추려면 콘솔 창을 닫거나:  python server_up.py --stop", "dim")


def main() -> int:
    """사전 점검 -> 스키마 확인 -> 선 빌드 -> watch 기동 -> 준비 대기 -> 랭킹 캐시 적재."""
    global _USE_COLOR

    setup_console()
    args = parse_args()
    _USE_COLOR = sys.stdout.isatty()

    if args.stop:
        return stop_servers()

    # 적재만 지시하는 경로(서버를 이미 띄워 둔 상태).
    if args.warmup_only:
        return run_warmup(args)

    # ── 접속 정보 확정(명시하지 않은 것만 물어본다) ──
    resolve_connection_info(args)

    # ── 사전 점검 — 막힐 것이 확실한 조건은 창을 띄우기 전에 알려 준다 ──
    if not check_dotnet():
        return 1
    if not check_account_secret():
        return 1
    if not args.skip_deps_check and not check_dependencies(args):
        return 1
    if not check_server_ports():
        return 1

    prepare_event_log_dir()

    # ── 스키마 적용·확인 ──
    if args.init_schema and not apply_schema(args):
        return 1
    if not verify_schema(args):
        return 1

    # 접속 정보(환경 변수 덮어쓰기)는 기동 직전에 확정한다 — 설정을 못 읽으면 여기서 멈춘다.
    env_by_server = resolve_child_env(args)

    # ── 선 빌드 ──
    print()
    if args.no_build:
        say("선 빌드를 건너뜁니다 — 첫 기동이면 두 watch가 공유 출력에서 충돌할 수 있습니다.", "warn")
    elif not prebuild():
        return 1

    # ── watch 기동 ──
    print()
    say("서버를 dotnet watch로 띄웁니다(각자 새 콘솔 창).", "info")
    for project, label, _ in SERVERS:
        if not start_watch(project, label, env_by_server[project]):
            return 1

    # ── 준비 대기 ──
    print()
    say("준비 상태 확인 중(첫 기동은 빌드 때문에 시간이 걸립니다)...", "dim")
    pending = [label for _, label, port in SERVERS if not wait_health(label, port, args.timeout)]
    if pending:
        print()
        say(f"아직 준비되지 않은 서버: {', '.join(pending)}", "warn")
        say("각 콘솔 창의 빌드·기동 로그를 확인하세요.", "warn")

    # ── 보스러시 랭킹 캐시 최초 적재 ──
    print()
    if args.no_warmup:
        say("랭킹 캐시 적재를 건너뜁니다(필요해지면: python server_up.py --warmup-only).", "dim")
    elif "GameServer" in pending:
        say("GameServer가 준비되지 않아 랭킹 캐시를 적재하지 못했습니다.", "err")
        return 1
    else:
        code = run_warmup(args)
        if code != 0:
            return code

    print_summary(args)
    return 1 if pending else 0


def hold_console() -> None:
    """
    끝나고 **엔터를 누를 때까지 창을 닫지 않는다** — 성공이든 실패든 마지막 출력을 읽을 수 있게.

    파일을 더블클릭해 실행하면 종료와 동시에 창이 닫혀 실패 사유가 그대로 사라진다. 그래서 기본으로
    기다리고, 콘솔이 아닌 실행(파이프·CI·다른 스크립트가 호출)에서는 멈추면 안 되므로 그냥 지나간다.
    --no-pause 로 끌 수 있다(인자 파싱 전에 죽은 경우에도 동작해야 하므로 argv를 직접 본다).
    """
    if "--no-pause" in sys.argv or not stdin_is_console():
        return

    try:
        input("\n창을 닫으려면 엔터를 누르세요... ")
    except (EOFError, KeyboardInterrupt):
        pass


if __name__ == "__main__":
    # 예외까지 이 자리에서 받아 낸다 — 더블클릭 실행에서는 역추적이 찍히자마자 창이 닫혀 사라진다.
    try:
        exit_code = main()
    except KeyboardInterrupt:
        say("\n중단했습니다.", "warn")
        exit_code = 130
    except Exception:
        say("예상치 못한 오류로 중단했습니다:", "err")
        traceback.print_exc()
        exit_code = 1

    hold_console()
    sys.exit(exit_code)
