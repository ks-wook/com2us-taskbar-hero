"""Tiny pixel-art drawing toolkit for authoring 9-slice UI sprites."""
from PIL import Image
import random

def hx(s, a=255):
    s = s.lstrip('#')
    return (int(s[0:2], 16), int(s[2:4], 16), int(s[4:6], 16), a)

def mix(c1, c2, t):
    return tuple(int(round(c1[i] + (c2[i] - c1[i]) * t)) for i in range(4))

NONE = (0, 0, 0, 0)


class C:
    def __init__(self, w, h):
        self.w, self.h = w, h
        self.im = Image.new('RGBA', (w, h), NONE)
        self.p = self.im.load()

    def set(self, x, y, c):
        if 0 <= x < self.w and 0 <= y < self.h and c is not None:
            self.p[x, y] = c

    def get(self, x, y):
        return self.p[x, y]

    def rect(self, x0, y0, x1, y1, c):
        """inclusive filled rect"""
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                self.set(x, y, c)

    def box(self, x0, y0, x1, y1, c):
        """1px outline rect (inclusive)"""
        for x in range(x0, x1 + 1):
            self.set(x, y0, c); self.set(x, y1, c)
        for y in range(y0, y1 + 1):
            self.set(x0, y, c); self.set(x1, y, c)

    def hline(self, x0, x1, y, c):
        for x in range(x0, x1 + 1):
            self.set(x, y, c)

    def vline(self, x, y0, y1, c):
        for y in range(y0, y1 + 1):
            self.set(x, y, c)

    def vgrad(self, x0, y0, x1, y1, c_top, c_bot):
        span = max(1, y1 - y0)
        for y in range(y0, y1 + 1):
            c = mix(c_top, c_bot, (y - y0) / span)
            self.hline(x0, x1, y, c)

    def chamfer(self, x0, y0, x1, y1, n=1):
        """knock out corner pixels to fake rounding"""
        for i in range(n):
            for j in range(n - i):
                self.set(x0 + i, y0 + j, NONE)
                self.set(x1 - i, y0 + j, NONE)
                self.set(x0 + i, y1 - j, NONE)
                self.set(x1 - i, y1 - j, NONE)

    def noise(self, x0, y0, x1, y1, c, chance=0.08, seed=1):
        r = random.Random(seed)
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                if self.p[x, y][3] and r.random() < chance:
                    self.set(x, y, c)

    def tint_all(self, c, t):
        for y in range(self.h):
            for x in range(self.w):
                px = self.p[x, y]
                if px[3]:
                    self.p[x, y] = mix(px, c, t)[:3] + (px[3],)

    def shift_down(self, n=1):
        """for pressed states: move content down n px"""
        out = Image.new('RGBA', (self.w, self.h), NONE)
        out.paste(self.im, (0, n))
        self.im = out
        self.p = self.im.load()

    def draw_map(self, rows, pal, ox=0, oy=0):
        for y, row in enumerate(rows):
            for x, ch in enumerate(row):
                if ch != '.' and ch in pal:
                    self.set(ox + x, oy + y, pal[ch])

    def save(self, path, scale=1):
        im = self.im
        if scale != 1:
            im = im.resize((self.w * scale, self.h * scale), Image.NEAREST)
        im.save(path)
        return path


def bevel_panel(w, h, top, bot, hi, lo, outline, cham=2, inner_outline=None):
    """Standard chunky UI panel: outline + top/left highlight + bottom/right shadow."""
    c = C(w, h)
    c.rect(0, 0, w - 1, h - 1, outline)
    c.vgrad(1, 1, w - 2, h - 2, top, bot)
    c.hline(1, w - 2, 1, hi)                  # top highlight
    c.vline(1, 1, h - 2, hi)                  # left highlight
    c.hline(1, w - 2, h - 2, lo)              # bottom shadow
    c.vline(w - 2, 1, h - 2, lo)              # right shadow
    if inner_outline:
        c.box(2, 2, w - 3, h - 3, inner_outline)
    c.chamfer(0, 0, w - 1, h - 1, cham)
    return c


def inset_panel(w, h, top, bot, hi, lo, outline, cham=2):
    """Recessed / sunken panel (highlight on bottom-right)."""
    return bevel_panel(w, h, top, bot, lo, hi, outline, cham)


def nine_slice_draw(dst, src_im, border, x, y, w, h):
    """Render a 9-sliced sprite into a PIL image (for preview composition)."""
    l, t, r, b = border
    sw, sh = src_im.size
    parts = {
        'tl': (0, 0, l, t), 'tc': (l, 0, sw - r, t), 'tr': (sw - r, 0, sw, t),
        'ml': (0, t, l, sh - b), 'mc': (l, t, sw - r, sh - b), 'mr': (sw - r, t, sw, sh - b),
        'bl': (0, sh - b, l, sh), 'bc': (l, sh - b, sw - r, sh), 'br': (sw - r, sh - b, sw, sh),
    }
    cw, ch = w - l - r, h - t - b

    def put(key, px, py, pw, ph):
        if pw <= 0 or ph <= 0:
            return
        piece = src_im.crop(parts[key]).resize((pw, ph), Image.NEAREST)
        dst.alpha_composite(piece, (px, py))

    put('tl', x, y, l, t); put('tc', x + l, y, cw, t); put('tr', x + w - r, y, r, t)
    put('ml', x, y + t, l, ch); put('mc', x + l, y + t, cw, ch); put('mr', x + w - r, y + t, r, ch)
    put('bl', x, y + h - b, l, b); put('bc', x + l, y + h - b, cw, b); put('br', x + w - r, y + h - b, r, b)
