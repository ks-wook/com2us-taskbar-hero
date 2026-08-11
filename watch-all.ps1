# 계정 서버 + 게임 서버를 각각 dotnet watch(핫 리로드)로 동시에 실행한다.
#   사용법:  ./watch-all.ps1
#   dotnet watch는 프로젝트 1개 대상이라, 두 서버를 각자 새 콘솔 창으로 띄운다.
#   실행 전 의존 서비스(MySQL·Redis)가 꺼져 있으면 자동으로 켠다.
#
#   ※ 이 파일은 반드시 UTF-8 with BOM으로 저장한다(Windows PowerShell 5.1이 한글 리터럴을
#      올바르게 파싱하도록). 아래 인코딩 설정과 함께 콘솔 한글 출력 깨짐을 방지한다.

# 콘솔 UTF-8 출력 설정(Write-Host 한글 깨짐 방지)
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch {}
$OutputEncoding = [System.Text.Encoding]::UTF8

$root = $PSScriptRoot

# ── 의존 서비스(MySQL·Redis) — docker-compose ──────────────────────────
#   Redis는 컨테이너(taskbar-hero-redis)로 띄운다. 단, 저장소 내 Windows 바이너리
#   Redis를 이미 6379로 띄워 둔 경우에는 포트가 충돌하므로 compose의 redis는 건드리지 않는다.
$composeFile = Join-Path $root "docker-compose.yml"

$redisContainerUp = docker ps --filter "name=taskbar-hero-redis" --filter "status=running" -q
$redisListening = [bool](Get-NetTCPConnection -LocalPort 6379 -State Listen -ErrorAction SilentlyContinue)
$services = @("mysql")
if ([string]::IsNullOrWhiteSpace($redisContainerUp) -and -not $redisListening) {
    $services += "redis"
}
elseif ([string]::IsNullOrWhiteSpace($redisContainerUp)) {
    Write-Host "Redis(6379)가 컨테이너 밖에서 이미 실행 중 — compose redis는 건너뜁니다." -ForegroundColor DarkGray
}

Write-Host "의존 서비스 확인/기동: $($services -join ', ')" -ForegroundColor DarkGray
docker compose -f $composeFile up -d @services | Out-Null

# 헬스체크가 붙은 서비스만 healthy를 기다린다(위에서 건너뛴 redis는 제외).
foreach ($c in @("taskbar-hero-mysql") + $(if ($services -contains "redis" -or -not [string]::IsNullOrWhiteSpace($redisContainerUp)) { @("taskbar-hero-redis") } else { @() })) {
    $health = $null
    for ($i = 0; $i -lt 30; $i++) {
        $health = docker inspect --format '{{.State.Health.Status}}' $c 2>$null
        if ($health -eq "healthy") { break }
        Start-Sleep -Seconds 2
    }
    if ($health -eq "healthy") { Write-Host "$c 준비 완료." -ForegroundColor Green }
    else { Write-Host "$c 가 아직 healthy가 아닙니다(계속 진행). 상태: $health" -ForegroundColor Yellow }
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
