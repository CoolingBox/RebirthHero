"""《轮回大侠》主题生成器 —— 8 套武侠主题的唯一定义处。

用法（任意目录执行）：
    python .codebuddy/tools/GenThemes.py --check     WCAG 对比度 + 敌我卡牌区分度校验
    python .codebuddy/tools/GenThemes.py --preview   生成 HTML 效果预览（本目录下）
    python .codebuddy/tools/GenThemes.py --write     生成 8 份 .tres 到 Resources/Theme/

为什么用生成器而不是手写 8 份 .tres：
    8 套 × 13 个 StyleBox × 20 个色板键，手写必然漂移。定义只此一份，
    .tres 是产物（与项目已有的 GenConfig.py / GenUICode.py 同一惯例）。
    运行时只读 .tres，不依赖本脚本。

id 约束：原 4 套 id 0-3 顺序绝不可动 —— SettingData.ThemeId 是 int 存档，
         重排会让老存档的主题错位。新增主题一律往后排。
"""

import argparse
import os
import pathlib


def _find_project_root(start):
    cur = os.path.abspath(start)
    while True:
        if os.path.exists(os.path.join(cur, "project.godot")):
            return cur
        parent = os.path.dirname(cur)
        if parent == cur:
            return os.path.dirname(os.path.abspath(__file__))
        cur = parent


ROOT = _find_project_root(os.path.dirname(os.path.abspath(__file__)))
THEME_DIR = os.path.join(ROOT, "Resources", "Theme")

# ============================================================
# 主题定义
# ============================================================
# 色彩结构：主色(底/面板/按钮) + 辅色(分隔/描边，与主色拉冷暖差) + 强调色(accent 系)
# 材质：corner 圆角 / border 描边宽 / shadow 投影浓度 —— 四套以上必须靠材质区分，
#       否则只是换色相，"纸/漆/瓷/铁"的质感出不来。

THEMES = [
    dict(
        key="ink", name="水墨", desc="宣纸留白 · 朱砂为印",
        mood="light", corner=0, border=0, shadow=0.10,
        bg_base="#E9E0CB", panel_fill="#F4ECDC",
        card_ally="#EFE7D5", card_enemy="#E9C9B8",
        button_fill="#DED2B8", button_hover="#D1C3A3", button_pressed="#C3B290",
        input_fill="#FBF6EA",
        separator="#A9B8B4",          # 辅色：淡青灰，与暖米白拉冷暖差
        panel_border="#9A917E",
        text_primary="#1F1C17", text_secondary="#554E3F", text_muted="#7E7562",
        accent="#A32D1C", accent_alt="#8A5A38", accent_dim="#C08A76",
        card_border_ally="#A9B8B4", card_border_enemy="#C08A76",
        backdrop_tint=(0.92, 0.88, 0.78, 0.10), backdrop_shade=(0.30, 0.26, 0.20, 0.18),
    ),
    dict(
        key="lacquer", name="朱漆", desc="朱门漆器 · 鎏金为章",
        mood="dark", corner=0, border=2, shadow=0.42,
        bg_base="#1A0906", panel_fill="#2B100B",
        card_ally="#34140E", card_enemy="#521A10",
        button_fill="#42180E", button_hover="#58231A", button_pressed="#2D0C08",
        input_fill="#210B07",
        separator="#6B4E1F",          # 辅色：暗金，比纯红更有层次
        panel_border="#C9A227",
        text_primary="#F1E5C9", text_secondary="#B99E6D", text_muted="#8A7450",
        accent="#E4BD47", accent_alt="#B23A29", accent_dim="#8A6A2A",
        card_border_ally="#8A6A2A", card_border_enemy="#B23A29",
        backdrop_tint=(0.30, 0.08, 0.05, 0.22), backdrop_shade=(0.05, 0.01, 0.01, 0.58),
    ),
    dict(
        key="celadon", name="青瓷", desc="雨过天青 · 淡金为饰",
        mood="dark", corner=4, border=1, shadow=0.26,
        bg_base="#111A18", panel_fill="#1A2925",
        card_ally="#22312C", card_enemy="#2E2028",   # 敌方改暗紫红，修掉与 button_pressed 撞色
        button_fill="#243331", button_hover="#2F423C", button_pressed="#192622",
        input_fill="#0F1715",
        separator="#3D5A50",
        panel_border="#7FA99B",
        text_primary="#E5EEE8", text_secondary="#9DB1A6", text_muted="#748A81",
        accent="#D9C68C", accent_alt="#7FA99B", accent_dim="#5A7A6C",
        card_border_ally="#5A7A6C", card_border_enemy="#8A5A6A",
        backdrop_tint=(0.06, 0.14, 0.12, 0.20), backdrop_shade=(0.02, 0.05, 0.04, 0.55),
    ),
    dict(
        key="nightblade", name="夜行", desc="夜雨江湖 · 金铁见血",
        mood="dark", corner=0, border=1, shadow=0.30,
        bg_base="#090D12", panel_fill="#121A23",
        card_ally="#151F2A", card_enemy="#4E1626",
        button_fill="#1B242E", button_hover="#26323E", button_pressed="#0F171E",
        input_fill="#080C11",
        separator="#3A4A5A",          # 辅色：冷铁青，修掉与 button_hover 撞色
        panel_border="#5C6B7A",
        text_primary="#D9E1E9", text_secondary="#8D9AA8", text_muted="#66727F",
        accent="#CBA52A", accent_alt="#A8322A", accent_dim="#7A6420",
        card_border_ally="#3A4A5A", card_border_enemy="#7A3040",
        backdrop_tint=(0.06, 0.10, 0.16, 0.12), backdrop_shade=(0.01, 0.02, 0.04, 0.62),
    ),
    # ---- 以下为新增，id 4-7 ----
    dict(
        key="snow", name="雪夜", desc="雪拥蓝关 · 寒玉生烟",
        mood="light", corner=3, border=1, shadow=0.12,
        bg_base="#E4EAF0", panel_fill="#F0F4F8",
        card_ally="#E8EEF4", card_enemy="#EFE4E8",
        button_fill="#CFDAE4", button_hover="#BCCAD8", button_pressed="#A8B8C8",
        input_fill="#F7FAFC",
        separator="#9FB4C4",
        panel_border="#8FA4B6",
        text_primary="#182430", text_secondary="#45586A", text_muted="#788A9A",
        accent="#2E4A6B", accent_alt="#A34A5A", accent_dim="#7A93A8",
        card_border_ally="#9FB4C4", card_border_enemy="#C49AA6",
        backdrop_tint=(0.80, 0.86, 0.92, 0.14), backdrop_shade=(0.20, 0.28, 0.38, 0.20),
    ),
    dict(
        key="tavern", name="市井", desc="酒旗灯火 · 人间烟火",
        mood="dark", corner=6, border=1, shadow=0.30,
        bg_base="#1A120C", panel_fill="#281B12",
        card_ally="#2E2015", card_enemy="#4A1A12",
        button_fill="#3D2A18", button_hover="#523620", button_pressed="#2A1B0E",
        input_fill="#140D07",
        separator="#6B4A26",
        panel_border="#A87C3E",
        text_primary="#F0E2CC", text_secondary="#C0A582", text_muted="#8A7358",
        accent="#E8B84A", accent_alt="#A84A3A", accent_dim="#8A6230",
        card_border_ally="#8A6230", card_border_enemy="#A84A3A",
        backdrop_tint=(0.32, 0.20, 0.08, 0.24), backdrop_shade=(0.06, 0.03, 0.01, 0.52),
    ),
    dict(
        key="bamboo", name="竹隐", desc="竹林听雨 · 清风入怀",
        mood="light", corner=2, border=0, shadow=0.10,
        bg_base="#E4EADD", panel_fill="#EFF3E8",
        card_ally="#E9EFE0", card_enemy="#EDE6D8",
        button_fill="#D2DCC4", button_hover="#C0CEAE", button_pressed="#ADBE98",
        input_fill="#F6F9F0",
        separator="#9DB58F",
        panel_border="#8AA07C",
        text_primary="#1E2A20", text_secondary="#4A5A48", text_muted="#7A8676",
        accent="#3E6B4A", accent_alt="#7A6B4A", accent_dim="#8FAE8C",
        card_border_ally="#9DB58F", card_border_enemy="#B3A684",
        backdrop_tint=(0.82, 0.88, 0.76, 0.12), backdrop_shade=(0.24, 0.30, 0.20, 0.18),
    ),
    dict(
        key="blood", name="血衣", desc="血衣修罗 · 白骨生花",
        mood="dark", corner=0, border=2, shadow=0.46,
        bg_base="#120608", panel_fill="#200A0E",
        card_ally="#260D12", card_enemy="#47131C",
        button_fill="#3A1218", button_hover="#521A22", button_pressed="#280A0E",
        input_fill="#0C0405",
        separator="#5C222C",
        panel_border="#8E3A46",
        text_primary="#E8DCD8", text_secondary="#B09A96", text_muted="#7A6462",
        accent="#D4A04A", accent_alt="#C83A3A", accent_dim="#7A3A42",
        card_border_ally="#7A3A42", card_border_enemy="#C83A3A",
        backdrop_tint=(0.24, 0.04, 0.08, 0.26), backdrop_shade=(0.04, 0.01, 0.02, 0.60),
    ),
]

# 排版阶梯：四套以上主题必须共享同一套字号，否则换主题会像换了一个游戏
# 与项目 2026-10-21 全局 ×1.3 后的阶梯对齐（见 README「字号分层」）。
# TitleLabel/HeadingLabel 原本没有字号，这次补齐阶梯；
# Label / Button / default 保持原值不动 —— 项目里大量标签框是按 ×1.3 后的尺寸调的，
# 擅自改这三项会让没有显式 override 的控件溢出压行。
FONT_TITLE = 31
FONT_HEADING = 25
FONT_BODY = 22
FONT_HINT = 19
FONT_BUTTON = 22
FONT_LABEL = 16
FONT_DEFAULT = 19

FONT_TITLE_RES = "res://Resources/Fonts/LXGWZhenKaiGB-Regular.ttf"
FONT_BODY_RES = "res://Resources/Fonts/LXGWWenKaiGB-Medium.ttf"

PALETTE_KEYS = [
    "bg_base", "panel_fill", "panel_border", "card_ally", "card_enemy",
    "text_primary", "text_secondary", "text_muted",
    "accent", "accent_alt", "accent_dim",
    "button_fill", "button_hover", "button_pressed",
    "separator", "input_fill",
    "card_border_ally", "card_border_enemy",
]


# ============================================================
# 色彩工具
# ============================================================
def hex_to_rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def rgb_to_hex(rgb):
    return "#%02X%02X%02X" % tuple(round(c * 255) for c in rgb[:3])


def godot_color(rgb, alpha=1.0):
    return "Color(%.8g, %.8g, %.8g, %.8g)" % (rgb[0], rgb[1], rgb[2], alpha)


def luminance(rgb):
    def ch(c):
        return c / 12.92 if c <= 0.03928 else ((c + 0.055) / 1.055) ** 2.4
    r, g, b = ch(rgb[0]), ch(rgb[1]), ch(rgb[2])
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def contrast(a, b):
    la, lb = luminance(a), luminance(b)
    hi, lo = max(la, lb), min(la, lb)
    return (hi + 0.05) / (lo + 0.05)


def check():
    bad = 0
    for t in THEMES:
        bg = hex_to_rgb(t["bg_base"])
        panel = hex_to_rgb(t["panel_fill"])
        btn = hex_to_rgb(t["button_fill"])
        cases = [
            ("text_primary/bg_base", t["text_primary"], t["bg_base"], 7.0),
            ("text_primary/panel_fill", t["text_primary"], t["panel_fill"], 7.0),
            ("text_secondary/panel_fill", t["text_secondary"], t["panel_fill"], 4.5),
            ("text_muted/panel_fill", t["text_muted"], t["panel_fill"], 3.0),
            ("accent/panel_fill", t["accent"], t["panel_fill"], 4.5),
            ("accent/button_fill", t["accent"], t["button_fill"], 3.0),
            ("text_primary/button_fill", t["text_primary"], t["button_fill"], 4.5),
        ]
        print("== %-12s %s" % (t["key"], t["name"]))
        for label, fgk, bgk, need in cases:
            c = contrast(hex_to_rgb(fgk), hex_to_rgb(bgk))
            ok = c >= need
            if not ok:
                bad += 1
            print("   %-26s %5.2f  需>=%.1f  %s" % (label, c, need, "OK" if ok else "**不足**"))
        # 敌我卡牌必须可区分：明度差或色相差
        ca, ce = hex_to_rgb(t["card_ally"]), hex_to_rgb(t["card_enemy"])
        dl = abs(luminance(ca) - luminance(ce))
        print("   敌我卡牌明度差 %.4f %s" % (dl, "OK" if dl > 0.004 else "**过低**"))
        if dl <= 0.004:
            bad += 1
    print()
    print("未达标项：", bad)
    return bad


# ============================================================
# .tres 生成
# ============================================================
def stylebox(sid, bg, border_rgb=None, bw=0, corner=0,
             shadow_alpha=0.0, margin=(10, 5), alpha=1.0):
    lines = ['[sub_resource type="StyleBoxFlat" id="%s"]' % sid]
    if margin:
        lines += [
            "content_margin_left = %s" % float(margin[0]),
            "content_margin_top = %s" % float(margin[1]),
            "content_margin_right = %s" % float(margin[0]),
            "content_margin_bottom = %s" % float(margin[1]),
        ]
    lines.append("bg_color = " + godot_color(bg, alpha))
    if bw > 0:
        for side in ("left", "top", "right", "bottom"):
            lines.append("border_width_%s = %d" % (side, bw))
    if border_rgb is not None and bw > 0:
        lines.append("border_color = " + godot_color(border_rgb))
    if corner > 0:
        for c in ("top_left", "top_right", "bottom_right", "bottom_left"):
            lines.append("corner_radius_%s = %d" % (c, corner))
    if shadow_alpha > 0:
        lines.append("shadow_color = Color(0, 0, 0, %.3f)" % shadow_alpha)
        lines.append("shadow_size = 4")
        lines.append("shadow_offset = Vector2(0, 2)")
    return "\n".join(lines) + "\n"


def build_tres(t):
    corner, bw, shadow = t["corner"], t["border"], t["shadow"]
    C = {k: hex_to_rgb(t[k]) for k in PALETTE_KEYS}

    subs = []
    subs.append(stylebox("sb_btn_disabled", C["button_fill"], C["text_muted"], bw, corner, 0.0, alpha=0.45))
    subs.append(stylebox("sb_btn_focus", (0, 0, 0), C["accent"], max(bw, 2), corner, 0.0, alpha=0.0))
    subs.append(stylebox("sb_btn_hover", C["button_hover"], C["accent"], max(bw, 1), corner, 0.0))
    subs.append(stylebox("sb_btn_normal", C["button_fill"], C["panel_border"], bw, corner, 0.0))
    subs.append(stylebox("sb_btn_pressed", C["button_pressed"], C["accent"], max(bw, 1), corner, 0.0))
    subs.append(stylebox("sb_grabber", C["text_primary"], C["panel_border"], 1, 2, 0.0))
    subs.append(stylebox("sb_area", C["accent"], None, 0, corner, 0.0))
    subs.append(stylebox("sb_area_hi", C["accent"], C["accent_alt"], 1, corner, 0.0))
    subs.append(stylebox("sb_grabber_hi", C["accent"], C["accent_alt"], 1, 2, 0.0))
    subs.append(stylebox("sb_slider", C["input_fill"], C["button_hover"], 1, corner, 0.0))
    subs.append(stylebox("sb_panel", C["panel_fill"], C["panel_border"], bw, corner, shadow))
    subs.append(stylebox("sb_panelc", C["panel_fill"], C["panel_border"], bw, corner, shadow))
    subs.append(stylebox("sb_popup", C["panel_fill"], C["accent_dim"], max(bw, 1), corner, shadow))
    subs.append(stylebox("sb_bgfull", C["bg_base"], None, 0, 0, 0.0, margin=None))
    subs.append(stylebox("sb_bgpanel", C["panel_fill"], None, 0, 0, 0.0, margin=None))

    res = []
    res.append('default_font = ExtResource("2_body")')
    res.append("default_font_size = %d" % FONT_DEFAULT)

    res.append('BgFull/base_type = &"Panel"')
    res.append('BgFull/styles/panel = SubResource("sb_bgfull")')
    res.append('BgPanel/base_type = &"Panel"')
    res.append('BgPanel/styles/panel = SubResource("sb_bgpanel")')

    # 标题：臻楷 + 大字号；标题下压一道金线由分隔条承担，不在字体里做
    res.append('TitleLabel/base_type = &"Label"')
    res.append("TitleLabel/colors/font_color = " + godot_color(C["text_primary"]))
    res.append('TitleLabel/fonts/font = ExtResource("1_title")')
    res.append("TitleLabel/font_sizes/font_size = %d" % FONT_TITLE)

    res.append('HeadingLabel/base_type = &"Label"')
    res.append("HeadingLabel/colors/font_color = " + godot_color(C["accent"]))
    res.append('HeadingLabel/fonts/font = ExtResource("1_title")')
    res.append("HeadingLabel/font_sizes/font_size = %d" % FONT_HEADING)

    res.append('BodyLabel/base_type = &"Label"')
    res.append("BodyLabel/colors/font_color = " + godot_color(C["text_primary"]))
    res.append("BodyLabel/font_sizes/font_size = %d" % FONT_BODY)

    res.append('HintLabel/base_type = &"Label"')
    res.append("HintLabel/colors/font_color = " + godot_color(C["text_muted"]))
    res.append("HintLabel/font_sizes/font_size = %d" % FONT_HINT)

    res.append('AccentLabel/base_type = &"Label"')
    res.append("AccentLabel/colors/font_color = " + godot_color(C["accent"]))
    res.append("AccentLabel/font_sizes/font_size = %d" % FONT_BODY)

    res.append("Label/colors/font_color = " + godot_color(C["text_primary"]))
    res.append("Label/font_sizes/font_size = %d" % FONT_LABEL)

    res.append("Button/colors/font_color = " + godot_color(C["text_primary"]))
    res.append("Button/colors/font_disabled_color = " + godot_color(C["text_muted"]))
    res.append("Button/colors/font_focus_color = " + godot_color(C["accent"]))
    res.append("Button/colors/font_hover_color = " + godot_color(C["accent"]))
    res.append("Button/colors/font_pressed_color = " + godot_color(C["accent"]))
    res.append("Button/font_sizes/font_size = %d" % FONT_BUTTON)
    res.append('Button/styles/disabled = SubResource("sb_btn_disabled")')
    res.append('Button/styles/focus = SubResource("sb_btn_focus")')
    res.append('Button/styles/hover = SubResource("sb_btn_hover")')
    res.append('Button/styles/normal = SubResource("sb_btn_normal")')
    res.append('Button/styles/pressed = SubResource("sb_btn_pressed")')

    res.append('HSlider/styles/grabber = SubResource("sb_grabber")')
    res.append('HSlider/styles/grabber_area = SubResource("sb_area")')
    res.append('HSlider/styles/grabber_area_highlight = SubResource("sb_area_hi")')
    res.append('HSlider/styles/grabber_highlight = SubResource("sb_grabber_hi")')
    res.append('HSlider/styles/slider = SubResource("sb_slider")')

    res.append('Panel/styles/panel = SubResource("sb_panel")')
    res.append('PanelContainer/styles/panel = SubResource("sb_panelc")')
    res.append('PopupPanel/styles/panel = SubResource("sb_popup")')

    for k in PALETTE_KEYS:
        res.append("Wuxia/colors/%s = %s" % (k, godot_color(C[k])))

    # 背景层参数（RGBA 浮点，不是色板语义但同属配色）
    res.append("Wuxia/colors/backdrop_tint = Color(%.6f, %.6f, %.6f, %.6f)" % t["backdrop_tint"])
    res.append("Wuxia/colors/backdrop_shade = Color(%.6f, %.6f, %.6f, %.6f)" % t["backdrop_shade"])

    head = (
        '[gd_resource type="Theme" format=3]\n\n'
        '[ext_resource type="FontFile" path="%s" id="1_title"]\n'
        '[ext_resource type="FontFile" path="%s" id="2_body"]\n\n'
    ) % (FONT_TITLE_RES, FONT_BODY_RES)

    return head + "\n".join(subs) + "\n[resource]\n" + "\n".join(res) + "\n"


# ============================================================
# HTML 预览
# ============================================================
def build_html():
    cards = []
    for t in THEMES:
        c = {k: t[k] for k in PALETTE_KEYS}
        corner, bw = t["corner"], t["border"]
        sh = "0 2px 6px rgba(0,0,0,%.2f)" % t["shadow"] if t["shadow"] > 0 else "none"
        panel_style = (
            "background:%s;border:%dpx solid %s;border-radius:%dpx;box-shadow:%s;"
            % (c["panel_fill"], bw, c["panel_border"], corner, sh)
        )
        btn = (
            "background:%s;border:%dpx solid %s;border-radius:%dpx;color:%s;"
            % (c["button_fill"], bw, c["panel_border"], corner, c["text_primary"])
        )
        btn_hover = (
            "background:%s;border:%dpx solid %s;border-radius:%dpx;color:%s;"
            % (c["button_hover"], max(bw, 1), c["accent"], corner, c["accent"])
        )

        card_html = ""
        for label, key, bkey in (("我方", "card_ally", "card_border_ally"),
                                 ("敌方", "card_enemy", "card_border_enemy")):
            card_html += (
                '<div style="flex:1;background:%s;border:1px solid %s;border-radius:%dpx;'
                'padding:10px 8px;text-align:center">'
                '<div style="font-size:12px;color:%s">%s</div>'
                '<div style="font-size:19px;color:%s;margin-top:4px">%s</div>'
                '</div>'
            ) % (c[key], c[bkey], corner, c["text_muted"], label,
                 c["text_primary"], "侠客" if label == "我方" else "强敌")

        cards.append("""
<section class="theme" style="background:%(bg)s">
  <header>
    <h2 style="color:%(tp)s">%(name)s <span>%(key)s · id=%(id)d</span></h2>
    <p style="color:%(tm)s">%(desc)s</p>
    <p class="meta" style="color:%(tm)s">材质：圆角 %(corner)dpx · 描边 %(bw)dpx · 投影 %(shadow)s</p>
  </header>
  <div class="panel" style="%(panel)s">
    <div style="font-size:31px;color:%(tp)s;font-weight:600">论剑华山</div>
    <div style="font-size:25px;color:%(ac)s;margin-top:2px">第二百七十层 · 血刀门</div>
    <div style="font-size:22px;color:%(tp)s;margin-top:8px">银两 12.5万　修为 3.4亿</div>
    <div style="font-size:19px;color:%(tm)s;margin-top:2px">上下阵与站位变更在下一场战斗生效</div>
    <div class="sep" style="background:%(sep)s"></div>
    <div class="cards">%(cards)s</div>
    <div class="btnrow">
      <button style="%(btn)s">继 续 论 剑</button>
      <button style="%(btnh)s">悬 停 描 金</button>
    </div>
    <div class="slider">
      <div class="track" style="background:%(inp)s;border-color:%(bh)s;border-radius:%(corner)dpx">
        <div class="fill" style="background:%(ac)s;border-radius:%(corner)dpx"></div>
      </div>
      <span style="color:%(tm)s">音量</span>
    </div>
  </div>
</section>
""" % dict(
            bg=c["bg_base"], tp=c["text_primary"], tm=c["text_muted"],
            name=t["name"], key=t["key"], id=THEMES.index(t), desc=t["desc"],
            corner=corner, bw=bw, shadow=t["shadow"],
            panel=panel_style, ac=c["accent"], sep=c["separator"],
            cards=card_html, btn=btn, btnh=btn_hover,
            inp=c["input_fill"], bh=c["button_hover"],
        ))

    return """<!DOCTYPE html>
<html lang="zh-CN"><head><meta charset="utf-8">
<title>轮回大侠 · 主题预览（8 套）</title>
<style>
  * { box-sizing: border-box; }
  body { margin:0; padding:24px; background:#2b2b2b;
         font-family:"LXGW WenKai","Kaiti","KaiTi",serif; }
  h1 { color:#eee; font-size:22px; font-weight:600; margin:0 0 4px }
  .note { color:#999; font-size:13px; margin-bottom:20px }
  .grid { display:grid; grid-template-columns:repeat(auto-fill,minmax(430px,1fr)); gap:20px }
  .theme { padding:16px; border-radius:4px }
  .theme h2 { margin:0; font-size:20px; font-weight:600 }
  .theme h2 span { font-size:12px; opacity:.55; font-weight:400; margin-left:8px }
  .theme p { margin:3px 0; font-size:14px }
  .theme .meta { font-size:11px; opacity:.7 }
  .panel { margin-top:12px; padding:16px }
  .sep { height:1px; margin:12px 0 }
  .cards { display:flex; gap:10px; margin-bottom:12px }
  .btnrow { display:flex; gap:10px; margin-bottom:12px }
  button { flex:1; padding:9px 0; font-size:18px; font-family:inherit; cursor:default }
  .slider { display:flex; align-items:center; gap:10px; font-size:13px }
  .track { flex:1; height:6px; border:1px solid; overflow:hidden }
  .fill { width:62%; height:100% }
</style></head><body>
<h1>《轮回大侠》主题预览 · 8 套</h1>
<div class="note">由 build_themes.py 从同一份色板定义生成，与最终 .tres 严格一致。
重点看：主/辅/强调三色层次、敌我卡牌可区分度、金线是否进入结构、材质差异。</div>
<div class="grid">__CARDS__</div>
</body></html>""".replace("__CARDS__", "\n".join(cards))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--preview", action="store_true")
    ap.add_argument("--check", action="store_true")
    ap.add_argument("--write", action="store_true")
    args = ap.parse_args()

    if args.check:
        check()

    if args.preview:
        out = pathlib.Path(__file__).parent / "theme_preview.html"
        out.write_text(build_html(), encoding="utf-8")
        print("预览页：", out)

    if args.write:
        for t in THEMES:
            p = pathlib.Path(THEME_DIR) / ("wuxia_%s.tres" % t["key"])
            p.write_text(build_tres(t), encoding="utf-8")
            print("写出", p)


if __name__ == "__main__":
    main()
