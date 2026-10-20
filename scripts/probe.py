"""每个攻击: 固定策略(静止/随机)存活时长 + 死因统计 + 速度测试"""
import sys, time, collections, numpy as np
sys.path.insert(0, '.')
from sim import MutantSim
from sim.projectiles import KIND_NAMES
from sim.boss import P1_ATTACKS, P2_ATTACKS, P3_ATTACKS
    for atk in P1_ATTACKS + P2_ATTACKS + P3_ATTACKS:
    for pol in ['idle', 'random']:
        sim = MutantSim(32, forced_attack=atk, seed=1); sim.reset_all()
        alive = np.ones(32, bool); death = np.full(32, 1200); cause = collections.Counter()
        for t in range(1200):
            a = np.full(32, 1) if pol == 'idle' else np.random.randint(0, 48, 32)
            h = sim.step(a)
            new = h & alive
            for e in np.flatnonzero(new):
                cause[KIND_NAMES.get(int(sim.hit_kind[e]), 'AURA')] += 1
            death[new] = t; alive &= ~h
        print(f'atk {atk} {pol:6s} mean_survive={death.mean():6.0f} survived1200={alive.mean():.2f} causes={dict(cause)} maxproj={sim.proj.active.reshape(32,-1).sum(1).max()} dropped={sim.proj.dropped}')
