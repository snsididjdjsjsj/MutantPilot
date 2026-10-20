"""渲染一局到 MP4: python scripts/render_episode.py --attack 0 --policy scripted|random|idle|ckpt.pt --out out.mp4"""
import argparse, sys, numpy as np, torch
sys.path.insert(0, '.')
from sim import MutantVecEnv
from sim.render import render_frame, VideoWriter
from sim.scripted import plan
ap = argparse.ArgumentParser()
ap.add_argument('--attack', type=int, default=None); ap.add_argument('--policy', default='scripted')
ap.add_argument('--out', default='episode.mp4'); ap.add_argument('--ticks', type=int, default=1500)
ap.add_argument('--every', type=int, default=1)
ap.add_argument('--phase', type=int, default=1); ap.add_argument('--keep_going', action='store_true', help='受击不结束 (检查用)')
a = ap.parse_args()
env = MutantVecEnv(1, forced_attack=a.attack, seed=3, phase=a.phase, no_hit=not a.keep_going,
                   max_ticks=10**6 if a.keep_going else None)
obs = env.reset()
net = None
if a.policy.endswith('.pt'):
    from train import Agent
    ck = torch.load(a.policy, map_location='cpu', weights_only=False); net = Agent(ck['cfg']['hidden']); net.load_state_dict(ck['model']); net.eval()
vw = VideoWriter(a.out)
for t in range(a.ticks):
    if net is not None:
        with torch.no_grad():
            act = net.act(torch.as_tensor(obs), greedy=True).numpy()
    elif a.policy == 'scripted':
        act = plan(env.sim)
    elif a.policy == 'idle':
        act = np.array([1])
    else:
        act = np.random.randint(0, 48, 1)
    obs, r, te, tr, info = env.step(act)
    if t % a.every == 0:
        vw.write(render_frame(env.sim, 0, hit=bool(info and info['hit'][0])))
    if info and info['done'][0]:
        print('episode end at tick', info['ticks'][0], 'hit', info['hit'][0], 'win', info['win'][0])
        break
vw.close(); print('wrote', a.out)
