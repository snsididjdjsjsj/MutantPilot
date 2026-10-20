"""常量表 (标准 Fargo 终局 Mutant 配装, 详见 docs/loadout.md)。来源标记:
  [SRC]  = FargowiltasSouls 源码 (文件:行号)
  [VAN]  = Terraria 1.4.4 原版 (凭反编译源码记忆, 未在本机核对; 建议用 bridge 记录轨迹验证)
  [GUESS]= 推断/经验值
单位: 像素 / 帧 (60/s), y 向下。1 物块 = 16 px。
"""
# ---------------- Player hitbox ----------------
PLAYER_W = 20             # [VAN] Player.defaultWidth (移动/平台碰撞用)
PLAYER_H = 42             # [VAN] Player.defaultHeight
HURT_W = 20               # [SRC] FargoSoulsPlayer.cs:1582 GetPrecisionHurtbox: min(w,h)=20 的居中正方形
HURT_H = 20               #       由 Precision Seal (Masochist Soul 内含, MasochistSoul.cs:295-296) 启用;
                          #       HitEffectsPlayer.cs:432 弹幕不与该小判定相交则不受伤
# ---------------- Gravity / jump ----------------
GRAVITY = 0.4             # [VAN] Player.defaultGravity
MAX_FALL = 10.0           # [VAN] maxFallSpeed
JUMP_SPEED = 5.01 + 2.4   # [VAN] Player.jumpSpeed 5.01 + frogLegJumpBoost(+2.4 jumpSpeedBoost);
                          #       frogLegJumpBoost 来自 MasoAeolusFrog (AeolusBoots.cs:73-76, Supersonic 内含)
JUMP_HEIGHT = 15          # [VAN] jumpHeight (frog leg 的加成未计入: [GUESS])
# ---------------- Horizontal (Supersonic Soul) ----------------
MOVE_SPEED = 1.0 + 0.5 + 0.25   # [SRC] SupersonicSoul.cs:46 moveSpeed+=0.5; [VAN] 迅捷药水 +0.25; 其余饰品加成 [GUESS] 未计
MAX_RUN = 3.0 * MOVE_SPEED      # [VAN] maxRunSpeed=3 * moveSpeed
RUN_ACCEL_BASE = 0.08 * MOVE_SPEED   # [VAN] runAcceleration=0.08 * moveSpeed
RUN_SLOWDOWN_BASE = 0.2         # [VAN] runSlowdown
NOMOMENTUM_MULT = 5.0           # [SRC] UpdatesPlayer.cs:223-232 NoMomentum: runAcceleration*=5, runSlowdown*=5
NOMOMENTUM_EXTRA_SLOW = 7.0     # [SRC] 同上: 未按住与速度同向时 runSlowdown += 7 (几乎瞬停)
ACC_RUN_GROUND = 15.6           # [SRC] SupersonicSoul.cs:41 accRunSpeed = 15.6 (RunSpeed 开关开启)
ACC_RUN_AIR = 18.0              # [SRC] FlightMasteryWings.cs:32-35 HorizontalWingSpeeds speed=18
RUN_ACCEL_AIR = 0.75            # [SRC] 同上 acceleration=0.75 (覆盖 runAcceleration; 与 NoMomentum ×5 的先后顺序 [GUESS]: 按覆盖处理)
OVER_MAXRUN_ACCEL_MULT = 0.2    # [VAN] HorizontalMovement: 超过 maxRunSpeed 后以 runAcceleration*0.2 加速至 accRunSpeed
# ---------------- Wings (Flight Mastery / Soul of Eternity) ----------------
WING_TIME = 10**9               # [SRC] FlightMasterySoul.cs:31 wingTimeMax=999999 (无限飞行)
WING_ASC_FALLING = 0.85 * 1.5   # [SRC] FlightMasteryWings.cs:17-19 (FlightMasteryGravity 开关 ×1.5)
WING_ASC_RISING = 0.25          # [SRC] :20
WING_MAX_CAN_ASCEND = 1.0       # [SRC] :21
WING_MAX_ASCENT = 1.75          # [SRC] :22  上升速度上限 = jumpSpeed*1.75 ≈ 12.97
WING_CONST_ASCEND = 0.135       # [SRC] :23
WING_UP_MULT = 6.0              # [SRC] :24-29 同时按住"上"(W) 时上面三项 ×6
WING_GRAVITY_WHILE_FLYING = False  # [VAN] 飞行时 WingMovement 代替重力 (凭记忆, 待验证)
# 悬停: 按住 下+跳 且未骑乘 -> 抵消本帧竖直位移, |vy|<=0.1  [SRC] FlightMasterySoul.cs:38-46
# ---------------- Dashes ----------------
DASH_TYPE = 1                   # [SRC] SupersonicSoul.cs:90-95: CthulhuShield->2 后被 SupersonicTabi->1 覆盖 (两开关都开时)
DASH_SPEED = 16.9               # [VAN] dashType 1 (Master Ninja Gear/Tabi) 冲刺初速
DASH_DELAY = 20                 # [VAN] 冲刺结束后 dashDelay (冷却)
DASH_DECAY_FAST = 0.985         # [VAN] |vx|>12 时每帧 ×0.985
# 冲刺在 |vx| <= max(accRunSpeed, maxRunSpeed) 时结束 [VAN]; 由于 accRunSpeed 15.6/18, 实际效果很短
BETSY_DASH = True               # [SRC] MasochistSoul.cs:267 BetsyDashEffect (需绑定 Active Skill 键)
BETSY_SPEED = 25.0              # [SRC] AccessoryEffectsPlayer.cs:282 速度 25 指向鼠标
BETSY_TICKS = 15                # [SRC] Content/Projectiles/BetsyDash.cs timeLeft=15, 期间 player.position += vel
BETSY_CD = 300                  # [SRC] AccessoryEffectsPlayer.cs:261 SpecialDashCD = 5 秒
# BetsyDashing 期间 ImmuneToDamage (FargoSoulsPlayer.cs:95-98) -> 冲刺中无敌
# ---------------- Grappling hook (Lunar Hook) ----------------
HOOK = True
HOOK_SHOOT_SPEED = 18.0         # [VAN] Lunar Hook shootSpeed (凭记忆)
HOOK_REACH = 550.0              # [VAN] Lunar Hook 射程 ~34.4 格 (凭记忆)
HOOK_PULL_SPEED = 16.0          # [VAN] Lunar Hook 拉拽速度 (凭记忆)
# ---------------- Iframes ----------------
IFRAMES = 40                    # [VAN] Player.Hurt immuneTime=40 (无伤训练中受击即终止, 影响不大)
# ---------------- Arena (docs/loadout.md §5) ----------------
PLATFORM_HALF_LEN = 3200.0      # [SRC-社区] Mini Instabridge ≈400 格木平台 = 6400 px
PLATFORM_ROWS = (0.0, -192.0, -384.0)   # [GUESS-社区] 3 层平台, 间距 12 格 (有翅膀时常用 8-12 格)
PLATFORM_Y = 0.0                # 最底层平台顶面
ARENA_AURA_RADIUS = 2000.0      # [SRC] MutantBoss.cs:480
# ---------------- Boss ----------------
BOSS_W = 120                    # [SRC] MutantBoss.cs:111
BOSS_LIFEMAX_EXPERT = 9_700_000 * 0.5   # [SRC] :121, :154
PHASE2_FRAC = 2.0 / 3.0         # [SRC] :775
PLAYER_DPS = 25000.0            # [GUESS]
RITUAL_RADIUS = 1200.0          # [SRC] MutantRitual.cs (threshold 1200)
SCYTHE_MAX = 40.0               # [GUESS] MutantScythe 加速上限 (源码以 ×(1+accel) 无上限加速, 很快离开屏幕)
