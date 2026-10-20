"""吞吐测试: python scripts/bench.py [n_envs...] [--workers W]"""
import sys, time, numpy as np
sys.path.insert(0, '.')
from sim import MutantVecEnv
from sim.parallel import ParallelVecEnv
args = [x for x in sys.argv[1:] if not x.startswith('--')]
W = int(next((x.split('=')[1] for x in sys.argv[1:] if x.startswith('--workers=')), 0))
for n in [int(x) for x in args] or [256, 512]:
    for atk in [None, 9, 36, 43, -3]:
        kw = dict(forced_attack=atk, max_ticks=100000, no_hit=False)
        env = ParallelVecEnv(n, W, seed=0, **kw) if W > 1 else MutantVecEnv(n, seed=0, **kw)
        env.reset()
        for _ in range(300):  # warm up: 让弹幕铺满
            env.step(np.random.randint(0, 48, n))
        t = time.time(); steps = 200
        for _ in range(steps):
            env.step(np.random.randint(0, 48, n))
        dt = time.time() - t
        alive = env.proj_alive() if W > 1 else env.sim.proj.active.sum()
        print(f'n_envs={n:4d} workers={W} attack={atk}  env-steps/s={n*steps/dt:8.0f}  (proj alive avg {alive/n:.0f}/env)', flush=True)
        if W > 1:
            env.close()
