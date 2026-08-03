# tools — 개발용 도구

## master_data_export.py — 마스터 데이터 추출기 (DB → Unity JSON)

`master-data-schema.sql`의 시드 데이터를 **Unity 클라이언트용 JSON 번들**로 추출한다.

- 흐름: `schema.sql` 재적용(DROP+CREATE+INSERT) → MySQL master DB 조회 → 기획서 §7 규약(**테이블별 camelCase JSON 배열**)으로 직렬화 → `com2us-taskbar-hero-client/Assets/Resources/MasterData/*.json` 출력.
- 자식 테이블은 부모 JSON에 배열로 중첩한다: `skill_coefficient`→`skill_master.coefs[]`, `stage_spawn`→`stage_master.spawns[]`, `cube_recipe_ingredient`→`cube_recipe.ingredients[]`. 스탯 컬럼(`hp`~`cooldown`)은 `baseStats`/`statBonus` 객체로 묶는다.
- 출력 파일(13종): `equip_slot_master · grade_master · class_master · level_master · skill_master · rune_master · item_master · monster_master · stage_master · stage_reward · cube_master · cube_recipe · attendance_master`. (값 미확정인 `enhance_master`는 제외. `gacha_master` 계열은 값이 확정됐으나 **익스포터가 아직 지원하지 않는다** — 클라 번들이 필요해지면 추출 대상에 추가한다.)

### 정본
- 값: `docs/세부/master-data/master-data-값.md`
- 구조/번들 포맷: `docs/세부/master-data/master-data-기획서.md` §7
- SQL: `docs/세부/master-data/master-data-schema.sql`

### 사전 준비
1. **Python 3** (Windows에선 Microsoft Store 스텁이 아닌 실제 설치본. 예: `winget install Python.Python.3.12`)
2. **pymysql**: `python -m pip install pymysql`
3. **로컬 MySQL 실행** (docker 컨테이너 `taskbar-hero-master`, `127.0.0.1:3306`)

### 사용법 (CLI)
```bash
python tools/master_data_export.py
# 옵션: --schema PATH  --output DIR  --host  --port  --user  --password  --database  --no-apply
#   --no-apply : schema.sql 재적용을 생략하고 현재 DB 상태에서만 추출
```
기본값은 이 리포 구조 기준으로 자동 계산된다(스크립트 위치 → 리포 루트).

### 사용법 (Unity 에디터)
Unity 메뉴 **`TaskbarHero > 마스터 데이터`**
- **최신화 (DB→JSON)**: 위 스크립트를 실행(schema.sql 재적용 + JSON 재생성) 후 `AssetDatabase.Refresh()`.
- **Python 경로 지정...**: 자동 탐색 실패 시 `python.exe` 경로 수동 지정(`EditorPrefs` 저장).
- **출력 폴더 열기**: `Assets/Resources/MasterData` 열기.

에디터 툴 소스: `com2us-taskbar-hero-client/Assets/Editor/MasterData/MasterDataExportTool.cs`
(Python은 `%LOCALAPPDATA%\Programs\Python\Python3xx` 또는 `%ProgramFiles%\Python3xx`에서 자동 탐색하며, Store 스텁은 회피한다.)

### 런타임 로드(참고, 별도 작업)
생성된 JSON은 기획서 §7.1의 공유 POCO(`TaskbarHero.Common.MasterData`)와 §7.2/7.3의 `JsonHelper`·`MasterDatabase` 로더로 로드하도록 설계돼 있다. 현재는 **추출 파이프라인**까지 구현했고, 런타임 로더/POCO 확장은 후속 작업이다.
