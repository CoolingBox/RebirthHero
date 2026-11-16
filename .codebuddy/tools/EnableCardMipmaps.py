"""给卡面插画层开启 mipmap 生成。

为什么需要：
    卡面显示约 199x257，而 L1 源图 896x1152 / 1806x2322，是 4.5x~9x 的大比例缩小。
    代码里 TextureFilter = LinearWithMipmaps，但导入预设 mipmaps/generate=false，
    GPU 取不到 mip 层 → 退化成纯双线性，只用 2x2 源像素代表 4.5x4.5 区域，
    产生混叠（细碎麻点、发丝闪烁、纹理糊），再好的原图也救不回来。

用法（任意目录执行）：
    python EnableCardMipmaps.py            开启
    python EnableCardMipmaps.py --off     关闭（回滚）
    python EnableCardMipmaps.py --stat    只统计，不改动

改完必须让 Godot 重新导入一次：
    Godot_v4.7.2-stable_mono_win64_console.exe --headless --path <工程根> --import

只动 L1/L2/L3/L4 的 .import，边框 frame_*.png 是 2:1 整数缩小、走 Nearest，
不需要 mipmap（开了也不采样）。
"""

import os
import sys
import pathlib

LAYERS = ("L1.png.import", "L2.png.import", "L3.png.import", "L4.png.import")


def find_root(start):
    cur = os.path.abspath(start)
    while True:
        if os.path.exists(os.path.join(cur, "project.godot")):
            return pathlib.Path(cur)
        parent = os.path.dirname(cur)
        if parent == cur:
            return pathlib.Path(os.path.dirname(os.path.abspath(__file__)))
        cur = parent


def main():
    args = sys.argv[1:]
    target = "true" if "--off" not in args else "false"
    stat_only = "--stat" in args

    root = find_root(os.path.dirname(os.path.abspath(__file__)))
    gallery = root / "Resources" / "UI" / "Gallery"
    if not gallery.is_dir():
        print("找不到 Gallery 目录:", gallery)
        return

    changed = 0
    already = 0
    missing = 0
    for hero in sorted(gallery.iterdir()):
        if not hero.is_dir():
            continue
        for layer in LAYERS:
            p = hero / layer
            if not p.exists():
                missing += 1
                continue
            text = p.read_text(encoding="utf-8")
            if "mipmaps/generate=" + target in text:
                already += 1
                continue
            if "mipmaps/generate=" not in text:
                missing += 1
                continue
            if not stat_only:
                old = "mipmaps/generate=true" if target == "false" else "mipmaps/generate=false"
                p.write_text(text.replace(old, "mipmaps/generate=" + target, 1), encoding="utf-8")
            changed += 1

    action = "统计" if stat_only else ("设为 " + target)
    print("%s：需改 %d 份、已是目标值 %d 份、无该参数 %d 份" % (action, changed, already, missing))
    if not stat_only and changed > 0:
        print("已改完，请运行 Godot --headless --import 重新导入。")


if __name__ == "__main__":
    main()
