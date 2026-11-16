"""实机截图颜色空间修正：线性帧缓冲 → sRGB PNG。

为什么需要：
    Godot 的 `Viewport.get_texture().get_image()` 返回**线性颜色空间**数据。
    直接 save_png() 写盘后，图片查看器按 sRGB 解释，画面会整体发黑、饱和度偏高。
    而窗口显示时显卡驱动会做正确的 sRGB 编码，所以"截图比实机暗"。

实测证据：ColorRect 设 Color(1, 0.5, 0)，帧缓冲取回 (1, 0.214, 0)；
          做 linear_to_srgb() 后回到 (1, 0.5, 0)。

用法：
    python FixShotSrgb.py <输入.png> [输出.png]
    python FixShotSrgb.py --all <目录>      批量修正目录内所有 png（生成 *_srgb.png）
"""

import pathlib
import sys
import numpy as np
from PIL import Image


def to_srgb(path, out=None):
    src = pathlib.Path(path)
    dst = pathlib.Path(out) if out else src.with_name(src.stem + "_srgb.png")

    a = np.array(Image.open(src).convert("RGB")).astype(np.float32) / 255.0
    enc = np.where(a <= 0.0031308,
                   a * 12.92,
                   1.055 * np.power(a, 1.0 / 2.4) - 0.055)
    Image.fromarray((np.clip(enc, 0, 1) * 255).astype(np.uint8)).save(dst)
    print("修正", src.name, "→", dst.name)
    return dst


def main():
    argv = sys.argv[1:]
    if not argv:
        print(__doc__)
        return

    if argv[0] == "--all":
        folder = pathlib.Path(argv[1])
        for p in sorted(folder.glob("*.png")):
            if p.stem.endswith("_srgb") or p.stem.startswith("compare"):
                continue
            to_srgb(p)
        return

    to_srgb(argv[0], argv[1] if len(argv) > 1 else None)


if __name__ == "__main__":
    main()
