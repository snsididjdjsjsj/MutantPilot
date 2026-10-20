"""MutantBoss 一阶段 AI 的逐帧复刻 (Eternity 默认, maso 开关部分支持)。
每个 env 一个 Boss 对象 (标量 Python, 每帧很便宜); 弹幕由 ProjPool 向量化处理。
行号引用: FargowiltasSouls/Content/Bosses/MutantBoss/MutantBoss.cs
"""
import math
import random
import collections
from . import projectiles as K
from .constants import *

TAU = 2 * math.pi


def norm(x, y):
    d = math.hypot(x, y)
    if d < 1e-6:
        return 0.0, 0.0
    return x / d, y / d


def rot(x, y, a):
    c, s = math.cos(a), math.sin(a)
    return x * c - y * s, x * s + y * c


def angle_lerp(a, b, t):
    d = (b - a + math.pi) % TAU - math.pi
    return a + d * t


P1_ATTACKS = [0, 1, 2, 4, 7, 9]  # 一阶段"入口"状态 (ai[0])
# 二阶段入口 (Phase2Transition 随机列表 + 各 ChooseNextAttack 列表的并集, MB.cs:1637 等)
P2_ATTACKS = [11, 13, 16, 19, 20, 21, 24, 26, 29, 31, 33, 35, 37, 39, 41, 42, 44, 45]
P3_ATTACKS = [-2, -3, -4, -5]
ATTACK_NAMES = {0: 'SpearTossDirectP1', 1: 'OkuuSpheresP1', 2: 'PrepareTrueEyeDiveP1', 3: 'TrueEyeDive',
                4: 'PrepareSpearDashDirectP1', 5: 'SpearDashDirectP1', 6: 'WhileDashingP1',
                7: 'ApproachForNextAttackP1', 8: 'VoidRaysP1', 9: 'BoundaryBulletHellAndSwordP1', 10: 'Phase2Transition',
                11: 'ApproachForNextAttackP2', 12: 'VoidRaysP2', 13: 'PrepareSpearDashPredictiveP2',
                14: 'SpearDashPredictiveP2', 15: 'WhileDashingP2', 16: 'ApproachForBoundaryP2', 17: 'BoundaryBulletHellP2',
                19: 'PillarDunk', 20: 'EOCStarSickles', 21: 'PrepareSpearDashDirectP2', 22: 'SpearDashDirectP2',
                23: 'WhileDashingDirectP2', 24: 'SpawnDestroyersForPredictiveThrow', 25: 'SpearTossPredictiveP2',
                26: 'PrepareMechRayFan', 27: 'MechRayFan', 29: 'PrepareFishron1', 30: 'SpawnFishrons',
                31: 'PrepareTrueEyeDiveP2', 32: 'TrueEyeDiveP2', 33: 'PrepareNuke', 34: 'Nuke', 35: 'PrepareSlimeRain',
                36: 'SlimeRain', 37: 'PrepareFishron2', 38: 'SpawnShadowHands', 39: 'PrepareOkuuSpheresP2',
                40: 'OkuuSpheresP2', 41: 'SpearTossDirectP2', 42: 'PrepareTwinRangsAndCrystals', 43: 'TwinRangsAndCrystals',
                44: 'EmpressSwordWave', 45: 'PrepareMutantSword', 46: 'MutantSwordP2', 52: 'P2NextAttackPause',
                -1: 'Phase3Transition', -2: 'VoidRaysP3', -3: 'OkuuSpheresP3', -4: 'BoundaryBulletHellP3',
                -5: 'FinalSpark', -6: 'DyingDramaticPause', -7: 'DyingAnimation'}
LIFE_MAX = BOSS_LIFEMAX_EXPERT
LANCE_LEN, LANCE_W = 180.0, 18.0   # FairyQueenLance (原版, 源码不可得: 近似)
P3_DRAIN_TIME = 480 + 240 + 420 + 480 + 1020 - 60     # MB.cs:407 (Eternity)


def wrap(a):
    return (a + math.pi) % TAU - math.pi


class Boss:
    def __init__(self, sim, e, seed):
        self.sim, self.e = sim, e
        self.rng = random.Random(seed)
        self.maso = sim.maso

    def reset(self, px, py, start_attack=0, forced=None, phase=1):
        """phase: 1 = 从 P1 开始; 2 = 从 Phase2Transition 尾声开始 (满血, ritual 已生成); 3 = 从 Phase3Transition 开始。
        forced: 课程用, 只循环该攻击入口 (不掉血/不换阶段)。"""
        self.x = px + self.rng.choice([-1, 1]) * 500.0
        self.y = py - 250.0
        self.vx = self.vy = 0.0
        self.ai = [float(start_attack), 0.0, 0.0, 0.0]
        self.lai = [0.0, 0.0, 0.0, 1.0]   # localAI[3]=1: 视为已进入战斗距离 (:457-466)
        self.direction = 1
        self.aura_x, self.aura_y, self.aura_scale = self.x, self.y, 1.0
        self.forced = forced              # 课程学习: 只循环某一个攻击入口
        self.attacks_done = 0
        self.life = LIFE_MAX
        self.hist = collections.deque()   # attackHistory (MB.cs:569-658)
        self.attack_count = 0
        self.ritual = False
        self.rx, self.ry = self.x, self.y
        self.ill = [(self.x, self.y)] * 4  # PillarDunk 分身位置 (0 = 本体)
        self.n_ill = 0
        self.reached = 1                  # 已到达的最高阶段 (1,2,3), 4 = 胜利 (进入 -6)
        if phase >= 2 or (forced is not None and (forced >= 10 or forced < 0)):
            self.lai[3] = 3.0
            self.ritual = True
            self.reached = 2
            if forced is None and phase == 2:
                self.ai = [10.0, 240.0, 0.0, 0.0]   # 跳过 Phase2Transition 的前 240 帧动画
        if phase >= 3 or (forced is not None and forced < 0):
            self.reached = 3
            if forced is None or forced == -2:
                self.ai = [-1.0, 0.0 if forced is None else 481.0, 0.0, 0.0]
        if forced is not None and forced >= 10:
            self.ai = [float(forced), 0.0, 0.0, 0.0]
        elif forced is not None and forced < -2:
            self.ai = [float(forced), 0.0, 0.0, 0.0]

    # ----------------------------------------------------------- helpers
    def player(self):
        s, e = self.sim, self.e
        return float(s.px[e]), float(s.py[e]), float(s.pvx[e]), float(s.pvy[e])

    def spawn(self, kind, x, y, vx=0.0, vy=0.0, now=False, **kw):
        """默认放入本帧生成队列 (sim 每帧按种类批量分配, 快很多); now=True 立即分配并返回索引。"""
        if now:
            return self.sim.proj.spawn(self.e, kind, x, y, vx, vy, **kw)
        self.sim.spawn_q.append((self.e, kind, x, y, vx, vy, kw))
        return None

    def clear_projectiles(self):  # FargoSoulsUtil.ClearHostileProjectiles / ClearAllProjectiles
        s, e = self.sim, self.e
        s.proj.active[e * s.cap:(e + 1) * s.cap] = False
        s.spawn_q[:] = [q for q in s.spawn_q if q[0] != e]

    def dist_to(self, tx, ty):
        return math.hypot(self.x - tx, self.y - ty)

    def dir_to(self, tx, ty):
        return norm(tx - self.x, ty - self.y)

    def movement(self, tx, ty, speed, fastX=True, cap=True):  # :792
        turn, maxs = 1.0, 24.0
        if self.maso:
            speed *= 2; turn *= 2; maxs *= 1.5
        if abs(self.x - tx) > 10:
            if self.x < tx:
                self.vx += speed
                if self.vx < 0:
                    self.vx += speed * (2 if fastX else 1) * turn
            else:
                self.vx -= speed
                if self.vx > 0:
                    self.vx -= speed * (2 if fastX else 1) * turn
        if self.y < ty:
            self.vy += speed
            if self.vy < 0:
                self.vy += speed * 2 * turn
        else:
            self.vy -= speed
            if self.vy > 0:
                self.vy -= speed * 2 * turn
        if cap:
            if abs(self.vx) > maxs: self.vx = math.copysign(maxs, self.vx)
            if abs(self.vy) > maxs: self.vy = math.copysign(maxs, self.vy)

    def side(self, px):  # NPC.Center.X < target.X ? -1 : 1
        return -1.0 if self.x < px else 1.0

    def phase2_check(self):  # :773
        if self.forced is None and self.life < LIFE_MAX * (2.0 / 3):
            self.ai = [10.0, 0.0, 0.0, 0.0]
            self.clear_projectiles()
            return True
        return False

    def p1_next(self, source):  # P1NextAttackOrMasoOptions :660
        ai, lai = self.ai, self.lai
        self.attacks_done += 1
        if self.forced is not None:
            if self.forced == 9 and ai[0] == 9 and lai[2] == 0:
                lai[2] = 1.0          # 边界弹幕之后接剑 (与正常流程一致)
            else:
                ai[0] = float(self.forced)
                lai[2] = 0.0
        elif self.maso and self.rng.random() < 1 / 3:
            opts = [0, 1, 2, 4, 7, 9, 9]
            ai[0] = float(self.rng.choice(opts))
            if ai[0] == source:
                ai[0] = 0.0 if source == 9 else 9.0
            bad = (ai[0] == 9 and source in (1, 2, 7)) or (ai[0] in (0, 7) and source == 2)
            if bad:
                ai[0] = 4.0
            elif ai[0] == 9 and self.rng.random() < 0.5:
                lai[2] = 1.0
            else:
                lai[2] = 0.0
        else:
            if ai[0] == 9 and lai[2] == 0:
                lai[2] = 1.0
            else:
                ai[0] += 1
                lai[2] = 0.0
        if ai[0] >= 10:
            ai[0] = 0.0
        ai[1] = ai[2] = ai[3] = 0.0
        lai[0] = lai[1] = 0.0

    def choose_next(self, *args):  # ChooseNextAttack :569
        ai, lai = self.ai, self.lai
        buffer = ai[0] + 1
        ai[0] = 52.0
        ai[1] = 0.0; ai[2] = buffer; ai[3] = 0.0
        lai[0] = lai[1] = lai[2] = 0.0
        self.attacks_done += 1
        if self.forced is not None:
            ai[2] = float(self.forced)
            return
        use_rand = lai[3] >= 3 and (self.maso or self.rng.uniform(0, 0.8) + 0.2 > (self.life / LIFE_MAX) ** 2)
        recent = list(self.hist)
        if use_rand:
            ai[2] = float(self.rng.choice(args))
        while recent:
            found = False
            for _ in range(5):
                if ai[2] not in recent:
                    found = True
                    break
                ai[2] = float(self.rng.choice(args))
            if found:
                break
            recent.pop(0)
        max_mem = 12 if self.maso else 18
        self.attack_count += 1
        if self.attack_count - 1 > max_mem * 1.25:
            self.attack_count = 0
            max_mem //= 4
        self.hist.append(ai[2])
        while len(self.hist) > max_mem:
            self.hist.popleft()

    def p3_next(self):
        """P3 攻击结束: 正常流程 AttackChoice--; 课程模式重复同一攻击。"""
        self.attacks_done += 1
        if self.forced is not None:
            f = int(self.forced)
            self.ai = [-1.0, 481.0, 0.0, 0.0] if f == -2 else [float(f), 0.0, 0.0, 0.0]
        else:
            self.ai[0] -= 1
            self.ai[1] = self.ai[2] = self.ai[3] = 0.0
        self.lai[0] = 0.0

    # ----------------------------------------------------------- main
    DISPATCH = None

    def step(self):
        px, py, pvx, pvy = self.player()
        self.direction = 1 if self.x < px else -1   # :244
        a = int(self.ai[0])
        f = getattr(self, Boss.DISPATCH.get(a, 'p2_default'))
        drain = f(px, py, pvx, pvy)
        a_after = int(self.ai[0])
        # aura (:375-391)
        tgt = 0.7 if a_after == 1 else 1.25 if a_after in (5, 6) else 1.0
        self.aura_scale += (tgt - self.aura_scale) * (0.02 if a_after == 1 else 0.1)
        self.x += self.vx; self.y += self.vy        # NPC.UpdateNPC: position += velocity (AI 之后)
        if not (self.maso and a_after in (5, 6)):
            self.aura_x += (self.x - self.aura_x) * 0.3
            self.aura_y += (self.y - self.aura_y) * 0.3
        # ---- 生命值 (PLAYER_DPS 推算玩家输出) ----
        if self.forced is None:
            if a_after >= 0 and a_after != 10:
                self.life -= self.sim.player_dps / 60.0
                if a_after >= 11 and self.life <= 0:                   # CheckDead :3967 (Eternity, ai0>=10)
                    self.life = 1.0
                    self.ai = [-1.0, 0.0, 0.0, 0.0]
                    self.lai[0] = self.lai[1] = self.lai[2] = 0.0
                    self.clear_projectiles()
            elif a_after < 0 and drain is not False and self.life > 1:  # P3 自动掉血 :405-414
                self.life = max(self.life - LIFE_MAX // P3_DRAIN_TIME, 1.0)
        a_after = int(self.ai[0])
        if a_after == 10 or 11 <= a_after <= 52:
            self.reached = max(self.reached, 2)
        if a_after < 0:
            self.reached = max(self.reached, 3)
        if a_after <= -6:
            self.reached = 4
        # ---- MutantRitual (P2+, MutantRitual.cs Movement) ----
        if self.ritual:
            div = 20.0 if a_after == 36 else 40.0 if a_after in (22, 23, 25) else 60.0
            if a_after != 19:
                self.rx += (self.x - self.rx) / div
                self.ry += (self.y - self.ry) / div
        # 分身位置 (MutantIllusion.cs: Center = player + (player - mutant) * (ai1, ai2))
        if a_after == 19 and self.n_ill:
            dx, dy = px - self.x, py - self.y
            self.ill = [(self.x, self.y)] + [(px + dx * m1, py + dy * m2) for (m1, m2) in ((1, -1), (1, 1), (-1, 1))]
        return a

    def p2_default(self, px, py, pvx, pvy):  # default: AttackChoice = 11 (:353); 18/28: ++
        a = int(self.ai[0])
        if a in (18, 28):
            self.ai[0] += 1
            return
        self.ai[0] = 11.0
        self.approach_p2(px, py, pvx, pvy)

    # ---------------- ai0 = 0  SpearTossDirectP1AndChecks :948
    def spear_toss(self, px, py, pvx, pvy):
        if self.phase2_check():
            return
        ai, lai = self.ai, self.lai
        lai[2] = 0
        tx = px + 500 * (-1 if self.x < px else 1)
        if self.dist_to(tx, py) > 50:
            self.movement(tx, py, 0.5 if lai[3] > 0 else 2.0, True, lai[3] > 0)
        if ai[3] == 0:
            ai[3] = float(self.rng.randint(2, 7)) if self.maso else 5.0
        if lai[3] > 0:
            ai[1] += 1
        if ai[1] < 145:
            dx, dy = px + pvx * 30 - self.x, py + pvy * 30 - self.y
            lai[0] = math.atan2(dy, dx)
        if ai[1] > 150:
            ai[1] = 60
            ai[2] += 1
            if ai[2] > ai[3]:
                self.p1_next(0)
                nx, ny = norm(px - self.x, py - self.y)
                self.vx, self.vy = nx * 2, ny * 2
            else:
                vx, vy = math.cos(lai[0]) * 25, math.sin(lai[0]) * 25
                self.spawn(K.SPEAR_THROWN, self.x, self.y, vx, vy, tl=180, col=K.C_SEG, hw=8, hh=8,
                           dx=math.cos(lai[0]), dy=math.sin(lai[0]), llen=200, lw=8, a1=0)
                if self.maso:
                    self.spawn_ray2(lai[0])
            lai[0] = 0
        elif ai[1] == 61 and ai[2] < ai[3]:
            # MutantDeathrayAim (预警, 85 帧, 跟随+预判) 与 MutantSpearAim(ai1=3, 90 帧, 有判定)
            ang = math.atan2(py + pvy * 30 - self.y, px + pvx * 30 - self.x)
            self.spawn(K.TELEGRAPH, self.x, self.y, tl=85, col=K.C_RAY, hw=0, hh=0, dx=math.cos(ang), dy=math.sin(ang),
                       llen=3000, lw=0, a0=1)
            ang0 = math.atan2(py - self.y, px - self.x)
            self.spawn(K.SPEAR_AIM, self.x, self.y, tl=60 + 30, col=K.C_SEG, hw=8, hh=8,
                       dx=math.cos(ang0), dy=math.sin(ang0), llen=200, lw=8, a1=3)

    # ---------------- ai0 = 1  OkuuSpheresP1 :1053
    def okuu(self, px, py, pvx, pvy):
        if self.phase2_check():
            return
        ai = self.ai
        if self.maso:
            self.vx = self.vy = 0.0
        ai[1] -= 1
        if ai[1] < 0:
            mod = 3 if self.maso else 1
            ai[1] = 90 / mod
            ai[2] += 1
            if ai[2] > 4 * mod:
                if not self.maso or ai[2] > 6 * mod:
                    self.p1_next(1)
            else:
                mx = 9 if self.maso else 6
                sp = 10.0 if self.maso else 9.0
                sign = (1 if ai[2] % 2 == 0 else -1) if self.maso else 1
                self.sphere_ring(mx, sp, 1.0 * sign)
                self.sphere_ring(mx, sp, -0.5 * sign)

    def sphere_ring(self, mx, speed, rotmod, offset=0.0):  # SpawnSphereRing :711
        r = TAU / mx
        for i in range(mx):
            vx, vy = rot(0.0, speed, r * i + offset)
            self.spawn(K.SPHERE_RING, self.x, self.y, vx, vy, tl=480, hw=20, hh=20,
                       a0=rotmod * self.direction, a1=speed, a2=speed)

    # ---------------- ai0 = 2/3 True Eye Dive :1087 / :1112
    def prep_eye_dive(self, px, py, pvx, pvy):
        if self.phase2_check():
            return
        ai = self.ai
        tx = px + 700 * (-1 if self.x < px else 1)
        ty = py - 400
        self.movement(tx, ty, 0.6)
        ai[1] += 1
        if self.dist_to(tx, ty) < 50 or ai[1] > 180:
            self.vx = 35.0 * (1 if self.x < px else -1)
            if self.vy < 0:
                self.vy *= -1
            self.vy *= 0.3
            ai[0] += 1
            ai[1] = 0

    def eye_dive(self, px, py, pvx, pvy):
        ai = self.ai
        if ai[3] == 0:
            ai[3] = math.copysign(1.0, self.x - px) if self.x != px else 1.0
        if ai[2] > 3:
            tx = px + (-500 if self.x < px else 500)
            if self.dist_to(tx, py) > 50:
                self.movement(tx, py, 0.3)
        else:
            self.vx *= 0.99; self.vy *= 0.99
        ai[1] -= 1
        if ai[1] < 0:
            ai[1] = 15
            mxe = 6 if self.maso else 3
            endlag = 3 if self.maso else 5
            ai[2] += 1
            if ai[2] > mxe + endlag:
                if ai[0] == 3:
                    self.p1_next(2)
                else:
                    self.choose_next(13, 19, 21, 24, 33, 33, 33, 39, 41, 44)
            elif ai[2] <= mxe:
                ratio = ai[2] / mxe * 3
                kind = K.TE_L if ratio <= 1 else K.TE_S if ratio <= 2 else K.TE_R
                # 真眼本体 CanDamage=false; a0=子状态(ai1), l1=side(localAI1)
                self.spawn(kind, self.x, self.y, 0, 0, tl=10**6, col=K.C_NONE, hw=16, hh=21, l1=ai[3])

    # ---------------- ai0 = 4/5/6 Spear dash direct :1173-1257
    def prep_dash(self, px, py, pvx, pvy):
        if self.phase2_check():
            return
        ai = self.ai
        if ai[3] == 0:
            ai[3] = 1
            self.spawn(K.SPEAR_SPIN, self.x, self.y, tl=240, hw=76, hh=76, a0=self.rng.choice([-1, 1]))
        ai[1] += 1
        if ai[1] > 240:
            ai[0] += 1
            ai[3] = 0
        tx, ty = px, py
        if self.y - BOSS_W / 2 < py + PLAYER_H / 2:   # NPC.Top.Y < player.Bottom.Y
            tx += 600 * (1 if self.x > px else -1 if self.x < px else 0)
        ty += 400
        self.movement(tx, ty, 0.7, False)

    def dash(self, px, py, pvx, pvy):
        if self.phase2_check():
            return
        ai = self.ai
        self.vx *= 0.9; self.vy *= 0.9
        if ai[3] == 0:
            ai[3] = float(self.rng.randint(3, 14)) if self.maso else 10.0
        ai[1] += 1
        if ai[1] > ai[3]:
            ai[0] += 1
            ai[1] = 0
            ai[2] += 1
            if ai[2] > 5:
                self.p1_next(4)
            else:
                sp = 45.0 if self.maso else 30.0
                nx, ny = norm(px + pvx - self.x, py + pvy - self.y)
                self.vx, self.vy = nx * sp, ny * sp
                self.spawn(K.SPEAR_DASH, self.x, self.y, tl=10**6, col=K.C_SEG, hw=8, hh=8, dx=nx, dy=ny,
                           llen=200, lw=8)
                if self.maso:
                    self.spawn_ray2(math.atan2(ny, nx))

    def while_dash(self, px, py, pvx, pvy):
        ai = self.ai
        self.direction = 1 if self.vx >= 0 else -1
        ai[1] += 1
        if ai[1] > 30:
            ai[0] -= 1
            ai[1] = 0

    # ---------------- ai0 = 7/8 Void rays :1259 / :1282
    def approach(self, px, py, pvx, pvy):
        if self.phase2_check():
            return
        ai = self.ai
        dx, dy = norm(self.x - px, self.y - py)
        tx, ty = px + dx * 250, py + dy * 250
        ai[2] += 1
        if self.dist_to(tx, ty) > 50 and ai[2] < 180:
            self.movement(tx, ty, 0.5)
        else:
            ai[0] += 1
            ai[1] = 0
            ai[2] = math.atan2(self.y - py, self.x - px)
            ai[3] = math.pi / 10 * (-1 if px < self.x else 1)

    def void_rays(self, px, py, pvx, pvy):
        if self.phase2_check():
            return
        ai, lai = self.ai, self.lai
        self.vx = self.vy = 0.0
        ai[1] -= 1
        if ai[1] < 0:
            vx, vy = rot(2.0, 0.0, ai[2])
            self.spawn(K.MARK1, self.x, self.y, vx, vy, tl=90, hw=23, hh=23)
            ai[1] = 3 if self.maso else 5
            ai[2] += ai[3]
            old = lai[0]; lai[0] += 1
            if old == 20 or lai[0] == 40:
                ai[2] -= ai[3] / (3 if self.maso else 2)
            elif lai[0] >= (60 if self.maso else 40):
                self.p1_next(7)

    # ---------------- ai0 = 9 boundary + sword :1310-1549
    def boundary_sword(self, px, py, pvx, pvy):
        ai, lai = self.ai, self.lai
        st = int(lai[2])
        if st == 0:
            if self.phase2_check():
                return
            if ai[3] == 0:
                ai[3] = 1
                lai[0] = math.copysign(1.0, self.x - px)
            self.vx = self.vy = 0.0
            ai[1] += 1
            if ai[1] > 2:
                ai[1] = 0
                if self.maso:
                    ai[2] += math.pi / 8 / 480 * (ai[3] - 300) * lai[0]
                else:
                    ai[2] += math.pi / 77
                mx = 5 if self.maso else 4
                for i in range(mx):
                    vx, vy = rot(0.0, -7.0, ai[2] + TAU / mx * i)
                    self.spawn(K.EYE, self.x, self.y, vx, vy, tl=300, hw=6, hh=6)
            ai[3] += 1
            if ai[3] > (360 if self.maso else 240):
                self.p1_next(9)
        elif st == 1:
            self.prep_sword(px, py)
        elif st == 2:
            self.sword(px, py)

    def prep_sword(self, px, py):  # PrepareMutantSword :1402 (P1: ai0=9/localAI2=1; P2: ai0=45)
        ai, lai = self.ai, self.lai
        p2 = ai[0] != 9
        sign = -1 if (p2 and lai[2] % 2 == 1) else 1
        if ai[2] == 0:
            tx = px + 420 * (1 if self.x > px else -1 if self.x < px else 0)
            ty = py - 210 * sign
            self.movement(tx, ty, 1.2)
            lai[0] += 1
            if (lai[0] > 30 or self.maso) and self.dist_to(tx, ty) < 64:
                self.vx = self.vy = 0.0
                lai[1] = 1.0 if px > self.x else -1.0 if px < self.x else 0.0
                start = math.pi / 4 * -lai[1]
                ai[2] = start * -4 / 20 * sign
                if sign < 0:
                    start += math.pi / 2 * -lai[1]
                ox, oy = rot(0.0, 1.0, start)
                ox, oy = -ox * 80, -oy * 80
                base = math.atan2(oy, ox)
                swords = [(base, 80 * i) for i in range(1, 12)]     # i=0 半径 0, 方向未定义 -> 并入 boss 中心
                swords.append((base, 0.0))
                for s_ in (-1, 1):
                    swords.append((base + math.radians(26.5 * s_), 180.0))
                    swords.append((base + math.radians(40 * s_), 240.0))
                for ang, rad in swords:
                    self.spawn(K.SWORD, self.x + math.cos(ang) * rad, self.y + math.sin(ang) * rad, tl=110,
                               col=K.C_SWEPT, hw=32, hh=32, a0=ang, a1=rad)
                ai[3] = 0.0
        else:
            self.vx = self.vy = 0.0
            ai[1] += 1
            if ai[1] > 90:
                if p2:
                    ai[0] += 1
                lai[2] += 1
                tx = px - 300 * ai[2]
                self.vx, self.vy = (tx - self.x) / 20, (py - self.y) / 20
                ai[1] = 0

    def sword(self, px, py):  # MutantSword :1483
        ai, lai = self.ai, self.lai
        p2 = ai[0] != 9
        ai[3] += ai[2]
        if ai[1] == 20:
            n = 8 if (p2 or self.maso) else 5   # Eternity: P1 5, P2 8
            look = math.copysign(1.0, lai[1]) if lai[1] else 0.0
            arc = math.copysign(1.0, ai[2]) if ai[2] else 0.0
            ox, oy = rot(look, 0.0, math.pi / 4 * arc)
            L = 80 * 12 / 2.0
            sx, sy = self.x + L * ox, self.y + L * oy
            bx, by = norm(px - sx, py - sy)
            for i in range(n):
                ang = math.atan2(by, bx) + TAU / n * i
                a1 = 48 if (i <= 2 or i == n - 2) else 24
                self.spawn_moon_blast(sx + self.rng.uniform(-60, 60), sy + self.rng.uniform(-60, 60), ang, a1, 0)
        ai[1] += 1
        if ai[1] > 25:
            if not p2:
                self.p1_next(9)
            elif self.maso and lai[2] < 3 * 0.5:   # [Maso] 连挥 (endTimeVariance 取 0 近似)
                ai[0] -= 1
                ai[1] = ai[2] = ai[3] = 0.0
                lai[1] = 0.0
            else:
                self.choose_next(13, 21, 24, 29, 31, 33, 37, 41, 42, 44)

    def spawn_moon_blast(self, x, y, ang, a1, mode):
        s = self.rng.uniform(1.5, 4.0)
        self.spawn(K.MOON_BLAST, x, y, tl=10**6, hw=35 * s, hh=35 * s, a0=ang, a1=a1, l0=mode, a2=s)

    # =================================================================== P2  (MB.cs:1553-3392)
    def spawn_ray2(self, ang, dmg_scale=1.0):  # MutantDeathray2: 原地不动, 180 帧, scale>=0.7 才有伤害
        for s_ in (1, -1):
            self.spawn(K.RAY2, self.x, self.y, tl=10**6, col=K.C_RAY, dx=s_ * math.cos(ang), dy=s_ * math.sin(ang),
                       llen=0, lw=0)

    def spawn_spear_aim(self, mode):  # MutantSpearAim.cs: ai1 = mode; >1 预判 (player+vel*30), 否则直瞄 lerp 0.2
        tl = 60 + {-1: 120, 1: -30, 3: 30, 4: 20}.get(mode, 0)
        px, py, _, _ = self.player()
        ang = math.atan2(py - self.y, px - self.x)
        self.spawn(K.SPEAR_AIM, self.x, self.y, tl=tl, col=K.C_SEG, hw=8, hh=8, dx=math.cos(ang), dy=math.sin(ang),
                   llen=200, lw=8, a1=mode)

    def spawn_aim_ray(self, t, px, py, pvx, pvy):  # MutantDeathrayAim (无伤预警, ai0 = 持续帧数)
        ang = math.atan2(py + pvy * 30 - self.y, px + pvx * 30 - self.x)
        self.spawn(K.TELEGRAPH, self.x, self.y, tl=int(t), col=K.C_RAY, hw=0, hh=0, dx=math.cos(ang), dy=math.sin(ang),
                   llen=3000, lw=0, a0=1)

    def spawn_spear_dash(self, spear_ai=0.0):
        nx, ny = norm(self.vx, self.vy)
        self.spawn(K.SPEAR_DASH, self.x, self.y, tl=10**6, col=K.C_SEG, hw=8, hh=8, dx=nx, dy=ny, llen=200, lw=8,
                   a1=spear_ai)

    def p2_transition(self, px, py, pvx, pvy):  # Phase2Transition :1555
        ai, lai = self.ai, self.lai
        self.vx *= 0.9; self.vy *= 0.9
        if ai[1] == 0:
            self.clear_projectiles()
            self.ritual = True
            self.rx, self.ry = self.x, self.y
        elif ai[1] > 150:
            lai[3] = 3.0
        ai[1] += 1
        if ai[1] > 270:
            self.life = LIFE_MAX
            ai[0] = float(self.rng.choice([11, 13, 16, 19, 20, 21, 24, 26, 29, 35, 37, 39, 42]))
            if self.forced is not None:
                ai[0] = float(self.forced)
            ai[1] = ai[2] = 0.0
            self.hist.append(ai[0])

    def approach_p2(self, px, py, pvx, pvy):  # :1659 (11 与 16 共用)
        ai = self.ai
        dx, dy = norm(self.x - px, self.y - py)
        tx, ty = px + dx * 300, py + dy * 300
        if self.dist_to(tx, ty) > 50 and ai[2] + 1 < 180:
            ai[2] += 1
            self.movement(tx, ty, 0.8)
        else:
            ai[0] += 1
            ai[1] = 0
            ai[2] = math.atan2(self.y - py, self.x - px)
            ai[3] = math.pi / 10 * (-1 if px < self.x else 1)
            self.lai[0] = 0

    def void_rays_p2(self, px, py, pvx, pvy):  # :1682
        ai, lai = self.ai, self.lai
        self.vx = self.vy = 0.0
        ai[1] -= 1
        if ai[1] < 0:
            vx, vy = rot(2.0, 0.0, ai[2])
            self.spawn(K.MARK1, self.x, self.y, vx, vy, tl=90, hw=23, hh=23)
            ai[1] = 3
            ai[2] += ai[3]
            old = lai[0]; lai[0] += 1
            if old == 20 or lai[0] == 40:
                ai[2] -= ai[3] / (3 if self.maso else 2)
            elif lai[0] >= 60:
                self.choose_next(13, 19, 21, 24, 31, 39, 41, 42)

    def prep_dash_pred(self, px, py, pvx, pvy):  # :1710
        ai = self.ai
        if ai[3] == 0:
            ai[3] = 1
            self.spawn(K.SPEAR_SPIN, self.x, self.y, tl=180, hw=76, hh=76)
        ai[1] += 1
        if ai[1] > 180:
            ai[0] += 1; ai[1] = 0; ai[3] = 0
        sgn = math.copysign(1.0, self.y - py) if self.y != py else 0.0
        self.movement(px, py + 400 * sgn, 0.7, False)
        if math.hypot(self.x - px, self.y - py) < 200:
            dx, dy = norm(self.x - px, self.y - py)
            self.movement(self.x + dx, self.y + dy, 1.4)

    def dash_pred(self, px, py, pvx, pvy):  # :1745
        ai, lai = self.ai, self.lai
        if lai[1] == 0:
            lai[1] = float(self.rng.randint(3 if self.maso else 5, 8))
        if ai[1] == 0:
            if ai[2] == lai[1] - 1:
                if math.hypot(self.x - px, self.y - py) > 450:
                    self.movement(px, py, 0.6)
                    return
                self.vx *= 0.75; self.vy *= 0.75
            if ai[2] < lai[1]:
                self.spawn_aim_ray(55, px, py, pvx, pvy)
                if ai[2] == lai[1] - 1:
                    self.spawn_spear_aim(4)
        self.vx *= 0.9; self.vy *= 0.9
        if ai[1] < 55:
            lai[0] = math.atan2(py + pvy * 30 - self.y, px + pvx * 30 - self.x)
        end = 80 if ai[2] == lai[1] - 1 else 60
        if self.maso and (ai[2] == 0 or ai[2] >= lai[1]):
            end = 0
        ai[1] += 1
        if ai[1] > end:
            ai[0] += 1; ai[1] = 0; ai[3] = 0
            ai[2] += 1
            if ai[2] > lai[1]:
                self.choose_next(16, 19, 20, 26, 29, 31, 33, 39, 42, 44, 45)
            else:
                self.vx, self.vy = math.cos(lai[0]) * 45, math.sin(lai[0]) * 45
                self.spawn_ray2(lai[0])
                self.spawn_spear_dash(-2.0 if ai[2] == lai[1] else 0.0)
            lai[0] = 0

    def while_dash_p2(self, px, py, pvx, pvy):  # :1828 (15, 23)
        ai, lai = self.ai, self.lai
        if ai[0] == 23 and ai[1] % 3 == 0:
            ai[1] += 1
        self.direction = 1 if self.vx > 0 else -1 if self.vx < 0 else 0
        ai[1] += 1
        if ai[1] > 30:
            ai[0] -= 1
            ai[1] = 0
            if ai[0] == 14 and ai[2] == lai[1] - 1 and math.hypot(self.x - px, self.y - py) > 450:
                dx, dy = self.dir_to(px, py)
                self.vx, self.vy = dx * 16, dy * 16

    def boundary_p2(self, px, py, pvx, pvy):  # :1845
        ai, lai = self.ai, self.lai
        self.vx = self.vy = 0.0
        if lai[0] == 0:
            lai[0] = math.copysign(1.0, self.x - px) if self.x != px else 1.0
            if self.maso:
                ai[2] = self.rng.uniform(0, math.pi)
        if ai[3] > 60:
            ai[1] += 1
            if ai[1] > 2:
                ai[1] = 0
                ai[2] += math.pi / 8 / 480 * ai[3] * lai[0]
                if ai[2] > math.pi:
                    ai[2] -= TAU
                mx = 5 + (1 if self.maso else 0)
                for i in range(mx):
                    vx, vy = rot(0.0, -6.0, ai[2] + TAU / mx * i)
                    self.spawn(K.EYE, self.x, self.y, vx, vy, tl=300, hw=6, hh=6)
        ai[3] += 1
        if ai[3] > 360 + 60:
            self.choose_next(11, 13, 19, 20, 21, 24, 31 if self.maso else 26, 33, 41, 44)

    def pillar_dunk(self, px, py, pvx, pvy):  # :1889
        ai, lai = self.ai, self.lai
        delay = 60
        if ai[2] == 0 and ai[3] == 0:
            # 3 个分身 (MutantIllusion), 分别在 delay*2/3/4 帧召唤柱子
            self.n_ill = 3
            dx, dy = px - self.x, py - self.y
            self.ill = [(self.x, self.y)] + [(px + dx * m1, py + dy * m2) for (m1, m2) in ((1, -1), (1, 1), (-1, 1))]
            ai[2], ai[3] = 1000.0, math.radians(45)   # 仅作为"已初始化"标记 (源码中为偏移长度/角度, 后续未使用)
        tx = px + (-700 if self.x < px else 700)
        ty = py + (400 if ai[1] < 240 else 150)
        if self.dist_to(tx, ty) > 50:
            self.movement(tx, ty, 1.0)
        end = 240 + delay * 4 + 60
        if self.maso:
            end += delay * 2
        lai[0] = end - ai[1]
        lai[0] += 60 + 60 * (1 - ai[1] / end)
        ai[1] += 1
        if ai[1] > end:
            self.n_ill = 0
            self.choose_next(11, 13, 20, 21, 26, 33, 41, 44)
            return
        owner = {delay: 0, delay * 2: 1, delay * 3: 2, delay * 4: 3}.get(int(ai[1]))
        if owner is not None:
            ox, oy = self.ill[owner]
            self.spawn(K.PILLAR, ox, oy, 0.0, 0.0, tl=600, hw=60, hh=60, a0=owner, l0=255, a2=-5.0)
        if self.maso and ai[1] == delay * 5:
            self.spawn(K.PILLAR, self.x, self.y, 0.0, 0.0, tl=600, hw=60, hh=60, a0=0, l0=255, a2=-5.0)

    def eoc_sickles(self, px, py, pvx, pvy):  # EOCStarSickles :1998
        ai = self.ai
        if ai[1] == 0:
            self.spawn(K.EOC, self.x, self.y, 0.0, -1.0, tl=216, hw=40, hh=40)
        if ai[1] < 120:
            ai[2], ai[3] = px, py
        tx, ty = ai[2], ai[3]
        dx, dy = rot(*norm(self.x - tx, self.y - ty), math.radians(-5))
        tx, ty = tx + dx * 450, ty + dy * 450
        if self.dist_to(tx, ty) > 50:
            self.movement(tx, ty, 0.25)
        ai[1] += 1
        if ai[1] > 450:
            self.choose_next(11, 13, 16, 21, 26, 29, 31, 33, 35, 37, 41, 44, 45)

    def prep_dash_direct(self, px, py, pvx, pvy):  # :2083
        ai = self.ai
        if ai[3] == 0:
            ai[3] = 1
            self.spawn(K.SPEAR_SPIN, self.x, self.y, tl=180, hw=76, hh=76)
        ai[1] += 1
        if ai[1] > 180:
            ai[0] += 1; ai[1] = 0; ai[3] = 0
        sgn = math.copysign(1.0, self.y - py) if self.y != py else 0.0
        self.movement(px, py + 450 * sgn, 0.7, False)
        if math.hypot(self.x - px, self.y - py) < 200:
            dx, dy = norm(self.x - px, self.y - py)
            self.movement(self.x + dx, self.y + dy, 1.4)

    def dash_direct(self, px, py, pvx, pvy):  # :2118
        ai, lai = self.ai, self.lai
        self.vx *= 0.9; self.vy *= 0.9
        if lai[1] == 0:
            lai[1] = float(self.rng.randint(3 if self.maso else 5, 8))
        ai[1] += 1
        if ai[1] > 5:
            ai[0] += 1
            ai[1] = 0
            ai[2] += 1
            if ai[2] > lai[1]:
                if self.maso:
                    self.choose_next(11, 13, 16, 19, 20, 31, 33, 35, 39, 42, 44)
                else:
                    self.choose_next(11, 16, 26, 29, 31, 35, 37, 39, 42, 44)
            else:
                dx, dy = self.dir_to(px, py)
                sp = 60.0 if self.maso else 45.0
                self.vx, self.vy = dx * sp, dy * sp
                self.spawn_ray2(math.atan2(dy, dx))
                self.spawn_spear_dash(0.0)

    def spawn_destroyers(self, px, py, pvx, pvy):  # :2157
        ai, lai = self.ai, self.lai
        dx, dy = norm(self.x - px, self.y - py)
        tx, ty = px + dx * 500, py + dy * 500
        if abs(tx - px) < 150:
            tx = px + 150 * (math.copysign(1.0, tx - px) if tx != px else 1.0)
            self.movement(tx, ty, 0.3)
        if self.dist_to(tx, ty) > 50:
            self.movement(tx, ty, 0.9)
        if lai[1] == 0:
            lai[1] = float(self.rng.randint(3 if self.maso else 5, 8))
            lai[2] = float(self.rng.randint(0, 1))
        ai[1] += 1
        if ai[1] > 60:
            ai[1] = 30
            cap = 5 + (2 if self.maso else 0)
            if self.maso:
                ai[1] += 15
            ai[2] += 1
            if ai[2] > cap:
                ai[0] += 1; ai[1] = 0; ai[2] = 0
            else:
                a = math.atan2(dy, dx) + self.rng.uniform(-1, 1) * math.radians(120) / 2   # RotatedByRandom(120°)
                vx, vy = math.cos(a) * 10, math.sin(a) * 10
                spd = 0.8 + 0.4 * ai[2] / 5 + (0.4 if self.maso else 0)
                tl = 30 * (cap - int(ai[2])) + 60 * int(lai[1]) + 30 + int(ai[2]) * 6
                head = self.spawn(K.DESTROYER, self.x, self.y, vx, vy, now=True, tl=tl, hw=21, hh=21, a0=-1, a1=spd)
                prev = int(head[0])
                if prev < 0:
                    return
                for i in range(self.rng.randint(8, 18) + 1):   # bodies + tail
                    r = self.spawn(K.DESTROYER, self.x, self.y, 0.0, 0.0, now=True, tl=tl, hw=15, hh=15, a0=prev, a1=0)
                    if int(r[0]) < 0:
                        break
                    prev = int(r[0])

    def spear_toss_pred(self, px, py, pvx, pvy):  # :2246
        ai, lai = self.ai, self.lai
        tx = px + 500 * (-1 if self.x < px else 1)
        if self.dist_to(tx, py) > 25:
            self.movement(tx, py, 0.8)
        ai[1] += 1
        if ai[1] > 60:
            ai[1] = 0
            attack = True
            ai[2] += 1
            if ai[2] > lai[1]:
                attack = False
                if self.maso:
                    self.choose_next(11, 19, 20, 29, 31, 33, 35, 37, 39, 42, 44, 45)
                else:
                    self.choose_next(11, 19, 20, 26, 26, 26, 29, 31, 33, 35, 37, 39, 42, 44)
            if attack or self.maso:
                dx, dy = self.dir_to(px + pvx * 30, py + pvy * 30)
                self.spawn_ray2(math.atan2(dy, dx))
                self.spawn(K.SPEAR_THROWN, self.x, self.y, dx * 30, dy * 30, tl=180, col=K.C_SEG, hw=8, hh=8,
                           dx=dx, dy=dy, llen=200, lw=8, a1=1)
        elif ai[1] == 1 and (ai[2] < lai[1] or self.maso):
            self.spawn_aim_ray(60, px, py, pvx, pvy)
            self.spawn_spear_aim(2)

    def prep_mech_fan(self, px, py, pvx, pvy):  # :2285
        ai = self.ai
        if ai[1] == 0 and self.maso:
            ai[1] = 31
        if ai[1] < 30:
            dx, dy = rot(*norm(self.x - px, self.y - py), math.radians(15))
            tx, ty = px + dx * 500, py + dy * 500
            if self.dist_to(tx, ty) > 50:
                self.movement(tx, ty, 0.3)
        else:
            tx, ty = px + 600 * (-1 if self.x < px else 1), py
            self.movement(tx, ty, 1.2, False)
        ai[1] += 1
        if ai[1] > 150 or (self.maso and self.dist_to(tx, ty) < 64):
            ai[0] += 1
            ai[1] = ai[2] = ai[3] = 0

    def mech_fan(self, px, py, pvx, pvy):  # MechRayFan :2339
        ai, lai = self.ai, self.lai
        self.vx = self.vy = 0.0
        if ai[2] == 0:
            ai[2] = float(self.rng.choice([-1, 1]))
        end = 60 + 180 + 150
        if ai[3] > (45 if self.maso else 60) and ai[3] < 60 + 180:
            ai[1] += 1
            if ai[1] > 10:
                ai[1] = 0
                rr = math.radians(245) * ai[2] / 80
                tbe = end - int(ai[3])
                for ang, turn in ((8 * ai[2], rr), (-8 * ai[2] + 180, -rr)):
                    a = math.radians(ang)
                    tl = min(270, tbe)
                    self.spawn(K.RAY3, self.x, self.y, tl=tl, col=K.C_RAY, dx=math.cos(a), dy=math.sin(a), lw=0,
                               a0=turn, a2=min(270, tl + 2))
                if self.maso:
                    sx, sy = self.x, self.y + ai[2] * -1200
                    for ang, turn in ((8 * ai[2] + 180, rr), (-8 * ai[2], -rr)):
                        a = math.radians(ang)
                        tl = min(270, tbe)
                        self.spawn(K.RAY3, sx, sy, tl=tl, col=K.C_RAY, dx=math.cos(a), dy=math.sin(a), lw=0,
                                   a0=turn, a2=min(270, tl + 2))
        if ai[3] < 180:
            lai[0] += 1
            if lai[0] > 1:
                lai[0] = 0
                off = self.rng.choice([-1, 1]) * self.rng.uniform(1400, 1800)
                aimx, aimy = self.x, self.y - ai[2] * 600
                ox, oy = rot(0.0, off, self.rng.uniform(-1, 1) * math.radians(15) / 2)
                sx, sy = aimx + ox, aimy + oy
                dx, dy = norm(aimx - sx, aimy - sy)
                self.spawn(K.GUARDIAN, sx, sy, dx * 32, dy * 32, tl=240, hw=35, hh=35)
        ai[3] += 1
        if ai[3] > end:
            self.choose_next(11, 13, 16, 19, 21, 24, 29, 31, 33, 35, 37, 39, 41, 42, 45)

    def prep_fishron1(self, px, py, pvx, pvy):  # :2443
        ai = self.ai
        sgn = math.copysign(1.0, self.y - py) if self.y != py else 0.0
        tx, ty = px, py + 600 * sgn
        self.movement(tx, ty, 1.4, False)
        if ai[1] == 0:
            ai[2] = math.copysign(1.0, self.x - px) if self.x != px else 1.0
        ai[1] += 1
        if ai[1] > 60 or self.dist_to(tx, ty) < 64:
            self.vx, self.vy = 30.0 * ai[2], 0.0
            ai[0] += 1
            ai[1] = 0

    def spawn_fishrons(self, px, py, pvx, pvy):  # SpawnFishrons :2465 (30: 猪鲨, 38: 暗影手)
        ai = self.ai
        self.vx *= 0.97; self.vy *= 0.97
        if ai[1] == 0:
            ai[2] = float(self.rng.randint(0, 1))
        delay, sets = 3, (3 if self.maso else 2)
        hand = ai[0] == 38
        if ai[1] % delay == 0 and ai[1] <= delay * sets:
            mx = int(ai[1]) // delay
            spread = math.pi / 3 / (sets + 1)
            for j in (-1, 1):
                for i in (-mx, mx) if mx else (0,):
                    if ai[2] == 0:
                        ox, oy = rot(0.0, 1.0, spread * i); ox, oy = ox * -450 * j, oy * -450 * j
                    else:
                        ox, oy = rot(1.0, 0.0, spread * i); ox, oy = ox * 475 * j, oy * 475 * j
                    hw = 25 if hand else 45
                    self.spawn(K.FISHRON, self.x, self.y, 0.0, 0.0, tl=240, hw=hw, hh=hw, a0=ox, a1=oy, l1=float(hand))
        ai[1] += 1
        if ai[1] > (60 if self.maso else 120):
            self.choose_next(13, 19, 20, 21, 44 if self.maso else 26, 31, 31, 31, 33, 35, 39, 41, 42, 44)

    def prep_eye_dive_p2(self, px, py, pvx, pvy):  # :2507
        ai = self.ai
        tx = px + 400 * (-1 if self.x < px else 1)
        ty = py + 400
        self.movement(tx, ty, 1.2)
        ai[1] += 1
        if ai[1] > 60:
            self.vx = 30.0 * (1 if self.x - BOSS_W / 2 < px - PLAYER_W / 2 else -1)
            if self.vy > 0:
                self.vy *= -1
            self.vy *= 0.3
            ai[0] += 1
            ai[1] = 0

    def prep_nuke(self, px, py, pvx, pvy):  # :2529
        ai = self.ai
        tx = px + 400 * (-1 if self.x < px else 1)
        ty = py - 400
        self.movement(tx, ty, 1.2, False)
        ai[1] += 1
        if ai[1] > 60:
            g, t = 0.2, (120.0 if self.maso else 180.0)
            dx, dy = (px - self.x) / t, (py - self.y) / t - 0.5 * g * t
            self.spawn(K.NUKE, self.x, self.y, dx, dy, tl=int(t), col=K.C_NONE, hw=10, hh=10, a0=g)
            self.spawn(K.FISH_RITUAL, self.x, self.y - 100, tl=600, col=K.C_INVCIRC, hw=0, hh=0, r=150, llen=1200,
                       l0=255)
            ai[0] += 1
            ai[1] = 0
            if (px - self.x > 0) == (self.vx > 0) and self.vx != 0 and px != self.x:
                self.vx *= -1
            if self.vy < 0:
                self.vy *= -1
            nx, ny = norm(self.vx, self.vy)
            self.vx, self.vy = nx * 3, ny * 3

    def nuke(self, px, py, pvx, pvy):  # Nuke :2567
        ai = self.ai
        if self.y + BOSS_W / 2 < py - PLAYER_H / 2:
            tx, ty = px + 300 * (math.copysign(1.0, self.x - px) if self.x != px else 0.0), py
        else:
            dx, dy = rot(*norm(self.x - px, self.y - py), math.radians(60) * (math.copysign(1.0, px - self.x) if px != self.x else 0))
            tx, ty = self.x + 30 * dx, self.y + 30 * dy
        self.movement(tx, ty, 0.1)
        ms = 3 if self.maso else 2
        sp = math.hypot(self.vx, self.vy)
        if sp > ms:
            self.vx, self.vy = self.vx / sp * ms, self.vy / sp * ms
        if ai[1] > (120 if self.maso else 180):
            sx, sy = self.x, self.y - 100
            safe = 150 + 200
            for _ in range(3):
                while True:   # NextVector2Circular(1200,1200): 圆内均匀
                    ux, uy = self.rng.uniform(-1, 1), self.rng.uniform(-1, 1)
                    if ux * ux + uy * uy <= 1:
                        break
                bx, by = self.x + ux * 1200, self.y + uy * 1200
                if math.hypot(sx - bx, sy - by) < safe:
                    dx, dy = norm(bx - sx, by - sy)
                    rr = self.rng.uniform(safe, 1200)
                    bx, by = sx + dx * rr, sy + dy * rr
                self.spawn(K.BOMB, bx, by, tl=15, col=K.C_CIRCLE, r=200)
        ai[1] += 1
        if ai[1] > 360:
            self.choose_next(11, 13, 16, 19, 24, 26 if self.maso else 29, 31, 35, 37, 39, 41, 42)

    def prep_slime(self, px, py, pvx, pvy):  # :2627
        ai = self.ai
        tx = px + 700 * (-1 if self.x < px else 1)
        ty = py + 200
        self.movement(tx, ty, 2.0)
        ai[2] += 1
        if ai[2] > 30 or (self.maso and self.dist_to(tx, ty) < 64):
            ai[0] += 1
            ai[1] = ai[2] = ai[3] = 0

    def slime_rain(self, px, py, pvx, pvy):  # SlimeRain :2649 (Eternity 非 Maso 分支)
        ai, lai = self.ai, self.lai
        if ai[3] == 0:
            ai[3] = 1
        bx = self.x - 1200
        if ai[1] == 0:
            first = lai[0] == 0
            lai[0] = float(self.rng.randint(5, 8) * 120)
            if first:
                if px < self.x and lai[0] > 1200:
                    lai[0] -= 1200
                elif px > self.x and lai[0] < 1200:
                    lai[0] += 1200
            else:
                if px < self.x and lai[0] < 1200:
                    lai[0] += 1200
                elif px > self.x and lai[0] > 1200:
                    lai[0] -= 1200
            lai[0] += 60
            for i in range(-360, 2761, 120):
                if i + 60 == int(lai[0]):
                    continue
                self.spawn(K.RETICLE, bx + i + 60, self.y, tl=150, col=K.C_NONE, hw=40, hh=40)
        if ai[1] > 120 and ai[1] % 5 == 0:
            rd = math.hypot(self.x - self.rx, self.y - self.ry) if self.ritual else 0.0
            xs, vys = [], []
            for i in range(-360, 2761, 75):
                xo = i + self.rng.randint(0, 74)
                if abs(xo - lai[0]) < 110:
                    continue
                xs.append(bx + xo); vys.append(self.rng.uniform(15, 20) * 2)
            xs += [bx + lai[0] + 110, bx + lai[0] - 110]; vys += [40.0, 40.0]
            import numpy as _np
            self.spawn(K.SLIME, _np.array(xs), _np.full(len(xs), self.y - 1300.0), 0.0, _np.array(vys), tl=90, hw=7, hh=7,
                       a0=rd)
        ai[1] += 1
        if ai[1] > 180:
            ai[1] = 0
        self.vx = self.vy = 0.0
        ai[2] += 1
        if ai[2] > 180 * 3:
            self.choose_next(11, 16, 19, 20, 26 if self.maso else 29, 31, 33, 37, 39, 41, 42, 45)

    def prep_fishron2(self, px, py, pvx, pvy):  # :2894
        ai = self.ai
        tx = px + 400 * (-1 if self.x < px else 1)
        ty = py - 400
        self.movement(tx, ty, 0.9)
        ai[1] += 1
        if ai[1] > 60 or (self.maso and self.dist_to(tx, ty) < 32):
            self.vx = 35.0 * (1 if self.x - BOSS_W / 2 < px - PLAYER_W / 2 else -1)
            self.vy = 10.0
            ai[0] += 1
            ai[1] = 0

    def prep_okuu_p2(self, px, py, pvx, pvy):  # :2917
        ai = self.ai
        dx, dy = norm(self.x - px, self.y - py)
        tx, ty = px + dx * 450, py + dy * 450
        ai[1] += 1
        if ai[1] < 180 and self.dist_to(tx, ty) > 50:
            self.movement(tx, ty, 0.8)
        else:
            ai[0] += 1
            ai[1] = ai[2] = ai[3] = 0

    def okuu_p2(self, px, py, pvx, pvy):  # :2936
        ai = self.ai
        self.vx = self.vy = 0.0
        end = 420
        ai[1] += 1
        if ai[1] > 10 and ai[3] > 60 and ai[3] < end - 60:
            ai[1] = 0
            rotation = math.radians(60) * (ai[3] - 45) / 240 * ai[2]
            mx, sp = (10, 11.0) if self.maso else (9, 10.0)
            self.sphere_ring(mx, sp, -1.0, rotation)
            self.sphere_ring(mx, sp, 1.0, rotation)
        if ai[2] == 0:
            ai[2] = float(self.rng.choice([-1, 1]))
            ai[3] = self.rng.uniform(0, TAU)
        ai[3] += 1
        if ai[3] > end:
            self.choose_next(13, 19, 20, 13 if self.maso else 26, 44 if self.maso else 33, 41, 44)

    def spear_toss_direct_p2_attack(self, px, py):
        dx, dy = self.dir_to(px, py)
        self.spawn_ray2(math.atan2(dy, dx))
        self.spawn(K.SPEAR_THROWN, self.x, self.y, dx * 30, dy * 30, tl=180, col=K.C_SEG, hw=8, hh=8, dx=dx, dy=dy,
                   llen=200, lw=8, a1=0)

    def spear_toss_direct_p2(self, px, py, pvx, pvy):  # :2992
        ai, lai = self.ai, self.lai
        if ai[1] == 0:
            lai[0] = wrap(math.atan2(self.y - py, self.x - px))
            lai[1] = float(self.rng.randint(3 if self.maso else 5, 8))
            if self.maso:
                lai[1] += self.rng.randint(0, 5)
            lai[2] = float(self.rng.choice([-1, 1]))
        a = TAU / 300 * ai[3] * lai[2] + lai[0]
        tx, ty = px + 500 * math.cos(a), py + 500 * math.sin(a)
        if self.dist_to(tx, ty) > 25:
            self.movement(tx, ty, 0.6)
        ai[3] += 1
        ai[1] += 1
        if ai[1] > 180:
            ai[1] = 150
            attack = True
            ai[2] += 1
            if ai[2] > lai[1]:
                self.choose_next(11, 16, 19, 20, 44 if self.maso else 26, 31, 33, 35, 42, 44, 45)
                attack = False
            if attack or self.maso:
                self.spear_toss_direct_p2_attack(px, py)
        elif self.maso and ai[1] == 165:
            self.spear_toss_direct_p2_attack(px, py)
        elif ai[1] == 151:
            if ai[2] > 0 and (ai[2] < lai[1] or self.maso):
                self.spawn_spear_aim(1)
        elif ai[1] == 1:
            self.spawn_spear_aim(-1)

    def prep_twin_rangs(self, px, py, pvx, pvy):  # :3060
        ai = self.ai
        tx = px + 500 * (-1 if self.x < px else 1)
        if self.dist_to(tx, py) > 50:
            self.movement(tx, py, 0.8)
        ai[1] += 1
        if ai[1] > 45:
            ai[0] += 1
            ai[1] = ai[2] = ai[3] = 0

    def twin_rangs(self, px, py, pvx, pvy):  # TwinRangsAndCrystals :3081
        ai, lai = self.ai, self.lai
        self.vx = self.vy = 0.0
        if ai[3] == 0:
            lai[0] = math.atan2(self.y - py, self.x - px)
        rdelay, rmax = (12, 5) if self.maso else (15, 4)
        if ai[3] % rdelay == 0 and ai[3] < rdelay * rmax:
            ro = TAU / rmax * ai[3] / rdelay + lai[0]
            base = 60
            fly = 120 + ai[3] / rdelay * (40 if self.maso else 50)
            m = self.spawn(K.MARK2, self.x, self.y, 300.0 / base * math.cos(ro), 300.0 / base * math.sin(ro), now=True,
                           tl=900, col=K.C_NONE, hw=16, hh=16, a0=base, a1=base + fly)
            mi = int(m[0])
            if mi >= 0:
                for i in range(5):
                    my = TAU / 5 * i + ro
                    self.spawn(K.LEAF, self.x + 125 * math.cos(my), self.y + 125 * math.sin(my), tl=900, hw=10, hh=10,
                               a0=mi, a1=my)
        if ai[3] > 45:
            ai[1] -= 1
            if ai[1] < 0:
                ai[1] = 20
                ai[2] = -1.0 if ai[2] > 0 else 1.0
                if ai[3] < 330:
                    rr, sr = 525.0, 350.0
                    rs, ss = TAU * rr / 300, TAU * sr / 180
                    racc, sacc = rs * rs / rr * ai[2], ss * ss / sr * -ai[2]
                    off = math.pi / 4 if self.maso else 0.0
                    for i in range(4):
                        a1 = math.pi / 2 * i + off
                        a2 = a1 + math.pi / 4
                        self.spawn(K.RANG, self.x, self.y, rs * math.cos(a1), rs * math.sin(a1), tl=10**6, hw=25, hh=25,
                                   a0=racc, a1=300)
                        self.spawn(K.RANG, self.x, self.y, ss * math.cos(a2), ss * math.sin(a2), tl=10**6, hw=25, hh=25,
                                   a0=sacc, a1=180)
        ai[3] += 1
        if ai[3] > 450:
            self.choose_next(11, 13, 16, 21, 24, 26, 29, 31, 33, 35, 39, 41, 44, 45)

    def lance(self, x, y, ang, ai1, vx, vy):  # Sword(): FairyQueenLance 生成于 pos - vel*60
        self.spawn(K.LANCE, x - vx * 60, y - vy * 60, vx, vy, tl=10**6, col=K.C_SEG, hw=0, hh=0, dx=math.cos(ang),
                   dy=math.sin(ang), llen=LANCE_LEN, lw=LANCE_W, a0=ang, a1=ai1)

    def empress_swords(self, px, py, pvx, pvy):  # EmpressSwordWave :3154
        ai, lai = self.ai, self.lai
        self.vx = self.vy = 0.0
        thr = 48 if self.maso else 60
        times, startup = 4, 90
        if ai[1] == 0:
            ai[3] = self.rng.uniform(0, TAU)
        if startup <= ai[1] < startup + thr * times:
            ai[2] -= 1
            if ai[2] < 0:
                ai[2] = thr
                if abs(wrap(math.atan2(self.y - py, self.x - px) - ai[3])) > math.pi / 2:
                    ai[3] += math.pi
                mx = 16 if self.maso else 12
                gap = float((1600 * 2) // mx)
                aa = ai[3]
                ux, uy = math.cos(aa), math.sin(aa)
                fx, fy = px, py
                i = 0.0
                while i < 1200:
                    nfx, nfy = fx + gap * ux, fy + gap * uy
                    if math.hypot(self.x - nfx, self.y - nfy) > math.hypot(self.x - fx, self.y - fy):
                        break
                    fx, fy = nfx, nfy
                    i += gap
                sd = 0.0
                while sd < 1200:
                    sd += gap
                mirror = 2 * math.sqrt(2 * sd * sd)
                cnt = 0
                for k in range(-mx, mx + 1):
                    sx = fx - ux * sd + (-uy) * gap * k * -1
                    sy = fy - uy * sd + (ux) * gap * k * -1
                    # spawnOffset.RotatedBy(pi/2) = (-(-uy), -ux) -> (uy, -ux)
                    sx = fx - ux * sd + uy * gap * k
                    sy = fy - uy * sd - ux * gap * k
                    a1 = cnt / (mx * 2.0 + 1); cnt += 1
                    ra = self.rng.uniform(0, TAU)
                    rvx, rvy = math.cos(ra), math.sin(ra)
                    if self.maso:
                        rvx, rvy = rvx * 2, rvy * 2
                    self.lance(sx, sy, aa + math.pi / 4, a1, rvx, rvy)
                    self.lance(sx, sy, aa - math.pi / 4, a1, rvx, rvy)
                    if self.maso:
                        for sg in (1, -1):
                            b = aa + sg * math.pi / 4
                            self.lance(sx + mirror * math.cos(b), sy + mirror * math.sin(b), b + math.pi, a1, rvx, rvy)
                ai[3] += math.pi / 4 * self.rng.choice([-1, 1]) + self.rng.uniform(0, math.pi / 8) * self.rng.choice([-1, 1])

        def mega(tx, ty):
            safe = ai[3]
            sr = math.radians(10)
            for i in range(60):
                ro = self.rng.uniform(sr, math.pi - sr)
                d = self.rng.uniform(600, 2400)
                ox, oy = d * math.cos(safe + ro), d * math.sin(safe + ro)
                if self.rng.random() < 0.5:
                    ox, oy = -ox, -oy
                sx, sy = tx + ox, ty + oy
                vx, vy = (tx - sx) / 60, (ty - sy) / 60
                self.lance(sx, sy, math.atan2(vy, vx), i / 60, -vx * 0.75, -vy * 0.75)
        swarm = startup + thr * times + 40
        if ai[1] == swarm:
            mega(px, py)
            lai[0], lai[1] = px, py
        if self.maso and ai[1] == swarm + 30:
            for sg in (-1, 1):
                mega(lai[0] + 600 * sg * math.cos(ai[3]), lai[1] + 600 * sg * math.sin(ai[3]))
        ai[1] += 1
        if ai[1] > swarm + (60 if self.maso else 30):
            self.choose_next(11, 13, 16, 21, 26 if self.maso else 24, 29, 31, 35, 37, 39, 41, 45)

    def sword_p2_prep(self, px, py, pvx, pvy):
        self.prep_sword(px, py)

    def sword_p2(self, px, py, pvx, pvy):
        self.sword(px, py)

    def next_pause(self, px, py, pvx, pvy):  # P2NextAttackPause :3359
        ai = self.ai
        dx, dy = norm(self.x - px, self.y - py)
        tx, ty = px + dx * 400, py + dy * 400
        self.movement(tx, ty, 0.3)
        if self.dist_to(tx, ty) > 200:
            self.movement(tx, ty, 0.3)
        ai[1] += 1
        if ai[1] > 60 or (self.dist_to(tx, ty) < 200 and ai[1] > (15 if self.lai[3] >= 3 else 30)):
            m = 0.25 if self.maso else 0.75
            self.vx *= m; self.vy *= m
            ai[0] = ai[2]
            ai[1] = ai[2] = 0

    # =================================================================== P3  (MB.cs:3395-3955)
    def p3_transition(self, px, py, pvx, pvy):  # Phase3Transition :3395 (返回 False = 本帧不掉血)
        ai, lai = self.ai, self.lai
        lai[3] = 3.0
        drain = True
        if ai[1] == 0:
            self.life = LIFE_MAX
            self.clear_projectiles()
        ai[1] += 1
        if ai[1] > 480:
            drain = False
            tx, ty = px, py - 300
            self.movement(tx, ty, 1.0, True, False)
            if self.dist_to(tx, ty) < 50 or ai[1] > 720:
                self.vx = self.vy = 0.0
                lai[0] = 0
                ai[0] -= 1
                ai[1] = 0
                ai[2] = math.atan2(self.y - py, self.x - px)
                ai[3] = math.pi / 20 * (-1 if px < self.x else 1)
        else:
            self.vx *= 0.9; self.vy *= 0.9
        return drain

    def void_rays_p3(self, px, py, pvx, pvy):  # :3481
        ai, lai = self.ai, self.lai
        ai[1] -= 1
        if ai[1] < 0:
            sp = 4.0 if (self.maso and lai[0] <= 40) else 2.0
            vx, vy = rot(sp, 0.0, ai[2])
            self.spawn(K.MARK1, self.x, self.y, vx, vy, tl=90, hw=23, hh=23)
            ai[1] = 1
            ai[2] += ai[3]
            old = lai[0]; lai[0] += 1
            if old == 40 or lai[0] == 80 or lai[0] == 120:
                ai[2] -= ai[3] / (3 if self.maso else 2)
            elif lai[0] >= (160 if self.maso else 120):
                self.p3_next()
        self.vx = self.vy = 0.0

    def okuu_p3(self, px, py, pvx, pvy):  # :3526
        ai = self.ai
        if ai[2] == 0:
            ai[2] = float(self.rng.choice([-1, 1]))
            ai[3] = self.rng.uniform(0, TAU)
        end = 360 + 120 + (360 if self.maso else 0)
        ai[1] += 1
        if ai[1] > 10 and ai[3] > 60 and ai[3] < end - 120:
            ai[1] = 0
            rotation = math.radians(45) * (ai[3] - 60) / 240 * ai[2]
            mx, sp = (11, 11.0) if self.maso else (10, 10.0)
            self.sphere_ring(mx, sp, -0.75, rotation)
            self.sphere_ring(mx, sp, 0.75, rotation)
        ai[3] += 1
        if ai[3] > end:
            self.p3_next()
        self.vx = self.vy = 0.0

    def boundary_p3(self, px, py, pvx, pvy):  # :3580
        ai, lai = self.ai, self.lai
        if lai[0] == 0:
            lai[0] = math.copysign(1.0, self.x - px) if self.x != px else 1.0
        ai[1] += 1
        if ai[1] > 3:
            ai[1] = 0
            ai[2] += math.pi / 5 / 420 * ai[3] * lai[0] * (2 if self.maso else 1)
            if ai[2] > math.pi:
                ai[2] -= TAU
            mx = 10 if self.maso else 8
            for i in range(mx):
                vx, vy = rot(0.0, -6.0, ai[2] + TAU / mx * i)
                self.spawn(K.EYE, self.x, self.y, vx, vy, tl=300, hw=6, hh=6)
        end = 360 + (360 if self.maso else 0)
        ai[3] += 1
        if ai[3] > end:
            self.p3_next()
        self.vx = self.vy = 0.0

    def final_spark(self, px, py, pvx, pvy):  # FinalSpark :3642 (非 Maso)
        ai, lai = self.ai, self.lai
        ai[1] += 1
        if ai[1] > 120:
            ai[1] = 0
            self.sphere_ring(10, 6.0, 0.5)
            self.sphere_ring(10, 6.0, -0.5)
        if ai[2] == 0:
            if not self.maso:
                lai[1] = 1
        if ai[2] < 420:
            if lai[1] == 0 or ai[2] > 420 - 90:
                ai[3] = math.atan2(self.y - py, self.x - px)      # DirectionFrom(player)
        end = 1020 + (180 if self.maso else 0)
        ai[2] += 1
        if ai[2] > end and (self.life <= 1 or self.forced is not None):
            if self.forced is not None:
                self.p3_next()
                self.clear_projectiles()
            else:
                ai[0] -= 1
                ai[1] = ai[2] = 0
                self.clear_projectiles()
            self.vx = self.vy = 0.0
            return
        elif ai[2] == 420:
            ai[3] += math.radians(20) * (1 if self.maso else -1)
            self.spawn(K.GIANT_RAY, self.x, self.y, tl=10**6, col=K.C_RAY, dx=math.cos(ai[3]), dy=math.sin(ai[3]), lw=0)
        # SpinLaser
        new = math.atan2(py - self.y, px - self.x)
        diff = wrap(new - ai[3])
        rd = TAU / 6 / 60
        ai[3] += min(rd, abs(diff)) * (1 if diff > 0 else -1 if diff < 0 else 0)
        self.vx = self.vy = 0.0

    def dying_pause(self, px, py, pvx, pvy):  # :3870 (之后全部 0 伤害)
        ai = self.ai
        ai[3] -= math.pi / 6 / 60
        self.vx = self.vy = 0.0
        ai[1] += 1
        if ai[1] > 120:
            ai[0] -= 1
            ai[1] = ai[2] = 0
            ai[3] = -math.pi / 2

    def dying_anim(self, px, py, pvx, pvy):
        self.vx = self.vy = 0.0


Boss.DISPATCH = {0: 'spear_toss', 1: 'okuu', 2: 'prep_eye_dive', 3: 'eye_dive', 4: 'prep_dash', 5: 'dash', 6: 'while_dash',
                 7: 'approach', 8: 'void_rays', 9: 'boundary_sword', 10: 'p2_transition',
                 11: 'approach_p2', 12: 'void_rays_p2', 13: 'prep_dash_pred', 14: 'dash_pred', 15: 'while_dash_p2',
                 16: 'approach_p2', 17: 'boundary_p2', 19: 'pillar_dunk', 20: 'eoc_sickles', 21: 'prep_dash_direct',
                 22: 'dash_direct', 23: 'while_dash_p2', 24: 'spawn_destroyers', 25: 'spear_toss_pred',
                 26: 'prep_mech_fan', 27: 'mech_fan', 29: 'prep_fishron1', 30: 'spawn_fishrons', 31: 'prep_eye_dive_p2',
                 32: 'eye_dive', 33: 'prep_nuke', 34: 'nuke', 35: 'prep_slime', 36: 'slime_rain', 37: 'prep_fishron2',
                 38: 'spawn_fishrons', 39: 'prep_okuu_p2', 40: 'okuu_p2', 41: 'spear_toss_direct_p2',
                 42: 'prep_twin_rangs', 43: 'twin_rangs', 44: 'empress_swords', 45: 'sword_p2_prep', 46: 'sword_p2',
                 52: 'next_pause', -1: 'p3_transition', -2: 'void_rays_p3', -3: 'okuu_p3', -4: 'boundary_p3',
                 -5: 'final_spark', -6: 'dying_pause', -7: 'dying_anim'}
