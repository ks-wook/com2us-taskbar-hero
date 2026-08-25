# 계정 서버 + 게임 서버를 각각 dotnet watch(핫 리로드)로 새 콘솔 창에 띄운다.
#   사용법:  ./watch-all.ps1
#   dotnet watch는 프로젝트 1개 대상이라, 두 서버를 각자 새 콘솔 창으로 띄운다.
#
#   이 스크립트는 **서버만** 띄운다. 의존 서비스는 docker-compose가 담당한다:
#       docker compose up -d mysql redis
#   서버가 뜬 뒤 보스러시 랭킹 캐시를 Redis에 적재한다(서버가 스스로 하지 않는다):
#       python server_up_with_docker.py --warmup-only
#
#   ※ `python server_up.py`가 같은 일(두 서버 watch 기동)에 사전 점검·선빌드·스키마 확인·랭킹 캐시
#      적재까지 묶어 처리한다(로컬 MySQL·Redis 전제. 컨테이너 DB에 붙일 때는
#      --mysql-port 33306 --redis 127.0.0.1:36379). 이 스크립트는 그 이전부터 쓰던 최소 경로다.
#   두 서버까지 컨테이너로 돌리고 싶으면 이 스크립트 대신 `python server_up_with_docker.py`를 쓴다
#   (같은 5160·5247 포트를 쓰므로 컨테이너 서버와 이 스크립트는 동시에 띄울 수 없다).
#
#   ※ 이 파일은 반드시 UTF-8 with BOM으로 저장한다(Windows PowerShell 5.1이 한글 리터럴을
#      올바르게 파싱하도록). 아래 인코딩 설정과 함께 콘솔 한글 출력 깨짐을 방지한다.

# 콘솔 UTF-8 출력 설정(Write-Host 한글 깨짐 방지)
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch {}
$OutputEncoding = [System.Text.Encoding]::UTF8

$root = $PSScriptRoot

function Test-PortListening([int]$Port) {
    return [bool](Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)
}

# ── 서버 포트 선점 확인 ────────────────────────────────────────────────
#   컨테이너 서버(docker compose)가 이미 5160·5247을 쓰고 있으면 watch가 기동 직후
#   바인딩 실패로 죽는다. 원인이 콘솔 창에서만 스쳐 지나가므로 여기서 먼저 막는다.
$busy = @()
if (Test-PortListening 5160) { $busy += "5160(AccountServer)" }
if (Test-PortListening 5247) { $busy += "5247(GameServer)" }
if ($busy.Count -gt 0) {
    Write-Host "이미 사용 중인 포트: $($busy -join ', ')" -ForegroundColor Red
    Write-Host "컨테이너 서버가 떠 있다면 먼저 내리세요: docker compose stop accountserver gameserver" -ForegroundColor Yellow
    exit 1
}

# ── 의존 서비스(MySQL·Redis) 확인 — 여기서 띄우지는 않는다 ─────────────
#   포트가 닫혀 있어도 진행은 한다(서버는 뜨고 DB 접근 시점에 실패한다). 대신 원인을
#   찾느라 헤매지 않도록 무엇이 없는지, 무엇을 실행하면 되는지 알려 준다.
$missing = @()
if (-not (Test-PortListening 33306)) { $missing += "MySQL(33306)" }
if (-not (Test-PortListening 36379)) { $missing += "Redis(36379)" }
if ($missing.Count -gt 0) {
    Write-Host "의존 서비스가 감지되지 않습니다: $($missing -join ', ')" -ForegroundColor Yellow
    Write-Host "먼저 실행하세요: docker compose up -d mysql redis" -ForegroundColor Yellow
}
else {
    Write-Host "의존 서비스 확인: MySQL(33306) · Redis(36379) 응답 중." -ForegroundColor DarkGray
}

# ── 선(先) 빌드: 두 watch를 띄우기 전에 솔루션을 직렬로 한 번 빌드 ──────
#   두 서버는 TaskbarHero.Common(net10.0)을 ProjectReference로 참조하는데,
#   Common의 obj/bin은 (Unity 로컬 패키지 격리를 위해) artifacts/로 재배치되어
#   두 서버가 '동일한' artifacts\TaskbarHero.Common\obj\Debug\net10.0\ 폴더를 공유한다.
#   아래 두 dotnet watch를 Start-Process로 동시에 띄우면 두 MSBuild 프로세스가
#   그 공유 폴더에 Common을 동시 빌드하다 파일 잠금(MSB3713 / CS2012)이 발생한다.
#   (최초 실행 때만 재현되고 재실행 때 사라지는 이유: Common이 최신이면 재기록을
#    건너뛰어 충돌이 없기 때문.) 여기서 직렬로 한 번 빌드해 Common을 최신 상태로
#   만들어 두면, 이후 두 watch의 빌드는 no-op이 되어 경합이 사라진다.
Write-Host "선 빌드 중(공유 출력 경합 방지)..." -ForegroundColor DarkGray
dotnet build (Join-Path $root "com2us-taskbar-hero.slnx") -v q
if ($LASTEXITCODE -ne 0) {
    Write-Host "선 빌드 실패. 위 오류를 확인하세요(watch는 계속 시도합니다)." -ForegroundColor Yellow
}
else {
    Write-Host "선 빌드 완료." -ForegroundColor Green
}

# ── 두 서버를 각각 dotnet watch로 실행(각자 새 콘솔 창) ─────────────────
Write-Host "AccountServer(:5160) · GameServer(:5247) server start..." -ForegroundColor Cyan

Start-Process -FilePath "dotnet" `
    -ArgumentList "watch run --launch-profile http" `
    -WorkingDirectory (Join-Path $root "AccountServer")

Start-Process -FilePath "dotnet" `
    -ArgumentList "watch run --launch-profile http" `
    -WorkingDirectory (Join-Path $root "GameServer")

Write-Host "  - AccountServer: http://localhost:5160/swagger" -ForegroundColor DarkGray
Write-Host "  - GameServer   : http://localhost:5247/swagger" -ForegroundColor DarkGray
