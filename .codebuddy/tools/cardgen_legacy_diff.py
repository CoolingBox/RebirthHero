# -*- coding: utf-8 -*-
"""对比旧试验素材 L2 在 HEAD 与工作区的 alpha 差异，判断那次批量重排是不是修好了透明度。"""
import os
import subprocess
import tempfile

import numpy as np
from PIL import Image

REPO = r"D:\Godot\Projects\RebirthHero"
NAMES = ["claude_n", "claude_q", "gpt_q", "beast_naiwa", "grok_n"]


def semi_ratio(path):
    a = np.array(Image.open(path).convert("RGBA"))[:, :, 3]
    ink = a > 24
    if ink.sum() == 0:
        return 0.0
    return ((a > 30) & (a < 220)).sum() / ink.sum()


def main():
    print("%-12s %-10s %-10s" % ("角色", "HEAD半透明", "工作区半透明"))
    for n in NAMES:
        rel = "Resources/UI/Gallery/%s/L2.png" % n
        tmp = os.path.join(tempfile.gettempdir(), "head_%s.png" % n)
        with open(tmp, "wb") as f:
            subprocess.run(["git", "cat-file", "-p", "HEAD:" + rel], cwd=REPO, stdout=f, check=True)
        print("%-12s %-14.1f%% %-14.1f%%" % (n, semi_ratio(tmp) * 100,
                                             semi_ratio(os.path.join(REPO, rel)) * 100))
        os.remove(tmp)


if __name__ == "__main__":
    main()
