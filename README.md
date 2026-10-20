# MutantPilot

在单人世界里打 Fargo's Souls 的突变体。控制器读取当前局面，用游戏原有的移动、瞄准和攻击输入走位。F6 把操作交还，再按一次恢复。

源码版本是 0.3.97，写在 mod/MutantPilot/build.txt。已经公开的一次受虐零受击是 0.3.87，记录在 docs/results.md。那次的救援预算是 96 毫秒，参数文件是 configs/live/no-gold-search96.json。当前源码的默认救援预算是 16 毫秒。不加载那份参数，就不是那次对局的配置。

## 需要

- 已购买的泰拉瑞亚，以及 tModLoader 1.4.4
- Fargo's Souls。build.txt 软依赖 1.7.3.7，战斗行为对照的是已安装的 1.7.3.9
- 单人世界，并打开 Masochist（受虐）模式
- .NET 8。在游戏里编译时，用 tModLoader 自带的开发者环境即可

本仓库不含泰拉瑞亚、tModLoader 或 Fargo's Souls 的程序和源码。

## 安装

1. 打开一次 tModLoader，在设置里启用开发者模式。确认这个文件存在：

```
文档/My Games/Terraria/tModLoader/ModSources/tModLoader.targets
```

2. 只把 mod/MutantPilot 复制或链接到 ModSources/MutantPilot。不要把整个仓库放进 ModSources，否则测试项目会被一起编译。
3. 在游戏的 Mod Sources 里编译并 Reload，和 Fargo's Souls 一起启用。
4. 进入单人受虐世界，把武器放进快捷栏。突变体出现后自动接管。

也可以在仓库根目录编译：

```powershell
dotnet build mod/MutantPilot/MutantPilot.csproj -c Release
```

tModLoader.targets 不在默认文档目录时，加上 -p:tMLTargets= 指向本机的那份文件。

开一局、看日志和录像见 docs/playing.md。代码怎么分工见 docs/architecture.md。

## 检查

不启动游戏也能跑：

```powershell
python scripts/check.py
```

需要 .NET 8。这组检查的 Python 部分只用标准库。对照某次实战日志的项目见 tests/README.md。

## 目录

- mod/MutantPilot：tModLoader 模组
- tests：预测和记录格式检查
- scripts：开局、看日志、可选的启动和录像
- configs/live：0.3.87 那次对局的参数
- tools/LoopbackRec：可选的 Windows 环回录音
- docs：操作和结果

## 许可

源码使用 MIT 许可，见 LICENSE。build.txt 里的 author 会显示在模组列表中。

泰拉瑞亚版权归 Re-Logic。Fargo's Souls 与突变体版权归 Fargo's Mods 团队。MutantPilot 不是这两个项目的官方作品。
