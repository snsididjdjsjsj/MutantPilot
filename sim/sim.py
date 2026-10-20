"""MutantSim: 向量化的 Mutant(一阶段) 无伤闪避模拟器。
    sim = MutantSim(n_envs=256)
    sim.reset_all()
    hit = sim.step(actions)    # actions: int array [N], 0..N_ACTIONS-1
"""
import math
import numpy as np
from . import projectiles as K
from .projectiles import ProjPool
from .boss import Boss, P1_ATTACKS
from .constants import *

# 动作 = 水平{左,无,右} x 垂直{无, 上(跳+W 飞行), 下(下落/穿平台), 悬停(下+跳)} x 技能{无, 冲刺(Tabi), Betsy冲刺, 钩爪}
# a = h + 3*v + 12*x  (h,v,x 为索引) -> 48 个离散动作
N_H, N_V, N_X = 3, 4, 4
N_ACTIONS = N_H * N_V * N_X
V_NONE, V_UP, V_DOWN, V_HOVER = 0, 1, 2, 3
X_NONE, X_DASH, X_BETSY, X_HOOK = 0, 1, 2, 3
PHW, PHH = PLAYER_W / 2, PLAYER_H / 2      # 移动/平台用
HHW, HHH = HURT_W / 2, HURT_H / 2          # 受伤判定 (Precision Seal)
ROWS = np.array(PLATFORM_ROWS, np.float32)
MB_FRAMES = 7        # [GUESS] MoonLordSunBlast 帧数 (决定存活 2*帧数 帧)
BOMB_TL = 15         # [GUESS] MutantBombSmall: LunarFlare 帧数(5)*3
BOLT_UPDATES = 2     # [GUESS] PhantasmalBolt extraUpdates+1
BOLT_TL = 300        # [GUESS]


def decode_actions(a):
    a = np.asarray(a)
    h = (a % 3) - 1
    v = (a // 3) % 4
    x = a // 12
    return h, v, x


def input_dir(h, v, face, default_up=False):
    dy = np.where(v == V_UP, -1.0, np.where(v == V_DOWN, 1.0, 0.0))
    dx = h.astype(np.float32)
    if default_up:
        dy = np.where((dx == 0) & (dy == 0), -1.0, np.where(dy == 0, -1.0, dy))
    else:
        dx = np.where((dx == 0) & (dy == 0), face, dx)
    n = np.hypot(dx, dy) + 1e-9
    return dx / n, dy / n


def seg_box_gap(ax, ay, bx, by, cx, cy, hx, hy, iters=3):
    """线段 a-b 与 AABB(中心 c, 半宽 h) 的最小距离; 返回 (dist, qx, qy) q 为线段上最近点。"""
    ex, ey = bx - ax, by - ay
    L2 = ex * ex + ey * ey + 1e-9
    px, py = cx, cy
    for _ in range(iters):
        t = np.clip(((px - ax) * ex + (py - ay) * ey) / L2, 0, 1)
        qx, qy = ax + t * ex, ay + t * ey
        px = np.clip(qx, cx - hx, cx + hx)
        py = np.clip(qy, cy - hy, cy + hy)
    t = np.clip(((px - ax) * ex + (py - ay) * ey) / L2, 0, 1)
    qx, qy = ax + t * ex, ay + t * ey
    return np.hypot(qx - px, qy - py), qx, qy


class MutantSim:
    """任务 (每个 env 独立, 用于课程):
       phase   起始阶段 1/2/3
       forced  None 或攻击入口 ai0 (只循环该攻击, 不掉血/不换阶段)
       goal_cycles  forced 时完成 n 次攻击即成功
       goal_reach   到达阶段 reached>=goal_reach 即成功 (2=进入P2, 3=进入P3, 4=熬过 FinalSpark)
       max_ticks    超时截断 (不算失败)
    """

    def __init__(self, n_envs=64, cap=1024, seed=0, maso=False, forced_attack=None, random_start=0.0,
                 player_dps=PLAYER_DPS, no_hit=True, max_ticks=None, phase=1, goal_cycles=2):
        self.N, self.cap = n_envs, cap
        self.rng = np.random.default_rng(seed)
        self.maso = maso
        self.random_start = random_start         # 完整模式 (phase 1/2) 随机起始攻击的概率
        self.no_hit = no_hit
        self.player_dps = player_dps
        self.p1_ticks = int(BOSS_LIFEMAX_EXPERT * (1 - PHASE2_FRAC) / player_dps * 60)
        self.proj = ProjPool(n_envs, cap, self.rng)
        self.spawn_q = []
        N = n_envs
        z = lambda dt=np.float32: np.zeros(N, dt)
        self.px, self.py, self.pvx, self.pvy = z(), z(), z(), z()
        self.on_ground, self.jump_t, self.dash_cd, self.dash_t, self.iframes = z(bool), z(np.int32), z(np.int32), z(np.int32), z(np.int32)
        self.face = np.ones(N, np.int32)
        self.flying, self.dashing = z(bool), z(bool)
        self.betsy_t, self.betsy_cd = z(np.int32), z(np.int32)
        self.bdx, self.bdy = z(), z()
        self.hook_s = z(np.int32)          # 0 无, 1 发射中, 2 已勾住
        self.hx, self.hy, self.hdx, self.hdy = z(), z(), z(), z()
        self.tick, self.hits = z(np.int32), z(np.int32)
        self.bx, self.by, self.bvx, self.bvy = z(), z(), z(), z()
        self.bai = np.zeros((N, 4), np.float32)
        self.blai = np.zeros((N, 4), np.float32)
        self.aura = np.zeros((N, 3), np.float32)
        self.ritual = np.zeros((N, 3), np.float32)     # x, y, on
        self.ill = np.zeros((N, 4, 2), np.float32)     # PillarDunk 分身
        self.life = z(); self.reached = z(np.int32); self.attacks_done = z(np.int32)
        self.success = z(bool)
        # tasks
        self.t_phase = np.full(N, phase, np.int32)
        self.t_forced = [forced_attack] * N
        self.t_cycles = np.full(N, goal_cycles, np.int32)
        self.t_reach = np.full(N, 4 if forced_attack is None else 99, np.int32)
        default_mt = max_ticks or (self.p1_ticks if (forced_attack is None and phase == 1) else 40000)
        if forced_attack is None and phase == 1 and max_ticks is None:
            self.t_reach[:] = 2                         # 旧行为: 一阶段打完即胜利
        self.t_max = np.full(N, default_mt, np.int32)
        self.bosses = [Boss(self, e, int(self.rng.integers(1 << 30))) for e in range(N)]
        self.last_gap = None
        self.hit_kind = np.zeros(n_envs, np.int32)

    @property
    def forced_attack(self):
        return self.t_forced[0]

    @forced_attack.setter
    def forced_attack(self, a):
        self.t_forced = [a] * self.N
        self.t_reach[:] = 99 if a is not None else 4

    @property
    def max_ticks(self):
        return self.t_max

    def set_task(self, e, phase=1, forced=None, goal_cycles=2, goal_reach=99, max_ticks=40000):
        self.t_phase[e] = phase; self.t_forced[e] = forced
        self.t_cycles[e] = goal_cycles; self.t_reach[e] = goal_reach; self.t_max[e] = max_ticks

    # ------------------------------------------------------------------ reset
    def reset(self, mask):
        mask = np.asarray(mask, bool)
        self.proj.clear(mask)
        idx = np.flatnonzero(mask)
        n = idx.size
        self.spawn_q = [q for q in self.spawn_q if not mask[q[0]]]
        self.px[idx] = self.rng.uniform(-300, 300, n)
        self.py[idx] = PLATFORM_Y - PHH
        self.pvx[idx] = 0; self.pvy[idx] = 0
        self.on_ground[idx] = True
        self.jump_t[idx] = 0; self.dash_cd[idx] = 0; self.dash_t[idx] = 0; self.iframes[idx] = 0
        self.flying[idx] = False; self.dashing[idx] = False; self.betsy_t[idx] = 0; self.betsy_cd[idx] = 0
        self.hook_s[idx] = 0
        self.tick[idx] = 0; self.hits[idx] = 0; self.success[idx] = False
        for e in idx:
            b = self.bosses[e]
            f, ph = self.t_forced[e], int(self.t_phase[e])
            start = 0
            if f is not None:
                start = f
            elif ph == 1 and self.rng.random() < self.random_start:
                start = int(self.rng.choice(P1_ATTACKS))
            b.reset(float(self.px[e]), float(self.py[e]), start, f, ph)
        self._sync_boss(idx)

    def reset_all(self):
        self.reset(np.ones(self.N, bool))

    def _sync_boss(self, idx=None):
        it = range(self.N) if idx is None else idx
        for e in it:
            b = self.bosses[e]
            self.bx[e], self.by[e], self.bvx[e], self.bvy[e] = b.x, b.y, b.vx, b.vy
            self.bai[e] = b.ai
            self.blai[e] = b.lai
            self.aura[e] = (b.aura_x, b.aura_y, b.aura_scale)
            self.ritual[e] = (b.rx, b.ry, 1.0 if b.ritual else 0.0)
            if b.n_ill:
                self.ill[e] = b.ill
            else:
                self.ill[e, 0] = (b.x, b.y)
            self.life[e] = b.life; self.reached[e] = b.reached; self.attacks_done[e] = b.attacks_done

    @property
    def blai2(self):
        return self.blai[:, 2]

    def _flush_spawns(self):
        q, self.spawn_q = self.spawn_q, []
        if not q:
            return
        groups = {}
        for item in q:
            key = (item[1], tuple(sorted(item[6])))
            groups.setdefault(key, []).append(item)
        for (kind, keys), items in groups.items():
            cols = {k: [] for k in ('x', 'y', 'vx', 'vy') + keys}
            envs = []
            for (e, _, x, y, vx, vy, kw) in items:
                vals = dict(x=x, y=y, vx=vx, vy=vy, **kw)
                n = max(np.size(v) for v in vals.values())
                envs.append(np.full(n, e, np.int64))
                for k in cols:
                    cols[k].append(np.broadcast_to(np.asarray(vals[k], np.float64), (n,)))
            c = {k: np.concatenate(v) for k, v in cols.items()}
            self.proj.spawn(np.concatenate(envs), kind, c.pop('x'), c.pop('y'), c.pop('vx'), c.pop('vy'), **c)

    # ------------------------------------------------------------------ player
    def _player_step(self, actions):
        """标准 Fargo 终局配装的玩家物理 (docs/loadout.md §3)。"""
        h, v, x = decode_actions(actions)
        vx, vy = self.pvx.copy(), self.pvy.copy()
        px, py = self.px.copy(), self.py.copy()
        ground = self.on_ground
        self.face = np.where(h != 0, h, self.face).astype(np.int32)
        self.dash_cd = np.maximum(self.dash_cd - 1, 0)
        self.betsy_cd = np.maximum(self.betsy_cd - 1, 0)

        # ---- Betsy 冲刺 (Masochist Soul): 25px/帧 x15 帧, 无敌, CD 300
        start_b = (x == X_BETSY) & (self.betsy_cd == 0) & (self.betsy_t == 0) & BETSY_DASH
        dx, dy = input_dir(h, v, self.face)
        self.bdx = np.where(start_b, dx, self.bdx); self.bdy = np.where(start_b, dy, self.bdy)
        self.betsy_t = np.where(start_b, BETSY_TICKS, self.betsy_t)
        self.betsy_cd = np.where(start_b, BETSY_CD, self.betsy_cd)
        self.hook_s = np.where(start_b, 0, self.hook_s)   # RemoveAllGrapplingHooks
        betsy = self.betsy_t > 0

        # ---- 钩爪
        if HOOK:
            fire = (x == X_HOOK) & ~betsy
            hdx, hdy = input_dir(h, v, self.face, default_up=True)
            self.hook_s = np.where(fire, 1, self.hook_s)
            self.hx = np.where(fire, px, self.hx); self.hy = np.where(fire, py, self.hy)
            self.hdx = np.where(fire, hdx, self.hdx); self.hdy = np.where(fire, hdy, self.hdy)
            sh = self.hook_s == 1
            ox, oy = self.hx, self.hy
            nhx, nhy = ox + self.hdx * HOOK_SHOOT_SPEED, oy + self.hdy * HOOK_SHOOT_SPEED
            latched = np.zeros(self.N, bool); lx = nhx.copy(); ly = nhy.copy()
            for r in ROWS:   # 平台可被钩住
                cross = sh & ~latched & (np.minimum(oy, nhy) <= r) & (np.maximum(oy, nhy) >= r)
                t = np.clip((r - oy) / np.where(nhy != oy, nhy - oy, 1), 0, 1)
                cx = ox + (nhx - ox) * t
                cross &= np.abs(cx) <= PLATFORM_HALF_LEN
                lx = np.where(cross, cx, lx); ly = np.where(cross, r, ly); latched |= cross
            self.hx = np.where(sh, lx, self.hx); self.hy = np.where(sh, ly, self.hy)
            too_far = sh & ~latched & (np.hypot(nhx - px, nhy - py) > HOOK_REACH)
            self.hook_s = np.where(latched, 2, np.where(too_far, 0, self.hook_s))
            # 跳跃 释放钩爪 [VAN]
            self.hook_s = np.where((self.hook_s == 2) & (v == V_UP) & (x != X_HOOK), 0, self.hook_s)
        hooked = self.hook_s == 2

        # ---- Tabi 冲刺 (dashType 1)
        start_d = (x == X_DASH) & (self.dash_cd == 0) & ~self.dashing & ~betsy & ~hooked
        vx = np.where(start_d, DASH_SPEED * self.face, vx)
        self.dashing |= start_d
        accrun = np.where(ground, ACC_RUN_GROUND, ACC_RUN_AIR)
        dash_dec = self.dashing & ~start_d
        vx = np.where(dash_dec & (np.abs(vx) > 12), vx * DASH_DECAY_FAST, vx)
        end_d = dash_dec & (np.abs(vx) <= np.maximum(accrun, MAX_RUN))
        self.dashing &= ~end_d
        self.dash_cd = np.where(end_d, DASH_DELAY, self.dash_cd)

        # ---- 水平 (Supersonic + NoMomentum + Flight Mastery 翅膀)
        ra = np.where(ground, RUN_ACCEL_BASE * NOMOMENTUM_MULT, RUN_ACCEL_AIR)
        rs = RUN_SLOWDOWN_BASE * NOMOMENTUM_MULT + np.where(h * np.sign(vx) > 0, 0.0, NOMOMENTUM_EXTRA_SLOW)
        hv = h * vx                         # 沿输入方向的速度
        below = hv < MAX_RUN
        turn = np.where(hv < -rs, rs, 0.0)
        mag = np.where(below, ra + turn, np.where(hv < accrun, ra * OVER_MAXRUN_ACCEL_MULT, 0.0))
        vx_in = vx + h * mag
        vx_in = np.where(h * vx_in > accrun, h * np.maximum(np.abs(vx_in) - rs, accrun), vx_in)
        vx_free = np.sign(vx) * np.maximum(np.abs(vx) - rs, 0)
        vx_new = np.where(h != 0, vx_in, vx_free)
        vx = np.where(self.dashing, vx, vx_new)

        # ---- 竖直
        up, down, hover = v == V_UP, v == V_DOWN, v == V_HOVER
        start_jump = up & ground
        self.jump_t = np.where(start_jump, JUMP_HEIGHT, np.where(up, np.maximum(self.jump_t - 1, 0), 0))
        jumping = up & (self.jump_t > 0)
        vy_j = -JUMP_SPEED
        fly = up & ~jumping & ~ground
        m = WING_UP_MULT
        vy_f = vy - WING_CONST_ASCEND * m
        vy_f = np.where(vy_f > 0, vy_f - WING_ASC_FALLING * m,
                        np.where(vy_f > -JUMP_SPEED * WING_MAX_CAN_ASCEND, vy_f - WING_ASC_RISING * m, vy_f))
        if WING_GRAVITY_WHILE_FLYING:
            vy_f = vy_f + GRAVITY
        vy_f = np.maximum(vy_f, -JUMP_SPEED * WING_MAX_ASCENT)
        vy_g = np.minimum(vy + GRAVITY, MAX_FALL)
        vy = np.where(jumping, vy_j, np.where(fly, vy_f, np.where(hover & ~ground, 0.0, vy_g)))
        vy = np.where(hover & ground, 0.0, vy)
        self.flying = fly | (hover & ~ground)

        # ---- 钩爪拉拽 (覆盖其它运动)
        if HOOK:
            ddx, ddy = self.hx - px, self.hy - py
            dist = np.hypot(ddx, ddy) + 1e-9
            pull = np.minimum(HOOK_PULL_SPEED, dist)
            vx = np.where(hooked, ddx / dist * pull, vx)
            vy = np.where(hooked, ddy / dist * pull, vy)

        # ---- Betsy 冲刺覆盖
        vx = np.where(betsy, self.bdx * BETSY_SPEED * 0.5, vx)
        vy = np.where(betsy, self.bdy * BETSY_SPEED * 0.5, vy)
        stepx = np.where(betsy, self.bdx * BETSY_SPEED, vx)
        stepy = np.where(betsy, self.bdy * BETSY_SPEED, vy)
        self.betsy_t = np.maximum(self.betsy_t - 1, 0)

        # ---- 积分 + 多层平台
        nx, ny = px + stepx, py + stepy
        b_old, b_new = py + PHH, ny + PHH
        land = np.zeros(self.N, bool); ly = ny.copy()
        for r in ROWS:
            l = (~down) & (stepy >= 0) & (b_old <= r + 1e-3) & (b_new >= r) & (np.abs(nx) <= PLATFORM_HALF_LEN + PHW) & ~betsy
            ly = np.where(l & ~land, r - PHH, ly); land |= l
        ny = np.where(land, ly, ny)
        vy = np.where(land, 0, vy)
        self.on_ground = land
        self.px, self.py = nx.astype(np.float32), ny.astype(np.float32)
        self.pvx, self.pvy = vx.astype(np.float32), vy.astype(np.float32)

    # ------------------------------------------------------------------ main step
    def step(self, actions):
        self._player_step(actions)
        self._betsy_immune = (self.betsy_t > 0) | ((decode_actions(actions)[2] == X_BETSY) & (self.betsy_cd == BETSY_CD))
        for e in range(self.N):
            self.bosses[e].step()
        self._flush_spawns()
        self._sync_boss()
        self._proj_step()
        hit = self._collide() & ~self._betsy_immune   # BetsyDashing -> ImmuneToDamage
        # P1 光环 (localAI3==1 时, MB.cs:480) / P2+ MutantRitual (半径 1200, 圈外被拉回且受伤: 近似为 d>1170 即命中)
        ax, ay, sc = self.aura[:, 0], self.aura[:, 1], self.aura[:, 2]
        out = (self.blai[:, 3] == 1) & (np.hypot(self.px - ax, self.py - ay) > ARENA_AURA_RADIUS * sc)
        rout = (self.ritual[:, 2] > 0) & (self.bai[:, 0] > -6) & \
            (np.hypot(self.px - self.ritual[:, 0], self.py - self.ritual[:, 1]) > RITUAL_RADIUS - 30)
        out |= rout
        self.hit_kind[out & ~hit] = -1   # -1 = 离开竞技场
        hit |= out
        hit &= self.iframes == 0
        self.iframes = np.where(hit, IFRAMES, np.maximum(self.iframes - 1, 0))
        self.hits += hit
        self.tick += 1
        forced = np.array([f is not None for f in self.t_forced])
        self.success = (forced & (self.attacks_done >= self.t_cycles)) | (self.reached >= self.t_reach)
        return hit

    def arena_margin(self):
        """到当前竞技场边界的距离 (正 = 在内部)。"""
        ax, ay, sc = self.aura[:, 0], self.aura[:, 1], self.aura[:, 2]
        m1 = ARENA_AURA_RADIUS * sc - np.hypot(self.px - ax, self.py - ay)
        m2 = RITUAL_RADIUS - 30 - np.hypot(self.px - self.ritual[:, 0], self.py - self.ritual[:, 1])
        m = np.where(self.blai[:, 3] == 1, m1, 3000.0)
        return np.where(self.ritual[:, 2] > 0, np.minimum(m, m2), m)

    # ------------------------------------------------------------------ projectiles
    def _proj_step(self):
        P = self.proj
        idx = np.flatnonzero(P.active)
        if idx.size == 0:
            return
        P.ox[idx] = P.x[idx]; P.oy[idx] = P.y[idx]
        kinds = P.kind[idx]
        env = idx // self.cap
        groups = {k: (idx[kinds == k], env[kinds == k]) for k in np.unique(kinds)}
        ppx, ppy, pvx, pvy = self.px, self.py, self.pvx, self.pvy
        spawns = []   # (env, kind, x, y, vx, vy, kw)
        kill = np.zeros(0, np.int64)
        kills = []

        def g(k):
            return groups.get(k, (np.zeros(0, np.int64), np.zeros(0, np.int64)))

        # --- SPEAR_THROWN (extraUpdates=1 -> 每帧 2 次更新)  MutantSpearThrown.cs
        i, e = g(K.SPEAR_THROWN)
        if i.size:
            for u in range(2):   # 第 2 次更新的移动/计时由下方统一处理
                P.l0[i] -= 1
                sp = P.l0[i] < 0
                P.l0[i] = np.where(sp, 4, P.l0[i])
                s = sp & (P.a1[i] == 0)
                if s.any():
                    spawns.append((e[s], K.SPHERE_SMALL, P.x[i][s], P.y[i][s], 0.0, 0.0,
                                   dict(tl=120, hw=23, hh=23, a0=0.0, a1=0.0)))
                if u == 0:
                    P.x[i] += P.vx[i]; P.y[i] += P.vy[i]
                    P.tl[i] -= 1
            sp = np.hypot(P.vx[i], P.vy[i]) + 1e-9
            P.dx[i] = P.vx[i] / sp; P.dy[i] = P.vy[i] / sp

        # --- SPHERE_SMALL  MutantSphereSmall.cs (ai0>=0 时 20 帧后以 1/20 插值追踪, 目标速度 5)
        i, e = g(K.SPHERE_SMALL)
        if i.size:
            hm = P.a0[i] >= 0
            P.a1[i] += hm
            go = hm & (P.a1[i] > 20)
            dx, dy = ppx[e] - P.x[i], ppy[e] - P.y[i]
            d = np.hypot(dx, dy) + 1e-9
            P.vx[i] = np.where(go, P.vx[i] + (dx / d * 5 - P.vx[i]) / 20, P.vx[i])
            P.vy[i] = np.where(go, P.vy[i] + (dy / d * 5 - P.vy[i]) / 20, P.vy[i])

        # --- SPHERE_RING  MutantSphereRing.cs:238
        i, e = g(K.SPHERE_RING)
        if i.size:
            P.l0[i] += 1
            ang = P.a1[i] / (2 * np.pi * P.a0[i] * P.l0[i])
            sp = np.hypot(P.vx[i], P.vy[i]) + 1e-9
            nx, ny = P.vx[i] / sp, P.vy[i] / sp
            c, s_ = np.cos(ang), np.sin(ang)
            P.vx[i] = P.a2[i] * (nx * c - ny * s_)
            P.vy[i] = P.a2[i] * (nx * s_ + ny * c)

        # --- MARK1  MutantMark1.cs (出生时生成 DeathraySmall 预警, 死亡时生成 MutantDeathray1)
        i, e = g(K.MARK1)
        if i.size:
            f = P.t[i] == 0
            if f.any():
                sp = np.hypot(P.vx[i], P.vy[i]) + 1e-9
                ii = i[f]
                spawns.append((e[f], K.TELEGRAPH, P.x[ii] + P.vx[ii] * P.tl[ii], P.y[ii] + P.vy[ii] * P.tl[ii], 0.0, 0.0,
                               dict(tl=30, col=K.C_RAY, hw=0, hh=0, dx=P.vx[ii] / sp[f], dy=P.vy[ii] / sp[f], llen=3000, a0=0)))

        # --- RAY1 / TE_RAY (BaseDeathray: 长度 localAI1 以 0.5 插值逼近 3000, 判定宽度 22*scale)
        for k, maxt, smax, mul in ((K.RAY1, 60, 1.0, 4.0), (K.TE_RAY, 90, 0.4, 10.0), (K.RAY2, 180, 0.7, 2.5)):
            i, e = g(k)
            if i.size:
                P.l0[i] += 1
                sc = np.minimum(np.sin(P.l0[i] * np.pi / maxt) * mul * smax, smax)
                if k == K.TE_RAY:
                    a = np.arctan2(P.dy[i], P.dx[i]) + P.a0[i]
                    P.dx[i], P.dy[i] = np.cos(a), np.sin(a)
                    P.dmg[i] = sc > 0
                else:
                    P.dmg[i] = sc >= smax - 1e-6   # Deathray1: scale>=1; Deathray2: scale>=0.7
                P.lw[i] = 22 * np.maximum(sc, 0)
                P.l1[i] += (3000 - P.l1[i]) * 0.5
                P.llen[i] = P.l1[i]
                P.tl[i] = np.where(P.l0[i] >= maxt, 0, 10**6)
                P.vx[i] = 0; P.vy[i] = 0

        # --- TELEGRAPH: a0=1 -> MutantDeathrayAim 跟随 boss 并以 0.2 插值到预判方向
        i, e = g(K.TELEGRAPH)
        if i.size:
            f = P.a0[i] == 1
            if f.any():
                ii, ee = i[f], e[f]
                P.x[ii] = self.bx[ee]; P.y[ii] = self.by[ee]
                tgt = np.arctan2(ppy[ee] + pvy[ee] * 30 - self.by[ee], ppx[ee] + pvx[ee] * 30 - self.bx[ee])
                cur = np.arctan2(P.dy[ii], P.dx[ii])
                a = cur + ((tgt - cur + np.pi) % (2 * np.pi) - np.pi) * 0.2
                P.dx[ii], P.dy[ii] = np.cos(a), np.sin(a)
            P.dmg[i] = False

        # --- EYE_HOMING  MutantEyeHoming.cs
        i, e = g(K.EYE_HOMING)
        if i.size:
            END = -600
            bai0 = self.bai[e, 0]
            maxs = (15.0 if self.maso else 10.0) * np.where((bai0 >= 21) & (bai0 <= 23), 2.0, 1.0)
            stop = ~np.isin(bai0, [4, 5, 6, 13, 14, 15, 21, 22, 23])
            P.a1[i] = np.where(stop, END, P.a1[i])
            P.a1[i] -= 1
            dx, dy = ppx[e] - P.x[i], ppy[e] - P.y[i]
            dist = np.hypot(dx, dy)
            vang = np.arctan2(P.vy[i], P.vx[i])
            sp = np.hypot(P.vx[i], P.vy[i])
            away = stop | ((P.a1[i] > 0) & (dist < 240))
            da = (np.arctan2(-dy, -dx) - vang + np.pi) % (2 * np.pi) - np.pi
            ang_away = vang + da * 0.05
            P.tl[i] = np.where(away, np.minimum(P.tl[i], 180), P.tl[i])
            homing = ~away & (P.a1[i] < 0) & (P.a1[i] > END)
            sp_h = np.where(sp < maxs, sp * 1.02, sp)
            far = dist > (360 if self.maso else 480)
            dh = (np.arctan2(dy, dx) - vang + np.pi) % (2 * np.pi) - np.pi
            ang_h = np.where(far, vang + dh * 0.1, vang)
            P.a1[i] = np.where(homing & ~far, END, P.a1[i])
            nang = np.where(away, ang_away, np.where(homing, ang_h, vang))
            nsp = np.where(homing, sp_h, sp)
            nsp = np.where((P.a1[i] < END) & (nsp > maxs), nsp * 0.96, nsp)
            P.vx[i], P.vy[i] = nsp * np.cos(nang), nsp * np.sin(nang)

        # --- SPEAR_DASH (附着 boss, 冲刺状态 ai0==6 才存在; 每 3 帧留下追踪小球)
        i, e = g(K.SPEAR_DASH)
        if i.size:
            alive = np.isin(self.bai[e, 0], [6, 15, 23])
            kills.append(i[~alive])
            i, e = i[alive], e[alive]
            side = P.a1[i] == -2      # ai1=-2: 每 2 帧向两侧发射非追踪小球 (MutantSpearDash.cs)
            if side.any():
                ii, ee = i[side], e[side]
                s2 = (P.t[ii] % 2) == 0
                if s2.any():
                    ii, ee = ii[s2], ee[s2]
                    for sg in (-1, 1):
                        spawns.append((ee, K.SPHERE_SMALL, P.x[ii], P.y[ii], -P.dy[ii] * 16 * sg, P.dx[ii] * 16 * sg,
                                       dict(tl=15, hw=23, hh=23, a0=-1.0)))
                i, e = i[~side], e[~side]
            sp = np.hypot(self.bvx[e], self.bvy[e]) + 1e-9
            P.dx[i], P.dy[i] = self.bvx[e] / sp, self.bvy[e] / sp
            P.x[i] = self.bx[e] + self.bvx[e]; P.y[i] = self.by[e] + self.bvy[e]
            P.vx[i] = 0; P.vy[i] = 0
            P.l0[i] -= 1
            s = P.l0[i] < 0
            P.l0[i] = np.where(s, 2, P.l0[i])
            if s.any():
                spawns.append((e[s], K.SPHERE_SMALL, P.x[i][s], P.y[i][s], 0.0, 0.0, dict(tl=120, hw=23, hh=23, a0=0.0)))

        # --- SPEAR_AIM  MutantSpearAim.cs (ai1>1: 指向预判点 player+vel*30)
        i, e = g(K.SPEAR_AIM)
        if i.size:
            P.x[i] = self.bx[e]; P.y[i] = self.by[e]
            pred = P.a1[i] > 1
            a = np.arctan2(ppy[e] + pvy[e] * 30 - self.by[e], ppx[e] + pvx[e] * 30 - self.bx[e])
            cur = np.arctan2(P.dy[i], P.dx[i])
            a0_ = np.arctan2(ppy[e] - self.by[e], ppx[e] - self.bx[e])
            a = np.where(pred, a, cur + ((a0_ - cur + np.pi) % (2 * np.pi) - np.pi) * 0.2)
            P.dx[i], P.dy[i] = np.cos(a), np.sin(a)

        # --- SPEAR_SPIN  MutantSpearSpin.cs (每 9 帧, 若距玩家>360 生成 EyeHoming)
        i, e = g(K.SPEAR_SPIN)
        if i.size:
            P.x[i] = self.bx[e]; P.y[i] = self.by[e]
            act = np.isin(self.bai[e, 0], [4, 13, 21])
            P.l0[i] += act
            s = act & (P.l0[i] > 8)
            P.l0[i] = np.where(s, 0, P.l0[i])
            s &= np.hypot(ppx[e] - P.x[i], ppy[e] - P.y[i]) > 360
            if s.any():
                ii, ee = i[s], e[s]
                n = ii.size
                a = self.rng.uniform(-np.pi / 2, np.pi / 2, n)
                spd = self.rng.uniform(6, 9, n)
                vx, vy = -np.sin(a) * spd, np.cos(a) * spd
                flip = np.where(self.by[ee] < ppy[ee], -1, 1)
                tl = P.tl[ii]
                spawns.append((ee, K.EYE_HOMING, P.x[ii] - 76 + self.rng.uniform(0, 152, n),
                               P.y[ii] - 76 + self.rng.uniform(0, 152, n), vx * flip, vy * flip,
                               dict(tl=900, hw=6, hh=6, a1=tl + self.rng.integers(0, np.maximum(tl // 2, 1)))))

        # --- SWORD  MutantSword.cs (绕 boss 旋转, 判定含上一帧/中点 -> 扫掠)
        i, e = g(K.SWORD)
        if i.size:
            a0, a1, a2, l2 = self.bai[e, 0], self.bai[e, 1], self.bai[e, 2], self.blai[e, 2]
            valid = ((a0 == 9) & ~((l2 == 2) & (a1 > 20))) | ((a0 == 45) & (a2 != 0)) | ((a0 == 46) & (a1 <= 20))
            kills.append(i[~valid])
            ang = self.bai[e, 3] + P.a0[i]
            P.x[i] = self.bx[e] + P.a1[i] * np.cos(ang)
            P.y[i] = self.by[e] + P.a1[i] * np.sin(ang)
            P.vx[i] = 0; P.vy[i] = 0
            P.ox[i] = np.where(P.t[i] == 0, P.x[i], P.ox[i])

        # --- True Eyes (L: 旋转激光, S: 幻影弹, R: 球环+冲撞)  MutantTrueEye{L,S,R}.cs
        for k, dur, off in ((K.TE_L, 120, (0, -300)), (K.TE_S, 60, None), (K.TE_R, 90, None)):
            i, e = g(k)
            if not i.size:
                continue
            P.l0[i] += 1
            side = P.l1[i]
            if k == K.TE_L:
                ox, oy = 0.0, -300.0
            elif k == K.TE_S:
                ox, oy = -200 * side, -200.0
            else:
                ox, oy = 300 * side, -300.0
            st = P.a0[i]
            c0 = st == 0
            dx, dy = ppx[e] + ox - P.x[i], ppy[e] + oy - P.y[i]
            d = np.hypot(dx, dy) + 1e-9
            nvx = (P.vx[i] * 29 + dx / d * 24) / 30
            nvy = (P.vy[i] * 29 + dy / d * 24) / 30
            near = np.hypot(ppx[e] - P.x[i], ppy[e] - P.y[i]) < 150
            nvx += near * np.where(P.x[i] < ppx[e], -0.25, 0.25)
            nvy += near * np.where(P.y[i] < ppy[e], -0.25, 0.25)
            P.vx[i] = np.where(c0, nvx, P.vx[i]); P.vy[i] = np.where(c0, nvy, P.vy[i])
            adv = c0 & (P.l0[i] > dur)
            # case 1
            c1 = st == 1
            if k == K.TE_R:
                f = c1 & (P.l0[i] == 1)
                if f.any():
                    for j in range(6):
                        a = 2 * np.pi / 6 * j
                        spawns.append((e[f], K.TE_SPHERE, P.x[i][f] + 100 * np.cos(a), P.y[i][f] - 6 + 100 * np.sin(a),
                                       0.0, 0.0, dict(tl=360, hw=23, hh=23, a0=i[f].astype(np.float32))))
                damp = 0.95
                done1 = c1 & (P.l0[i] > 60)
            else:
                damp = 0.95 if k == K.TE_L else 0.9
                done1 = c1 & (np.hypot(P.vx[i] * damp, P.vy[i] * damp) < 1)
            P.vx[i] = np.where(c1, P.vx[i] * damp, P.vx[i]); P.vy[i] = np.where(c1, P.vy[i] * damp, P.vy[i])
            P.vx[i] = np.where(done1, 0, P.vx[i]); P.vy[i] = np.where(done1, 0, P.vy[i])
            # case 2
            c2 = st == 2
            if k == K.TE_L:
                f = c2 & (P.l0[i] == 1)
                if f.any():
                    rd = (2 * np.pi / 3 / 90) * np.where(P.x[i][f] < ppx[e][f], -1, 1)
                    # [GUESS] 瞳孔角近似为指向玩家; 初始方向 = 指向玩家 - rd*60, 之后每帧转 rd (共 120°)
                    a = np.arctan2(ppy[e][f] - P.y[i][f], ppx[e][f] - P.x[i][f]) - rd * 60
                    spawns.append((e[f], K.TE_RAY, P.x[i][f], P.y[i][f] - 6, 0.0, 0.0,
                                   dict(tl=10**6, col=K.C_RAY, dx=np.cos(a), dy=np.sin(a), a0=rd, lw=0)))
                done2 = c2 & (P.l0[i] > 90)
            elif k == K.TE_S:
                f = c2 & ((P.l0[i] == 7) | (P.l0[i] == 14))
                if f.any():
                    sx, sy = P.x[i][f], P.y[i][f] - 6
                    tx, ty = ppx[e][f] + pvx[e][f] * 15 - sx, ppy[e][f] + pvy[e][f] * 15 - sy
                    dd = np.hypot(tx, ty) + 1e-9
                    spawns.append((e[f], K.PHANT_BOLT, sx, sy, tx / dd * 8, ty / dd * 8, dict(tl=BOLT_TL, hw=5, hh=5)))
                done2 = c2 & (P.l0[i] > 21)
            else:
                f = c2 & (P.l0[i] == 1)
                dx, dy = ppx[e] - P.x[i], ppy[e] - P.y[i]
                d = np.hypot(dx, dy) + 1e-9
                P.vx[i] = np.where(f, dx / d * 24, P.vx[i]); P.vy[i] = np.where(f, dy / d * 24, P.vy[i])
                done2 = c2 & (P.l0[i] > 10)
            nxt = adv | done1 | done2
            P.a0[i] = P.a0[i] + nxt
            P.l0[i] = np.where(nxt, 0, P.l0[i])
            kills.append(i[P.a0[i] >= 3])
            P.dmg[i] = False

        # --- TE_SPHERE: 前 65 帧跟随 R 眼速度 (MutantTrueEyeSphere.cs)
        i, e = g(K.TE_SPHERE)
        if i.size:
            own = P.a0[i].astype(np.int64)
            own_c = np.clip(own, 0, None)
            valid = (own >= 0) & (P.tl[i] > 295) & P.active[own_c] & (P.kind[own_c] == K.TE_R)
            lost = valid & (P.a0[own_c] == 0)
            P.a0[i] = np.where(lost, -1, P.a0[i])
            P.vx[i] = np.where(lost, 0, np.where(valid, P.vx[own_c], P.vx[i]))
            P.vy[i] = np.where(lost, 0, np.where(valid, P.vy[own_c], P.vy[i]))

        # --- PHANT_BOLT (额外更新)
        i, e = g(K.PHANT_BOLT)
        if i.size:
            P.x[i] += P.vx[i] * (BOLT_UPDATES - 1); P.y[i] += P.vy[i] * (BOLT_UPDATES - 1)

        # --- MOON_BLAST  MoonLordSunBlast.cs (链式向前传播 + 驻留爆炸)
        i, e = g(K.MOON_BLAST)
        if i.size:
            P.l1[i] += 1
            fr = P.l1[i] // 2
            P.dmg[i] = (fr == 3) | (fr == 4)
            P.tl[i] = np.where(fr >= MB_FRAMES, 0, 10**6)
            f = (P.l1[i] == 6) & (P.a1[i] > 0)
            if f.any():
                ii, ee = i[f], e[f]
                P.a1[ii] -= 1
                n = ii.size
                st = P.l0[ii] != 2
                if st.any():
                    s = self.rng.uniform(1.5, 4.0, st.sum())
                    spawns.append((ee[st], K.MOON_BLAST, P.x[ii][st] + self.rng.uniform(-20, 20, st.sum()),
                                   P.y[ii][st] + self.rng.uniform(-20, 20, st.sum()), 0.0, 0.0,
                                   dict(tl=10**6, hw=35 * s, hh=35 * s, a0=P.a0[ii][st], a1=np.minimum(5, P.a1[ii][st]), l0=1.0)))
                fw = P.l0[ii] != 1
                if fw.any():
                    m = fw.sum()
                    s = self.rng.uniform(1.5, 4.0, m)
                    a = P.a0[ii][fw] + self.rng.uniform(-np.radians(15), np.radians(15), m)
                    spawns.append((ee[fw], K.MOON_BLAST, P.x[ii][fw] + 100 * np.cos(a), P.y[ii][fw] + 100 * np.sin(a), 0.0, 0.0,
                                   dict(tl=10**6, hw=35 * s, hh=35 * s, a0=P.a0[ii][fw], a1=P.a1[ii][fw], l0=P.l0[ii][fw])))

        # ================= P2/P3 弹幕 =================
        bai0 = self.bai[:, 0]
        rx, ry, ron = self.ritual[:, 0], self.ritual[:, 1], self.ritual[:, 2] > 0

        def far_from_ritual(i, e, r=1200.0):
            return ron[e] & (np.hypot(P.x[i] - rx[e], P.y[i] - ry[e]) > r)

        for k in (K.SPHERE_RING, K.EYE, K.FRAGMENT):          # 离开 ritual 1200 外即消失 (各弹幕 AI 中的 ritual 检查)
            i, e = g(k)
            if i.size:
                kills.append(i[far_from_ritual(i, e)])

        # --- RAY3  MutantDeathray3 (MechRayFan): 原点固定, 每帧转 a0 (45<t<150 时 ×0.9712), scale=sin(tπ/a2)*6 上限 1
        i, e = g(K.RAY3)
        if i.size:
            P.l0[i] += 1
            dec = (P.l0[i] > 45) & (P.l0[i] < 150)
            P.a0[i] = np.where(dec, P.a0[i] * 0.9712, P.a0[i])
            a = np.arctan2(P.dy[i], P.dx[i]) + P.a0[i]
            P.dx[i], P.dy[i] = np.cos(a), np.sin(a)
            sc = np.clip(np.sin(P.l0[i] * np.pi / np.maximum(P.a2[i], 1)) * 6, 0, 1)
            P.lw[i] = 22 * sc
            P.l1[i] += (3000 - P.l1[i]) * 0.5; P.llen[i] = P.l1[i]
            P.dmg[i] = sc > 0
            P.vx[i] = 0; P.vy[i] = 0

        # --- GIANT_RAY  MutantGiantDeathray2 (FinalSpark): 跟随 boss ai3 朝向, scale=min(sin(tπ/600)*70, 10), scale>=5 判定
        i, e = g(K.GIANT_RAY)
        if i.size:
            P.l0[i] += 1
            sc = np.clip(np.sin(P.l0[i] * np.pi / 600) * 70, 0, 10)
            a = self.bai[e, 3]
            P.dx[i], P.dy[i] = np.cos(a), np.sin(a)
            P.x[i] = self.bx[e] + P.dx[i] * 175 * sc / 10; P.y[i] = self.by[e] + P.dy[i] * 175 * sc / 10
            P.lw[i] = 22 * sc; P.llen[i] = 3000
            P.dmg[i] = sc >= 5
            P.vx[i] = 0; P.vy[i] = 0
            kills.append(i[(P.l0[i] >= 600) | (bai0[e] != -5)])

        # --- PILLAR  MutantPillar: 透明度 255->0 期间随主人上升 (无伤), 之后以 32 速度飞向玩家, 死亡时散开碎片
        i, e = g(K.PILLAR)
        if i.size:
            rise = P.a3[i] == 0
            own = np.clip(P.a0[i].astype(np.int64), 0, 3)
            P.l0[i] = np.where(rise, P.l0[i] - 2, P.l0[i])
            P.a2[i] = np.where(rise, P.a2[i] + 5 / 120, P.a2[i])
            P.l1[i] = np.where(rise, P.l1[i] + P.a2[i], P.l1[i])
            ox, oy = self.ill[e, own, 0], self.ill[e, own, 1]
            P.x[i] = np.where(rise, ox, P.x[i]); P.y[i] = np.where(rise, oy + P.l1[i], P.y[i])
            P.vx[i] = np.where(rise, 0, P.vx[i]); P.vy[i] = np.where(rise, 0, P.vy[i])
            go = rise & (P.l0[i] <= 0)
            if go.any():
                ii, ee = i[go], e[go]
                dx, dy = ppx[ee] - P.x[ii], ppy[ee] - P.y[ii]
                d = np.hypot(dx, dy) + 1e-9
                P.vx[ii] = dx / d * 32; P.vy[ii] = dy / d * 32
                P.tl[ii] = np.maximum((d / 32).astype(np.int32), 1) + 1
                P.a3[ii] = 1
            P.dmg[i] = P.a3[i] > 0
        # --- FRAGMENT: 速度 ×0.985
        i, e = g(K.FRAGMENT)
        if i.size:
            P.vx[i] *= 0.985; P.vy[i] *= 0.985
            P.dmg[i] = True

        # --- EOC  MutantEOC (StarSickles): 0-120 帧环绕玩家, 120 锁定目标并传送, 之后反复冲刺并留下镰刀
        i, e = g(K.EOC)
        if i.size:
            P.l0[i] += 1
            t = P.l0[i]
            a = t < 120
            if a.any():
                ii, ee = i[a], e[a]
                fx, fy = P.x[ii] - ppx[ee], P.y[ii] - ppy[ee]
                fd = np.hypot(fx, fy) + 1e-9
                c, s_ = np.cos(np.radians(20)), np.sin(np.radians(20))
                ux, uy = (fx * c - fy * s_) / fd, (fx * s_ + fy * c) / fd
                dist = 700 * np.clip(P.l0[ii] * 1.5 / 120, 0.25, 1)
                tx, ty = ppx[ee] + ux * dist, ppy[ee] + uy * dist
                dx, dy = tx - P.x[ii], ty - P.y[ii]
                d = np.hypot(dx, dy) + 1e-9
                vx, vy = P.vx[ii] + dx / d * 0.6, P.vy[ii] + dy / d * 0.6
                sp = np.hypot(vx, vy) + 1e-9
                f = np.minimum(1, 24 / sp)
                P.vx[ii] = vx * f; P.vy[ii] = vy * f
                P.x[ii] += pvx[ee] / 2; P.y[ii] += pvy[ee] / 2
            lock = t == 120
            if lock.any():
                ii, ee = i[lock], e[lock]
                P.a0[ii] = ppx[ee]; P.a1[ii] = ppy[ee]
                dx, dy = P.x[ii] - P.a0[ii], P.y[ii] - P.a1[ii]
                d = np.hypot(dx, dy) + 1e-9
                P.x[ii] = P.a0[ii] + dx / d * 700; P.y[ii] = P.a1[ii] + dy / d * 700
                P.vx[ii] = 0; P.vy[ii] = 0
            dash = t == 121
            if dash.any():
                ii, ee = i[dash], e[dash]
                dx, dy = P.a0[ii] - P.x[ii], P.a1[ii] - P.y[ii]
                d = np.hypot(dx, dy) + 1e-9
                ux, uy = dx / d, dy / d
                tl = P.tl[ii]
                spawns.append((ee, K.SCYTHE, P.x[ii].copy(), P.y[ii].copy(), 0.0, 0.0,
                               dict(tl=tl + 210, hw=15, hh=15, a0=0.025, dx=ux, dy=uy)))
                c, s_ = np.cos(np.radians(22.5)), np.sin(np.radians(22.5))
                P.vx[ii] = 120 / 6 * (ux * c - uy * s_); P.vy[ii] = 120 / 6 * (ux * s_ + uy * c)
            mid = (t > 121) & (t < 131.67)
            if mid.any():
                ii, ee = i[mid], e[mid]
                dx, dy = P.a0[ii] - P.x[ii], P.a1[ii] - P.y[ii]
                d = np.hypot(dx, dy) + 1e-9
                for fx, fy in ((P.x[ii], P.y[ii]), (P.x[ii] - P.vx[ii] / 2, P.y[ii] - P.vy[ii] / 2)):
                    spawns.append((ee, K.SCYTHE, fx.copy(), fy.copy(), 0.0, 0.0,
                                   dict(tl=P.tl[ii] + 360, hw=15, hh=15, a0=0.03, dx=dx / d, dy=dy / d)))
            P.l0[i] = np.where(t >= 131.67, 120, P.l0[i])
            P.dmg[i] = True
        # --- SCYTHE  MutantScythe1/2: 静止, 剩余寿命 <180 时沿 (dx,dy) 加速 ×(1+a0)
        i, e = g(K.SCYTHE)
        if i.size:
            mv = P.tl[i] < 180
            st = mv & (P.vx[i] == 0) & (P.vy[i] == 0)
            P.vx[i] = np.where(st, P.dx[i], P.vx[i]); P.vy[i] = np.where(st, P.dy[i], P.vy[i])
            sp = np.hypot(P.vx[i], P.vy[i])
            f = np.where(mv & (sp < SCYTHE_MAX), 1 + P.a0[i], 1.0)
            P.vx[i] *= f; P.vy[i] *= f
            P.dmg[i] = True

        # --- DESTROYER  MutantDestroyerHead/Body/Tail: 头 60 帧后追踪, 身体节间距 36 跟随前一节 (1 帧延迟近似)
        i, e = g(K.DESTROYER)
        if i.size:
            head = P.a0[i] < 0
            hi, he = i[head], e[head]
            if hi.size:
                fct = P.a1[hi]
                dx, dy = ppx[he] - P.x[hi], ppy[he] - P.y[hi]
                d = np.hypot(dx, dy) + 1e-9
                far = d > 700
                spd = 10 * fct * np.where(far, 2, 1)
                fr = np.maximum(25 / fct * np.where(far, 0.5, 1), 1)
                h_on = P.t[hi] > 60
                P.vx[hi] = np.where(h_on, P.vx[hi] + (dx / d * spd - P.vx[hi]) / fr, P.vx[hi])
                P.vy[hi] = np.where(h_on, P.vy[hi] + (dy / d * spd - P.vy[hi]) / fr, P.vy[hi])
            bi, be = i[~head], e[~head]
            if bi.size:
                pr = P.a0[bi].astype(np.int64)
                ok = P.active[pr] & (P.kind[pr] == K.DESTROYER)
                kills.append(bi[~ok])
                dx, dy = P.x[pr] - P.x[bi], P.y[pr] - P.y[bi]
                d = np.hypot(dx, dy) + 1e-9
                m = np.maximum(d - 36, 0)
                P.x[bi] += dx / d * m; P.y[bi] += dy / d * m
                P.vx[bi] = 0; P.vy[bi] = 0
            P.dmg[i] = True

        # --- FISHRON (猪鲨 / 暗影手, l1=1 为手)  MutantFishron / ShadowHand
        i, e = g(K.FISHRON)
        if i.size:
            P.l0[i] += 1
            t = P.l0[i]
            hand = P.l1[i] > 0
            fol = t < 85
            dx, dy = ppx[e] + P.a0[i] - P.x[i], ppy[e] + P.a1[i] - P.y[i]
            d = np.hypot(dx, dy) + 1e-9
            near = hand | (d < 50)
            lvx, lvy = (P.vx[i] * 19 + dx / 4) / 20, (P.vy[i] * 19 + dy / 4) / 20
            # SmartAccel(accel 3, decel 2) 近似: 沿差向量加速 3, 速度上限 = 距离/4
            avx, avy = P.vx[i] + dx / d * 3, P.vy[i] + dy / d * 3
            asp = np.hypot(avx, avy) + 1e-9
            cap = np.maximum(d / 4, 2)
            f = np.minimum(1, cap / asp)
            avx *= f; avy *= f
            P.vx[i] = np.where(fol, np.where(near, lvx, avx), P.vx[i])
            P.vy[i] = np.where(fol, np.where(near, lvy, avy), P.vy[i])
            P.x[i] += np.where(fol, pvx[e] / 2, 0); P.y[i] += np.where(fol, pvy[e] / 2, 0)
            go = t == 85
            if go.any():
                ii, ee = i[go], e[go]
                dx, dy = ppx[ee] - P.x[ii], ppy[ee] - P.y[ii]
                d = np.hypot(dx, dy) + 1e-9
                sp = np.where(P.l1[ii] > 0, 20.0, 24.0)
                P.vx[ii] = dx / d * sp; P.vy[ii] = dy / d * sp
            P.dmg[i] = t > 85

        # --- NUKE (重力抛物线, 无伤)
        i, e = g(K.NUKE)
        if i.size:
            P.vy[i] += P.a0[i]
            P.dmg[i] = False
        # --- FISH_RITUAL  MutantFishronRitual: 安全圈 (半径 150) 中心 = boss-(0,100); Nuke 爆炸期 (炸弹存在) 圈外 1200 内受伤
        i, e = g(K.FISH_RITUAL)
        if i.size:
            P.x[i] = self.bx[e]; P.y[i] = self.by[e] - 100
            on = bai0[e] == 34
            P.l0[i] = np.where(on, np.maximum(P.l0[i] - 7, 0), P.l0[i] + 17)
            kills.append(i[~on & (P.l0[i] >= 255)])
            boom = self.bai[e, 1] > (120 if self.maso else 180)
            P.dmg[i] = on & (P.l0[i] <= 0) & boom
            P.vx[i] = 0; P.vy[i] = 0
        # --- BOMB (MutantBomb 爆炸圈 r=200)
        i, e = g(K.BOMB)
        if i.size:
            P.dmg[i] = True
        # --- SLIME  MutantSlimeBall: 远离 boss 且距 ritual 超过 1200+a0 时 10 帧后消失
        i, e = g(K.SLIME)
        if i.size:
            away = np.sign(P.vy[i]) == np.sign(P.y[i] - self.by[e])
            far = np.hypot(P.x[i] - self.bx[e], P.y[i] - self.by[e]) > 1200 + P.a0[i]
            P.l1[i] = np.where(away & far, P.l1[i] + 1, P.l1[i])
            kills.append(i[P.l1[i] > 10])
            P.dmg[i] = True
        # --- RETICLE (无伤预警)
        i, e = g(K.RETICLE)
        if i.size:
            P.dmg[i] = False
        # --- MARK2  MutantMark2 (TwinRangs 水晶): a0 帧后停住, a1 帧时以 15 速度飞向玩家
        i, e = g(K.MARK2)
        if i.size:
            stop = P.t[i] == P.a0[i].astype(np.int32)
            P.vx[i] = np.where(stop, 0, P.vx[i]); P.vy[i] = np.where(stop, 0, P.vy[i])
            fly = P.t[i] == P.a1[i].astype(np.int32)
            if fly.any():
                ii, ee = i[fly], e[fly]
                dx, dy = ppx[ee] - P.x[ii], ppy[ee] - P.y[ii]
                d = np.hypot(dx, dy) + 1e-9
                P.vx[ii] = dx / d * 15; P.vy[ii] = dy / d * 15
            P.dmg[i] = False
        # --- LEAF  MutantCrystalLeaf: 绕 Mark2 半径 100 旋转, 转速随 Mark2 起飞临近而增大
        i, e = g(K.LEAF)
        if i.size:
            m = P.a0[i].astype(np.int64)
            ok = P.active[m] & (P.kind[m] == K.MARK2)
            kills.append(i[~ok])
            rem = P.a1[m] - P.t[m]
            P.a1[i] += 0.15 * np.maximum(0, 150 - rem) / 150
            P.x[i] = P.x[m] + P.vx[m] + 100 * np.cos(P.a1[i]); P.y[i] = P.y[m] + P.vy[m] + 100 * np.sin(P.a1[i])
            P.vx[i] = 0; P.vy[i] = 0
            P.dmg[i] = True
        # --- RANG  MutantRetirang / MutantSpazmarang: 恒速圆周 (向心加速度 a0), 寿命 a1
        i, e = g(K.RANG)
        if i.size:
            P.tl[i] = np.where(P.t[i] == 0, P.a1[i].astype(np.int32), P.tl[i])
            sp = np.hypot(P.vx[i], P.vy[i]) + 1e-9
            ang = np.arctan2(P.vy[i], P.vx[i]) + P.a0[i] / sp
            P.vx[i] = sp * np.cos(ang); P.vy[i] = sp * np.sin(ang)
            P.dmg[i] = True
        # --- LANCE  FairyQueenLance [GUESS: 原版 AI 不在源码中] 漂移 60 帧, 然后沿 a0 以 40 速度刺出, 90 帧消失
        i, e = g(K.LANCE)
        if i.size:
            go = P.t[i] == 60
            P.vx[i] = np.where(go, 40 * np.cos(P.a0[i]), P.vx[i]); P.vy[i] = np.where(go, 40 * np.sin(P.a0[i]), P.vy[i])
            P.dx[i] = np.cos(P.a0[i]); P.dy[i] = np.sin(P.a0[i])
            P.tl[i] = np.where(P.t[i] >= 90, 0, 10**6)
            P.dmg[i] = P.t[i] > 60
        # GUARDIAN (直线冲撞)
        i, e = g(K.GUARDIAN)
        if i.size:
            P.dmg[i] = True

        # --- 默认: 有伤害的普通弹
        for k in (K.SPEAR_THROWN, K.SPHERE_SMALL, K.SPHERE_RING, K.MARK1, K.EYE, K.EYE_HOMING, K.SPEAR_DASH,
                  K.SPEAR_AIM, K.SPEAR_SPIN, K.SWORD, K.TE_SPHERE, K.PHANT_BOLT):
            i, _ = g(k)
            P.dmg[i] = True
        i, e = g(K.SPHERE_SMALL)
        if i.size:
            pass
        # BOMB_SMALL: 帧 0..2 有伤害 (每 3 帧一帧)
        i, e = g(K.BOMB_SMALL)
        if i.size:
            P.dmg[i] = P.t[i] < 9

        # --- 统一移动 + 计时
        P.x[idx] += P.vx[idx]; P.y[idx] += P.vy[idx]
        P.t[idx] += 1
        P.tl[idx] -= 1
        dead = idx[P.tl[idx] <= 0]
        for kk in kills:
            dead = np.concatenate([dead, kk])
        # 死亡触发
        dk = P.kind[dead]
        f = dk == K.SPHERE_SMALL
        if f.any():
            d = dead[f]
            spawns.append((d // self.cap, K.BOMB_SMALL, P.x[d], P.y[d], 0.0, 0.0, dict(tl=BOMB_TL, col=K.C_CIRCLE, r=137.5)))
        f = dk == K.MARK1
        if f.any():
            d = dead[f]
            sp = np.hypot(P.vx[d], P.vy[d]) + 1e-9
            spawns.append((d // self.cap, K.RAY1, P.x[d], P.y[d], 0.0, 0.0,
                           dict(tl=10**6, col=K.C_RAY, dx=P.vx[d] / sp, dy=P.vy[d] / sp, lw=0)))
        f = (dk == K.PILLAR) & (P.a3[dead] > 0)
        if f.any():
            d = dead[f]
            ee = d // self.cap
            ang = np.arctan2(P.vy[d], P.vx[d])
            for j in range(4):
                for q in range(24):
                    a = ang + 2 * np.pi / 24 * q
                    sp = 5 * (j + 0.5)
                    spawns.append((ee, K.FRAGMENT, P.x[d], P.y[d], np.cos(a) * sp, np.sin(a) * sp,
                                   dict(tl=np.maximum(self.blai[ee, 0], 1).astype(np.int32), hw=4, hh=4)))
        P.active[dead] = False
        for (ee, kind, x, y, vx, vy, kw) in spawns:
            P.spawn(ee, kind, x, y, vx, vy, **kw)

    # ------------------------------------------------------------------ collision / geometry
    def gaps(self, idx):
        """每个弹幕到玩家 hitbox 的间隙 (<=0 即重叠) 与弹幕上最近点。"""
        P = self.proj
        e = idx // self.cap
        cx, cy = self.px[e], self.py[e]
        col = P.col[idx]
        x, y, hw, hh = P.x[idx], P.y[idx], P.hw[idx], P.hh[idx]
        # AABB gap
        gx = np.maximum(np.abs(x - cx) - hw - HHW, 0)
        gy = np.maximum(np.abs(y - cy) - hh - HHH, 0)
        gap = np.hypot(gx, gy)
        gap = np.where((gx == 0) & (gy == 0), -1.0, gap)
        qx = np.clip(cx, x - hw, x + hw); qy = np.clip(cy, y - hh, y + hh)
        # lines
        isseg, isray = col == K.C_SEG, col == K.C_RAY
        ln = isseg | isray
        if ln.any():
            j = np.flatnonzero(ln)
            ii = idx[j]
            dx, dy, L = P.dx[ii], P.dy[ii], P.llen[ii]
            seg = isseg[j]
            ax = np.where(seg, P.x[ii] - dx * L / 2, P.x[ii]); ay = np.where(seg, P.y[ii] - dy * L / 2, P.y[ii])
            bx = np.where(seg, P.x[ii] + dx * L / 2, P.x[ii] + dx * L); by = np.where(seg, P.y[ii] + dy * L / 2, P.y[ii] + dy * L)
            dl, lqx, lqy = seg_box_gap(ax, ay, bx, by, cx[j], cy[j], HHW, HHH)
            lg = dl - P.lw[ii] / 2
            lg = np.where(lg <= 0, -1.0, lg)
            use = lg < gap[j]
            if col is not None:
                gap[j] = np.where(use, lg, gap[j])
                qx[j] = np.where(use, lqx, qx[j]); qy[j] = np.where(use, lqy, qy[j])
            # rays: 只有线 (hitbox 48x48 在中心, 影响很小, 忽略)
            gap[j] = np.where(isray[j], lg, gap[j])
            qx[j] = np.where(isray[j], lqx, qx[j]); qy[j] = np.where(isray[j], lqy, qy[j])
        circ = col == K.C_CIRCLE
        if circ.any():
            j = np.flatnonzero(circ)
            ii = idx[j]
            bxp = np.clip(P.x[ii], cx[j] - HHW, cx[j] + HHW); byp = np.clip(P.y[ii], cy[j] - HHH, cy[j] + HHH)
            cg = np.hypot(P.x[ii] - bxp, P.y[ii] - byp) - P.r[ii]
            gap[j] = np.where(cg <= 0, -1.0, cg)
            ddx, ddy = bxp - P.x[ii], byp - P.y[ii]
            dd = np.hypot(ddx, ddy) + 1e-9
            rr = np.minimum(P.r[ii], dd)
            qx[j] = P.x[ii] + ddx / dd * rr; qy[j] = P.y[ii] + ddy / dd * rr
        inv = col == K.C_INVCIRC     # 安全圈: 圈内 (r) 与 llen 外安全
        if inv.any():
            j = np.flatnonzero(inv)
            ii = idx[j]
            ddx, ddy = cx[j] - P.x[ii], cy[j] - P.y[ii]
            dc = np.hypot(ddx, ddy) + 1e-9
            g_in = P.r[ii] - dc - HHW
            g_out = dc - HHW - P.llen[ii]
            cg = np.maximum(g_in, g_out)
            gap[j] = np.where(cg <= 0, -1.0, cg)
            rr = np.where(g_in > g_out, P.r[ii], P.llen[ii])
            qx[j] = P.x[ii] + ddx / dc * rr; qy[j] = P.y[ii] + ddy / dc * rr
        sw = col == K.C_SWEPT
        if sw.any():
            j = np.flatnonzero(sw)
            ii = idx[j]
            for fx, fy in ((P.ox[ii], P.oy[ii]), ((P.ox[ii] + P.x[ii]) / 2, (P.oy[ii] + P.y[ii]) / 2)):
                g2x = np.maximum(np.abs(fx - cx[j]) - hw[j] - HHW, 0)
                g2y = np.maximum(np.abs(fy - cy[j]) - hh[j] - HHH, 0)
                g2 = np.where((g2x == 0) & (g2y == 0), -1.0, np.hypot(g2x, g2y))
                gap[j] = np.minimum(gap[j], g2)
        nonecol = col == K.C_NONE
        return gap, qx, qy

    def _collide(self):
        P = self.proj
        idx = np.flatnonzero(P.active)
        hit = np.zeros(self.N, bool)
        self.hit_kind[:] = 0
        if idx.size == 0:
            self._cache = (idx, np.zeros(0), np.zeros(0), np.zeros(0))
            return hit
        gap, qx, qy = self.gaps(idx)
        h = (gap <= 0) & P.dmg[idx] & (P.col[idx] != K.C_NONE)
        hit[idx[h] // self.cap] = True
        self.hit_kind[idx[h] // self.cap] = self.proj.kind[idx[h]]
        self._cache = (idx, gap, qx, qy)
        return hit
