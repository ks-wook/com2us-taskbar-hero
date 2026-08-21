# fluentd 이미지(로컬 개발용) — 로그 수집기 1대.
#
#   역할: GameServer가 쓰는 **이벤트 로그 JSON 파일**을 in_tail로 걷어, 레코드의 `tag` 필드를
#   fluentd 태그로 승격한 뒤 TimescaleDB(`logdb`)의 이벤트별 테이블에 적재한다.
#   설계 정본: docs/공통/로그-이벤트-정의.md 3·9장.
#
#   설정 파일은 이미지에 굽지 않고 docker-compose.yml이 마운트한다
#   (fluent.conf · conf.d/*.conf 를 고치면 컨테이너 재시작만으로 반영된다).
#
#     docker compose build fluentd
#     docker compose up -d fluentd

FROM fluent/fluentd:v1.17-debian-1

# 플러그인 설치는 root로 한다(기본 사용자는 `fluent`).
USER root

# build-essential·libpq-dev: `pg` 젬이 네이티브 확장을 컴파일하고 libpq에 링크한다.
#   fluent-plugin-rewrite-tag-filter : 레코드의 tag 필드를 fluentd 태그로 승격(9.2)
#   fluent-plugin-sql + pg           : out_sql(Sequel + postgresql 어댑터)로 logdb 적재(9.2)
RUN apt-get update -y \
    && apt-get install -y --no-install-recommends build-essential libpq-dev \
    && fluent-gem install --no-document fluent-plugin-rewrite-tag-filter \
    && fluent-gem install --no-document fluent-plugin-sql pg \
    && rm -rf /var/lib/apt/lists/*

# pos_file·버퍼가 쌓이는 상태 디렉터리. **이미지에서 미리 만들어 `fluent`에게 넘긴다** —
# 비어 있는 named volume은 이미지 쪽 디렉터리의 소유권을 그대로 물려받으므로,
# 여기서 chown 해 두지 않으면 마운트 후 root 소유가 되어 pos_file 쓰기가 실패한다.
RUN mkdir -p /var/log/fluentd/buf /var/log/taskbarhero/event \
    && chown -R fluent:fluent /var/log/fluentd

USER fluent
