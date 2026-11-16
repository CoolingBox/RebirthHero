"""清理卡牌边缘毛糙：人物层 L2 + AI 平面线描卡框。

根因（2026-11-16 实机放大确认）：
  · 人物层 L2 由 AI 生图抠像而来，轮廓外带一圈半透明淡边（约 5k 像素），
    其中部分还带背景暖色溢色（沈孤鸿 18.5%）→ 放大后呈金黄毛刺。
  · 卡框 PNG 是绿幕/宣纸底双键控产物，保留 6~9% 半透明软边 → 深色卡面上呈细碎白点。

处理：
  1. alpha 硬化：低于 CUTOFF 的淡边直接透明，其余线性拉伸回 0~255，
     去掉"若有若无"的毛点，同时保留主轮廓的抗锯齿。
  2. 卡框额外压暗半透明像素（去掉抠像留下的亮白边）。

安全：人物层原图已有同目录 L2_raw.png；卡框处理前另存 *_soft.png 备份。
"""

import pathlib
import shutil
import numpy as np
import cv2
from PIL import Image

ROOT = pathlib.Path(r"D:\Godot\Projects\RebirthHero")
GALLERY = ROOT / "Resources" / "UI" / "Gallery"
BORDERS = ROOT / "Resources" / "UI" / "Borders"
CUTOFF = 40


def harden_alpha(rgba, darken_edge=False):
    a = rgba[..., 3].astype(np.float32)

    # 淡边清零，其余拉伸回满量程：消除半透明毛点，主轮廓仍有过渡
    a2 = np.clip((a - CUTOFF) * (255.0 / (255.0 - CUTOFF)), 0, 255)

    out = rgba.copy()
    out[..., 3] = a2.astype(np.uint8)

    if darken_edge:
        # 抠像残留的亮白边只在半透明处出现，压到 65% 亮度即可，不伤金色线描主体
        edge = (a2 > 0) & (a2 < 250)
        out[..., :3][edge] = (out[..., :3][edge].astype(np.float32) * 0.65).astype(np.uint8)

    return out


def clean_portraits():
    """从 L2_raw.png（含留白原图）重新裁剪再清理，保证重复运行结果一致。

    不能从当前 L2 读——那会让每次运行都在上一次的硬化结果上再硬化一次。
    """
    count = 0
    for l2 in sorted(GALLERY.glob("*/L2.png")):
        raw = l2.with_name("L2_raw.png")
        if not raw.exists():
            shutil.copy2(l2, raw)          # 补一份原图，保证可回滚

        src = Image.open(raw).convert("RGBA")
        bbox = src.split()[3].getbbox()
        if bbox is not None:
            l, t, r, b = bbox
            pad = 4                         # 与 CropPortraits.py 的 PAD 一致
            box = (max(0, l - pad), max(0, t - pad),
                   min(src.width, r + pad), min(src.height, b + pad))
            src = src.crop(box)

        data = np.array(src)
        Image.fromarray(harden_alpha(data), "RGBA").save(l2)
        count += 1
    return count


def clean_borders():
    """卡框用更严格的参数：线描是硬边装饰，不需要大片半透明过渡。

    · 阈值 140：低于它的淡边（抠像残留在透明区的一圈雾点）直接清零
    · 连通域去噪：面积 < 6px 的孤立碎点清掉，金线主体（连续大块）保留
    """
    count = 0
    for p in sorted(BORDERS.glob("frame_*.png")):
        if p.stem.endswith("_soft"):
            continue
        bak = p.with_name(p.stem + "_soft.png")
        if not bak.exists():
            shutil.copy2(p, bak)

        src = np.array(Image.open(bak).convert("RGBA"))
        a = src[..., 3].astype(np.float32)

        # 主体(>=160)保实、淡边(<90)清零、中间保留窄过渡。
        # 试过线性拉伸到 140 阈值，会把线描主体压成半透明，反而更虚。
        a2 = np.where(a >= 160, 255.0, np.where(a < 90, 0.0, a))

        mask = (a2 > 0).astype(np.uint8)
        n, labels, stats, _ = cv2.connectedComponentsWithStats(mask, 8)
        keep = np.zeros(mask.shape, np.uint8)
        for i in range(1, n):
            if stats[i, cv2.CC_STAT_AREA] >= 6:
                keep[labels == i] = 1
        a2 = a2 * keep

        out = src.copy()
        out[..., 3] = a2.astype(np.uint8)
        Image.fromarray(out, "RGBA").save(p)
        count += 1
    return count


def main():
    n1 = clean_portraits()
    n2 = clean_borders()
    print("清理人物层", n1, "张 / 卡框", n2, "张")
    print("回滚：人物层用 L2_raw.png，卡框用 *_soft.png")


if __name__ == "__main__":
    main()
