"""平面线描边框：绿幕 + 宣纸底色 双键控，只留纹样线条。

AI 出的国风线描框把中央画成纯绿，但边框外圈是宣纸/底色。
只抠绿的话，底色会整块盖住卡面插画 —— 必须把"接近底色"的像素也转透明。

输入命名约定：raw_<key>.png  →  输出 frame_<key>.png
（用 key 命名而不是按文件名排序，避免中文排序把纹样对错，踩过这个坑。）

用法（任意目录执行）：
    python CutFlatFrames.py                          默认目录（旧流程）
    python CutFlatFrames.py --src <dir> --dst <dir>  指定目录
    python CutFlatFrames.py --preview               额外出一张拼图预览

不做 clamp_outward：细线边框本来就不需要外沿实心，强延展反而会把线糊成实心块。
不做 despill：它对全部半透明像素压 G，会把暖金线描染成洋红。
"""

import argparse
import os
import pathlib

import cv2
import numpy as np
from PIL import Image

OUT_SIZE = (406, 522)

# 中央强制镂空的内缩比例。AI 偶尔会把淡色纹样画进绿区（如冰裂网格），
# 绿幕键控认不出那些非绿色线条，会留下残影。卡框中央本就必须全透明，
# 直接清掉最稳，边框纹样都在更外圈，不会被误伤。
CENTER_INSET = 0.06


def work_root():
    """默认输入输出放在工作区 tmp 下；工程工具目录只存脚本。"""
    cur = os.path.abspath(os.path.dirname(__file__))
    while True:
        if os.path.exists(os.path.join(cur, "project.godot")):
            return pathlib.Path(cur)
        parent = os.path.dirname(cur)
        if parent == cur:
            break
        cur = parent
    # 脚本被复制到工作区独立运行时，退回当前目录
    return pathlib.Path(os.path.abspath(os.path.dirname(__file__)))


def repair_watermark(rgb):
    """右下角 AI 水印用同图左下角水平镜像覆盖。"""
    h, w = rgb.shape[:2]
    out = rgb.copy()
    bw, bh = int(w * 0.22), int(h * 0.07)
    x0, y0 = w - bw, h - bh
    out[y0:, x0:] = rgb[y0:, :bw][:, ::-1]
    return out


def sample_bg(rgb):
    """取外圈带像素的中位色作为底色。

    不能取四角均值：角上常有大块纹样会把底色带偏。
    也不能取"全图最亮的一批像素"：遇到赭红/深色底的图，最亮像素是纹样高光，
    底色被采得过亮，亮度判据会把整块彩色底判成"比底色暗"而保留下来。
    外圈带里底色占绝大多数，取中位数对纹样占比不敏感；再排除偏绿像素，
    避免绿幕从边缘混入。
    """
    h, w = rgb.shape[:2]
    f = rgb.astype(np.float32)
    bw, bh = max(1, int(w * 0.18)), max(1, int(h * 0.18))
    mask = np.zeros((h, w), dtype=bool)
    mask[:bh, :] = True
    mask[-bh:, :] = True
    mask[:, :bw] = True
    mask[:, -bw:] = True
    greenish = (f[..., 1] > f[..., 0] + 20) & (f[..., 1] > f[..., 2] + 20)
    mask &= ~greenish
    px = rgb[mask]
    if px.size == 0:
        px = rgb.reshape(-1, 3)
    return np.median(px, axis=0).astype(np.float32)


def double_key(rgb):
    bgr = cv2.cvtColor(rgb, cv2.COLOR_RGB2BGR)
    b = bgr[..., 0].astype(np.float32)
    g = bgr[..., 1].astype(np.float32)
    r = bgr[..., 2].astype(np.float32)

    # ① 绿幕
    greenish = (g > b + 8) & (g > r + 8)
    green_dist = np.sqrt(b * b + (g - 255) ** 2 + r * r)
    a_green = np.clip((green_dist - 60) / 70.0, 0, 1)
    a_green[greenish] = 0

    # ② 底色：按"比底色暗多少" + "饱和度" + "颜色距离" 三条判据取较强者。
    #   只用颜色距离会把极淡的墨线一起抠掉；亮度判据对淡墨线最有效；
    #   颜色距离对金线最有效，饱和度对朱砂点最有效。三者互补。
    fr = rgb[..., 0].astype(np.float32)
    fg = rgb[..., 1].astype(np.float32)
    fb = rgb[..., 2].astype(np.float32)
    bg = sample_bg(rgb)

    lum = 0.299 * fr + 0.587 * fg + 0.114 * fb
    bg_lum = 0.299 * bg[0] + 0.587 * bg[1] + 0.114 * bg[2]
    a_lum = np.clip((bg_lum - lum - 8) / 30.0, 0, 1)

    mx = np.maximum(np.maximum(fr, fg), fb)
    mn = np.minimum(np.minimum(fr, fg), fb)
    # 必须用"相对底色的饱和度差"。用绝对饱和度的话，赭红/深色底本身饱和度就高，
    # 整块彩色底会被当成纹样保留下来，抠成一条实心色带盖住卡面插画。
    bg_mx = float(bg.max())
    bg_mn = float(bg.min())
    bg_sat = bg_mx - bg_mn
    a_sat = np.clip(((mx - mn) - bg_sat - 28) / 45.0, 0, 1)

    d = np.sqrt((fr - bg[0]) ** 2 + (fg - bg[1]) ** 2 + (fb - bg[2]) ** 2)
    a_dist = np.clip((d - 18) / 42.0, 0, 1)
    a_bg = np.maximum(np.maximum(a_lum, a_sat), a_dist)

    alpha = np.minimum(a_green, a_bg)
    a8 = (alpha * 255).astype(np.uint8)

    # 不做四边 floodFill：中央纯绿已由 greenish 清干净；底色在亮度判据下有
    # 微弱 alpha，floodFill 会从边缘蔓延到全图把金线一起误清（实测整套全透明）。
    return np.dstack([rgb[..., 0], rgb[..., 1], rgb[..., 2], a8])


def strip_band_bg(rgba):
    """深色/彩色"锦带"的二次键控。

    有些出图会把边框画成有底色的密纹装饰带（赭红鳞甲、深色竹编），
    双键控后仍是一整条半透明色带，会盖住卡面插画一圈。
    取外圈带中高 alpha 像素的中位色当作"锦带底色"，按颜色距离衰减 alpha，
    只留下偏离底色的线条。--deep-clean 显式开启，避免误伤普通浅底图。
    """
    h, w = rgba.shape[:2]
    rgb = rgba[..., :3].astype(np.float32)
    a = rgba[..., 3].astype(np.float32)
    bw, bh = max(1, int(w * 0.10)), max(1, int(h * 0.10))
    mask = np.zeros((h, w), dtype=bool)
    mask[:bh, :] = True
    mask[-bh:, :] = True
    mask[:, :bw] = True
    mask[:, -bw:] = True
    sel = mask & (a > 100)
    if sel.sum() < 100:
        return rgba
    bg = np.median(rgb[sel], axis=0)
    d = np.linalg.norm(rgb - bg, axis=2)
    factor = np.clip((d - 18) / 48.0, 0, 1)
    out = rgba.copy()
    out[..., 3] = (a * factor).astype(np.uint8)
    return out


def clear_center(rgba, inset):
    """把中央矩形强制透明：卡框中央必须镂空，顺便清掉画进绿区的淡色残纹。"""
    h, w = rgba.shape[:2]
    x0, x1 = int(w * inset), int(w * (1.0 - inset))
    y0, y1 = int(h * inset), int(h * (1.0 - inset))
    out = rgba.copy()
    out[y0:y1, x0:x1, 3] = 0
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--src", default="")
    ap.add_argument("--dst", default="")
    ap.add_argument("--preview", action="store_true")
    ap.add_argument("--deep-clean", default="",
                    help="逗号分隔的 key，对这些图做锦带二次键控（例：suojia,zhujie）。"
                         "必须在缩放后执行：验证过的阈值是针对 406x522 成品图的。")
    args = ap.parse_args()

    deep = set()
    for k in args.deep_clean.split(","):
        if k.strip():
            deep.add(k.strip())

    base = work_root()
    src = pathlib.Path(args.src) if args.src else base / "tmp" / "frames_flat"
    dst = pathlib.Path(args.dst) if args.dst else base / "tmp" / "frames_flat_cut"
    dst.mkdir(parents=True, exist_ok=True)

    files = sorted(src.glob("raw_*.png"))
    if not files:
        # 兼容旧流程：中文命名的原图，按关键词映射
        files = sorted(src.glob("*.png"))
    print("输入 %d 张  源目录 %s" % (len(files), src))

    made = []
    for path in files:
        name = path.stem
        if name.startswith("raw_"):
            name = name[4:]
        elif "工笔线描" in name or "细鎏金" in name:
            name = "huilan"
        elif "水墨平面" in name or "淡墨细线" in name:
            name = "liuyun"
        else:
            name = name[:12]

        pil = Image.open(path).convert("RGB")
        rgb = repair_watermark(np.array(pil))
        rgba = double_key(rgb)
        out = Image.fromarray(clear_center(rgba, CENTER_INSET), "RGBA").resize(
            OUT_SIZE, Image.LANCZOS)
        if name in deep:
            out = Image.fromarray(strip_band_bg(np.array(out)), "RGBA")
        target = dst / ("frame_%s.png" % name)
        out.save(target)
        made.append((name, target))

        a = np.array(out)[..., 3]
        h, w = a.shape
        print("  %-10s 中心α=%d 上边线区α均值=%5.1f 不透明占比=%5.1f%%" % (
            name, a[h // 2, w // 2], a[:20, :].mean(), 100.0 * (a > 128).mean()))

    if args.preview and made:
        sheet = Image.new("RGB", (len(made) * 250, 340), (34, 38, 46))
        for i, (name, p) in enumerate(made):
            im = Image.open(p).convert("RGBA")
            tile = Image.new("RGBA", im.size, (58, 64, 74, 255))
            tile.alpha_composite(im)
            tile = tile.convert("RGB")
            tile.thumbnail((220, 300), Image.LANCZOS)
            sheet.paste(tile, (i * 250 + 15, 30))
        from PIL import ImageDraw
        dr = ImageDraw.Draw(sheet)
        for i, (name, _) in enumerate(made):
            dr.text((i * 250 + 15, 8), name, fill=(225, 225, 225))
        preview = dst / "frames_preview.png"
        sheet.save(preview)
        print("预览拼图:", preview)


if __name__ == "__main__":
    main()
