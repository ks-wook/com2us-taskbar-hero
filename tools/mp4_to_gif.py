#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""mp4 -> GIF 변환 (ffmpeg 없이 cv2 + PIL 만 사용).

GitHub 마크다운은 <video> 태그를 지우고 저장소 내 mp4도 인라인 재생하지 않지만,
GIF는 이미지로 그대로 자동 재생된다. 발표 문서용으로 해상도·프레임을 줄여 옮긴다.
"""
import argparse
import os
import sys

import cv2
import numpy as np
from PIL import Image

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding="utf-8")
    except Exception:
        pass


def convert(src, dst, width, sample_fps, play_fps, colors, start=0.0, end=None, crop=None):
    cap = cv2.VideoCapture(src)
    if not cap.isOpened():
        raise SystemExit(f"열 수 없음: {src}")

    src_fps = cap.get(cv2.CAP_PROP_FPS) or 30.0
    total = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
    w0 = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
    h0 = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))

    first = int(start * src_fps)
    last = int(end * src_fps) if end else total
    step = max(1, round(src_fps / sample_fps))

    if crop:
        cx, cy, cw, ch = crop
        cw = min(cw, w0 - cx)
        ch = min(ch, h0 - cy)
    else:
        cx, cy, cw, ch = 0, 0, w0, h0

    height = int(round(ch * width / cw / 2) * 2)
    rgbs = []
    idx = first
    cap.set(cv2.CAP_PROP_POS_FRAMES, first)
    while idx < last:
        ok, frame = cap.read()
        if not ok:
            break
        if (idx - first) % step == 0:
            roi = frame[cy:cy + ch, cx:cx + cw]
            small = cv2.resize(roi, (width, height), interpolation=cv2.INTER_AREA)
            rgbs.append(Image.fromarray(cv2.cvtColor(small, cv2.COLOR_BGR2RGB)))
        idx += 1
    cap.release()

    if not rgbs:
        raise SystemExit("추출된 프레임이 없습니다.")

    # 프레임마다 팔레트가 다르면 GIF가 매 프레임 팔레트를 싣고 차분 최적화도 못 해 용량이 급증한다.
    # 전체에서 뽑은 **공통 팔레트** 하나로 모든 프레임을 양자화한다(화면 녹화에서 특히 효과가 크다).
    sample = rgbs[:: max(1, len(rgbs) // 24)][:24]
    strip = Image.new("RGB", (width, height * len(sample)))
    for i, im in enumerate(sample):
        strip.paste(im, (0, i * height))
    palette = strip.quantize(colors=colors, method=Image.MEDIANCUT)
    frames = [im.quantize(palette=palette, dither=Image.Dither.NONE) for im in rgbs]

    duration = int(round(1000.0 / play_fps))
    # disposal=1(이전 프레임 유지) 이라야 PIL이 바뀐 영역만 싣는 차분 최적화를 할 수 있다.
    frames[0].save(dst, save_all=True, append_images=frames[1:], loop=0,
                   duration=duration, optimize=True, disposal=1)

    size_mb = os.path.getsize(dst) / (1024 * 1024)
    src_sec = (last - first) / src_fps
    play_sec = len(frames) / play_fps
    print(f"{os.path.basename(dst)}: {width}x{height} / {len(frames)}프레임 / "
          f"원본 {src_sec:.1f}초 → 재생 {play_sec:.1f}초 ({src_sec / play_sec:.1f}배속) / {size_mb:.1f}MB")
    return size_mb


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("src")
    ap.add_argument("dst")
    ap.add_argument("--width", type=int, default=800)
    ap.add_argument("--sample-fps", type=float, default=8.0, help="원본에서 뽑는 초당 프레임 수")
    ap.add_argument("--play-fps", type=float, default=10.0, help="GIF 재생 fps(샘플보다 크면 빨라진다)")
    ap.add_argument("--colors", type=int, default=128)
    ap.add_argument("--start", type=float, default=0.0)
    ap.add_argument("--end", type=float)
    ap.add_argument("--crop", help="원본 픽셀 기준 잘라낼 영역 'x,y,w,h'")
    a = ap.parse_args()
    crop = tuple(int(v) for v in a.crop.split(",")) if a.crop else None
    convert(a.src, a.dst, a.width, a.sample_fps, a.play_fps, a.colors, a.start, a.end, crop)
