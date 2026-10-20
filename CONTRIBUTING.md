# 参与修改

先从仓库根目录跑通不依赖游戏的检查：

```powershell
python scripts/check.py
```

## 改战斗行为时

- 预测某一招的未来位置或碰撞时，在 `tests/` 里加一个不启动游戏也能跑的检查。能放进 `scripts/check.py` 的，就放进去。
- 需要一整份战斗日志才能跑的程序，保留在 `tests/` 下对应的项目里，并在 `tests/README.md` 写明它要什么输入。
- 实战成绩以游戏里的 Hurt、`OnKill` 和日志摘要为准。模拟器生存率、录像是否完整、几何小检查通过，都不能单独写成无伤击杀。

## 不要提交

- 泰拉瑞亚、tModLoader、FNA 或 Fargo's Souls 的反编译源码、安装包、DLL
- 存档、角色、世界文件
- 战斗 `jsonl`、录像、训练权重、`bin/`、`obj/`
- 本机绝对路径、角色名、世界名、邮箱、令牌

`scripts/verify_logic_video.py` 写出的报告里会带解析后的本机路径。这种报告留在本地。

问题模板里只需要版本、受虐模式、Hurt 次数，以及 `scripts/analyze_combat.py` 打出来的摘要。
