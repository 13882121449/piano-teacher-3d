"""用本地的 TripoSR 官方权重从三视图中的正视图重建网格，不上传图片。"""

import argparse
from pathlib import Path
import sys
import types

import imageio.v3 as iio
import mcubes
import numpy as np
from PIL import Image
from scipy import ndimage
import torch


def prepare(reference, directory, weights):
    """把正视图裁成两个尺度，头部单独输入以保留脸部采样分辨率。"""
    image = Image.open(reference).convert("RGB")
    if image.size != (1774, 887):
        raise ValueError("输入应为原版 1774×887 三视图")
    for name, bounds in (("body", (183, 10, 570, 857)), ("head", (325, 10, 448, 131))):
        part = np.asarray(image.crop(bounds))
        mask_weights = weights / "u2netp.onnx"
        if name == "body" and mask_weights.is_file() and mask_weights.stat().st_size == 4574861:
            import onnxruntime as ort
            session = ort.InferenceSession(str(weights / "u2netp.onnx"), providers=["CPUExecutionProvider"])
            inp = np.asarray(Image.fromarray(part).resize((320, 320)), dtype=np.float32) / 255
            inp = ((inp - np.array([0.485, 0.456, 0.406])) / [0.229, 0.224, 0.225]).transpose(2, 0, 1)
            raw = session.run(None, {session.get_inputs()[0].name: inp[None].astype(np.float32)})[0][0, 0]
            alpha = np.asarray(Image.fromarray(raw).resize((part.shape[1], part.shape[0]), Image.Resampling.BILINEAR))
            alpha = np.clip((alpha - 0.08) / 0.84, 0, 1)
        else:
            rgb = part.astype(np.float32)
            bg = np.median(rgb[:, :8], axis=1)[:, None, :]
            mask = np.linalg.norm(rgb - bg, axis=2) > 19
            labels, total = ndimage.label(mask)
            sizes = np.bincount(labels.ravel(), minlength=total + 1)
            sizes[0] = 0
            mask = ndimage.binary_fill_holes(labels == sizes.argmax())
            alpha = ndimage.gaussian_filter(mask.astype(np.float32), sigma=0.7)
        ys, xs = np.where(alpha > 0.6)
        if len(xs) < 100:
            raise ValueError(f"{name} 未找到合理的人物轮廓")
        composed = (part * alpha[..., None] + 128 * (1 - alpha[..., None])).astype(np.uint8)
        patch = Image.fromarray(composed).crop((xs.min(), ys.min(), xs.max() + 1, ys.max() + 1))
        size = int(max(patch.size) / (0.84 if name == "body" else 0.80))
        canvas = Image.new("RGB", (size, size), (128, 128, 128))
        canvas.paste(patch, ((size - patch.width) // 2, (size - patch.height) // 2))
        canvas.save(directory / f"{name}_input.png")
        yield name, canvas


def use_local_backend(repo, weights):
    """Windows 机器使用 PyMCubes，转换轴顺序以匹配官方 torchmcubes 接口。"""
    sys.path.insert(0, str(repo))
    marching = types.ModuleType("torchmcubes")
    def marching_cubes(field, threshold):
        vertices, faces = mcubes.marching_cubes(field.float().cpu().numpy(), threshold)
        return torch.from_numpy(vertices[:, [2, 1, 0]].astype(np.float32)), torch.from_numpy(faces.astype(np.int64))
    marching.marching_cubes = marching_cubes
    sys.modules["torchmcubes"] = marching
    # 输入已在本地脚本去背景；上游工具仅在调用 remove_background 时才访问 rembg。
    sys.modules["rembg"] = types.ModuleType("rembg")
    from tsr.models.tokenizers import image
    def config_download(repo_id, filename):
        if (repo_id, filename) != ("facebook/dino-vitb16", "config.json"):
            raise ValueError("不允许意外访问网络")
        return str(weights / "dino.json")
    image.hf_hub_download = config_download
    from tsr.system import TSR
    return TSR


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("image", type=Path)
    p.add_argument("work", type=Path)
    p.add_argument("--part", choices=("head", "body", "both"), default="both")
    p.add_argument("--preview", action="store_true")
    p.add_argument("--resolution", type=int, default=144)
    args = p.parse_args()
    work = args.work.resolve()
    output = work / "output"
    output.mkdir(exist_ok=True)
    inputs = list(prepare(args.image, output, work))
    root = work / "TripoSR"
    device = "cuda:0" if torch.cuda.is_available() else "cpu"
    print("推理设备：", device, flush=True)
    TSR = use_local_backend(root, work)
    model = TSR.from_pretrained(str(work), "config.yaml", "model.ckpt").eval().to(device)
    model.renderer.set_chunk_size(8192 if device != "cpu" else 4096)
    torch.set_grad_enabled(False)
    for name, source in reversed(inputs):
        if args.part not in (name, "both"):
            continue
        codes = model([source], device=device)
        print("完成编码：", name, flush=True)
        mesh, = model.extract_mesh(codes, True, resolution=args.resolution)
        pieces = mesh.split(only_watertight=False)
        mesh = max(pieces, key=lambda item: item.area)
        mesh.fix_normals()
        mesh.export(output / f"{name}.ply")
        if args.preview:
            images, = model.render(codes, n_views=4, elevation_deg=0, height=192, width=192, return_type="np")
            iio.imwrite(output / f"{name}_views.png", (np.concatenate(images, axis=1).clip(0, 1) * 255).astype(np.uint8))
        print(name, len(mesh.vertices), len(mesh.faces), mesh.bounds.tolist(), flush=True)


if __name__ == "__main__":
    main()

