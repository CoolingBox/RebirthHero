# -*- coding: utf-8 -*-
"""给每个角色目录拼一张验收预览图，输出到 .codebuddy/tmp/preview/。

2026-11-15 起预览图不再写进 Resources（游戏不读它，进去只会让 Godot
多导入 46 张 2MB 的图白占显存），卡框也统一由 CardBorderFactory 提供。
"""
import os

from PIL import Image

ROOT = r"D:\Godot\Projects\RebirthHero\Resources\UI\Gallery"
OUT = r"D:\Godot\Projects\RebirthHero\.codebuddy\tmp\preview"
OLD = ("claude", "deepseek", "gemini", "glm", "gpt", "grok", "kimi", "qwen", "beast")


def main():
    os.makedirs(OUT, exist_ok=True)
    n = 0
    for d in sorted(os.listdir(ROOT)):
        p = os.path.join(ROOT, d)
        if not os.path.isdir(p) or d.startswith(OLD) or "_v" in d:
            continue
        if not os.path.exists(os.path.join(p, "L2.png")):
            continue
        base = Image.open(os.path.join(p, "L1.png")).convert("RGBA")
        for layer in ("L2", "L3", "L4"):
            f = os.path.join(p, layer + ".png")
            if os.path.exists(f):
                base.alpha_composite(Image.open(f).convert("RGBA"))
        base.convert("RGB").save(os.path.join(OUT, d + ".png"))
        n += 1
    print("生成预览 %d 张 -> %s" % (n, OUT))


if __name__ == "__main__":
    main()
