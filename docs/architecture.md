# 结构

实战控制器是 `mod/MutantPilot`。tModLoader 加载 `MutantPilot` 这个 `Mod`，`Common/PilotSystem` 注册 F6。Boss 在场时，`Common/Combat/LiveCombat.cs` 读取原生状态，`CombatPlanner` 在时间预算内比较候选输入。选中的输入通过游戏原有的控制字段发出。

搜索用到的未来弹幕、激光、剑、冲刺和虫体写在 `Common/Combat/` 下按招式分开的文件里。`PilotTuning` 是局间可调的参数，`scripts/live.py tune` 会整份替换它。

`Common/PilotHarness.cs` 监视存档目录中的 `command.json`，把结果写回 `status.json`。这是本机文件，不是网络服务。

`Common/PilotWorld.cs` 读取受虐模式，并列出它认识的无敌或穿墙开关，供验收时剔除。`Common/PilotLink.cs` 是早先本机策略客户端的协议，只面向 `127.0.0.1:47000`。当前规划器不会创建它。对应的旧客户端在 `legacy/client/`。

录制在 `Common/LogicFrameRecorder.cs` 和 `Common/Combat/LogicVideoWriter.cs`。每个成功收下的游戏帧编码成一帧视频。

## 版本

| 版本 | 含义 |
| --- | --- |
| 0.3.87 | 一次自然随机、无金身、零 Hurt 的受虐击杀。安装包没有放进仓库。 |
| 0.3.97 | 本仓库源码。在那次击杀之后加入了空间索引、逻辑帧录像和开场前的录像预留。 |

后一个版本的存在，不表示每一项改动都重新通过了无伤验收。

## 测试怎么分成两类

`scripts/check.py` 编译并运行不读存档的项目，再跑 `tests/test_combat_report.py`。这些项目直接把 `mod/MutantPilot/Common/Combat` 里的纯计算文件编进来，所以不引用泰拉瑞亚程序集。

需要战斗日志、世界文件或 tModLoader 安装目录的项目留在 `tests/` 里，说明见 [tests/README.md](../tests/README.md)。`scripts/check.py` 会编译其中不依赖游戏安装目录的项目，但不会运行。运行它们需要本机日志或世界，这些文件不放进仓库。
