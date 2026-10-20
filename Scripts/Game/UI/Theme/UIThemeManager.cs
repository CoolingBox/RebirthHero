// 文件：UIThemeManager.cs
// 《轮回大侠》主题管理 —— 装载 .tres、切换、持久化、取色、广播
//
// 真值在哪：Resources/Theme/wuxia_*.tres，共 4 份。
//   一份 .tres 里同时装了两类东西：
//     1. 控件样式 —— Label / Button / Panel / HSlider 的字体、颜色、StyleBox（Godot 标准项）
//     2. 氛围色板 —— 存在自定义类型 "Wuxia" 下的 15 个色（bg_base / panel_fill / ...）
//       ↑ 因为 ColorRect 的底色是节点属性、不吃 Theme 样式，只能由代码喂，
//         所以把色板也塞进同一份资源，避免"样式在资源里、背景色在代码里"两份真值。
//
// 代码里不再有任何色值。改配色请直接编辑 .tres（编辑器里双击即可可视化改）。
//
// 为什么不再用代码构建 Theme：
//   构建出来的 Theme 每次都一样，而 .tres 可以直接在编辑器里调；两者并存时
//   必然出现"到底哪边生效"的疑问。现在只留一条路径：读 .tres。
//   代价是资源丢了会难看 —— 所以读取失败会打印错误并返回洋红兜底色，
//   宁可刺眼也不要静默白屏（白屏最难排查）。
//
// 主题切换的连锁反应：SetTheme → 写盘 → 广播 Changed → 各面板重建表现层。

using System;
using Godot;
using TinyFramework;

public enum UIThemeId
{
    Ink = 0,        // 水墨（浅色）
    Lacquer = 1,    // 朱漆（暖暗）
    Celadon = 2,    // 青瓷（冷暗）
    NightBlade = 3, // 夜行（暗）
}

/// <summary>色板键：与 .tres 里 "Wuxia" 类型下的颜色项一一对应。改键名要同步改资源。</summary>
public static class UIThemeKey
{
    public const string BgBase = "bg_base";
    public const string PanelFill = "panel_fill";
    public const string PanelBorder = "panel_border";
    public const string CardAlly = "card_ally";
    public const string CardEnemy = "card_enemy";
    public const string TextPrimary = "text_primary";
    public const string TextSecondary = "text_secondary";
    public const string TextMuted = "text_muted";
    public const string Accent = "accent";
    public const string AccentAlt = "accent_alt";
    public const string ButtonFill = "button_fill";
    public const string ButtonHover = "button_hover";
    public const string ButtonPressed = "button_pressed";
    public const string Separator = "separator";
    public const string InputFill = "input_fill";

    // 背景层参数（BackdropLayer 消费）：不是控件色，但同属配色，一并放资源里
    public const string BackdropTint = "backdrop_tint";   // 叠在底图上的主题色，alpha 即强度
    public const string BackdropShade = "backdrop_shade"; // 暗角色，alpha 即浓度
}

public static class UIThemeManager
{
    public const int Count = 4;

    /// <summary>.tres 里色板所在的自定义类型名。</summary>
    public const string PaletteType = "Wuxia";

    // 显示名与调性说明属于文案（不是皮肤），留在代码里。
    static readonly string[] Names =
    {
        "水墨",
        "朱漆",
        "青瓷",
        "夜行",
    };

    static readonly string[] Descs =
    {
        "宣纸留白 · 朱砂为印",
        "朱门漆器 · 鎏金为章",
        "雨过天青 · 淡金为饰",
        "夜雨江湖 · 金铁见血",
    };

    static readonly string[] ResPaths =
    {
        "res://Resources/Theme/wuxia_ink.tres",
        "res://Resources/Theme/wuxia_lacquer.tres",
        "res://Resources/Theme/wuxia_celadon.tres",
        "res://Resources/Theme/wuxia_nightblade.tres",
    };

    // 默认夜行（与深色视觉连续）；水墨是浅色主题，交由玩家主动选择。
    static int _id = (int)UIThemeId.NightBlade;

    // 资源按 id 缓存。"读不到"这个结论也只认一次，避免每次取色都碰一次磁盘。
    static Theme[] _cache;
    static bool[] _tried;

    /// <summary>主题切换后触发（参数为新主题 id）。订阅方负责重建自己的动态 UI。</summary>
    public static event Action<int> Changed;

    public static int Id { get { return _id; } }

    public static string ThemeName { get { return Names[Clamp(_id)]; } }

    public static string ThemeDesc { get { return Descs[Clamp(_id)]; } }

    /// <summary>当前主题的 Theme 资源（挂到 Control 根节点即可向下继承）。</summary>
    public static Theme Theme { get { return GetTheme(_id); } }

    // ============ 取色 ============
    // 色值一律来自 .tres。属性只是键的包装，避免在调用处散落字符串字面量。

    public static Color Color(string key) { return ColorOf(_id, key); }

    /// <summary>取任意主题的色（设置面板的色块预览需要同时展示 4 套）。</summary>
    public static Color ColorOf(int themeId, string key)
    {
        Theme theme = GetTheme(themeId);

        if (theme == null)
            return Colors.Magenta;

        if (!theme.HasColor(key, PaletteType))
        {
            GD.PrintErr("[UIThemeManager] .tres 缺少色板项: " + key + " @ " + ResPaths[Clamp(themeId)]);
            return Colors.Magenta;
        }

        return theme.GetColor(key, PaletteType);
    }

    public static Color BgBase { get { return Color(UIThemeKey.BgBase); } }
    public static Color PanelFill { get { return Color(UIThemeKey.PanelFill); } }
    public static Color PanelBorder { get { return Color(UIThemeKey.PanelBorder); } }
    public static Color CardAlly { get { return Color(UIThemeKey.CardAlly); } }
    public static Color CardEnemy { get { return Color(UIThemeKey.CardEnemy); } }
    public static Color TextPrimary { get { return Color(UIThemeKey.TextPrimary); } }
    public static Color TextSecondary { get { return Color(UIThemeKey.TextSecondary); } }
    public static Color TextMuted { get { return Color(UIThemeKey.TextMuted); } }
    public static Color Accent { get { return Color(UIThemeKey.Accent); } }
    public static Color AccentAlt { get { return Color(UIThemeKey.AccentAlt); } }
    public static Color ButtonFill { get { return Color(UIThemeKey.ButtonFill); } }
    public static Color ButtonHover { get { return Color(UIThemeKey.ButtonHover); } }
    public static Color ButtonPressed { get { return Color(UIThemeKey.ButtonPressed); } }
    public static Color Separator { get { return Color(UIThemeKey.Separator); } }
    public static Color InputFill { get { return Color(UIThemeKey.InputFill); } }

    // ============ 生命周期 ============

    /// <summary>从存档读回主题（GameRoot 启动时调用一次）。</summary>
    public static void Init()
    {
        int saved = 0;

        SettingData data = SettingManager.Data;
        if (data != null)
            saved = data.ThemeId;

        _id = Clamp(saved);
    }

    /// <summary>切换主题：立刻写盘 + 广播。相同主题不重复广播。</summary>
    public static void SetTheme(int themeId)
    {
        int id = Clamp(themeId);
        bool changed = id != _id;

        _id = id;

        SettingData data = SettingManager.Data;
        if (data != null)
        {
            data.ThemeId = id;
            SaveManager.Save(data);
        }

        if (changed && Changed != null)
            Changed(id);
    }

    /// <summary>把当前主题挂到某个面板根节点上（子节点自动继承）。</summary>
    public static void ApplyTo(Control root)
    {
        if (root == null)
            return;

        root.Theme = GetTheme(_id);
    }

    /// <summary>取某套主题资源。读不到返回 null（调用方需处理）。</summary>
    public static Theme GetTheme(int themeId)
    {
        int id = Clamp(themeId);

        EnsureCache();

        if (!_tried[id])
        {
            _tried[id] = true;
            _cache[id] = LoadThemeRes(id);
        }

        return _cache[id];
    }

    /// <summary>主题显示名（设置面板用）。</summary>
    public static string NameOf(int themeId) { return Names[Clamp(themeId)]; }

    /// <summary>主题调性说明（设置面板用）。</summary>
    public static string DescOf(int themeId) { return Descs[Clamp(themeId)]; }

    // ============ 内部 ============

    static Theme LoadThemeRes(int themeId)
    {
        string path = ResPaths[themeId];

        if (!ResourceLoader.Exists(path))
        {
            GD.PrintErr("[UIThemeManager] 主题资源不存在: " + path);
            return null;
        }

        Theme theme = ResourceLoader.Load<Theme>(path, "");
        if (theme == null)
            GD.PrintErr("[UIThemeManager] 主题资源损坏: " + path);

        return theme;
    }

    static void EnsureCache()
    {
        if (_cache != null && _tried != null)
            return;

        _cache = new Theme[Count];
        _tried = new bool[Count];
    }

    static int Clamp(int themeId)
    {
        if (themeId < 0) return 0;
        if (themeId >= Count) return Count - 1;
        return themeId;
    }
}
