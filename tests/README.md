# 测试

在仓库根目录执行：

```powershell
python scripts/check.py
```

这会先编译其余不依赖 tModLoader 安装目录的测试项目，再运行下面这些不读存档的项目，然后跑 `test_combat_report.py`：

- `CombatPlannerChecks`：规划、法阵、冲刺和固定 Boss 轨迹。法阵与冲刺样本在 `fixtures/`。
- `ForecastIndexChecks`：危险物空间索引和全量查询一致。
- `WormResponseChecks`：虫体跟随。
- `SwordRoutingStudy`：单独的剑阵几何。直接运行时打印一份 JSON，不表示游戏胜利；`check.py` 只确认它能跑完。
- `LogicVideoChecks`：逻辑帧序号。不带参数时不编码视频。
- `ShortRayChecks`：短激光的伤害时段。不带参数时不读日志。

## 需要自己的战斗日志

这些程序用来把某一份 `combat_*.jsonl` 和出生记录对上预测。日志留在本机，不要提交。

- `FinalPhaseReplay`
- `LockedTossChecks`
- `MarkSpawnChecks`
- `MechFanChecks`
- `NoGoldSphereChecks`
- `P1TransitionReplay`
- `SphereSpawnChecks`
- `TossResponseChecks`
- `VoidExitReplay`
- `VoidPhaseReplay`

每个 `Program.cs` 开头写了参数顺序。缺参数时有的项目会直接返回，那不算通过，所以 `scripts/check.py` 只编译、不运行它们。

## 需要本机游戏文件

- `BackgroundHookChecks`：读 tModLoader 的 `tModLoader.dll`，确认失焦赋值仍是预期的那一处。默认安装路径是 Steam 下的 `steamapps/common/tModLoader`，也可以把安装目录设给环境变量 `TMODLOADER_DIR`，或把 dll 路径当作第一个参数。Mono.Cecil 从同一安装目录引用。没有这份 dll 时项目无法编译，所以 `scripts/check.py` 不包含它。
- `EmptyWorldChecks`：检查一张 8400×2400 的空白世界。参数是世界路径和 tModLoader 目录。

`dotnet` 从仓库根目录运行时才能找到 `mod/MutantPilot/build.txt`，夹具路径是靠这个标记向上找的。
