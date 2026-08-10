# tools — 개발용 도구

## master_data_export.py — 마스터 데이터 추출기 (DB → Unity JSON)

`master-data-schema.sql`의 시드 데이터를 **Unity 클라이언트용 JSON 번들**로 추출한다.

- 흐름: `schema.sql` 재적용(DROP+CREATE+INSERT) → MySQL master DB 조회 → 기획서 §7 규약(**테이블별 camelCase JSON 배열**)으로 직렬화 → `com2us-taskbar-hero-client/Assets/Resources/MasterData/*.json` 출력.
- 자식 테이블은 부모 JSON에 배열로 중첩한다: `skill_coefficient`→`skill_master.coefs[]`, `stage_spawn`→`stage_master.spawns[]`, `cube_recipe_ingredient`→`cube_recipe.ingredients[]`. 스탯 컬럼(`hp`~`cooldown`)은 `baseStats`/`statBonus` 객체로 묶는다.
- 출력 파일(16종): `equip_slot_master · grade_master · class_master · level_master · skill_master · rune_master · item_master · monster_master · stage_master · stage_reward · cube_master · cube_recipe · attendance_master · character_create_cost · inventory_expand_master · gacha_master`. (값 미확정인 `enhance_master`는 제외.)
- `gacha_master`는 자식 3종을 부모에 중첩한다: `gacha_grade_weight`→`gradeWeights[]`, `gacha_item_pool`→`itemPool[]`, `gacha_pity_rule`→`pityRules[]`. 클라이언트가 배너 이름·비용·등급 확률·천장 기준을 이 번들에서 읽는다(가챠 기획서 §5 서두 — 서버는 "지금 열려 있는가·내 천장이 얼마인가"만 내려준다).

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
- **최신화 (DB→JSON)**: 위 스크립트를 실행(schema.sql 재적용 + JSON 재생성) → `AssetDatabase.Refresh(ForceSynchronousImport)` → **`ItemIconDatabaseBuilder.Build()`** 까지 이어서 수행한다. 아이템이 늘면 마스터 JSON과 아이콘(`Assets/Art/Icon/Item/item_{code}.png`)이 함께 늘어나므로 둘을 한 메뉴로 묶어 "데이터만 갱신되고 아이콘은 안 나오는" 상태를 없앴다. 아이콘 DB 빌드가 실패해도 마스터 데이터 갱신은 성공으로 두고 경고만 남긴다(그 경우 `TaskbarHero/UI/아이템 아이콘 DB 빌드`로 재실행).
- **Python 경로 지정...**: 자동 탐색 실패 시 `python.exe` 경로 수동 지정(`EditorPrefs` 저장).
- **출력 폴더 열기**: `Assets/Resources/MasterData` 열기.

에디터 툴 소스: `com2us-taskbar-hero-client/Assets/Editor/MasterData/MasterDataExportTool.cs`
(Python은 `%LOCALAPPDATA%\Programs\Python\Python3xx` 또는 `%ProgramFiles%\Python3xx`에서 자동 탐색하며, Store 스텁은 회피한다.)

### 런타임 로드(참고, 별도 작업)
생성된 JSON은 기획서 §7.1의 공유 POCO(`TaskbarHero.Common.MasterData`)와 §7.2/7.3의 `JsonHelper`·`MasterDatabase` 로더로 로드하도록 설계돼 있다. 현재는 **추출 파이프라인**까지 구현했고, 런타임 로더/POCO 확장은 후속 작업이다.

---

## master_item_tool.py — 마스터 아이템(item_master) 생성·검증기

아이템 한 종을 추가하려면 **정본 두 곳**을 사람이 손으로 맞춰야 했다 — `master-data-값.md` §6의 **(A) 공통 속성 표**·**(B) 장비 스탯 표**와 `master-data-schema.sql`의 `INSERT INTO item_master`. 이 도구가 그 둘을 한 번에 만들고(`add`), 서로 어긋나지 않았는지 검사한다(`verify`). **DB 적재·클라 JSON 번들은 위 `master_data_export.py`가 이어받는다** — 이 도구는 두 텍스트 정본만 다루고 DB에 접속하지 않는다(의존성 없음, 표준 라이브러리만).

### 파이프라인에서의 위치

```
① master_item_tool.py add        →  값.md (A)(B) + schema.sql INSERT + item-icon-manifest.json
② pixel-item-icons 스킬           →  Assets/Art/Icon/Item/<아이템명>.png   (한글 이름 그대로)
③ master_item_tool.py link-icons →  <아이템명>.png → item_{code}.png (.meta 동반 이동)
④ Unity 메뉴 `TaskbarHero > 마스터 데이터 > 최신화 (DB→JSON)`
        →  master_data_export.py 실행(schema.sql 재적용 + JSON 번들) **+ ItemIconDatabase 재생성까지 한 번에**
```

**①~③은 `.claude/skills/master-item` 스킬이 실행한다** — 아이템명·종류·등급만 주면 된다. 남는 수동 단계는 **④ 에디터 메뉴 한 번**뿐이다(에디터 조작이라 사용자 몫).

- ④를 CLI로 쪼개 돌릴 수도 있다: `python tools/master_data_export.py` 실행 후 Unity 메뉴 `TaskbarHero/UI/아이템 아이콘 DB 빌드`. **아이콘만 교체했을 때는** MySQL 없이 이 두 번째 메뉴만 돌리면 된다.
- 아래 CLI 설명은 그 스킬이 쓰는 명령이자, 손으로 돌릴 때의 사용법이다.

### `verify` — 정합성 검사

```bash
python tools/master_item_tool.py verify              # 아이콘 존재까지 검사
python tools/master_item_tool.py verify --no-icons   # 텍스트 정본 둘만 검사
```

검사 항목 — **오류**(코드 1로 종료): (A) 표 ↔ SQL의 **코드 집합·전 필드 값 일치** · 장비의 (B) 표 스탯 일치 · 장비 코드 체계(`30000 + 슬롯×1000 + 클래스×100 + 등급×10 + 순번`) · `item_type`/`grade`/`equip_slot`/`class_req` 유효값 · `level_req` 5의 배수 · 장비 `stack_max=1`·`equip_slot≠0` · 비장비 스탯 0 · `sellable=0`이면 `base_price=0` · **이름 중복** · §6 「규모/현황」 문장과 실제 개수 일치.
**경고**(종료 코드에 영향 없음): 등급 파생값 이탈(`level_req`·`base_price`가 등급 기준값과 다름) · `item_{code}.png` 누락 · 대응 아이템이 없는 고아 아이콘.

> 도입 첫 실행에서 **소모품 2종(42001·42002)이 (A) 표에서 누락**된 것을 잡아냈다(SQL 95행 ↔ 표 93행). 손으로 두 곳을 맞추던 방식에서 실제로 생겼던 종류의 불일치다.

### `add` — 아이템 추가

```bash
# 단건(CLI)
python tools/master_item_tool.py add --name "심연의 대도끼" --item-type 1 --grade 5 \
    --slot 1 --class-req 4 --atk 118 --crit-chance 0.09 --cooldown -0.1

# 배치(JSON 객체 또는 배열)
python tools/master_item_tool.py add --json new_items.json

# 파일을 고치지 않고 삽입될 줄만 출력
python tools/master_item_tool.py add ... --dry-run
```

- **코드 자동 채번**: 장비는 코드 체계의 다음 빈 순번, 재료 `41xxx`·소모품 `42xxx`는 블록 내 다음 번호(`--item-code`로 직접 지정 가능). 미리 보려면 `next-code`:
  `python tools/master_item_tool.py next-code --item-type 1 --slot 1 --class-req 4 --grade 5` → `31453`
- **등급 파생값 자동**: 장비의 `level_req`(0·10·20·30·40)와 `base_price`(500·5,000·50,000·150,000·400,000)를 등급에서 채운다(명시하면 그 값 우선).
- **삽입 위치**: 두 파일 모두 **코드 오름차순**을 유지하며, SQL 값 블록의 그룹 구분 주석(`-- 소모품(42xxx)…`) 위로 올라가지 않도록 "자기보다 작은 마지막 코드 바로 뒤"에 넣는다.
- **표기 재현**: (A) 표의 `1 장비`·`5 전설`·`1 무기`·`4 슬레이어` 라벨과 SQL의 소수 자릿수(`move_speed` 1 · `crit_chance` 2 · `crit_damage` 1 · `cooldown` 1, 더 정밀한 값이면 그만큼)를 기존 표기 그대로 맞춘다.
- §6 「규모/현황」 문장의 개수를 함께 갱신하고, **반영 직후 `verify`를 자동 실행**한다(실패 시 종료 코드 1).
- 중복 이름·중복 코드는 거부하며, **같은 배치 안에서의 중복도** 막는다.
- 끝나면 **아이콘 작업 지시서** `tools/item-icon-manifest.json`을 쓴다(아래).

### `manifest` / `link-icons` — 아이콘 연결

아이콘 파일명은 `item_{code}.png`가 **곧 매핑 키**다(`Assets/Editor/ItemIconDatabaseBuilder.cs`가 이 이름을 스캔한다). 반면 `pixel-item-icons` 스킬은 세트 일관성·팔레트 레지스트리 규칙상 **아이템 이름 그대로**(`심연의 대도끼.png`) 저장한다. 그 간극을 이 두 명령이 메운다.

```bash
python tools/master_item_tool.py manifest            # 아이콘이 아직 없는 아이템 목록 → 지시서
python tools/master_item_tool.py manifest --all      # 전체 아이템
python tools/master_item_tool.py link-icons          # <이름>.png → item_{code}.png (--dry-run 지원)
```

지시서(`tools/item-icon-manifest.json`)에는 아이콘 컨셉을 잡는 데 필요한 값이 함께 들어간다 — `iconSourceName`(스킬이 저장할 한글 파일명) · `iconFile`(연결될 코드 이름) · `gradeName`·`equipSlotName`·`classReqName` · `iconExists`.

`link-icons`는 `Assets/Art/Icon/Item/`과 `Assets/Art/Icon/` 둘 다에서 원본을 찾고, **`.meta`가 있으면 함께 옮겨 GUID를 보존**한다(기존 참조가 끊기지 않는다). 이미 목적지 파일이 있으면 건너뛰고, 원본이 없으면 그 항목을 보고하며 종료 코드 1을 낸다.

### 정본
`master_data_export.py`와 동일 — 값 `docs/세부/master-data/master-data-값.md`, 구조 `master-data-기획서.md`, SQL `master-data-schema.sql`.

---

## master_monster_tool.py — 마스터 몬스터(monster_master) 생성·검증기

몬스터 한 종을 추가하려면 **정본 세 곳**을 사람이 손으로 맞춰야 했다 — `master-data-값.md` §9 표(+「규모/현황」 문장), `master-data-schema.sql`의 `INSERT INTO monster_master`(+ 머리 주석의 종수 목록), 그리고 클라이언트 개발 씬이 읽는 `Assets/Dev/monster-appearance-recipe.json`(외형 레시피). 이 도구가 셋을 한 번에 만들고(`add`) 서로 어긋나지 않았는지 검사한다(`verify`). 표준 라이브러리만 쓰며 DB에 접속하지 않는다.

### 왜 이 도구가 필요한가 (순서 함정)

`CharacterDevScene`의 F12는 **클라이언트 번들** `Assets/Resources/MasterData/monster_master.json`만 고친다. 반면 `master_data_export.py`는 **schema.sql → MySQL → 번들** 단방향으로 번들을 다시 만든다. 즉 **서버 정본을 먼저 맞추지 않고 export를 돌리면 씬이 고친 값이 조용히 사라진다.** 이 도구로 정본을 먼저 맞춰 그 역전을 막는다.

```
① master_monster_tool.py add [--spawn "5:4"]
       →  값.md §9·§11 + schema.sql monster_master·stage_spawn + monster-appearance-recipe.json
② Unity 메뉴 `TaskbarHero/몬스터/신규 몬스터 전투 반영`
       →  monster_{code}.prefab 생성(플레이 모드 불필요) + 던전 전투 배선(코드→프리팹 맵)
③ python tools/master_data_export.py
       →  DB 재적용 + 클라 JSON 번들 재생성
```

**세 단계 모두 `com2us-taskbar-hero-client/.claude/skills/master-monster` 스킬이 실행한다** — 외형 컨셉·지역·스테이지만 주면 된다(②는 Unity MCP `ExecuteMenuItem`으로 호출한다). 남는 수동 작업은 **값.md §11 (B) 요약 표를 손보는 것**뿐이고, 그것도 필요할 때만 `verify`가 경고로 알려 준다.

### 명령

```bash
python tools/master_monster_tool.py verify                          # 정본·레시피·프리팹·번들 정합성
python tools/master_monster_tool.py list                            # 현황(레시피·프리팹 보유 표시)
python tools/master_monster_tool.py recommend --act 2 --stage 5     # → hp 120 / attack 16
python tools/master_monster_tool.py next-code --act 2 [--boss]      # → 9103
python tools/master_monster_tool.py add --name "스켈레톤 궁수" --act 2 --stage 5 \
    --race undead --classes ranged,physical --spawn "5:4,6:5" [--boss] [--hp N --attack N] [--dry-run]
python tools/master_monster_tool.py spawn --code 9103 --stages "5:4,6:5"   # 5스테이지 4마리로 확정
python tools/master_monster_tool.py spawn --code 9101 --stages "5:+3"      # 3마리 더 (증분), "-2"면 감소
python tools/master_monster_tool.py reskin --code 9101 --race devil --classes melee,damage
python tools/master_monster_tool.py reskin --code 9101 --reroll            # 태그 유지, 시드만 옮겨 다시 뽑기
python tools/master_monster_tool.py adopt-bundle                    # 씬(F12)이 번들에만 반영한 값 → 정본
```

- **코드 자동 채번**: 일반은 그 Act 대역(`9{act-1}01`~`98`)의 빈 번호, 보스는 `xx99`. 기존 몬스터를 고칠 때만 `--code`로 지정한다(이때 외형 레시피는 보존되며, 덮어쓰려면 `--replace-recipe`).
- **능력치 자동 산출**: 클라이언트 `MonsterStatCurve`(`CharacterDevRecipe.cs`)와 **같은 산식**을 옮겨 둔 것이다 — 일반(1~9)은 그 Act 하한에서 다음 Act 하한까지 9칸 기하 보간, 보스(10)는 그 Act 보스 실측값. **한쪽을 고치면 다른 쪽도 고쳐야 한다**(추천값이 갈리면 씬 표시와 정본이 어긋난다).
- **삽입 방식**: SQL 값 블록을 **재포맷하지 않고** 새 줄만 코드 순으로 끼워 넣는다(기존 12줄은 손으로 맞춘 정렬이라 어떤 규칙으로도 재현되지 않아, 재포맷하면 값이 그대로인 줄까지 diff에 섞인다). 새 줄의 열 위치는 기존 줄들의 최빈 열을 흉내 낸다.
- 「규모/현황」 문장(값.md)과 머리 주석 종수 목록(schema.sql)을 함께 갱신하고, **반영 직후 `verify`를 자동 실행**한다.
- `spawn` — **전투 등장 여부와 마리 수를 정하는 단계**. `stage_spawn`에 `(stage_id, monster_code, spawn_count)` 행을 넣고 값.md §11의 총 행수·스폰 몬스터 목록 문장을 갱신한다. 난이도 1·2 **양쪽에 넣는 것이 기본**이다(현행 데이터가 동일 구성 — `--difficulty`로 한쪽만 지정 가능). 보스는 여기가 아니라 `stage_master.boss_monster_code`가 담당하므로 거부한다. `add --spawn "5:4,6:5"`로 추가와 동시에 배치할 수도 있다.
  - **마리 수 표기**: `5:4` = 그 스테이지를 4마리로 **확정**, `5:+3`/`5:-2` = 현재 값 기준 **증분**("3마리 더"). 증분은 난이도별 현재 값에 각각 더하며, 결과가 1 미만이면 거부한다.
  - 실행할 때마다 스테이지별 `이전 → 이후` 마리 수와 **그 스테이지 총량**을 출력하고, 총량이 값 문서 §11의 통상 범위(**8~16마리**)를 벗어나면 경고한다 — 총 마리 수가 곧 클리어 시간이기 때문이다.
- `reskin` — **능력치는 그대로 두고 외형만 교체**한다. 바뀌는 것은 `monster-appearance-recipe.json` 한 곳뿐이라 `monster_master`·`stage_spawn`·DB·클라 번들은 건드리지 않는다(**export 불필요**). `--reroll`은 태그를 유지한 채 시드만 옮겨 다시 뽑는다("외형이 마음에 안 든다"는 후속 요청용).
  - 반영은 Unity 메뉴 **`TaskbarHero/몬스터/외형 교체 반영 (지정 코드 재생성)`** — 대상 코드는 `EditorPrefs`의 `TaskbarHero.Dev.MonsterRebuildCodes`(콤마 구분)로 넘기고, 실행 시 한 번 쓰고 지워진다. 기존 프리팹은 백업 후 **같은 경로에 덮어써져 GUID가 유지되므로 전투 배선도 다시 돌릴 필요가 없다**.
  - 외형만 바꾸려고 `add --code`를 쓰면 **능력치가 추천값으로 덮어써진다** — 그 용도로는 반드시 `reskin`을 쓴다.
- `adopt-bundle` — **역방향 경로**. 씬에서 능력치를 먼저 고쳐 F12로 클라 번들에 반영한 뒤 이걸 돌리면 그 차이가 정본으로 올라간다(갱신·신규 모두). 번들에 없고 정본에만 있는 코드는 **지우지 않고 알리기만 한다** — 삭제는 `stage_spawn`·`stage_master` 참조를 끊을 수 있어 사람이 판단할 일이다.
- `verify` — **오류**(코드 1): 값.md ↔ schema.sql의 코드 집합·이름·hp·attack 불일치 · 코드 규약(9000~9499) 위반 · 중복 코드 · 개수 문장 불일치 · `stage_spawn`에 `monster_master`에 없는 코드나 보스가 들어감 · §11 스폰 총 행수·몬스터 목록 문장 불일치. **경고**: 외형 레시피 없음 · 프리팹 없음 · 클라 번들이 정본과 다름(= export 필요) · **어느 스테이지에도 배치되지 않아 전투에 등장하지 않음** · Act별 스폰 종수가 값.md §11 (B) 요약 표의 전제(Act1·2는 2종 / Act3~5는 1종)와 달라 표를 손봐야 함. 프리팹·번들 경고는 위 ②③ 전이면 정상이다.

### 정본
- 값: `docs/세부/master-data/master-data-값.md` §9
- SQL: `docs/세부/master-data/master-data-schema.sql`
- 외형: `com2us-taskbar-hero-client/Assets/Dev/monster-appearance-recipe.json`
- 씬 설계: [`캐릭터-개발씬-기획서.md`](../com2us-taskbar-hero-client/docs/캐릭터-개발씬-기획서.md) §4.4(산출식)·§7.4(책임 분리)

---

## mp4_to_gif.py — 문서용 GIF 변환기 (ffmpeg 불필요)

**GitHub 마크다운은 `<video>` 태그를 지우고, 저장소에 올린 mp4도 인라인 재생하지 않는다.** 문서에 움직이는 화면을 넣으려면 GIF여야 한다(이미지로 취급되어 자동 재생). 이 스크립트가 `cv2` + `PIL`만으로 변환한다 — ffmpeg 설치가 필요 없다.

```bash
python tools/mp4_to_gif.py 입력.mp4 출력.gif --width 1440 --sample-fps 4 --play-fps 10 --colors 64
python tools/mp4_to_gif.py 입력.mp4 출력.gif --width 500 --colors 48 --crop "680,95,860,860"
```

- `--sample-fps`(원본에서 뽑는 초당 프레임) < `--play-fps`(GIF 재생 fps)면 **그 비율만큼 빨라진다**. 긴 화면 녹화는 2~3배속으로 줄이는 것이 보기 좋다.
- `--crop "x,y,w,h"` — 원본 픽셀 기준으로 잘라낸다. 에디터 전체가 담긴 녹화에서 **게임 뷰만** 뽑을 때 쓴다(작게 나오던 화면이 크게 보인다).
- **용량의 핵심은 공통 팔레트다.** 프레임마다 팔레트를 따로 만들면 매 프레임 팔레트가 실리고 차분 최적화도 막혀 용량이 몇 배가 된다. 이 스크립트는 전체에서 뽑은 팔레트 하나로 통일하고 `disposal=1`로 저장한다 — 실측에서 **17MB → 2.3MB**(같은 해상도·색 수)로 줄었다.
- 정지 화면이 많은 녹화(터미널·에디터)는 차분이 잘 먹어 고해상도를 써도 가볍고, **움직임이 많은 전투 화면은 반대**라 해상도·색 수를 낮춰야 한다.
- 목표 용량은 **5MB 이하**로 잡는다(GitHub에서 로딩이 쾌적하고, 도구로 읽어 확인하기도 좋다).

---

## character_illust_cutout.py — 캐릭터 일러스트 컷아웃 생성기

`Assets/Art/Character/Image/*.png`(직업×성별 8종 전신 일러스트)는 **초록 크로마키 배경**이라 UI에 그대로 얹으면 캐릭터 주변에 초록 사각형이 남는다. 이 스크립트가 배경을 지운 사본과 **얼굴 좌표**를 만든다. 편성창 파티 카드가 이 결과로 얼굴을 확대해 보여준다([파티 편성 UI 기획서](../com2us-taskbar-hero-client/docs/ui/파티-편성-ui-기획서.md)).

- 출력 ①: `Assets/Art/Character/Image/Cutout/<Name>.png` — 배경 투명 + 경계의 초록 프린지 억제 + 가로 1024로 정규화. **원본은 건드리지 않는다.**
- 출력 ②: 콘솔에 얼굴 중심 normalized 좌표(x는 왼쪽부터, **y는 위에서부터**).
- 출력 ③: `--sheet out.png` — 얼굴 크롭 8종을 이어 붙인 대지(프레이밍을 눈으로 확인).

```bash
python tools/character_illust_cutout.py --sheet face_check.png
```

- **얼굴 좌표는 8종 모두 확정돼 있다**(스크립트 안 `FACE_OVERRIDE`). 자동 추정(살색 픽셀의 가장 위 덩어리)은 첫 값을 잡는 용도이고, **최종 값은 편성창을 플레이 모드로 띄워 카드를 보며 맞췄다** — 픽셀아트는 손·팔의 살색 면적이 얼굴보다 크고 무기를 든 손이 머리와 같은 높이에 오는 경우가 많아 자동 추정만으로는 맞지 않는다. 값을 바꿀 때는 반드시 `--sheet` 대지로 확인한다.
- **최종 값은 Unity 쪽이 갖는다** — `Assets/Editor/CharacterIllustrationDatabaseBuilder.cs`의 `FaceAnchors`. 일러스트를 교체하면 ① 이 스크립트로 컷아웃·좌표를 다시 만들고 ② 그 좌표를 빌더에 옮긴 뒤 ③ Unity 메뉴 **`TaskbarHero/캐릭터/일러스트 DB 빌드`** 를 실행한다(컷아웃 임포트 규격 교정 + `CharacterIllustrationDatabase` 재생성).
- 의존성: `pillow`, `numpy` (`python -m pip install pillow numpy`)
