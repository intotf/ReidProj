"""
setup_models.py — 完全自包含的模型下载和 ONNX 导出脚本

用法:
    cd ReidProj
    python scripts/setup_models.py

输出:
    models/yolo11n.onnx            — YOLOv11n 人物检测模型
    models/reid_model.onnx          — FastReID ResNet50-IBN-a 特征提取模型
    models/movenet_lightning.onnx   — MoveNet Lightning 姿态估计模型
"""
import os
import sys
import subprocess
import zipfile
import io
import argparse
from pathlib import Path

# ─── 路径配置 ─────────────────────────────────────────────────────
PROJECT_DIR = Path(__file__).resolve().parent.parent
MODELS_DIR = PROJECT_DIR / "models"
SCRIPTS_DIR = Path(__file__).resolve().parent
FASTREID_DIR = SCRIPTS_DIR / "fast-reid"


def log(msg: str):
    print(f"[setup_models] {msg}")


def run_cmd(cmd: list[str], cwd: Path | None = None, desc: str = ""):
    log(f"运行: {' '.join(cmd)}  {f'({desc})' if desc else ''}")
    result = subprocess.run(cmd, cwd=cwd, capture_output=True, text=True)
    if result.returncode != 0:
        log(f"❌ 失败: {result.stderr.strip()}")
        sys.exit(result.returncode)
    return result


# ═══════════════════════════════════════════════════════════════════
# 1. YOLOv11n → ONNX
# ═══════════════════════════════════════════════════════════════════
def export_yolo():
    yolo_onnx = MODELS_DIR / "yolo11n.onnx"
    if yolo_onnx.exists():
        log(f"✅ YOLO ONNX 已存在，跳过: {yolo_onnx}")
        return

    log("=" * 50)
    log("Step 1/2: 导出 YOLOv11n → ONNX")
    log("=" * 50)

    # 确保 ultralytics 已安装
    run_cmd([sys.executable, "-m", "pip", "install", "-q", "ultralytics"],
            desc="安装 ultralytics")

    # 使用 ultralytics 导出
    import ultralytics
    log("下载 YOLOv11n 权重并导出 ONNX...")
    model = ultralytics.YOLO("yolo11n.pt")
    model.export(format="onnx", imgsz=640, opset=18)

    # 复制到 models/
    src = PROJECT_DIR / "yolo11n.onnx"
    if not src.exists():
        src = Path("yolo11n.onnx")
    if src.exists():
        src.rename(yolo_onnx)
        log(f"✅ YOLOv11n ONNX 已导出: {yolo_onnx}")
    else:
        log("❌ 未找到导出的 ONNX 文件")
        sys.exit(1)


# ═══════════════════════════════════════════════════════════════════
# 2. FastReID → ONNX
# ═══════════════════════════════════════════════════════════════════
def export_reid():
    reid_onnx = MODELS_DIR / "reid_model.onnx"
    if reid_onnx.exists():
        log(f"✅ ReID ONNX 已存在，跳过: {reid_onnx}")
        return

    log("=" * 50)
    log("Step 2/3: 导出 FastReID ResNet50-IBN-a → ONNX")
    log("=" * 50)

    # 克隆 FastReID 仓库
    if not FASTREID_DIR.exists():
        log("克隆 JDAI-CV/fast-reid...")
        run_cmd(
            ["git", "clone", "--depth=1", "https://github.com/JDAI-CV/fast-reid",
             str(FASTREID_DIR)],
            cwd=PROJECT_DIR,
            desc="克隆 fast-reid"
        )
    else:
        log(f"✅ fast-reid 已存在: {FASTREID_DIR}")

    # 安装依赖
    log("安装 FastReID 依赖...")
    run_cmd(
        [sys.executable, "-m", "pip", "install", "-q",
         "torch", "torchvision", "onnx",
         "fvcore", "iopath", "yacs", "timm",
         "termcolor", "tabulate", "tensorboard"],
        desc="安装 PyTorch + 依赖"
    )

    # 将 fastreid 添加到 sys.path
    sys.path.insert(0, str(FASTREID_DIR))
    sys.path.insert(0, str(FASTREID_DIR / "projects" / "FastDistill"))

    # 构建并导出
    log("构建 ResNet50-IBN-a 模型...")
    _do_export_reid(reid_onnx)


def _do_export_reid(output_path: Path):
    """在导入 fastreid 后执行 ONNX 导出"""
    import torch
    import torch.nn as nn
    from torch.onnx import export as onnx_export

    # 导入 fastreid 组件
    from fastreid.config import get_cfg
    from fastreid.modeling.meta_arch import build_model
    from fastreid.utils.checkpoint import Checkpointer

    # ── 构建默认配置 ──────────────────────────────────────────
    cfg = get_cfg()
    cfg.merge_from_file(str(FASTREID_DIR / "configs" / "Market1501" / "bagtricks_R50-ibn.yml"))

    # 遵循官方部署脚本的配置模式
    cfg.MODEL.WEIGHTS = ""  # 稍后手动加载
    cfg.MODEL.BACKBONE.PRETRAIN = False  # 避免下载 ImageNet 预训练权重
    cfg.MODEL.DEVICE = "cpu"  # 强制 CPU（当前环境无 CUDA）
    cfg.DATALOADER.NUM_WORKERS = 0
    # 注意: 不覆盖 WITH_BNNECK/NORM/EMBEDDING_DIM，使用配置文件中的默认值
    # 因为 pretrained 权重是基于这些默认值训练的
    cfg.freeze()

    # ── 构建模型 ──────────────────────────────────────────────
    model = build_model(cfg)
    model.to('cpu')
    model.eval()

    # 下载 Market1501 预训练权重
    weights_path = SCRIPTS_DIR / "bagtricks_R50-ibn.pth"

    if not weights_path.exists():
        log("下载 Market1501 预训练权重...")
        import urllib.request
        urllib.request.urlretrieve(
            "https://github.com/JDAI-CV/fast-reid/releases/download/v0.1.1/market_bot_R50-ibn.pth",
            weights_path)
        log(f"✅ 权重已下载: {weights_path}")

    # 加载权重
    log("加载预训练权重...")
    Checkpointer(model).load(str(weights_path))

    # 遵循官方 onnx_export.py 的导出前处理
    if hasattr(model.backbone, 'deploy'):
        model.backbone.deploy(True)

    # ── ONNX 导出包装器 ───────────────────────────────────────
    # 将均值/标准差归一化内嵌到 ONNX 图中，同时包含 GlobalAvgPool
    class ONNXWrapper(nn.Module):
        def __init__(self, baseline):
            super().__init__()
            self.baseline = baseline
            self.mean = torch.tensor([[[0.485]], [[0.456]], [[0.406]]])
            self.std = torch.tensor([[[0.229]], [[0.224]], [[0.225]]])

        def forward(self, x: torch.Tensor) -> torch.Tensor:
            x = (x - self.mean) / self.std          # 1) ImageNet 归一化
            features = self.baseline.backbone(x)     # 2) ResNet → [B,2048,16,8]
            feat = self.baseline.heads.pool_layer(features)  # 3) GlobalAvgPool → [B,2048,1,1]
            feat = feat[..., 0, 0]                   # 4) Squeeze → [B,2048]
            feat = nn.functional.normalize(feat, p=2, dim=1)  # 5) L2 归一化
            return feat

    # 构建包装器（使用完整 model，包含 backbone + head.pool_layer）
    wrapper = ONNXWrapper(model)
    wrapper.eval()

    # ── 导出 ONNX ─────────────────────────────────────────────
    dummy_input = torch.randn(1, 3, 256, 128)

    log("导出 ONNX（主网络，包含内嵌归一化 + L2 归一化）...")
    with torch.no_grad():
        onnx_export(
            wrapper,
            dummy_input,
            str(output_path),
            input_names=["input"],
            output_names=["output"],
            dynamic_axes={
                "input": {0: "batch_size"},
                "output": {0: "batch_size"},
            },
            opset_version=18,
            do_constant_folding=True,
        )

    # ── 合并外部数据到单一 .onnx 文件 ────────────────────────
    # PyTorch 2.x 导出的 ONNX 可能包含外部权重文件 (.onnx.data)
    data_file = output_path.with_suffix(output_path.suffix + ".data")
    if data_file.exists():
        log("检测到外部权重数据，合并到单一 ONNX 文件...")
        try:
            import onnx
            onnx_model = onnx.load(str(output_path))
            onnx.save_model(onnx_model, str(output_path), save_as_external_data=False)
            data_file.unlink()  # 删除外部数据文件
            merged_size = round(output_path.stat().st_size / (1024 * 1024), 1)
            log(f"✅ 外部数据已合并到 ONNX 文件 ({merged_size} MB)")
        except Exception as e:
            log(f"⚠️  合并外部数据失败 ({e})，保留外部数据文件")

    # ONNX 优化和简化（参考官方脚本）
    log("优化 ONNX 模型...")
    try:
        import onnx
        import onnxoptimizer
        onnx_model = onnx.load(str(output_path))
        passes = ["extract_constant_to_initializer", "eliminate_unused_initializer"]
        onnx_model = onnxoptimizer.optimize(onnx_model, passes)
        # 移除输入中的初始化器（官方做法）
        inputs = onnx_model.graph.input
        name_to_input = {input.name: input for input in inputs}
        for initializer in onnx_model.graph.initializer:
            if initializer.name in name_to_input:
                inputs.remove(name_to_input[initializer.name])
        onnx.save_model(onnx_model, str(output_path))
        log("ONNX 优化完成")
    except ImportError:
        log("onnxoptimizer 不可用，跳过优化")
    except Exception as e:
        log(f"ONNX 优化跳过 ({e})")

    log(f"✅ FastReID ONNX 已导出: {output_path}")


# ═══════════════════════════════════════════════════════════════════
# 3. MoveNet Lightning → ONNX 直接下载
# ═══════════════════════════════════════════════════════════════════
def export_movenet():
    pose_onnx = MODELS_DIR / "movenet_lightning.onnx"
    if pose_onnx.exists():
        log(f"✅ MoveNet ONNX 已存在，跳过: {pose_onnx}")
        return

    log("=" * 50)
    log("Step 3/3: 下载 MoveNet Lightning → ONNX")
    log("=" * 50)

    import urllib.request

    # 从 PINTO Model Zoo 下载预编译 ONNX
    url = "https://github.com/PINTO0309/PINTO_model_zoo/raw/main/306_MoveNet_Lightning/saved_model/model_float32.onnx"
    log(f"下载 MoveNet Lightning ONNX (~2.5 MB)...")
    try:
        urllib.request.urlretrieve(url, pose_onnx)
        size_mb = round(pose_onnx.stat().st_size / (1024 * 1024), 1)
        log(f"✅ MoveNet Lightning ONNX 已下载: {pose_onnx} ({size_mb} MB)")
    except Exception as e:
        log(f"⚠️ PINTO 仓库下载失败 ({e})")
        log("尝试从 TF Hub 转换（需安装 tensorflow）...")
        run_cmd(
            [sys.executable, "-m", "pip", "install", "-q",
             "tensorflow", "tensorflow-hub", "onnx", "tf2onnx"],
            desc="安装 TensorFlow + tf2onnx"
        )
        _convert_movenet_from_tf(pose_onnx)


def _convert_movenet_from_tf(output_path: Path):
    """从 TF Hub 下载 MoveNet Lightning 并转换为 ONNX"""
    import tensorflow as tf
    import tensorflow_hub as hub
    import tf2onnx

    log("从 TF Hub 加载 MoveNet Lightning...")
    model = hub.load("https://tfhub.dev/google/movenet/singlepose/lightning/4")
    model = model.signatures['serving_default']

    dummy_input = tf.constant(0., shape=[1, 192, 192, 3])
    log("转换为 ONNX（使用 tf2onnx）...")
    onnx_model, _ = tf2onnx.convert.from_function(
        model,
        input_signature=[tf.TensorSpec([1, 192, 192, 3], tf.float32, name='input')],
        opset=18,
        output_path=str(output_path),
    )
    size_mb = round(output_path.stat().st_size / (1024 * 1024), 1)
    log(f"✅ MoveNet Lightning ONNX 已转换: {output_path} ({size_mb} MB)")


# ═══════════════════════════════════════════════════════════════════
# Main
# ═══════════════════════════════════════════════════════════════════
def main():
    parser = argparse.ArgumentParser(description="下载并导出 YOLOv11 + FastReID + MoveNet 模型为 ONNX")
    parser.add_argument("--skip-yolo", action="store_true", help="跳过 YOLO 人物检测导出")
    parser.add_argument("--skip-reid", action="store_true", help="跳过 ReID 导出")
    parser.add_argument("--skip-pose", action="store_true", help="跳过 MoveNet 姿态估计导出")
    args = parser.parse_args()

    MODELS_DIR.mkdir(parents=True, exist_ok=True)

    if not args.skip_yolo:
        export_yolo()
    else:
        log("跳过 YOLO 导出")

    if not args.skip_reid:
        export_reid()
    else:
        log("跳过 ReID 导出")

    if not args.skip_pose:
        export_movenet()
    else:
        log("跳过姿态估计导出")

    log("=" * 50)
    log("🎉 全部完成！")
    log(f"   - {MODELS_DIR / 'yolo11n.onnx'}")
    log(f"   - {MODELS_DIR / 'reid_model.onnx'}")
    log(f"   - {MODELS_DIR / 'movenet_lightning.onnx'}")
    log("=" * 50)


if __name__ == "__main__":
    main()
