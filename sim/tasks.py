"""课程任务定义与采样 (可 pickle, 供多进程 worker 使用)。"""
import numpy as np
from .boss import P1_ATTACKS, P2_ATTACKS, P3_ATTACKS, ATTACK_NAMES


def auto_stages(cfg):
    """per-attack (P1, P2, P3) -> 分阶段 -> 完整战斗。"""
    cyc = cfg.get('cycles', 2)
    mt_atk = cfg.get('attack_max_ticks', 6000)
    st = []
    for ph, lst in ((1, P1_ATTACKS), (2, P2_ATTACKS), (3, P3_ATTACKS)):
        for a in lst:
            st.append(dict(name=f'P{ph}:{a}:{ATTACK_NAMES[a]}', phase=ph, forced=a, cycles=cyc, max_ticks=mt_atk))
    st.append(dict(name='PHASE1', phase=1, reach=2, max_ticks=cfg.get('p1_max_ticks', 6000)))
    st.append(dict(name='PHASE2', phase=2, reach=3, max_ticks=cfg.get('p2_max_ticks', 15000)))
    st.append(dict(name='PHASE3', phase=3, reach=4, max_ticks=cfg.get('p3_max_ticks', 6000)))
    st.append(dict(name='FULL', phase=1, reach=4, max_ticks=cfg.get('full_max_ticks', 26000)))
    return st


class TaskSampler:
    def __init__(self, stages, weights, seed=0):
        self.stages = stages
        self.w = np.asarray(weights, np.float64) / np.sum(weights)
        self.rng = np.random.default_rng(seed)

    def __call__(self, e):
        i = int(self.rng.choice(len(self.stages), p=self.w))
        t = dict(self.stages[i]); t['id'] = i
        return t


def stage_weights(n, cur, replay):
    w = np.zeros(n)
    if cur == 0 or replay <= 0:
        w[cur] = 1.0
    else:
        w[cur] = 1 - replay
        w[:cur] = replay / cur
    return w
