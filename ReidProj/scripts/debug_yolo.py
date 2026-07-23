"""
诊断 YOLOv11 ONNX 模型输出的 bbox 格式
"""
import numpy as np
import onnxruntime as ort
from PIL import Image

s = ort.InferenceSession("models/yolo11n.onnx")

img = Image.open("scripts/test_sample.jpg").convert("RGB")
w, h = img.size
scale = min(640 / w, 640 / h)
nw, nh = int(w * scale), int(h * scale)
img_rs = img.resize((nw, nh))
canvas = Image.new("RGB", (640, 640), (114, 114, 114))
canvas.paste(img_rs, ((640 - nw) // 2, (640 - nh) // 2))
arr = np.array(canvas, dtype=np.float32) / 255.0
mean = np.array([0.485, 0.456, 0.406])
std = np.array([0.229, 0.224, 0.225])
arr = ((arr - mean) / std).transpose(2, 0, 1)[np.newaxis].astype(np.float32)

out = s.run(None, {"images": arr})[0]

# bbox 直接在 letterbox 空间
person_logits = out[0, 4, :]
person_scores = 1.0 / (1.0 + np.exp(-person_logits))
mask = person_scores > 0.3
idx = np.where(mask)[0]
print(f"person sigmoid>0.3: {len(idx)} 个")

# 用 Python 的 NMS 压一下看最终几个框
boxes_lb = []  # letterbox空间
for i in idx:
    x1, y1, x2, y2 = out[0, :4, i]
    x1 = max(0, x1)
    y1 = max(0, y1)
    x2 = min(640, x2)
    y2 = min(640, y2)
    if x2 > x1 and y2 > y1:
        boxes_lb.append((x1, y1, x2, y2, person_scores[i]))

# 简单 NMS
def _iou(a, b):
    xi1 = max(a[0], b[0]); yi1 = max(a[1], b[1])
    xi2 = min(a[2], b[2]); yi2 = min(a[3], b[3])
    if xi1 >= xi2 or yi1 >= yi2: return 0
    inter = (xi2 - xi1) * (yi2 - yi1)
    a_area = (a[2] - a[0]) * (a[3] - a[1])
    b_area = (b[2] - b[0]) * (b[3] - b[1])
    return inter / (a_area + b_area - inter + 1e-6)

boxes_lb.sort(key=lambda b: b[4], reverse=True)
keep = []
while boxes_lb:
    best = boxes_lb.pop(0)
    keep.append(best)
    boxes_lb = [b for b in boxes_lb if _iou(best, b) < 0.45]

print(f"NMS后: {len(keep)} 个")

for k in keep:
    x1, y1, x2, y2, conf = k
    # 转原图坐标
    pad_x = (640 - nw) // 2
    pad_y = (640 - nh) // 2
    ox1 = (x1 - pad_x) / scale
    oy1 = (y1 - pad_y) / scale
    ox2 = (x2 - pad_x) / scale
    oy2 = (y2 - pad_y) / scale
    print(f"  原图: ({ox1:.0f},{oy1:.0f})-({ox2:.0f},{oy2:.0f}) conf={conf:.3f}")
