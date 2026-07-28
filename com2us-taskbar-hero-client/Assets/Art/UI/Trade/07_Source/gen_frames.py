import os
from pixkit import C, hx, mix, NONE, bevel_panel
from palette import P

OUT = '/mnt/user-data/outputs/PixelUI_AuctionKit'
D_FRAME = OUT + '/01_Frames_Panels'
D_BTN = OUT + '/02_Buttons'
D_SLOT = OUT + '/03_Slots_Frames'
D_ORN = OUT + '/05_Ornaments'
for d in (D_FRAME, D_BTN, D_SLOT, D_ORN):
    os.makedirs(d, exist_ok=True)

MANIFEST = []  # (relpath, w, h, border L,T,R,B, note)


def reg(path, c, border=None, note=''):
    MANIFEST.append((os.path.relpath(path, OUT), c.w, c.h, border, note))


# ---------------------------------------------------------------- 1. WINDOW
def window_frame(filled=True):
    """Main window: dark body, wood frame, double gold trim, filigree corners."""
    S = 64          # 64x64, border 20
    c = C(S, S)
    o, gh, g, gl = P['outline'], P['gold_hi'], P['gold'], P['gold_lo']

    # body
    if filled:
        c.rect(0, 0, S - 1, S - 1, P['bg_dark'])
    # wood band: rows/cols 0..13
    c.rect(0, 0, S - 1, S - 1, P['wood_bot']) if False else None

    def band(i0, i1, col):
        for i in range(i0, i1 + 1):
            c.hline(i, S - 1 - i, i, col)
            c.hline(i, S - 1 - i, S - 1 - i, col)
            c.vline(i, i, S - 1 - i, col)
            c.vline(S - 1 - i, i, S - 1 - i, col)

    band(0, 1, o)                             # outer black
    for i in range(2, 12):                    # wood, vertical shading
        t = (i - 2) / 9
        band(i, i, mix(P['wood_hi'], P['wood_bot'], t))
    band(12, 12, P['wood_lo'])
    band(13, 13, gl)                          # gold trim
    band(14, 14, g)
    band(15, 15, gh)
    band(16, 16, gl)
    band(17, 17, o)
    if filled:
        c.rect(18, 18, S - 19, S - 19, P['bg_dark'])
        # subtle inner vignette line
        c.box(18, 18, S - 19, S - 19, P['bg_dark2'])
    else:
        c.rect(18, 18, S - 19, S - 19, NONE)

    # wood plank grain on top/bottom bands
    for x in range(3, S - 3, 6):
        for y in range(3, 11):
            c.set(x, y, mix(c.get(x, y), P['wood_lo'], .45))
            c.set(x, S - 1 - y, mix(c.get(x, S - 1 - y), P['wood_lo'], .45))
    for y in range(3, S - 3, 6):
        for x in range(3, 11):
            c.set(x, y, mix(c.get(x, y), P['wood_lo'], .45))
            c.set(S - 1 - x, y, mix(c.get(S - 1 - x, y), P['wood_lo'], .45))

    # gold filigree in the 4 corners (inside the fixed border area)
    fil = [
        '..ooo....',
        '.oyyyo...',
        'oyy.yyo..',
        'oy...yo..',
        'oy..oyyo.',
        '.oo..oyyo',
        '......oyo',
        '.......oo',
    ]
    pal = {'o': gl, 'y': gh}
    c.draw_map(fil, pal, 4, 4)
    # mirrored copies
    from PIL import Image
    corner = c.im.crop((4, 4, 13, 12))
    c.im.alpha_composite(corner.transpose(Image.FLIP_LEFT_RIGHT), (S - 13, 4))
    c.im.alpha_composite(corner.transpose(Image.FLIP_TOP_BOTTOM), (4, S - 12))
    c.im.alpha_composite(corner.transpose(Image.ROTATE_180), (S - 13, S - 12))
    c.p = c.im.load()
    c.chamfer(0, 0, S - 1, S - 1, 2)
    return c


c = window_frame(True)
c.save(D_FRAME + '/window_frame.png'); reg(D_FRAME + '/window_frame.png', c, (20, 20, 20, 20), 'main window (dark body included)')
c = window_frame(False)
c.save(D_FRAME + '/window_frame_hollow.png'); reg(D_FRAME + '/window_frame_hollow.png', c, (20, 20, 20, 20), 'same frame, transparent center')


# ---------------------------------------------------------------- 2. WOOD PANEL (sidebar)
def wood_panel():
    S = 40
    c = C(S, S)
    o = P['outline']

    def band(i, col):
        c.hline(i, S - 1 - i, i, col); c.hline(i, S - 1 - i, S - 1 - i, col)
        c.vline(i, i, S - 1 - i, col); c.vline(S - 1 - i, i, S - 1 - i, col)

    c.rect(0, 0, S - 1, S - 1, P['wood_bot'])
    band(0, o)
    band(1, P['wood_hi'])
    band(2, P['wood_top'])
    band(3, P['wood_lo'])
    band(4, P['gold_lo'])
    band(5, P['gold'])
    band(6, o)
    c.rect(7, 7, S - 8, S - 8, P['bg_dark2'])
    c.hline(7, S - 8, 7, mix(P['bg_dark'], P['outline'], .6))
    c.chamfer(0, 0, S - 1, S - 1, 2)
    return c


c = wood_panel(); c.save(D_FRAME + '/panel_wood.png')
reg(D_FRAME + '/panel_wood.png', c, (10, 10, 10, 10), 'sidebar / sub panel')


# ---------------------------------------------------------------- 3. PARCHMENT PANEL
def parchment(w=40, h=40, torn=True):
    c = C(w, h)
    c.rect(0, 0, w - 1, h - 1, P['parch_edge'])
    c.vgrad(1, 1, w - 2, h - 2, P['parch_top'], P['parch_bot'])
    c.hline(1, w - 2, 1, P['parch_hi'])
    c.vline(1, 1, h - 2, P['parch_hi'])
    c.hline(1, w - 2, h - 2, P['parch_lo'])
    c.vline(w - 2, 1, h - 2, P['parch_lo'])
    c.box(3, 3, w - 4, h - 4, mix(P['parch_bot'], P['parch_lo'], .5))
    c.noise(4, 4, w - 5, h - 5, mix(P['parch_bot'], P['parch_lo'], .35), .05, seed=7)
    c.noise(4, 4, w - 5, h - 5, P['parch_hi'], .04, seed=11)
    if torn:  # rough scroll edge nibbles on left/right (inside border zone)
        for y in (5, 9, h - 10, h - 6):
            c.set(0, y, NONE); c.set(w - 1, y, NONE)
    c.chamfer(0, 0, w - 1, h - 1, 2)
    return c


c = parchment(); c.save(D_FRAME + '/panel_parchment.png')
reg(D_FRAME + '/panel_parchment.png', c, (8, 8, 8, 8), 'content / list background')


# ---------------------------------------------------------------- 4. HEADER BAR
def header_bar():
    w, h = 24, 20
    c = C(w, h)
    c.rect(0, 0, w - 1, h - 1, P['outline'])
    c.vgrad(1, 1, w - 2, h - 2, P['wood_sel_top'], P['wood_bot'])
    c.hline(1, w - 2, 1, P['wood_sel_hi'])
    c.hline(1, w - 2, h - 3, P['gold_lo'])
    c.hline(1, w - 2, h - 2, P['gold'])
    c.chamfer(0, 0, w - 1, h - 1, 1)
    return c


c = header_bar(); c.save(D_FRAME + '/bar_table_header.png')
reg(D_FRAME + '/bar_table_header.png', c, (6, 6, 6, 6), 'table column header strip')


# ---------------------------------------------------------------- 5. LIST ROW
def list_row(state='normal'):
    w, h = 24, 24
    c = C(w, h)
    o = P['outline']
    if state == 'normal':
        top, bot, hi = hx('#3E2320'), hx('#2C1817'), hx('#5A342A')
    elif state == 'alt':
        top, bot, hi = hx('#472A25'), hx('#33201C'), hx('#653C30')
    elif state == 'hover':
        top, bot, hi = hx('#5A3428'), hx('#3E241D'), hx('#7E4A34')
    else:  # selected
        top, bot, hi = hx('#6E4326'), hx('#4A2B18'), P['gold']
    c.rect(0, 0, w - 1, h - 1, o)
    c.vgrad(1, 1, w - 2, h - 2, top, bot)
    c.hline(1, w - 2, 1, hi)
    c.vline(1, 1, h - 2, mix(hi, top, .4))
    c.hline(1, w - 2, h - 2, hx('#1E100F'))
    if state == 'selected':
        c.box(1, 1, w - 2, h - 2, P['gold_lo'])
        c.hline(2, w - 3, 2, P['gold_hi'])
    c.chamfer(0, 0, w - 1, h - 1, 1)
    return c


for st in ('normal', 'alt', 'hover', 'selected'):
    c = list_row(st); c.save(D_FRAME + '/row_%s.png' % st)
    reg(D_FRAME + '/row_%s.png' % st, c, (6, 6, 6, 6), 'listing row (%s)' % st)


# ---------------------------------------------------------------- 6. SEARCH FIELD / INSET
def inset_field(parch=True):
    w, h = 28, 20
    c = C(w, h)
    o = P['outline']
    c.rect(0, 0, w - 1, h - 1, o)
    if parch:
        c.vgrad(1, 1, w - 2, h - 2, P['parch_bot'], P['parch_top'])
        c.hline(1, w - 2, 1, P['parch_lo'])
        c.vline(1, 1, h - 2, P['parch_lo'])
        c.hline(1, w - 2, h - 2, P['parch_hi'])
        c.box(0, 0, w - 1, h - 1, o)
        # gold outer trim
        c.box(1, 1, w - 2, h - 2, mix(P['parch_lo'], P['gold_lo'], .35))
    else:
        c.vgrad(1, 1, w - 2, h - 2, hx('#1A0E0D'), hx('#2A1715'))
        c.hline(1, w - 2, 1, hx('#100807'))
        c.hline(1, w - 2, h - 2, hx('#4A2A20'))
    c.chamfer(0, 0, w - 1, h - 1, 2)
    return c


c = inset_field(True); c.save(D_FRAME + '/field_search.png')
reg(D_FRAME + '/field_search.png', c, (7, 7, 7, 7), 'search input (parchment inset)')
c = inset_field(False); c.save(D_FRAME + '/inset_dark.png')
reg(D_FRAME + '/inset_dark.png', c, (6, 6, 6, 6), 'generic sunken dark inset')


def gold_bar():
    w, h = 24, 22
    c = C(w, h)
    c.rect(0, 0, w - 1, h - 1, P['outline'])
    c.vgrad(1, 1, w - 2, h - 2, P['parch_top'], P['parch_bot'])
    c.hline(1, w - 2, 1, P['parch_hi'])
    c.hline(1, w - 2, h - 2, P['parch_lo'])
    c.box(1, 1, w - 2, h - 2, P['parch_edge'])
    c.chamfer(0, 0, w - 1, h - 1, 2)
    return c


c = gold_bar(); c.save(D_FRAME + '/badge_currency.png')
reg(D_FRAME + '/badge_currency.png', c, (7, 7, 7, 7), 'gold/currency badge (bottom-left)')


# ---------------------------------------------------------------- 7. BUTTONS
def btn(kind, state):
    w, h = 28, 18
    key = {'blue': ('blue_hi', 'blue_top', 'blue_bot', 'blue_lo'),
           'gold': ('gbtn_hi', 'gbtn_top', 'gbtn_bot', 'gbtn_lo'),
           'red':  ('red_hi', 'red_top', 'red_bot', 'red_lo'),
           'wood': ('wood_hi', 'wood_top', 'wood_bot', 'wood_lo')}[kind]
    hi, top, bot, lo = (P[k] for k in key)
    if state == 'hover':
        top = mix(top, P['white'], .18); bot = mix(bot, P['white'], .14); hi = mix(hi, P['white'], .3)
    if state == 'disabled':
        g = lambda col: mix(col, hx('#6A6A6A'), .78)
        hi, top, bot, lo = g(hi), g(top), g(bot), g(lo)
    c = C(w, h)
    c.rect(0, 0, w - 1, h - 1, P['outline'])
    if state == 'pressed':
        c.vgrad(1, 1, w - 2, h - 2, bot, mix(top, bot, .4))
        c.hline(1, w - 2, 1, lo)
        c.vline(1, 1, h - 2, lo)
        c.hline(1, w - 2, h - 2, mix(hi, bot, .5))
    else:
        c.vgrad(1, 1, w - 2, h - 2, top, bot)
        c.hline(1, w - 2, 1, hi)
        c.vline(1, 1, h - 2, mix(hi, top, .35))
        c.hline(1, w - 2, h - 2, lo)
        c.vline(w - 2, 1, h - 2, mix(lo, bot, .3))
        c.hline(2, w - 3, 2, mix(top, P['white'], .22))   # glossy sheen
    c.chamfer(0, 0, w - 1, h - 1, 2)
    return c


for kind in ('blue', 'gold', 'red', 'wood'):
    for st in ('normal', 'hover', 'pressed', 'disabled'):
        c = btn(kind, st)
        p = D_BTN + '/btn_%s_%s.png' % (kind, st)
        c.save(p); reg(p, c, (8, 6, 8, 6), 'generic button %s/%s' % (kind, st))


def btn_sidebar(state):
    w, h = 32, 22
    c = C(w, h)
    o = P['outline']
    if state == 'selected':
        top, bot, hi, lo = P['wood_sel_top'], P['wood_sel_bot'], P['wood_sel_hi'], P['wood_lo']
    elif state == 'hover':
        top, bot, hi, lo = mix(P['wood_top'], P['white'], .12), mix(P['wood_bot'], P['white'], .08), P['wood_hi'], P['wood_lo']
    else:
        top, bot, hi, lo = P['wood_top'], P['wood_bot'], P['wood_hi'], P['wood_lo']
    c.rect(0, 0, w - 1, h - 1, o)
    c.vgrad(1, 1, w - 2, h - 2, top, bot)
    c.hline(1, w - 2, 1, hi)
    c.vline(1, 1, h - 2, mix(hi, top, .4))
    c.hline(1, w - 2, h - 2, lo)
    c.vline(w - 2, 1, h - 2, lo)
    if state == 'selected':
        c.box(1, 1, w - 2, h - 2, P['gold'])
        c.box(2, 2, w - 3, h - 3, P['gold_lo'])
    else:
        c.box(1, 1, w - 2, h - 2, mix(top, o, .35))
    c.chamfer(0, 0, w - 1, h - 1, 2)
    return c


for st in ('normal', 'hover', 'selected'):
    c = btn_sidebar(st); p = D_BTN + '/btn_category_%s.png' % st
    c.save(p); reg(p, c, (9, 8, 9, 8), 'sidebar category tab (%s)' % st)


def btn_square(kind, state, size=16):
    c = C(size, size)
    o = P['outline']
    sets = {'page': (P['parch_hi'], P['parch_top'], P['parch_bot'], P['parch_lo']),
            'page_sel': (P['gbtn_hi'], P['gbtn_top'], P['gbtn_bot'], P['gbtn_lo'])}
    hi, top, bot, lo = sets[kind]
    if state == 'hover':
        top = mix(top, P['white'], .18); bot = mix(bot, P['white'], .12)
    c.rect(0, 0, size - 1, size - 1, o)
    if state == 'pressed':
        c.vgrad(1, 1, size - 2, size - 2, bot, top)
        c.hline(1, size - 2, 1, lo)
    else:
        c.vgrad(1, 1, size - 2, size - 2, top, bot)
        c.hline(1, size - 2, 1, hi)
        c.hline(1, size - 2, size - 2, lo)
        c.vline(size - 2, 1, size - 2, lo)
        c.vline(1, 1, size - 2, mix(hi, top, .4))
    c.chamfer(0, 0, size - 1, size - 1, 2)
    return c


for kind in ('page', 'page_sel'):
    for st in ('normal', 'hover', 'pressed'):
        c = btn_square(kind, st); p = D_BTN + '/btn_%s_%s.png' % (kind, st)
        c.save(p); reg(p, c, (5, 5, 5, 5), 'pagination square (%s/%s)' % (kind, st))


# close button (X drawn in, fixed size, no 9-slice)
def btn_close(state):
    S = 18
    c = C(S, S)
    o = P['outline']
    hi, top, bot, lo = P['red_hi'], P['red_top'], P['red_bot'], P['red_lo']
    if state == 'hover':
        top = mix(top, P['white'], .2); bot = mix(bot, P['white'], .12); hi = mix(hi, P['white'], .3)
    if state == 'pressed':
        top, bot = bot, mix(top, bot, .4)
    c.rect(0, 0, S - 1, S - 1, o)
    c.vgrad(1, 1, S - 2, S - 2, top, bot)
    c.hline(1, S - 2, 1, hi if state != 'pressed' else lo)
    c.vline(1, 1, S - 2, mix(hi, top, .4))
    c.hline(1, S - 2, S - 2, lo)
    c.vline(S - 2, 1, S - 2, lo)
    c.box(1, 1, S - 2, S - 2, mix(top, o, .3))
    # X glyph
    for i in range(9):
        x = 4 + i
        c.set(x, 4 + i, P['white']); c.set(x, 5 + i, hx('#F3D8D8'))
        c.set(S - 1 - x, 4 + i, P['white']); c.set(S - 1 - x, 5 + i, hx('#F3D8D8'))
    c.chamfer(0, 0, S - 1, S - 1, 2)
    return c


for st in ('normal', 'hover', 'pressed'):
    c = btn_close(st); p = D_BTN + '/btn_close_%s.png' % st
    c.save(p); reg(p, c, None, 'close button with X (%s)' % st)


# ---------------------------------------------------------------- 8. ITEM SLOTS
def item_slot(color_key, w=20, h=20, glow=True):
    col = P[color_key]
    c = C(w, h)
    c.rect(0, 0, w - 1, h - 1, P['outline'])
    inner_top = mix(col, hx('#100808'), .78)
    inner_bot = mix(col, hx('#100808'), .90)
    c.vgrad(2, 2, w - 3, h - 3, inner_top, inner_bot)
    c.box(1, 1, w - 2, h - 2, col)                       # rarity ring
    c.box(2, 2, w - 3, h - 3, mix(col, P['outline'], .55))
    c.hline(2, w - 3, 2, mix(col, P['white'], .25)) if glow else None
    c.chamfer(0, 0, w - 1, h - 1, 2)
    return c


slots = {'common': 'r_common', 'uncommon': 'r_uncommon', 'rare': 'r_rare',
         'epic': 'r_epic', 'legendary': 'r_legend', 'quest': 'r_red'}
for name, key in slots.items():
    c = item_slot(key); p = D_SLOT + '/slot_%s.png' % name
    c.save(p); reg(p, c, (5, 5, 5, 5), 'item slot – %s rarity' % name)

# empty/neutral slot
c = item_slot('r_common', glow=False)
c.tint_all(hx('#000000'), .25)
c.save(D_SLOT + '/slot_empty.png'); reg(D_SLOT + '/slot_empty.png', c, (5, 5, 5, 5), 'empty slot')


def avatar_frame():
    S = 20
    c = C(S, S)
    c.rect(0, 0, S - 1, S - 1, P['outline'])
    c.rect(2, 2, S - 3, S - 3, hx('#25334A'))
    c.box(1, 1, S - 2, S - 2, P['r_uncommon'])
    c.box(2, 2, S - 3, S - 3, mix(P['r_uncommon'], P['outline'], .5))
    c.chamfer(0, 0, S - 1, S - 1, 2)
    return c


c = avatar_frame(); c.save(D_SLOT + '/frame_avatar.png')
reg(D_SLOT + '/frame_avatar.png', c, (5, 5, 5, 5), 'seller portrait frame')


# ---------------------------------------------------------------- 9. ORNAMENTS
def ribbon():
    """Title banner: 3-slice horizontally (fixed height)."""
    w, h = 48, 30
    c = C(w, h)
    o = P['outline']
    # tails
    for i in range(8):
        y0, y1 = 4 + i // 2, h - 5 - i // 2
        c.vline(i, y0, y1, mix(P['wood_bot'], P['wood_lo'], .3))
        c.vline(w - 1 - i, y0, y1, mix(P['wood_bot'], P['wood_lo'], .3))
    # notch cut in tail
    for i in range(5):
        c.vline(0 + i, 12 - i, 17 + i, NONE)
        c.vline(w - 1 - i, 12 - i, 17 + i, NONE)
    # center plate
    c.rect(6, 2, w - 7, h - 3, o)
    c.vgrad(7, 3, w - 8, h - 4, P['wood_sel_top'], P['wood_bot'])
    c.hline(7, w - 8, 3, P['wood_sel_hi'])
    c.hline(7, w - 8, h - 4, P['wood_lo'])
    c.box(8, 5, w - 9, h - 6, P['gold_lo'])
    c.hline(9, w - 10, 6, P['gold_hi'])
    # tail outline
    for i in range(8):
        c.set(i, 4 + i // 2 - 1, o); c.set(i, h - 4 - i // 2, o)
        c.set(w - 1 - i, 4 + i // 2 - 1, o); c.set(w - 1 - i, h - 4 - i // 2, o)
    return c


c = ribbon(); c.save(D_ORN + '/banner_ribbon.png')
reg(D_ORN + '/banner_ribbon.png', c, (14, 6, 14, 6), 'title ribbon (stretch horizontally only)')


def divider():
    w, h = 32, 6
    c = C(w, h)
    c.hline(0, w - 1, 2, P['gold_lo'])
    c.hline(0, w - 1, 3, P['gold'])
    c.hline(0, w - 1, 4, P['gold_deep'])
    # center gem
    cx = w // 2
    gem = ['..o..', '.oyo.', 'oy.yo', '.oyo.', '..o..']
    c.draw_map(gem, {'o': P['gold_lo'], 'y': P['gold_hi']}, cx - 2, 0)
    return c


c = divider(); c.save(D_ORN + '/divider_gold.png')
reg(D_ORN + '/divider_gold.png', c, (8, 0, 8, 0), 'horizontal gold divider')


def gem_diamond():
    S = 12
    c = C(S, S)
    body = [
        '.....kk.....',
        '....kppk....',
        '...kpwppk...',
        '..kpwvppk...',
        '.kpwvvppk...',
        'kpwvvvppk...',
        'kpvvvvpk....',
        '.kpvvpk.....',
        '..kppk......',
        '...kk.......',
    ]
    pal = {'k': P['outline'], 'p': hx('#B15CE8'), 'w': hx('#E9B6FF'), 'v': hx('#6E2A9A')}
    c.draw_map(body, pal, 1, 1)
    return c


c = gem_diamond(); c.save(D_ORN + '/gem_purple.png')
reg(D_ORN + '/gem_purple.png', c, None, 'small decorative gem')


def corner_ornament():
    S = 14
    c = C(S, S)
    fil = [
        '..ooo.....',
        '.oyyyo....',
        'oyy.yyo...',
        'oy...yo...',
        'oy..oyyo..',
        '.oo..oyyo.',
        '......oyo.',
        '.......oo.',
    ]
    c.draw_map(fil, {'o': P['gold_lo'], 'y': P['gold_hi']}, 2, 2)
    return c


c = corner_ornament(); c.save(D_ORN + '/corner_filigree.png')
reg(D_ORN + '/corner_filigree.png', c, None, 'corner decoration (rotate 90/180/270 in Unity)')


def scrollbar_parts():
    # track
    c = C(10, 24)
    c.rect(0, 0, 9, 23, P['outline'])
    c.vgrad(1, 1, 8, 22, hx('#2A1715'), hx('#1A0E0D'))
    c.vline(1, 1, 22, hx('#150B0A'))
    c.chamfer(0, 0, 9, 23, 2)
    c.save(D_ORN + '/scroll_track.png'); reg(D_ORN + '/scroll_track.png', c, (4, 4, 4, 4), 'scrollbar track')
    # handle
    c = C(10, 24)
    c.rect(0, 0, 9, 23, P['outline'])
    c.vgrad(1, 1, 8, 22, P['wood_sel_top'], P['wood_bot'])
    c.vline(1, 1, 22, P['wood_sel_hi'])
    c.hline(1, 8, 1, P['wood_sel_hi'])
    c.vline(8, 1, 22, P['wood_lo'])
    c.box(1, 1, 8, 22, mix(P['gold_lo'], P['wood_top'], .4))
    c.chamfer(0, 0, 9, 23, 2)
    c.save(D_ORN + '/scroll_handle.png'); reg(D_ORN + '/scroll_handle.png', c, (4, 4, 4, 4), 'scrollbar handle')


scrollbar_parts()


def tooltip_panel():
    w, h = 28, 24
    c = C(w, h)
    c.rect(0, 0, w - 1, h - 1, P['outline'])
    c.vgrad(1, 1, w - 2, h - 2, hx('#241419'), hx('#150C10'))
    c.box(1, 1, w - 2, h - 2, P['gold_lo'])
    c.box(2, 2, w - 3, h - 3, hx('#3A2228'))
    c.chamfer(0, 0, w - 1, h - 1, 2)
    return c


c = tooltip_panel(); c.save(D_FRAME + '/panel_tooltip.png')
reg(D_FRAME + '/panel_tooltip.png', c, (7, 7, 7, 7), 'tooltip / dialog panel')

import json
with open('/home/claude/manifest_frames.json', 'w') as f:
    json.dump(MANIFEST, f)
print('frames done:', len(MANIFEST))
