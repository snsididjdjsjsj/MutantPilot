# 旧模拟器

`sim/` 用 NumPy 批量步进玩家、弹幕和 Boss。Boss 出招的计时和弹幕运动按整理时读到的 Fargo's Souls 公开源码逐帧来写，玩家飞行和冲刺有一部分是估计值。标成近似或未实现的招式见 [attacks.md](attacks.md)，配装假设见 [loadout.md](loadout.md)。

Boss 血量按 `player_dps` 扣除，默认每秒 25000。这里没有武器命中、受虐机制和实装配装。模拟器里的生存率不能用来声称实战击杀。

`train.py` 因此默认退出。只有明确要做离线实验时才加 `--allow-uncalibrated-sim`。

## 安装

Python 3.10 或更新版本。渲染 MP4 需要 `ffmpeg`。

```bash
python -m venv .venv
source .venv/bin/activate   # Windows: .venv\Scripts\activate
pip install -r requirements.txt
```

用 GPU 训练时，先按自己的 CUDA 版本安装 PyTorch，再装 `requirements.txt`。CPU 冒烟不强制装 CUDA 轮子，但 `torch` 仍要能导入。

## 冒烟

在仓库根目录：

```bash
python train.py --config configs/smoke_cpu.json --allow-uncalibrated-sim
python scripts/bench.py 256
python scripts/render_episode.py --policy scripted --attack 4 --out dash.mp4
```

`bench.py` 和 `render_episode.py` 都把仓库根目录加进模块路径，所以要从根目录启动。正式课程配置是 `configs/default.json`，输出在 `runs/`，该目录已被忽略。

```bash
python train.py --config configs/default.json --allow-uncalibrated-sim
python scripts/status.py runs/default
python export_onnx.py runs/default/latest.pt policy.onnx
```

覆盖单个配置项时，值按 JSON 解析：

```bash
python train.py --config configs/default.json --allow-uncalibrated-sim --n_envs=1024 --out_dir='"runs/big"'
```

`--fresh` 忽略已有的 `latest.pt`。同一条命令再次执行时，默认从 `out_dir/latest.pt` 续训。

## 课程在做什么

`configs/default.json` 里 `curriculum.stages` 为 `auto` 时，脚本先逐个招式训练，再进入完整阶段。最近一个窗口里的无伤率达到阈值就升到下一阶段；某一阶段步数到顶也会强制升级，避免停在一招上。大约四分之一的环境会抽以前的阶段，用来减轻遗忘。

动作是 48 个离散选择：水平方向、垂直方向（含跳、下穿、悬停）和冲刺或钩爪的组合。观测把玩家、Boss 和最近的弹幕拼在一起。奖励鼓励活着和完成阶段目标，受击即结束该局。

这些设定描述的是模拟器，不是 `mod/MutantPilot` 的实战输入。实战控制器没有读取这里导出的 ONNX。
