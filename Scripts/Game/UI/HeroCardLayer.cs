// 文件：HeroCardLayer.cs
// 《轮回大侠》表现层 —— **侠客单位卡**组件（Godot 节点：Control 子类，挂在 BattlePanel.tscn 的 TCards 上）
// 职责：为**侠客**（非召唤物）在本层内建卡并逐帧刷新：血条（含银灰锁死段）/ 行动条 / 护盾·沉默 / 武学行。
// 状态自持：卡表 _cards 私有；面板只经 Build / Refresh / Clear / TryGetAnchor 调用。
// 解耦边界：只读 BattleUnit 数据渲染，不含战斗数学；召唤物不归本类（见 SummonStackLayer）。

using System;
using System.Collections.Generic;
using Godot;

public partial class HeroCardLayer : Control
{
    // 尺寸与间距统一由 BattleLayout 提供（堆叠卡也要用 CardW 贴边，两处写死必然不同步）
    private const float CardW = BattleLayout.CardW;
    private const float CardH = BattleLayout.CardH;
    private const float Gap = BattleLayout.CardGap;
    private const float TopY = BattleLayout.TopY;
    private const float SideX = BattleLayout.SideX;

    private sealed class Card
    {
        public BattleUnit Unit;
        public Control Root;
        public ColorRect HpFill;
        public ColorRect LockHpFill;     // 银灰色段：被召唤武学锁死、不可用的生命（血条最右端）
        public ColorRect GaugeFill;
        public Label HpText;
        public Label ShieldText;
        public Vector2 BasePos;
        public Vector2 Size;
    }

    private readonly Dictionary<BattleUnit, Card> _cards = new();

    public void Clear()
    {
        foreach (var c in _cards.Values) c.Root.QueueFree();
        _cards.Clear();
    }

    /// <summary>A 队（左）/ B 队（右）各按 slot 自上而下建卡；只给侠客建，召唤物跳过。areaX = 视口宽。</summary>
    public void Build(List<BattleUnit> a, List<BattleUnit> b, float areaX)
    {
        float rx = areaX - SideX - CardW;
        for (int i = 0; i < 4; i++)
        {
            if (i < a.Count && !a[i].IsSummon) BuildOne(a[i], SideX, TopY + i * (CardH + Gap), false);
            if (i < b.Count && !b[i].IsSummon) BuildOne(b[i], rx, TopY + i * (CardH + Gap), true);
        }
    }

    private void BuildOne(BattleUnit u, float x, float y, bool right)
    {
        // 卡牌底属"氛围色"→ 随主题变；血条/护盾属"语义色"→ 所有主题共用（见 UISignalColors）。
        // 换主题时面板会重建卡牌，所以这里每次 Build 现取当前色板即可，不需要缓存。
        var root = new Control { Position = new Vector2(x, y), Size = new Vector2(CardW, CardH) };
        Color cardBg = right ? UIThemeManager.CardEnemy : UIThemeManager.CardAlly;
        var bg = new ColorRect { Color = cardBg, Size = root.Size, Position = Vector2.Zero };
        root.AddChild(bg);

        string slotTag;
        switch (u.Slot)
        {
            case 1: slotTag = "①"; break;
            case 2: slotTag = "②"; break;
            case 3: slotTag = "③"; break;
            case 4: slotTag = "④"; break;
            default: slotTag = ""; break;
        }
        var name = new Label { Text = slotTag + u.Name, Position = new Vector2(8, 6), Size = new Vector2(CardW - 16, 26), HorizontalAlignment = HorizontalAlignment.Center };
        name.AddThemeFontSizeOverride("font_size", 18);
        root.AddChild(name);

        var hpbg = new ColorRect { Color = UISignalColors.HpBack, Position = new Vector2(8, 38), Size = new Vector2(CardW - 16, BattleLayout.HpBarH) };
        var hpfill = new ColorRect { Color = UISignalColors.HpGood, Position = hpbg.Position, Size = hpbg.Size };
        // 银灰色段：被召唤武学锁死的生命上限（固定在血条最右端；宽度由 Refresh 按 LockedHp 计算）
        var hplock = new ColorRect { Color = UISignalColors.HpLocked, Position = hpbg.Position, Size = new Vector2(0, BattleLayout.HpBarH), Visible = false };
        root.AddChild(hpbg); root.AddChild(hpfill); root.AddChild(hplock);

        var hptext = new Label { Position = hpbg.Position, Size = hpbg.Size, HorizontalAlignment = HorizontalAlignment.Center };
        hptext.AddThemeFontSizeOverride("font_size", 14);
        root.AddChild(hptext);

        var gbg = new ColorRect { Color = UISignalColors.GaugeBack, Position = new Vector2(8, 64), Size = new Vector2(CardW - 16, BattleLayout.GaugeH) };
        var gfill = new ColorRect { Color = UISignalColors.GaugeFill, Position = gbg.Position, Size = gbg.Size };
        root.AddChild(gbg); root.AddChild(gfill);

        var stats = new Label { Position = new Vector2(8, 84), Size = new Vector2(CardW - 16, 22), Text = "ATK " + u.BaseAtk.ToString("0") + "  SPD " + u.BaseSpd.ToString("0") };
        stats.AddThemeFontSizeOverride("font_size", 14);
        root.AddChild(stats);

        var shtext = new Label { Position = new Vector2(8, 110), Size = new Vector2(CardW - 16, 22), Text = "" };
        shtext.AddThemeFontSizeOverride("font_size", 14);
        shtext.AddThemeColorOverride("font_color", UISignalColors.Shield);
        root.AddChild(shtext);

        var sktext = new Label { Position = new Vector2(8, 136), Size = new Vector2(CardW - 16, 22), Text = SkillsText(u) };
        sktext.AddThemeFontSizeOverride("font_size", 13);
        sktext.AddThemeColorOverride("font_color", UIThemeManager.TextSecondary);
        root.AddChild(sktext);

        AddChild(root);
        _cards[u] = new Card { Unit = u, Root = root, HpFill = hpfill, LockHpFill = hplock, GaugeFill = gfill, HpText = hptext, ShieldText = shtext, BasePos = new Vector2(x, y), Size = new Vector2(CardW, CardH) };
    }

    private static string SkillsText(BattleUnit u)
    {
        if (u.Skills == null || u.Skills.Count == 0) return "无";
        var parts = new List<string>();
        foreach (var s in u.Skills) parts.Add(s.Name);
        return "功 " + string.Join("·", parts);
    }

    public void Refresh(BattleEngine engine)
    {
        foreach (var c in _cards.Values)
        {
            var u = c.Unit;
            // 血条满格 = 可用上限(BaseHp) + 锁死上限(LockedHp)；彩色段=当前气血占满格比例，银灰段=锁死部分固定在最右端
            double total = u.BaseHp + u.LockedHp;
            double availFrac = total > 0 ? u.BaseHp / total : 1;
            double hpFrac = total > 0 ? Math.Clamp(u.Hp / total, 0, 1) : 0;
            float barW = CardW - 16;
            c.HpFill.Size = new Vector2((float)(barW * hpFrac), BattleLayout.HpBarH);
            c.HpFill.Color = u.Hp / u.BaseHp > 0.5 ? UISignalColors.HpGood
                : u.Hp / u.BaseHp > 0.25 ? UISignalColors.HpWarn : UISignalColors.HpBad;
            string hpText = Math.Max(0, u.Hp).ToString("0") + "/" + u.BaseHp.ToString("0");
            if (u.LockedHp > 0)
            {
                c.LockHpFill.Visible = true;
                c.LockHpFill.Position = new Vector2(BattleLayout.CardPadX + (float)(barW * availFrac), 38);
                c.LockHpFill.Size = new Vector2((float)(barW * (1 - availFrac)), BattleLayout.HpBarH);
                hpText += " 锁" + u.LockedHp.ToString("0");
            }
            else
            {
                c.LockHpFill.Visible = false;
            }
            c.HpText.Text = hpText;
            float gfrac = (float)Math.Clamp(u.Gauge / BattleEngine.GaugeMax, 0, 1);
            c.GaugeFill.Size = new Vector2((float)((CardW - 16) * gfrac), BattleLayout.GaugeH);
            c.ShieldText.Text = u.Shield > 0 ? "🛡 护盾 " + u.Shield.ToString("0") : (u.Silenced(engine.T) ? "⛔ 沉默" : "");
            c.Root.Modulate = u.Alive ? Colors.White : new Color(1, 1, 1, 0.3f);
        }
    }

    /// <summary>飘字锚点：侠客卡上方。</summary>
    public bool TryGetAnchor(BattleUnit u, out Vector2 pos)
    {
        if (_cards.TryGetValue(u, out var c))
        {
            pos = c.BasePos + new Vector2(c.Size.X * 0.5f - 60, 24);
            return true;
        }
        pos = Vector2.Zero;
        return false;
    }

    /// <summary>同上，但换算到**世界坐标**（供音频定位用；飘字仍用局部坐标）。</summary>
    public bool TryGetWorldAnchor(BattleUnit u, out Vector2 world)
    {
        if (!TryGetAnchor(u, out Vector2 local))
        {
            world = Vector2.Zero;
            return false;
        }

        world = GetGlobalTransform() * local;
        return true;
    }
}
