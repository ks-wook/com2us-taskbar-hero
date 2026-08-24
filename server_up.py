#!/usr/bin/env python
"""로컬 개발 환경 부트스트랩 — 컨테이너 기동부터 보스러시 랭킹 캐시 최초 적재까지.

사용법 (저장소 루트에서 실행한다 — docker compose가 이 위치를 기준으로 돈다)
    python server_up.py                  # **아무것도 묻지 않는다** — 서버 이미지 재빌드 + 게임 필수
                                         #   4개(accountserver · gameserver · mysql · redis) 기동
                                         #   + 보스러시 랭킹 캐시 적재
    python server_up.py --mode full      # 로그 파이프라인(fluentd·logdb·Grafana)까지 전부
    python server_up.py -i               # 무엇을 띄울지·재빌드할지 메뉴로 고른다
    python server_up.py --warmup-only    # 이미 떠 있는 GameServer에 랭킹 캐시 적재만 지시
    python server_up.py --down           # 그 묶음을 중지(볼륨·데이터는 남는다)

    -i 로 물을 때도 옵션으로 명시한 항목은 묻지 않는다.

띄우는 것 — 둘 중 하나다(대화형 메뉴의 1번·2번이 각각 이것이다)
    dev (기본)  mysql · redis · accountserver · gameserver
                게임이 도는 최소 구성. GameServer는 이벤트 로그를 파일로 계속 쓰고, 그 파일을
                걷어 적재·조회하는 쪽(fluentd·logdb·Grafana)만 빠진다.
    full        dev + timescaledb(logdb) · fluentd · grafana
                로그 파이프라인이 끝까지 도는 완전한 환경(파일 → fluentd → logdb → Grafana).
                **다른 PC에서 Grafana를 쓰려면** 컴포즈의 GF_SERVER_ROOT_URL 기본값이 작성자의
                테일스케일 주소이므로 저장소 루트 .env에 두 줄이 필요하다:
                    GRAFANA_ROOT_URL=http://localhost:3000/
                    GRAFANA_DOMAIN=localhost

    두 묶음 모두 서버를 컨테이너로 띄운다. 서버를 콘솔(watch-all.ps1)로 띄우고 싶으면 의존 서비스만
    직접 올린다:  docker compose up -d mysql redis   (그 뒤 랭킹 캐시는 --warmup-only로 적재)

기동 절차(스크립트가 순서대로 한다)
    ① 사전 점검 — 도커 접속 · 토큰 서명 키 · 이벤트 로그 디렉터리 · **호스트 포트 충돌**
    ② docker compose up -d (서버 이미지 재빌드)
    ③ 모든 컨테이너가 healthy 될 때까지 대기
    ④ **MySQL 스키마 확인** — 계정·게임·마스터 DB의 테이블이 실제로 만들어졌는지 센다
    ⑤ 보스러시 랭킹 캐시 적재(관리 API 1회 호출)

주요 옵션
    -i, --interactive  무엇을 띄울지·재빌드할지 메뉴로 물어본다(기본은 묻지 않는다).
    --mode dev|full 무엇을 띄울지 지정한다(생략하면 dev).
    --build         묻지 않고 서버 이미지를 다시 빌드한다.
    --no-build      서버 이미지 재빌드를 건너뛴다(코드를 안 고쳤을 때만).
    --force-warmup  리더보드가 이미 채워져 있어도 MySQL에서 다시 적재한다.
    --no-warmup     랭킹 캐시 적재 단계를 건너뛴다(적재는 묻지 않고 기본으로 한다).
    --admin-key K   관리 API 키(기본: 환경변수 TASKBAR_HERO_ADMIN_KEY -> GameServer/appsettings.json).
    --game-url U    GameServer 주소(기본 http://localhost:5247).
    --timeout N     준비 대기 상한(초, 기본 180).

왜 랭킹 캐시 적재가 여기 있나
    보스러시 랭킹은 MySQL(boss_rush_record)이 정본이고 Redis Sorted Set은 조회용 파생 인덱스다.
    Redis가 비어 있으면(첫 기동·볼륨 초기화) 정본에서 다시 만들어야 하는데, 예전에는 시즌 정산
    배치가 리더 락을 쥔 채 매 주기 앞단에서 그 일을 했다. 그러면 적재 시점이 배치 주기에 묶여
    눈에 보이지 않는다. 지금은 이 스크립트가 기동 절차의 명시적인 한 단계로 관리 API를 한 번
    호출한다(POST /api/admin/boss-rush/rank/warmup) — 서버는 스스로 적재하지 않는다.
    호출자가 하나뿐이라 중복 재구축을 막을 분산 락도 필요하지 않다.

    적재 로직 자체(점수 인코딩·키 이름)는 서버 코드에만 있다. 이 스크립트는 MySQL·Redis에 직접
    붙지 않는다 — 붙으면 인코딩 규칙이 두 언어에 복제돼 조용히 어긋난다.

주의
    · **새 PC에서 손으로 준비할 것이 없다.** AccountServer의 토큰 서명 키는 저장소에 든
      AccountServer/appsettings.json의 Security:SecretKey(로컬 개발용 기본값)를 쓴다. 다른 키로
      돌리려면 .env나 호스트 환경에 `Security__SecretKey=<값>` 을 두면 기본값을 덮어쓴다.
      (Grafana만 예외 — full 모드에서 다른 PC라면 위의 .env 두 줄이 필요하다.)
    · 컨테이너 서버와 watch-all.ps1 은 같은 5160·5247 포트를 쓰므로 동시에 띄울 수 없다.
      핫 리로드로 개발할 때는 `docker compose up -d mysql redis` + watch-all.ps1 을 쓰고,
      서버가 뜬 뒤 `python server_up.py --warmup-only` 로 랭킹 캐시를 적재한다.
    · 서버가 포함되면 이미지를 자동으로 다시 빌드한다. 서버 이미지는 소스를 COPY해 굽기 때문에
      (핫 리로드 없음) 재빌드를 건너뛰면 옛 바이너리가 그대로 뜬다.
    · **스키마·마스터 데이터는 데이터 볼륨이 빈 첫 기동에만 만들어진다** — compose가 docs/의 스키마
      SQL을 /docker-entrypoint-initdb.d 로 마운트하기 때문이다. 그래서 새 PC의 첫 기동은 저절로
      되지만, 스키마를 고쳐 다시 반영하려면 볼륨을 비워야 한다(데이터가 사라지므로 이 스크립트는
      하지 않는다):  docker compose down -v
      ④단계가 테이블 수를 세는 이유가 이것이다 — "볼륨은 있는데 스키마가 없는" 상태는 스스로 낫지
      않고, 그대로 넘어가면 서버가 뜬 뒤 SQL 오류로만 드러난다.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import socket
import subprocess
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

# ── 저장소 구조 ─────────────────────────────────────────────────────────
#   이 스크립트는 저장소 루트에 있다(docker compose를 그 자리에서 실행해야 하므로).
ROOT = Path(__file__).resolve().parent

# ── 묶음 정의 ───────────────────────────────────────────────────────────
#   compose의 depends_on이 순서를 보장하므로 여기서는 "무엇을 띄울지"만 정한다.
# 선택은 둘 중 하나다 — 게임을 돌리는 데 필요한 것만(dev), 로그 파이프라인까지 전부(full).
#   dev  : 게임이 도는 최소 구성. 로그는 GameServer가 파일로 계속 쓰고, 그 파일을 걷어 적재·조회하는
#          쪽(fluentd·logdb·Grafana)만 빠진다.
#   full : dev + 로그 수집·저장·대시보드. **Grafana를 다른 PC에서 쓰려면** 컴포즈의
#          GF_SERVER_ROOT_URL 기본값이 작성자의 테일스케일 주소라 저장소 루트 .env에 두 줄이 필요하다:
#            GRAFANA_ROOT_URL=http://localhost:3000/
#            GRAFANA_DOMAIN=localhost
GROUP_DEV = ["mysql", "redis", "accountserver", "gameserver"]
GROUPS = {
    "dev": GROUP_DEV,
    "full": ["mysql", "redis", "timescaledb", "fluentd", "grafana"] + ["accountserver", "gameserver"],
}
DEFAULT_MODE = "dev"

# 대화형 메뉴 — (번호, 모드, 이름, 포함되는 것). 사용자가 보는 문구는 이 표가 정본이다.
MODE_MENU = (
    ("1", "dev", "게임 서버 필수만", "MySQL · Redis · AccountServer · GameServer"),
    ("2", "full", "로그까지 전부", "1번 + logdb(TimescaleDB) · fluentd · Grafana"),
)
DEFAULT_MODE_KEY = next(key for key, mode, _, _ in MODE_MENU if mode == DEFAULT_MODE)

# 헬스체크가 없는 서비스(= "running"이면 준비된 것으로 본다).
NO_HEALTHCHECK = {"fluentd"}

# 컨테이너가 호스트 포트를 점유하는 서비스. 다른 PC에는 로컬 MySQL·Redis·Postgres가 이미 깔려
# 있을 수 있어, compose의 bind 실패보다 **먼저** 무엇이 겹쳤는지 알려 준다(서버 포트는 콘솔 실행과의
# 충돌도 함께 걸러 낸다).
SERVICE_PORTS = {
    "mysql": 33306,
    "redis": 36379,
    "timescaledb": 5432,
    "grafana": 3000,
    "fluentd": 24220,
    "accountserver": 5160,
    "gameserver": 5247,
}

# 소스를 COPY해 굽는 이미지(= 두 서버). 포트 충돌 안내를 "콘솔 서버와의 충돌"과 "그 PC의 다른
# 프로그램"으로 가르는 기준으로도 쓴다.
SOURCE_BUILT = ("accountserver", "gameserver")

# 스키마 확인용 접속 정보(docker-compose.yml의 mysql 서비스와 같은 값. 로컬 개발용 고정 계정이다).
MYSQL_ROOT_PASSWORD = "taskbar_hero_dev"
REQUIRED_DATABASES = ("taskbar_hero_account", "taskbar_hero_game", "taskbar_hero_master")

DEFAULT_GAME_URL = "http://localhost:5247"
GAME_HEALTH_PATH = "/openapi/v1.json"
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
    """한글·기호가 깨지지 않게 콘솔 출력을 UTF-8로 맞춘다(watch-all.ps1이 하는 일과 같다)."""
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


# ── docker ──────────────────────────────────────────────────────────────
def docker(*args: str, capture: bool = True) -> subprocess.CompletedProcess:
    """docker 명령 1회 실행. 성공·실패 판정은 stderr가 아니라 반환 코드로만 한다.

    docker는 진행 상황(빌드 로그 등)을 stderr로 내기 때문에, stderr가 있으면 실패로 보는 처리
    (PowerShell 5.1의 NativeCommandError)가 정상 빌드를 실패로 만든다 — 그 함정을 구조적으로
    없애려고 이 스크립트는 반환 코드만 본다.
    """
    return subprocess.run(
        ["docker", *args],
        cwd=ROOT,
        capture_output=capture,
        text=True,
        encoding="utf-8",
        errors="replace",
    )


def docker_available() -> bool:
    """도커 데몬에 붙을 수 있는지 확인한다(Docker Desktop 미실행을 먼저 알려 준다)."""
    return docker("info").returncode == 0


def container_id(service: str) -> str | None:
    """이 컴포즈가 만든 그 서비스의 컨테이너 id(없으면 None)."""
    result = docker("compose", "ps", "-q", service)
    if result.returncode != 0:
        return None

    lines = [line.strip() for line in result.stdout.splitlines() if line.strip()]
    return lines[0] if lines else None


def container_state(service: str) -> str:
    """서비스의 준비 상태 문자열. 헬스체크가 있으면 health, 없으면 status(없는 컨테이너는 '없음')."""
    cid = container_id(service)
    if not cid:
        return "없음"

    result = docker(
        "inspect",
        "--format",
        "{{.State.Status}}|{{if .State.Health}}{{.State.Health.Status}}{{end}}",
        cid,
    )
    if result.returncode != 0:
        return "없음"

    status, _, health = result.stdout.strip().partition("|")
    return health or status


def is_ready(service: str, state: str) -> bool:
    """헬스체크가 있는 서비스는 healthy를, 없는 서비스는 running을 준비 완료로 본다."""
    if service in NO_HEALTHCHECK:
        return state == "running"
    return state == "healthy"


# ── 설정 읽기 ───────────────────────────────────────────────────────────
def read_json_setting(path: Path, keys: tuple[str, ...]) -> str | None:
    """
    appsettings 계열 JSON에서 중첩 키 하나를 문자열로 읽는다. 없거나 못 읽으면 None.

    ASP.NET Core 설정 파서는 주석을 허용하므로 파싱 전에 `//` 줄을 걷어낸다. 서버 설정을 스크립트가
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


# ── 사전 점검 ───────────────────────────────────────────────────────────
def port_taken(port: int) -> bool:
    """그 포트를 이미 누군가 쥐고 있는지 — ①접속을 걸어 보고, 안 되면 ②직접 bind를 시도해 본다.

    접속만 보면 **연결을 받아 주지 않는 점유자**를 놓친다(백로그가 찬 채 멈춘 프로세스). 그러면
    사전 점검이 조용히 통과하고 compose의 bind 실패 메시지로만 드러나므로, 접속이 안 될 때 같은
    주소에 bind를 한 번 시도한다 — 성공하면 곧바로 닫으므로 이 함수가 포트를 쥐고 있지는 않다.
    <b>SO_REUSEADDR를 켜지 않는다</b>: 윈도우에서 그 옵션은 이미 잡힌 주소에도 bind를 허용해
    (점유를 못 본 것처럼) 판정을 뒤집는다.
    """
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as probe:
        probe.settimeout(0.3)
        if probe.connect_ex(("127.0.0.1", port)) == 0:
            return True

    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as binder:
        try:
            binder.bind(("127.0.0.1", port))
        except OSError:
            return True

    return False


def check_port_conflicts(services: list[str]) -> list[tuple[str, int]]:
    """띄울 서비스의 호스트 포트를 이미 다른 프로그램이 쥐고 있으면 그 목록을 돌려준다.

    그 포트를 쥔 것이 이 컴포즈의 컨테이너면 충돌이 아니다 — 코드를 고치고 다시 배포하는 것이 이
    스크립트의 정상 사용법이라, 자기 자신이 띄운 컨테이너를 보고 막으면 재실행 자체가 불가능해진다
    (compose가 알아서 교체한다). 걸러 내는 것은 **컴포즈 밖의 점유자**다 — 콘솔로 띄운 서버
    (5160·5247)나, 그 PC의 다른 프로그램이 이 호스트 포트를 이미 쓰는 경우다. 저장소 서비스는
    흔한 기본 포트를 피해 호스트 쪽을 옮겨 두었다(MySQL 33306 · Redis 36379) — 그 PC에 이미 깔린
    로컬 MySQL(3306)·Redis(6379)와 부딪히지 않게 하려는 것이다.
    """
    busy = []
    for service, port in SERVICE_PORTS.items():
        if service not in services or not port_taken(port):
            continue

        cid = container_id(service)
        own_running = False
        if cid:
            result = docker("inspect", "--format", "{{.State.Running}}", cid)
            own_running = result.returncode == 0 and result.stdout.strip() == "true"
        if not own_running:
            busy.append((service, port))

    return busy


def prepare_event_log_dir() -> None:
    """fluentd가 :ro로 마운트하는 이벤트 로그 디렉터리를 미리 만든다.

    서버를 한 번도 안 띄웠으면 없는 폴더이고, 도커가 대신 만들면 소유·권한이 어긋날 수 있다.
    """
    event_dir = ROOT / "GameServer" / "logs" / "event"
    if not event_dir.exists():
        event_dir.mkdir(parents=True, exist_ok=True)
        say("이벤트 로그 디렉터리 생성: GameServer/logs/event", "dim")


def check_account_secret() -> bool:
    """
    AccountServer의 토큰 서명 키를 어디서든 얻을 수 있는지 확인한다.

    정상 경로는 <b>AccountServer/appsettings.json의 Security:SecretKey</b>다(저장소에 들어 있는 로컬
    개발용 기본값). 그래서 새 PC는 클론만 하면 되고 준비물이 없다. 다른 키로 돌리려면 .env나 호스트
    환경에 `Security__SecretKey`를 두면 컴포즈가 그 값을 전달해 기본값을 덮어쓴다.
    """
    if read_json_setting(ROOT / "AccountServer" / "appsettings.json", ("Security", "SecretKey")):
        return True

    env_file = ROOT / ".env"
    if env_file.exists():
        # utf-8-sig로 읽어 BOM을 떼어 낸다 — 윈도우에서 만든 .env는 BOM이 붙는 일이 흔하고, BOM은
        # 공백류가 아니라서 키가 첫 줄이면 아래 정규식이 있는 키를 없다고 판정한다.
        text = env_file.read_text(encoding="utf-8-sig", errors="replace")
        if re.search(r"^\s*Security__SecretKey\s*=\s*\S", text, re.MULTILINE):
            return True

    if os.environ.get("Security__SecretKey"):
        return True

    say("AccountServer 토큰 서명 키(Security:SecretKey)를 찾을 수 없습니다.", "err")
    say("AccountServer/appsettings.json의 Security:SecretKey를 되돌리거나,", "warn")
    say("저장소 루트 .env에 한 줄 넣으세요:  Security__SecretKey=<임의의 긴 문자열>", "warn")
    return False


# ── 기동 · 대기 ─────────────────────────────────────────────────────────
def compose_up(services: list[str], rebuild: bool) -> int:
    """docker compose up -d [--build]로 그 묶음을 띄운다(진행 로그는 그대로 흘려보낸다)."""
    args = ["compose", "up", "-d"]
    if rebuild:
        args.append("--build")
    args += services
    return docker(*args, capture=False).returncode


def compose_stop(services: list[str]) -> int:
    """그 묶음을 중지한다(볼륨·데이터는 남긴다 — 완전 초기화는 이 스크립트가 하지 않는다)."""
    return docker("compose", "stop", *services, capture=False).returncode


def wait_ready(services: list[str], timeout: int) -> tuple[dict[str, str], list[str]]:
    """모든 서비스가 준비될 때까지 기다린다. (상태 표, 아직 준비 안 된 서비스)를 돌려준다.

    `up -d`는 healthy까지 기다리지 않고 돌아오므로 여기서 확인해 둔다 — healthy 전에 요청하면
    접속 거부로 헤매게 된다.
    """
    deadline = time.monotonic() + timeout
    state: dict[str, str] = {}

    while True:
        pending: list[str] = []
        for service in services:
            state[service] = container_state(service)
            if not is_ready(service, state[service]):
                pending.append(service)

        if not pending or time.monotonic() >= deadline:
            return state, pending
        time.sleep(3)


# ── 스키마 확인 ─────────────────────────────────────────────────────────
def mysql_query(sql: str) -> tuple[bool, str]:
    """mysql 컨테이너 안에서 질의 1건을 돌린다. (성공 여부, 탭 구분 출력)."""
    result = docker(
        "compose", "exec", "-T", "mysql",
        "mysql", "-uroot", f"-p{MYSQL_ROOT_PASSWORD}", "-N", "-B", "-e", sql,
    )
    return result.returncode == 0, result.stdout.strip()


def verify_schema() -> bool:
    """
    게임·계정·마스터 DB의 테이블이 실제로 만들어졌는지 확인한다.

    스키마·마스터 데이터는 컨테이너의 **첫 기동**에만 만들어진다 — compose가 마운트한 init SQL은
    데이터 볼륨이 비어 있을 때만 실행되기 때문이다. 그래서 새 PC의 첫 기동은 저절로 되지만,
    <b>볼륨이 이미 있는데 스키마가 없는 상태</b>(중간에 끊긴 첫 기동, 다른 컴포즈가 만든 볼륨)는
    스스로 낫지 않는다. 그 상태로 넘어가면 서버가 뜬 뒤 SQL 오류로만 드러나므로 여기서 잡는다.
    """
    ok, output = mysql_query(
        "SELECT table_schema, COUNT(*) FROM information_schema.tables "
        f"WHERE table_schema IN ({', '.join(repr(db) for db in REQUIRED_DATABASES)}) "
        "GROUP BY table_schema;"
    )
    if not ok:
        say("MySQL 스키마를 확인하지 못했습니다(컨테이너 접속 실패) — 계속 진행합니다.", "warn")
        return True

    counts = {}
    for line in output.splitlines():
        parts = line.split("	")
        if len(parts) == 2 and parts[1].isdigit():
            counts[parts[0]] = int(parts[1])

    missing = [db for db in REQUIRED_DATABASES if counts.get(db, 0) == 0]
    if missing:
        say(f"MySQL에 테이블이 없습니다: {', '.join(missing)}", "err")
        say("스키마·마스터 데이터는 **데이터 볼륨이 비어 있는 첫 기동에만** 만들어집니다", "warn")
        say("(docker-compose.yml이 docs/의 스키마 SQL을 /docker-entrypoint-initdb.d 로 마운트합니다).", "warn")
        say("이미 만들어진 볼륨이 비어 있는 상태이므로, 볼륨을 지우고 다시 띄워야 합니다:", "warn")
        say("  docker compose down -v  &&  python server_up.py", "warn")
        return False

    say("MySQL 스키마 확인: " + " · ".join(f"{db.split('_')[-1]} {counts[db]}개 테이블" for db in REQUIRED_DATABASES), "dim")

    # 마스터 시드와 보스러시 시즌은 없어도 서버가 뜨긴 하므로 경고만 남긴다(원인은 대부분 같다).
    ok, output = mysql_query(
        "SELECT (SELECT COUNT(*) FROM taskbar_hero_master.item_master), "
        "(SELECT COUNT(*) FROM taskbar_hero_game.boss_rush_season WHERE status = 1);"
    )
    if ok:
        parts = output.split("	")
        if len(parts) == 2 and parts[0] == "0":
            say("마스터 데이터가 비어 있습니다 — 게임 로직이 MasterDataNotLoaded로 거부됩니다.", "warn")
        if len(parts) == 2 and parts[1] == "0":
            say("진행 중 보스러시 시즌이 없습니다 — 도전·랭킹이 닫힌 상태입니다.", "warn")

    return True


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
    """GameServer가 요청을 받을 준비가 됐는지 OpenAPI 문서로 확인한다(컨테이너 헬스체크와 같은 기준)."""
    deadline = time.monotonic() + timeout
    url = base_url.rstrip("/") + GAME_HEALTH_PATH

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
    # 첫 시즌 1행은 스키마 초기화 SQL(docs/공통/db-schema.sql)이 심으므로, 이 경고가 뜨는 경우는
    # 그 시드 없이 만들어진 DB이거나 정산이 다음 시즌을 열지 못한 상태다.
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
        say(f"GameServer가 준비되지 않았습니다: {args.game_url}{GAME_HEALTH_PATH}", "err")
        return 1

    return 0 if warm_up_rank_cache(args.game_url, admin_key, args.force_warmup) else 1


# ── 대화형 입력 ─────────────────────────────────────────────────────────
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


def ask_mode(action: str) -> str:
    """
    무엇을 띄울지(또는 중지할지) **번호로** 고르게 한다.

    `dev`/`full` 같은 내부 이름만 물으면 무엇이 포함되는지 알 수 없으므로, 보기마다 실제 서비스
    목록을 붙여 보여 준다. 번호(1·2)와 모드 이름(dev·full)을 모두 받고, 빈 입력·EOF는 기본값이다.
    """
    say(f"  무엇을 {action}까요?")
    for key, mode, label, detail in MODE_MENU:
        say(f"    {key}) {label} — {detail}", "dim")

    while True:
        try:
            answer = input(f"  선택 [{DEFAULT_MODE_KEY}] ").strip().lower()
        except EOFError:
            return DEFAULT_MODE

        if not answer:
            return DEFAULT_MODE
        for key, mode, _, _ in MODE_MENU:
            if answer in (key, mode):
                return mode
        say(f"  {' 또는 '.join(key for key, *_ in MODE_MENU)}로 답해 주세요.", "warn")


def resolve_options(args: argparse.Namespace) -> tuple[str, list[str], bool, bool]:
    """
    명시하지 않은 항목만 물어보고 (묶음, 서비스 목록, 재빌드 여부, 적재 여부)를 확정한다.

    옵션으로 준 값은 절대 묻지 않는다 — 스크립트로 호출하는 쪽이 프롬프트에 걸려 멈추지 않게
    하려는 것이고, 같은 이유로 입력이 터미널이 아니면(파이프·CI) 아무것도 묻지 않는다.
    """
    # **기본은 묻지 않는다** — 옵션 없이 실행하면 곧바로 기본값(dev · 재빌드 · 랭킹 캐시 적재)으로
    # 진행한다. 매번 쓰는 조합이 그것 하나이고, 스크립트로 호출하는 쪽도 그대로 쓸 수 있어야 한다.
    # 다른 구성을 고르고 싶을 때만 --interactive 로 메뉴를 띄운다.
    prompt = args.interactive
    if prompt:
        say("실행할 구성을 확인합니다(엔터 = 기본값).", "info")

    # ① 무엇을 띄울지 — 둘 중 하나다(dev: 게임 서버 필수만 · full: 로그까지 전부).
    mode = args.mode
    if mode is None:
        mode = ask_mode("중지할" if args.down else "띄울") if prompt else DEFAULT_MODE

    services = list(GROUPS[mode])

    # ② 서버 이미지 재빌드. 이미지에 소스가 구워져 있어 건너뛰면 옛 바이너리가 뜬다.
    if args.build:
        rebuild = True
    elif args.no_build:
        rebuild = False
    elif args.down:
        rebuild = False
    elif prompt:
        rebuild = ask_yes_no("서버 이미지를 다시 빌드할까요?(코드를 고쳤다면 필요)", True)
    else:
        rebuild = True

    # ③ 보스러시 랭킹 캐시 적재. **묻지 않는다** — GameServer를 띄웠다면 랭킹 캐시를 채우는 것이
    #    기동의 일부이고(안 채우면 랭킹 조회가 조용히 MySQL 폴백으로 돈다), 몇 번을 돌려도 안전하다.
    #    건너뛰려면 --no-warmup을 명시한다.
    warmup = not (args.no_warmup or args.down or "gameserver" not in services)

    if prompt:
        print()

    return mode, services, rebuild, warmup


# ── 결과 안내 ───────────────────────────────────────────────────────────
def print_summary(services: list[str], state: dict[str, str], pending: list[str]) -> None:
    """서비스별 준비 상태와, 실제로 띄운 것의 접속 주소만 안내한다."""
    print()
    for service in services:
        value = state.get(service, "없음")
        ready = is_ready(service, value)
        say(f"  {'OK  ' if ready else '..  '}{service:<14} {value}", "ok" if ready else "warn")

    if pending:
        print()
        say(f"아직 준비되지 않은 서비스: {', '.join(pending)}", "warn")
        say(f"로그를 확인하세요:  docker compose logs -f {' '.join(pending)}", "warn")

    print()
    addresses = [
        ("mysql", "MySQL        : 127.0.0.1:33306 (root / taskbar_hero_dev)"),
        ("redis", "Redis        : 127.0.0.1:36379"),
        ("timescaledb", "logdb        : 127.0.0.1:5432 (fluentd / logdb)"),
        ("grafana", "Grafana      : http://localhost:3000"),
        ("accountserver", "AccountServer: http://localhost:5160/swagger"),
        ("gameserver", "GameServer   : http://localhost:5247/swagger"),
    ]
    for service, line in addresses:
        if service in services:
            say(f"  {line}", "dim")


# ── 진입점 ──────────────────────────────────────────────────────────────
def parse_args() -> argparse.Namespace:
    """명령행 인자를 읽는다(기본은 --mode all: 로그 파이프라인이 끝까지 도는 완전한 환경)."""
    parser = argparse.ArgumentParser(
        prog="server_up.py",
        description="로컬 개발 환경 부트스트랩(컨테이너 기동 + 보스러시 랭킹 캐시 최초 적재).",
    )
    parser.add_argument("--mode", choices=list(GROUPS), default=None,
                        help=f"dev(게임만) 또는 full(로그 파이프라인까지). 생략하면 {DEFAULT_MODE}")
    parser.add_argument("-i", "--interactive", action="store_true",
                        help="무엇을 띄울지·재빌드할지 메뉴로 물어본다(기본은 묻지 않고 진행)")
    parser.add_argument("--build", action="store_true", help="서버 이미지를 다시 빌드한다(기본 동작이라 -i로 물을 때만 의미가 있다)")
    parser.add_argument("--no-build", action="store_true", help="서버 이미지 재빌드를 건너뛴다")
    parser.add_argument("--down", action="store_true", help="그 묶음을 중지한다(볼륨·데이터는 남는다)")
    parser.add_argument("--warmup-only", action="store_true", help="컨테이너는 건드리지 않고 랭킹 캐시 적재만 지시한다")
    parser.add_argument("--no-warmup", action="store_true", help="랭킹 캐시 적재 단계를 건너뛴다")
    parser.add_argument("--force-warmup", action="store_true", help="리더보드가 이미 채워져 있어도 다시 적재한다")
    parser.add_argument("--admin-key", help="관리 API 키(기본: TASKBAR_HERO_ADMIN_KEY -> appsettings.json)")
    parser.add_argument("--game-url", default=DEFAULT_GAME_URL, help=f"GameServer 주소(기본 {DEFAULT_GAME_URL})")
    parser.add_argument("--timeout", type=int, default=180, help="준비 대기 상한(초, 기본 180)")
    return parser.parse_args()


def main() -> int:
    """사전 점검 -> 컨테이너 기동 -> 준비 대기 -> 랭킹 캐시 적재 순으로 진행한다."""
    global _USE_COLOR

    setup_console()
    args = parse_args()
    _USE_COLOR = sys.stdout.isatty()

    # 적재만 지시하는 경로(서버를 콘솔로 띄웠을 때). 도커를 아예 건드리지 않는다.
    if args.warmup_only:
        return run_warmup(args)

    if not docker_available():
        say("도커에 연결할 수 없습니다. Docker Desktop이 실행 중인지 확인하세요.", "err")
        return 1

    mode, services, rebuild, warmup = resolve_options(args)

    if args.down:
        say(f"[{mode}] 중지: {' · '.join(services)}", "info")
        code = compose_stop(services)
        if code != 0:
            return code
        say("중지 완료(볼륨·데이터는 그대로입니다).", "ok")
        return 0

    # ── 사전 점검 — 막힐 것이 확실한 조건은 컨테이너를 띄우기 전에 알려 준다 ──
    if "fluentd" in services:
        prepare_event_log_dir()

    if "accountserver" in services and not check_account_secret():
        return 1

    busy = check_port_conflicts(services)
    if busy:
        say(f"이미 사용 중인 포트: {', '.join(f'{port}({service})' for service, port in busy)}", "err")
        # 무엇이 쥐고 있느냐에 따라 할 일이 다르다 — 서버 포트는 콘솔 실행과의 충돌이고,
        # 나머지는 그 PC에 이미 깔린 로컬 MySQL·Redis·Postgres 같은 별개 프로그램이다.
        if any(service in SOURCE_BUILT for service, _ in busy):
            say("콘솔로 띄운 서버(watch-all.ps1 · dotnet run)가 있으면 먼저 종료하세요.", "warn")
            say("서버는 콘솔로 계속 쓰고 의존 서비스만 띄우려면:  docker compose up -d mysql redis", "warn")
        if any(service not in SOURCE_BUILT for service, _ in busy):
            say("그 포트를 쓰는 로컬 프로그램(로컬 MySQL·Redis·Postgres 등)을 멈추거나,", "warn")
            say("docker-compose.yml의 해당 서비스 ports에서 호스트 쪽 번호를 바꾸세요.", "warn")
        return 1

    # ── 기동 ──
    say(f"[{mode}] 기동: {' · '.join(services)}", "info")

    # 서버 이미지에는 소스가 구워져 있어, 재빌드를 건너뛰면 고친 코드가 아니라 옛 바이너리가 뜬다
    # (그 상태로 테스트하면 무엇을 검증한 것인지 알 수 없다).
    if rebuild:
        say("서버 이미지를 다시 빌드합니다(소스가 이미지에 구워집니다). 처음이면 몇 분 걸립니다.", "dim")
    else:
        say("이미지 재빌드 없음: 기존 이미지를 그대로 씁니다 — 코드를 고쳤다면 반영되지 않습니다.", "warn")

    code = compose_up(services, rebuild)
    if code != 0:
        say("기동 실패. 위 출력을 확인하세요.", "err")
        return code

    # ── 준비 대기 ──
    say("준비 상태 확인 중...", "dim")
    state, pending = wait_ready(services, args.timeout)
    print_summary(services, state, pending)

    # ── 스키마 확인 ──
    #   mysql이 healthy면 init SQL은 이미 끝나 있다(healthcheck가 TCP로 검사하고, init을 돌리는
    #   임시 서버는 소켓만 열기 때문). 그래도 "볼륨은 있는데 스키마가 없는" 상태는 스스로 낫지
    #   않으므로 여기서 실제로 테이블을 세어 본다.
    print()
    if "mysql" not in pending and not verify_schema():
        return 1

    # ── 보스러시 랭킹 캐시 최초 적재 ──
    print()
    if not warmup:
        say("랭킹 캐시 적재를 건너뜁니다(필요해지면: python server_up.py --warmup-only).", "dim")
    elif "gameserver" in pending:
        say("GameServer가 준비되지 않아 랭킹 캐시를 적재하지 못했습니다.", "err")
        return 1
    else:
        code = run_warmup(args)
        if code != 0:
            return code

    print()
    say("코드를 고치면 이 스크립트를 다시 실행하세요(이미지 재빌드 후 재기동).", "dim")
    if "fluentd" in services:
        say("이벤트 로그는 GameServer/logs/event 에 쌓이고 fluentd가 logdb로 옮깁니다.", "dim")

    return 1 if pending else 0


if __name__ == "__main__":
    sys.exit(main())
