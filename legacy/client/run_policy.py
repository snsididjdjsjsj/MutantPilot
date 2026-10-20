"""本机策略。游戏每帧把局面发到 127.0.0.1:47000，这里用已经拷下来的权重选动作。

算在这台电脑的显卡上。不连接云端，云端训练继续跑自己的。

    python client/run_policy.py
    python client/run_policy.py --self-test
"""
import argparse
import os
import socket
import struct

import numpy as np
import torch
import torch.nn as nn

from obs_build import BASE_DIM, HAZ_F, K_HAZ, OBS_DIM, build_obs

MAGIC = 0x4D504C54
VERSION = 2
HOVER = 10
HEADER = struct.Struct("<IHIHB")
HERE = os.path.dirname(os.path.abspath(__file__))


class Policy(nn.Module):
    def __init__(self, hidden=256):
        super().__init__()
        self.haz = nn.Sequential(nn.Linear(HAZ_F, 128), nn.ReLU(), nn.Linear(128, 128), nn.ReLU())
        self.base = nn.Sequential(nn.Linear(BASE_DIM, 128), nn.ReLU())
        self.trunk = nn.Sequential(nn.Linear(128 + 256, hidden), nn.ReLU(), nn.Linear(hidden, hidden), nn.ReLU())
        self.pi = nn.Linear(hidden, 36)
        self.v = nn.Linear(hidden, 1)

    def forward(self, obs):
        b = self.base(obs[:, :BASE_DIM])
        h = obs[:, BASE_DIM:].view(-1, K_HAZ, HAZ_F)
        valid = h[..., :1]
        e = self.haz(h) * valid
        mx = (e - (1 - valid) * 1e4).max(1).values.clamp(min=0)
        mean = e.sum(1) / valid.sum(1).clamp(min=1)
        return self.pi(self.trunk(torch.cat([b, mx, mean], -1)))


def pick_device():
    if torch.cuda.is_available():
        return torch.device("cuda")
    return torch.device("cpu")


def load_policy(path):
    device = pick_device()
    ck = torch.load(path, map_location=device, weights_only=False)
    hidden = int(ck.get("cfg", {}).get("hidden", 256))
    net = Policy(hidden).to(device)
    net.load_state_dict(ck["model"])
    net.eval()
    step = int(ck.get("step", 0))
    with torch.no_grad():
        net(torch.zeros(1, OBS_DIM, device=device))
    if device.type == "cuda":
        torch.cuda.synchronize()
    return net, step, device


def act(net, packet):
    obs = build_obs(packet)
    device = next(net.parameters()).device
    with torch.no_grad():
        logits = net(torch.from_numpy(obs).view(1, -1).to(device))
    return int(logits.argmax(-1).item()), obs


def read_exact(conn, n):
    buf = bytearray()
    while len(buf) < n:
        chunk = conn.recv(n - len(buf))
        if not chunk:
            raise ConnectionError("closed")
        buf += chunk
    return bytes(buf)


def handle(conn, net):
    last = None
    with conn:
        conn.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
        while True:
            magic, version, tick, obs_count, _flags = HEADER.unpack(read_exact(conn, HEADER.size))
            if magic != MAGIC or version != VERSION or obs_count > 4096:
                raise ValueError(f"bad frame magic={magic:#x} ver={version} obs={obs_count}")
            raw = read_exact(conn, obs_count * 4) if obs_count else b""
            if obs_count < 40:
                action = HOVER
            else:
                packet = np.frombuffer(raw, dtype="<f4", count=obs_count)
                action, _obs = act(net, packet)
            if action != last or tick % 60 == 0:
                print(f"tick {tick} -> action {action}", flush=True)
                last = action
            conn.sendall(bytes((action, 0)))


def device_label(device):
    if device.type == "cuda":
        return f"cuda:{device.index or 0} {torch.cuda.get_device_name(device)}"
    return "cpu"


def serve(host, port, net, step, device):
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as server:
        server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        server.bind((host, port))
        server.listen(1)
        print(f"local policy step={step} listening {host}:{port} {device_label(device)}", flush=True)
        while True:
            conn, addr = server.accept()
            print(f"game {addr}", flush=True)
            try:
                handle(conn, net)
            except (ConnectionError, ValueError, OSError) as exc:
                print(f"closed {exc}", flush=True)


def self_test(path):
    net, step, device = load_policy(path)
    packet = np.zeros(40 + 20, np.float32)
    packet[11] = 1
    packet[12] = 500
    packet[13] = -250
    packet[16] = 19
    packet[24] = 800000
    packet[25] = 1000000
    packet[33] = 2
    packet[40:50] = (700, 0, 0, 32, 60, 60, 0, 24, 10, 0)
    packet[50:60] = (40, 0, -5, 0, 5, 5, 1, 25, 8, 0)
    action, obs = act(net, packet)
    haz = obs[BASE_DIM:].reshape(K_HAZ, HAZ_F)
    if obs.shape != (OBS_DIM,) or not (0 <= action < 36):
        raise SystemExit(f"self-test failed action={action} shape={obs.shape}")
    if haz[0, 0] != 1 or abs(haz[0, 6] - 60 / 200) > 1e-5:
        raise SystemExit("self-test failed: pillar was not kept in the first hazard slot")
    print(f"self-test ok step={step} action={action} {device_label(device)}", flush=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=47000)
    parser.add_argument("--checkpoint", default=os.path.join(HERE, "latest.pt"))
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        self_test(args.checkpoint)
        return
    net, step, device = load_policy(args.checkpoint)
    serve(args.host, args.port, net, step, device)


if __name__ == "__main__":
    main()
