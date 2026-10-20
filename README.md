# MutantPilot

Single-player tModLoader controller for the Mutant boss in Fargo's Souls. It reads the live fight and answers with ordinary movement, aiming, and item use. The fight recorded in [docs/results.md](docs/results.md) is one natural Masochist clear on version 0.3.87. The source in this repository is **0.3.97**.

文档以中文为主。

## 这是什么

MutantPilot 在单人世界里接管突变体战斗。Boss 出现后，控制器根据当前弹幕和 Boss 状态搜索接下来一小段时间的走位，再写入游戏本来就有的方向、跳跃和攻击输入。F6 把操作交还玩家，再按一次恢复。

当前源码版本写在 `mod/MutantPilot/build.txt`。默认普通搜索预算 4 毫秒，危险窗口救援预算 **16 毫秒**。下面那次零受击对局把救援预算调到了 96 毫秒，参数在 `configs/live/no-gold-search96.json`。不加载这份参数，就不是那次对局的配置。

`sim/` 和 `train.py` 是更早的向量化模拟器和 PPO 实验。模拟器按固定 DPS 扣 Boss 的血，训练曲线不能当成实战成绩。`train.py` 默认直接退出。

## 环境

- 已购买的泰拉瑞亚，以及 tModLoader 1.4.4
- [Fargo's Souls](https://github.com/Fargo-Mod-Team/FargowiltasSouls)。`build.txt` 软依赖 `1.7.3.7`；战斗行为对照的是已安装的 **1.7.3.9**
- 单人世界，并打开 Masochist（受虐）模式
- 编译模组需要 .NET 8 SDK。只在游戏内编译时，用 tModLoader 自带的开发者环境即可

本仓库不含泰拉瑞亚、tModLoader、FNA，也不含 Fargo's Souls 的源码或安装包。

## 安装模组

1. 打开一次 tModLoader，并在设置里启用开发者模式，直到这个文件存在：

   `文档/My Games/Terraria/tModLoader/ModSources/tModLoader.targets`

2. 只把 `mod/MutantPilot` 复制或链接到 `ModSources/MutantPilot`。整个仓库放进 ModSources 会把测试项目一起编进去。
3. 在游戏的 Mod Sources 里编译并 Reload。和 Fargo's Souls 一起启用。
4. 进入单人受虐世界，把武器放进快捷栏。突变体出现后自动接管，F6 交还。

也可以在仓库根目录直接编译，工程会去找上面的 `tModLoader.targets`。targets 不在默认文档目录时：

```powershell
dotnet build mod/MutantPilot/MutantPilot.csproj -c Release -p:tMLTargets="D:\path\to\ModSources\tModLoader.targets"
```

本地试跑、记录和验收见 [docs/playing.md](docs/playing.md)。目录和工作方式见 [docs/architecture.md](docs/architecture.md)。

## 检查

下面这组检查不需要启动游戏，也不需要存档：

```powershell
python scripts/check.py
```

需要 .NET 8 SDK。Python 只用标准库。还要对照某次实战日志时，看 [tests/README.md](tests/README.md)。

## 目录

| 路径 | 内容 |
| --- | --- |
| `mod/MutantPilot/` | tModLoader 模组 |
| `tests/` | 预测和记录格式检查；`tests/fixtures/` 是脱去本机路径后的小样本 |
| `scripts/` | 查询状态、开一局、分析日志、可选的启动和录像辅助 |
| `configs/live/` | 实战参数样例 |
| `sim/`、`train.py`、`configs/*.json` | 旧模拟器与研究用训练配置 |
| `legacy/client/` | 已不再被实战控制器调用的本机策略客户端 |
| `tools/LoopbackRec/` | 可选的 Windows 环回录音 |
| `docs/` | 操作、结果、机制笔记、模拟器 |

## 旧模拟器

只在明确要做离线研究时再训练。安装和课程说明在 [docs/simulator.md](docs/simulator.md)。机制笔记在 [docs/attacks.md](docs/attacks.md)，配装假设在 [docs/loadout.md](docs/loadout.md)。

## 许可与归属

源码使用 [MIT](LICENSE)。`build.txt` 里的 `author` 会显示在模组列表中。

泰拉瑞亚版权归 Re-Logic。Fargo's Souls 与突变体版权归 Fargo's Mods 团队。MutantPilot 不是这两个项目的官方作品。引用它们的公开机制是为了把控制器写对，不表示获得了再分发游戏或该模组的许可。
