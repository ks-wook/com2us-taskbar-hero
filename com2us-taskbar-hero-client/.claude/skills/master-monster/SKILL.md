---
name: master-monster
description: 몬스터를 능력치·외형·프리팹·전투 등장까지 한 번에 만들거나, 기존 몬스터의 외형·마리 수·능력치를 바꾼다. 사용자가 "~ 외형의 몬스터를 만들어줘, ~지역 ~스테이지에 ~마리 등장할 몬스터야" 같은 한 줄을 주면 서버 정본(master-data-값.md §9·§11 · master-data-schema.sql)과 외형 레시피에 반영하고, monster_{code}.prefab을 생성해 Assets/Prefabs/Character/Monster/에 넣고, 던전 전투 배선과 클라이언트 번들 재생성까지 수행해 실제 전투에 등장시킨다. "몬스터 추가", "새 보스 만들어줘", "~지역 ~스테이지에 ~마리 추가/배치해줘", "몬스터 마리 수 조정", "몬스터 능력치 조정", 그리고 능력치는 그대로 두고 생김새만 바꾸는 "몬스터 외형 교체/프리팹 교체", "외형 다시 뽑아줘"에 사용한다.
---

# 몬스터 만들기 (능력치 → 외형 → 프리팹 → 실제 전투 등장)

**이 스킬의 목표는 "입력 한 줄 → 그 몬스터가 실제 전투에 나온다"까지다.** 사람이 Unity를 조작하는 단계는 남기지 않는다.

```
① 입력 해석 → ② 능력치·외형 결정 → ③ 정본 + 스폰 반영 → ④ 프리팹 생성 → ⑤ 전투 배선 → ⑥ 번들 재생성 → ⑦ 검증
                    └────────── ③ 도구(CLI) ──────────┘   └── ④⑤ Unity MCP ──┘   └ ⑥ CLI ┘
```

모든 CLI 명령은 **저장소 루트**(`com2us-taskbar-hero/`)에서 실행한다.

**왜 이 순서인가** — `master_data_export.py`는 `schema.sql → MySQL → 클라 번들` **단방향**이다. 정본을 먼저 맞추지 않고 ⑥을 돌리면 앞선 수정이 덮어써진다. 반대로 ④는 **레시피 JSON**만 있으면 되므로 ⑥ 전에 할 수 있다.

### 요청 종류에 따라 흐름을 고른다

| 사용자가 원하는 것 | 할 일 | 번들 재생성(⑥) |
|---|---|---|
| **새 몬스터**를 만들어 전투에 등장시킨다 | ①~⑦ 전체 | 필요 |
| **외형만 교체**한다(능력치 유지) | 아래 「외형 교체」 절 (`reskin`) | **불필요** |
| **마리 수만** 조정한다 | `spawn` → ⑥ | 필요 |
| **능력치만** 조정한다 | `add --code <코드> …` → ⑥ | 필요 |

전부를 기계적으로 돌리지 말고, 요청에 해당하는 것만 한다 — 특히 **외형 교체는 서버 정본·DB·번들을 건드리지 않는다.**

## ① 입력 해석

사용자는 보통 **외형 컨셉 · 지역(Act) · 스테이지**를 준다. 부족한 값은 아래 규칙으로 채우고 **되묻지 않는다**.

| 항목 | 값 | 기본값 |
|---|---|---|
| `--act` | 1~5 | 컨셉에서 역추론 — Act1 빙의된 인간 · Act2 언데드 · Act3 악마 · Act4 타락한 성기사/천사 · Act5 공허 |
| `--stage` | 1~10 | 명시가 없으면 **5**(그 Act 중간). 능력치 추천 기준이 된다 |
| `--boss` | 보스 여부 | "보스"라는 말이 있으면 붙인다 → 코드 `xx99`, 능력치는 보스 실측값 |
| `--spawn` | 배치할 스테이지·마리 수 | **사용자가 스테이지를 말했으면 반드시 넣는다**(안 넣으면 전투에 안 나온다) |
| 마리 수 | 사용자가 말했으면 **그 값을 그대로** | 말하지 않았으면 4~6마리(그 스테이지 총량을 보고 정한다) |
| `monster_code` | 자동 채번 | 지정 금지(기존 몬스터 수정 시에만 `--code`) |
| `hp`·`attack` | Act·스테이지에서 자동 산출 | 사용자가 수치를 주면 그 값 우선 |

### 마리 수 표현 해석

`--spawn`/`--stages`의 값은 **`스테이지:마리수`** 이고, 마리 수에 **부호를 붙이면 증분**이다. 사용자 표현을 이렇게 옮긴다.

| 사용자가 말한 것 | 해석 | 명령 |
|---|---|---|
| "2지역 5스테이지에 **4마리** 나오게" | 그 스테이지를 4마리로 **확정** | `--spawn "5:4"` |
| "5스테이지에 스켈레톤 궁수 **4마리 추가**해줘" (신규 몬스터) | 새로 넣는 것이므로 확정 | `--spawn "5:4"` |
| "5스테이지 스켈레톤 병사 **3마리 더** 넣어줘" (이미 있는 몬스터) | 현재 값 + 3 | `spawn --code 9101 --stages "5:+3"` |
| "5·6스테이지에 **4마리, 5마리**" | 스테이지별 개별 지정 | `--spawn "5:4,6:5"` |
| "5~7스테이지에 **5마리씩**" | 같은 값 반복 | `--spawn "5:5,6:5,7:5"` |

**"N마리 추가"가 애매하면 이렇게 가른다** — 그 스테이지에 그 몬스터가 **아직 없으면 확정**(`5:4`), **이미 있으면 증분**(`5:+4`). 도구가 `이전 → 이후` 와 그 스테이지 **총 마리 수**를 함께 출력하므로, 그 값을 보고에 그대로 옮겨 사용자가 확인할 수 있게 한다.

**총량을 항상 확인한다** — 값 문서 §11 기준 일반 스테이지는 **8~16마리**다. 도구가 범위를 벗어나면 경고를 찍는다. 새 몬스터를 끼워 넣어 총량이 늘면 클리어 시간이 그만큼 길어지므로, ⓐ 사용자가 마리 수를 명시했으면 그대로 두고 총량 변화를 보고에 적고, ⓑ 명시하지 않았으면 기존 몬스터를 줄여 총량을 유지한다(`spawn --code <기존> --stages "5:-2"`).

## ② 능력치·외형 결정

**능력치** — 도구가 클라이언트 `MonsterStatCurve`와 같은 산식으로 뽑는다.

```bash
python tools/master_monster_tool.py recommend --act 2 --stage 5     # → hp 120 / attack 16
python tools/master_monster_tool.py list                            # 기존 몬스터·프리팹 현황
```

추천을 그대로 쓰는 것이 기본이다. 조정은 두 경우만 — 사용자가 방향을 준 경우(±10~30%), 같은 Act에서 역전이 생기는 경우. **보스 `attack`은 그 Act 최강 일반의 1.3~1.45배를 넘기지 않는다**(아군 즉사 방지 — 값 문서 §9).

**외형** — SPUM 태그로 정한다. `--race` 1개 + `--classes` 콤마 구분(AND).

| race | 계열 | 어울리는 classes | Act |
|---|---|---|---|
| `human` | 빙의된 인간·광신도·배교자 | `melee,physical` · `melee,damage` · `magical,damage` | 1 (·4) |
| `undead` | 스켈레톤·구울·리치 | `melee,physical` · `melee,tank` · `ranged,physical` · `tank,magical` | 2 |
| `devil` | 임프·데몬·서큐버스·발록 | `melee,damage` · `magical,damage` · `ranged,damage` | 3 (·5) |
| `orc` | 오크 전사·주술사·오우거 | `melee,tank` · `magical,support` | — |
| `elf` | 다크엘프 궁수·숲의 파수꾼 | `ranged,physical` · `ranged,damage` | — |
| `highelf` | 타락한 대천사·빛의 배신자 | `magical,support` · `magical,tank` | 4 |

- `--theme`는 기본 `fantasy`. `--gender`는 생략하면 무제한(시드가 고른다).
- 무기 등을 못 박으려면 `--fixed-parts "Weapons=Bow_1"`(값은 **SPUM 파일명**). 무기 2개는 `"Weapons=Sword_1,Shield_2"`, 이때 항목 구분은 세미콜론.
- 계열 색은 `--colors "Body=#8FB7A8"` — **`Body`·`Hair`·`Cloth` 3키만**.
- **`Style` 축은 쓰지 않는다.** 태그 DB에 실재하는 축은 `Part`·`Race`·`Gender`·`Theme`·`Class`뿐이다.
- 태그 값이 DB에 없으면 ④에서 실패한다. 위 표 밖의 값을 쓰려면 먼저 확인할 것.

## ③ 정본 + 스폰 반영

```bash
python tools/master_monster_tool.py add --name "스켈레톤 궁수" --act 2 --stage 5 \
    --race undead --classes ranged,physical --spawn "5:4,6:5"
```

한 명령이 **네 곳**을 맞춘다 — `master-data-값.md` §9 표·「규모/현황」 · `master-data-schema.sql` `monster_master` INSERT·머리 주석 · `Assets/Dev/monster-appearance-recipe.json` · (`--spawn` 시) `stage_spawn` INSERT와 §11 요약 문장. 끝나면 **자동으로 `verify`** 가 돈다.

- `--spawn "5:4,6:5"` = 5스테이지 4마리, 6스테이지 5마리. **난이도 1·2 양쪽에 들어간다**(현행 데이터가 동일 구성). 증분(`5:+3`)은 난이도별 현재 값에 각각 더한다.
- 보스는 `stage_spawn`이 아니라 `stage_master.boss_monster_code`로 배치된다 — 그 Act의 `xx99`는 이미 10스테이지에 걸려 있으므로, **기존 보스를 교체**하는 것이 아니면 새 보스는 배치되지 않는다. 그 사실을 사용자에게 알린다.
- 이미 있는 몬스터의 배치만 바꾸려면 `spawn` 서브커맨드를 쓴다: `spawn --code 9101 --stages "5:9"`.
- 기존 몬스터 수정은 `--code 9201`. 이때 외형 레시피는 보존된다(`--replace-recipe`로 덮어쓰기).
- 씬(F12)으로 클라 번들만 먼저 고쳤다면 `adopt-bundle`로 정본에 올린다.
- **세 파일을 손으로 편집하지 말 것.**

## ④ 프리팹 생성 (Unity MCP · 자동)

`MonsterPrefabBuilder`가 레시피에서 `monster_{code}.prefab`을 만들어 `Assets/Prefabs/Character/Monster/`에 넣는다. **플레이 모드도, 사용자 조작도 필요 없다.**

```csharp
// mcp__unity-mcp__Unity_RunCommand
EditorApplication.ExecuteMenuItem("TaskbarHero/몬스터/프리팹 생성 (누락분만)");
```

**반드시 `ExecuteMenuItem`으로 부른다.** 지켜야 할 함정 세 가지:

- **MCP 샌드박스는 `PrefabUtility.SaveAsPrefabAsset`을 차단한다** — `MonsterUnitFactory.BuildPrefab`을 커맨드에서 직접 부르면 "User interactions are not supported"로 실패한다. 메뉴 실행은 샌드박스 밖 경로라 통과한다.
- **`EditorApplication.delayCall` 우회는 쓰지 말 것** — 에디터가 백그라운드(`isFocused=False`)면 틱이 돌지 않아 영원히 실행되지 않고, 동적 커맨드의 람다는 어셈블리가 해제되며 사라진다.
- **Unity 에디터가 떠 있지 않으면 여기서 멈추고 사용자에게 알린다.** 대신 띄우려 하지 않는다.

생성물은 저장 직후 3항목(루트 `SPUM_Prefabs` · `_anim` 배선 · 상태별 클립 리스트)이 검증되고, 실패하면 백업으로 되돌아간다. 조립은 **프리뷰 씬**에서 하므로 열려 있는 씬이 dirty해지지 않는다.

## ⑤ 전투 배선 (Unity MCP · 자동)

```csharp
EditorApplication.ExecuteMenuItem("TaskbarHero/몬스터/신규 몬스터 전투 반영 (프리팹 + 전투 배선)");
```

④와 `DungeonBattleBuilder.Build()`(코드→프리팹 맵 갱신)를 이어서 실행하고, 끝나면 원래 열려 있던 씬으로 되돌린다. **④를 따로 돌렸다면 이것만 불러도 된다**(누락분만 생성하므로 중복 작업이 없다).

- 열려 있는 씬에 저장 안 된 변경이 있으면 **중단**한다(리포트에 그 사유가 나온다). 사용자에게 저장을 요청한다.
- 결과는 `MonsterPrefabBuilder.LastReport`로 읽는다.

## ⑥ 클라이언트 번들 재생성

```bash
python tools/master_data_export.py
```

`schema.sql`을 MySQL에 재적용하고 `Assets/Resources/MasterData/*.json`을 다시 만든다. **전투는 클라 권위**라 런타임 값(hp·attack·스폰)이 이 번들에서 온다 — 이 단계까지 끝나야 게임이 새 몬스터를 읽는다.

- **MySQL이 안 떠 있으면 거기서 멈추고 그 사실만 알린다.** 정본·레시피·프리팹은 이미 반영된 상태다. DB나 서버를 대신 띄우지 않는다.
- 번들이 바뀌었으므로 Unity에서 `AssetDatabase.Refresh()`를 한 번 태운다(MCP 커맨드로 가능).

## ⑦ 검증

```bash
python tools/master_monster_tool.py verify      # 오류 0 확인
```

- **오류**: 값.md ↔ schema.sql 불일치 · 코드 규약 위반 · 개수/총행수 문장 불일치 · `stage_spawn`에 없는 몬스터나 보스가 들어감.
- **경고 중 반드시 확인할 것**: `어느 스테이지에도 배치되지 않아 전투에 등장하지 않습니다` — 사용자가 스테이지를 지정했다면 이 경고가 남으면 안 된다.
- **`값.md §11 (B) 요약 표의 전제와 다릅니다` 경고가 뜨면 그 표를 직접 손본다.** 그 표는 "Act1·2는 2종 / Act3~5는 1종"을 전제로 그려져 있어 종이 늘면 열·절을 사람이 맞춰야 한다(도구가 자동 생성하지 않는 유일한 곳).

Unity 쪽 최종 확인은 MCP로 프리팹 존재와 맵 등록을 조회한다.

## 외형 교체 (능력치는 그대로)

"이 몬스터 외형을 ~로 바꿔줘" 처럼 **생김새만** 갈아 끼우는 요청이다. `add`가 아니라 `reskin`을 쓴다 — `add --code`는 능력치를 다시 계산해 덮어쓰므로 이 용도에 맞지 않는다.

```bash
# 새 컨셉으로 교체
python tools/master_monster_tool.py reskin --code 9101 --race devil --classes melee,damage \
    [--colors "Body=#8B2E2E"] [--fixed-parts "Weapons=Sword_1"] [--dry-run]

# 태그는 그대로 두고 "다시 뽑기"(마음에 안 들 때)
python tools/master_monster_tool.py reskin --code 9101 --reroll
```

바뀌는 것은 **외형 레시피 하나**뿐이다. `monster_master`(이름·hp·attack)와 `stage_spawn`(배치)은 건드리지 않으므로 **서버 정본·DB·클라 번들은 그대로**다. 실행하면 이전/새 레시피와 "능력치 유지" 사실을 함께 출력하니 그대로 보고에 옮긴다.

이어서 프리팹만 다시 만든다.

```csharp
// mcp__unity-mcp__Unity_RunCommand
EditorPrefs.SetString("TaskbarHero.Dev.MonsterRebuildCodes", "9101");   // 여러 개면 "9101,9102"
EditorApplication.ExecuteMenuItem("TaskbarHero/몬스터/외형 교체 반영 (지정 코드 재생성)");
```

- 기존 프리팹은 `Assets/Dev/MonsterPrefabBackup/`에 백업된 뒤 **같은 경로에 덮어써진다 → `.meta`의 GUID가 유지**된다. 따라서 `DungeonBattleFlow`의 코드→프리팹 맵이 그대로 유효해 **전투 배선을 다시 돌릴 필요가 없다.**
- 능력치·배치가 그대로이므로 **`master_data_export.py`도 돌리지 않는다.**
- 대상 코드는 EditorPrefs로 넘긴다(MCP 커맨드는 메뉴 실행만 통과하므로). 키는 실행 시 **한 번 쓰고 지워진다.**
- 교체 전후로 스프라이트 수 등을 조회해 **외형이 실제로 바뀌었는지 확인**하고 보고한다.

**"외형이 마음에 안 든다"는 후속 요청**에는 `--reroll`로 시드만 옮겨 다시 뽑고 프리팹을 재생성한다(태그가 문제라면 태그를 바꾼다). 몇 번 돌려도 능력치·배치는 영향받지 않는다.

### 외형을 손볼 때 알아야 할 것 (실측)

- **`colors`는 곱셈 틴트다 — 원본보다 밝게 만들 수 없다.** "더 밝게 해 달라"는 요청에 밝은 색(`#7A6B8C` 등)을 주면 오히려 **더 어두워진다**. 밝히려면 그 파트의 **색 지정을 아예 빼서 원본 밝기를 되찾거나**, 밝은 원본을 가진 파츠로 교체한다. `#FFFFFF`가 원본 그대로이고 그보다 밝아질 수 없다.
- **파트가 담당하는 부위** — `Body`는 **피부(얼굴·손·발)만**이고 몸통은 `Armor`(가슴 갑옷)·`Cloth`·`Pant`·`Back`(망토)이 덮는다. "몸통을 밝게"에 `Body`를 건드리면 얼굴만 바뀐다.
- **뿔·볏 같은 머리 장식은 `Helmet` 파츠에 들어 있다**(`Body`가 아니다). 뿔을 키우려면 뿔이 큰 투구로 바꾼다 — `New_Helmet_04`(위로 크게 두 갈래) · `New_Helmet_01`·`Helmet_4`·`Helmet_5`(옆으로 벌어짐). `New_Helmet_02/03/06`, `Helmet_1/2/3/7/9`는 뿔이 없다.
- **파츠 크기는 레시피로 조절할 수 없다.** 스케일 필드가 없으므로 "더 크게"는 **더 큰 파츠를 고르는 것**으로만 답한다.
- **눈으로 확인하고 판단한다.** 프리뷰 씬에 프리팹을 세우고 직교 카메라 → `RenderTexture` → `EncodeToPNG`로 PNG를 남긴 뒤 그 파일을 읽으면, 열려 있는 씬을 건드리지 않고 결과를 볼 수 있다(`orthographicSize` 0.62 / 카메라 y 0.62가 한 몸 가득). 후보를 고를 때는 격자 시트로 한 장에 모아 비교하고, **밝은 배경과 어두운 배경 두 장**을 내어 던전에서 묻히지 않는지 본다.
- **어느 부위가 어느 파트인지 모를 때는 파트별로 원색(빨강·초록·파랑…)을 칠해 한 장 렌더**하면 즉시 판별된다.

## 보고 형식

표로 정리한다 — **코드 · 이름 · Act/보스 · hp·attack(추천 대비) · 외형 태그 · 배치(스테이지별 `이전 → 이후` 마리 수와 그 스테이지 총량) · 프리팹 · verify 결과**. 판단으로 채운 값(Act 추론·태그 선택·마리 수 조정)은 근거를 한 줄씩 붙인다. **막힌 단계가 있으면 무엇이 남았는지 명시**한다(예: MySQL 미기동으로 ⑥ 미완).

## 하지 말 것

- 정본 3파일(`master-data-값.md`·`master-data-schema.sql`·`monster-appearance-recipe.json`)을 손으로 편집
- `monster_code` 임의 지정(Act 대역·보스 `xx99` 규약이 깨진다 — 확인만 하려면 `next-code`)
- 서버 정본을 건너뛰고 씬 F12로만 번들 수정(다음 export에서 사라진다)
- MCP 커맨드에서 `MonsterUnitFactory.BuildPrefab`·`SaveAsPrefab` 직접 호출(샌드박스가 막는다 — 반드시 `ExecuteMenuItem`)
- 어시스턴트가 Unity 에디터·MySQL·게임 서버를 기동하는 것
- 사용자가 스테이지를 지정했는데 `--spawn` 없이 끝내는 것(만들어도 전투에 안 나온다)
- **외형만 바꾸는 요청에 `add --code`를 쓰는 것** — 능력치가 추천값으로 덮어써진다. `reskin`을 쓸 것
- 외형 교체 뒤에 `master_data_export.py`나 전투 배선을 돌리는 것(바뀐 게 없다 — GUID가 유지되므로 불필요한 DB 재적용·씬 수정만 남는다)
- `verify`에 **오류**가 남은 상태로 완료 보고
