"""向量化弹幕池 (numpy). 所有 env 的弹幕放在扁平数组 [n_envs * cap] 中。
每种弹幕的行为都对照 FargowiltasSouls 源码, 见 docs/attacks.md。
"""
import numpy as np

# ---- kinds ----
_NAMES = ['SPEAR_THROWN', 'SPHERE_SMALL', 'BOMB_SMALL', 'SPHERE_RING', 'MARK1', 'RAY1', 'TELEGRAPH', 'EYE', 'EYE_HOMING',
          'SPEAR_DASH', 'SPEAR_AIM', 'SPEAR_SPIN', 'SWORD', 'TE_L', 'TE_S', 'TE_R', 'TE_SPHERE', 'TE_RAY', 'PHANT_BOLT',
          'MOON_BLAST',
          # ---- P2/P3 ----
          'RAY2', 'RAY3', 'GIANT_RAY', 'PILLAR', 'FRAGMENT', 'EOC', 'SCYTHE', 'DESTROYER', 'GUARDIAN', 'FISHRON',
          'NUKE', 'FISH_RITUAL', 'BOMB', 'SLIME', 'RETICLE', 'MARK2', 'LEAF', 'RANG', 'LANCE']
for _i, _n in enumerate(_NAMES):
    globals()[_n] = _i + 1
N_KINDS = len(_NAMES) + 1
KIND_NAMES = {i + 1: n for i, n in enumerate(_NAMES)}
# 观测里的粗分组: 0 小弹 1 大球/爆炸 2 射线 3 长矛/剑/实体冲撞 4 预警(无伤) 5 无伤实体 6 区域(安全圈)
KIND_GROUP = np.zeros(N_KINDS, np.int64)
for k, g in [(SPEAR_THROWN, 3), (SPHERE_SMALL, 1), (BOMB_SMALL, 1), (SPHERE_RING, 1), (MARK1, 1), (RAY1, 2),
             (TELEGRAPH, 4), (EYE, 0), (EYE_HOMING, 0), (SPEAR_DASH, 3), (SPEAR_AIM, 3), (SPEAR_SPIN, 3),
             (SWORD, 3), (TE_L, 5), (TE_S, 5), (TE_R, 5), (TE_SPHERE, 1), (TE_RAY, 2), (PHANT_BOLT, 0), (MOON_BLAST, 1),
             (RAY2, 2), (RAY3, 2), (GIANT_RAY, 2), (PILLAR, 3), (FRAGMENT, 0), (EOC, 3), (SCYTHE, 0), (DESTROYER, 3),
             (GUARDIAN, 3), (FISHRON, 3), (NUKE, 5), (FISH_RITUAL, 6), (BOMB, 1), (SLIME, 0), (RETICLE, 4),
             (MARK2, 4), (LEAF, 0), (RANG, 3), (LANCE, 3)]:
    KIND_GROUP[k] = g
N_GROUPS = 7

# collider types
C_NONE, C_AABB, C_SEG, C_RAY, C_CIRCLE, C_SWEPT, C_INVCIRC = 0, 1, 2, 3, 4, 5, 6   # INVCIRC: 半径 r 安全圈外 (llen 内) 受伤

FIELDS_F = ['x', 'y', 'vx', 'vy', 'hw', 'hh', 'dx', 'dy', 'llen', 'lw', 'r', 'ox', 'oy',
            'a0', 'a1', 'a2', 'a3', 'l0', 'l1']
FIELDS_I = ['kind', 'col', 't', 'tl']


class ProjPool:
    def __init__(self, n_envs, cap, rng):
        self.N, self.P = n_envs, cap
        self.rng = rng
        size = n_envs * cap
        self.active = np.zeros(size, bool)
        self.dmg = np.zeros(size, bool)     # 本帧是否可造成伤害
        for f in FIELDS_F:
            setattr(self, f, np.zeros(size, np.float32))
        for f in FIELDS_I:
            setattr(self, f, np.zeros(size, np.int32))
        self.dropped = 0

    def clear(self, env_mask):
        m = np.repeat(env_mask, self.P)
        self.active[m] = False

    def spawn(self, env, kind, x, y, vx=0.0, vy=0.0, tl=60, col=C_AABB, hw=8.0, hh=8.0, **kw):
        """env 可为数组 (允许重复)。返回扁平索引 (分配失败的为 -1)。"""
        env = np.atleast_1d(np.asarray(env, np.int64))
        R = env.size
        if R == 0:
            return env
        order = np.argsort(env, kind='stable')
        es = env[order]
        # rank within env
        first = np.r_[0, np.flatnonzero(np.diff(es)) + 1]
        rank = np.arange(R) - np.repeat(first, np.diff(np.r_[first, R]))
        free = ~self.active.reshape(self.N, self.P)
        cum = np.cumsum(free[es], axis=1)
        ok = cum[:, -1] > rank
        slot = np.argmax(cum == (rank + 1)[:, None], axis=1)
        idx_sorted = np.where(ok, es * self.P + slot, -1)
        idx = np.empty(R, np.int64)
        idx[order] = idx_sorted
        self.dropped += int((~ok).sum())
        good = idx >= 0
        gi = idx[good]

        def bc(v):
            v = np.asarray(v)
            return v if v.ndim == 0 else v.reshape(-1)[good] if v.size == R else v
        self.active[gi] = True
        self.dmg[gi] = False
        self.kind[gi] = kind
        self.col[gi] = col
        self.t[gi] = 0
        self.tl[gi] = bc(tl)
        self.x[gi] = bc(x); self.y[gi] = bc(y); self.vx[gi] = bc(vx); self.vy[gi] = bc(vy)
        self.hw[gi] = bc(hw); self.hh[gi] = bc(hh)
        for f in ['dx', 'dy', 'llen', 'lw', 'r', 'a0', 'a1', 'a2', 'a3', 'l0', 'l1']:
            getattr(self, f)[gi] = bc(kw.get(f, 0.0))
        self.ox[gi] = self.x[gi]; self.oy[gi] = self.y[gi]
        return idx
