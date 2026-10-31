// 文件：UIThemeManager.cs
// 《轮回大侠》主题管理 —— 装载 .tres、切换、持久化、取色、广播
//
// 真值在哪：Resources/Theme/wuxia_*.tres，共 8 份。
//   一份 .tres 里同时装了两类东西：
//     1. 控件样式 —— Label / Button / Panel / HSlider 的字体、字号、颜色、StyleBox
//     2. 氛围色板 —— 存在自定义类型 "Wuxia" 下的 18 个色 + 2 个背景参数
//        （卡牌/堆叠卡这类运行时动态生成的 ColorRect 不吃 Theme，只能由代码喂，
//          所以色板也放进同一份资源，避免"样式在资源、颜色在代码"两份真值）
//
// 代码里不再有任何色值。配色由 `.codebuddy/tools/GenThemes.py` 统一生成到 .tres，
// 不要手改 .tres（下次生成会覆盖）。
//
// 结构与文案的分工：
//   · 名称 / 调性说明 / 明暗 / 资源路径 → 本文件的 Infos 表（结构信息，不是皮肤）
//   · 颜色 / 字号 / 圆角 / 描边 / 投影   → .tres（皮肤）
//   主题套数由 Infos 表长度决定，加一套主题 = 加一行 + 一份 .tres。
//
// ⚠️ id 顺序不可重排：SettingData.ThemeId 是 int 存档，
//    原 4 套（水墨0/朱漆1/青瓷2/夜行3）必须留在原位，新主题一律追加到末尾。
//
// 主题切换的连锁反应：SetTheme → 写盘 → 广播 Changed → 各面板重建表现层。

using System;
using Godot;
using TinyFramework;

public enum UIThemeId
{
    Ink = 0,        // 水墨（浅暖 · 纸）
    Lacquer = 1,    // 朱漆（深暖 · 漆器）
    Celadon = 2,    // 青瓷（深冷 · 釉）
    NightBlade = 3, // 夜行（深冷 · 铁）—— 默认
    Snow = 4,       // 雪夜（浅冷 · 寒玉）
    Tavern = 5,     // 市井（深暖 · 灯火）
    Bamboo = 6,     // 竹隐（浅冷 · 竹）
    Blood = 7,      // 血衣（深暖 · 修罗）
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
    public const string AccentDim = "accent_dim";
    public const string ButtonFill = "button_fill";
    public const string ButtonHover = "button_hover";
    public const string ButtonPressed = "button_pressed";
    public const string Separator = "separator";
    public const string InputFill = "input_fill";
    public const string CardBorderAlly = "card_border_ally";
    public const string CardBorderEnemy = "card_border_enemy";

    // 背景层参数（BackdropLayer 消费）：不是控件色，但同属配色，一并放资源里
    public const string BackdropTint = "backdrop_tint";   // 叠在底图上的主题色，alpha 即强度
    public const string BackdropShade = "backdrop_shade"; // 暗角色，alpha 即浓度
}

/// <summary>
/// 一套主题的元数据（结构信息）。**色值不在这里**，全在 .tres。
/// IsLight 决定背景层用宣纸山水还是夜雨江湖 —— 明暗是结构属性，不该靠 id 硬编码判断。
/// </summary>
public class UIThemeInfo
{
    public readonly int Id;
    public readonly string Key;      // 资源文件名：wuxia_<key>.tres
    public readonly string Name;     // 显示名
    public readonly string Desc;     // 调性说明
    public readonly string ResPath;
    public readonly bool IsLight;

    public UIThemeInfo(int id, string key, string name, string desc, bool isLight)
    {
        Id = id;
        Key = key;
        Name = name;
        Desc = desc;
        IsLight = isLight;
        ResPath = "res://Resources/Theme/wuxia_" + key + ".tres";
    }
}

public static class UIThemeManager
{
    /// <summary>.tres 里色板所在的自定义类型名。</summary>
    public const string PaletteType = "Wuxia";

    // id 即数组下标。新增主题只能追加，插入中间会让老存档的主题错位。
    static readonly UIThemeInfo[] Infos =
    {
        new UIThemeInfo(0, "ink", "水墨", "宣纸留白 · 朱砂为印", true),
        new UIThemeInfo(1, "lacquer", "朱漆", "朱门漆器 · 鎏金为章", false),
        new UIThemeInfo(2, "celadon", "青瓷", "雨过天青 · 淡金为饰", false),
        new UIThemeInfo(3, "nightblade", "夜行", "夜雨江湖 · 金铁见血", false),
        new UIThemeInfo(4, "snow", "雪夜", "雪拥蓝关 · 寒玉生烟", true),
        new UIThemeInfo(5, "tavern", "市井", "酒旗灯火 · 人间烟火", false),
        new UIThemeInfo(6, "bamboo", "竹隐", "竹林听雨 · 清风入怀", true),
        new UIThemeInfo(7, "blood", "血衣", "血衣修罗 · 白骨生花", false),
    };

    // 默认夜行（与深色视觉连续）；浅色主题交由玩家主动选择。
    static int _id = (int)UIThemeId.NightBlade;

    // 资源按 id 缓存。"读不到"这个结论也只认一次，避免每次取色都碰一次磁盘。
    static Theme[] _cache;
    static bool[] _tried;

    /// <summary>主题切换后触发（参数为新主题 id）。订阅方负责重建自己的动态 UI。</summary>
    public static event Action<int> Changed;

    public static int Count { get { return Infos.Length; } }

    public static int Id { get { return _id; } }

    public static string ThemeName { get { return InfoOf(_id).Name; } }

    public static string ThemeDesc { get { return InfoOf(_id).Desc; } }

    /// <summary>当前主题的 Theme 资源（挂到 Control 根节点即可向下继承）。</summary>
    public static Theme Theme { get { return GetTheme(_id); } }

    // ============ 元数据 ============

    public static UIThemeInfo InfoOf(int themeId) { return Infos[Clamp(themeId)]; }

    /// <summary>主题显示名（设置面板用）。</summary>
    public static string NameOf(int themeId) { return InfoOf(themeId).Name; }

    /// <summary>主题调性说明（设置面板用）。</summary>
    public static string DescOf(int themeId) { return InfoOf(themeId).Desc; }

    /// <summary>是否浅色主题。背景层据此选底图，新增浅色主题不必改背景层代码。</summary>
    public static bool IsLightOf(int themeId) { return InfoOf(themeId).IsLight; }

    // ============ 取色 ============
    // 色值一律来自 .tres。属性只是键的包装，避免在调用处散落字符串字面量。

    public static Color Color(string key) { return ColorOf(_id, key); }

    /// <summary>取任意主题的色（设置面板的色块预览需要同时展示全部套）。</summary>
    public static Color ColorOf(int themeId, string key)
    {
        Theme theme = GetTheme(themeId);

        if (theme == null)
        {
            return Colors.Magenta;
        }

        if (!theme.HasColor(key, PaletteType))
        {
            GD.PrintErr("[UIThemeManager] .tres 缺少色板项: " + key + " @ " + InfoOf(themeId).ResPath);
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
    public static Color AccentDim { get { return Color(UIThemeKey.AccentDim); } }
    public static Color ButtonFill { get { return Color(UIThemeKey.ButtonFill); } }
    public static Color ButtonHover { get { return Color(UIThemeKey.ButtonHover); } }
    public static Color ButtonPressed { get { return Color(UIThemeKey.ButtonPressed); } }
    public static Color Separator { get { return Color(UIThemeKey.Separator); } }
    public static Color InputFill { get { return Color(UIThemeKey.InputFill); } }
    public static Color CardBorderAlly { get { return Color(UIThemeKey.CardBorderAlly); } }
    public static Color CardBorderEnemy { get { return Color(UIThemeKey.CardBorderEnemy); } }

    // ============ 生命周期 ============

    /// <summary>从存档读回主题（GameRoot 启动时调用一次）。</summary>
    public static void Init()
    {
        int saved = 0;

        SettingData data = SettingManager.Data;
        if (data != null)
        {
            saved = data.ThemeId;
        }

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
        {
            Changed(id);
        }
    }

    /// <summary>把当前主题挂到某个面板根节点上（子节点自动继承）。</summary>
    public static void ApplyTo(Control root)
    {
        if (root == null)
        {
            return;
        }

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

    // ============ 内部 ============

    static Theme LoadThemeRes(int themeId)
    {
        string path = InfoOf(themeId).ResPath;

        if (!ResourceLoader.Exists(path))
        {
            GD.PrintErr("[UIThemeManager] 主题资源不存在: " + path);
            return null;
        }

        Theme theme = ResourceLoader.Load<Theme>(path, "");
        if (theme == null)
        {
            GD.PrintErr("[UIThemeManager] 主题资源损坏: " + path);
        }

        return theme;
    }

    static void EnsureCache()
    {
        if (_cache != null && _tried != null)
        {
            return;
        }

        _cache = new Theme[Count];
        _tried = new bool[Count];
    }

    static int Clamp(int themeId)
    {
        if (themeId < 0)
        {
            return 0;
        }
        if (themeId >= Count)
        {
            return Count - 1;
        }
        return themeId;
    }
}
