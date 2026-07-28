"""
setup_models.py — 人脸模型下载脚本（FaceFeature 专用）

用法:
    cd FaceFeature
    python scripts/setup_models.py

输出:
    models/det_10g.onnx       — SCRFD-10g 人脸检测模型
    models/w600k_r50.onnx       — ArcFace 人脸特征提取模型
"""
import os
import sys
import zipfile
import io
import urllib.request
from pathlib import Path

PROJECT_DIR = Path(__file__).resolve().parent.parent
MODELS_DIR = PROJECT_DIR / "models"


def log(msg: str):
    print(f"[setup_models] {msg}")


def export_models():
    face_onnx = MODELS_DIR / "det_10g.onnx"
    face_rec_onnx = MODELS_DIR / "w600k_r50.onnx"
    if face_onnx.exists() and face_rec_onnx.exists():
        log(f"✅ 人脸模型已存在，跳过")
        return

    log("=" * 60)
    log("从 InsightFace buffalo_l 模型包提取 ONNX 模型")
    log("=" * 60)

    url = "https://github.com/deepinsight/insightface/releases/download/v0.7/buffalo_l.zip"
    log(f"下载 buffalo_l.zip (约 84 MB)...")
    resp = urllib.request.urlopen(url)
    data = resp.read()
    log(f"  下载完成 ({len(data) / 1024 / 1024:.1f} MB)")

    with zipfile.ZipFile(io.BytesIO(data)) as zf:
        log("从 zip 中提取 det_10g.onnx ...")
        zf.extract("det_10g.onnx", MODELS_DIR)
        log("从 zip 中提取 w600k_r50.onnx ...")
        zf.extract("w600k_r50.onnx", MODELS_DIR)

    log(f"✅ SCRFD-10g ONNX 已就绪: {face_onnx} ({round(face_onnx.stat().st_size / 1024 / 1024, 1)} MB)")

    rec_size_mb = round(face_rec_onnx.stat().st_size / 1024 / 1024, 1)
    log(f"✅ ArcFace ONNX 已就绪:   {face_rec_onnx} ({rec_size_mb} MB)")


def main():
    MODELS_DIR.mkdir(parents=True, exist_ok=True)
    export_models()

    log("=" * 60)
    log("🎉 全部完成！")
    log(f"   - {MODELS_DIR / 'det_10g.onnx'}")
    log(f"   - {MODELS_DIR / 'w600k_r50.onnx'}")
    log("=" * 60)


if __name__ == "__main__":
    main()
