# 实战操作

命令都在仓库根目录执行。Python 3.10 及以上。这些脚本只读写本机 tModLoader 存档目录里的 `MutantPilot` 文件夹，地址是 `文档/My Games/Terraria/tModLoader/MutantPilot/`。

## 一局怎么开始

游戏已经停在单人世界、角色活着、场上没有 Boss 时。普通受虐用第一份参数，传奇受虐（大师 + For the Worthy + 受虐）用第二份：

```powershell
python scripts/start_trial.py --version 0.3.122 --settings configs/live/no-gold-search96.json
python scripts/start_trial.py --version 0.3.122 --settings configs/live/legendary-no-gold.json
```

`--version` 必须和 mod/MutantPilot/build.txt 里的 version 一致。脚本会先看状态是不是刚刷新的，再要求受虐模式、没有它认识的辅助，然后用正常飞行升到准备高度。生命达到 `--life`（默认 900）并且停稳之后，才召唤一次。脚本检查受虐开关，不代替你自己打开大师和 For the Worthy。

默认救援预算在源码里是 16 毫秒。第一份参数对应 [0.3.87 那次普通受虐](results.md)，第二份对应 [0.3.122 那次传奇受虐](results.md)。当前源码加载其中一份，也不会把历史构建变成同一次验收。

主武器是 FargowiltasSouls/TheDestroyer 时，Boss 出现前会按快捷栏第 5 到第 7 格里的法杖正常施法，准备召唤物。换装只在没有 Boss 时进行：

```powershell
python scripts/live.py equiptrial --weapon FargowiltasSouls/TheDestroyer
```

允许的武器名还有 FargowiltasSouls/DragonBreath2 和 FargowiltasSouls/NukeFishron。这条命令会按游戏自己的规则生成物品并备份被替换的格子，不在战斗中改生命或伤害。调参接口会拒绝金身和全部冲刺。

生命上限不是 900 时加上自己的值，例如 `--life 500`。

常用命令：

```powershell
python scripts/live.py status
python scripts/live.py prepare
python scripts/live.py summon
python scripts/live.py stop
python scripts/live.py pilot
python scripts/live.py savequit
python scripts/live.py tune --settings configs/live/no-gold-search96.json
```

`stop` 交还操作。`pilot` 恢复接管。`clean` 把控制器能识别到的无敌和穿墙开关关掉，用来排除别的模组留下的辅助；它本身不提供这些能力。`seed` 和 `anchor` 会固定随机过程或准备位置，这样开出来的局不能算自然随机成绩。

失焦后若希望单人世界继续按原速更新，可在没有 Boss 时执行 `python scripts/live.py background --enabled 1`，用 `--enabled 0` 关掉。这只改变失焦暂停，不改伤害和 Boss。

## 看结果

战斗日志是该目录下的 `combat_*.jsonl`。

```powershell
python scripts/analyze_combat.py "文档路径\combat_YYYYMMDD_HHMMSS_fff.jsonl"
```

一次可报告的无伤击杀需要同时满足：原生 `boss_killed`、Hurt 为 0、受虐模式、记录没有损坏、没有检测到已知辅助、没有中途把操作交还。Boss 消失、活得久、进入最后阶段，都不能代替击杀。

`scripts/analyze_performance.py` 只汇总帧耗时，不判断输赢。

## 地图

那次通过的对局发生在一张大型空白地图上，不依赖平台和钩爪。可以用自己的世界当模板另存一张，不覆盖原世界：

```powershell
python scripts/create_large_empty_world.py --template "你的世界.wld" --output "MutantEmpty.wld"
```

模板和输出都留在本机存档目录。脚本不会改角色。

## 启动游戏

平时用 Steam 或 tModLoader 自己的入口即可。需要从终端拉起时：

```powershell
.\scripts\launch_game.ps1 -PlayerName "角色显示名" -WorldName "世界显示名"
```

两个名字要同时给，或者都不给。`-Windowed` 会先备份 `config.json`，再把显示改成 1280×720 窗口。这会改本机显示设置。

`scripts/focus_game.ps1` 只把已经打开的 tModLoader 窗口带到前台。

## 录像

在准备召唤的那条命令后加 `--record`。需要能在 `PATH` 里找到 `ffmpeg`，否则加 `--ffmpeg` 指向可执行文件。默认把无声视频写到仓库下的 `recordings/`，这个目录已经在 `.gitignore` 里。

```powershell
python scripts/start_trial.py --version 0.3.122 --settings configs/live/legendary-no-gold.json --record
python scripts/verify_logic_video.py "recordings\某一段.mp4" --ffprobe "ffprobe 的路径"
```

视频按连续的游戏逻辑帧做成 60 FPS，缺帧就失败，不用重复画面填洞。Windows 上可以用 `tools/LoopbackRec` 录系统默认输出设备，再用 `scripts/warp_and_mux.py` 按每帧墙上时间把音效贴回游戏时间轴。录像完整只说明画面连续，不说明这局无伤。细节在 [recording.md](recording.md)。
