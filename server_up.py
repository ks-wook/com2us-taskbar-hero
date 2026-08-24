#!/usr/bin/env python
"""로컬 개발 환경 부트스트랩 — 컨테이너 기동부터 보스러시 랭킹 캐시 최초 적재까지.

사용법 (저장소 루트에서 실행한다 — docker compose가 이 위치를 기준으로 돈다)
    python server_up.py                    # all  : 저장소+로그 파이프라인+서버 2개 + 랭킹 캐시 적재
    python server_up.py --mode infra       # infra: 서버를 뺀 나머지(서버는 watch-all.ps1로 콘솔 기동)
    python server_up.py --mode game        # game : mysql · redis 만
    python server_up.py --mode log         # log  : timescaledb · fluentd · grafana 만
    python server_up.py --warmup-only      # 이미 떠 있는 GameServer에 랭킹 캐시 적재만 지시
    python server_up.py --down --mode all  # 그 묶음을 중지(볼륨·데이터는 남는다)

묶음
    all   = infra + accountserver · gameserver   (로그 파이프라인이 끝까지 도는 완전한 환경)
    infra = game + log                           (서버를 콘솔로 띄울 때. watch-all.ps1과 짝)
    game  = mysql · redis                        (게임 DB·캐시만)
    log   = timescaledb · fluentd · grafana      (로그 저장·조회만)

주요 옵션
    --no-build      서버 이미지 재빌드를 건너뛴다(코드를 안 고쳤을 때만).
    --build         서버가 없는 묶음에서도 이미지를 새로 빌드한다(fluentd 등).
    --force-warmup  리더보드가 이미 채워져 있어도 MySQL에서 다시 적재한다.
    --no-warmup     랭킹 캐시 적재 단계를 건너뛴다.
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
    · all 과 watch-all.ps1 은 같은 5160·5247 포트를 쓰므로 동시에 띄울 수 없다.
      핫 리로드로 개발할 때는 --mode infra + watch-all.ps1 을 쓰고, 서버가 뜬 뒤
      python server_up.py --warmup-only 로 랭킹 캐시를 적재한다.
    · 서버가 포함되면 이미지를 자동으로 다시 빌드한다. 서버 이미지는 소스를 COPY해 굽기 때문에
      (핫 리로드 없음) 재빌드를 건너뛰면 옛 바이너리가 그대로 뜬다.
    · 데이터까지 지우는 완전 초기화는 이 스크립트가 하지 않는다(되돌릴 수 없어서다):
      docker compose down -v
      logdb 스키마(docs/공통/logdb-schema.sql)는 빈 볼륨에서만 적용되므로, 스키마를 고쳐 다시
      반영하려면 그 초기화가 필요하다.
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
GROUP_GAME = ["mysql", "redis"]
GROUP_LOG = ["timescaledb", "fluentd", "grafana"]
GROUPS = {
    "game": GROUP_GAME,
    "log": GROUP_LOG,
    "infra": GROUP_GAME + GROUP_LOG,
    "all": GROUP_GAME + GROUP_LOG + ["accountserver", "gameserver"],
}

# 헬스체크가 없는 서비스(= "running"이면 준비된 것으로 본다).
NO_HEALTHCHECK = {"fluentd"}

# 컨테이너로 띄울 때 호스트 포트를 점유하는 서비스(콘솔 실행과 충돌 확인용).
SERVER_PORTS = {"accountserver": 5160, "gameserver": 5247}

# 소스를 COPY해 굽는 이미지. 이 중 하나라도 띄우면 기본으로 다시 빌드한다.
SOURCE_BUILT = ("accountserver", "gameserver")

DEFAULT_GAME_URL = "http://localhost:5247"
GAME_HEALTH_PATH = "/openapi/v1.json"
WARMUP_PATH = "/api/admin/boss-rush/rank/warmup"

# 서버가 돌려주는 워밍업 결과 상태값(GameServer/Constants.cs 의 RankWarmupStatus 와 같은 문자열).
WARMUP_CACHE_UNAVAILABLE = "cache-unavailable"

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


# ── 사전 점검 ───────────────────────────────────────────────────────────
def port_listening(port: int) -> bool:
    """호스트 루프백의 그 포트를 누군가 듣고 있는지."""
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
        sock.settimeout(0.3)
        return sock.connect_ex(("127.0.0.1", port)) == 0


def check_port_conflicts(services: list[str]) -> list[str]:
    """콘솔로 띄운 서버가 쥔 포트를 찾아 목록으로 돌려준다.

    그 포트를 쥔 것이 이 컴포즈의 컨테이너면 충돌이 아니다 — 코드를 고치고 다시 배포하는 것이 이
    스크립트의 정상 사용법이라, 자기 자신이 띄운 컨테이너를 보고 막으면 재실행 자체가 불가능해진다
    (compose가 알아서 교체한다).
    """
    busy = []
    for service, port in SERVER_PORTS.items():
        if service not in services or not port_listening(port):
            continue

        cid = container_id(service)
        own_running = False
        if cid:
            result = docker("inspect", "--format", "{{.State.Running}}", cid)
            own_running = result.returncode == 0 and result.stdout.strip() == "true"
        if not own_running:
            busy.append(f"{port}({service})")

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
    """AccountServer 컨테이너가 .env에서 받는 토큰 서명 키가 있는지 확인한다.

    없으면 compose가 그 자리에서 멈추므로(`:?` 지정), 무엇을 넣어야 하는지 먼저 알려 준다.
    """
    env_file = ROOT / ".env"
    if env_file.exists():
        text = env_file.read_text(encoding="utf-8", errors="replace")
        if re.search(r"^\s*ACCOUNT_SECRET_KEY\s*=\s*\S", text, re.MULTILINE):
            return True

    say(".env에 ACCOUNT_SECRET_KEY가 없습니다(AccountServer 토큰 서명 키).", "err")
    say("저장소 루트 .env에 한 줄 넣으세요:  ACCOUNT_SECRET_KEY=<임의의 긴 문자열>", "warn")
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

    settings = ROOT / "GameServer" / "appsettings.json"
    if not settings.exists():
        return None

    try:
        # appsettings.json은 주석을 허용하므로(ASP.NET Core 설정 파서) 파싱 전에 걷어낸다.
        text = re.sub(r"^\s*//.*$", "", settings.read_text(encoding="utf-8-sig"), flags=re.MULTILINE)
        return json.loads(text).get("Admin", {}).get("ApiKey") or None
    except (OSError, ValueError):
        return None


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
        ("mysql", "MySQL        : 127.0.0.1:3306 (root / taskbar_hero_dev)"),
        ("redis", "Redis        : 127.0.0.1:6379"),
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
    parser.add_argument("--mode", choices=sorted(GROUPS), default="all", help="띄울 묶음(기본 all)")
    parser.add_argument("--build", action="store_true", help="서버가 없는 묶음에서도 이미지를 새로 빌드한다")
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

    services = GROUPS[args.mode]
    has_servers = any(service in SOURCE_BUILT for service in services)

    if args.down:
        say(f"[{args.mode}] 중지: {' · '.join(services)}", "info")
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
        say(f"이미 사용 중인 포트: {', '.join(busy)}", "err")
        say("콘솔로 띄운 서버가 있다면 먼저 종료하거나, --mode infra 로 의존 서비스만 띄우세요.", "warn")
        return 1

    # ── 기동 ──
    say(f"[{args.mode}] 기동: {' · '.join(services)}", "info")

    # 서버가 끼면 기본으로 재빌드한다 — 이미지에 소스가 구워져 있어, 건너뛰면 고친 코드가 아니라
    # 옛 바이너리가 뜬다(그 상태로 테스트하면 무엇을 검증한 것인지 알 수 없다).
    rebuild = args.build or (has_servers and not args.no_build)
    if rebuild and has_servers:
        say("서버 이미지를 다시 빌드합니다(소스가 이미지에 구워집니다). 처음이면 몇 분 걸립니다.", "dim")
    elif has_servers:
        say("--no-build: 기존 이미지를 그대로 씁니다 — 코드를 고쳤다면 반영되지 않습니다.", "warn")

    code = compose_up(services, rebuild)
    if code != 0:
        say("기동 실패. 위 출력을 확인하세요.", "err")
        return code

    # ── 준비 대기 ──
    say("준비 상태 확인 중...", "dim")
    state, pending = wait_ready(services, args.timeout)
    print_summary(services, state, pending)

    # ── 보스러시 랭킹 캐시 최초 적재 ──
    print()
    if args.no_warmup:
        say("--no-warmup: 랭킹 캐시 적재를 건너뜁니다.", "dim")
    elif "gameserver" not in services:
        say("이 묶음에는 GameServer가 없어 랭킹 캐시 적재를 건너뜁니다.", "dim")
        say("서버를 콘솔로 띄운 뒤:  python server_up.py --warmup-only", "dim")
    elif "gameserver" in pending:
        say("GameServer가 준비되지 않아 랭킹 캐시를 적재하지 못했습니다.", "err")
        return 1
    else:
        code = run_warmup(args)
        if code != 0:
            return code

    print()
    if has_servers:
        say("코드를 고치면 이 스크립트를 다시 실행하세요(이미지 재빌드 후 재기동).", "dim")
        if "fluentd" in services:
            say("이벤트 로그는 GameServer/logs/event 에 쌓이고 fluentd가 logdb로 옮깁니다.", "dim")
    else:
        say("서버는 콘솔로 띄웁니다:  ./watch-all.ps1", "dim")

    return 1 if pending else 0


if __name__ == "__main__":
    sys.exit(main())
