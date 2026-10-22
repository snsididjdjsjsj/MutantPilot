# 实战参数

两份文件都只覆盖写出来的字段。没写的字段用当前源码里 PilotTuning 的默认值。

no-gold-search96.json 是 0.3.87 那次普通受虐零受击使用的参数。它把 RescueBudgetMs 和 FinalBeamRescueBudgetMs 设为 96。源码里这两个值的默认是 16。文件里的 AllowImmunityCrutches 是当时的字段，0.3.122 已经不再读取它。金身和冲刺由当前控制器直接拒绝。

legendary-no-gold.json 是 0.3.122 那次传奇受虐零受击使用的参数。救援预算是 256 毫秒，最终激光救援是 192 毫秒，主武器是 FargowiltasSouls/TheDestroyer。不加载这份文件，就不是那次对局的配置。

加载方式见 docs/playing.md。
