// 文件：EquipIcon.cs
// 装备图标（P1 表现层）：把"模板 → 图标文件"的映射收在一处
//
// 命名约定（与 Resources/UI/Icons/Equip/ 一一对应）：
//   · 散件  → scatter_{slot}.png
//   · 套装  → set{SetId}_{slot}.png   （SetId 见 EquipSetConfig）
//   · slot  → weapon / armor / trinket / boots
//
// 为什么按"约定"取而不是给 EquipConfig 加 Icon 列：
//   44 行模板只有 7 种视觉（散件 + 6 套），在表里重复写 44 次路径只会让改文件名变成改表。
//   稀有度不进文件名 —— 稀有度靠**文字色 + 左侧色条**表达，图标只表达"这是什么、属于哪套"。
//
// 套装与散件的视觉区分（同屏可辨）：
//   · 套装件：朱砂色底 + 右下角"套"字
//   · 散件：中性灰底，无角标

using System.Collections.Generic;
using Godot;

public static class EquipIcon
{
    const string Root = "res://Resources/UI/Icons/Equip/";

    static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();

    /// <summary>模板 → 图标键（不含扩展名）。</summary>
    public static string KeyOf(EquipConfig cfg)
    {
        if (cfg == null) return null;

        string head = cfg.SetId > 0 ? ("set" + cfg.SetId) : "scatter";
        return head + "_" + SlotKey(cfg.Slot);
    }

    public static string SlotKey(EquipSlot slot)
    {
        switch (slot)
        {
            case EquipSlot.Weapon: return "weapon";
            case EquipSlot.Armor: return "armor";
            case EquipSlot.Trinket: return "trinket";
            case EquipSlot.Boots: return "boots";
            default: return "weapon";
        }
    }

    /// <summary>取图标（带缓存）。资源缺失返回 null，调用方按"无图"降级，不抛错。</summary>
    public static Texture2D Load(EquipConfig cfg)
    {
        string key = KeyOf(cfg);
        if (key == null) return null;

        Texture2D cached;
        if (Cache.TryGetValue(key, out cached)) return cached;

        string path = Root + key + ".png";
        Texture2D tex = null;
        if (ResourceLoader.Exists(path))
            tex = GD.Load<Texture2D>(path);

        Cache[key] = tex;
        return tex;
    }

    /// <summary>
    /// 造一个方形缩略图（色底 + 图标 + 套装角标）。
    /// size = 外框边长（UI 里给 40）；图标内缩 2px。
    /// </summary>
    public static Control BuildThumb(EquipConfig cfg, int size)
    {
        Control frame = new Control();
        frame.CustomMinimumSize = new Vector2(size, size);

        bool isSet = cfg != null && cfg.SetId > 0;

        // 底色：套装朱砂、散件中性灰 —— 这是"套装 vs 散件"最省事的一眼区分
        ColorRect bg = new ColorRect();
        bg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        if (isSet) bg.Color = new Color(0.55f, 0.16f, 0.11f, 0.38f);
        else bg.Color = new Color(1f, 1f, 1f, 0.06f);
        bg.MouseFilter = Control.MouseFilterEnum.Ignore;
        frame.AddChild(bg);

        Texture2D tex = Load(cfg);
        if (tex != null)
        {
            TextureRect img = new TextureRect();
            img.Texture = tex;
            img.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
            img.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
            img.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            img.OffsetLeft = 2;
            img.OffsetTop = 2;
            img.OffsetRight = -2;
            img.OffsetBottom = -2;
            img.MouseFilter = Control.MouseFilterEnum.Ignore;
            frame.AddChild(img);
        }
        else
        {
            Label none = new Label();
            none.Text = "?";
            none.AddThemeFontSizeOverride("font_size", 16);
            none.AddThemeColorOverride("font_color", UIThemeManager.TextMuted);
            none.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            none.HorizontalAlignment = HorizontalAlignment.Center;
            none.VerticalAlignment = VerticalAlignment.Center;
            frame.AddChild(none);
        }

        if (isSet)
        {
            // 角标底：图标本身的颜色深浅不定，直接压字会糊 —— 垫一层近黑的底
            ColorRect markBg = new ColorRect();
            markBg.Color = new Color(0.05f, 0.02f, 0.02f, 0.72f);
            markBg.CustomMinimumSize = new Vector2(20, 20);
            markBg.Position = new Vector2(size - 21, size - 21);
            markBg.MouseFilter = Control.MouseFilterEnum.Ignore;
            frame.AddChild(markBg);

            Label mark = new Label();
            mark.Text = "套";
            mark.AddThemeFontSizeOverride("font_size", 16);
            mark.AddThemeColorOverride("font_color", new Color(1f, 0.88f, 0.55f));
            mark.Position = new Vector2(size - 20, size - 23);
            mark.MouseFilter = Control.MouseFilterEnum.Ignore;
            frame.AddChild(mark);
        }

        return frame;
    }

    /// <summary>稀有度色条（3px 竖条），放在行首让眼睛先抓稀有度。</summary>
    public static Control BuildRarityBar(Rarity rarity, int height)
    {
        ColorRect bar = new ColorRect();
        bar.CustomMinimumSize = new Vector2(3, height);
        bar.Color = UISignalColors.RarityOf(rarity);
        bar.MouseFilter = Control.MouseFilterEnum.Ignore;
        return bar;
    }
}
