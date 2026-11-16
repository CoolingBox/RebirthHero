"""从 Super Pixel Effects Gigapack 生成战斗特效精灵表。

素材授权（license.txt）：商用 OK、随游戏打包 OK，
需在鸣谢中标注 "Pixel Art Assets - Will Tice / unTied Games"。
禁止把素材用作 AI 训练材料；此处仅做裁切/拼接后直接使用，不属于训练。

输出：
  Resources/UI/FxPixel/fxp_<相性>_bolt.png    投射物（多帧）
  Resources/UI/FxPixel/fxp_<相性>_impact.png  命中（多帧）
  每张高 128、宽 = 帧数×128，代码里 frames = width/FxPixel.FrameSize，
  不需要额外元数据（改 FRAME 时 FxPixel.FrameSize 必须同步）。

执行顺序：本脚本出图 → **Godot 重新导入** → FxPixel.cs 加载 → BattleFxLayer 按帧绘制。
⚠️ 覆盖已有 PNG 后，运行时（--path 直接启动）不会重导入，实机仍是旧图；
   必须用 `--headless --editor --quit` 跑一次编辑器重导入，否则改了等于没改。
"""

import pathlib
import re
import shutil
from PIL import Image, ImageDraw

PACK = pathlib.Path(r"D:\Res\2DRPG\Super Pixel Effects Gigapack\spritesheet")
OUT = pathlib.Path(r"D:\Godot\Projects\RebirthHero\Resources\UI\FxPixel")
PREVIEW = pathlib.Path(r"D:\AI\WorkBuddy\2026-10-20-00-40-59\preview")

FRAME = 128            # large 变体帧最大到 256×144，等比缩入 128 格比塞进 64 格保留更多细节
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

    for kind, table in (("bolt", BOLT), ("impact", HIT)):
        for cat, (group, anim, color) in table.items():
            d = find_variant(group, anim, color)
            if d is None:
                print("缺素材", kind, cat, group, anim)
                continue
            sheet, n = build_sheet(d)
            sheet.save(OUT / ("fxp_%s_%s.png" % (cat, kind)))
            rows.append((cat, kind, d.name, n))

    # 总览：每个相性一行，取命中动画的第 1 / 中间 / 末帧
    preview = Image.new("RGB", (3 * 80 + 150, len(HIT) * 74 + 30), (24, 28, 34))
    dr = ImageDraw.Draw(preview)
    for i, cat in enumerate(HIT.keys()):
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
