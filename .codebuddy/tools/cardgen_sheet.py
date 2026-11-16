# -*- coding: utf-8 -*-
"""把若干角色目录的 preview.png 拼成一张带名字的总览图，用于批量验收卡面。

用法：
    python .codebuddy/tools/cardgen_sheet.py out.png 名字1 名字2 ...
    python .codebuddy/tools/cardgen_sheet.py out.png --all     # 扫全部新角色目录
"""
import os
import sys

from PIL import Image, ImageDraw

ROOT = r"D:\Godot\Projects\RebirthHero\Resources\UI\Gallery"
PREVIEW_DIR = r"D:\Godot\Projects\RebirthHero\.codebuddy\tmp\preview"
OLD = ("claude", "deepseek", "gemini", "glm", "gpt", "grok", "kimi", "qwen", "beast")

CELL_W, CELL_H = 300, 400          # 卡面 300×386 + 底部名字条
COLS = 6


def card_image(name):
    """取一张"拼好的卡面"：优先用产线预览图，没有就现场叠 L1+L2+L3(+L4)。

    预览图 2026-11-15 起不再写进 Resources（游戏不读，白占显存），
    所以老目录里找不到 preview.png 是正常现象，不能因此判定素材缺失。
    """
    for p in (os.path.join(PREVIEW_DIR, name + ".png"),
              os.path.join(ROOT, name, "preview.png")):
        if os.path.exists(p):
            return Image.open(p).convert("RGB")

    base = None
    for layer in ("L1", "L2", "L3", "L4"):
        p = os.path.join(ROOT, name, layer + ".png")
        if not os.path.exists(p):
            continue
        im = Image.open(p).convert("RGBA")
        if base is None:
            base = Image.new("RGBA", im.size, (0, 0, 0, 255))
        base.alpha_composite(im)
    if base is None:
        return None
    return base.convert("RGB")


def collect(args):
    if "--all" in args:
        names = []
        for d in sorted(os.listdir(ROOT)):
            p = os.path.join(ROOT, d)
            if not os.path.isdir(p):
                continue
            if d.startswith(OLD) or "_v" in d:
                continue
            if os.path.exists(os.path.join(p, "L2.png")):
                names.append(d)
        return names
    return args


def load_font(size=22):
    """用系统黑体画标签，默认字体太小，缩略总览里根本认不出名字。"""
    for p in (r"C:\Windows\Fonts\msyh.ttc", r"C:\Windows\Fonts\simhei.ttf"):
        if os.path.exists(p):
            try:
                from PIL import ImageFont
                return ImageFont.truetype(p, size)
            except Exception:
                pass
    return None


def main():
    out = sys.argv[1]
    names = collect(sys.argv[2:])
    if not names:
        print("没有可用的角色目录")
        return

    font = load_font(22)
    rows = (len(names) + COLS - 1) // COLS
    sheet = Image.new("RGB", (CELL_W * COLS, CELL_H * rows), (18, 18, 20))
    draw = ImageDraw.Draw(sheet)

    for i, n in enumerate(names):
        im = card_image(n)
        if im is None:
            continue
        im = im.resize((CELL_W - 8, CELL_H - 30), Image.LANCZOS)
        x = (i % COLS) * CELL_W + 4
        y = (i // COLS) * CELL_H + 4
        sheet.paste(im, (x, y))
        draw.text((x + 6, y + CELL_H - 26), "%02d %s" % (i + 1, n), fill=(240, 235, 220), font=font)

    sheet.save(out)
    print("已保存 %s（%d 个角色）" % (out, len(names)))
    for i, n in enumerate(names):
        print("%02d %s" % (i + 1, n))


if __name__ == "__main__":
    main()
