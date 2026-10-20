"""简单脚本化闪避规划器 (向量化): 对每个动作做粗略线性外推 T 帧, 选最小间隙最大的动作。
用途: baseline / 行为克隆预热。只是启发式, 不追求最优。"""
import numpy as np
from .sim import decode_actions, N_ACTIONS, PHH, V_UP, V_DOWN, V_HOVER, X_DASH, X_BETSY, X_HOOK, input_dir
from .constants import *


def plan(sim, horizon=(4, 10, 18)):
    idx, gap, qx, qy = sim._cache
    N = sim.N
    h, v, x = decode_actions(np.arange(N_ACTIONS))
    score = np.zeros((N, N_ACTIONS), np.float32)
    P = sim.proj
    e = idx // sim.cap
    dmg = P.dmg[idx] | (P.t[idx] < 3)
    bdx, bdy = input_dir(h, v, 1)
    for T in horizon:
        vx = np.clip(sim.pvx[:, None] * (h[None] != 0) + h[None] * 0.75 * T, -ACC_RUN_AIR, ACC_RUN_AIR)
        vy = np.where(v[None] == V_UP, -np.minimum(2.3 * T, 12.9) / 2,
                      np.where(v[None] == V_HOVER, 0.0, np.minimum(sim.pvy[:, None] + GRAVITY * T / 2, MAX_FALL)))
        fx = sim.px[:, None] + vx * T
        fy = sim.py[:, None] + vy * T
        fy = np.where((v[None] != V_DOWN) & sim.on_ground[:, None], np.minimum(fy, sim.py[:, None]), fy)
        bet = (x[None] == X_BETSY) & (sim.betsy_cd[:, None] == 0)
        fx = np.where(bet, sim.px[:, None] + bdx[None] * 25 * min(T, 15), fx)
        fy = np.where(bet, sim.py[:, None] + bdy[None] * 25 * min(T, 15), fy)
        mind = np.full((N, N_ACTIONS), 3000.0, np.float32)
        if idx.size:
            hx = qx + P.vx[idx] * T; hy = qy + P.vy[idx] * T
            dd = np.hypot(fx[e] - hx[:, None], fy[e] - hy[:, None]) - (P.lw[idx] / 2 + np.minimum(P.hw[idx], 140))[:, None] - 12
            dd = np.where(dmg[:, None], dd, 3000)
            np.minimum.at(mind, e, dd)
        mind = np.where(bet & (T <= 15), 300, mind)
        ax, ay, sc = sim.aura[:, 0], sim.aura[:, 1], sim.aura[:, 2]
        m1 = np.where((sim.blai[:, 3] == 1)[:, None], ARENA_AURA_RADIUS * sc[:, None] - np.hypot(fx - ax[:, None], fy - ay[:, None]), 3000)
        rx, ry = sim.ritual[:, 0], sim.ritual[:, 1]
        m2 = np.where((sim.ritual[:, 2] > 0)[:, None], RITUAL_RADIUS - 30 - np.hypot(fx - rx[:, None], fy - ry[:, None]), 3000)
        marg = np.minimum(m1, m2) - 150
        score += np.minimum(np.minimum(mind, 300), marg) / T
    score -= 5.0 * (x == X_BETSY)[None] + 1.0 * (x == X_HOOK)[None]   # 节约 Betsy CD
    return np.argmax(score + 1e-3 * np.random.rand(N, N_ACTIONS), axis=1)
