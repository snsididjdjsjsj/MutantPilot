"""多进程向量环境: 把 n_envs 拆给 W 个 worker 进程 (每个持有一个 MutantVecEnv), 用共享内存交换 obs/action。"""
import multiprocessing as mp
import numpy as np
from multiprocessing import shared_memory
from .env import MutantVecEnv, OBS_DIM
from .sim import N_ACTIONS


def _worker(rank, n, kw, shm_name, total, off, pipe):
    env = MutantVecEnv(n, **kw)
    shm = shared_memory.SharedMemory(name=shm_name)
    obs = np.ndarray((total, OBS_DIM), np.float32, buffer=shm.buf)
    while True:
        cmd, arg = pipe.recv()
        if cmd == 'step':
            o, r, te, tr, info = env.step(arg)
            obs[off:off + n] = o
            pipe.send((r, te, tr, info))
        elif cmd == 'reset':
            obs[off:off + n] = env.reset()
            pipe.send(None)
        elif cmd == 'sampler':
            env.task_fn = arg
            pipe.send(None)
        elif cmd == 'proj':
            pipe.send(int(env.sim.proj.active.sum()))
        elif cmd == 'close':
            shm.close()
            pipe.send(None)
            break


class ParallelVecEnv:
    def __init__(self, n_envs, n_workers=4, seed=0, **kw):
        self.N = n_envs
        sizes = [n_envs // n_workers + (1 if i < n_envs % n_workers else 0) for i in range(n_workers)]
        self.offs = np.r_[0, np.cumsum(sizes)]
        self.shm = shared_memory.SharedMemory(create=True, size=n_envs * OBS_DIM * 4)
        self.obs = np.ndarray((n_envs, OBS_DIM), np.float32, buffer=self.shm.buf)
        ctx = mp.get_context('fork')
        self.pipes, self.procs = [], []
        for i, n in enumerate(sizes):
            a, b = ctx.Pipe()
            p = ctx.Process(target=_worker, args=(i, n, dict(kw, seed=seed + 1000 * i), self.shm.name, n_envs,
                                                  int(self.offs[i]), b), daemon=True)
            p.start(); self.pipes.append(a); self.procs.append(p)

    def set_sampler(self, make):
        """make(rank) -> TaskSampler"""
        for i, p in enumerate(self.pipes):
            p.send(('sampler', make(i)))
        for p in self.pipes:
            p.recv()

    def reset(self):
        for p in self.pipes:
            p.send(('reset', None))
        for p in self.pipes:
            p.recv()
        return self.obs.copy()

    def step(self, actions):
        for i, p in enumerate(self.pipes):
            p.send(('step', actions[self.offs[i]:self.offs[i + 1]]))
        res = [p.recv() for p in self.pipes]
        r = np.concatenate([x[0] for x in res]); te = np.concatenate([x[1] for x in res]); tr = np.concatenate([x[2] for x in res])
        info = {}
        if any(x[3] for x in res):
            keys = next(x[3] for x in res if x[3]).keys()
            for k in keys:
                parts = []
                for i, x in enumerate(res):
                    n = self.offs[i + 1] - self.offs[i]
                    if x[3]:
                        parts.append(x[3][k])
                    else:
                        parts.append(np.zeros(n, bool) if k == 'done' else np.zeros(n, np.float32))
                info[k] = np.concatenate(parts)
        return self.obs.copy(), r, te, tr, info

    def proj_alive(self):
        for p in self.pipes:
            p.send(('proj', None))
        return sum(p.recv() for p in self.pipes)

    def close(self):
        for p in self.pipes:
            try:
                p.send(('close', None)); p.recv()
            except Exception:
                pass
        for p in self.procs:
            p.join(timeout=5)
        self.shm.close(); self.shm.unlink()
