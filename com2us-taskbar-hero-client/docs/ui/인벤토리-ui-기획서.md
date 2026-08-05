# 인벤토리 UI 기획서 (클라이언트)

> 본 문서는 **Unity 클라이언트**의 인벤토리/장비 화면(UI 표현·조작·상태 연동)을 다룬다.
> 인벤토리/아이템의 **서버 권위 규칙·데이터 모델·API·에러코드**는 서버 기획서가 정본이며 여기서 복제하지 않는다:
> [서버 인벤토리/아이템/큐브 기획서](../../../docs/세부/inventory-item-cube-기획서.md) · [API 통합](../../../docs/공통/api-통합.md) · [ErrorCode 통합 정의](../../../docs/공통/error-code-정의.md).
> 클라이언트 씬/UI 큰 그림은 [클라이언트 씬 / 오브젝트 / UI 구성](../클라이언트-씬-UI-구성.md) 3.4-(C) `InventoryPanel` 항목에 대응한다.

## 목차

- [1. 개요](#1-개요)
- [2. 기능 설명 (사용자 관점)](#2-기능-설명-사용자-관점)
- [3. 요구사항](#3-요구사항)
- [4. 화면 구성 / UI 리소스](#4-화면-구성--ui-리소스)
- [5. 데이터 바인딩 (표시용 소스)](#5-데이터-바인딩-표시용-소스)
- [6. UIManager 연동 및 On/Off 정책](#6-uimanager-연동-및-onoff-정책)
- [7. 스크립트 / asmdef 구성](#7-스크립트--asmdef-구성)
- [8. 상호작용 흐름](#8-상호작용-흐름)
- [9. 서버 연동 의존성 (선행 필요)](#9-서버-연동-의존성-선행-필요)
- [10. 미결 사항 / TODO](#10-미결-사항--todo)

## 1. 개요

- **목적**: GameScene에서 플레이어가 보유 아이템을 확인하고 각 캐릭터의 **장착 장비(6부위)**를 관리할 수 있는 인벤토리 화면을 제공한다. 화면은 게임 뷰 위에 겹쳐 뜨는 **오버레이 패널**로, `UIManager`를 통해 열고 닫는다(On/Off). 아이템 아이콘 그리드 + 캐릭터 장비 슬롯 + 아이템 상세/장착·해제 조작으로 구성한다.
- **대상**: **클라이언트(Unity) 전용**. 표현(presentation)과 사용자 입력·서버 요청만 담당하고, 수량·등급·장착 성립 여부 등 모든 확정은 서버(GameServer)가 한다(서버 권위, [클라이언트 씬 구성](클라이언트-씬-UI-구성.md) 1장).
- **범위 경계**:
  - 본 문서는 **인벤토리 보기 + 장착/해제 UI**를 1차 범위로 한다. 큐브(합성/분해/제작)·**장비 강화**는 별도 패널(`CubePanel`)이 담당하며([클라이언트 씬 구성](../클라이언트-씬-UI-구성.md) 3.4 기능 패널 표), 인벤토리 패널은 그 결과인 **강화 단계만 표시**한다 — **슬롯 좌측 하단의 빨간 "+N" 배지**(공용 아이템 슬롯 프리팹 규격, 아래 9.4), 아이템 이름 "+N", 옵션 스탯·능력치 합산에 `enhance_master` 배율 반영. 랜덤 상자·드래그 이동은 여전히 후속(10장)이다.
  - 아이템 획득(전투 드롭)·오프라인 보상 등은 다른 도메인이 담당하고, 인벤토리 UI는 그 결과가 반영된 세이브 스냅샷을 **표시**만 한다.
- **관련 기획서**: [서버 인벤토리/아이템/큐브 기획서](../../../docs/세부/inventory-item-cube-기획서.md)(장착 5.1·해제 5.2·이동 5.5), [클라이언트 씬 / 오브젝트 / UI 구성](../클라이언트-씬-UI-구성.md)(3.4 Main 씬 패널), [마스터 데이터 기획서](../../../docs/세부/master-data/master-data-기획서.md)(`item_master`·`equip_slot_master`·`grade_master`).

## 2. 기능 설명 (사용자 관점)

- GameScene 진입 후 HUD의 **인벤토리 버튼**을 누르면 인벤토리 패널이 게임 화면 위에 열린다. 같은 버튼(또는 **패널 바깥 영역**/`ESC`)으로 닫는다 — **토글 On/Off**. 창 안에 닫기(X) 버튼은 두지 않는다(미관상 제거).
- 패널은 두 영역으로 나뉜다.
  - **장비 영역**: 현재 선택된 캐릭터의 초상과 **6개 장착 슬롯**(무기·보조무기·투구·갑옷·장갑·신발, `equip_slot_master` 1~6). 각 슬롯은 장착된 아이템 아이콘을 보이거나, 비었으면 부위 표시만 보인다.
  - **능력치 패널**(초상 좌측, 220×300): 최종 능력치 = **(클래스+레벨 기본 + 장착 장비) × 학습 패시브 배율**. 아이템·패시브로 인한 **상승분은 `+상승분`** 으로 병기하며, 그 텍스트만 **연한 파란색**(`#8FC1FF`)으로 표시한다(기본값은 흰색). 패시브 배율·statType 매핑은 전투(`PlayerCombatant`)와 동일하다.
    - **능력치명 열과 값 열을 별도 `Text` 두 개로 나눈다** — 이름은 왼쪽 정렬(`StatNames`, x 25 · 폭 64), 값은 **오른쪽 정렬**(`StatValues`, x 95 · 폭 98)이라 자릿수가 달라도 **세로 줄이 맞는다**. 두 열은 **같은 폰트 크기 17 · 같은 줄 간격 1.4 · 같은 줄 수**를 쓰며(첫 행 y 77, 제목은 x 15 · y 35), 표시하지 않는 능력치(값 0인 치명·이동속도)는 `AppendStatRow`로 **양쪽에서 함께 빠져** 행이 어긋나지 않는다.
    - 값이 긴 능력치(체력)는 이름이 짧아(`체력`) 남는 여백으로 넘어가므로 좁은 열에서도 겹치지 않는다. 좁은 폭에 맞추려고 상승분은 괄호 없이 `+N`으로 적는다.
    - **테두리**: 배경으로 초상·장비 칸과 같은 `ui_slot_portrait`를 써서 같은 톤의 금색 테두리를 얻는다(옆 초상 슬롯과 크기가 같은 220×300이라 테두리 두께도 똑같이 보인다). 아트 미배선 시에는 어두운 남색 사각형으로 폴백한다.
  - **경험치 막대**(`ExpBar`, 초상 바로 아래 — 초상과 같은 x 232 · 폭 220 · 높이 24): 배경은 **전투 몬스터 HP바와 같은 프레임 아트**(`Assets/Art/Icon/Combat/체력바.png`)를 쓴다.
    - 이 아트는 768×144에 사방 테두리 24px(12배로 그린 픽셀아트의 2픽셀)인데 **9-slice 테두리 값이 없다**(`spriteBorder` 0). 공용 아트의 임포트 설정은 바꾸지 않는 규칙이므로 그대로 늘려 쓰며, **축 배율이 달라 좌우 테두리(24 × 220/768 ≈ 7px)가 상하(24 × 24/144 = 4px)보다 약간 두껍게** 보인다(세로는 정확히 6배 축소).
    - 채움(`ExpBarFill`)은 프레임 안쪽 컨테이너(`ExpBarInner`, 여백 7 × 4 → 내부 206×16)에 넣는다. 여백을 채움 자신에게 주면 **진행률이 낮을 때 rect 폭이 음수가 되어 막대가 뒤집혀 그려진다**(0% → 폭 0, 5% → 10.3px로 검증).
  - **인벤토리 영역**: 계정이 보유한 아이템 목록을 **격자(grid) 슬롯**으로 표시한다. 각 칸은 아이콘·수량(재료 스택)·등급 테두리를 보인다. 격자는 **한 번에 2줄(10칸)만 보이는 스크롤 영역**이며, 아래로 스크롤해 나머지 칸을 본다. 그리드의 **마지막 칸은 아이템 칸이 아니라 인벤토리 확장 요청 버튼(`＋ 확장`)**으로, 누르면 아이템 슬롯이 1칸 늘어난다(확장 버튼은 항상 마지막 칸 유지).
- 아이템 칸에 **마우스를 올리면(hover)** 커서 옆에 **별도의 툴팁 창**이 떠서 이름·등급·장착 슬롯·요구 클래스/레벨·스탯 등 **상세 정보**를 보여준다. 이 창에는 장비일 경우 **장착/해제 버튼**도 함께 들어간다. 마우스를 칸/툴팁에서 모두 벗어나면 창이 사라진다(고정 상세 영역이 아니라 hover 팝업).
- 장착 가능한 장비를 고르고 **장착**을 누르면 서버에 요청하고, 성공 응답에 따라 장비 슬롯·인벤토리 표시가 갱신된다. 이미 그 부위에 장비가 있으면 **스왑**되어 기존 장비가 인벤토리로 돌아온다(서버 5.1 규칙).
- **소모품(`item_type=4`, 경험치·골드 부스터)은 장착 개념이 없으므로 툴팁의 좌측 버튼이 `장착` 대신 `사용`으로 바뀐다**(버튼을 따로 늘리지 않고 같은 자리를 재사용). 누르면 `POST /api/game/consumable/use`로 1개 사용을 요청하고, 성공 시 부여된 버프(종류·증가율·만료)를 모달로 안내한 뒤 가방 수량과 **우측 상단 버프 아이콘**을 갱신한다([서버 소모품/버프 기획서](../../../docs/세부/consumable-buff-기획서.md) 5.1). 배율·지속시간은 전적으로 서버가 마스터 데이터에서 확정한다.
- **방치형 특성**: 인벤토리는 상시 게임(자동 전투) 위에 겹쳐 열리며, 열려 있는 동안에도 뒤의 전투 연출은 계속 돈다. 인벤토리는 게임 진행을 막는 모달이 아니라 **정보 조회·성장 조작 패널**이다.

## 3. 요구사항

**기능 요구사항**
- (F1) `UIManager`를 통해 인벤토리 패널을 열고 닫는다(On/Off 토글). GameScene에 진입점(HUD 버튼)을 둔다.
- (F2) 인벤토리 격자는 **스크롤 뷰(`ScrollRect`+`RectMask2D`)로 2줄(10칸)만 노출**하고 나머지는 스크롤로 본다. 아이템 슬롯은 `Session.GameData.player.inventoryCapacity`(실데이터 연동 전엔 데모값 14)만큼 만들고, 보유 아이템은 각 항목의 `slot`(0-based) 위치에 배치한다. `slot`이 없는 항목(재화 등)은 격자에서 제외한다.
- (F2b) 그리드 **마지막 칸은 확장 요청 버튼**이다(아이템 칸 아님, 드롭 대상 제외). 클릭 시 아이템 슬롯이 1칸 늘고 버튼은 항상 마지막을 유지한다. 초기 상태는 14 아이템 슬롯 + 확장 버튼 = **총 15칸(3줄)**. 실제 용량 증가는 서버 권위(`POST /api/game/inventory/expand`, 서버 기획서 5.4)이며 현재는 로컬 데모로 배선까지만 한다.
- (F3) 각 아이템 칸은 마스터데이터(`item_master`)로 아이콘·이름·등급·타입을 룩업해 표시하고, 재료(스택) 아이템은 `quantity`를 함께 표시한다.
- (F4) 장비 영역은 선택된 캐릭터가 장착한 아이템(`equippedCharacterId`==선택 캐릭터, `equippedSlot`)을 6부위 슬롯에 매핑해 표시한다.
- (F5) 아이템 선택 → 상세 표시 → 장비면 **장착/해제** 요청(서버 5.1/5.2)을 보내고 응답으로 UI를 갱신한다.
- (F6) 캐릭터가 여러 명(최대 3인 파티)이면 장비 영역 상단의 **화살표 버튼(`<` `>`)** 으로 대상 캐릭터를 전환(순환)하며, 인디케이터(`캐릭터 N / M`)와 초상·6부위 장착 슬롯이 해당 캐릭터 기준으로 갱신된다.
- (F7) 서버 오류는 `ErrorMessages.ToKorean(...)`로 변환해 사용자에게 표시한다(장착 불가·아이템 없음 등).

**비기능 요구사항**
- (N1) **서버 권위**: 장착 성립·스왑·수량은 서버 응답으로만 확정한다. 클라이언트는 낙관적 갱신을 하지 않거나, 하더라도 실패 응답 시 즉시 롤백한다(기본: 응답 후 갱신).
- (N2) **로컬 상태 일관성**: 서버 액션 성공 시 로컬 캐시(`Session.GameData` 코어 + `Session.Bag` 가방)도 같은 상태로 맞춘다 — 배치 이동처럼 결과가 확정적인 변경은 캐시를 직접 패치하고, 아이템 구성이 바뀌는 변경(장착·해제·소모품·큐브·거래)은 **응답에 실려 오는 변경분**(`inventoryDelta`·`ApplyEquipResult`·`ApplyUnequipResult`)으로 캐시를 맞춘다 — 액션마다 재조회하지 않는다. 캐시가 서버와 어긋난 게 드러났을 때(`ItemNotFound`·이동 저장 실패)만 `InventoryLoader.ReloadBag()`으로 전량을 다시 받는다. 패널을 닫았다 다시 열어도 동일 상태가 보여야 한다.
- (N3) **네트워크 검증 범위**: 실서버 연동 검증은 [클라 규칙](../../CLAUDE.md)에 따라 명시 요청 시에만 수행한다. 그 외에는 컴파일·씬 배선·패널 On/Off·데이터 바인딩(로컬 스냅샷 기준)까지만 확인한다.
- (N4) 기존 UI 관례 준수: 레거시 `UnityEngine.UI`(`Button`/`Text`/`Image`), `[SerializeField] private` 인스펙터 배선, `Awake` 리스너 등록.

## 4. 화면 구성 / UI 리소스

### 4.1 사용 리소스 (`Assets/Art/UI/Inventory/`)

| 파일 | 용도 |
|---|---|
| `ui_panel_background.png` | 인벤토리 패널 **전체 배경**(장비 영역·인벤토리 영역·상세 영역을 감싸는 창) |
| `ui_slot_normal.png` | 인벤토리 격자 **기본 슬롯 바탕**(빈 칸 및 아이템 칸의 배경) |
| `ui_slot_highlight.png` | **hover 슬롯 강조**(커서가 올라온 칸의 HoverFrame, 장착 가능 슬롯 하이라이트) |
| `ui_slot_portrait.png` | **캐릭터 초상 / 장비 부위 슬롯 바탕**(장비 영역의 6부위 슬롯 및 캐릭터 초상 프레임) |

> **임포트 설정 확인 필요**: 4개 모두 `textureType: Sprite`이나 `spriteBorder`가 0이라 9-slice가 설정돼 있지 않다. `ui_panel_background`/슬롯 바탕처럼 크기가 늘어나는 배경은 **9-slice(Border) 지정**을 권장한다(그리드 칸 수·해상도에 따라 늘어날 때 모서리 왜곡 방지). Sprite Editor에서 Border를 잡고 `Image.type = Sliced`로 사용한다. 이 조정은 구현 단계에서 확정한다(10장).

### 4.2 레이아웃 (세로형 소형 창 기준)

```
┌───────────── InventoryPanel (ui_panel_background) ─────────────┐
│  [보유 골드]        인벤토리         [ < ] 캐릭터 1/3 [ > ]     │
│  ┌── 장비 영역 ──────────────────────────────────────────────┐ │
│  │   (portrait)     [무기]   [보조무기]                       │ │
│  │   캐릭터 초상     [투구]   [갑옷]                           │ │
│  │   Lv / 직업      [장갑]   [신발]     ← ui_slot_portrait     │ │
│  └────────────────────────────────────────────────────────────┘ │
│  ┌ 인벤토리 격자 (ScrollRect, 2줄만 노출) ────────────────────┐ │
│  │   [][][][][]                        ┌── Tooltip(hover) ──┐   │ │
│  │   [][][][][]   ← 2줄(10칸)만 보임    │ 아이콘  이름/등급    │   │ │
│  ├ ─ ─ ─ ─ ─ ─ (스크롤 경계) ─ ─ ─ ─   │ 부위/요구Lv·클래스   │   │ │
│  │   [][][][＋]   ← 스크롤 시 노출,      │ 스탯                 │   │ │
│  │                 마지막 칸=＋확장버튼  │ [ 장착 ] / [ 해제 ]  │   │ │
│  │                                     └─────────────────────┘   │ │
│  └────────────────────────────────────────────────────────────┘ │
└────────────────────────────────────────────────────────────────┘
```

- **아이템 상세는 고정 영역이 아니라 hover 툴팁 창**(`InventoryTooltip`)으로 띄운다. 칸 위에 커서가 올라오면 커서 근처에 뜨고, 칸·툴팁 양쪽에서 벗어나면 닫힌다(4.3·8.2).
- 격자는 **`ScrollRect`(vertical)+`Viewport`(`RectMask2D`)+`Content`(`GridLayoutGroup`+`ContentSizeFitter`)** 구조로, 뷰포트 높이 = 2줄(`visibleRows`). 마지막 칸 `ExpandButton`(＋)은 항상 콘텐츠 마지막 sibling.
- **가방 영역은 `BagBackground`(어두운 남색 반투명)로 다른 영역과 구분**하고, 그리드 **우측에 세로 스크롤바**(`Scrollbar`, `verticalScrollbarVisibility=Permanent`)를 두어 스크롤 가능함을 항상 노출한다.
- **슬롯 크기**: 캐릭터 초상(`PortraitSlot` 220×300)은 크게 유지, 장비 슬롯은 100×100, 가방 칸은 120×120으로 축소해 정보 밀도를 높였다.
- 등급 색: `grade_master`는 이름만 정의(노말·고급·희귀·영웅·전설)하고 색은 없다. 등급별 테두리/틴트 색은 **클라이언트 표현값**으로 팔레트를 둔다(계약 아님, 10장에서 팔레트 확정).

### 4.3 프리팹 구조 (기존 패턴 준수)

- `InventoryPanel`(프리팹, 자체 Canvas + CanvasScaler + GraphicRaycaster 포함 — 기존 `UIManager` 전제: "각 패널 프리팹은 자체 Canvas를 포함").
  - `PanelRoot`(Image: `ui_panel_background`)
    - `CharacterNav`(캐릭터 전환: `PrevCharButton`◀ + `CharIndicator`(캐릭터 N/M) + `NextCharButton`▶)
      - 화살표는 **`Assets/Art/UI/화살표버튼.png` 하나**를 쓴다 — 아트가 **오른쪽 방향**이라 이전 버튼은 화살표 자식만
        `localScale.x = -1`로 **좌우 반전**한다(루트 pivot이 좌상단이라 루트를 반전하면 버튼이 앵커 밖으로 밀려난다).
      - 구조는 **투명한 루트(64×64 클릭 영역) + 화살표 자식**(안쪽 6px 여백, `preserveAspect`, `raycastTarget=false`)이다.
      - 클릭하면 **잠깐 커졌다 원래대로** 돌아온다(`ButtonPunchScale` — 화살표 자식에 붙여 가운데 기준으로 커지고,
        기준 스케일에 배율만 곱하므로 반전도 유지된다). 캐릭터가 1명이면 전환이 없으므로 연출도 재생하지 않는다.
      - 아트가 배선되지 않으면 기존 슬롯 배경 + `<`·`>` 텍스트로 폴백한다(버튼이 사라지지 않게).
    - `GoldArea`(보유 골드 — 제목 옆에서 과해 보이지 않도록 블록째 `localScale 0.7`로 축소)
    - 닫기(X) 버튼은 **두지 않는다** — 미관상 제거하고 창 밖(딤) 클릭·HUD 버튼 토글·`ESC`로 닫는다
      (스테이지 지도·지역 창, 뽑기 창과 같은 규칙)
    - `SkillButton`·`RuneButton`·`CubeButton`(패널 최하단 가로 중앙, 바닥에서 62px 띄움)
    - `EquipArea` → `PortraitSlot`(Image: `ui_slot_portrait`) + `EquipSlot`×6(각 Image: `ui_slot_portrait`, 자식 `Icon`)
    - `InventoryScroll`(ScrollRect) → `Viewport`(RectMask2D, 2줄 높이) → `Content`(GridLayoutGroup+ContentSizeFitter) → `Slot`×N + 마지막 `ExpandButton`(＋)
  - `TooltipRoot`(패널 최상단, 기본 비활성) → `InventoryTooltip` 1개(재사용). hover 시 위치·내용을 갱신해 표시.
- `ItemSlot`(프리팹): `Background`(Image: `ui_slot_normal`) + `HoverFrame`(Image: `ui_slot_highlight`, 기본 비활성) + `Icon`(Image) + `QuantityText`(Text). 마우스 진입/이탈을 감지해야 하므로 `IPointerEnterHandler`/`IPointerExitHandler`(또는 `EventTrigger`)로 hover 이벤트를 컨트롤러에 전달한다(장착 대상 선택도 hover된 칸 기준).
- `InventoryTooltip`(프리팹/재사용 오브젝트): 작은 창(자체 배경) — `Icon`/`NameText`/`GradeText`/`SlotText`/`ReqText`/`StatsText`/`EquipButton`/`UnequipButton`. 커서를 칸→툴팁으로 옮겨도 유지되도록 툴팁 자신도 hover 상태로 취급한다(8.2 keep-open 규칙).

### 4.4 아이템 칸의 그림은 공용 슬롯 프리팹이 그린다 (2026-08-05 통일)

아이템이 놓인 칸의 **아이콘·등급 배경·수량·강화 배지**는 인벤토리가 직접 만들지 않고 **공용 아이템 슬롯 프리팹**
`Assets/Prefabs/UI/ItemSlot.prefab`(`ItemSlotView`)이 그린다. 가방 격자 칸(`InventoryItemView`)과 **장비 부위 칸**
(`InventoryItemSlot.SetEquipped`) 모두 이 프리팹을 자식으로 인스턴스화하며, 프리팹 참조는 에디터 빌더가
`InventoryPanelController._itemSlotPrefab`에 배선한다(메뉴 `TaskbarHero/UI/아이템 슬롯·상세 팝업 배선` 또는
`인벤토리 패널·씬 생성`).

- 인벤토리가 자기 몫으로 남기는 것: **드래그·hover 입력**(투명한 루트 이미지), **착용 불가 X 표시**,
  **아이콘 없는 아이템의 첫 글자 폴백**, 칸 프레임(`ui_slot_normal`)과 hover 하이라이트.
- 칸이 이미 자기 프레임을 그리므로 슬롯 프레임은 `SetFrameVisible(false)`로 감춘다(이중 테두리 방지).
- hover 상세는 인벤토리 전용 툴팁(장착·사용 버튼 포함)이 담당하므로 슬롯의 공용 상세 팝업은 끈다(`showDetail = false`).
- **강화 단계 표시 규격은 슬롯이 고정한다** — 칸 기준 **좌측 하단 · 흰 글자 + 검은 외곽선(2px) · 굵게**(칸 비율
  x 8~62% · y 5~45%). 수량 "xN"은 같은 아래쪽이지만 우측이라 겹치지 않는다.
- **글자 크기는 칸 높이 × 0.352의 고정값이며 `BestFit`을 쓰지 않는다.** 실측 적용 폰트: 높이 150px 칸 53 ·
  118px 칸(인벤토리) 42 · 114px 칸(큐브 타일) 40 · 72px 칸(거래소) 25. BestFit을 쓰면 `UiTextStyle`이 0.4초 주기로 `verticalOverflow`를 Truncate → Overflow로
  바꾸는 순간 크기가 다시 계산되어 **글자가 작아졌다 커지는 현상**이 생긴다(큐브에서 타일을 누를 때마다 그리드를
  다시 만들어 재현됐다 — 2026-08-05 수정). 지금은 overflow를 양방향 `Overflow`로 두어 `UiTextStyle`이 손대지 않고,
  칸 크기가 정해지기 전(높이 &lt; 24px)에는 배지를 그리지 않아 엉뚱한 크기로 한 프레임 보이는 일도 없다.
- 부수 효과로 **가방 칸에도 스택 수량("xN")이 표시**되고(이전에는 표시가 없었다), **장비 부위 칸에도 강화 단계가
  표시**된다(이전에는 장착 중인 장비의 강화 단계를 칸에서 볼 수 없었다).

## 5. 데이터 바인딩 (표시용 소스)

로드는 **2단계**다([세이브 데이터 기획서](../../../docs/세부/save-data-기획서.md) 5.1·5.2). 고정 크기 데이터(장착 장비 포함)는 코어 로드(`POST /api/game/load`) 스냅샷에서 오고, **가방 아이템만** 창고를 열 때 `POST /api/game/inventory/list`로 페이징 조회해 세션에 캐싱한다.

가방은 **한 번에 전량을 받지 않고 스크롤에 맞춰 지연 로딩**한다(아래 5.1).

| 표시 항목 | 소스 |
|---|---|
| 가방 아이템 목록·slot·수량·강화 | `Session.Bag` (`List<InventoryItemDto>`, `BagPager`가 스크롤에 맞춰 페이지 단위로 채움) |
| 캐릭터별 장착 장비(장비 6슬롯) | `Session.Equipped` (`List<EquippedItemDto>`, 코어 로드의 `equipped`) |
| 인벤토리 칸 수 | `Session.GameData.player.inventoryCapacity` |
| 캐릭터 목록(장비 영역 대상) | `Session.GameData.characters` (`List<CharacterDto>`) |
| 아이템 정의(이름·타입·등급·부위·요구·스탯) | `MasterDataManager.Db.Items[itemCode]` (`ItemMaster`) |
| 장착 슬롯 이름 | `MasterDataManager.Db.EquipSlots[slot]` (`EquipSlotMaster`) |
| 등급 이름 | `MasterDataManager.Db.Grades[grade]` (`GradeMaster`) |

- 조인 키: `itemCode` ↔ `ItemMaster.itemCode`(가방·장착 양쪽 동일).
- 장비 영역 매핑: `Session.Equipped` 중 `equippedCharacterId == 선택 캐릭터 && equippedSlot == s`인 항목을 슬롯 `s`(1~6)에 배치. 없으면 빈 슬롯.
- 인벤토리 격자 매핑: `Session.Bag`의 각 항목을 `slot`(0-based) 칸에 배치. **장착 중인 아이템은 인벤 칸을 점유하지 않아 가방 목록에 없고**(장비 슬롯에서만 보인다), 재화도 코어 로드의 `currencies`로 분리돼 격자에 오지 않는다.
- 가방 캐시 수명: 창고는 **열 때마다** 캐시를 비우고 첫 페이지부터 다시 받는다(자동 전투로 전리품이 계속 쌓이므로 로컬 캐시를 신뢰하지 않는다). 가방을 바꾸는 액션 뒤에는 응답의 변경분(`inventoryDelta`)·`ApplyEquipResult`·`ApplyUnequipResult`로 캐시를 직접 맞추므로 재조회하지 않는다.
- **페이지 병합은 클라이언트 책임(계약)**: 서버는 페이지 사이의 인벤토리 변경을 감지하지 않는다(변경 카운터·버전 토큰 없음). 페이지를 이어붙일 때 **`itemId`를 키로 중복을 제거하고 나중 페이지를 우선**하며(`Session.MergeBagPage`), 최종 목록을 `slot` 오름차순으로 정리한다. 이동으로 같은 아이템이 두 페이지에 걸쳐도 최종 위치 하나만 남는다.
- **남는 오차와 해소**: 이미 지나간 칸으로 이동한 아이템은 이번 조회에서 빠지고, 읽은 뒤 소모된 아이템은 유령으로 남는다. 창고를 다시 열면 해소되며, 유령 아이템을 조작하면 서버가 `ItemNotFound(4001)`로 거부한다 → **각 패널의 실패 핸들러가 그 코드를 받으면 가방을 새로 고친다**(인벤토리/큐브는 `ReloadBagAndRefresh`, 거래는 `ReloadBag`).

### 5.1 가방 스크롤 지연 로딩 (slot 커서 페이징)

서버 `POST /api/game/inventory/list`는 `slot` 커서 keyset 페이징을 지원한다(요청 `{ cursor, limit }`, 응답 `{ items, nextCursor, hasMore, total }`). 클라이언트는 이를 **무한 스크롤**로 소비한다 — 창고를 열면 한 페이지만 받고, 스크롤이 아직 받지 않은 칸에 닿을 때 다음 페이지를 이어 받는다.

- **커서 상태**: `BagPager`(`Assets/Scripts/Managers/InventoryLoader.cs`). `InventoryLoader.BeginPaged(pageLimit)`이 캐시를 비우고 커서를 `-1`로 만들며, `LoadNext(onPage, onError)`가 한 페이지를 받아 `Session.MergeBagPage`로 캐시를 넓힌다. 중복 요청(`IsLoading`)과 마지막 페이지(`HasMore`)는 `BagPager`가 걸러내므로 호출 측은 스크롤 이벤트마다 불러도 된다.
- **다음 페이지 판정**: 격자는 **용량(`inventoryCapacity`) 전체만큼** 항상 그려지므로(빈 칸 포함) 스크롤 범위가 받은 개수와 무관하다. 따라서 `뷰포트 맨 아래 줄 + 미리받기 여유 줄(prefetchRows, 기본 2)`의 마지막 칸 번호가 `BagPager.LoadedSlot`(마지막으로 받은 칸)을 넘으면 다음 페이지를 요청한다. 한 페이지로 화면을 못 채웠거나 스크롤바를 아래로 건너뛰면 필요한 만큼 연속으로 이어 받는다.
- **페이지 크기**: `InventoryPanelController.bagPageLimit`(기본 `InventoryLoader.ScrollPageLimit` = 20칸 = 4줄). 서버는 1~500으로 클램프한다.
- **트리거 배선**: `ScrollRect.onValueChanged`는 비영구 리스너라 프리팹에 직렬화되지 않으므로 `WireRuntime()`에서 매 실행 다시 연결한다(`WireGridScroll`).
- **로딩 표시**: 페이지 대기 중 격자 하단에 "아이템 불러오는 중..." 라벨(런타임 생성, 프리팹에 굽지 않음)을 띄운다.
- **전량이 필요한 화면은 예외**: 큐브 재료 목록·거래 판매 등록은 가방 **전체**를 알아야 하므로 지연 로딩을 쓰지 않고 `InventoryLoader.ReloadBag`으로 끝까지 받는다. 인벤토리 패널도 캐시가 서버와 어긋났을 때(`ItemNotFound`·이동 저장 실패)는 이 전량 재조회를 쓰고, 낡은 커서로 이어 받지 않도록 `BagPager.MarkComplete()`로 페이저를 닫는다.
- **전리품 직후**: 스테이지 클리어로 아이템을 받으면(`DungeonBattleFlow.OnClear`) 그 자리에서 `Session.InvalidateBag()`을 호출해, 다음에 창고를 열 때 첫 페이지부터 다시 받게 한다.
- 마스터데이터는 `MasterDataManager.EnsureLoaded()`로 최초 1회 로드 후 사용. **아이콘 스프라이트 매핑**(`itemCode` → 아이템 아이콘)은 아직 규약이 없어 10장 미결(현재 `item_master`에 아이콘 키 없음).

## 6. UIManager 연동 및 On/Off 정책

사용자 요구: **인벤토리 UI는 `UIManager`로 관리**하고 GameScene에서 On/Off 한다.

### 6.1 UIManager 확장

- `PanelType`에 `Inventory`를 추가하고, `[SerializeField] private GameObject inventoryPanelPrefab;` + `GetPrefab` case를 확장한다.
- 공개 메서드 추가:
  - `ShowInventory()` / `Hide(PanelType.Inventory)`
  - `ToggleInventory()` — `Current == Inventory`면 Hide, 아니면 Show(토글 On/Off의 핵심).

### 6.2 "한 번에 하나" 정책과의 관계 (설계 결정)

현재 `UIManager.Show(type)`는 **자기 외 모든 패널을 끈다**. 이는 로그인↔회원가입 같은 **전환형** 화면에 맞다. 인벤토리는 게임 뷰(HUD) 위에 겹쳐 뜨는 **오버레이**지만, GameScene에서 UIManager가 관리하는 다른 오버레이 패널이 아직 없으므로 **현행 Show/Hide/Toggle로 충분**하다. 단:
- GameScene HUD(재화·메뉴바 등)는 `UIManager` 관리 패널이 아니라 씬 상주 UI이므로 `Show(Inventory)`가 HUD를 끄지 않는다(HUD는 `_instances` 밖).
- 향후 여러 기능 패널(성장·상점·메일 등)이 UIManager에 등록되면 "한 번에 하나"가 그대로 적용돼 기능 패널끼리는 자동으로 배타 표시된다(원하는 동작). 여러 오버레이 동시 표시가 필요해지면 그때 스택형(`PanelStack`)으로 확장한다(10장).

### 6.3 지속 인스턴스(DontDestroyOnLoad) 배선 주의

`UIManager`는 `DontDestroyOnLoad` 싱글턴이라 **TitleScene에서 생성된 인스턴스가 GameScene까지 유지**된다. 따라서 `inventoryPanelPrefab` 참조는 **최초 생성 씬(Title)의 UIManager 인스턴스**에 지정돼 있어야 GameScene에서 유효하다. 프로젝트 관례([클라 규칙](../../CLAUDE.md))에 따라 프리팹은 **`Assets/Prefabs/UI/InventoryPanel.prefab`** 에 두고 **`UIManager`의 `[SerializeField]`에 직접 배선**한다(`Resources` 미사용 — 기존 `LoginPanel`·`SignUpPanel`과 동일 방식). 씬 간 유지 문제는 **Title·GameScene 두 UIManager 모두에 참조를 지정**해 해결한다(실제 구현 완료).

### 6.4 GameScene 진입점

- GameScene에 **HUD Canvas + 인벤토리 토글 버튼**과 이를 배선할 컨트롤러(`GameSceneController` 또는 `HudController`)를 신규로 둔다(현재 GameScene은 라벨만 있는 빈 씬).
- 버튼 `onClick` → `UIManager.Instance.ToggleInventory()`.

## 7. 스크립트 / asmdef 구성

- 위치: `Assets/Scripts/UI/`(asmdef `TaskbarHero.Client.UI`, 네임스페이스 `TaskbarHero.Client.UI`).
- 신규 스크립트(안):
  - `InventoryPanelController` — 패널 루트. 데이터 로드/그리드 생성/캐릭터 탭/툴팁 표시·갱신/장착·해제 요청 조율.
  - `InventoryItemSlot` — 개별 칸 뷰(아이콘·수량·hover 프레임). `IPointerEnterHandler`/`IPointerExitHandler`로 hover 진입/이탈을 컨트롤러로 전달.
  - `InventoryTooltip` — hover 상세 창 뷰(아이콘·이름·등급·부위·요구·스탯 + 장착/해제 버튼). 자신의 pointer enter/exit로 keep-open을 지원.
  - `EquipSlotView` — 장비 부위 슬롯 뷰(hover 시 장착 아이템 툴팁 표시).
  - (선택) `ItemGradePalette` — 등급→색 매핑(ScriptableObject 또는 정적 테이블).
  - GameScene용 `GameSceneController`/`HudController`(`Assets/Scripts/...`) — 인벤토리 토글 버튼 배선.
- **asmdef 의존성 추가 필요**: `TaskbarHero.Client.UI.asmdef`에 **`TaskbarHero.Client.MasterData` 참조를 추가**한다. `MasterDataManager.Db`의 반환 타입 `MasterDatabase`가 해당 asmdef 소속이기 때문이다(현재 UI asmdef는 미참조). `ItemMaster`/`EquipSlotMaster`/`GradeMaster` POCO 자체는 이미 참조 중인 `TaskbarHero.Common`에 있다.
- 기존 참조(`TaskbarHero.Common`, `TaskbarHero.Client.Managers`, `UnityEngine.UI`, `Unity.InputSystem`)로 `Session`·`NetworkManager`·DTO·uGUI는 그대로 사용 가능.

## 8. 상호작용 흐름

### 8.1 열기 / 닫기
```
HUD 인벤토리 버튼 클릭 → UIManager.ToggleInventory()
  Current != Inventory → Show(Inventory)
     → (최초) 프리팹 인스턴스화 → OnEnable에서 Rebuild()
     → Rebuild(): MasterDataManager.EnsureLoaded()
                  → 캐릭터 네비게이션(◀ N/M ▶) 구성(기본=첫 캐릭터)
                  → 장비 6슬롯 채우기(코어 로드의 equipped — 즉시 표시)
                  → InventoryLoader.BeginPaged() → 첫 페이지만 요청 → 도착 후 격자 채우기
                  → 이후 스크롤이 아직 안 받은 칸에 닿으면 BagPager.LoadNext()로 이어 받기(5.1)
  Current == Inventory → Hide(Inventory)
```

### 8.2 아이템 hover → 상세 툴팁
```
ItemSlot에 커서 진입(PointerEnter) → 컨트롤러.ShowTooltip(itemDto, 칸 위치)
  → 해당 칸 HoverFrame on(ui_slot_highlight)
  → InventoryTooltip 활성 + 커서 근처로 위치(화면 밖으로 넘치면 반대편으로 클램프)
  → 툴팁 내용 갱신: ItemMaster 룩업(이름/등급/부위/요구/스탯)
  → 장비(item_type=1)면: 장착 중이면 [해제] 활성, 미장착이면 [장착] 활성
     (요구 클래스/레벨 위반 여부는 표시로 안내, 최종 판정은 서버)
  → 비장비면 버튼 숨김(1차 범위: 소모/분해 등은 후속)

커서 이탈(PointerExit) → HideTooltip() 예약
  keep-open 규칙: 칸에서 나가도 곧바로 툴팁 위로 진입하면 유지, 칸·툴팁 양쪽에서
  모두 벗어났을 때만 HoverFrame off + 툴팁 비활성(버튼 클릭 가능하도록).
```
- 장착 대상 캐릭터는 상단 화살표 네비게이션에서 선택된 캐릭터다(hover는 어떤 아이템을 조작할지만 지정). 장비 슬롯에 장착된 아이템도 hover 시 동일 툴팁을 띄운다.

### 8.3 장착 / 해제 (서버 권위)
장착/해제 버튼은 hover 툴팁 창 안에 있다(8.2). 버튼 클릭 시:
```
[장착] → data { characterId(선택 캐릭터), itemId(=player_item_id) }
        → NetworkManager.PostToGame(/api/game/inventory/equip, ...)
   성공: Session.ApplyEquipResult(응답) — 재조회 없이 캐시를 서버 확정값으로 맞춘다
         (장착품은 가방에서 빠져 장착 목록으로, 스왑된 기존 장비는 응답의 unequippedBagSlot 칸으로)
         → 장비 영역·격자·상세 재갱신 + Session.RaiseInventoryChanged()(전투 스탯 재계산)
   실패: ErrorMessages.ToKorean(error) 표시(4001 없음 / 4003 장착불가 / 4007 타 캐릭터 장착중 / 2006 캐릭터ID)

[해제] → data { characterId, slot(장착 슬롯 1~6) }
        → /api/game/inventory/unequip
   성공: 장착과 동일하게 Session.ApplyUnequipResult(응답)로 캐시를 맞춘 뒤 재갱신
         (해제한 장비는 응답의 bagSlot 칸으로 되돌아간다)
```

### 8.4 예외 / 엣지
- `Session.GameData`/`characters`가 비어 있으면(비정상 진입) 패널을 빈 상태로 열고 경고 로그.
- `NetworkManager.Instance == null`이면 버튼 비활성 + 안내(로그인 흐름과 동일 방어).
- 마스터데이터에 없는 `itemCode`는 "알 수 없는 아이템"으로 표시(크래시 금지).
- 서버 응답 대기 중 장착/해제 버튼 중복 클릭 방지(요청 중 `interactable=false`).

## 9. 서버 연동 의존성 (선행 필요)

장착/해제 요청·응답은 **공유 DTO를 그대로 사용**해야 한다([클라 규칙](../../CLAUDE.md): DTO는 `TaskbarHero.Common/Dto`에 정의, 클라 중복 금지). 현재 `TaskbarHero.Common/Dto/GameDto.cs`에는 **장착/해제/이동용 요청·응답 DTO가 아직 없다**(서버 기획서 5.1/5.2/5.5는 응답 `data` 구조를 명세하되 공유 DTO는 "제안" 상태). 따라서 UI 구현 전(또는 병행) 다음 DTO를 `TaskbarHero.Common/Dto`에 추가해야 한다(서버와 공유):

- 장착 요청 데이터 `{ characterId, itemId }` + 인증 래퍼, 응답 `data { characterId, equipped{slot,itemId}, unequipped{slot,itemId}? }`
- 해제 요청 데이터 `{ characterId, slot }`, 응답 `data { characterId, slot, itemId }`
- (후속) 이동 `{ itemId, toSlot }` 등

> 본 DTO 추가는 서버-클라 계약이므로 **서버 인벤토리 기획서 5장 스키마를 정본**으로 하고, 필드명은 그 JSON(camelCase)과 1:1로 맞춘다. 값(에러코드 등)은 이미 정의된 `ErrorCode` 4000번대를 그대로 사용한다(신규 에러코드 없음).

## 10. 미결 사항 / TODO

- **1차 범위 확정**: 보기 + 장착/해제만 우선 구현. 랜덤 상자·드래그 이동(5.5)은 후속 패널 확장으로 분리. **강화(5.3)는 큐브 패널 '강화' 탭으로 구현됐고**(인벤토리 패널은 강화 단계 표시만 담당), 용량 확장(5.4)도 이 패널에서 서버 연동돼 있다.
- **아이콘 매핑 규약**: `itemCode` → 아이템 아이콘 스프라이트 매핑 방법 미정(`item_master`에 아이콘 키 없음). 아틀라스/Resources 규약 또는 마스터데이터에 아이콘 필드 추가 여부를 정해야 함.
- **등급 색 팔레트**: 노말·고급·희귀·영웅·전설 5등급의 테두리/틴트 색(클라이언트 표현값). 계약 아님.
- **툴팁 위치·keep-open 세부**: hover 툴팁의 커서 기준 오프셋, 화면 경계 클램프 방향, 칸↔툴팁 이동 시 유지 판정(짧은 지연/레이캐스트 기준)과 터치 환경 대응(터치는 hover가 없으므로 탭으로 대체할지) 확정.
- **격자 수치(현재값)**: 5열, 가방 칸 120·간격 12, 노출 2줄(뷰포트 648×252), 초기 아이템 14칸+확장 버튼. 장비 슬롯 100, 초상 220×300. 세로형 창 해상도 확정 시 재조정.
- ~~**인벤토리 확장 서버 연동**~~ — 완료: 확장 버튼이 `POST /api/game/inventory/expand`(서버 기획서 5.4)를 호출하고, 응답의 `inventoryCapacity`·`balance`로 칸 수와 골드를 갱신한다(상한 도달은 `InventoryCapacityMax`).
- **장착 중 아이템의 격자 표시**: 장착 중 항목을 인벤토리 격자에도 계속 표시할지, 별도 표시(테두리/뱃지)할지.
- **패널 프리팹 배선 방식(확정)**: `Assets/Prefabs/UI/InventoryPanel.prefab` + `UIManager.[SerializeField]` 직접 배선(Title·GameScene 양쪽). `Resources` 미사용(6.3).
- **UI 리소스 임포트**: `ui_panel_background`·슬롯 바탕의 9-slice(Border) 지정 및 `Image.type=Sliced` 적용(4.1).
- **캐릭터 초상 리소스**: 장비 영역 초상(`ui_slot_portrait` 프레임 안)에 넣을 캐릭터 이미지 소스(SPUM 캡처/직업 아이콘 등) 미정.
- **DTO 선행 추가**: 9장 장착/해제 공유 DTO를 `TaskbarHero.Common/Dto`에 추가(서버 기획서 5장 정본).
- **실서버 연동 검증**: 명시 요청 시에만 수행(N3). 그 전까지는 로컬 스냅샷 기준 표시·토글·배선까지 검증.
