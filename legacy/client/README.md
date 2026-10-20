# 旧的本机策略客户端

实战模组已经不创建 `PilotLink`，因此这里不会被当前控制器调用。

当时的约定是：游戏把观测发到 `127.0.0.1:47000`，这个进程用本地权重回一个动作。权重没有放进仓库。

```powershell
python run_policy.py --self-test
```

在本目录里执行。需要 `numpy` 和 `torch`，见仓库根目录的 `requirements.txt`。
