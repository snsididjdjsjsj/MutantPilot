# 标准 Fargo 终局配装与竞技场（模拟器依据）

标记：**[SRC]** 来自 Fargo 源码（FargowiltasSouls 仓库，路径/行号为调研时版本）；**[VAN]** 原版 Terraria 机制（凭记忆/wiki，需在游戏内复核）；**[GUESS]** 推测。

## 1. 配装（打 Mutant 前，Eternity Advisor 推荐）
来源：`Content/Items/Misc/EternityAdvisor.cs:595-622` [SRC]
- 饰品：Terraria Soul、Masochist Soul、Universe Soul、Dimension Soul、Sparkling Adoration、Abominable Wand、Universe Core（击败后换 Mutant 套 + Eternity Soul + Mutant Eye）。
- 盔甲/武器：职业相关（通常为本职业最强 Soul 武器）；对移动无影响，模拟器不建模，伤害用 `PLAYER_DPS` 粗估 [GUESS]。
- 钩爪：Lunar Hook [VAN]（射速 18、射程 550、拉速 16 凭记忆 [GUESS]）。
- 坐骑：**不使用**。理由：Flight Mastery 提供无限飞行；悬停与 NoMomentum 在坐骑上不生效 [SRC: FlightMasterySoul / UpdatesPlayer]。
- Buff：Swiftness（+25% moveSpeed）[VAN]，其余为伤害/防御类，不影响模拟。

## 2. 与移动相关的效果
| 效果 | 数值 | 来源 |
|---|---|---|
| Supersonic 奔跑 | accRunSpeed 15.6，moveSpeed +0.5 | SupersonicSoul.cs:41,46 [SRC] |
| NoMomentum | runAcceleration×5，runSlowdown×5，不按方向时 +7 | UpdatesPlayer.cs:223-232 [SRC] |
| 冲刺 | dashType 1（Tabi，覆盖克苏鲁盾），速度 16.9、间隔 20 [VAN] | SupersonicSoul.cs:90-95 [SRC] |
| 翅膀 | 水平 speed 18 / accel 0.75；ascentWhenFalling 0.85、Rising 0.25、maxAscent 1.75、constantAscend 0.135；按住上 ×6 | FlightMasteryWings.cs [SRC] |
| 飞行时间 | wingTimeMax 999999（无限） | FlightMasterySoul.cs:31 [SRC] |
| 悬停 | 下+跳：vy 限制 ±0.1 | FlightMasterySoul.cs:38-46 [SRC] |
| Betsy 冲刺（主动技能） | 速度 25 朝鼠标，15 帧，期间无敌，CD 5 秒 | MasochistSoul.cs:267；AccessoryEffectsPlayer.cs:257-305；FargoSoulsPlayer.cs:95-98 [SRC] |
| 平台穿透 | 下键穿平台 | MasochistSoul PlatformFallthrough [SRC] |

## 3. 判定与 Debuff
- **受击判定 20×20**（Precision Seal，居中）：FargoSoulsPlayer.cs:1582 `GetPrecisionHurtbox`；启用 MasochistSoul.cs:295-296；使用 HitEffectsPlayer.cs:432 [SRC]。
- 无敌帧 40 [GUESS]（no-hit 目标下不影响）。
- **Mutant Presence**（P2 起，或受虐模式全程）：noDodge、擦弹半径 ×0.5、治疗 ×0.5（FargoSoulsPlayer.cs:1454）[SRC]。noSupersonic 字段仅被重置，似未使用 [SRC，不确定]。
- 未建模：Parry（Dread Shell/Pumpking's Cape）、擦弹加成、Terraria Soul 中各附魔的闪避/冲刺（Shinobi、Titanium 等）[不确定其在 Presence 下是否可用]。

## 4. 竞技场
- 社区标准：Mini Instabridge（约 400 格木平台）铺多层平台，行距 8-12 格，配合钩爪躲避。来源：Fargo wiki（Instabridge 页面）、terraria.guide 竞技场指南、Scribd Eternity 模式指南、Steam 指南 3293167612。未找到唯一“官方”公开地图，故采用此通用布局 [GUESS-社区]。
- 模拟器：3 层平台，y = 0、-192、-384 像素（12 格行距），半长 3200 像素（400 格），Mutant 光环半径 2000 [SRC 近似]。

## 5. 仍为推测的参数
飞行时重力、翅膀加速与 NoMomentum 的叠加方式、其它装备 moveSpeed 加成、青蛙腿跳跃高度、钩爪数值、无敌帧、玩家 DPS。
