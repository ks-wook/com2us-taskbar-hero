"""Compose a full auction-house screen out of the exported sprites (proof of assembly)."""
import os
from PIL import Image
from pixkit import nine_slice_draw, hx
from palette import P

OUT = '/mnt/user-data/outputs/PixelUI_AuctionKit'
D_PREV = OUT + '/00_Preview'
os.makedirs(D_PREV, exist_ok=True)
L = lambda p: Image.open(OUT + '/' + p).convert('RGBA')

W, H = 344, 192
cv = Image.new('RGBA', (W, H), (0, 0, 0, 0))

# window
nine_slice_draw(cv, L('01_Frames_Panels/window_frame.png'), (20, 20, 20, 20), 0, 0, W, H)

# title banner
bw = 150
nine_slice_draw(cv, L('05_Ornaments/banner_center.png'), (6, 0, 6, 0), (W - bw) // 2, 4, bw, 26)
cv.alpha_composite(L('05_Ornaments/banner_tail_left.png'), ((W - bw) // 2 - 11, 4))
cv.alpha_composite(L('05_Ornaments/banner_tail_right.png'), ((W + bw) // 2 - 3, 4))

# search field
nine_slice_draw(cv, L('01_Frames_Panels/field_search.png'), (7, 7, 7, 7), 236, 16, 92, 18)
cv.alpha_composite(L('04_Icons/icon_magnifier.png'), (239, 17))

# close button
cv.alpha_composite(L('02_Buttons/btn_close_normal.png'), (W - 26, 8))

# sidebar
nine_slice_draw(cv, L('01_Frames_Panels/panel_wood.png'), (10, 10, 10, 10), 12, 40, 84, 140)
cats = ['grid', 'sword', 'shield', 'potion', 'ore', 'bag', 'gavel']
for i, ic in enumerate(cats):
    y = 48 + i * 19
    state = 'selected' if i == 1 else 'normal'
    nine_slice_draw(cv, L('02_Buttons/btn_category_%s.png' % state), (9, 8, 9, 8), 18, y, 72, 17)
    cv.alpha_composite(L('04_Icons/icon_%s.png' % ic), (20, y + 1))

# content parchment
px, py, pw, ph = 104, 40, 228, 140
nine_slice_draw(cv, L('01_Frames_Panels/panel_parchment.png'), (8, 8, 8, 8), px, py, pw, ph)

# table header
nine_slice_draw(cv, L('01_Frames_Panels/bar_table_header.png'), (6, 6, 6, 6), px + 6, py + 16, pw - 12, 12)

# rows
rarities = ['legendary', 'uncommon', 'quest', 'common', 'epic']
items = ['flamesword', 'helm', 'potion', 'ring', 'bow']


def textbar(x, y, w, h=3, col=hx('#FFFFFF', 130)):
    cv.alpha_composite(Image.new('RGBA', (w, h), col), (x, y))


for i in range(5):
    ry = py + 31 + i * 20
    nine_slice_draw(cv, L('01_Frames_Panels/row_%s.png' % ('normal' if i % 2 == 0 else 'alt')),
                    (6, 6, 6, 6), px + 6, ry, pw - 22, 19)
    nine_slice_draw(cv, L('03_Slots_Frames/slot_%s.png' % rarities[i]), (5, 5, 5, 5), px + 9, ry + 2, 15, 15)
    cv.alpha_composite(L('04_Icons/icon_%s.png' % items[i]).resize((13, 13), Image.NEAREST), (px + 10, ry + 3))
    textbar(px + 27, ry + 5, 34, 4, hx('#F2C878', 220))     # item name
    textbar(px + 27, ry + 11, 26, 3)                        # description
    textbar(px + 66, ry + 8, 8, 4)                          # lvl
    textbar(px + 79, ry + 8, 6, 4)                          # qty
    nine_slice_draw(cv, L('03_Slots_Frames/frame_avatar.png'), (5, 5, 5, 5), px + 90, ry + 3, 14, 14)
    textbar(px + 107, ry + 8, 22, 4)                        # seller
    textbar(px + 134, ry + 8, 20, 4)                        # time left
    textbar(px + 154, ry + 8, 16, 4, hx('#FFD870', 230))    # price
    cv.alpha_composite(L('04_Icons/icon_coin.png').resize((10, 10), Image.NEAREST), (px + 172, ry + 5))
    nine_slice_draw(cv, L('02_Buttons/btn_blue_normal.png'), (8, 6, 8, 6), px + 182, ry + 4, 14, 12)
    nine_slice_draw(cv, L('02_Buttons/btn_gold_normal.png'), (8, 6, 8, 6), px + 197, ry + 4, 16, 12)

# currency badge
nine_slice_draw(cv, L('01_Frames_Panels/badge_currency.png'), (7, 7, 7, 7), px + 4, py + ph - 16, 62, 16)
textbar(px + 9, py + ph - 10, 32, 4, hx('#5C3A0E', 220))
cv.alpha_composite(L('04_Icons/icon_coin.png').resize((10, 10), Image.NEAREST), (px + 46, py + ph - 13))

# pagination
bx = px + pw - 58
for i, kind in enumerate(['page', 'page_sel', 'page', 'page']):
    nine_slice_draw(cv, L('02_Buttons/btn_%s_normal.png' % kind), (5, 5, 5, 5), bx + i * 14, py + ph - 16, 13, 13)
cv.alpha_composite(L('04_Icons/icon_chevron_left.png').resize((9, 9), Image.NEAREST), (bx + 2, py + ph - 14))
cv.alpha_composite(L('04_Icons/icon_chevron_right.png').resize((9, 9), Image.NEAREST), (bx + 44, py + ph - 14))

# scrollbar
nine_slice_draw(cv, L('05_Ornaments/scroll_track.png'), (4, 4, 4, 4), px + pw - 12, py + 31, 8, 96)
nine_slice_draw(cv, L('05_Ornaments/scroll_handle.png'), (4, 4, 4, 4), px + pw - 12, py + 33, 8, 34)

for s in (1, 4):
    cv.resize((W * s, H * s), Image.NEAREST).save(D_PREV + '/assembled_preview_%dx.png' % s)

# palette swatch
sw = 16
keys = list(P.keys())
pl = Image.new('RGBA', (sw * 8, sw * ((len(keys) + 7) // 8)), (20, 14, 16, 255))
for i, k in enumerate(keys):
    x, y = (i % 8) * sw, (i // 8) * sw
    pl.alpha_composite(Image.new('RGBA', (sw - 1, sw - 1), P[k]), (x, y))
pl.resize((pl.width * 3, pl.height * 3), Image.NEAREST).save(OUT + '/06_Palette/palette_swatch.png') \
    if os.path.isdir(OUT + '/06_Palette') else (os.makedirs(OUT + '/06_Palette'),
                                                pl.resize((pl.width * 3, pl.height * 3), Image.NEAREST).save(OUT + '/06_Palette/palette_swatch.png'))
print('preview ok')
