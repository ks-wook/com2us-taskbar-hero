# BatchServer 컨테이너 이미지(주기 배치 전담 워커, 로컬 개발용).
#
#   빌드 컨텍스트는 **저장소 루트**다 — 이 워커가 GameServer.Core와 TaskbarHero.Common을
#   ProjectReference로 참조하므로 세 프로젝트 폴더가 모두 컨텍스트에 있어야 한다.
#     docker compose build batchserver     (compose가 context: . 로 지정)
#     docker build -f BatchServer/Dockerfile -t taskbar-hero-batchserver .
#
#   **인스턴스는 1대만 띄운다.** 게임 API를 N대로 늘려도 배치는 늘리지 않는 것이 분리의 목적이다
#   (compose는 container_name 고정으로, 오케스트레이터라면 replicas: 1로 강제한다).
#
#   MySQL·Redis 접속 문자열은 이미지에 굽지 않고 docker-compose.yml의 환경 변수
#   (ConnectionStrings__*, Redis__ConnectionString)로 주입한다.

# ── 빌드 스테이지 ──────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 복원(restore)만 먼저 하도록 프로젝트 파일을 따로 복사한다 — 소스만 바뀐 재빌드에서
# NuGet 복원 레이어를 캐시로 재사용한다.
COPY TaskbarHero.Common/TaskbarHero.Common.csproj TaskbarHero.Common/
COPY GameServer.Core/GameServer.Core.csproj GameServer.Core/
COPY BatchServer/BatchServer.csproj BatchServer/
RUN dotnet restore BatchServer/BatchServer.csproj

COPY TaskbarHero.Common/ TaskbarHero.Common/
COPY GameServer.Core/ GameServer.Core/
COPY BatchServer/ BatchServer/

# TaskbarHero.Common/Directory.Build.props는 obj/bin을 폴더 밖 artifacts/로 재배치한다
# (Unity가 이 폴더를 로컬 UPM 패키지로 직접 컴파일하기 때문). 컨테이너 빌드에는 그 격리가
# 필요 없고, 재배치 경로가 Windows 역슬래시라 리눅스에서는 이상한 폴더명이 된다.
# 그래서 **컨테이너 안 사본에서만** 지운다(호스트 파일은 그대로다).
RUN rm -f TaskbarHero.Common/Directory.Build.props

RUN dotnet publish BatchServer/BatchServer.csproj -c Release --no-restore -o /app/publish

# ── 런타임 스테이지 ────────────────────────────────────────────────────
# HTTP를 받지 않으므로 aspnet이 아니라 runtime 이미지를 쓴다(더 가볍다).
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app

# tzdata: 로그 접두의 시각과 일 단위 배치의 KST 실행 시각을 Asia/Seoul(TZ 환경변수)로 맞추기 위해.
# curl은 넣지 않는다 — 헬스체크할 HTTP 엔드포인트가 없다(compose는 프로세스 생존만 본다).
RUN apt-get update \
    && apt-get install -y --no-install-recommends tzdata \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish ./

ENTRYPOINT ["dotnet", "BatchServer.dll"]
