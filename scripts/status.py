"""打印训练进度摘要: python scripts/status.py [runs/default]"""
import json, os, sys, time
out = sys.argv[1] if len(sys.argv) > 1 else 'runs/default'
cj = os.path.join(out, 'curriculum.json'); lj = os.path.join(out, 'log.jsonl')
if not os.path.exists(lj):
    sys.exit(f'没有找到 {lj}')
with open(lj, 'rb') as f:
    f.seek(0, 2); n = f.tell(); f.seek(max(0, n - 200000))
    lines = [l for l in f.read().decode('utf-8', 'ignore').strip().splitlines() if l.startswith('{')]
recs = []
for l in lines[-50:]:
    try:
        recs.append(json.loads(l))
    except json.JSONDecodeError:
        pass
last = recs[-1]
mt = os.path.getmtime(lj)
print(f'== {out}')
print(f'最后更新: {time.strftime("%Y-%m-%d %H:%M:%S", time.localtime(mt))} ({(time.time() - mt) / 60:.1f} 分钟前)')
print(f'总步数: {last["step"]:,}   吞吐: {last["sps"]} steps/s (仿真 {last.get("sim_sps")}), 近 {len(recs)} 次迭代平均 '
      f'{sum(r["sps"] for r in recs) / len(recs):.0f}')
print(f'当前阶段: #{last["stage"]} {last["stage_name"]}  生存率={last["stage_survival"]}  局数={last["stage_episodes"]}')
print(f'ep_ret={last["ep_ret"]}  ep_ticks={last["ep_ticks"]}  entropy={last["ent"]:.3f}')
if os.path.exists(cj):
    c = json.load(open(cj))
    adv = {s: (st, r) for s, st, r in c['advanced']}
    print(f'\n{"#":>3} {"阶段":<44} {"状态":<6} {"生存率":>7} {"总局数":>7}  晋级')
    for i, s in enumerate(c['stages']):
        h = c['hist'][i]
        rate = f'{sum(h) / len(h):.3f}' if h else '-'
        state = '完成' if i < c['stage'] else '训练中' if i == c['stage'] else '未开始'
        a = adv.get(i)
        print(f'{i:>3} {s["name"]:<44} {state:<6} {rate:>7} {c["total"][i]:>7}  {f"@{a[0]:,} ({a[1]})" if a else ""}')
