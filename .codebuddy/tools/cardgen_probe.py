# -*- coding: utf-8 -*-
"""诊断：打印一张图四角与中心的像素值、以及"接近纯绿"的像素占比。"""
import sys

import numpy as np
from PIL import Image

p = sys.argv[1]
im = Image.open(p)
print("mode", im.mode, "size", im.size)
rgb = np.array(im.convert("RGB")).astype(np.int16)
h, w = rgb.shape[:2]
for tag, (y, x) in {
    "左上": (6, 6),
    "右上": (6, w - 7),
    "左下": (h - 7, 6),
    "右下": (h - 7, w - 7),
    "中心": (h // 2, w // 2),
}.items():
    r, g, b = rgb[y, x]
    print("%s RGB=(%d,%d,%d)" % (tag, r, g, b))

d = np.sqrt((rgb[:, :, 0].astype(np.float32)) ** 2
            + (rgb[:, :, 1].astype(np.float32) - 255) ** 2
            + (rgb[:, :, 2].astype(np.float32)) ** 2)
print("接近纯绿(d<60) 占比 %.1f%%" % ((d < 60).mean() * 100))
print("接近纯绿(d<130) 占比 %.1f%%" % ((d < 130).mean() * 100))
