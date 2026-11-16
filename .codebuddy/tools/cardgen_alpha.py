# -*- coding: utf-8 -*-
"""体检：找出"衣服被键控成半透明"的卡。

判据：alpha 落在 30~220 的像素占墨迹的比例。
正常图只有边缘抗锯齿是半透明（一般 < 12%）；如果 30% 以上，
说明整块衣服被当成了"接近背景色"，背景会从袍子里透出来。
"""
import os

import numpy as np
from PIL import Image

ROOT = r"D:\Godot\Projects\RebirthHero\Resources\UI\Gallery"
OLD = ("claude", "deepseek", "gemini", "glm", "gpt", "grok", "kimi", "qwen", "beast")

rows = []
for d in sorted(os.listdir(ROOT)):
    p = os.path.join(ROOT, d)
    if not os.path.isdir(p) or d.startswith(OLD) or "_v" in d:
        continue
    l2 = os.path.join(p, "L2.png")
    if not os.path.exists(l2):
        continue
    a = np.array(Image.open(l2).convert("RGBA"))[:, :, 3]
    ink = a > 24
    if ink.sum() == 0:
        continue
    semi = ((a > 30) & (a < 220)).sum() / ink.sum()
    rows.append((semi, d, os.path.exists(os.path.join(p, "L1.png"))))

rows.sort(reverse=True)
print("%-10s %-8s %s" % ("角色", "半透明%", "有背景"))
for semi, d, has_bg in rows:
    flag = "  ← 可疑" if semi > 0.25 else ""
    print("%-10s %-8.1f %s%s" % (d, semi * 100, "Y" if has_bg else "缺", flag))

missing_bg = [d for _, d, bg in rows if not bg]
print("\n缺背景: %s" % ("、".join(missing_bg) if missing_bg else "无"))
