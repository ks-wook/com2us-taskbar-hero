import os, json
from pixkit import C, hx, mix, NONE
from palette import P

OUT = '/mnt/user-data/outputs/PixelUI_AuctionKit'
D_ICON = OUT + '/04_Icons'
D_ORN = OUT + '/05_Ornaments'
os.makedirs(D_ICON, exist_ok=True)
MANIFEST = []

PAL = {
    'k': hx('#140A0C'),               # outline
    'w': hx('#FFFFFF'),
    's': hx('#DCE4EC'), 't': hx('#A6B3C0'), 'u': hx('#68788A'),   # steel
    'y': hx('#FFE08A'), 'o': hx('#E0A32C'), 'b': hx('#8A5A18'),   # gold
    'n': hx('#A8683C'), 'c': hx('#7A4326'), 'h': hx('#4C2717'),   # wood/leather
    'r': hx('#F0625A'), 'e': hx('#C2352F'), 'x': hx('#7A1B1C'),   # red
    'g': hx('#9AA3AD'), 'd': hx('#5D6670'),                        # gray stone
    'p': hx('#C77BF0'), 'q': hx('#8E39C4'),                        # purple
    'z': hx('#7FCBFF'), 'i': hx('#2C82D6'), 'j': hx('#17497F'),   # blue
    'f': hx('#FFC64A'), 'a': hx('#F0782A'),                        # flame
    'l': hx('#BFE9FF'),                                            # gem light
}

ICONS = {}

ICONS['sword'] = [
    '............kk..',
    '...........ksk..',
    '..........kwstk.',
    '.........kwstk..',
    '........kwstk...',
    '.......kwstk....',
    '......kwstk.....',
    '.....kwstk......',
    '....kwstk.......',
    '...kkstkk.......',
    '..kokkkok.......',
    '.kooookoook.....',
    '..kokkkok.......',
    '....kok.........',
    '...koyok........',
    '....kkk.........',
]

ICONS['shield'] = [
    '..kkkkkkkkkk....',
    '.kwsssssttuk....',
    'kwssssssttuuk...',
    'kwssssssttuuk...',
    'kwsskkkkttuuk...',
    'kwsskssktttuk...',
    '.kwskssktttuk...',
    '.kwsskkkkttuk...',
    '..kwsssttuuk....',
    '...kwssttuk.....',
    '....kwsttk......',
    '.....kwuk.......',
    '......kk........',
]

ICONS['potion'] = [
    '.....kkkk.......',
    '.....kcck.......',
    '....kkkkkk......',
    '....kwsstk......',
    '...kkwsstkk.....',
    '..kwrrrrrrtk....',
    '.kwrrrrrrrrtk...',
    '.kwrrrrrrrrtk...',
    'kwrrrrrrrrrrtk..',
    'kwrreeeeeerrtk..',
    'kwreeeeeeeertk..',
    'kwreeeeeeeertk..',
    '.kweeeeeeeetk...',
    '.kwxeeeeeextk...',
    '..kkxxxxxxkk....',
    '....kkkkkk......',
]

ICONS['ore'] = [
    '.......kkkk.....',
    '......kwggdk....',
    '.....kwgggddk...',
    '....kwggggddk...',
    '...kwgggggddk...',
    '...kggggggddk...',
    '..kkgggggddk....',
    '.kwgkkkkkkk.....',
    'kwgggggdk.......',
    'kwggggggdk......',
    'kgggggggdk......',
    'kgdddddddk......',
    '.kkkkkkkk.......',
]

ICONS['bag'] = [
    '......kk.k......',
    '.....kck.ck.....',
    '.....kckckk.....',
    '....kkcccckk....',
    '...kcnnnnnnck...',
    '..kcnnnnnnnnck..',
    '.kcnnnnnnnnnnck.',
    'kcnnnnnnnnnnnnck',
    'kcnnnnnnnnnnnnck',
    'kcnnnkyoknnnnck.',
    'kcnnnkyoknnnck..',
    '.kcnnnkkknnck...',
    '..kccnnnnnckk...',
    '...kkccccckk....',
    '.....kkkkk......',
]

ICONS['gavel'] = [
    '.........kkkkkk.',
    '........kwssstk.',
    '........kwssstk.',
    '.......kkkkkkkk.',
    '......kwsstk....',
    '.....kwsstk.....',
    '....kcnnck......',
    '...kcnnck.......',
    '..kcnnck........',
    '..kcnck.........',
    '..kkk...........',
    '................',
    '.kkkkkkkkkkk....',
    'kcnnnnnnnnnck...',
    'kchhhhhhhhhck...',
    '.kkkkkkkkkkk....',
]

ICONS['grid'] = [
    'kkkkkkkkkkkkkk..',
    'kyyykyyykyyyk...',
    'kyyykyyykyyyk...',
    'kyyykyyykyyyk...',
    'kkkkkkkkkkkkk...',
    'kyyykyyykyyyk...',
    'kyyykyyykyyyk...',
    'kyyykyyykyyyk...',
    'kkkkkkkkkkkkk...',
    'kyyykyyykyyyk...',
    'kyyykyyykyyyk...',
    'kyyykyyykyyyk...',
    'kkkkkkkkkkkkk...',
]

ICONS['magnifier'] = [
    '....kkkkk.......',
    '..kkzzzzzkk.....',
    '.kzzwwwwzzzk....',
    '.kzwwlllwzzk....',
    'kzzwllllwzzzk...',
    'kzzwllllwzzzk...',
    'kzzwllllwzzzk...',
    '.kzzwllwzzzk....',
    '.kzzzwwzzzzk....',
    '..kkzzzzzkkk....',
    '....kkkkkjk.....',
    '.......kjjjk....',
    '........kjjjk...',
    '.........kjjjk..',
    '..........kjjk..',
    '...........kk...',
]

ICONS['coin'] = [
    '.....kkkk.......',
    '...kkooookk.....',
    '..koyyyyyyok....',
    '.koyywyyyyyok...',
    '.koywyyoyyyok...',
    'koyyyyoyyyyook..',
    'koyyyyoyyyyook..',
    'koyyyoooyyyook..',
    'koyyyyoyyyyook..',
    '.koyyyoyyyook...',
    '.koyyyoyyyook...',
    '..koyyyyyook....',
    '...kkooookk.....',
    '.....kkkk.......',
]

ICONS['coin_stack'] = [
    '................',
    '....kkkkkk......',
    '..kkoyyyyokk....',
    '.koyyyyyyyyok...',
    '.kooyyyyyyook...',
    '..kkooooookk....',
    '.kkoyyyyyokk....',
    'koyyyyyyyyyok...',
    'kooyyyyyyyook...',
    '.kkooooooookk...',
    'kkoyyyyyyyokk...',
    'koyyyyyyyyyok...',
    'kooyyyyyyyook...',
    '.kkoooooooookk..',
    '..kkkkkkkkkk....',
]

ICONS['clock'] = [
    '.....kkkk.......',
    '...kkttttkk.....',
    '..ktsssssstk....',
    '.ktssswssssstk..',
    '.kssswkwssssk...',
    'ktsssskwsssstk..',
    'ktssswkkwssstk..',
    'ktsssskwwssstk..',
    'ktssssskssssstk.',
    '.ksssssssssssk..',
    '.ktssssssssstk..',
    '..ktssssssstk...',
    '...kkttttttk....',
    '.....kkkkk......',
]

ICONS['chevron_left'] = [
    '.........kk.....',
    '........kwk.....',
    '.......kwyk.....',
    '......kwyyk.....',
    '.....kwyyk......',
    '....kwyyk.......',
    '...kwyyk........',
    '..kwyyk.........',
    '...kwyyk........',
    '....kwyyk.......',
    '.....kwyyk......',
    '......kwyyk.....',
    '.......kwyk.....',
    '........kwk.....',
    '.........kk.....',
]

ICONS['chevron_down'] = [
    '................',
    'kk...........kk.',
    'kwk.........kwk.',
    'kywk.......kwyk.',
    '.kywk.....kwyk..',
    '..kywk...kwyk...',
    '...kywk.kwyk....',
    '....kywkwyk.....',
    '.....kywyk......',
    '......kyk.......',
    '.......k........',
]

ICONS['plus'] = [
    '......kkk.......',
    '.....kyyyk......',
    '.....kyyyk......',
    '.....kyyyk......',
    'kkkkkkyyykkkkkk.',
    'kyyyyyyyyyyyyyk.',
    'kyyyyyyyyyyyyyk.',
    'kkkkkkyyykkkkkk.',
    '.....kyyyk......',
    '.....kyyyk......',
    '.....kyyyk......',
    '......kkk.......',
]

ICONS['helm'] = [
    '....kkkkkkk.....',
    '..kkwssssstkk...',
    '.kwssssssssstk..',
    'kwsssssssssstk..',
    'kwsstttttttsstk.',
    'kwstkkkkkkktstk.',
    'kwstkuuuuuktstk.',
    'kwsttkkkkkttstk.',
    'kwssttttttttstk.',
    'kwsstkkkkktsstk.',
    'kwssstuuuutssstk',
    '.kwsstkkkktsstk.',
    '..kwssttttsstk..',
    '...kkwssssskk...',
    '.....kkkkkkk....',
]

ICONS['ring'] = [
    '.........kkk....',
    '........klllk...',
    '.......kllwlk...',
    '.....kkkllllk...',
    '...kksssklkk....',
    '..kstttssk......',
    '.kstkkkstsk.....',
    '.kstk..kstsk....',
    'kstk....kstk....',
    'kstk....kstk....',
    'kstk...kstk.....',
    '.kstk.kstk......',
    '.kstttstk.......',
    '..kssssk........',
    '...kkkk.........',
]

ICONS['bow'] = [
    '......kkk.......',
    '.....kqpk.k.....',
    '....kqpk..wk....',
    '...kqpk...wk....',
    '...kqk....wk....',
    '..kqpk....wk....',
    '..kqpk....wk....',
    '..kqpk....wk....',
    '..kqpk....wk....',
    '..kqpk....wk....',
    '...kqk....wk....',
    '...kqpk...wk....',
    '....kqpk..wk....',
    '.....kqpk.k.....',
    '......kkk.......',
]

ICONS['arrow_projectile'] = [
    '............kk..',
    '...........kwk..',
    '..........kwyk..',
    '.........kwyk...',
    '.......kkkyk....',
    '......knnk......',
    '.....knnk.......',
    '....knnk........',
    '...knnk.........',
    '..knnk..........',
    '.kwnk...........',
    'kwwk............',
    'kwk.............',
    '.kk.............',
]

ICONS['flamesword'] = [
    '............kk..',
    '...........kfk..',
    '..........kfak..',
    '.........kfak...',
    '........kfak....',
    '.......kfak.....',
    '......kfak......',
    '.....kfak.......',
    '....kfak........',
    '...kkakk........',
    '..kokkkok.......',
    '.kooookoook.....',
    '..kokkkok.......',
    '....kok.........',
    '...koyok........',
    '....kkk.........',
]


def build(name, rows, size=16):
    c = C(size, size)
    # center the map
    h = len(rows)
    w = max(len(r) for r in rows)
    ox = (size - w) // 2
    oy = (size - h) // 2
    c.draw_map(rows, PAL, max(0, ox), max(0, oy))
    return c


for name, rows in ICONS.items():
    c = build(name, rows)
    p = D_ICON + '/icon_%s.png' % name
    c.save(p)
    MANIFEST.append((os.path.relpath(p, OUT), c.w, c.h, None, 'icon'))

# mirrored chevron_right
from PIL import Image
im = Image.open(D_ICON + '/icon_chevron_left.png').transpose(Image.FLIP_LEFT_RIGHT)
im.save(D_ICON + '/icon_chevron_right.png')
MANIFEST.append(('04_Icons/icon_chevron_right.png', 16, 16, None, 'icon'))
im = Image.open(D_ICON + '/icon_chevron_down.png').transpose(Image.FLIP_TOP_BOTTOM)
im.save(D_ICON + '/icon_chevron_up.png')
MANIFEST.append(('04_Icons/icon_chevron_up.png', 16, 16, None, 'icon'))


# ------------------------------------------------ improved banner (3 pieces)
def banner_center():
    w, h = 24, 26
    c = C(w, h)
    o = P['outline']
    c.rect(0, 2, w - 1, h - 3, o)
    c.vgrad(1, 3, w - 2, h - 4, P['wood_sel_top'], P['wood_bot'])
    c.hline(1, w - 2, 3, P['wood_sel_hi'])
    c.hline(1, w - 2, h - 4, P['wood_lo'])
    c.hline(0, w - 1, 5, P['gold_lo'])
    c.hline(0, w - 1, 6, P['gold_hi'])
    c.hline(0, w - 1, h - 7, P['gold_hi'])
    c.hline(0, w - 1, h - 6, P['gold_lo'])
    return c


def banner_tail(flip=False):
    w, h = 14, 26
    c = C(w, h)
    o = P['outline']
    for x in range(w):
        top = 5 + x // 3
        bot = h - 6 - x // 3
        c.vline(x, top, bot, mix(P['wood_top'], P['wood_bot'], .45))
        c.set(x, top, o); c.set(x, bot, o)
        c.set(x, top + 1, P['wood_hi'])
        c.set(x, bot - 1, P['wood_lo'])
        # gold stripes following the fold
        c.set(x, top + 3, P['gold_lo'])
        c.set(x, bot - 3, P['gold_lo'])
    # V notch on the outer edge
    for i in range(5):
        c.vline(i, 11 - i, 14 + i, NONE)
    for i in range(5):
        c.set(i, 10 - i, o); c.set(i, 15 + i, o)
    c.vline(w - 1, 5 + (w - 1) // 3, h - 6 - (w - 1) // 3, o)
    if flip:
        c.im = c.im.transpose(Image.FLIP_LEFT_RIGHT); c.p = c.im.load()
    return c


c = banner_center(); c.save(D_ORN + '/banner_center.png')
MANIFEST.append(('05_Ornaments/banner_center.png', c.w, c.h, (6, 0, 6, 0), 'banner center – stretch X only'))
c = banner_tail(True); c.save(D_ORN + '/banner_tail_left.png')
MANIFEST.append(('05_Ornaments/banner_tail_left.png', c.w, c.h, None, 'banner left tail'))
c = banner_tail(False); c.save(D_ORN + '/banner_tail_right.png')
MANIFEST.append(('05_Ornaments/banner_tail_right.png', c.w, c.h, None, 'banner right tail'))

with open('/home/claude/manifest_icons.json', 'w') as f:
    json.dump(MANIFEST, f)
print('icons done:', len(MANIFEST))
