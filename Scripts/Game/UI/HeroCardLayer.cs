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
    private const float CardW = 460f, CardH = 160f, Gap = 24f, TopY = 140f, SideX = 120f;

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
        var root = new Control { Position = new Vector2(x, y), Size = new Vector2(CardW, CardH) };
        var bg = new ColorRect { Color = right ? new Color(0.18f, 0.10f, 0.12f) : new Color(0.12f, 0.14f, 0.18f), Size = root.Size, Position = Vector2.Zero };
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
        var name = new Label { Text = slotTag + u.Name, Position = new Vector2(8, 4), Size = new Vector2(CardW - 16, 20), HorizontalAlignment = HorizontalAlignment.Center };
        name.AddThemeFontSizeOverride("font_size", 16);
        root.AddChild(name);

        var hpbg = new ColorRect { Color = new Color(0.30f, 0.10f, 0.10f), Position = new Vector2(8, 28), Size = new Vector2(CardW - 16, 16) };
        var hpfill = new ColorRect { Color = new Color(0.25f, 0.85f, 0.35f), Position = hpbg.Position, Size = hpbg.Size };
        // 银灰色段：被召唤武学锁死的生命上限（固定在血条最右端；宽度由 Refresh 按 LockedHp 计算）
        var hplock = new ColorRect { Color = new Color(0.50f, 0.52f, 0.55f), Position = hpbg.Position, Size = new Vector2(0, 16), Visible = false };
        root.AddChild(hpbg); root.AddChild(hpfill); root.AddChild(hplock);

        var hptext = new Label { Position = hpbg.Position, Size = hpbg.Size, HorizontalAlignment = HorizontalAlignment.Center };
        hptext.AddThemeFontSizeOverride("font_size", 12);
        root.AddChild(hptext);

        var gbg = new ColorRect { Color = new Color(0.10f, 0.10f, 0.32f), Position = new Vector2(8, 48), Size = new Vector2(CardW - 16, 10) };
        var gfill = new ColorRect { Color = new Color(0.35f, 0.55f, 1.0f), Position = gbg.Position, Size = gbg.Size };
        root.AddChild(gbg); root.AddChild(gfill);

        var stats = new Label { Position = new Vector2(8, 62), Size = new Vector2(CardW - 16, 18), Text = "ATK " + u.BaseAtk.ToString("0") + "  SPD " + u.BaseSpd.ToString("0") };
        stats.AddThemeFontSizeOverride("font_size", 12);
        root.AddChild(stats);

        var shtext = new Label { Position = new Vector2(8, 82), Size = new Vector2(CardW - 16, 18), Text = "" };
        shtext.AddThemeFontSizeOverride("font_size", 12);
        shtext.AddThemeColorOverride("font_color", new Color(0.4f, 0.9f, 1.0f));
        root.AddChild(shtext);

        var sktext = new Label { Position = new Vector2(8, 100), Size = new Vector2(CardW - 16, 16), Text = SkillsText(u) };
        sktext.AddThemeFontSizeOverride("font_size", 11);
        sktext.AddThemeColorOverride("font_color", new Color(0.72f, 0.74f, 0.86f));
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
            c.HpFill.Size = new Vector2((float)(barW * hpFrac), 16);
            c.HpFill.Color = u.Hp / u.BaseHp > 0.5 ? new Color(0.25f, 0.85f, 0.35f)
                : u.Hp / u.BaseHp > 0.25 ? new Color(0.95f, 0.85f, 0.2f) : new Color(0.9f, 0.25f, 0.2f);
            string hpText = Math.Max(0, u.Hp).ToString("0") + "/" + u.BaseHp.ToString("0");
            if (u.LockedHp > 0)
            {
                c.LockHpFill.Visible = true;
                c.LockHpFill.Position = new Vector2(8 + (float)(barW * availFrac), 28);
                c.LockHpFill.Size = new Vector2((float)(barW * (1 - availFrac)), 16);
                hpText += " 锁" + u.LockedHp.ToString("0");
            }
            else
            {
                c.LockHpFill.Visible = false;
            }
            c.HpText.Text = hpText;
            float gfrac = (float)Math.Clamp(u.Gauge / BattleEngine.GaugeMax, 0, 1);
            c.GaugeFill.Size = new Vector2((float)((CardW - 16) * gfrac), 10);
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
}
