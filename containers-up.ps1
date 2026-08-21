# docker-compose의 컨테이너를 용도별로 묶어 띄운다. **서버까지 컨테이너로 띄우는 것이 기본**이다.
#   사용법:  ./containers-up.ps1 [-Mode all|infra|game|log] [-NoBuild] [-Build] [-Down]
#
#   -Mode all (기본)   : mysql · redis · timescaledb · fluentd · grafana
#                        + accountserver · gameserver
#                        = **로그 파이프라인이 끝까지 도는 완전한 환경**
#                          (서버 → 이벤트 JSON → fluentd → logdb → Grafana).
#   -Mode infra        : 서버를 뺀 나머지 전부. 서버를 콘솔로 띄울 때(watch-all.ps1과 짝).
#   -Mode game         : mysql · redis                   (게임 DB·캐시만)
#   -Mode log          : timescaledb · fluentd · grafana (로그 저장·조회만)
#   -NoBuild           : 서버 이미지 재빌드를 건너뛴다(코드를 안 고쳤을 때만).
#   -Build             : 서버가 없는 묶음에서도 이미지를 새로 빌드한다(fluentd 등).
#   -Down              : 그 묶음을 내린다(볼륨·데이터는 남는다).
#
#   ※ **서버가 포함되면 이미지를 자동으로 다시 빌드한다.** 서버 이미지는 소스를 COPY해 굽기
#      때문에(핫 리로드 없음) 재빌드를 건너뛰면 **옛 바이너리가 그대로 뜬다** — 고친 코드가
#      반영된 줄 알고 옛 동작을 검증하게 되는 것이 이 환경에서 가장 비싼 실수다.
#
#   ※ `all`과 `watch-all.ps1`은 **같은 5160·5247 포트를 쓰므로 동시에 띄울 수 없다.**
#      핫 리로드로 개발할 때는 `-Mode infra` + watch-all.ps1을 쓴다. 그 조합에서도 로그 수집은
#      그대로 동작한다 — gameserver 컨테이너가 이벤트 로그 디렉터리를 호스트와 공유하므로
#      fluentd는 서버를 어떻게 띄우든 **같은 폴더**를 tail 한다.
#
#   ※ 데이터까지 지우는 완전 초기화는 이 스크립트가 하지 않는다(되돌릴 수 없어서다).
#      필요하면 직접 실행한다:  docker compose down -v
#      logdb 스키마(docs/공통/logdb-schema.sql)는 **빈 볼륨에서만** 적용되므로,
#      스키마를 고쳐 다시 반영하려면 그 초기화가 필요하다.
#
#   ※ 이 파일은 반드시 UTF-8 with BOM으로 저장한다(Windows PowerShell 5.1이 한글 리터럴을
#      올바르게 파싱하도록). watch-all.ps1과 같은 이유다.

[CmdletBinding()]
param(
    [ValidateSet('all', 'infra', 'game', 'log')]
    [string]$Mode = 'all',
    [switch]$NoBuild,
    [switch]$Build,
    [switch]$Down
)

# 콘솔 UTF-8 출력 설정(Write-Host 한글 깨짐 방지)
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch {}
$OutputEncoding = [System.Text.Encoding]::UTF8

# 이 스크립트의 본체는 docker(네이티브 exe) 호출이고, docker는 **진행 상황을 stderr로** 낸다
# ("Image ... Building" 등). Windows PowerShell 5.1은 ErrorActionPreference='Stop'일 때 그 stderr를
# 종료 오류(NativeCommandError)로 바꿔 버리므로, 정상 빌드가 실패로 중단된다.
# 성공/실패 판정은 stderr가 아니라 **$LASTEXITCODE**로 한다(호출 지점마다 확인).
$ErrorActionPreference = 'Continue'
Push-Location $PSScriptRoot

try {
    # ── 묶음 정의 ──────────────────────────────────────────────────────────
    #   compose의 depends_on이 순서를 보장하므로 여기서는 "무엇을 띄울지"만 정한다.
    $groups = @{
        game  = @('mysql', 'redis')
        log   = @('timescaledb', 'fluentd', 'grafana')
    }
    $groups['infra'] = $groups['game'] + $groups['log']
    $groups['all']   = $groups['infra'] + @('accountserver', 'gameserver')

    $services = $groups[$Mode]

    # 헬스체크가 없는 서비스(= "running"이면 준비된 것으로 본다).
    $noHealthcheck = @('fluentd')

    # 컨테이너로 띄울 때 호스트 포트를 점유하는 서비스(콘솔 실행과 충돌 확인용).
    $serverPorts = @{ accountserver = 5160; gameserver = 5247 }

    # 소스를 COPY해 굽는 이미지. 이 중 하나라도 띄우면 기본으로 다시 빌드한다.
    $sourceBuiltServices = @('accountserver', 'gameserver')
    $hasServers = @($services | Where-Object { $sourceBuiltServices -contains $_ }).Count -gt 0

    # ── 도커 확인 ──────────────────────────────────────────────────────────
    docker info *> $null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "도커에 연결할 수 없습니다. Docker Desktop이 실행 중인지 확인하세요." -ForegroundColor Red
        exit 1
    }

    # ── 내리기 ─────────────────────────────────────────────────────────────
    if ($Down) {
        Write-Host "[$Mode] 중지: $($services -join ' · ')" -ForegroundColor Cyan
        docker compose stop @services
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        Write-Host "중지 완료(볼륨·데이터는 그대로입니다)." -ForegroundColor Green
        exit 0
    }

    # ── 사전 점검 ──────────────────────────────────────────────────────────
    #   막힐 것이 확실한 조건은 컨테이너를 띄우기 전에 알려 준다 — compose가 뱉는
    #   오류만 보고 원인을 되짚는 것보다 낫다.

    # fluentd가 :ro로 마운트하는 이벤트 로그 디렉터리. 서버를 한 번도 안 띄웠으면 없다.
    # 도커가 대신 만들면 소유·권한이 어긋날 수 있어 여기서 먼저 만든다.
    if ($services -contains 'fluentd') {
        $eventDir = Join-Path $PSScriptRoot 'GameServer\logs\event'
        if (-not (Test-Path $eventDir)) {
            New-Item -ItemType Directory -Path $eventDir -Force | Out-Null
            Write-Host "이벤트 로그 디렉터리 생성: GameServer\logs\event" -ForegroundColor DarkGray
        }
    }

    # AccountServer 컨테이너는 토큰 서명 키를 .env에서 받는다(compose가 `:?`로 강제한다).
    # 없으면 compose가 그 자리에서 멈추므로, 무엇을 넣어야 하는지 먼저 알려 준다.
    if ($services -contains 'accountserver') {
        $envFile = Join-Path $PSScriptRoot '.env'
        $hasKey = (Test-Path $envFile) -and
                  ((Get-Content $envFile) -match '^\s*ACCOUNT_SECRET_KEY\s*=\s*\S')
        if (-not $hasKey) {
            Write-Host ".env에 ACCOUNT_SECRET_KEY가 없습니다(AccountServer 토큰 서명 키)." -ForegroundColor Red
            Write-Host "저장소 루트 .env에 한 줄 넣으세요:  ACCOUNT_SECRET_KEY=<임의의 긴 문자열>" -ForegroundColor Yellow
            exit 1
        }
    }

    # 콘솔로 띄운 서버(watch-all.ps1 / dotnet run)가 이미 그 포트를 쓰고 있으면 컨테이너가 바인딩에
    # 실패한다. 단 **그 포트를 쥔 것이 이 컴포즈의 컨테이너면 충돌이 아니다** — 코드를 고치고 다시
    # 배포하는 것이 이 스크립트의 정상 사용법이라, 자기 자신이 띄운 컨테이너를 보고 막으면
    # 재실행 자체가 불가능해진다(compose가 알아서 교체한다).
    $busy = @()
    foreach ($svc in $serverPorts.Keys) {
        if ($services -notcontains $svc) { continue }
        $port = $serverPorts[$svc]
        if (-not (Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue)) { continue }

        $ownId = (docker compose ps -q $svc 2>$null | Select-Object -First 1)
        $ownRunning = $false
        if ($ownId) {
            $ownRunning = ((docker inspect --format '{{.State.Running}}' $ownId 2>$null) -eq 'true')
        }
        if (-not $ownRunning) { $busy += "$port($svc)" }
    }
    if ($busy.Count -gt 0) {
        Write-Host "이미 사용 중인 포트: $($busy -join ', ')" -ForegroundColor Red
        Write-Host "콘솔로 띄운 서버가 있다면 먼저 종료하거나, -Mode infra 로 의존 서비스만 띄우세요." -ForegroundColor Yellow
        exit 1
    }

    # ── 기동 ───────────────────────────────────────────────────────────────
    Write-Host "[$Mode] 기동: $($services -join ' · ')" -ForegroundColor Cyan

    # 서버가 끼면 기본으로 재빌드한다 — 이미지에 소스가 구워져 있어, 건너뛰면 고친 코드가
    # 아니라 **옛 바이너리**가 뜬다(그 상태로 테스트하면 무엇을 검증한 것인지 알 수 없다).
    $rebuild = $Build -or ($hasServers -and -not $NoBuild)

    $upArgs = @('compose', 'up', '-d')
    if ($rebuild) {
        $upArgs += '--build'
        if ($hasServers) {
            Write-Host "서버 이미지를 다시 빌드합니다(소스가 이미지에 구워집니다). 처음이면 몇 분 걸립니다." -ForegroundColor DarkGray
        }
    }
    elseif ($hasServers) {
        Write-Host "-NoBuild: 기존 이미지를 그대로 씁니다 — 코드를 고쳤다면 반영되지 않습니다." -ForegroundColor Yellow
    }
    $upArgs += $services

    & docker @upArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Host "기동 실패. 위 출력을 확인하세요." -ForegroundColor Red
        exit $LASTEXITCODE
    }

    # ── 준비 대기 ──────────────────────────────────────────────────────────
    #   compose의 depends_on(condition: service_healthy)이 기동 순서는 잡아 주지만,
    #   `up -d`는 healthy까지 기다리지 않고 돌아온다. 여기서 확인해 두면 곧바로
    #   테스트를 시작할 수 있다(healthy 전에 요청하면 접속 거부로 헤매게 된다).
    Write-Host "준비 상태 확인 중..." -ForegroundColor DarkGray

    $deadline = (Get-Date).AddMinutes(3)
    $state = @{}
    do {
        $pending = @()
        foreach ($svc in $services) {
            $id = (docker compose ps -q $svc 2>$null | Select-Object -First 1)
            if (-not $id) { $state[$svc] = '없음'; $pending += $svc; continue }

            $raw = docker inspect --format '{{.State.Status}}|{{if .State.Health}}{{.State.Health.Status}}{{end}}' $id 2>$null
            $parts = "$raw".Split('|')
            $status = $parts[0]
            $health = if ($parts.Length -gt 1) { $parts[1] } else { '' }

            # 헬스체크가 있으면 healthy를, 없으면 running을 준비 완료로 본다.
            $ready = if ($health) { $health -eq 'healthy' } else { $status -eq 'running' }
            $state[$svc] = if ($health) { $health } else { $status }
            if (-not $ready) { $pending += $svc }
        }

        if ($pending.Count -eq 0) { break }
        Start-Sleep -Seconds 3
    }
    while ((Get-Date) -lt $deadline)

    # ── 결과 출력 ──────────────────────────────────────────────────────────
    Write-Host ""
    foreach ($svc in $services) {
        $value = $state[$svc]
        $isReady = ($value -eq 'healthy') -or ($value -eq 'running' -and $noHealthcheck -contains $svc)
        $mark = if ($isReady) { 'OK  ' } else { '..  ' }
        $color = if ($isReady) { 'Green' } else { 'Yellow' }
        Write-Host ("  {0}{1,-14} {2}" -f $mark, $svc, $value) -ForegroundColor $color
    }

    if ($pending.Count -gt 0) {
        Write-Host ""
        Write-Host "아직 준비되지 않은 서비스: $($pending -join ', ')" -ForegroundColor Yellow
        Write-Host "로그를 확인하세요:  docker compose logs -f $($pending -join ' ')" -ForegroundColor Yellow
    }

    # 접속 주소는 실제로 띄운 것만 안내한다.
    Write-Host ""
    if ($services -contains 'mysql')         { Write-Host "  MySQL       : 127.0.0.1:3306 (root / taskbar_hero_dev)" -ForegroundColor DarkGray }
    if ($services -contains 'redis')         { Write-Host "  Redis       : 127.0.0.1:6379" -ForegroundColor DarkGray }
    if ($services -contains 'timescaledb')   { Write-Host "  logdb       : 127.0.0.1:5432 (fluentd / logdb)" -ForegroundColor DarkGray }
    if ($services -contains 'grafana')       { Write-Host "  Grafana     : http://localhost:3000" -ForegroundColor DarkGray }
    if ($services -contains 'accountserver') { Write-Host "  AccountServer: http://localhost:5160/swagger" -ForegroundColor DarkGray }
    if ($services -contains 'gameserver')    { Write-Host "  GameServer  : http://localhost:5247/swagger" -ForegroundColor DarkGray }

    Write-Host ""
    if ($hasServers) {
        Write-Host "코드를 고치면 이 스크립트를 다시 실행하세요(이미지 재빌드 후 재기동)." -ForegroundColor DarkGray
        if ($services -contains 'fluentd') {
            Write-Host "이벤트 로그는 GameServer/logs/event 에 쌓이고 fluentd가 logdb로 옮깁니다." -ForegroundColor DarkGray
        }
    }
    elseif ($Mode -eq 'infra' -or $Mode -eq 'game') {
        Write-Host "서버는 콘솔로 띄웁니다:  ./watch-all.ps1" -ForegroundColor DarkGray
    }
}
finally {
    Pop-Location
}
