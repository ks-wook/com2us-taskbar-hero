# 로그 파이프라인이 실제로 흐르고 있는지 한 화면으로 확인한다.
#   사용법:  ./pipeline-status.ps1 [-Watch] [-IntervalSeconds 5]
#
#   파이프라인은 네 구간이다(docs/공통/로그-이벤트-정의.md 3장):
#       GameServer(이벤트 JSON 파일) → fluentd(in_tail) → fluentd(out_sql) → TimescaleDB(logdb)
#   각 구간이 "지금 막힘 없이 흐르는가"를 서로 다른 신호로 본다.
#
#     1) 서버가 쓰고 있나   : 이벤트 파일 크기·마지막 라인 시각
#     2) 수집이 따라잡나    : pos_file 오프셋 vs 파일 크기 = **아직 안 읽은 바이트**
#     3) 전송이 밀리나      : in_monitor_agent 의 버퍼 큐 길이·재시도 횟수
#     4) 적재가 되고 있나   : logdb 최신 행 시각과 지금의 차이 = **끝단 지연**
#
#   -Watch 를 주면 지울 때까지 반복한다(Ctrl+C 로 종료).
#
#   ※ 이 파일은 반드시 UTF-8 with BOM으로 저장한다(Windows PowerShell 5.1 한글 리터럴).

[CmdletBinding()]
param(
    [switch]$Watch,
    [int]$IntervalSeconds = 5
)

try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch {}
$OutputEncoding = [System.Text.Encoding]::UTF8

# docker 는 진행 상황을 stderr 로 내므로 'Stop'이면 정상 출력이 종료 오류가 된다(성공 판정은 종료 코드로).
$ErrorActionPreference = 'Continue'
Push-Location $PSScriptRoot

$FLUENTD  = 'taskbar-hero-fluentd'
$LOGDB    = 'taskbar-hero-timescaledb'
$EVENTDIR = Join-Path $PSScriptRoot 'GameServer\logs\event'
$MONITOR  = 'http://127.0.0.1:24220/api/plugins.json'

function Write-Section($text) { Write-Host ""; Write-Host $text -ForegroundColor Cyan }
function Write-Line($label, $value, $color = 'Gray') {
    Write-Host ("  {0,-22} " -f $label) -NoNewline
    Write-Host $value -ForegroundColor $color
}

function Get-Verdict($ok, $warn) {
    if ($ok) { return 'Green' } elseif ($warn) { return 'Yellow' } else { return 'Red' }
}

function Show-Status {
    $now = Get-Date

    # ── 1. 서버가 이벤트 로그를 쓰고 있나 ──────────────────────────────────
    Write-Section "1. 서버 — 이벤트 로그 쓰기"
    $files = @(Get-ChildItem -Path $EVENTDIR -Filter *.json -ErrorAction SilentlyContinue |
               Sort-Object LastWriteTime -Descending)
    if ($files.Count -eq 0) {
        Write-Line "이벤트 파일" "없음 — 서버가 아직 한 줄도 쓰지 않았습니다" 'Red'
        $lastEventUtc = $null
    }
    else {
        $latest = $files[0]
        $ageSec = [int]($now - $latest.LastWriteTime).TotalSeconds
        Write-Line "파일" ("{0} ({1:N0} bytes, {2}개)" -f $latest.Name, $latest.Length, $files.Count)
        Write-Line "마지막 쓰기" ("{0:HH:mm:ss} ({1}초 전)" -f $latest.LastWriteTime, $ageSec) `
            (Get-Verdict ($ageSec -lt 300) ($ageSec -lt 1800))

        # 마지막 라인의 timestamp = 서버가 마지막으로 남긴 사건의 시각.
        $lastLine = Get-Content -Path $latest.FullName -Tail 1 -ErrorAction SilentlyContinue
        $lastEventUtc = $null
        if ($lastLine) {
            try {
                $obj = $lastLine | ConvertFrom-Json
                $lastEventUtc = [datetime]::Parse($obj.timestamp).ToUniversalTime()
                Write-Line "마지막 이벤트" ("{0} · {1:HH:mm:ss}" -f $obj.tag, $lastEventUtc.ToLocalTime())
            } catch { Write-Line "마지막 이벤트" "파싱 실패(라인이 잘렸을 수 있음)" 'Yellow' }
        }
    }

    # ── 2. fluentd 가 어디까지 읽었나 ──────────────────────────────────────
    #   pos_file 은 "경로 \t 오프셋(16진) \t inode(16진)" 이다. 오프셋이 파일 크기보다 작으면
    #   그만큼이 아직 안 읽힌 것이고, ffffffffffffffff 는 더 이상 보지 않는 파일(삭제 등)이다.
    Write-Section "2. fluentd — 수집(in_tail)"
    $pos = docker exec $FLUENTD cat /var/log/fluentd/event.pos 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $pos) {
        Write-Line "pos_file" "읽을 수 없음 — fluentd 컨테이너가 떠 있나요?" 'Red'
    }
    else {
        foreach ($line in @($pos)) {
            $f = $line -split "`t"
            if ($f.Count -lt 2) { continue }
            $name = Split-Path $f[0] -Leaf
            if ($f[1] -eq 'ffffffffffffffff') {
                Write-Line $name "추적 해제됨(파일 삭제/교체)" 'DarkGray'
                continue
            }
            $read = [Convert]::ToInt64($f[1], 16)
            $size = (Get-ChildItem -Path (Join-Path $EVENTDIR $name) -ErrorAction SilentlyContinue).Length
            if ($null -eq $size) { $size = $read }
            $behind = [Math]::Max(0, $size - $read)
            Write-Line $name ("읽음 {0:N0} / {1:N0} bytes · 미수집 {2:N0}" -f $read, $size, $behind) `
                (Get-Verdict ($behind -eq 0) ($behind -lt 1MB))
        }
    }

    # ── 3. 전송(out_sql) 이 밀리고 있나 ────────────────────────────────────
    #   버퍼 큐가 계속 쌓이거나 retry_count 가 올라가면 logdb 쪽이 막힌 것이다.
    Write-Section "3. fluentd — 전송(out_sql) · 계측"
    $mon = $null
    try { $mon = Invoke-RestMethod -Uri $MONITOR -TimeoutSec 5 } catch {}
    if (-not $mon) {
        Write-Line "monitor_agent" "응답 없음 — fluent.conf 의 monitor_agent 와 24220 포트 매핑 확인" 'Yellow'
    }
    else {
        foreach ($p in $mon.plugins) {
            if (-not $p.output_plugin) {
                if ($p.type -eq 'tail') {
                    Write-Line "in_tail" ("emit {0:N0}건 · 열린 파일 {1}" -f $p.emit_records, $p.opened_file_count)
                }
                continue
            }
            $q  = [int]$p.buffer_queue_length
            $qb = [int]$p.buffer_total_queued_size
            $rc = [int]$p.retry_count
            Write-Line ("out:" + $p.type) ("emit {0:N0}건 · 대기 {1}청크({2:N0} bytes) · 재시도 {3}" -f `
                $p.emit_records, $q, $qb, $rc) (Get-Verdict (($q -eq 0) -and ($rc -eq 0)) ($rc -eq 0))
            if ($p.rollback_count -and [int]$p.rollback_count -gt 0) {
                Write-Line "  rollback" $p.rollback_count 'Yellow'
            }
        }
    }

    # 최근 오류(전송 실패·매핑 오류)는 컨테이너 로그에만 남는다.
    $errLines = @(docker logs --since 10m $FLUENTD 2>&1 |
                  Select-String -Pattern '^\d{4}-\d{2}-\d{2} [\d:]+ \+\d{4} \[error\]')
    Write-Line "최근 10분 오류" ("{0}건" -f $errLines.Count) (Get-Verdict ($errLines.Count -eq 0) $false)
    if ($errLines.Count -gt 0) {
        Write-Host ("      " + ($errLines[-1].ToString().Substring(0, [Math]::Min(160, $errLines[-1].ToString().Length)))) -ForegroundColor DarkYellow
    }

    # ── 4. logdb 에 실제로 들어갔나 ────────────────────────────────────────
    Write-Section "4. logdb — 적재"
    $sql = @"
select
  (select count(*) from stage_clear_logs) || '|' ||
  (select count(*) from currency_flow_logs) || '|' ||
  (select count(*) from unknown_event_logs) || '|' ||
  coalesce((select max("timestamp") from (
      select max("timestamp") as "timestamp" from stage_clear_logs
      union all select max("timestamp") from currency_flow_logs
      union all select max("timestamp") from save_load_logs
      union all select max("timestamp") from batch_run_logs
      union all select max("timestamp") from online_user_history) t)::text, '-')
"@
    $row = docker exec $LOGDB psql -U fluentd -d logdb -t -A -c $sql 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $row) {
        Write-Line "logdb" "조회 실패 — timescaledb 컨테이너가 떠 있나요?" 'Red'
    }
    else {
        $parts = ("$row".Trim() -split '\|')
        Write-Line "행 수(표본)" ("stage_clear {0:N0} · currency_flow {1:N0}" -f [long]$parts[0], [long]$parts[1])
        Write-Line "unknown_event_logs" ("{0}건 (0이어야 정상 — 카탈로그 누락 탐지)" -f [long]$parts[2]) `
            (Get-Verdict ([long]$parts[2] -eq 0) $false)

        if ($parts[3] -ne '-') {
            $lastDb = [datetime]::Parse($parts[3]).ToUniversalTime()
            $lagSec = [int]((Get-Date).ToUniversalTime() - $lastDb).TotalSeconds
            if ($lagSec -lt 0) {
                # 미래 시각이 들어오는 경우: 서버·DB 시계 어긋남이거나 사람이 주입한 데이터다.
                # 지연으로 읽으면 음수가 되어 뜻이 없으므로 사실만 적는다.
                Write-Line "최신 적재 시각" ("{0:HH:mm:ss} — 미래 시각({1}초 뒤). 시계 어긋남 또는 주입 데이터" -f `
                    $lastDb.ToLocalTime(), [Math]::Abs($lagSec)) 'Yellow'
            }
            else {
                Write-Line "최신 적재 시각" ("{0:HH:mm:ss} ({1}초 전)" -f $lastDb.ToLocalTime(), $lagSec) `
                    (Get-Verdict ($lagSec -lt 120) ($lagSec -lt 900))
            }

            # 끝단 지연: 서버가 마지막으로 쓴 사건이 logdb 에 반영되기까지.
            if ($lastEventUtc) {
                $e2e = [int]($lastEventUtc - $lastDb).TotalSeconds
                if ($e2e -le 0) {
                    Write-Line "끝단 지연" "따라잡음(파일의 마지막 사건까지 적재됨)" 'Green'
                }
                else {
                    Write-Line "끝단 지연" ("{0}초 뒤처짐 (flush 주기 10초)" -f $e2e) `
                        (Get-Verdict ($e2e -lt 30) ($e2e -lt 300))
                }
            }
        }
    }

    Write-Host ""
    Write-Host ("  기준 시각 {0:yyyy-MM-dd HH:mm:ss}" -f $now) -ForegroundColor DarkGray
}

try {
    if ($Watch) {
        while ($true) {
            Clear-Host
            Write-Host "로그 파이프라인 상태 (Ctrl+C 종료)" -ForegroundColor White
            Show-Status
            Start-Sleep -Seconds $IntervalSeconds
        }
    }
    else {
        Show-Status
    }
}
finally {
    Pop-Location
}
