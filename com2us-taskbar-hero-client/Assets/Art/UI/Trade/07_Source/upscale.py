"""1x 스프라이트를 정수배(기본 4x)로 일괄 확대 복사.
사용법:  python3 upscale.py 4
결과:    PixelUI_AuctionKit_4x/ 폴더에 동일한 구조로 저장 (9-slice 경계값도 4배로 곱해서 쓰면 됩니다)
"""
import os, sys, glob
from PIL import Image

SCALE = int(sys.argv[1]) if len(sys.argv) > 1 else 4
SRC = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
DST = SRC.rstrip('/') + '_%dx' % SCALE

n = 0
for path in glob.glob(SRC + '/**/*.png', recursive=True):
    if '07_Source' in path or '00_Preview' in path:
        continue
    rel = os.path.relpath(path, SRC)
    out = os.path.join(DST, rel)
    os.makedirs(os.path.dirname(out), exist_ok=True)
    im = Image.open(path)
    im.resize((im.width * SCALE, im.height * SCALE), Image.NEAREST).save(out)
    n += 1
print('%d files -> %s' % (n, DST))
