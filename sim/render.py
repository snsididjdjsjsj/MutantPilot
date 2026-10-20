"""纯 numpy 光栅化 + ffmpeg 管道写 MP4 (不依赖 matplotlib/pygame)。"""
import subprocess
import numpy as np
from . import projectiles as K
from .constants import *

W, H, SCALE = 960, 540, 0.4      # 视野 2400x1350 世界像素
GROUP_COLORS = np.array([[255, 80, 80], [255, 170, 40], [80, 220, 255], [120, 255, 120], [120, 120, 160], [200, 120, 255], [60, 200, 200]], np.uint8)


class Canvas:
    def __init__(self, cx, cy):
        self.img = np.zeros((H, W, 3), np.uint8); self.img[:] = (12, 12, 24)
        self.cx, self.cy = cx, cy

    def w2s(self, x, y):
        return (x - self.cx) * SCALE + W / 2, (y - self.cy) * SCALE + H / 2

    def rect(self, x, y, hw, hh, col, fill=True):
        sx, sy = self.w2s(x, y)
        x0, x1 = int(max(sx - hw * SCALE, 0)), int(min(sx + hw * SCALE + 1, W))
        y0, y1 = int(max(sy - hh * SCALE, 0)), int(min(sy + hh * SCALE + 1, H))
        if x0 < x1 and y0 < y1:
            if fill:
                self.img[y0:y1, x0:x1] = col
            else:
                self.img[y0, x0:x1] = col; self.img[y1 - 1, x0:x1] = col
                self.img[y0:y1, x0] = col; self.img[y0:y1, x1 - 1] = col

    def line(self, ax, ay, bx, by, width, col):
        (sx0, sy0), (sx1, sy1) = self.w2s(ax, ay), self.w2s(bx, by)
        n = int(max(abs(sx1 - sx0), abs(sy1 - sy0), 1))
        n = min(n, 4000)
        t = np.linspace(0, 1, n + 1)
        xs, ys = sx0 + (sx1 - sx0) * t, sy0 + (sy1 - sy0) * t
        r = max(int(width * SCALE / 2), 0)
        for ox in range(-r, r + 1):
            for oy in range(-r, r + 1):
                X, Y = (xs + ox).astype(int), (ys + oy).astype(int)
                m = (X >= 0) & (X < W) & (Y >= 0) & (Y < H)
                self.img[Y[m], X[m]] = col

    def circle(self, x, y, r, col, fill=False):
        sx, sy = self.w2s(x, y)
        R = r * SCALE
        x0, x1 = int(max(sx - R, 0)), int(min(sx + R + 1, W)); y0, y1 = int(max(sy - R, 0)), int(min(sy + R + 1, H))
        if x0 >= x1 or y0 >= y1:
            return
        yy, xx = np.mgrid[y0:y1, x0:x1]
        d = np.hypot(xx - sx, yy - sy)
        m = d <= R if fill else np.abs(d - R) < 1.5
        self.img[y0:y1, x0:x1][m] = col


def render_frame(sim, e=0, hit=False):
    c = Canvas(float(sim.px[e]), float(sim.py[e]) - 150)
    ax, ay, sc = sim.aura[e]
    if sim.blai[e, 3] == 1:
        c.circle(ax, ay, ARENA_AURA_RADIUS * sc, (180, 60, 60))
    if sim.ritual[e, 2] > 0:
        c.circle(sim.ritual[e, 0], sim.ritual[e, 1], RITUAL_RADIUS, (220, 180, 60))
    for r in PLATFORM_ROWS:
        c.line(-PLATFORM_HALF_LEN, r + 4, PLATFORM_HALF_LEN, r + 4, 8, (110, 90, 60))
    if sim.hook_s[e] > 0:
        c.line(sim.px[e], sim.py[e], sim.hx[e], sim.hy[e], 2, (200, 200, 200))
    P = sim.proj
    sl = slice(e * sim.cap, (e + 1) * sim.cap)
    for j in np.flatnonzero(P.active[sl]):
        i = e * sim.cap + j
        col = GROUP_COLORS[K.KIND_GROUP[P.kind[i]]]
        if not P.dmg[i]:
            col = (col * 0.4).astype(np.uint8)
        x, y = P.x[i], P.y[i]
        if P.col[i] == K.C_RAY:
            L = P.llen[i]
            c.line(x, y, x + P.dx[i] * L, y + P.dy[i] * L, max(P.lw[i], 3), col)
        elif P.col[i] == K.C_SEG:
            L = P.llen[i] / 2
            c.line(x - P.dx[i] * L, y - P.dy[i] * L, x + P.dx[i] * L, y + P.dy[i] * L, max(P.lw[i], 3), col)
        elif P.col[i] == K.C_INVCIRC:
            c.circle(x, y, P.r[i], (120, 255, 255) if P.dmg[i] else col)
            c.circle(x, y, P.llen[i], col)
        elif P.col[i] == K.C_CIRCLE:
            c.circle(x, y, P.r[i], col, fill=P.dmg[i])
        else:
            c.rect(x, y, P.hw[i], P.hh[i], col, fill=P.col[i] != K.C_NONE)
    c.rect(sim.bx[e], sim.by[e], BOSS_W / 2, BOSS_W / 2, (90, 255, 170), fill=False)
    pc = (255, 60, 60) if hit else (255, 140, 0) if sim.betsy_t[e] > 0 else (255, 255, 255)
    c.rect(sim.px[e], sim.py[e], PLAYER_W / 2, PLAYER_H / 2, pc, fill=False)
    c.rect(sim.px[e], sim.py[e], HURT_W / 2, HURT_H / 2, pc)
    # HUD: 攻击 id 条 + 进度条
    a = int(sim.bai[e, 0])
    if a >= 0:
        c.img[5:15, 5:5 + min(8 * (a + 1), W - 10)] = (90, 255, 170)
    else:
        c.img[5:15, 5:5 + 40 * -a] = (255, 90, 90)
    c.img[20:25, 5:5 + int(300 * max(sim.life[e], 0) / BOSS_LIFEMAX_EXPERT)] = (255, 255, 0)
    for k in range(int(sim.reached[e])):
        c.img[30:38, 5 + 12 * k:13 + 12 * k] = (200, 200, 255)
    return c.img


class VideoWriter:
    def __init__(self, path, fps=60):
        self.p = subprocess.Popen(['ffmpeg', '-y', '-loglevel', 'error', '-f', 'rawvideo', '-pix_fmt', 'rgb24',
                                   '-s', f'{W}x{H}', '-r', str(fps), '-i', '-', '-c:v', 'libx264', '-pix_fmt', 'yuv420p', path],
                                  stdin=subprocess.PIPE)

    def write(self, img):
        self.p.stdin.write(img.tobytes())

    def close(self):
        self.p.stdin.close(); self.p.wait()
