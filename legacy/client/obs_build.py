"""把游戏发来的原始局面收成和模拟器相同的 758 维观测。只在本机运行。"""
import numpy as np

OBS_DIM = 758
BASE_DIM = 86
K_HAZ = 32
HAZ_F = 21
N_GROUPS = 7
HEADER = 40
STRIDE = 10
ARENA_HALF_W = 3200.0
AURA_R = 2000.0
RITUAL_HIT = 1140.0
HURT = 10.0
PILLAR = 24
RAYS = {6, 18, 21, 22, 23}

# 与 sim/projectiles.py 的 KIND_GROUP 一致。下标是 kind id。
KIND_GROUP = np.zeros(40, np.int64)
for kind, group in (
    (1, 3), (2, 1), (3, 1), (4, 1), (5, 1), (6, 2), (7, 4), (8, 0), (9, 0),
    (10, 3), (11, 3), (12, 3), (13, 3), (14, 5), (15, 5), (16, 5), (17, 1),
    (18, 2), (19, 0), (20, 1), (21, 2), (22, 2), (23, 2), (24, 3), (25, 0),
    (26, 3), (27, 0), (28, 3), (29, 3), (30, 3), (31, 5), (32, 6), (33, 1),
    (34, 0), (35, 4), (36, 4), (37, 0), (38, 3), (39, 3),
):
    KIND_GROUP[kind] = group


def state_index(ai0):
    a = int(ai0)
    if a >= 0:
        return 47 if a == 52 else min(max(a, 0), 46)
    return min(max(47 - a, 48), 54)


def _aabb(px, py, x, y, hw, hh):
    gx = max(abs(x - px) - hw - HURT, 0.0)
    gy = max(abs(y - py) - hh - HURT, 0.0)
    gap = -1.0 if gx == 0.0 and gy == 0.0 else float(np.hypot(gx, gy))
    qx = min(max(px, x - hw), x + hw)
    qy = min(max(py, y - hh), y + hh)
    return gap, qx, qy


def _ray(px, py, x, y, vx, vy, thick):
    speed = float(np.hypot(vx, vy))
    if speed < 1e-3:
        return _aabb(px, py, x, y, 24.0, 24.0)
    dx, dy = vx / speed, vy / speed
    length = 2200.0
    ax, ay = x, y
    bx, by = x + dx * length, y + dy * length
    abx, aby = bx - ax, by - ay
    l2 = abx * abx + aby * aby
    t = 0.0 if l2 < 1e-6 else min(1.0, max(0.0, ((px - ax) * abx + (py - ay) * aby) / l2))
    qx, qy = ax + t * abx, ay + t * aby
    dist = float(np.hypot(px - qx, py - qy)) - HURT - thick / 2
    gap = -1.0 if dist <= 0 else dist
    return gap, qx, qy


def build_obs(packet):
    p = np.asarray(packet, np.float32)
    o = np.zeros(OBS_DIM, np.float32)
    px, py, pvx, pvy = float(p[0]), float(p[1]), float(p[2]), float(p[3])
    dash_cd, dashing, immune, flying = float(p[4]), float(p[5]), float(p[6]), float(p[7])
    betsy_t, betsy_cd, face = float(p[8]), float(p[9]), float(p[10])
    boss = float(p[11]) > 0.5
    bx, by, bvx, bvy = float(p[12]), float(p[13]), float(p[14]), float(p[15])
    ai0, ai1, ai2, ai3 = float(p[16]), float(p[17]), float(p[18]), float(p[19])
    lai2, lai3 = float(p[22]), float(p[23])
    life, life_max = float(p[24]), max(float(p[25]), 1.0)
    aura_on = float(p[26]) > 0.5
    ax, ay, scale = float(p[27]), float(p[28]), float(p[29]) if float(p[29]) > 0 else 1.0
    ritual_on = float(p[30]) > 0.5
    rx, ry = float(p[31]), float(p[32])
    n = int(p[33]) if p.size > 33 else 0
    tick = float(p[34]) if p.size > 34 else 0.0

    margin = 3000.0
    if aura_on:
        margin = AURA_R * scale - float(np.hypot(px - ax, py - ay))
    if ritual_on:
        m2 = RITUAL_HIT - float(np.hypot(px - rx, py - ry))
        margin = min(margin, m2)

    o[0] = px / ARENA_HALF_W
    o[1] = py / 1000
    o[2] = pvx / 18
    o[3] = pvy / 13
    o[4] = dash_cd / 20
    o[5] = 1 if dashing > 0.5 else 0
    o[6] = min(margin, 3000) / 1000
    o[7] = 1 if immune > 0 else 0
    o[8] = 1 if flying > 0.5 else 0
    o[9] = betsy_t / 15
    o[10] = betsy_cd / 300
    o[11] = face
    if aura_on:
        o[12] = (ax - px) / 1000
        o[13] = (ay - py) / 1000
    if boss:
        o[14] = (bx - px) / 1000
        o[15] = (by - py) / 1000
        o[16] = bvx / 30
        o[17] = bvy / 30
        o[18 + state_index(ai0)] = 1
        o[73] = min(max(ai1, -10), 1200) / 240
        o[74] = min(max(ai2, -10), 1200) / 100
        o[75] = min(max(ai3, -10), 600) / 300
        o[76] = lai2 / 2
    o[77] = min(tick / 20000, 2)
    phase = 2 if ai0 < 0 else (1 if ritual_on else 0)
    o[78 + phase] = 1
    o[81] = life / life_max if boss else 0
    if ritual_on:
        o[82] = (rx - px) / 1000
        o[83] = (ry - py) / 1000
        o[84] = min(margin, 3000) / 1000
    o[85] = 1 if lai3 >= 2 else 0

    n = max(0, min(n, (p.size - HEADER) // STRIDE))
    if n:
        rows = []
        base = HEADER
        for i in range(n):
            s = base + i * STRIDE
            x, y, vx, vy = float(p[s]), float(p[s + 1]), float(p[s + 2]), float(p[s + 3])
            hw, hh = float(p[s + 4]), float(p[s + 5])
            dmg = 1.0 if p[s + 6] > 0.5 else 0.0
            kind = int(p[s + 7])
            age = float(p[s + 8])
            lw = float(p[s + 9])
            dx = dy = 0.0
            if kind in RAYS:
                gap, qx, qy = _ray(px, py, x, y, vx, vy, max(lw, 20.0))
                seg = 1.0
                speed = float(np.hypot(vx, vy))
                if speed > 1e-3:
                    dx, dy = vx / speed, vy / speed
            else:
                gap, qx, qy = _aabb(px, py, x, y, hw, hh)
                seg = 0.0
            score = gap + 200.0 * (1.0 - dmg)
            if kind == PILLAR:
                score -= 1.0e6
            rows.append((score, gap, qx, qy, vx, vy, hw, hh, dmg, age, kind, seg, lw, dx, dy))
        rows.sort(key=lambda r: r[0])
        for rank, row in enumerate(rows[:K_HAZ]):
            _, gap, qx, qy, vx, vy, hw, hh, dmg, age, kind, seg, lw, dx, dy = row
            f = BASE_DIM + rank * HAZ_F
            o[f] = 1
            o[f + 1] = (qx - px) / 1000
            o[f + 2] = (qy - py) / 1000
            o[f + 3] = min(max(gap, -1), 3000) / 1000
            o[f + 4] = vx / 30
            o[f + 5] = vy / 30
            o[f + 6] = min(hw, 1000) / 200
            o[f + 7] = min(hh, 1000) / 200
            o[f + 8] = dx
            o[f + 9] = dy
            o[f + 10] = seg
            o[f + 11] = min(lw, 300) / 50
            o[f + 12] = dmg
            o[f + 13] = min(age, 120) / 120
            group = KIND_GROUP[kind] if 0 <= kind < len(KIND_GROUP) else 0
            o[f + 14 + group] = 1
    return o
