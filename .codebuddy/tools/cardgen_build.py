# -*- coding: utf-8 -*-
"""把云端出图（绿幕、无 alpha）加工成引擎用的分层卡面素材。

用法：
    python .codebuddy/tools/cardgen_build.py <角色名> <原图路径> [--kit 套件名] [--no-bg]

做的事：
    1. 抹掉右下角的水印（填成纯绿，否则抠像会把它当"人物"留下来）
    2. 绿幕键控 + 去绿溢色 → alpha
    3. 按人物墨迹外框裁成 7:9，并给头脚留边距 → 896×1152
    4. 落盘 Resources/UI/Gallery/<角色名>/L2.png
    5. 从套件目录借用 L1（背景）与 L3（前景）与 L4（卡框，全部套件共用同一张）
    6. 合成一张预览 preview.png 方便肉眼验收

铁律（见 skill aigen-transparent-layers）：
    · cv2 读写中文路径会静默失败 → 一律用 PIL 读、numpy 处理、PIL 写
    · 键控全程 BGR，写盘前转回 RGB，否则整张图会发青
"""

import os
import sys
import shutil

import numpy as np
import cv2
from PIL import Image

CARD_W, CARD_H = 896, 1152
ROOT = r"D:\Godot\Projects\RebirthHero\Resources\UI\Gallery"

# 验收预览图落盘处（故意不进 Resources：游戏不读，进去只是白占显存）
PREVIEW_DIR = r"D:\Godot\Projects\RebirthHero\.codebuddy\tmp\preview"

# 旧的 16 套试验素材 + 三兽，不属于"按角色名"的新目录，重排时要跳过
OLD = ("claude", "deepseek", "gemini", "glm", "gpt", "grok", "kimi", "qwen", "beast")

# 是否生成空的前景层（默认是）。要借用套件前景时用 --fg 关掉。
NO_FG = True

# 套件：借用的背景 / 前景（L4 卡框 16 套完全一致，任取其一）
KITS = {
    "forest": ("claude_n", "claude_n"),    # 秋林 / 落叶
    "night": ("grok_n", "grok_n"),         # 夜色 / 冷雾
    "temple": ("qwen_n", "qwen_n"),        # 室内 / 静物
    "snow": ("glm_n", "glm_n"),            # 雪原 / 飞雪
}


def read_rgb(path):
    """PIL 读图（cv2.imread 遇中文路径静默失败）。"""
    return np.array(Image.open(path).convert("RGB"))


def write_rgb(path, rgb):
    Image.fromarray(rgb).save(path)


def green_key(bgr):
    """绿幕键控。返回 (去溢色图, alpha)。

    ⚠️ 三条血泪（2026-11-14 一天内踩了两次）：
    1. **不能假设底色是纯绿**。云端出图的绿幕实测是 RGB(69,197,80)，到 (0,255,0) 的距离约 120 ——
       按"到纯绿的距离"算，整张图会被判成前景，随后 floodFill 的种子全灭，
       `ff` 全 0 时 floodFill 会把**整张图**灌满 → 人物被整张抹掉（花想容/女飞贼 等 11 张）。
       正确做法：**先从四角估出底色**，再按"到该色的距离"算 alpha。
    2. floodFill 的种子必须自己有底（`a8 < 40`），不能用 `<8` —— 阈值太紧就会出现"种子全 0"。
    3. 不用 `绿 = G 明显大于 R/B` 这种硬判据，它会把深绿色衣服当背景吃掉（毒尊那类配色）。
    """
    h, w = bgr.shape[:2]
    b, g, r = bgr[:, :, 0].astype(np.int16), bgr[:, :, 1].astype(np.int16), bgr[:, :, 2].astype(np.int16)

    # —— ① 四角估背景色（取 24px 角块的逐通道中位数，抗噪） ——
    k = 24
    corners = np.concatenate([
        bgr[0:k, 0:k].reshape(-1, 3), bgr[0:k, w - k:w].reshape(-1, 3),
        bgr[h - k:h, 0:k].reshape(-1, 3), bgr[h - k:h, w - k:w].reshape(-1, 3),
    ])
    bg = np.median(corners, axis=0).astype(np.float32)   # BGR

    # —— ② 距离键控（软边只留 28~58） ——
    # ⚠️ 2026-11-14 第三次修：原来软边是 40~110，结果**整具人物都被判成半透明**
    #（实测 阿蛮 32% / 苏小药 28% / 幽冥鬼卒 67% 的像素 alpha 落在 30~220），
    # 叠在洋红底上一眼就能看透 —— 原因是青灰、土褐这类低饱和颜色离绿幕只有 60~100。
    # 幕布是均匀的（同一个 prompt 出来的底色几乎一致），所以软边压到 28~58 就够：
    # 只有"几乎等于背景色"的像素才透明，衣服再灰也不会被吃掉。
    d = np.sqrt((b - bg[0]) ** 2 + (g - bg[1]) ** 2 + (r - bg[2]) ** 2).astype(np.float32)
    a8 = (np.clip((d - 28.0) / 30.0, 0.0, 1.0) * 255).astype(np.uint8)
    a8[d < 28.0] = 0          # 与底色几乎一致的像素肯定是背景（含腿间空隙）

    # —— ③ 从四边灌掉连通的背景（种子阈值 40，保证有底可灌） ——
    mask = np.zeros((h + 2, w + 2), np.uint8)
    ff = (a8 < 40).astype(np.uint8) * 255
    for seed in ((0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1)):
        if ff[seed[1], seed[0]] == 255:
            cv2.floodFill(ff, mask, seed, 128)
    a8[ff == 128] = 0

    # —— ④ despill：只压"偏绿且贴着背景色"的边缘，不碰深绿衣服 ——
    greenish = (g > r + 10) & (g > b + 10)
    edge = greenish & (d < 160)
    maxrb = np.maximum(b, r)
    out = bgr.copy()
    gp = np.minimum(g, maxrb + 12).astype(np.uint8)
    out[:, :, 1] = np.where(edge, gp, out[:, :, 1])
    return out, a8


def decontaminate(bgr, a8, steps=8):
    """把**边缘像素的颜色换成内侧颜色**（颜色去污染）。

    为什么必须做：绿幕键控只把不透明与否判对了，边缘那圈像素仍带着背景色。
    只压 G 通道（despill）不够 —— 青蓝（G+B）残留会变成发丝与白衣上的蓝青描边，
    玩家看到的就是"有杂色"。这里把内部颜色向外扩几步，边缘直接采用邻近的干净颜色。
    """
    inside = (a8 > 250).astype(np.uint8)
    m = inside.astype(bool)
    core = bgr.copy()
    k = np.ones((3, 3), np.uint8)

    for _ in range(steps):
        grown = cv2.dilate(core, k)
        gm = cv2.dilate(m.astype(np.uint8), k).astype(bool)
        fill = gm & (~m)
        core[fill] = grown[fill]
        m = gm

    out = bgr.copy()
    soft = (a8 <= 250)
    out[soft] = core[soft]
    return out


def sharpen(rgb, amount=0.65, radius=1.1):
    """反遮罩锐化：缩图（1024→896）之后会发虚，补一点高频回来。只作用于不透明部分。"""
    blur = cv2.GaussianBlur(rgb, (0, 0), radius)
    sharp = cv2.addWeighted(rgb, 1.0 + amount, blur, -amount, 0)
    return sharp


def drop_specks(a8, keep_ratio=0.08):
    """去掉零碎小块（右下角水印、背景残渣）。

    原来是把右下角一整块方形区域涂绿 —— 那会**连人物的脚一起抹掉**（玩家反馈"缺腿"）。
    改成按连通域保留：只留最大的那块（人物），以及不小于它 8% 的次要块（分离的兵器/兽）。
    """
    n, labels, stats, _ = cv2.connectedComponentsWithStats((a8 > 12).astype(np.uint8), 8)
    if n <= 1:
        return a8
    areas = stats[1:, cv2.CC_STAT_AREA]
    biggest = areas.max()
    out = a8.copy()
    for i in range(1, n):
        if stats[i, cv2.CC_STAT_AREA] < biggest * keep_ratio:
            out[labels == i] = 0
    return out


def fit_card(rgba):
    """把人物放进**卡框内开孔**（784×1040），而不是整张 896×1152。

    为什么：卡框画在最上层，内开孔只有 x 56~839 / y 56~1095。
    原来按整卡高 88% 排版，脚落在 1106px —— 正好被下边框吃掉，看着就是"没腿"。
    """
    a = rgba[:, :, 3]
    ys, xs = np.where(a > 12)
    out = np.zeros((CARD_H, CARD_W, 4), np.uint8)
    if len(xs) == 0:
        return out

    x0, x1 = xs.min(), xs.max()
    y0, y1 = ys.min(), ys.max()
    ch, cw = y1 - y0 + 1, x1 - x0 + 1

    # 卡框内开孔（由 claude_n/L4.png 实测）
    fx, fy, fw, fh = 56, 56, 784, 1040
    avail_w = fw * 0.94
    avail_h = fh * 0.90

    s = min(avail_w / cw, avail_h / ch)
    nw, nh = max(1, int(round(cw * s))), max(1, int(round(ch * s)))

    tight = rgba[y0:y1 + 1, x0:x1 + 1]
    tight = cv2.resize(tight, (nw, nh), interpolation=cv2.INTER_AREA)

    px = fx + (fw - nw) // 2
    py = fy + fh - nh - int(fh * 0.03)      # 脚底离内开孔下沿留 3%
    out[py:py + nh, px:px + nw] = tight
    return out


def cover_to_card(path):
    """背景层：等比铺满 896×1152 再居中裁切，输出不透明 RGB。"""
    im = Image.open(path).convert("RGB")
    w, h = im.size
    scale = max(CARD_W / w, CARD_H / h)
    nw, nh = int(round(w * scale)), int(round(h * scale))
    im = im.resize((nw, nh), Image.LANCZOS)
    left = (nw - CARD_W) // 2
    top = (nh - CARD_H) // 2
    return im.crop((left, top, left + CARD_W, top + CARD_H))


def build(name, src, kit="forest", bg_src=None):
    bgr = cv2.cvtColor(read_rgb(src), cv2.COLOR_RGB2BGR)
    no_green, a8 = green_key(bgr)
    a8 = drop_specks(a8)                        # 去掉水印/残渣，不动人物
    no_green = decontaminate(no_green, a8)      # 边缘颜色换成内侧色，去掉青蓝描边
    no_green = sharpen(no_green)
    rgba = np.dstack([no_green, a8])
    rgba = fit_card(rgba)

    dst = os.path.join(ROOT, name)
    os.makedirs(dst, exist_ok=True)
    # 统一用 RGBA 一次写盘：BGR→RGB 只在这里转一次，别分两步（分两步最容易把通道搞反）
    out_rgb = cv2.cvtColor(rgba[:, :, :3], cv2.COLOR_BGR2RGB)
    Image.fromarray(np.dstack([out_rgb, rgba[:, :, 3]]), "RGBA").save(os.path.join(dst, "L2.png"))

    bg_key, fg_key = KITS.get(kit, KITS["forest"])
    if bg_src:
        # 该角色专属背景：等比铺满再居中裁切
        cover_to_card(bg_src).save(os.path.join(dst, "L1.png"))
    else:
        shutil.copyfile(os.path.join(ROOT, bg_key, "L1.png"), os.path.join(dst, "L1.png"))

    # 前景层：**默认留空**（2026-11-14）。原来借 claude_n 的落叶，玩家一眼认出
    # "怎么用的是别人的背景"；而每人单独出一张前景等于再翻一倍成本。
    # 空 L3 不影响任何代码（GalleryCard 会把它算成"没有这层"），只是少一点纵深。
    if NO_FG:
        Image.new("RGBA", (CARD_W, CARD_H), (0, 0, 0, 0)).save(os.path.join(dst, "L3.png"))
    else:
        shutil.copyfile(os.path.join(ROOT, fg_key, "L3.png"), os.path.join(dst, "L3.png"))
    # 卡框层：2026-11-15 起卡框统一由 `UI/CardBorderFactory` 从
    # `Resources/UI/Borders/frame_*.png` 提供，卡面目录里不再各存一份 L4。
    # 这里保留"有就拷"的兼容分支，避免老目录（还有 L4 的）和产线行为不一致。
    legacy_l4 = os.path.join(ROOT, "claude_n", "L4.png")
    if os.path.exists(legacy_l4):
        shutil.copyfile(legacy_l4, os.path.join(dst, "L4.png"))

    # 预览：L1 + L2 + L3(+L4) 依次叠加。
    # ⚠️ 预览图**不写进 Resources**：游戏不读它，写进去只会让 Godot 多导入
    # 46 张 2MB 的图白占显存。放到 .codebuddy/tmp/preview/ 里给人工验收看。
    base = Image.open(os.path.join(dst, "L1.png")).convert("RGBA")
    layers = ["L2.png", "L3.png"]
    if os.path.exists(os.path.join(dst, "L4.png")):
        layers.append("L4.png")
    for layer in layers:
        base.alpha_composite(Image.open(os.path.join(dst, layer)).convert("RGBA"))
    os.makedirs(PREVIEW_DIR, exist_ok=True)
    base.convert("RGB").save(os.path.join(PREVIEW_DIR, name + ".png"))

    # 卡册缩略图（128 宽的合成小图）：卡册一次要铺几十张，
    # 直接读 L2 全尺寸每张 5MB，一排 20 张就是 100MB。
    thumb = base.convert("RGB")
    tw = 128
    thumb = thumb.resize((tw, int(round(tw * thumb.height / thumb.width))), Image.LANCZOS)
    thumb.save(os.path.join(dst, "thumb.png"))

    # 验收读数
    a = np.array(Image.open(os.path.join(dst, "L2.png")))[:, :, 3]
    print("%s  不透明像素 %.1f%%  边缘四角 α=%s" % (
        name, (a > 200).mean() * 100, [int(a[0, 0]), int(a[0, -1]), int(a[-1, 0]), int(a[-1, -1])]))


def pick(folder, prefer):
    """只认规范命名的文件（char_<名字>.png / bg_<名字>.png），**不做兜底**。

    为什么不兜底（2026-11-14 踩过）：并发出图时图会串目录，目录里留着别人的时间戳命名文件；
    以前"找不到就取最新一张"会把这些错图当成自己的用 —— 结果毒尊配成了苏小药。
    现在找不到就返回 None，调用方报缺、重出，宁可缺也不要错。
    """
    p = os.path.join(folder, prefer)
    if os.path.exists(p):
        return p
    return None


def rebuild_all():
    """把所有已有角色目录按当前算法重排一遍（改了裁切/键控之后用）。"""
    raw = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                       "tmp", "cardgen", "raw")
    names = []
    for d in sorted(os.listdir(ROOT)):
        if not os.path.isdir(os.path.join(ROOT, d)) or d.startswith(OLD) or "_v" in d:
            continue
        names.append(d)
    for n in names:
        char = pick(os.path.join(raw, n + "_char"), "char_" + n + ".png")
        bg = pick(os.path.join(raw, n + "_bg"), "bg_" + n + ".png")
        if not char:
            print("%s  跳过（没有人物原图）" % n)
            continue
        build(n, char, "forest", bg)


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("用法: cardgen_build.py <角色名> <人物原图> [--bg 背景原图] [--fg]")
        print("      cardgen_build.py --rebuild      # 全部按当前算法重排")
        sys.exit(1)
    if sys.argv[1] == "--rebuild":
        rebuild_all()
        sys.exit(0)
    if len(sys.argv) < 3:
        print("缺人物原图路径")
        sys.exit(1)
    kit = "forest"
    bg = None
    if "--fg" in sys.argv:
        NO_FG = False
    if "--kit" in sys.argv:
        kit = sys.argv[sys.argv.index("--kit") + 1]
    if "--bg" in sys.argv:
        bg = sys.argv[sys.argv.index("--bg") + 1]
    build(sys.argv[1], sys.argv[2], kit, bg)
