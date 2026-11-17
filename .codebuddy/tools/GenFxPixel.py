"""从 Super Pixel Effects Gigapack 生成战斗特效精灵表。

素材授权（license.txt）：商用 OK、随游戏打包 OK，
需在鸣谢中标注 "Pixel Art Assets - Will Tice / unTied Games"。
禁止把素材用作 AI 训练材料；此处仅做裁切/拼接后直接使用，不属于训练。

输出：
  Resources/UI/FxPixel/fxp_<相性>_bolt.png    投射物（多帧）
  Resources/UI/FxPixel/fxp_<相性>_impact.png  命中（多帧）
  每张高 256、宽 = 帧数×256，代码里 frames = width/FxPixel.FrameSize，
  不需要额外元数据（改 FRAME 时 FxPixel.FrameSize 必须同步）。

执行顺序：本脚本出图 → **Godot 重新导入** → FxPixel.cs 加载 → BattleFxLayer 按帧绘制。
⚠️ 覆盖已有 PNG 后，运行时（--path 直接启动）不会重导入，实机仍是旧图；
   必须用 `--headless --editor --quit` 跑一次编辑器重导入，否则改了等于没改。
"""

import io
import math
import pathlib
import re
import shutil
import zipfile

import numpy as np
from PIL import Image, ImageDraw

PACK = pathlib.Path(r"D:\Res\2DRPG\Super Pixel Effects Gigapack\spritesheet")

# Super Pixel Projectiles Pack 4（同一作者 unTied Games）—— 它的 energy_blast_B
# 自带横向拖尾，比用 Gigapack 的径向爆发 + 代码铺残影更接近业界 ribbon。
# 由 SRC_BOLT 里的 ("zip", 动画名, 颜色) 引用。
PJ4_ZIP = pathlib.Path(r"D:\Res\2DRPG\Craftpix\Effect\projectiles.zip")

# CraftPix 特效包根目录（2026-11-17 起成为主要素材来源）
CRAFT = pathlib.Path(r"D:\Res\2DRPG\Craftpix\Effect")

# ============================================================
# 2026-11-17 全量重挑：按「武学分系语义」选素材，不再按旧的颜色表硬凑。
#
# 选择原则：
#   ① bolt 必须是**会飞的投射物**（球/弹/彗星/带尾），不能是柱子或爆发；
#      优先选自带拖尾的形（energy_blast / Fireball / comet / Water_whip），
#      这样代码不必再铺残影。
#   ② impact 必须是**命中那一瞬间的形**（斩击/冲击/电光/冰爆/爪痕），
#      不能是持续性的火墙、护盾、光柱。
#   ③ 同相性的 bolt 与 impact 气质要对得上：鞭是水鞭 + 电光，
#      医是冰针 + 冰爆，毒是绿毒弹 + 骷髅，兽是锯轮 + 爪痕。
#
# spec 三种写法：
#   ("zip",  动画名, 颜色)          → projectiles.zip 里的 pj4_* 序列帧
#   ("dir",  相对 CRAFT 的目录)      → CraftPix 包里的序列帧目录
#   ("pack", 分类, 动画名, 颜色)     → Super Pixel Gigapack（保留兜底用）
# ============================================================
SRC_BOLT = {
    "sword":     ("zip", "pj4_energy_blast_B", "blue"),      # 蓝色剑气，长尾
    # 2026-11-17 提像素：Fireball 内容只有 22x17（显示要放大 6 倍、糊成色块），
    # 换 10-magic-effects 的 fire（71x37，同为火焰带尾，像素量 6 倍）。
    "saber":     ("dir", r"10-magic-effects-pixel-art-pack/PNG/fire"),
    "fist":      ("dir", r"10-magic-effects-pixel-art-pack/PNG/comet"),
    "palm":      ("zip", "pj4_plasma_ball", "violet"),        # 掌风能量球
    # 小弹丸让给暗器了；腿法改用 energy_blast_A（48×32 带尾能量弹，像素量 8 倍），
    # 形态是「踢出的气劲」，比原来那颗 16×16 的小白点清楚得多。
    "leg":       ("zip", "pj4_energy_blast_A", "white"),
    "whip":      ("dir", r"water-effects-pixel-art-set/PNG/Water_whip"),
    # 暗器试过四版：scrap_metal（实为废铁面罩+绿火花，像小怪物）、
    # spikes（一排整齐细刺，三连发叠起来像栅栏）、Ball_animation（去色后是巨大网状球）、
    # ice（实为雪花+十字，帧间形态乱跳）。全部不合适 —— 素材库里没有「飞针/飞镖」。
    # 最终用小弹丸：形态是稳定旋转的白色弹丸，配合三连发就是「三枚暗器」，
    # 代码里再把暗器尺寸缩到 0.55 倍（见 BattleFxLayer.BoltSize），
    # 「小而快」的观感才对，也避免 16px 源被放大到 86px。
    "hidden":    ("zip", "pj4_energy_pellet", "white"),
    "lightness": ("dir", r"10-magic-effects-pixel-art-pack/PNG/tornado"),
    # 冰晶梭（81×20 横向）让给暗器当飞针了；医改用 Ice_spike 的冰棱
    # （29×58 竖，生成时会自动转横成 58×29）。语义仍是「寒冰/银针」。
    "medicine":  ("dir", r"water-effects-pixel-art-set/PNG/Ice_spike"),   # 冰棱
    "poison":    ("zip", "pj4_energy_blast_B", "green"),
    # 暗物质是 48x48 的小球；奇门该是「阵法」的形。
    # 注意不能和 qimen 的 impact 用同一张 Spiral —— 否则飞行与命中长得一样，
    # 读不出「打中了」。bolt 用 Black hole 的紫黑环（8 帧、52x51），
    # impact 才是 Spiral 的旋涡（95x94），一个飞一个炸，语义分开。
    "qimen":     ("dir", r"10-magic-sprite-sheet-effects-pixel-art/9 Black hole"),
    "zen":       ("dir", r"pixel-art-magic-sprite-effects-and-icons-pack/PNG/Midas_hand"),
    "beast":     ("zip", "pj4_sawblade", "orange"),           # 兽爪锯轮
}

SRC_HIT = {
    # 斩击类全部从 image/ 换到 png/ —— 后者是同一动画的高清版，
    # 内容像素 236x296 / 456x430，是 image 的 3~4 倍（image 只有 125x157 / 204x173）。
    "sword":     ("dir", r"free-slash-effects-sprite-pack/slash6/png"),   # 弧形斩
    "saber":     ("dir", r"free-slash-effects-sprite-pack/slash4/png"),   # 大弧斩
    "fist":      ("dir", r"10-magic-effects-pixel-art-pack/PNG/light"),    # 拳劲放射
    "palm":      ("dir", r"10-magic-sprite-sheet-effects-pixel-art/5 Explosion"),  # 紫爆，贴紫气
    "leg":       ("dir", r"pixel-art-magic-sprite-effects-and-icons-pack/PNG/Explosion"),
    "whip":      ("dir", r"10-magic-effects-pixel-art-pack/PNG/tesla_ball"),  # 紫电弧，比 43x106 更完整
    "hidden":    ("dir", r"10-magic-effects-pixel-art-pack/PNG/spikes"),  # 钉入的尖刺
    "lightness": ("dir", r"water-effects-pixel-art-set/PNG/Snowflake"),
    "medicine":  ("dir", r"water-effects-pixel-art-set/PNG/Ice_crystal"),
    "poison":    ("dir", r"10-magic-effects-pixel-art-pack/PNG/kill_all"),  # 毒骷髅
    "qimen":     ("dir", r"pixel-art-magic-sprite-effects-and-icons-pack/PNG/Spiral"),
    "zen":       ("dir", r"pixel-art-magic-sprite-effects-and-icons-pack/PNG/Sun_strike"),  # 金日，佛光
    # 爪痕从 Spikes（86x33 的尖刺）换成 slash7/png 的短弧爪（186x127，像素量 12 倍）。
    "beast":     ("dir", r"free-slash-effects-sprite-pack/slash7/png"),
}

# ============================================================
# 色调修正表（2026-11-17）
#
# 只用于「形态对、但颜色让它在实机里读错或消失」的素材。key = "<相性>_<bolt|impact>"。
#
#   lightness_bolt：tornado 平均色 (45,48,52)，战场背景约 (26,30,40) —— 几乎同色，
#     实机读成「一团黑羽毛/黑爪」，完全看不出是旋风。按亮度提亮到浅灰白。
#   hidden_bolt：spikes 是暗褐骨刺 (65,59,42)，读起来像「地上长的刺」；
#     提亮去色成浅银灰，才像「金属飞针」。
#   hidden_impact：Kraken 是蓝色碎片 (41,134,209)，蓝=水/冰，和暗器不搭；
#     几乎完全去色成银灰，保留「碎片四散」的形态。
#
# 做法：按原亮度重映射，形态由 alpha 与亮度结构保留，不动形状。
#   gamma 越小、暗部提得越狠；whiten 越大越接近纯白（1.0 = 完全去色）。
# 关掉只需把对应条目删掉。
# ============================================================
TONE = {
    "lightness_bolt":   {"gamma": 0.40, "gain": 1.05, "whiten": 0.82},
    # 暗器命中是 spikes（暗褐骨刺），提亮成浅银灰才像「钉入的金属刺」
    "hidden_impact":    {"gamma": 0.45, "gain": 1.25, "whiten": 0.55},
}


def _apply_tone(im, cfg):
    """按亮度把颜色重映射到浅灰白，alpha 与形状不变。"""
    a = np.array(im.convert("RGBA")).astype(np.float32)
    rgb = a[:, :, :3] / 255.0

    lum = 0.299 * rgb[:, :, 0] + 0.587 * rgb[:, :, 1] + 0.114 * rgb[:, :, 2]
    t = np.power(np.clip(lum, 0.0, 1.0), cfg["gamma"]) * cfg["gain"]
    t = np.clip(t, 0.0, 1.0)

    white = np.stack([t, t, t], axis=2)
    out = rgb * (1.0 - cfg["whiten"]) + white * cfg["whiten"]

    a[:, :, :3] = np.clip(out, 0.0, 1.0) * 255.0
    return Image.fromarray(a.astype(np.uint8), "RGBA")


def _natural(name):
    return [int(t) if t.isdigit() else t.lower() for t in re.split(r"(\d+)", name)]


def _split_sheet(im):
    """单张横排 spritesheet → 帧列表。宽度是高度的整数倍时按正方形切。"""
    if im.height > 0 and im.width > im.height and im.width % im.height == 0:
        n = im.width // im.height
        if n > 1:
            return [im.crop((i * im.height, 0, (i + 1) * im.height, im.height))
                    for i in range(n)]
    return [im]


def _crop_content(im):
    """按 alpha 包围盒裁掉空白：各包留白比例差别很大，不裁会让大小失控。"""
    box = im.split()[-1].getbbox()
    if box:
        return im.crop(box)
    return im


def _frames_of_pack(d):
    sheet = Image.open(d / "spritesheet.png").convert("RGBA")
    rects = parse_meta(d)
    if not rects:
        n = max(1, sheet.width // FRAME)
        rects = [(i * FRAME, 0, FRAME, FRAME) for i in range(n)][:MAX_FRAMES]
    return [sheet.crop((x, y, x + w, y + h)) for (x, y, w, h) in rects][:MAX_FRAMES]


def load_frames(spec):
    """按 spec 读出该动画的帧列表（PIL Image，RGBA）。"""
    kind = spec[0]

    if kind == "zip":
        if not PJ4_ZIP.exists():
            return []
        anim, color = spec[1], spec[2]
        key = anim + "_large_" + color
        with zipfile.ZipFile(PJ4_ZIP) as z:
            names = [n for n in z.namelist()
                     if key in n and "/PNG/" in n and n.lower().endswith(".png")]
            names.sort(key=_natural)
            out = []
            for n in names[:MAX_FRAMES]:
                with z.open(n) as f:
                    out.append(Image.open(io.BytesIO(f.read())).convert("RGBA"))
            return out

    if kind == "dir":
        d = CRAFT / spec[1]
        if not d.is_dir():
            print("缺目录", d)
            return []
        fs = sorted([p for p in d.iterdir() if p.suffix.lower() == ".png"],
                    key=lambda p: _natural(p.name))
        if not fs:
            return []
        if len(fs) == 1:
            return _split_sheet(Image.open(fs[0]).convert("RGBA"))
        return [Image.open(p).convert("RGBA") for p in fs[:MAX_FRAMES]]

    if kind == "pack":
        d = find_variant(spec[1], spec[2], spec[3])
        if d is None:
            return []
        return _frames_of_pack(d)

    return []


def _mass(im):
    """不透明像素数（alpha > 32），用来衡量视觉体量。"""
    h = im.split()[-1].histogram()
    return sum(h[33:])


def sheet_from_frames(frames, target_mass=16800):
    """把任意尺寸的帧统一成 FRAME 见方的精灵表。

    三个关键处理：
    ① **竖直素材转横向**：冰锥这类是竖的（bbox 43×110），当投射物飞起来是「细竖条」，
       和横向拖尾的语言不搭，按 bbox 判断后顺时针转 90°。
    ② **按视觉体量归一化**，不按最长边：13 个相性的不透明面积差到 8 倍，
       按最长边归一化后球状显得大、细条显得小。
       改为 s = sqrt(target_mass / mass)，再用帧格上限 clamp 防溢出。
    ③ **帧格 256 + 目标体量 16800（2026-11-17 提像素）**：
       FRAME 与 target_mass 同步翻倍，内容占格比例不变、**实机显示尺寸不变**，
       但高清素材（斩击 236×296、456×430）不再被压进 128 格丢掉一半以上像素。
       低分辨率素材（如 32×16 的能量弹）不受益 —— 它们的像素上限由源素材决定，
       只能靠换素材解决，不能靠调帧格。
    """
    cells = []
    for im in frames:
        im = _crop_content(im)

        if im.height > im.width * 1.35:
            im = im.rotate(-90, expand=True, resample=Image.NEAREST)

        mass = max(1, _mass(im))
        s = math.sqrt(target_mass / float(mass))
        fit = FRAME * 0.96 / float(max(im.width, im.height))
        s = min(s, fit)

        w = max(1, int(round(im.width * s)))
        h = max(1, int(round(im.height * s)))
        im = im.resize((w, h), Image.NEAREST)
        cell = Image.new("RGBA", (FRAME, FRAME), (0, 0, 0, 0))
        cell.paste(im, ((FRAME - w) // 2, (FRAME - h) // 2))
        cells.append(cell)

    out = Image.new("RGBA", (FRAME * len(cells), FRAME), (0, 0, 0, 0))
    for i, c in enumerate(cells):
        out.paste(c, (i * FRAME, 0))
    return out, len(cells)
OUT = pathlib.Path(r"D:\Godot\Projects\RebirthHero\Resources\UI\FxPixel")
PREVIEW = pathlib.Path(r"D:\AI\WorkBuddy\2026-10-20-00-40-59\preview")

FRAME = 256            # 2026-11-17 从 128 提到 256：斩击高清版内容有 236~456px，
                       # 塞进 128 格会被压掉一半以上像素；256 格能基本 1:1 保留。
                       # 与 FxPixel.FrameSize 必须同步。
MAX_FRAMES = 12

# 相性 → (分类, 动画名, 颜色)。
#
# 语义硬约束（2026-11-16 修「伤害技能冒心」）：
#   BattleManager.HookEvents 里 PlayHitFx **只在 OnDamage(amt > 0) 时调用**，
#   OnHeal / OnShield 只飘字、根本不播特效。也就是说 26 张贴图全部是
#   「打人 / 命中 / 飞行」语义，一张都不该是治疗、增益、庆祝或状态类。
#   旧表给 medicine 选了 spell_heal_001（末帧是白心形）、给 poison 选了
#   spell_poison_001（骷髅 debuff）、给 zen 选了 spell_absorb_001（吸收光点）、
#   给 qimen 选了 spell_ice_001（星星闪光）、给 lightness 选了烟雾 —— 全部换掉。
#
#   命中统一用 Impacts / Lightning / Splatters 的冲击、爆发、溅射；
#   投射物统一用方向性粒子爆发，靠编号与颜色区分相性，不用 heart / bubble /
#   sparkle / smoke / music / coin / party / star 这类非攻击素材。
HIT = {
    "sword":     ("Impacts", "directional_impact_001", "blue"),
    "saber":     ("Impacts", "directional_impact_002", "orange"),
    "fist":      ("Impacts", "symmetrical_impact_001", "yellow"),
    "palm":      ("Impacts", "symmetrical_impact_003", "violet"),
    "leg":       ("Impacts", "directional_impact_004", "green"),
    "whip":      ("Lightning", "lightning_burst_001", "violet"),
    "hidden":    ("Impacts", "directional_impact_005", "white"),
    "lightness": ("Impacts", "directional_impact_003", "white"),
    "medicine":  ("Impacts", "symmetrical_impact_002", "green"),
    "poison":    ("Splatters", "directional_splatter_003", "green"),
    "qimen":     ("Impacts", "stylized_impact_001", "violet"),
    "zen":       ("Impacts", "symmetrical_impact_005", "yellow"),
    "beast":     ("Splatters", "burst_splatter_002", "red"),
}
BOLT = {
    "sword":     ("Magic Bursts", "directional_particle_burst_001", "blue"),
    "saber":     ("Magic Bursts", "directional_particle_burst_002", "orange"),
    "fist":      ("Magic Bursts", "directional_particle_burst_003", "yellow"),
    "palm":      ("Magic Bursts", "directional_particle_burst_004", "violet"),
    "leg":       ("Magic Bursts", "directional_particle_burst_001", "green"),
    "whip":      ("Magic Bursts", "directional_particle_burst_002", "violet"),
    "hidden":    ("Magic Bursts", "directional_particle_burst_003", "white"),
    "lightness": ("Magic Bursts", "directional_particle_burst_002", "white"),
    "medicine":  ("Magic Bursts", "directional_particle_burst_001", "green"),
    "poison":    ("Magic Bursts", "directional_particle_burst_003", "green"),
    "qimen":     ("Magic Bursts", "directional_particle_burst_004", "violet"),
    "zen":       ("Magic Bursts", "directional_particle_burst_002", "yellow"),
    "beast":     ("Magic Bursts", "directional_particle_burst_004", "red"),
}

# 防回归：这些关键词一旦出现在映射表里就直接拒绝生成。
FORBIDDEN = ("heart", "heal", "absorb", "buff", "debuff", "party",
             "music", "coin", "star", "bubble", "sparkle", "smoke",
             "sleep", "dispel", "haste", "forge", "status_", "thorns",
             "attack_up", "defense_up", "death")


def check_semantics():
    bad = []
    for kind, table in (("bolt", BOLT), ("impact", HIT)):
        for cat, (group, anim, color) in table.items():
            for word in FORBIDDEN:
                if word in anim:
                    bad.append((kind, cat, anim, word))
    return bad

COLOR_FALLBACK = ["white", "black", "blue", "green", "orange", "red", "violet", "yellow"]


def find_variant(cat, anim, color):
    """找 <anim>_large_<color> 优先，其次 small；颜色缺失时按兜底顺序找。

    2026-11-16 改为 large 优先：特效在游戏里已放大 1.6 倍，
    small 的 32~48px 源帧被拉大两三倍后像素块过粗。
    """
    base = PACK / cat / anim
    if not base.is_dir():
        return None
    dirs = [d for d in base.iterdir() if d.is_dir()]
    for size in ("large", "small"):
        for c in [color] + COLOR_FALLBACK:
            want = "%s_%s_%s" % (anim, size, c)
            for d in dirs:
                if d.name == want and (d / "spritesheet.png").exists():
                    return d
    for d in dirs:
        if (d / "spritesheet.png").exists():
            return d
    return None


def parse_meta(d):
    """元数据每行： ... = x y w h"""
    f = d / "spritesheet.txt"
    if not f.exists():
        return []
    rects = []
    for line in f.read_text(encoding="utf-8", errors="ignore").splitlines():
        m = re.search(r"=\s*(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s*$", line)
        if m:
            rects.append(tuple(int(x) for x in m.groups()))
    return rects[:MAX_FRAMES]


def build_sheet(d):
    sheet = Image.open(d / "spritesheet.png").convert("RGBA")
    rects = parse_meta(d)
    if not rects:
        # 没元数据时按 64px 横切兜底
        n = max(1, sheet.width // FRAME)
        rects = [(i * FRAME, 0, FRAME, FRAME) for i in range(n)][:MAX_FRAMES]

    out = Image.new("RGBA", (FRAME * len(rects), FRAME), (0, 0, 0, 0))
    for i, (x, y, w, h) in enumerate(rects):
        cell = sheet.crop((x, y, x + w, y + h))

        # 等比缩放后居中贴入，保持宽高比：素材里有 128×72、140×50、256×144
        # 这类非方形帧，直接 resize 到正方形会把特效拉变形。
        scale = min(FRAME / float(cell.width), FRAME / float(cell.height))
        nw = max(1, int(round(cell.width * scale)))
        nh = max(1, int(round(cell.height * scale)))
        if (nw, nh) != cell.size:
            cell = cell.resize((nw, nh), Image.NEAREST)

        ox = (FRAME - nw) // 2
        oy = (FRAME - nh) // 2
        out.paste(cell, (i * FRAME + ox, oy))
    return out, len(rects)


def main():
    bad = check_semantics()
    if bad:
        for item in bad:
            print("语义不合格", item)
        raise SystemExit(1)

    OUT.mkdir(parents=True, exist_ok=True)
    rows = []

    for kind, table in (("bolt", SRC_BOLT), ("impact", SRC_HIT)):
        for cat, spec in table.items():
            frames = load_frames(spec)
            if not frames:
                print("缺素材", kind, cat, spec)
                continue

            # 色调修正是按「相性_槽位」登记的，不会误伤同相性的另一个槽位
            tone = TONE.get("%s_%s" % (cat, kind))
            if tone is not None:
                frames = [_apply_tone(f, tone) for f in frames]
                print("色调修正", kind, cat, tone)

            sheet, n = sheet_from_frames(frames)
            sheet.save(OUT / ("fxp_%s_%s.png" % (cat, kind)))

            # 投射物都有方向（头/尾），敌方从右往左飞时要镜像版。
            # impact 是中心爆发，翻转没有意义，不生成。
            if kind == "bolt":
                mirror = sheet.transpose(Image.FLIP_LEFT_RIGHT)
                mirror.save(OUT / ("fxp_%s_bolt_r.png" % cat))

            rows.append((cat, kind, str(spec[1])[-26:], n))

    # 总览：每个相性一行，取命中动画的第 1 / 中间 / 末帧
    preview = Image.new("RGB", (3 * 80 + 150, len(HIT) * 74 + 30), (24, 28, 34))
    dr = ImageDraw.Draw(preview)
    for i, cat in enumerate(SRC_HIT.keys()):
        p = OUT / ("fxp_%s_impact.png" % cat)
        if not p.exists():
            continue
        sheet = Image.open(p).convert("RGBA")
        n = sheet.width // FRAME
        picks = [0, n // 2, max(0, n - 1)]
        for k, fi in enumerate(picks):
            cell = sheet.crop((fi * FRAME, 0, (fi + 1) * FRAME, FRAME))
            bg = Image.new("RGBA", cell.size, (50, 56, 66, 255))
            bg.alpha_composite(cell)
            preview.paste(bg.convert("RGB"), (150 + k * 80, i * 74 + 20))
        dr.text((8, i * 74 + 38), cat, fill=(220, 220, 220))

    PREVIEW.mkdir(parents=True, exist_ok=True)
    preview.save(PREVIEW / "fxpixel_sheet_overview.png")
    print("生成", len(rows), "张精灵表 →", OUT)
    print("帧数范围", min(r[3] for r in rows), "~", max(r[3] for r in rows))
    for cat, kind, variant, n in rows:
        print("  %-10s %-6s %2d帧  %s" % (cat, kind, n, variant))
    print("总览图 →", PREVIEW / "fxpixel_sheet_overview.png")


if __name__ == "__main__":
    main()
