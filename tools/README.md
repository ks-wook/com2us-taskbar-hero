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
