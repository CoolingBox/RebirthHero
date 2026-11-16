# -*- coding: utf-8 -*-
"""按当前键控算法重排**全部**角色卡：从各处私有目录找原图，重新合成。

为什么要这个：2026-11-14 发现 alpha 软边过宽，人物整具半透明（背景从身上透出来，看着"糊"）。
修好 ramp 后必须把所有已合成的卡重跑一遍，否则旧卡还是透的。

原图来源（按优先级依次找，先找到先用）：
    raw/<名字>_char/char_<名字>.png      raw/<名字>_bg/bg_<名字>.png
    src_verified/<名字>_char.png         src_verified/<名字>_bg.png
    src1/<名字>_char.png                 src1/<名字>_bg.png
    raw3/gp3_<名字>_char.png             raw3/gp3_<名字>_bg.png
    raw4/<名字>_char.png                 raw4/<名字>_bg.png
    raw5/<名字>_char.png                 raw5/<名字>_bg.png
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cardgen_build as cb

BASE = r"D:\Godot\Projects\RebirthHero\.codebuddy\tmp\cardgen"
ROOT = cb.ROOT
OLD = cb.OLD

CANDIDATES = [
    (os.path.join(BASE, "raw", "{n}_char", "char_{n}.png"), os.path.join(BASE, "raw", "{n}_bg", "bg_{n}.png")),
    (os.path.join(BASE, "src_verified", "{n}_char.png"), os.path.join(BASE, "src_verified", "{n}_bg.png")),
    (os.path.join(BASE, "src1", "{n}_char.png"), os.path.join(BASE, "src1", "{n}_bg.png")),
    (os.path.join(BASE, "raw3", "gp3_{n}_char.png"), os.path.join(BASE, "raw3", "gp3_{n}_bg.png")),
    (os.path.join(BASE, "raw4", "{n}_char.png"), os.path.join(BASE, "raw4", "{n}_bg.png")),
    (os.path.join(BASE, "raw5", "{n}_char.png"), os.path.join(BASE, "raw5", "{n}_bg.png")),
    (os.path.join(BASE, "raw8", "{n}_char.png"), os.path.join(BASE, "raw8", "{n}_bg.png")),
]


def find_pair(n):
    for c_tpl, b_tpl in CANDIDATES:
        c = c_tpl.format(n=n)
        b = b_tpl.format(n=n)
        if os.path.exists(c):
            return c, (b if os.path.exists(b) else None)
    return None, None


def main():
    names = []
    for d in sorted(os.listdir(ROOT)):
        p = os.path.join(ROOT, d)
        if os.path.isdir(p) and not d.startswith(OLD) and "_v" not in d:
            names.append(d)

    done, miss = [], []
    for n in names:
        c, b = find_pair(n)
        if not c:
            miss.append(n)
            continue
        cb.build(n, c, "forest", b)
        done.append(n)

    print("\n重排 %d 个；缺原图 %d 个：%s" % (len(done), len(miss), "、".join(miss) if miss else "无"))


if __name__ == "__main__":
    main()
