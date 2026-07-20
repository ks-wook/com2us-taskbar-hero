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

# ── MySQL (docker-compose) ─────────────────────────────────────────────
$mysqlUp = docker ps --filter "name=taskbar-hero-mysql" --filter "status=running" -q
if ([string]::IsNullOrWhiteSpace($mysqlUp)) {
    Write-Host "MySQL 컨테이너가 꺼져 있어 시작합니다..." -ForegroundColor Yellow
    docker compose -f (Join-Path $root "docker-compose.yml") up -d | Out-Null

    Write-Host "MySQL 준비 대기 중(healthy)..." -ForegroundColor DarkGray
    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep -Seconds 2
        $health = docker inspect --format '{{.State.Health.Status}}' taskbar-hero-mysql 2>$null
        if ($health -eq "healthy") { break }
    }
    if ($health -eq "healthy") { Write-Host "MySQL 준비 완료." -ForegroundColor Green }
    else { Write-Host "MySQL이 아직 healthy가 아닙니다(계속 진행). 상태: $health" -ForegroundColor Yellow }
}
else {
    Write-Host "MySQL 컨테이너 이미 실행 중." -ForegroundColor DarkGray
}

# ── Redis (프로젝트 경로, 127.0.0.1:6379) ──────────────────────────────
$redisListening = [bool](Get-NetTCPConnection -LocalPort 6379 -State Listen -ErrorAction SilentlyContinue)
if (-not $redisListening) {
    $redisDir = Join-Path $root "Redis-8.8.0-Windows-x64-cygwin-with-Service"
    Write-Host "Redis(6379)가 꺼져 있어 프로젝트 경로 Redis를 시작합니다..." -ForegroundColor Yellow
    Start-Process -FilePath (Join-Path $redisDir "redis-server.exe") `
        -ArgumentList "redis.conf" -WorkingDirectory $redisDir -WindowStyle Hidden

    for ($i = 0; $i -lt 10; $i++) {
        Start-Sleep -Seconds 1
        if (Get-NetTCPConnection -LocalPort 6379 -State Listen -ErrorAction SilentlyContinue) { break }
    }
    if (Get-NetTCPConnection -LocalPort 6379 -State Listen -ErrorAction SilentlyContinue) {
        Write-Host "Redis 준비 완료." -ForegroundColor Green
    }
    else { Write-Host "Redis가 아직 리스닝하지 않습니다(계속 진행)." -ForegroundColor Yellow }
}
else {
    Write-Host "Redis(6379) 이미 실행 중." -ForegroundColor DarkGray
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
