"""Gymnasium 接口。
- MutantVecEnv: 批量环境 (训练用, 自动 reset), 观测 [N, OBS_DIM] float32
- MutantGymEnv: 标准 gymnasium.Env 单环境包装 (调试 / SB3 兼容)
观测布局 (与 bridge mod 导出保持一致, 见 docs/bridge_design.md):
  player(22) aura(2) boss(4+55+4) time(1) extra(8: 阶段/血量/ritual/presence) hazards(K_HAZ * HAZ_F)
"""
import numpy as np
import gymnasium as gym
from gymnasium import spaces
from .sim import MutantSim, N_ACTIONS, PHW, PHH, ROWS
from . import projectiles as K
from .constants import *

K_HAZ = 32
HAZ_F = 14 + K.N_GROUPS
# boss 状态 one-hot: ai0 0..46 -> 0..46, 52 -> 47, -1..-7 -> 48..54
N_BOSS_STATES = 55
N_PLAYER = 22
N_EXTRA = 3 + 1 + 3 + 1          # phase one-hot, life 比例, ritual 相对位置(2)+边界余量, MutantPresence
BASE_DIM = N_PLAYER + 2 + (4 + N_BOSS_STATES + 4) + 1 + N_EXTRA
OBS_DIM = BASE_DIM + K_HAZ * HAZ_F


def state_index(a0):
    a = a0.astype(np.int64)
    return np.where(a >= 0, np.where(a == 52, 47, np.clip(a, 0, 46)), np.clip(47 - a, 48, 54))


def build_obs(sim: MutantSim):
    N, P = sim.N, sim.cap
    o = np.zeros((N, OBS_DIM), np.float32)
    ax, ay = sim.aura[:, 0], sim.aura[:, 1]
    margin = sim.arena_margin()
    o[:, 0] = sim.px / PLATFORM_HALF_LEN; o[:, 1] = (sim.py - PLATFORM_Y) / 1000
    o[:, 2] = sim.pvx / 18; o[:, 3] = sim.pvy / 13
    o[:, 4] = sim.on_ground; o[:, 5] = sim.jump_t / 15; o[:, 6] = sim.dash_cd / 20; o[:, 7] = sim.dashing
    o[:, 8] = np.minimum(margin, 3000) / 1000; o[:, 9] = sim.iframes > 0
    o[:, 10] = sim.flying; o[:, 11] = sim.betsy_t / 15; o[:, 12] = sim.betsy_cd / 300
    o[:, 13] = sim.hook_s == 1; o[:, 14] = sim.hook_s == 2
    o[:, 15] = np.where(sim.hook_s > 0, (sim.hx - sim.px) / 550, 0); o[:, 16] = np.where(sim.hook_s > 0, (sim.hy - sim.py) / 550, 0)
    feet = sim.py + PHH
    d = ROWS[None, :] - feet[:, None]                  # 各层平台相对脚底的高度
    above = np.where(d < -1, d, -np.inf).max(1); below = np.where(d >= -1, d, np.inf).min(1)
    o[:, 17] = np.clip(np.nan_to_num(above, neginf=-2000), -2000, 0) / 1000
    o[:, 18] = np.clip(np.nan_to_num(below, posinf=2000), 0, 2000) / 1000
    o[:, 19] = (PLATFORM_HALF_LEN - np.abs(sim.px)) / PLATFORM_HALF_LEN
    o[:, 20] = sim.face; o[:, 21] = 0
    p1 = sim.blai[:, 3] == 1
    o[:, 22] = np.where(p1, (ax - sim.px) / 1000, 0); o[:, 23] = np.where(p1, (ay - sim.py) / 1000, 0)
    c = N_PLAYER + 2
    o[:, c] = (sim.bx - sim.px) / 1000; o[:, c + 1] = (sim.by - sim.py) / 1000
    o[:, c + 2] = sim.bvx / 30; o[:, c + 3] = sim.bvy / 30
    o[np.arange(N), c + 4 + state_index(sim.bai[:, 0])] = 1
    c2 = c + 4 + N_BOSS_STATES
    o[:, c2] = np.clip(sim.bai[:, 1], -10, 1200) / 240; o[:, c2 + 1] = np.clip(sim.bai[:, 2], -10, 1200) / 100
    o[:, c2 + 2] = np.clip(sim.bai[:, 3], -10, 600) / 300; o[:, c2 + 3] = sim.blai[:, 2] / 2
    o[:, c2 + 4] = np.minimum(sim.tick / 20000, 2)
    c3 = c2 + 5
    ph = np.where(sim.bai[:, 0] < 0, 2, np.where(sim.ritual[:, 2] > 0, 1, 0))
    o[np.arange(N), c3 + ph] = 1
    o[:, c3 + 3] = sim.life / BOSS_LIFEMAX_EXPERT
    on = sim.ritual[:, 2] > 0
    o[:, c3 + 4] = np.where(on, (sim.ritual[:, 0] - sim.px) / 1000, 0)
    o[:, c3 + 5] = np.where(on, (sim.ritual[:, 1] - sim.py) / 1000, 0)
    o[:, c3 + 6] = np.where(on, np.minimum(margin, 3000) / 1000, 0)
    o[:, c3 + 7] = sim.blai[:, 3] >= 2                  # MutantPresence (P2+)
    # hazards: 稀疏 top-K (按 env 分组 lexsort, 不再构造 [N, cap] 稠密矩阵)
    idx, gap, qx, qy = sim._cache
    if idx.size:
        pr = sim.proj
        e = idx // P
        score = gap + 200.0 * (~pr.dmg[idx])            # 无伤弹幕 (预警) 排在后面
        order = np.lexsort((score, e))
        es = e[order]
        first = np.searchsorted(es, np.arange(N))
        rank = np.arange(es.size) - first[es]
        keep = rank < K_HAZ
        sel, es, rk = order[keep], es[keep], rank[keep]
        fl = idx[sel]
        f = np.zeros((N, K_HAZ, HAZ_F), np.float32)
        v = f[es, rk]
        v[:, 0] = 1
        v[:, 1] = (qx[sel] - sim.px[es]) / 1000
        v[:, 2] = (qy[sel] - sim.py[es]) / 1000
        v[:, 3] = np.clip(gap[sel], -1, 3000) / 1000
        v[:, 4] = pr.vx[fl] / 30; v[:, 5] = pr.vy[fl] / 30
        v[:, 6] = np.minimum(pr.hw[fl], 1000) / 200; v[:, 7] = np.minimum(pr.hh[fl], 1000) / 200
        v[:, 8] = pr.dx[fl]; v[:, 9] = pr.dy[fl]
        col = pr.col[fl]
        v[:, 10] = (col == K.C_SEG) | (col == K.C_RAY); v[:, 11] = np.minimum(pr.lw[fl], 300) / 50
        v[:, 12] = pr.dmg[fl]; v[:, 13] = np.minimum(pr.t[fl], 120) / 120
        v[np.arange(fl.size), 14 + K.KIND_GROUP[pr.kind[fl]]] = 1
        f[es, rk] = v
        o[:, BASE_DIM:] = f.reshape(N, -1)
    return o


class MutantVecEnv:
    """批量环境, 自动 reset。step 返回 obs, reward, terminated, truncated, info(dict of arrays)。
    task_fn(e) -> dict(id, phase, forced, cycles, reach, max_ticks) 在每个 env reset 前调用 (课程用)。"""

    def __init__(self, n_envs=64, frame_skip=1, seed=0, r_alive=0.02, r_hit=-10.0, r_win=10.0,
                 r_edge=-0.02, edge_margin=250.0, task_fn=None, **sim_kw):
        self.sim = MutantSim(n_envs, seed=seed, **sim_kw)
        self.N, self.frame_skip = n_envs, frame_skip
        self.r_alive, self.r_hit, self.r_win, self.r_edge, self.edge_margin = r_alive, r_hit, r_win, r_edge, edge_margin
        self.observation_space = spaces.Box(-np.inf, np.inf, (OBS_DIM,), np.float32)
        self.action_space = spaces.Discrete(N_ACTIONS)
        self.ep_ret = np.zeros(n_envs, np.float32)
        self.ep_len = np.zeros(n_envs, np.int32)
        self.task_id = np.zeros(n_envs, np.int32)
        self.task_fn = task_fn

    def _assign(self, mask):
        if self.task_fn is None:
            return
        for e in np.flatnonzero(mask):
            t = self.task_fn(int(e))
            self.task_id[e] = t['id']
            self.sim.set_task(e, t.get('phase', 1), t.get('forced'), t.get('cycles', 2), t.get('reach', 99),
                              t.get('max_ticks', 40000))

    def set_sampler(self, make):
        self.task_fn = make(0)

    def set_forced_attack(self, a):
        self.sim.forced_attack = a
        return self.reset()

    def reset(self):
        self._assign(np.ones(self.N, bool))
        self.sim.reset_all()
        self.sim._collide()
        self.ep_ret[:] = 0; self.ep_len[:] = 0
        return build_obs(self.sim)

    def step(self, actions):
        s = self.sim
        rew = np.zeros(self.N, np.float32)
        hit_any = np.zeros(self.N, bool)
        for _ in range(self.frame_skip):
            hit = s.step(actions)
            hit_any |= hit
            margin = s.arena_margin()
            rew += self.r_alive + self.r_edge * (margin < self.edge_margin) + self.r_hit * hit
            if s.no_hit and hit_any.all():
                break
        win = s.success & ~hit_any
        rew += self.r_win * win
        term = (hit_any & s.no_hit) | win
        trunc = (s.tick >= s.t_max) & ~term
        self.ep_ret += rew; self.ep_len += 1
        done = term | trunc
        info = {}
        if done.any():
            info = dict(done=done.copy(), ep_ret=self.ep_ret.copy(), ep_len=self.ep_len.copy(), win=win.copy(),
                        hit=hit_any.copy(), ticks=s.tick.copy(), attack=s.bai[:, 0].copy(), task=self.task_id.copy(),
                        reached=s.reached.copy(), hit_kind=s.hit_kind.copy())
            self._assign(done)
            s.reset(done)
            s._collide()
            self.ep_ret[done] = 0; self.ep_len[done] = 0
        obs = build_obs(s)
        return obs, rew, term, trunc, info


class MutantGymEnv(gym.Env):
    metadata = {"render_modes": ["rgb_array"]}

    def __init__(self, **kw):
        self.v = MutantVecEnv(n_envs=1, **kw)
        self.observation_space = self.v.observation_space
        self.action_space = self.v.action_space

    def reset(self, seed=None, options=None):
        super().reset(seed=seed)
        return self.v.reset()[0], {}

    def step(self, action):
        o, r, te, tr, info = self.v.step(np.array([action]))
        return o[0], float(r[0]), bool(te[0]), bool(tr[0]), {k: v[0] for k, v in info.items()}

    def render(self):
        from .render import render_frame
        return render_frame(self.v.sim, 0)
