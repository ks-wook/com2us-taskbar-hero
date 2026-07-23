# 공용 모달(안내창) UI 기획서 (클라이언트)

> 클라이언트 전역에서 재사용하는 **공용 모달(안내/선택 창)** 을 다룬다. 특정 도메인에 종속되지 않는 UI 인프라다.

## 1. 개요

- **목적**: 성공/실패 안내, 확인 요청 등을 일관된 모양으로 띄우는 **전역 공용 모달**. 어느 화면에서든 한 줄로 호출한다.
- **두 종류**(한 프리팹으로 표현):
  - **확인 모달**(`ShowConfirm`): `확인` 버튼만. 단순 안내.
  - **확인/취소 모달**(`ShowConfirmCancel`): `취소`·`확인` 버튼. 사용자 선택.
- **에셋**: 배경 `Assets/Art/UI/modal_bg.png`, 버튼 `Assets/Art/UI/pixel_rpg_button.png`(9-slice, `Image.Type.Sliced`). 딤 배경으로 뒤 입력을 차단한다.
- **연출**: 표시 시 패널 팝인(0.85→1), **버튼 클릭 시 커졌다 원상 복귀(punch)** 후 콜백 실행. 모두 `unscaledTime`이라 게임 일시정지 중에도 정상 동작한다.

## 2. 구성

- `Assets/Scripts/Managers/ModalController.cs` (asmdef `TaskbarHero.Client.Managers`) — 모달 계층 생성·표시/숨김·버튼 punch. `UnityEngine.UI` 참조를 위해 Managers asmdef에 추가.
- `Assets/Scripts/Managers/ModalManager.cs` — **DontDestroyOnLoad 싱글턴**. 모달 프리팹 인스턴스를 1개 캐싱해 재사용. `Instance.ShowConfirm(...)` / `ShowConfirmCancel(...)`.
- `Assets/Prefabs/UI/Modal.prefab` — 컨트롤러 + 스프라이트 참조 + 정적 계층(오버레이 Canvas `sortingOrder=500`).
- `Assets/Editor/ModalUiBuilder.cs` — 메뉴 **TaskbarHero/UI/모달 패널·씬 생성**(프리팹 생성 + Title/GameScene에 ModalManager 배치·배선).
- **어셈블리 위치 이유**: 모든 어셈블리(UI·Battle·Game)가 `Managers`를 참조하므로, 모달을 Managers에 두면 클라이언트 전역에서 호출 가능하다.

## 3. 사용법

```csharp
// 확인만
ModalManager.Instance.ShowConfirm("회원가입 완료", "회원가입이 완료되었습니다.\n로그인해 주세요.", () => UIManager.Instance.ShowLogin());
// 확인/취소
ModalManager.Instance.ShowConfirmCancel("구매 확인", "구매하시겠습니까?", onOk: Buy, onCancel: null);
```

- 매니저가 없을 수 있는 경로(초기화 전 등)는 호출측에서 폴백(인라인 문구)을 둔다.

## 4. 적용 지점(현재)

- **TitleScene 로그인 실패**(`LoginPanelController`): 로그인/데이터 로드 실패 시 `확인` 모달로 사유 안내(기존 인라인 문구 대체).
- **TitleScene 회원가입 완료/실패**(`SignUpPanelController`): 가입 완료 시 `확인` 모달 → 확인하면 로그인 화면으로. 실패 시 `확인` 모달로 사유 안내.

## 5. 미결 / 확장

- CreateCharacterScene 등 다른 씬에도 ModalManager 배치(현재 Title·GameScene). DontDestroyOnLoad로 대부분 유지되나 직접 진입 대비.
- 입력 필드/체크박스가 필요한 고급 모달은 별도 확장으로.
