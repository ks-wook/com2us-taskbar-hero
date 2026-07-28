# Pixel UI Kit — Item Auction House (Unity 조립용)

업로드해 주신 목업 이미지를 기준으로 **텍스트/룬 문자를 모두 제거하고**, 유니티에서 직접 조립할 수 있도록
파트별 스프라이트로 분해·재구성한 UI 키트입니다.

> **왜 원본을 잘라내지 않았는가**
> 원본은 AI 생성 래스터 이미지라 (1) 글자가 픽셀에 구워져 있고 (2) 버튼·아이콘·프레임이 서로 겹쳐 있으며
> (3) 픽셀 그리드가 일정하지 않습니다. 그대로 크롭하면 글자 잔상·깨진 테두리가 남고 9-slice가 불가능합니다.
> 그래서 **원본에서 팔레트와 형태 언어(1px 검은 외곽선 + 상단 하이라이트 + 하단 그림자 + 골드 트림)를 추출해
> 동일한 룩으로 깨끗하게 다시 그렸습니다.** 모든 파일은 알파 배경, 정수 픽셀, 9-slice 대응입니다.

`00_Preview/assembled_preview_4x.png` 를 먼저 보세요. 이 키트의 부품들만으로 원본 화면을 재조립한 결과입니다.
(회색 막대는 텍스트가 들어갈 자리 표시입니다.)

---

## 1. 폴더 구조

```
PixelUI_AuctionKit/
├─ 00_Preview/            조립 예시 (1x, 4x)
├─ 01_Frames_Panels/      창 프레임, 나무 패널, 양피지 패널, 행 배경, 입력창, 툴팁
├─ 02_Buttons/            블루/골드/레드/우드 버튼, 카테고리 탭, 페이지 버튼, 닫기 버튼 (상태별)
├─ 03_Slots_Frames/       등급별 아이템 슬롯 프레임, 판매자 초상화 프레임
├─ 04_Icons/              16×16 픽셀 아이콘 (카테고리 / UI / 아이템)
├─ 05_Ornaments/          타이틀 배너(3분할), 골드 구분선, 코너 장식, 스크롤바, 젬
├─ 06_Palette/            팔레트 스와치 이미지
└─ 07_Source/             생성 파이썬 스크립트 (색·크기 수정 후 재출력 가능)
```

모든 스프라이트는 **1x 픽셀아트 원본 해상도**입니다. 유니티에서 Point 필터로 정수배 확대해 쓰는 것을
전제로 만들었습니다. 더 큰 텍스처가 필요하면 `07_Source/upscale.py 4` 를 실행하세요.

---

## 2. 유니티 임포트 설정 (반드시 이대로)

모든 PNG를 선택한 뒤 Inspector에서:

| 항목 | 값 |
|---|---|
| Texture Type | **Sprite (2D and UI)** |
| Sprite Mode | Single |
| Pixels Per Unit | 16 (UGUI만 쓸 경우 값은 무관) |
| Mesh Type | **Full Rect** ← 9-slice 필수 |
| Filter Mode | **Point (no filter)** |
| Compression | **None** |
| Generate Mip Maps | 해제 |
| Wrap Mode | Clamp |

그다음 각 프레임/버튼 스프라이트에서 **Sprite Editor → Border** 값을 아래 표대로 입력하세요.
Image 컴포넌트는 **Image Type = Sliced**, `Pixels Per Unit Multiplier` 로 확대 배율을 조절합니다
(예: 4배 크기로 보이게 하려면 0.25 입력).

---

## 3. 파일 목록 & 9-slice 경계값

| 파일 | 원본 크기 | Border (L/T/R/B) | 용도 |
|---|---|---|---|
| `01_Frames_Panels/badge_currency.png` | 24×22 | L7 T7 R7 B7 | gold/currency badge (bottom-left) |
| `01_Frames_Panels/bar_table_header.png` | 24×20 | L6 T6 R6 B6 | table column header strip |
| `01_Frames_Panels/field_search.png` | 28×20 | L7 T7 R7 B7 | search input (parchment inset) |
| `01_Frames_Panels/inset_dark.png` | 28×20 | L6 T6 R6 B6 | generic sunken dark inset |
| `01_Frames_Panels/panel_parchment.png` | 40×40 | L8 T8 R8 B8 | content / list background |
| `01_Frames_Panels/panel_tooltip.png` | 28×24 | L7 T7 R7 B7 | tooltip / dialog panel |
| `01_Frames_Panels/panel_wood.png` | 40×40 | L10 T10 R10 B10 | sidebar / sub panel |
| `01_Frames_Panels/row_alt.png` | 24×24 | L6 T6 R6 B6 | listing row (alt) |
| `01_Frames_Panels/row_hover.png` | 24×24 | L6 T6 R6 B6 | listing row (hover) |
| `01_Frames_Panels/row_normal.png` | 24×24 | L6 T6 R6 B6 | listing row (normal) |
| `01_Frames_Panels/row_selected.png` | 24×24 | L6 T6 R6 B6 | listing row (selected) |
| `01_Frames_Panels/window_frame.png` | 64×64 | L20 T20 R20 B20 | main window (dark body included) |
| `01_Frames_Panels/window_frame_hollow.png` | 64×64 | L20 T20 R20 B20 | same frame, transparent center |
| `02_Buttons/btn_blue_disabled.png` | 28×18 | L8 T6 R8 B6 | generic button blue/disabled |
| `02_Buttons/btn_blue_hover.png` | 28×18 | L8 T6 R8 B6 | generic button blue/hover |
| `02_Buttons/btn_blue_normal.png` | 28×18 | L8 T6 R8 B6 | generic button blue/normal |
| `02_Buttons/btn_blue_pressed.png` | 28×18 | L8 T6 R8 B6 | generic button blue/pressed |
| `02_Buttons/btn_category_hover.png` | 32×22 | L9 T8 R9 B8 | sidebar category tab (hover) |
| `02_Buttons/btn_category_normal.png` | 32×22 | L9 T8 R9 B8 | sidebar category tab (normal) |
| `02_Buttons/btn_category_selected.png` | 32×22 | L9 T8 R9 B8 | sidebar category tab (selected) |
| `02_Buttons/btn_close_hover.png` | 18×18 | — (고정 크기) | close button with X (hover) |
| `02_Buttons/btn_close_normal.png` | 18×18 | — (고정 크기) | close button with X (normal) |
| `02_Buttons/btn_close_pressed.png` | 18×18 | — (고정 크기) | close button with X (pressed) |
| `02_Buttons/btn_gold_disabled.png` | 28×18 | L8 T6 R8 B6 | generic button gold/disabled |
| `02_Buttons/btn_gold_hover.png` | 28×18 | L8 T6 R8 B6 | generic button gold/hover |
| `02_Buttons/btn_gold_normal.png` | 28×18 | L8 T6 R8 B6 | generic button gold/normal |
| `02_Buttons/btn_gold_pressed.png` | 28×18 | L8 T6 R8 B6 | generic button gold/pressed |
| `02_Buttons/btn_page_hover.png` | 16×16 | L5 T5 R5 B5 | pagination square (page/hover) |
| `02_Buttons/btn_page_normal.png` | 16×16 | L5 T5 R5 B5 | pagination square (page/normal) |
| `02_Buttons/btn_page_pressed.png` | 16×16 | L5 T5 R5 B5 | pagination square (page/pressed) |
| `02_Buttons/btn_page_sel_hover.png` | 16×16 | L5 T5 R5 B5 | pagination square (page_sel/hover) |
| `02_Buttons/btn_page_sel_normal.png` | 16×16 | L5 T5 R5 B5 | pagination square (page_sel/normal) |
| `02_Buttons/btn_page_sel_pressed.png` | 16×16 | L5 T5 R5 B5 | pagination square (page_sel/pressed) |
| `02_Buttons/btn_red_disabled.png` | 28×18 | L8 T6 R8 B6 | generic button red/disabled |
| `02_Buttons/btn_red_hover.png` | 28×18 | L8 T6 R8 B6 | generic button red/hover |
| `02_Buttons/btn_red_normal.png` | 28×18 | L8 T6 R8 B6 | generic button red/normal |
| `02_Buttons/btn_red_pressed.png` | 28×18 | L8 T6 R8 B6 | generic button red/pressed |
| `02_Buttons/btn_wood_disabled.png` | 28×18 | L8 T6 R8 B6 | generic button wood/disabled |
| `02_Buttons/btn_wood_hover.png` | 28×18 | L8 T6 R8 B6 | generic button wood/hover |
| `02_Buttons/btn_wood_normal.png` | 28×18 | L8 T6 R8 B6 | generic button wood/normal |
| `02_Buttons/btn_wood_pressed.png` | 28×18 | L8 T6 R8 B6 | generic button wood/pressed |
| `03_Slots_Frames/frame_avatar.png` | 20×20 | L5 T5 R5 B5 | seller portrait frame |
| `03_Slots_Frames/slot_common.png` | 20×20 | L5 T5 R5 B5 | item slot – common rarity |
| `03_Slots_Frames/slot_empty.png` | 20×20 | L5 T5 R5 B5 | empty slot |
| `03_Slots_Frames/slot_epic.png` | 20×20 | L5 T5 R5 B5 | item slot – epic rarity |
| `03_Slots_Frames/slot_legendary.png` | 20×20 | L5 T5 R5 B5 | item slot – legendary rarity |
| `03_Slots_Frames/slot_quest.png` | 20×20 | L5 T5 R5 B5 | item slot – quest rarity |
| `03_Slots_Frames/slot_rare.png` | 20×20 | L5 T5 R5 B5 | item slot – rare rarity |
| `03_Slots_Frames/slot_uncommon.png` | 20×20 | L5 T5 R5 B5 | item slot – uncommon rarity |
| `04_Icons/icon_arrow_projectile.png` | 16×16 | — (고정 크기) | icon |
| `04_Icons/icon_bag.png` | 16×16 | — (고정 크기) | icon |
| `04_Icons/icon_bow.png` | 16×16 | — (고정 크기) | icon |
| `04_Icons/icon_chevron_down.png` | 16×16 | — (고정 크기) | icon |
| `04_Icons/icon_chevron_left.png` | 16×16 | — (고정 크기) | icon |
| `04_Icons/icon_chevron_right.png` | 16×16 | — (고정 크기) | icon |
| `04_Icons/icon_chevron_up.png` | 16×16 | — (고정 크기) | icon |
| `04_Icons/icon_clock.png` | 16×16 | — (고정 크기) | icon |
| `04_Icons/icon_coin.png` | 16×16 | — (고정 크기) | icon |
| `04_Icons/icon_coin_stack.png` | 16×16 | — (고정 크기) | icon |
| `04_Icons/icon_flamesword.png` | 16×16 | — (고정 크기) | icon |
| `04_Icons/icon_gavel.png` | 16×16 | — (고정 크기) | icon |
| `04_Icons/icon_grid.png` | 16×16 | — (고정 크기) | icon |
| `04_Icons/icon_helm.png` | 16×16 | — (고정 크기) | icon |
| `04_Icons/icon_magnifier.png` | 16×16 | — (고정 크기) | icon |
| `04_Icons/icon_ore.png` | 16×16 | — (고정 크기) | icon |
| `04_Icons/icon_plus.png` | 16×16 | — (고정 크기) | icon |
| `04_Icons/icon_potion.png` | 16×16 | — (고정 크기) | icon |
| `04_Icons/icon_ring.png` | 16×16 | — (고정 크기) | icon |
| `04_Icons/icon_shield.png` | 16×16 | — (고정 크기) | icon |
| `04_Icons/icon_sword.png` | 16×16 | — (고정 크기) | icon |
| `05_Ornaments/banner_center.png` | 24×26 | L6 T0 R6 B0 | banner center – stretch X only |
| `05_Ornaments/banner_ribbon.png` | 48×30 | L14 T6 R14 B6 | title ribbon (stretch horizontally only) |
| `05_Ornaments/banner_tail_left.png` | 14×26 | — (고정 크기) | banner left tail |
| `05_Ornaments/banner_tail_right.png` | 14×26 | — (고정 크기) | banner right tail |
| `05_Ornaments/corner_filigree.png` | 14×14 | — (고정 크기) | corner decoration (rotate 90/180/270 in Unity) |
| `05_Ornaments/divider_gold.png` | 32×6 | L8 T0 R8 B0 | horizontal gold divider |
| `05_Ornaments/gem_purple.png` | 12×12 | — (고정 크기) | small decorative gem |
| `05_Ornaments/scroll_handle.png` | 10×24 | L4 T4 R4 B4 | scrollbar handle |
| `05_Ornaments/scroll_track.png` | 10×24 | L4 T4 R4 B4 | scrollbar track |

### 축별 늘리기 주의
- `05_Ornaments/banner_center.png`, `divider_gold.png` : **가로만** 늘리세요 (세로 고정).
- `02_Buttons/btn_*` : 세로 그라디언트가 있어 세로로 크게 늘리면 그라디언트가 뭉개집니다.
  높이는 원본의 정수배(18·36·54…)로 쓰는 것을 권장합니다.
- Border가 `—` 인 것(닫기 버튼, 아이콘, 젬, 배너 꼬리)은 **Image Type = Simple** + 정수배 크기로 사용.

---

## 4. 조립 계층 구조 (권장 Hierarchy)

```
Canvas  (Render Mode: Screen Space - Overlay)
└─ AuctionWindow                Image: window_frame.png (Sliced)
   ├─ TitleBanner               (Horizontal 배치용 빈 오브젝트)
   │  ├─ TailLeft               Image: banner_tail_left.png  (Simple)
   │  ├─ Center                 Image: banner_center.png     (Sliced, 가로만 늘림)
   │  │  └─ Text_Title          TMP: "ITEM AUCTION HOUSE"
   │  └─ TailRight              Image: banner_tail_right.png (Simple)
   ├─ Text_Subtitle             TMP: "MARKETPLACE"
   ├─ SearchArea
   │  ├─ Text_Label             TMP: "SEARCH ITEMS:"
   │  └─ InputField_Search      Image: field_search.png (Sliced)
   │     └─ Icon_Search         Image: icon_magnifier.png
   ├─ Btn_CloseWindow           Button + Image: btn_close_normal.png
   ├─ SidebarPanel              Image: panel_wood.png (Sliced)
   │  └─ CategoryList           Vertical Layout Group (Spacing 4)
   │     └─ Btn_Category (×7)   Button + Image: btn_category_normal.png
   │        ├─ Icon             Image: icon_grid / sword / shield / potion / ore / bag / gavel
   │        └─ Label            TMP: "WEAPONS" ...
   └─ ContentPanel              Image: panel_parchment.png (Sliced)
      ├─ Text_ListingsTitle     TMP: "CURRENT LISTINGS"
      ├─ Btn_ClosePanel         Button + Image: btn_close_normal.png
      ├─ TableHeader            Image: bar_table_header.png (Sliced)
      │  └─ TMP ×7              ITEM / LVL / QTY / SELLER / TIME LEFT / BID / BUY
      ├─ ScrollView_Listings    Scroll Rect (Vertical), Viewport + Mask
      │  ├─ Content             Vertical Layout Group + Content Size Fitter
      │  │  └─ ListingRow (프리팹)   Image: row_normal.png (Sliced)
      │  │     ├─ ItemSlot          Image: slot_legendary.png (Sliced)
      │  │     │  └─ ItemIcon       Image: icon_flamesword.png
      │  │     ├─ Text_Name / Text_Desc / Text_Lvl / Text_Qty
      │  │     ├─ SellerAvatar      Image: frame_avatar.png + 초상화 자식
      │  │     ├─ Text_Seller / Text_TimeLeft
      │  │     ├─ PriceGroup        Text_BidPrice + Icon_Coin(icon_coin.png)
      │  │     ├─ Btn_Bid           Button + btn_blue_normal.png
      │  │     └─ Btn_BuyNow        Button + btn_gold_normal.png
      │  └─ Scrollbar_Vertical      scroll_track.png / scroll_handle.png
      ├─ Badge_Gold             Image: badge_currency.png (Sliced)
      │  └─ Text_Gold + Icon_Coin
      └─ Pagination             Horizontal Layout Group
         ├─ Btn_Prev            btn_page_normal.png + icon_chevron_left
         ├─ Btn_Page (×N)       btn_page_normal / btn_page_sel_normal
         └─ Btn_Next            btn_page_normal.png + icon_chevron_right
```

### 행(ListingRow) 프리팹 팁
- 컬럼 정렬은 Horizontal Layout Group 대신 **각 자식의 anchorMin/Max를 컬럼 비율로 고정**하는 편이
  헤더와 어긋나지 않습니다. (예: LVL 열 = anchorX 0.42~0.50)
- 등급 표현은 `slot_*.png` 교체 + 아이템명 TMP 색상 교체 2가지를 같이 쓰세요. 색상은 6절 참고.

---

## 5. 버튼 상태 연결

Button 컴포넌트 → **Transition: Sprite Swap** 으로 두고 아래처럼 지정합니다.

| Button | Target(Normal) | Highlighted | Pressed | Disabled |
|---|---|---|---|---|
| 입찰(BID) | `btn_blue_normal` | `btn_blue_hover` | `btn_blue_pressed` | `btn_blue_disabled` |
| 즉시구매 | `btn_gold_normal` | `btn_gold_hover` | `btn_gold_pressed` | `btn_gold_disabled` |
| 취소/위험 | `btn_red_normal` | `btn_red_hover` | `btn_red_pressed` | `btn_red_disabled` |
| 일반/보조 | `btn_wood_normal` | `btn_wood_hover` | `btn_wood_pressed` | `btn_wood_disabled` |
| 카테고리 탭 | `btn_category_normal` | `btn_category_hover` | `btn_category_selected` | — |
| 닫기 | `btn_close_normal` | `btn_close_hover` | `btn_close_pressed` | — |
| 페이지 | `btn_page_normal` | `btn_page_hover` | `btn_page_pressed` | — |

카테고리 탭의 "현재 선택" 상태는 Sprite Swap의 Pressed가 아니라 **Toggle Group + ToggleGroup의
Graphic 교체** 또는 스크립트에서 `image.sprite = selectedSprite` 로 처리하는 편이 안정적입니다.
(`btn_category_selected` = 골드 테두리 버전)

행 hover/선택은 `row_normal` / `row_alt` / `row_hover` / `row_selected` 를 스크립트로 교체하세요.

---

## 6. 팔레트

| 역할 | HEX | 역할 | HEX |
|---|---|---|---|
| 외곽선 | `#120A0C` | 창 배경 | `#1B1014` |
| 나무 하이라이트 | `#8A5236` | 나무 상단 | `#6E3F28` |
| 나무 하단 | `#492618` | 나무 그림자 | `#331A11` |
| 골드 하이라이트 | `#F7D479` | 골드 | `#D9962B` |
| 골드 섀도 | `#8A5A18` | 골드 딥 | `#5C3A0E` |
| 양피지 하이라이트 | `#EBD1A3` | 양피지 상단 | `#DEBB86` |
| 양피지 하단 | `#C79E68` | 양피지 테두리 | `#8A6038` |
| 블루 버튼 상단 | `#3B93E0` | 블루 버튼 하단 | `#1F5FA8` |
| 골드 버튼 상단 | `#F6BE3E` | 골드 버튼 하단 | `#C4801B` |
| 레드 상단 | `#C23A38` | 레드 하단 | `#8E2226` |

**등급 색상 (아이템명 TMP 색 + 슬롯 프레임)**

| 등급 | HEX | 파일 |
|---|---|---|
| Common | `#9AA3AD` | `slot_common.png` |
| Uncommon | `#4F9BE0` | `slot_uncommon.png` |
| Rare | `#3FBF6F` | `slot_rare.png` |
| Epic | `#B15CE8` | `slot_epic.png` |
| Legendary | `#F0872A` | `slot_legendary.png` |
| Quest/특수 | `#E04A4A` | `slot_quest.png` |

---

## 7. 해상도 / 스케일 전략

1. Canvas → **Canvas Scaler: Scale With Screen Size**, Reference Resolution `1920×1080`,
   Match `0.5`.
2. 픽셀아트가 흐려지지 않게 하려면 **UI 요소 크기를 원본 스프라이트의 정수배로만** 쓰세요.
   이 키트는 4배(=1x 스프라이트 1px → 화면 4px)를 기준으로 설계했습니다.
   예: `btn_blue`(28×18) → 실제 UI 112×72, `slot_*`(20×20) → 80×80, 아이콘(16×16) → 64×64.
3. 조립 예시(`00_Preview`)의 논리 캔버스는 **344×192** 입니다. 4배 → 1376×768,
   5배 → 1720×960 로 원본 목업과 거의 같은 비율이 됩니다.
4. Project Settings → Player 에서 색 밴딩이 보이면 Color Space를 Linear가 아닌 Gamma로 두거나
   스프라이트 압축을 None으로 유지하세요.

---

## 8. 폰트

원본의 글자는 모두 제거했으므로 TextMeshPro로 직접 넣으시면 됩니다. 이 톤에 어울리는 픽셀 폰트:

- **한글 지원**: Galmuri (갈무리) 9/11/14, Neo둥근모, DungGeunMo — 모두 무료/오픈 라이선스
- **영문 전용**: Silkscreen, Press Start 2P, m5x7, Pixellari

TMP 세팅: Sampling Point Size = 폰트의 기본 픽셀 크기(예: 11), **Atlas Render Mode = SMOOTH 대신
`RASTER_HINTED`**, Material의 Sharpness 최대, Anti-aliasing 없음 → 픽셀이 또렷하게 유지됩니다.
텍스트 크기도 폰트 기본 크기의 정수배로 쓰세요.

---

## 9. 색·크기 커스터마이즈

`07_Source/` 의 스크립트를 그대로 다시 돌리면 전체 키트가 재생성됩니다.

```bash
pip install pillow
python3 gen_frames.py     # 프레임·패널·버튼·슬롯
python3 gen_icons.py      # 아이콘·배너
python3 gen_preview.py    # 조립 프리뷰
python3 upscale.py 4      # 전체 4배 확대본 생성
```

- 색을 바꾸려면 `palette.py` 의 HEX만 수정하세요 (전체 파트에 일괄 반영됩니다).
- 크기를 바꾸려면 각 생성 함수 안의 `w, h` 값을 수정하고, 9-slice Border도 그에 맞게 다시 잡으세요.
- 아이콘을 추가하려면 `gen_icons.py` 의 `ICONS` 딕셔너리에 16×16 문자 맵을 추가하면 됩니다
  (문자 → 색 대응은 같은 파일 상단 `PAL` 참고).

---

## 10. 원본 대비 의도적으로 뺀 것

- 모든 텍스트(제목, 컬럼명, 아이템명, 가격, 버튼 라벨) → TMP로 대체
- 장식용 룬 문자열 → 제거 (필요하면 폰트로 넣는 편이 관리하기 쉽습니다)
- 판매자 초상화 일러스트 → 프레임(`frame_avatar.png`)만 제공. 안쪽에 실제 초상화 스프라이트를 넣으세요.
- 아이템 아이콘은 원본과 1:1 동일하지 않은, 같은 스타일의 대체 아이콘입니다.
