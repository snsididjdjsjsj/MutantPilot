# Mutant (FargowiltasSouls, tModLoader 1.4.4) 攻击机制文档

> 行号来自整理模拟器时对照的 Fargo's Souls 公开源码 `Content/Bosses/MutantBoss/MutantBoss.cs`，不是某一台机器上的路径。下文 `MB.cs:行号` 指这份 `MutantBoss.cs`。实战控制器在 `mod/MutantPilot`，不从本文自动生成。
> 默认难度: **Eternity**；`[Maso]` 标注 Masochist 模式 (`WorldSavingSystem.MasochistModeReal`) 的差异。
> 单位: 像素 / 帧 (60 帧 = 1 秒)，y 轴向下。状态机变量: `AttackChoice = NPC.ai[0]`，计时器 `ai[1..3]`、`localAI[0..3]`。
> 实现状态: ✅ 已在模拟器中逐帧复刻；🟡 已实现但有近似；⛔ 未实现 (stub)。

## 0. 通用机制

| 项目 | 内容 | 源码 |
|---|---|---|
| 本体尺寸 | 120×120，`noGravity/noTileCollide` | MB.cs:111 |
| 血量 | 专家 9,700,000 × 0.5 (ApplyDifficultyAndPlayerScaling) × balance | MB.cs:121,154 |
| 接触伤害 | Eternity **不造成接触伤害**；`[Maso]` 距离 < Player.defaultHeight 时造成伤害 | MB.cs:172-178 |
| 竞技场光环 | `ArenaAura(AuraCenter, 2000*AuraScale)`，出圈给 GodEater + MutantFang (等同死亡)。AuraCenter 每帧 lerp 0.3 跟随本体；AuraScale: 攻击1→0.7 (lerp 0.02)，冲刺(5/6)→1.25，其余 1 | MB.cs:375-391,480 |
| 移动函数 `Movement(target, speed, fastX, cap)` | 每轴 ±speed 加速，反向时额外 ×2 (fastX) 转向；速度上限 24；`[Maso]` speed×2、转向×2、上限×1.5 | MB.cs:792-839 |
| 朝向 | 每帧 `direction = 本体在玩家左侧 ? 1 : -1` (影响球环旋转方向) | MB.cs:244 |
| 二阶段判定 | `life < 2/3 lifeMax` → AttackChoice=10，清弹幕 | MB.cs:773-790 |
| P1 攻击顺序 | `P1NextAttackOrMasoOptions`: Eternity 严格顺序 0→1→2(3)→4(5,6)→7(8)→9(边界)→9(剑)→0…；`[Maso]` 1/3 概率从 {0,1,2,4,7,9,9} 随机, 禁止若干组合 (见源码) | MB.cs:660-709 |
| P2 选招 | `ChooseNextAttack(args)`: 每招结束给出候选列表; Eternity 下当 `localAI[3]>=3` 且随机数大于 (life/lifeMax)² 时随机选，并用历史队列 (Eternity 18 / Maso 12) 避免重复 | MB.cs:569-658 |
| 受击附加 (Eternity) | 多数弹幕: MaxLifeReduction+100、OceanicMaul、MutantFang、CurseoftheMoon | 各弹幕 OnHitPlayer |
| Purged | 剑攻击期间给玩家 Purged: 闪避类效果失效 | MB.cs:1404, PurgedBuff.cs |

## 1. 一阶段 (P1) — 模拟器已实现

### 1.1 Spear Toss Direct (ai0=0) — ✅ `boss.spear_toss`
`SpearTossDirectP1AndChecks` MB.cs:948-1051
- 移动: 目标 = 玩家 ±500 (本体所在一侧)，`Movement(.., 0.5)`，距离>50 才移动。
- 计时: `ai1++`；`ai1<145` 时持续记录瞄准角 `localAI0 = 指向 (player.Center + player.velocity*30)` (**预判 30 帧**)。
- `ai1 == 61` (且未投完): 生成 `MutantDeathrayAim`(无伤预警线, 85 帧, 跟随本体, 每帧 0.2 插值到预判方向) + `MutantSpearAim`(ai1=3, 有判定, 寿命 60+30=90 帧, 一直指向预判点)。
- `ai1 > 150`: `ai1=60`，`ai2++`；若 `ai2 > ai3 (Eternity=5)` 结束→下一招，并给本体 2px/帧 朝玩家的速度；否则发射 `MutantSpearThrown` 速度 25。→ 第一次投掷在第 151 帧，之后每 90 帧一次，共 5 次。
- `MutantSpearThrown` (MutantSpearThrown.cs): 16×16 + 沿速度方向长 200、宽 8 的线段判定；`extraUpdates=1` (每帧走 2 次 → 50px/帧)，timeLeft 180 次更新 (=90 帧)；每 5 次更新在原地留下 `MutantSphereSmall`(ai0=target)。
- `MutantSphereSmall`: 46×46，120 帧；20 帧后以 1/20 插值转向玩家、目标速度 5 (追踪)；死亡时生成 `MutantBombSmall` (圆形判定半径 137.5，仅动画帧 0..2 有伤害, 每帧 3 tick)。
- `[Maso]`: 投掷次数随机 2..7；第一次时额外召唤 8 条毁灭者 (MutantDestroyer*)；每次投掷附带前后两条 `MutantDeathray2`；长矛改为每 4 次更新向两侧各发一颗 15 帧的小球。⛔ 毁灭者未实现；Deathray2 用 RAY1 近似 (🟡)。

### 1.2 Okuu Spheres (ai0=1) — ✅ `boss.okuu`
`OkuuSpheresP1` MB.cs:1053-1085, `SpawnSphereRing` MB.cs:711
- Eternity 本体**不清零速度** (保留上一招结束时的 2px/帧 漂移)。每 91 帧一轮，共 4 轮；每轮两个环: 6 颗、速度 9，`ai0 = +1*direction` 和 `-0.5*direction`。
- `MutantSphereRing` (MutantSphereRing.cs:238): 40×40，480 帧；每帧速度方向旋转 `speed / (2π·ai0·t)` 弧度 (t 为已存活帧数) → 先急转后渐直的螺旋。在 P2 若距 MutantRitual > 1200 会提前消失 (P1 无 ritual)。
- `[Maso]`: 速度清零, 节奏 ×3 (每 30 帧)，每环 9 颗速度 10，正反交替，额外结尾等待。

### 1.3 True Eye Dive (ai0=2→3) — 🟡 `boss.prep_eye_dive / eye_dive`
MB.cs:1087-1171, MutantTrueEye{L,S,R}.cs, MutantTrueEyeSphere.cs, MutantTrueEyeDeathray.cs
- 准备: 移到玩家 ±700、上方 400 (speed 0.6)，到达(<50)或 180 帧后以 x=35 俯冲 (vy 取正 ×0.3)。
- 俯冲: 速度 ×0.99 衰减；每 16 帧一个事件，前 3 次依次召唤 L / S / R 真眼 (CanDamage=false 本体无伤)，之后 5 次空等，然后下一招；`ai2>3` 后本体回到玩家 ±500。
- 真眼共同移动: 目标点 = 玩家 + 偏移，速度向 24 以 1/30 插值; 距玩家 <150 时每轴推开 0.25。
  - **L 眼**: 偏移 (0,-300)，120 帧 → 减速 0.95 至 <1 → 发射 `MutantTrueEyeDeathray` (90 帧，scale=min(0.4, 4·sin(πt/90))，判定宽 22·scale；每帧旋转 `±(2π/3)/90`，总扫 120°)。🟡 初始角度依赖瞳孔角 `localAI0`(UpdatePupil)，模拟器近似为"指向玩家 − 60 帧的旋转量"。
  - **S 眼**: 偏移 (-200·side, -200)，60 帧 → 减速 0.9 → 第 7、14 帧各向 `player + vel*15` 发射原版 PhantasmalBolt (速度 8)。🟡 PhantasmalBolt 尺寸/extraUpdates 为猜测。
  - **R 眼**: 偏移 (300·side, -300)，90 帧 → 在周围半径 100 生成 6 个 `MutantTrueEyeSphere`(46×46, 360 帧) 并减速 60 帧 → 以 24 速度冲向玩家 10 帧。球在前 65 帧跟随 R 眼速度，之后静止。
- `[Maso]`: 召唤 6 只眼 (每种 2 只)，结束等待 3。

### 1.4 Spear Dash Direct (ai0=4→5↔6) — ✅ `boss.prep_dash / dash / while_dash`
MB.cs:1173-1257, MutantSpearSpin.cs, MutantSpearDash.cs, MutantEyeHoming.cs
- 准备 240 帧: 生成 `MutantSpearSpin` (152×152 判定, 跟随本体)；本体移到玩家下方 400、侧向 600 (仅当本体顶部高于玩家脚底时加侧向偏移)，`Movement(0.7, fastX=false)`。
  - SpearSpin 每 9 帧, 若与玩家距离 > 360，在自身范围内随机位置生成 `MutantEyeHoming`：速度 6~9、方向在 +Y ±90° 内随机，若本体在玩家上方则反向；`ai1 = 剩余时间 + rand(剩余/2)`。
  - `MutantEyeHoming`: 12×12, 900 帧；`ai1>0` 且离玩家<240 时转离玩家并把寿命截到 180；`ai1∈(-600,0)` 时加速 1.02 至 10 并以 0.1 转向玩家, 进入 480 内即停止追踪; 本体离开冲刺状态后全部停止追踪并减速。
- 冲刺: 共 5 次；每次先 11 帧 (`ai3=10`) 速度 ×0.9 停顿，然后以 30 速度冲向 `player.Center + player.velocity` (预判 1 帧)，持续 31 帧。`MutantSpearDash` 附在本体 (线段 200/宽 8)，每 3 帧留下追踪 `MutantSphereSmall` (→爆炸)。
- `[Maso]`: 停顿随机 3..14，速度 45，前后 Deathray2，小球改为向两侧发射。

### 1.5 Void Rays (ai0=7→8) — ✅ `boss.approach / void_rays`
MB.cs:1259-1305, MutantMark1.cs, MutantDeathray1.cs, MutantDeathraySmall.cs
- 接近: 目标 = 玩家 + (玩家→本体方向)×250，speed 0.5，到达或 180 帧。记录初始角 = 玩家→本体，步进 π/10 (玩家在左则为负)。
- 释放: 本体静止，每 6 帧生成 `MutantMark1` (46×46，速度 2 沿当前角，90 帧)，角度 += π/10；第 21、41 个时回退半步。共 41 个。
- Mark1 出生时在终点生成 `MutantDeathraySmall` 预警 (无伤, 30 帧)；死亡时生成 `MutantDeathray1` (60 帧, scale=min(1, 4·sin(πt/60))，**仅 scale≥1 时有伤害** (t≈5..55)，判定宽 22，长度 3000)。
- `[Maso]`: 每 4 帧一个、共 61 个、回退 1/3 步；Deathray1 不变。

### 1.6 Boundary Lite + Mutant Sword (ai0=9) — 🟡 `boss.boundary_sword`
MB.cs:1310-1549, MutantEye.cs, MutantSword.cs, MoonLordSunBlast.cs
- 边界 (localAI2=0): 本体静止 240 帧；每 3 帧 `ai2 += π/77`，向 4 个方向 (间隔 90°) 发射 `MutantEye` (12×12, 速度 7, 300 帧)。`[Maso]`: 真 Boundary (角速度随时间二次变化)、5 方向、360 帧。
- 剑准备 (localAI2=1): 移到玩家 ±420、上方 210 (speed 1.2)，30 帧后且距离<64 时停住；生成 12 把 `MutantSword` 沿一条线 (间隔 80) + 两侧 ±26.5° (半径 180)、±40° (半径 240) 共 4 把；`ai2 = (π/4·-dir)·(-4/20)` 为挥动角速度；等待 90 帧 (FancyFireballs 预警)，然后本体以 `(玩家 - 300·ai2·x̂ - 本体)/20` 的速度移动。
- 挥剑 (localAI2=2): 26 帧，每帧剑整体绕本体旋转 `ai2`；第 20 帧在剑中段 (距本体 480, 45° 方向) 生成 5 个 (Eternity) `MoonLordMoonBlast` 链式爆炸 (各方向间隔 72°，前 3 个和倒数第 2 个链长 48 节，其余 24 节；每节 70×70×U(1.5,4)，每 6 帧向前 100px 传播并留下驻留爆炸)；ai1>20 后剑消失。
- `MutantSword` 判定: 64×64，且检测上一帧位置与中点 (扫掠) — 已复刻。
- 🟡 MoonBlast 的动画帧数 (决定伤害窗口) 为猜测 (7 帧，每帧 2 tick，帧 3/4 有伤害)。`[Maso]`: 8 个爆炸。

## 2. 二阶段 (P2) — 已实现 (`sim/boss.py` 第 P2 节, `sim/sim.py` P2/P3 弹幕)

> 进入: P1 任意招式开头检测 `life < 2/3 lifeMax` (MB.cs:773) → ai0=10 清弹。Phase2Transition (MB.cs:1555) 270 帧: ai1==0 清弹 + 生成 MutantRitual; ai1>150 置 localAI3=3; 结束时回满血, 从 [11,13,16,19,20,21,24,26,29,35,37,39,42] 随机选第一招。
> 之后每招结束调用 `ChooseNextAttack(候选...)` (MB.cs:569: 随机化条件 `U(0,0.8)+0.2 > (life/max)^2`, attackHistory 防重复 5 次重抽, maxMem 18 / Maso 12, attackCount 超过 1.25×maxMem 时本次只记 maxMem/4) → ai0=52 `P2NextAttackPause` (MB.cs:3359) → 下一招。default 分支 → 11；18/28 → ++。
> 血量: `PLAYER_DPS/60` 每帧 (ai0≥0 且 ≠10)；P2 血量 ≤0 → CheckDead → ai0=-1 (清弹)。
> 竞技场: P2+ 为 MutantRitual (半径 1200, 每帧向 boss 移动 (npc−r)/60; 36 号 /20, 22/23/25 号 /40, 19 号冻结)。圈外被拉回并受伤 → 模拟器近似为 **距圈心 >1170 即判定受击** 🟡。P1 光环 (2000×scale) 仅在 localAI3==1 时生效。
> MutantPresence (localAI3≥2): noDodge / GrazeRadius×0.5 / moonLeech —— 模拟器不模拟闪避/擦弹/回血, 因此只作为观测标志位 🟡。

| ai0 | 方法 (行号) | 弹幕 | 状态 | 近似 / 说明 |
|---|---|---|---|---|
| 10 | Phase2Transition (1555) | — | ✅ | 课程从 phase 2 开始时跳过前 240 帧动画 |
| 11/12 | ApproachForNextAttackP2 / VoidRaysP2 (1659/1682) | Mark1→Deathray1 | ✅ | 射线起点不随 mark 漂移 (同 P1) |
| 13/14/15 | PrepareSpearDashPredictiveP2 / SpearDashPredictiveP2 / WhileDashingP2 (1710/1745/1828) | SpearSpin(→EyeHoming), DeathrayAim, SpearAim(4), SpearDash(-2 侧弹), Deathray2 | ✅ | — |
| 16/17 | ApproachForBoundaryP2 / BoundaryBulletHellP2 (1845) | MutantEye ×5 (Maso 6) | ✅ | — |
| 19 | PillarDunk (1889) | MutantPillar, MutantIllusion ×3, 碎片 | 🟡 | 分身位置按 MutantIllusion 镜像公式; 柱子上升速度 / 碎片朝向按源码阅读复刻, 碎片 4×24 环 |
| 20 | EOCStarSickles (1998) | MutantEOC, MutantScythe1/2 | 🟡 | EOC 环绕阶段的 Fargo 加速度用"朝目标 +0.6, 上限 24"近似; 镰刀加速上限 40 为 [GUESS] |
| 21/22/23 | PrepareSpearDashDirectP2 / SpearDashDirectP2 (2083/2118) | SpearSpin, SpearDash, Deathray2 | ✅ | 23 状态每 3 帧跳帧 |
| 24/25 | SpawnDestroyersForPredictiveThrow / SpearTossPredictiveP2 (2157/2246) | MutantDestroyerHead/Body/Tail, SpearThrown(ai1=1), SpearAim(2), DeathrayAim | 🟡 | 身体节跟随有 1 帧延迟 (向量化); 节间距 36 |
| 26/27 | PrepareMechRayFan / MechRayFan (2285/2339) | MutantDeathray3 ×2/10 帧, MutantGuardian | ✅ | Glowything / Reticle2 为纯视觉, 省略 |
| 29/30 | PrepareFishron1 / SpawnFishrons (2443/2465) | MutantFishron | 🟡 | SmartAccel(3,2) 用"沿差向量加速 3, 速度上限 距离/4"近似 |
| 31/32 | PrepareTrueEyeDiveP2 / TrueEyeDive (2507) | TrueEye L/S/R | 🟡 | 同 P1 (L 眼激光初始角近似) |
| 33/34 | PrepareNuke / Nuke (2529/2567) | MutantNuke(无伤), MutantFishronRitual 安全圈, MutantBomb ×3/帧 | 🟡 | 爆炸期 (ai1>180) 安全圈 (半径 150) 外 1200 内全部受伤; 炸弹 r=200, 15 帧 |
| 35/36 | PrepareSlimeRain / SlimeRain (2649) | MutantReticle(预警), MutantSlimeBall | ✅ | Eternity 非 Maso 分支 (Maso 的移动缝隙版本未实现) |
| 37/38 | PrepareFishron2 / SpawnShadowHands (2894/2465) | MutantShadowHand (复用 FISHRON, l1=1) | 🟡 | 同 29/30 |
| 39/40 | PrepareOkuuSpheresP2 / OkuuSpheresP2 (2917/2936) | SphereRing 9×2 (Maso 10) | ✅ | — |
| 41 | SpearTossDirectP2 (2992) | SpearAim(-1/1), SpearThrown(+追踪小球), Deathray2 | ✅ | — |
| 42/43 | PrepareTwinRangsAndCrystals / TwinRangsAndCrystals (3060/3081) | MutantMark2, MutantCrystalLeaf ×5, MutantRetirang/Spazmarang ×4 | ✅ | 回旋镖按恒速圆周 (向心加速度) 复刻 |
| 44 | EmpressSwordWave (3154) | FairyQueenLance (原版弹幕) | 🟡 | **原版 FairyQueenLance AI 不在 Fargo 源码中**: [GUESS] 漂移 60 帧后沿朝向以 40 速度刺出, 90 帧消失, 判定线段 180×18; 生成几何 (gap 266, spawnDistance 1330, MegaSwordSwarm 60 把) 按源码 |
| 45/46 | PrepareMutantSword / MutantSword (1402/1483) | MutantSword, MoonBlast ×8 | 🟡 | 同 P1 (MoonBlast 帧数猜测); endTimeVariance 在 Eternity 为 0 |
| 52 | P2NextAttackPause (3359) | — | ✅ | — |
| 18, 28 | — | — | ✅ | 空槽 (++) |
| 47–50 | — | — | — | 源码已注释掉 |

## 3. 三阶段 (P3, 绝望阶段) — 已实现

- 本体无敌, 每帧流失 `lifeMax / 2580` 血 (Eternity, MB.cs:405-414); Phase3Transition 的 ai1>480 段不流失。
- Phase3Transition: ai1==0 回满血 + 清弹; 480 帧后移动到玩家上方 300 处 (≤720 帧) → -2。

| ai0 | 方法 (行号) | 弹幕 | 状态 | 说明 |
|---|---|---|---|---|
| -1 | Phase3Transition (3395) | — | ✅ | — |
| -2 | VoidRaysP3 (3481) | Mark1 ×120 (每 2 帧) | ✅ | — |
| -3 | OkuuSpheresP3 (3526) | SphereRing 10×2, rotmod ±0.75 | ✅ | — |
| -4 | BoundaryBulletHellP3 (3580) | MutantEye ×8 | ✅ | — |
| -5 | FinalSpark (3642) | SphereRing 10×2/120 帧, MutantGiantDeathray2 | 🟡 | 巨型激光宽度 22×scale (scale≤10), scale≥5 有判定; 随 SpinLaser 以 ≤60°/s 追踪玩家 |
| -6/-7 | DyingDramaticPause / DyingAnimation | (全部 0 伤害) | ✅ | **到达 -6 即胜利** |

## 4. 模拟器实现状态一览

✅ = 逐帧按源码复刻; 🟡 = 已实现, 含标注的近似。

| 阶段 | 攻击 | 状态 |
|---|---|---|
| P1 | 0 SpearToss ✅ · 1 Okuu ✅ · 2/3 TrueEyeDive 🟡 · 4/5/6 SpearDash ✅ · 7/8 VoidRays ✅ · 9 Boundary+Sword 🟡 | 全部实现 |
| P2 | 11/12 ✅ · 13-15 ✅ · 16/17 ✅ · 19 🟡 · 20 🟡 · 21-23 ✅ · 24/25 🟡 · 26/27 ✅ · 29/30 🟡 · 31/32 🟡 · 33/34 🟡 · 35/36 ✅ · 37/38 🟡 · 39/40 ✅ · 41 ✅ · 42/43 ✅ · 44 🟡 · 45/46 🟡 | 全部实现 |
| P3 | -1 ✅ · -2 ✅ · -3 ✅ · -4 ✅ · -5 🟡 · -6/-7 ✅ | 全部实现 |

全局近似: 玩家输出由 `PLAYER_DPS` (默认 25k) 推算 (P1 ≈3880 帧, P2 ≈11640 帧, P3 ≈3500 帧, 全程约 1.9 万帧 ≈5.3 分钟)；Maso 只覆盖"便宜"的数值差异 (P1 毁灭者、P2 Maso 专属分支如 SlimeRain 缝隙版 / 双倍 Nuke 等未实现)；不模拟 Graze/闪避饰品/Presence 的非移动效果；所有纯视觉弹幕 (Glowything、Reticle2、EyeWavy 等) 省略；ritual 出圈 = 受击。
