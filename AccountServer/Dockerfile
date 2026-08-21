# AccountServer 컨테이너 이미지(로컬 개발용).
#
#   빌드 컨텍스트는 **저장소 루트**다 — 이 서버가 TaskbarHero.Common을 ProjectReference로
#   참조하므로 두 프로젝트 폴더가 모두 컨텍스트에 있어야 한다.
#     docker compose build accountserver      (compose가 context: . 로 지정)
#     docker build -f AccountServer/Dockerfile -t taskbar-hero-accountserver .
#
#   MySQL·Redis 접속 문자열과 수신 포트는 이미지에 굽지 않고 docker-compose.yml의
#   환경 변수(ConnectionStrings__*, Redis__ConnectionString, ASPNETCORE_URLS)로 주입한다.

# ── 빌드 스테이지 ──────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 복원(restore)만 먼저 하도록 프로젝트 파일을 따로 복사한다 — 소스만 바뀐 재빌드에서
# NuGet 복원 레이어를 캐시로 재사용한다.
COPY TaskbarHero.Common/TaskbarHero.Common.csproj TaskbarHero.Common/
COPY AccountServer/AccountServer.csproj AccountServer/
RUN dotnet restore AccountServer/AccountServer.csproj

COPY TaskbarHero.Common/ TaskbarHero.Common/
COPY AccountServer/ AccountServer/

# TaskbarHero.Common/Directory.Build.props는 obj/bin을 폴더 밖 artifacts/로 재배치한다
# (Unity가 이 폴더를 로컬 UPM 패키지로 직접 컴파일하기 때문). 컨테이너 빌드에는 그 격리가
# 필요 없고, 재배치 경로가 Windows 역슬래시라 리눅스에서는 이상한 폴더명이 된다.
# 그래서 **컨테이너 안 사본에서만** 지운다(호스트 파일은 그대로다).
RUN rm -f TaskbarHero.Common/Directory.Build.props

RUN dotnet publish AccountServer/AccountServer.csproj -c Release --no-restore -o /app/publish

# ── 런타임 스테이지 ────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# tzdata: 로그 접두의 시각을 Asia/Seoul(TZ 환경변수)로 찍기 위해.
# curl    : compose 헬스체크용(aspnet 이미지에는 HTTP 클라이언트가 없다).
RUN apt-get update \
    && apt-get install -y --no-install-recommends tzdata curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish ./

ENTRYPOINT ["dotnet", "AccountServer.dll"]
