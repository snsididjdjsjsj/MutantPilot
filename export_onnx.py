"""导出策略为 ONNX (供 Windows 端 Python 客户端 onnxruntime 使用)
  python export_onnx.py runs/default/latest.pt policy.onnx"""
import sys, torch
from train import Agent, PolicyOnly
from sim.env import OBS_DIM
ck = torch.load(sys.argv[1], map_location='cpu', weights_only=False)
a = Agent(ck['cfg']['hidden']); a.load_state_dict(ck['model']); a.eval()
torch.onnx.export(PolicyOnly(a), torch.zeros(1, OBS_DIM), sys.argv[2], input_names=['obs'], output_names=['logits'],
                  dynamic_axes={'obs': {0: 'batch'}, 'logits': {0: 'batch'}}, opset_version=17, dynamo=False)
print('exported', sys.argv[2])
