# 实战参数

`no-gold-search96.json` 是 0.3.87 那次零受击对局使用的参数。它把 `RescueBudgetMs` 和 `FinalBeamRescueBudgetMs` 设为 96。源码里这两个值的默认是 16。

文件里没有的字段保持 `PilotTuning` 的默认值。当前源码比这份文件多出的 `AsyncDecision` 和 `ProposalBudget` 因此仍是 `false` 和 `0`。

加载方式见 [docs/playing.md](../../docs/playing.md)。
