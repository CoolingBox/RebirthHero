"""裁剪卡牌人物层 L2.png 的透明留白。

为什么裁：源图人物四周留白多，而 LayoutCharOver 按整图尺寸排版，
留白也被算进人物尺寸 —— 人物本体因此被稀释到卡宽的 45%。
裁到 alpha 边界后，"纹理尺寸"就等于"人物尺寸"，排版才谈得上放大。

安全：原图保留为同目录 L2_raw.png（已存在则跳过备份），可随时回滚。
只处理 L2（人物层），L1/L3/L4 不动。
"""

import os
import pathlib
from PIL import Image

def _find_root(start):
    cur = os.path.abspath(start)
    while True:
        if os.path.exists(os.path.join(cur, "project.godot")):
            return cur
        parent = os.path.dirname(cur)
        if parent == cur:
            return os.path.dirname(os.path.abspath(__file__))
        cur = parent


ROOT = pathlib.Path(_find_root(os.path.dirname(os.path.abspath(__file__)))) / "Resources" / "UI" / "Gallery"
PAD = 4  # 裁完留 4px 呼吸边，避免贴边太紧被 shader 渐隐切到

count = 0
for l2 in sorted(ROOT.glob("*/L2.png")):
    raw = l2.with_name("L2_raw.png")
    src = raw if raw.exists() else l2

    img = Image.open(src).convert("RGBA")
    alpha = img.split()[3]
    bbox = alpha.getbbox()
    if bbox is None:
        print("跳过（全透明）", l2.parent.name)
        continue

    l, t, r, b = bbox
    l = max(0, l - PAD)
    t = max(0, t - PAD)
    r = min(img.width, r + PAD)
    b = min(img.height, b + PAD)
    box = (l, t, r, b)

    if box == (0, 0, img.width, img.height):
        continue

    if not raw.exists():
        img.save(raw)          # 备份原图

    cropped = img.crop(box)
    cropped.save(l2)
    count += 1
    print("%-14s %dx%d → %dx%d  (留白裁掉 %.0f%%)" % (
        l2.parent.name, img.width, img.height, cropped.width, cropped.height,
        100 * (1 - cropped.width * cropped.height / (img.width * img.height))))

print("\n共裁剪", count, "张人物层")
