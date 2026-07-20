# Knight Sword Slash (Fixed)

검기 일부가 셀 경계에서 잘리지 않도록 모든 프레임을 `512 x 512` 투명 캔버스로 다시 구성했습니다.

## Unity 임포트

`KnightSwordSlash_Sheet_8x1.png` 설정:

```text
Texture Type: Sprite (2D and UI)
Sprite Mode: Multiple
Filter Mode: Point (no filter)
Compression: None
Wrap Mode: Clamp
Generate Mip Maps: Off
```

Sprite Editor에서 다음과 같이 자릅니다.

```text
Slice Type: Grid by Cell Size
Pixel Size: 512 x 512
Pivot: Center
```

개별 PNG를 사용하는 경우 `Frames` 폴더의 8개 파일을 순서대로 Animation 창에 넣으면 됩니다.

```text
Samples: 20
Loop Time: Off
```

