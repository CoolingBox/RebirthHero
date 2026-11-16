# -*- coding: utf-8 -*-
"""清点 raw 目录：哪些角色有可信的人物/背景原图。

可信 = 文件名严格是 char_<角色>.png / bg_<角色>.png（agent 复制时按规范命名）。
其余时间戳命名的文件在并发期可能串过目录，一律**不可信**，需要重出。
"""
import os

RAW = r"D:\Godot\Projects\RebirthHero\.codebuddy\tmp\cardgen\raw"
OUT = r"D:\Godot\Projects\RebirthHero\.codebuddy\tmp\cardgen\inventory.txt"

names = set()
for d in os.listdir(RAW):
    p = os.path.join(RAW, d)
    if not os.path.isdir(p):
        continue
    if d.endswith("_char") or d.endswith("_bg"):
        names.add(d.rsplit("_", 1)[0])

lines = []
ok = []
for n in sorted(names):
    c = os.path.join(RAW, n + "_char", "char_" + n + ".png")
    b = os.path.join(RAW, n + "_bg", "bg_" + n + ".png")
    has_c, has_b = os.path.exists(c), os.path.exists(b)
    lines.append("%-10s char=%s bg=%s" % (n, "Y" if has_c else "-", "Y" if has_b else "-"))
    if has_c and has_b:
        ok.append(n)

lines.append("")
lines.append("可信成对: %d 个 -> %s" % (len(ok), "、".join(ok)))

with open(OUT, "w", encoding="utf-8") as f:
    f.write("\n".join(lines))
print("written", OUT, "pairs", len(ok))
