# 스킬 레벨업 UI 기획서 (클라이언트)

> 본 문서는 **Unity 클라이언트**의 캐릭터 스킬 레벨업 화면(UI 표현·조작·상태 연동)을 다룬다.
> 스킬/스킬 포인트의 **서버 권위 규칙·데이터 모델·API·에러코드**는 서버 기획서가 정본이며 여기서 복제하지 않는다:
> [서버 성장(직업·스킬·룬) 기획서](../../../docs/세부/growth-기획서.md) · [API 통합](../../../docs/공통/api-통합.md) · [ErrorCode 통합 정의](../../../docs/공통/error-code-정의.md).
> 인벤토리 패널에서 진입하므로 [인벤토리 UI 기획서](인벤토리-ui-기획서.md)와 짝을 이룬다.

## 1. 개요

- **목적**: 플레이어가 각 캐릭터의 **스킬을 레벨업**(스킬 포인트 소모)하고, 필요 시 **초기화**(무료)할 수 있는 오버레이 패널을 제공한다. 화면은 인벤토리와 동일하게 게임 뷰 위에 겹쳐 뜨는 **오버레이 패널**이며 `UIManager`로 On/Off한다.
- **진입점**: **인벤토리 패널 → '스킬 레벨업' 버튼**(장비 라벨 우측)으로 연다. 스킬 패널은 인벤토리 위(sortingOrder 110 > 인벤토리 100)에 겹쳐 뜨고, 닫으면 인벤토리로 돌아간다.
- **대상**: **클라이언트(Unity) 전용**. 표현·입력·서버 요청만 담당하고, 레벨업 성립·스킬 포인트·최대 레벨 등 모든 확정은 서버(GameServer)가 한다(서버 권위).
- **범위 경계**: 본 문서는 **스킬 레벨업 + 스킬 초기화 + 스킬 포인트 표시 + 액티브 스킬 장착(2슬롯)**(서버 API 5.1·5.2·5.3)을 다룬다. **룬 트리 UI**(5.4)는 [룬 UI 기획서](룬-ui-기획서.md)에서 별도로 다룬다.
- **관련 기획서**: [서버 성장 기획서](../../../docs/세부/growth-기획서.md)(스킬 레벨업 5.1·초기화 5.2), [마스터 데이터 기획서](../../../docs/세부/master-data/master-data-기획서.md)(`skill_master`·`level_master`·`class_master`).

## 2. 기능 설명 (사용자 관점)

- 인벤토리에서 **스킬 레벨업** 버튼을 누르면 스킬 패널이 열린다. 상단 **캐릭터 네비게이션**(◀ 직업 Lv.N · i/N ▶)으로 3인 파티 중 대상 캐릭터를 고른다.
- 그 아래 **스킬 포인트 배너**가 `사용 가능 / 총량`을 보여준다. 스킬 포인트는 **저장값이 아니라 캐릭터 레벨에서 파생**한다 — `사용 가능 = 레벨 비례 총량(level_master.skill_points) − 그 캐릭터가 이미 투자한 스킬 레벨 합`.
- 포인트 배너 아래 **장착 액티브 스킬 슬롯(최대 2)**: 현재 장착된 액티브 스킬을 아이콘+이름으로 보이고(없으면 '비었음'), 슬롯을 클릭하면 해제한다.
- 본문은 **선택 캐릭터 직업 소속 스킬 목록**(스크롤). 각 행은 **아이콘 + 스킬 이름만** 노출하고 **레벨업 버튼(SP 1)**, 액티브 스킬은 **장착/해제 토글 버튼**(미습득이면 '미습득' 비활성)을 둔다. 타입(액티브·패시브)·레벨·효과·재사용 대기시간 등 **상세 정보는 행에 마우스를 올리면(hover) 인벤토리 아이템처럼 별도 툴팁**으로 뜬다.
  - 레벨업 버튼은 **레벨 < 최대 && 사용 가능 포인트 ≥ 1**일 때만 활성화된다. 최대 레벨이면 `MAX`로 잠긴다.
  - 장착: 습득(레벨 ≥ 1)한 액티브만 가능하며 **캐릭터당 최대 2개**. 초과 시도는 클라에서 막고 서버도 `ActiveSkillLimitExceeded(5007)`로 재검증한다.
- 하단 **스킬 초기화(무료)** 버튼은 그 캐릭터의 모든 스킬 레벨을 0으로 되돌려 포인트를 전량 회수한다(장착도 함께 해제).
- **방치형 특성**: 인벤토리와 마찬가지로 뒤의 자동 전투 연출은 계속 돈다. 진행을 막는 모달이 아니라 성장 조작 패널이다.

## 3. 요구사항

**기능 요구사항**
- (F1) 인벤토리 패널의 '스킬 레벨업' 버튼으로 `UIManager.Show(PanelType.Skill)` 진입. 패널 닫기(닫기 버튼·딤 클릭)로 스킬 패널만 닫는다.
- (F2) 대상 캐릭터를 파티 네비게이션으로 전환한다(순환). 캐릭터별로 직업 소속 스킬만 노출한다(`skill_master.class_code == player_character.class_code`).
- (F3) 스킬 포인트(사용 가능/총량)를 **세션 스냅샷에서 파생 계산**해 표시한다(서버와 동일 공식). 저장 컬럼을 신뢰하지 않는다.
- (F4) 레벨업/초기화는 서버 API를 호출하고, **성공 시 세이브를 재로드**(`/api/game/load`)해 세션·UI·전투 상태를 갱신한다.
- (F5) 서버 오류(포인트 부족·최대 레벨·직업 불일치 등)는 하단 안내 문구로 한글화해 보여준다(`ErrorMessages`).

**비기능 요구사항**
- (N1) **서버 권위**: 버튼 활성/포인트 표시는 UX 힌트일 뿐, 성립 여부는 서버가 최종 확정한다. 클라 표시와 서버 판정이 어긋나면 서버 응답을 따른다.
- (N2) **중복 요청 방지**: 요청 진행 중(`_busy`)에는 추가 레벨업/초기화를 막는다.

## 4. 화면 구성 / UI 리소스

- 캔버스: `ScreenSpaceOverlay`, `sortingOrder=110`(인벤토리 100 위). 기준 해상도 1080×1920.
- UI 스프라이트는 **인벤토리와 공용**(`Assets/Art/UI/Inventory`의 `ui_panel_background`·`ui_slot_normal`·`ui_slot_highlight`).
- 스킬 아이콘은 **전투 아이콘 재사용**: `Assets/Art/Icon/Combat/{Knight|Archer|Mage}/{스킬명}.png`. `SkillIconDatabase`(Resources)가 `skill_code → Sprite`를 들고, `SkillIconDatabaseBuilder`가 `skill_master` 이름과 파일명을 **공백 무시**로 대조해 채운다(예: "파이어볼" == "파이어 볼").
- 계층(정적): `PanelRoot`(배경, 860×940) 안에 `Title`·`CloseButton`·`CharNav`(◀▶+인디케이터)·`PointBanner`·`EquipSlots`(장착 슬롯 2)·`ListArea`(ScrollRect+Viewport+Content, **우측 세로 스크롤바 `SkillScrollbar` 상시 표시**)·`Message`·`ResetButton`. **스킬 행은 런타임에 `Content`(VerticalLayoutGroup)로 생성**한다.
- **목록 스크롤**: 목록 뷰포트(372px)는 기본적으로 스킬 **약 2.5개**가 보이도록 잡고, 나머지는 세로 스크롤(우측 스크롤바)로 본다.
- **hover 툴팁**(`SkillTooltip`): 루트에 붙은 정보 전용(raycast 비활성) 패널로, 행에 hover 시 이름·타입/레벨·**스킬 설명(`skill_master.description`)**·효과(계수)·재사용 대기시간을 커서 근처에 표시하고 이탈 시 숨긴다(인벤토리 아이템 툴팁과 동일 UX).

## 5. 데이터 바인딩 (표시용 소스)

- 파티/레벨: `Session.GameData.characters`(`CharacterDto`: characterId·classCode·level).
- 스킬 레벨: `Session.GameData.skills`(`SkillDto`: characterId·skillCode·level·equipped).
- 마스터: `MasterDataManager.Db`의 `Skills`(`skill_master`)·`Classes`(`class_master`)·`Levels`(`level_master`).
- 파생 계산(클라·서버 동일):
  - `총량 = Levels[charLevel].skillPoints`
  - `사용량 = Σ(그 캐릭터 스킬 레벨)` (1레벨당 1포인트)
  - `사용 가능 = max(0, 총량 − 사용량)`

## 6. UIManager 연동 및 On/Off 정책

- `UIManager.PanelType.Skill` 추가. `ShowSkill()`/`ToggleSkill()`/`Show(PanelType.Skill)`로 연다.
- 프리팹 `Assets/Prefabs/UI/SkillPanel.prefab`을 **Title·GameScene 양쪽 UIManager**의 `skillPanelPrefab`에 배선한다(DontDestroyOnLoad 인스턴스가 씬 전환에도 유지되므로).

## 7. 스크립트 / 에디터 도구 구성

- `Assets/Scripts/UI/SkillPanelController.cs` (asmdef `TaskbarHero.Client.UI`) — 패널 계층 생성·세션 바인딩·서버 연동.
- `Assets/Scripts/Managers/SkillIconDatabase.cs` (asmdef `TaskbarHero.Client.Managers`) — `skill_code → Sprite` 조회 SO.
- `Assets/Editor/SkillIconDatabaseBuilder.cs` — 메뉴 **TaskbarHero/UI/스킬 아이콘 DB 빌드**.
- `Assets/Editor/SkillUiBuilder.cs` — 메뉴 **TaskbarHero/UI/스킬 패널·씬 생성**(아이콘 DB 빌드 + 프리팹 생성 + Title/GameScene 배선).
- 인벤토리 진입점: `InventoryPanelController`에 '스킬 레벨업' 버튼 추가(`OnOpenSkillPanel`).

## 8. 상호작용 흐름

1. 인벤토리 → '스킬 레벨업' → `UIManager.Show(Skill)` → `OnEnable` → `RefreshFromSession()`.
2. 레벨업 버튼 → `POST /api/game/growth/skill/levelup {characterId, skillCode}` → 성공 → `/api/game/load` 재로드 → `Session.SetGameData` → 목록/포인트 재계산 → `Session.RaiseInventoryChanged()`(전투 재계산).
3. 초기화 버튼 → `POST /api/game/growth/skill/reset {characterId}` → 동일하게 재로드·갱신.
4. 실패 → `ErrorMessages.ToKorean(error)`를 하단 안내에 표시.

## 9. 서버 연동 의존성 (선행 필요)

- GameServer 성장 API(`/api/game/growth/skill/levelup`·`/skill/reset`) — **서버 구현 완료**.
- 번들 마스터 데이터(`skill_master`·`level_master`·`class_master`) 최신화(에디터 **TaskbarHero/마스터 데이터** 메뉴).

## 10. 미결 사항 / TODO

- 인벤토리 선택 캐릭터와 스킬 패널 선택 캐릭터 **동기화**(현재는 스킬 패널이 자체 네비게이션 유지).
- 장착 슬롯의 **슬롯 지정 선택 흐름**(빈 슬롯 클릭 → 목록에서 지정) — 현재는 행의 장착/해제 토글 + 슬롯 클릭 해제로 대체.

> 액티브 스킬 장착(2슬롯)은 **구현·실서버 연동 검증 완료**(장착/해제 UI + 5005/5006/5007 거부 E2E). 룬 트리 UI는 [룬 UI 기획서](룬-ui-기획서.md) 참조.
>
> **전투 연동**: 인게임 전투(`PlayerCombatant`)는 serverMode에서 **그 캐릭터가 장착(equipped=1)한 액티브 스킬만**(최대 2) 실제 습득 레벨로 사용한다. 장착/해제·레벨업 시 `Session.InventoryChanged`로 파티 스킬 세트와 전투 스킬 HUD(`SkillCooldownUI`)가 다시 구성된다. 스킬 포인트 총량은 캐릭터 레벨(`level_master.skill_points`)에 비례한다.
>
> **패시브 능력치 상승**: 학습(레벨 ≥ 1)한 **패시브 스킬**(`skill_type=2`)은 장착과 무관하게 상시 적용된다. `PlayerCombatant.ApplyEquipStats`가 패시브의 `statType`별 레벨 배율(coef)을 곱해 전투 스탯에 반영한다(공격력 1 → `_atk`, 체력 3 → `_maxHp`, 이동속도 6 → `_moveSpeed`). 레벨업 시 `InventoryChanged`로 즉시 재계산된다. (검증: 불굴 Lv.1 습득 시 기사 MaxHp 130→136 = ×1.05, 실서버 E2E 통과.)
