#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""캐릭터 일러스트 컷아웃 생성기 (초록 크로마키 배경 제거 + 얼굴 좌표 산출)

배경
  `Assets/Art/Character/Image/*.png`는 **초록 크로마키 배경의 전신 픽셀아트**다(1024×559 / 2816×1536).
  편성창 파티 카드는 이 일러스트의 **얼굴만 확대해** 액자(party_slot) 안에 얹으므로,
  배경을 그대로 두면 머리 주변에 초록 사각형이 남는다. 그래서 배경을 투명 처리한 사본을 만든다.

출력
  1) `Assets/Art/Character/Image/Cutout/<Name>.png` — 배경 투명 + 가로 1024로 정규화(원본은 건드리지 않는다)
  2) 얼굴 중심 normalized 좌표(x, y는 **위에서부터**) 콘솔 출력
  3) `--sheet` 지정 시 얼굴 크롭 8종 대지 PNG(눈으로 프레이밍 검증)

얼굴 좌표
  살색 픽셀의 '가장 위 덩어리'(머리)를 자동 추정하지만, 손·천이 얼굴로 잡히는 3종은
  아래 FACE_OVERRIDE로 수동 보정한다(격자를 얹어 눈으로 읽은 값).
  **최종 값은 Unity의 `CharacterIllustrationDatabaseBuilder`가 갖고 있다** — 이 스크립트는
  일러스트를 교체했을 때 새 좌표를 얻기 위한 도구다.

사용
  python tools/character_illust_cutout.py [--sheet out.png]
의존성: pillow, numpy
"""
import argparse
import glob
import os
import sys

import numpy as np
from PIL import Image

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding="utf-8")
    except Exception:
        pass

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.dirname(SCRIPT_DIR)
SRC_DIR = os.path.join(REPO_ROOT, "com2us-taskbar-hero-client", "Assets", "Art", "Character", "Image")
OUT_DIR = os.path.join(SRC_DIR, "Cutout")

TARGET_W = 1024               # 컷아웃 가로 정규화(2816은 축소, 1024는 그대로)
KEY_LOW, KEY_HIGH = 70, 130   # 배경색 거리 임계(이하 투명 / 이상 불투명 / 사이는 램프)

# 파티 카드 안쪽 창 비율(가로/세로)과 크롭 높이 — TeamListController의 값과 맞춰 둔다(대지 검증용).
WINDOW_ASPECT = 0.5772
# 크롭 높이는 얼굴 좌표의 ±0.02 오차에도 얼굴이 프레임 안에 남도록 여유를 둔 값이다(얼굴+어깨까지).
CROP_HEIGHT = 0.46
FACE_IN_CROP = 0.34           # 크롭 위에서 얼굴이 놓이는 비율

# 확정 좌표 — **8종 전부 편성창을 플레이 모드로 띄워 카드를 보며 맞춘 값**이다.
# 이 스크립트의 자동 추정(살색 픽셀)은 첫 값을 잡는 용도이고, 최종 값은 화면에서 확인해 정했다.
#
# **자동 추정을 믿을 수 없는 이유** — 픽셀아트 캐릭터는 손·팔·다리 살색 면적이 얼굴보다 크고,
# 무기를 든 손이 머리와 같은 높이에 있는 경우가 많다("가장 위 살색 덩어리" 규칙이 손을 집는다).
# 일러스트를 교체하면 자동 추정으로 첫 값을 얻은 뒤 --sheet 대지로 확인하고, 인게임에서 최종 조정한다.
#
# 게임이 실제로 쓰는 값은 Unity의 CharacterIllustrationDatabaseBuilder.FaceAnchors 이며 이 표와 같게 유지한다
# (여기가 어긋나면 --sheet 대지가 게임 화면과 다른 프레이밍을 보여 준다).
FACE_OVERRIDE = {
    "Knight_Male": (0.521, 0.205),
    "Knight_Female": (0.511, 0.189),
    "Archer_Male": (0.500, 0.247),
    "Archer_Female": (0.477, 0.227),
    "Mage_Male": (0.518, 0.179),       # 남·여 구도가 같아 한 값을 공유한다
    "Mage_Female": (0.518, 0.179),
    "Slayer_Male": (0.518, 0.172),
    "Slayer_Female": (0.522, 0.205),
}


def bg_color(rgba):
    """테두리 4변 픽셀의 최빈 RGB = 크로마키 배경색."""
    border = np.concatenate([rgba[0, :, :3], rgba[-1, :, :3], rgba[:, 0, :3], rgba[:, -1, :3]])
    colors, counts = np.unique(border.reshape(-1, 3), axis=0, return_counts=True)
    return colors[counts.argmax()]


def skin_mask(rgb):
    """픽셀아트 얼굴의 살색 대략 판정(따뜻한 중간 밝기 + R>G>B)."""
    r = rgb[..., 0].astype(int)
    g = rgb[..., 1].astype(int)
    b = rgb[..., 2].astype(int)
    return (r > 120) & (g > 70) & (g < 210) & (b > 45) & (b < 190) & \
           (r - g > 18) & (g - b > 5) & (r - b > 35)


def estimate_face(rgb, fg, bbox_height):
    """살색 픽셀 중 가장 위 덩어리(머리)의 무게중심. 손·팔·다리에 끌리지 않도록 상단 띠만 본다."""
    skin = skin_mask(rgb) & fg
    ys, xs = np.where(skin)
    if len(ys) < 30:
        return None
    head_top = np.percentile(ys, 5)
    band = skin & (np.arange(rgb.shape[0])[:, None] <= head_top + bbox_height * 0.12)
    by, bx = np.where(band)
    return bx.mean() / rgb.shape[1], by.mean() / rgb.shape[0]


def cut_out(img):
    """배경을 투명 처리한 RGBA 이미지와 배경색을 돌려준다(경계의 초록 프린지도 억제)."""
    rgba = np.array(img)
    a = rgba.astype(int)
    bg = bg_color(rgba)
    dist = np.abs(a[:, :, :3] - bg.astype(int)).sum(axis=2)

    alpha = np.clip((dist - KEY_LOW) * 255.0 / (KEY_HIGH - KEY_LOW), 0, 255)
    rgb = a[:, :, :3].astype(float)
    edge = (alpha > 0) & (alpha < 255)
    excess = rgb[:, :, 1] - np.maximum(rgb[:, :, 0], rgb[:, :, 2])   # 초록 과다분
    rgb[:, :, 1] = np.where(edge & (excess > 0), rgb[:, :, 1] - excess * 0.9, rgb[:, :, 1])

    out = Image.fromarray(np.dstack([np.clip(rgb, 0, 255), alpha]).astype(np.uint8), "RGBA")
    return out, dist > KEY_LOW


def main():
    ap = argparse.ArgumentParser(description="캐릭터 일러스트 컷아웃 생성기")
    ap.add_argument("--sheet", help="얼굴 크롭 대지 PNG 경로(검증용, 생략 시 만들지 않는다)")
    args = ap.parse_args()

    os.makedirs(OUT_DIR, exist_ok=True)
    crops = []
    print("이름              얼굴 x      얼굴 y     출처     컷아웃")
    for path in sorted(glob.glob(os.path.join(SRC_DIR, "*.png"))):
        name = os.path.splitext(os.path.basename(path))[0]
        img = Image.open(path).convert("RGBA")
        cut, fg = cut_out(img)

        ys, _ = np.where(fg)
        bbox_h = ys.max() - ys.min()
        auto = estimate_face(np.array(img)[:, :, :3], fg, bbox_h)
        if name in FACE_OVERRIDE:
            face, how = FACE_OVERRIDE[name], "수동"
        elif auto is not None:
            face, how = auto, "자동"
        else:
            face, how = (0.5, 0.2), "기본"

        if cut.width > TARGET_W:
            cut = cut.resize((TARGET_W, int(round(cut.height * TARGET_W / cut.width))), Image.LANCZOS)
        cut.save(os.path.join(OUT_DIR, name + ".png"))
        print("%-16s %.4f    %.4f    %-6s   %dx%d" % (name, face[0], face[1], how, cut.width, cut.height))

        if args.sheet:
            ch = CROP_HEIGHT * cut.height
            cw = ch * WINDOW_ASPECT
            cx, cy = face[0] * cut.width, face[1] * cut.height
            box = (int(cx - cw / 2), int(cy - ch * FACE_IN_CROP),
                   int(cx + cw / 2), int(cy + ch * (1 - FACE_IN_CROP)))
            crops.append((name, cut.crop(box).resize((180, int(180 / WINDOW_ASPECT)), Image.LANCZOS)))

    print("컷아웃 출력: %s" % OUT_DIR)

    if args.sheet and crops:
        tw, th = crops[0][1].size
        sheet = Image.new("RGBA", (tw * len(crops), th), (40, 44, 60, 255))
        for i in range(0, sheet.width, 16):          # 알파 확인용 체커 배경
            for j in range(0, sheet.height, 16):
                if (i // 16 + j // 16) % 2 == 0:
                    sheet.paste((60, 66, 86, 255),
                                (i, j, min(i + 16, sheet.width), min(j + 16, sheet.height)))
        for i, (_, thumb) in enumerate(crops):
            sheet.alpha_composite(thumb, (i * tw, 0))
        sheet.save(args.sheet)
        print("대지: %s (%s)" % (args.sheet, ", ".join(n for n, _ in crops)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
