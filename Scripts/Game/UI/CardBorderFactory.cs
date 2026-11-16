// 文件：CardBorderFactory.cs
// 《轮回大侠》卡牌边框 —— AI 生图素材的共享纹理加载与品阶映射
//
// 素材来源：ImageGen 出图（绿幕 + 满幅贴边 + 平面线描提示词）
//   → `.codebuddy/tools/CutFlatFrames.py` 绿幕 + 底色双键控 → 中央强制镂空
//   → 406×522 透明 PNG（正好是卡面 203×261 的 2 倍）
//
// 复用原则：**每种纹样全局只有一张纹理**，所有卡牌共用；颜色不进素材、
// 由 Modulate / 主题负责，所以不为阵营、不为单卡重复出图。
//
// 2026-11-16：从 2 套扩到 6 套，并把枚举里三个"从未出图"的空项删掉。
// 早期 SVG 方案（8 个 border_*.svg）已停用，见文档 §11。

using System.Collections.Generic;
using Godot;

/// <summary>实际已出图的 6 套纹样。值不存档，只用于运行时查表。</summary>
public enum CardBorderKind
{
    HuiLan = 0,  // 鎏金回纹（细金线 + 卷云角 + 朱砂印）—— 默认
    LiuYun = 1,  // 水墨卷云（淡墨卷云 + 竹叶）
    ZhuJie = 2,  // 竹节（深色竹编分节 + 朱砂点）
    HanLie = 3,  // 冰裂（淡青冰裂纹，四角发散）
    SuoJia = 4,  // 锁甲（赭红鳞甲细纹）—— 首领
    YunLei = 5,  // 云雷（淡青云雷回旋纹 + 朱砂点）
}

public static class CardBorderFactory
{
    const string Dir = "res://Resources/UI/Borders/";

    static readonly Dictionary<CardBorderKind, Texture2D> _cache =
        new Dictionary<CardBorderKind, Texture2D>();

    /// <summary>纹样 → 素材文件。新增一套只需在这里加一项 + 放一张 PNG。</summary>
    static string FileOf(CardBorderKind kind)
    {
        switch (kind)
        {
            case CardBorderKind.LiuYun:
                return "frame_liuyun.png";
            case CardBorderKind.ZhuJie:
                return "frame_zhujie.png";
            case CardBorderKind.HanLie:
                return "frame_hanlie.png";
            case CardBorderKind.SuoJia:
                return "frame_suojia.png";
            case CardBorderKind.YunLei:
                return "frame_yunlei.png";
            default:
                return "frame_huilan.png";
        }
    }

    /// <summary>取某套纹样的共享纹理。同种纹样全局只有一张，重复调用直接命中缓存。</summary>
    public static Texture2D Get(CardBorderKind kind)
    {
        Texture2D tex;
        if (_cache.TryGetValue(kind, out tex))
        {
            return tex;
        }

        string path = Dir + FileOf(kind);
        tex = ResourceLoader.Load<Texture2D>(path, "");
        if (tex == null)
        {
            GD.PrintErr("[CardBorderFactory] 边框纹理缺失: " + path);
        }

        _cache[kind] = tex;
        return tex;
    }

    /// <summary>
    /// 品阶 → 纹样（1普通 / 2精良 / 3史诗 / 4传说 / 5神话）。
    /// 五档各给一套不同纹样，扫一眼就能分出货色；只是默认值，可按门派另行指定。
    /// </summary>
    public static CardBorderKind ForRarity(int rarity)
    {
        switch (rarity)
        {
            case 2:
                return CardBorderKind.ZhuJie;
            case 3:
                return CardBorderKind.HanLie;
            case 4:
                return CardBorderKind.LiuYun;
            case 5:
                return CardBorderKind.YunLei;
            default:
                return CardBorderKind.HuiLan;
        }
    }

    /// <summary>首领 / 重甲单位：锁甲最硬朗。目前尚未自动接线（缺"是否首领"判定来源）。</summary>
    public static CardBorderKind ForBoss()
    {
        return CardBorderKind.SuoJia;
    }

    /// <summary>
    /// 造一圈不透明实体衬边（环形，中央透明）。
    /// AI 线描边框是镂空的，卡面背景会一直铺到卡边、从金线外圈露出来，
    /// 看起来像"背景比边框大一圈"。这圈衬边把最外沿挡住，金线再叠在它上面。
    /// 战斗卡 / 卡册大卡 / 详情大卡三处共用，宽度按卡宽比例自适应。
    /// </summary>
    public static Panel CreateBacking(float width)
    {
        Panel panel = new Panel();
        panel.Name = "FrameBacking";
        panel.MouseFilter = Control.MouseFilterEnum.Ignore;

        StyleBoxFlat edge = new StyleBoxFlat();
        edge.BgColor = new Color(0f, 0f, 0f, 0f);
        edge.SetBorderWidthAll((int)width);
        edge.BorderColor = UIThemeManager.PanelBorder;
        panel.AddThemeStyleboxOverride("panel", edge);
        return panel;
    }

    /// <summary>
    /// 造一个铺满卡面的边框节点。
    /// AI 边框是**固定比例的整幅卡框**（不是九宫格），用 TextureRect 整体缩放，
    /// 九宫格会把线描纹样拉花。
    /// </summary>
    public static TextureRect CreateNode(CardBorderKind kind, Color tint)
    {
        TextureRect node = new TextureRect();
        node.Texture = Get(kind);
        node.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        node.StretchMode = TextureRect.StretchModeEnum.Scale;
        // 406×522 → 203×261 正好 2:1，必须 Nearest：Linear 会插值把 1px 金线
        // 洗成 2~3px 软边（实测半透明边 1.43% → 16.97%）。见文档 §14。
        node.TextureFilter = Control.TextureFilterEnum.Nearest;
        node.Modulate = tint;
        node.MouseFilter = Control.MouseFilterEnum.Ignore;
        return node;
    }
}
