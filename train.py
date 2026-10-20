"""CleanRL 风格向量化 PPO (单卡)。
  python train.py --config configs/default.json      # 自动从 out_dir/latest.pt 续训
观测中的弹幕集合用 DeepSets (共享 MLP + max/mean pooling) 编码, 与顺序无关。
"""
import argparse, json, os, time, sys
import numpy as np
import torch
import torch.nn as nn
from torch.distributions import Categorical
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from sim import MutantVecEnv, N_ACTIONS
from sim.env import BASE_DIM, K_HAZ, HAZ_F, OBS_DIM
from sim.parallel import ParallelVecEnv
from sim.tasks import auto_stages, TaskSampler, stage_weights

DEFAULTS = json.load(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'configs/default.json')))


class Agent(nn.Module):
    def __init__(self, hidden=256):
        super().__init__()
        self.haz = nn.Sequential(nn.Linear(HAZ_F, 128), nn.ReLU(), nn.Linear(128, 128), nn.ReLU())
        self.base = nn.Sequential(nn.Linear(BASE_DIM, 128), nn.ReLU())
        self.trunk = nn.Sequential(nn.Linear(128 + 256, hidden), nn.ReLU(), nn.Linear(hidden, hidden), nn.ReLU())
        self.pi = nn.Linear(hidden, N_ACTIONS)
        self.v = nn.Linear(hidden, 1)
        nn.init.orthogonal_(self.pi.weight, 0.01); nn.init.zeros_(self.pi.bias)

    def features(self, obs):
        b = self.base(obs[:, :BASE_DIM])
        h = obs[:, BASE_DIM:].view(-1, K_HAZ, HAZ_F)
        valid = h[..., :1]
        e = self.haz(h) * valid
        mx = (e - (1 - valid) * 1e4).max(1).values.clamp(min=0)
        mean = e.sum(1) / valid.sum(1).clamp(min=1)
        return self.trunk(torch.cat([b, mx, mean], -1))

    def forward(self, obs):
        f = self.features(obs)
        return self.pi(f), self.v(f).squeeze(-1)

    @torch.no_grad()
    def act(self, obs, greedy=False):
        logits, _ = self(obs)
        return logits.argmax(-1) if greedy else Categorical(logits=logits).sample()


class PolicyOnly(nn.Module):  # for ONNX export
    def __init__(self, a):
        super().__init__(); self.a = a

    def forward(self, obs):
        return self.a(obs)[0]


def atomic_save(obj, path):
    tmp = path + '.tmp'
    torch.save(obj, tmp)
    os.replace(tmp, path)


class Curriculum:
    """自动课程: 当前阶段最近 window 局生存率 >= threshold (且至少 min_episodes 局) 或该阶段训练步数超过
    stage_max_steps 时进入下一阶段; replay 比例的 env 从之前的阶段均匀采样, 防止遗忘。"""

    def __init__(self, c, stages):
        self.c, self.stages = c, stages
        self.stage, self.stage_start = 0, 0
        self.hist = [[] for _ in stages]          # 每个任务的最近结果 (1=存活)
        self.total = [0] * len(stages)
        self.advanced = []                        # (stage, step, reason)

    def record(self, task, survived):
        for tk, sv in zip(task, survived):
            h = self.hist[tk]; h.append(int(sv)); self.total[tk] += 1
            if len(h) > self.c['window']:
                del h[0]

    def rate(self, i):
        h = self.hist[i]
        return float(np.mean(h)) if h else None

    def maybe_advance(self, step):
        if self.stage >= len(self.stages) - 1:
            return False
        h = self.hist[self.stage]
        r = self.rate(self.stage)
        reason = None
        if len(h) >= self.c['min_episodes'] and r >= self.c['threshold']:
            reason = f'survival {r:.3f}'
        elif step - self.stage_start >= self.c['stage_max_steps']:
            reason = 'max_steps'
        if reason:
            self.advanced.append((self.stage, step, reason))
            self.stage += 1; self.stage_start = step
            return True
        return False

    def weights(self):
        return stage_weights(len(self.stages), self.stage, self.c['replay'])

    def state(self):
        return dict(stage=self.stage, stage_start=self.stage_start, hist=self.hist, total=self.total, advanced=self.advanced)

    def load(self, s):
        self.stage, self.stage_start = s['stage'], s['stage_start']
        self.hist, self.total, self.advanced = s['hist'], s['total'], [tuple(x) for x in s['advanced']]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--config', default=None)
    ap.add_argument('--resume', default=None, help='指定 checkpoint; 默认自动从 out_dir/latest.pt 续训')
    ap.add_argument('--fresh', action='store_true', help='忽略已有 latest.pt, 从头训练')
    ap.add_argument('--allow-uncalibrated-sim', action='store_true',
                    help='显式允许在未校准的旧模拟器上做研究实验；其结果不能证明实战击杀能力')
    args, extra = ap.parse_known_args()
    if not args.allow_uncalibrated_sim:
        raise SystemExit('旧模拟器按固定 DPS 自动扣 Boss 血，尚未校准实战物理，默认禁止继续烧算力。'
                         '当前主线是 mod/MutantPilot 的实战规划器。'
                         '仅研究旧模拟器时可显式添加 --allow-uncalibrated-sim。')
    cfg = dict(DEFAULTS)
    if args.config:
        cfg.update(json.load(open(args.config)))
    for kv in extra:  # 覆盖: --key=value (value 为 JSON)
        if '=' in kv:
            k, v = kv.lstrip('-').split('=', 1); cfg[k] = json.loads(v)
    dev = torch.device(cfg['device'] if (cfg['device'] != 'cuda' or torch.cuda.is_available()) else 'cpu')
    torch.manual_seed(cfg['seed']); np.random.seed(cfg['seed'])
    out = cfg['out_dir']
    os.makedirs(out, exist_ok=True)
    json.dump(cfg, open(os.path.join(out, 'config.json'), 'w'), indent=1)
    N, T = cfg['n_envs'], cfg['n_steps']
    cc = dict(threshold=0.9, window=200, min_episodes=100, stage_max_steps=10_000_000, replay=0.25)
    cc.update(cfg['curriculum'])
    stages = auto_stages(cc) if cc.get('stages', 'auto') == 'auto' else cc['stages']
    if cc.get('only'):
        stages = [s for s in stages if any(s['name'].startswith(p) for p in cc['only'])]
    cur = Curriculum(cc, stages)
    env_kw = dict(frame_skip=cfg['frame_skip'], maso=cfg['maso'], random_start=cfg['random_start'],
                  player_dps=cfg.get('player_dps', 25000.0))
    if cfg.get('n_workers', 0) > 1:
        env = ParallelVecEnv(N, cfg['n_workers'], seed=cfg['seed'], **env_kw)
    else:
        env = MutantVecEnv(N, seed=cfg['seed'], **env_kw)
    agent = Agent(cfg['hidden']).to(dev)
    opt = torch.optim.Adam(agent.parameters(), lr=cfg['lr'], eps=1e-5)
    gpu_name = torch.cuda.get_device_name(0) if dev.type == 'cuda' else 'cpu'
    print(f'[device] train_device={dev} cuda_available={torch.cuda.is_available()} '
          f'gpu={gpu_name} param_device={next(agent.parameters()).device} '
          f'n_envs={N} n_workers={cfg.get("n_workers", 0)}', flush=True)
    gstep = 0
    latest = os.path.join(out, 'latest.pt')
    rp = args.resume or (latest if os.path.exists(latest) and not args.fresh else None)
    if rp:
        ck = torch.load(rp, map_location=dev, weights_only=False)
        agent.load_state_dict(ck['model']); opt.load_state_dict(ck['opt']); gstep = ck['step']
        if 'curriculum' in ck:
            cur.load(ck['curriculum'])
        print(f'[resume] {rp} step={gstep} stage={cur.stage} ({stages[cur.stage]["name"]})')

    def push_sampler():
        w = cur.weights()
        env.set_sampler(lambda rank: TaskSampler(stages, w, seed=cfg['seed'] * 7919 + rank + gstep))
    push_sampler()
    obs = torch.as_tensor(env.reset(), device=dev)
    log = open(os.path.join(out, 'log.jsonl'), 'a')
    csv_path = os.path.join(out, 'progress.csv')
    new_csv = not os.path.exists(csv_path)
    csvf = open(csv_path, 'a')
    if new_csv:
        csvf.write('time,step,stage,stage_name,stage_survival,stage_episodes,sps,ep_ret,ep_ticks\n')

    def save(tag=None):
        ck = dict(model=agent.state_dict(), opt=opt.state_dict(), step=gstep, cfg=cfg, curriculum=cur.state(),
                  stages=stages)
        atomic_save(ck, latest)
        if tag:
            atomic_save(ck, os.path.join(out, f'ckpt_{tag}.pt'))
        json.dump(dict(step=gstep, time=time.strftime('%Y-%m-%d %H:%M:%S'), stages=stages, **cur.state()),
                  open(os.path.join(out, 'curriculum.json.tmp'), 'w'))
        os.replace(os.path.join(out, 'curriculum.json.tmp'), os.path.join(out, 'curriculum.json'))

    # ---- 可选: 用脚本规划器做行为克隆预热 (单进程环境时)
    if cfg.get('bc_steps', 0) and not rp and isinstance(env, MutantVecEnv):
        from sim.scripted import plan
        bopt = torch.optim.Adam(agent.parameters(), lr=1e-3)
        o = env.reset()
        for i in range(cfg['bc_steps']):
            a = plan(env.sim)
            logits, _ = agent(torch.as_tensor(o, device=dev))
            loss = nn.functional.cross_entropy(logits, torch.as_tensor(a, device=dev))
            bopt.zero_grad(); loss.backward(); bopt.step()
            o, *_ = env.step(a)
        print(f'[bc] done {cfg["bc_steps"]} steps, loss={loss.item():.3f}')
        obs = torch.as_tensor(env.reset(), device=dev)

    bo = torch.zeros((T, N, OBS_DIM), device=dev); ba = torch.zeros((T, N), dtype=torch.long, device=dev)
    blp = torch.zeros((T, N), device=dev); br = torch.zeros((T, N), device=dev)
    bd = torch.zeros((T, N), device=dev); bv = torch.zeros((T, N), device=dev)
    rets, ticks = [], []
    t0 = time.time(); last_save = gstep; last_save_t = time.time(); last_ck = gstep
    try:
        while gstep < cfg['total_steps']:
            frac = 1 - gstep / cfg['total_steps']
            for g in opt.param_groups:
                g['lr'] = cfg['lr'] * max(frac, 0.05)
            sim_time = 0.0
            for t in range(T):
                with torch.no_grad():
                    logits, v = agent(obs)
                    dist = Categorical(logits=logits); a = dist.sample()
                bo[t] = obs; ba[t] = a; blp[t] = dist.log_prob(a); bv[t] = v
                ts = time.time()
                o, r, te, tr, info = env.step(a.cpu().numpy())
                sim_time += time.time() - ts
                br[t] = torch.as_tensor(r, device=dev); bd[t] = torch.as_tensor(te, device=dev, dtype=torch.float32)
                obs = torch.as_tensor(o, device=dev)
                if info:
                    m = info['done'].astype(bool)
                    rets += info['ep_ret'][m].tolist(); ticks += info['ticks'][m].tolist()
                    cur.record(info['task'][m].astype(int), ~info['hit'][m].astype(bool))
            gstep += T * N
            with torch.no_grad():
                _, nv = agent(obs)
                adv = torch.zeros_like(br); last = 0
                for t in reversed(range(T)):
                    nextv = nv if t == T - 1 else bv[t + 1]
                    nonterm = 1 - bd[t]
                    delta = br[t] + cfg['gamma'] * nextv * nonterm - bv[t]
                    last = delta + cfg['gamma'] * cfg['gae_lambda'] * nonterm * last
                    adv[t] = last
                retn = adv + bv
            fo, fa, flp, fadv, fret = bo.view(-1, OBS_DIM), ba.view(-1), blp.view(-1), adv.view(-1), retn.view(-1)
            B = fo.shape[0]; mb = B // cfg['minibatches']
            for ep in range(cfg['epochs']):
                perm = torch.randperm(B, device=dev)
                for s in range(0, B, mb):
                    j = perm[s:s + mb]
                    logits, v = agent(fo[j])
                    dist = Categorical(logits=logits)
                    ratio = (dist.log_prob(fa[j]) - flp[j]).exp()
                    ad = fadv[j]; ad = (ad - ad.mean()) / (ad.std() + 1e-8)
                    pl = torch.max(-ad * ratio, -ad * ratio.clamp(1 - cfg['clip'], 1 + cfg['clip'])).mean()
                    vl = 0.5 * (v - fret[j]).pow(2).mean()
                    ent = dist.entropy().mean()
                    loss = pl + cfg['vf_coef'] * vl - cfg['ent_coef'] * ent
                    opt.zero_grad(); loss.backward()
                    nn.utils.clip_grad_norm_(agent.parameters(), cfg['max_grad_norm']); opt.step()
            rets, ticks = rets[-500:], ticks[-500:]
            st = cur.stage
            rec = dict(time=time.strftime('%Y-%m-%d %H:%M:%S'), step=gstep, stage=st, stage_name=stages[st]['name'],
                       stage_survival=cur.rate(st), stage_episodes=len(cur.hist[st]),
                       sps=round(T * N / (time.time() - t0 + 1e-9)), sim_sps=round(T * N / max(sim_time, 1e-9)),
                       ep_ret=float(np.mean(rets[-200:])) if rets else None,
                       ep_ticks=float(np.mean(ticks[-200:])) if ticks else None,
                       pl=pl.item(), vl=vl.item(), ent=ent.item(),
                       survival={stages[i]['name']: round(cur.rate(i), 3) for i in range(len(stages)) if cur.hist[i]})
            t0 = time.time()
            print(json.dumps({k: v for k, v in rec.items() if k != 'survival'}, ensure_ascii=False))
            log.write(json.dumps(rec, ensure_ascii=False) + '\n'); log.flush()
            csvf.write(f"{rec['time']},{gstep},{st},{stages[st]['name']},{rec['stage_survival']},{rec['stage_episodes']},"
                       f"{rec['sps']},{rec['ep_ret']},{rec['ep_ticks']}\n"); csvf.flush()
            if cur.maybe_advance(gstep):
                print(f'== curriculum -> stage {cur.stage}: {stages[cur.stage]["name"]}  ({cur.advanced[-1][2]})')
                push_sampler()
                save(f'stage{cur.stage}')
            if gstep - last_ck >= cfg['ckpt_every']:
                last_ck = gstep; save(str(gstep)); last_save = gstep; last_save_t = time.time()
            elif gstep - last_save >= cfg['save_every'] or time.time() - last_save_t >= cfg.get('save_minutes', 10) * 60:
                last_save = gstep; last_save_t = time.time(); save()
        save('final')
    except KeyboardInterrupt:
        print('[interrupt] saving latest.pt'); save()
    finally:
        if hasattr(env, 'close'):
            env.close()


if __name__ == '__main__':
    main()
