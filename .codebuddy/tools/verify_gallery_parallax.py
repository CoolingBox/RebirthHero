"""验证《轮回大侠》卡册的 4 层视差：连截 3 张（卡在自动摆动），
逐格做相位相关，看位移是否按深度递增（框 0 / 背景小 / 人物中 / 前景大）。

卡面在屏幕上的位置：面板 offset(210,60) + TCardHolder offset(40,80) = (250,140)，尺寸 552×710。
"""
from PIL import Image
import numpy as np
import os

D = r'D:\Godot\Projects\RebirthHero\shots_gallery'
A = Image.open(os.path.join(D, 'gallery_a.png')).convert('L')
B = Image.open(os.path.join(D, 'gallery_b.png')).convert('L')
print('size', A.size)
print('两张分别是 tilt=(-0.9,0) 与 (0.9,0)：ΔTilt=1.8，位移 = -1.8 × ShiftPx(24) × depth')
print('理论：L4 卡框 0 / L1 背景 -8 / L2 人物 -24 / L3 前景 -43\n')

CX0, CY0, CW, CH = 250, 140, 552, 710
COLS, ROWS = 6, 8


def shift_est(box):
    ca = np.asarray(A.crop(box), dtype=np.float64)
    cb = np.asarray(B.crop(box), dtype=np.float64)
    if ca.size == 0:
        return 0, 0.0
    ca -= ca.mean()
    cb -= cb.mean()
    win = np.outer(np.hanning(ca.shape[0]), np.hanning(ca.shape[1]))
    ca *= win
    cb *= win
    fa = np.fft.fft2(ca)
    fb = np.fft.fft2(cb)
    prod = fb * np.conj(fa)
    mag = np.abs(prod)
    mag[mag < 1e-12] = 1e-12
    corr = np.fft.ifft2(prod / mag).real
    idx = np.unravel_index(np.argmax(corr), corr.shape)
    dy, dx = idx
    if dx > ca.shape[1] // 2:
        dx -= ca.shape[1]
    if dy > ca.shape[0] // 2:
        dy -= ca.shape[0]
    return int(dx), float(corr.max())


print('卡区间 dx（3 张之间的摆动位移；卡框应为 0，越靠前景越大）\n')
print('      ' + ''.join('{:>12}'.format('c%d' % c) for c in range(COLS)))
for r in range(ROWS):
    line = 'r%d  ' % r
    for c in range(COLS):
        x0 = CX0 + int(CW * c / COLS)
        x1 = CX0 + int(CW * (c + 1) / COLS)
        y0 = CY0 + int(CH * r / ROWS)
        y1 = CY0 + int(CH * (r + 1) / ROWS)
        dx, peak = shift_est((x0, y0, x1, y1))
        line += '{:>12}'.format('%d(%.2f)' % (dx, peak))
    print(line)
