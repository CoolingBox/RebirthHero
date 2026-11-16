# -*- coding: utf-8 -*-
"""体检：找出被裁掉/被键控吃掉的角色。

判据（都是"看得见的残缺"的直接信号）：
  1. 墨迹贴到画布某一边（1px 内）→ 人物被裁切
  2. 墨迹包围盒高度 < 画布 60% → 人物被缩得太小或只剩半截
  3. 墨迹面积占比 < 8% → 大部分身体被键控吃掉
用法：python .codebuddy/tools/cardgen_check.py [--all]
"""
import os
import sys

import numpy as np
from PIL import Image

ROOT = r"D:\Godot\Projects\RebirthHero\Resources\UI\Gallery"
OLD = ("claude", "deepseek", "gemini", "glm", "gpt", "grok", "kimi", "qwen", "beast")


def main():
    names = sorted(os.listdir(ROOT))
    print("%-10s %-6s %-6s %-6s %s" % ("角色", "占比%", "高占比%", "贴边", "判定"))
    bad = []
    for d in names:
        p = os.path.join(ROOT, d)
        if not os.path.isdir(p) or d.startswith(OLD) or "_v" in d:
            continue
        l2 = os.path.join(p, "L2.png")
        if not os.path.exists(l2):
            continue
        a = np.array(Image.open(l2).convert("RGBA"))[:, :, 3]
        h, w = a.shape
        mask = a > 24
        area = mask.mean() * 100

        ys, xs = np.where(mask)
        if len(xs) == 0:
            print("%-10s 全透明！" % d)
            bad.append(d)
            continue
        bh = (ys.max() - ys.min() + 1) / h * 100
        bw = (xs.max() - xs.min() + 1) / w * 100

        edges = []
        if mask[:, 0:2].any():
            edges.append("左")
        if mask[:, w - 2:w].any():
            edges.append("右")
        if mask[0:2, :].any():
            edges.append("上")
        if mask[h - 2:h, :].any():
            edges.append("下")

        verdict = "ok"
        if edges:
            verdict = "裁切"
        elif area < 8 or bh < 60:
            verdict = "疑似残缺"
        if verdict != "ok":
            bad.append(d)
        print("%-10s %-6.1f %-6.1f %-6s %s" % (d, area, bh, "".join(edges) or "-", verdict))

    print("\n需要复查: %s" % ("、".join(bad) if bad else "无"))


if __name__ == "__main__":
    main()
