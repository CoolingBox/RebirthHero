// 文件：SummonStackLayer.cs
// 《轮回大侠》表现层 —— **召唤物堆叠卡**组件（Godot 节点：Control 子类，挂在 BattlePanel.tscn 的 TStacks 上）
// 口径（2026-10-16）："同一召唤物显示上可堆叠" —— 一个类型一张卡，显示【名字 ×N】+ 聚合血条 + 最快到期 TTL。
// 状态自持：堆叠表 _stacks / 顺序 _stackOrder 私有；面板只经 Sync / TryGetAnchor / Clear 调用。
// 解耦边界：只读 BattleUnit 数据渲染；召唤物的"多只"由引擎决定，本层只做归组显示。

using System.Collections.Generic;
using Godot;

public partial class SummonStackLayer : Control
{
    // 布局常量统一由 BattleLayout 提供：堆叠卡要贴着侠客卡右缘，必须和它共用 CardW
    private const float StackW = BattleLayout.StackW;
    private const float StackH = BattleLayout.StackH;
    private const float StackGap = BattleLayout.StackGap;
    private const float StackMargin = BattleLayout.StackMargin;
    private const float TopY = BattleLayout.TopY;
    private const float SideX = BattleLayout.SideX;
    private const float CardW = BattleLayout.CardW;

    private sealed class StackCard
    {
        public string Key = "";
        public string Name = "";
        public Control Root;
        public Label TitleText;
        public ColorRect HpFill;
        public Label HpText;
        public Vector2 Pos;
        public readonly List<BattleUnit> Units = new();
    }

    private readonly Dictionary<string, StackCard> _stacks = new();
    private readonly List<string> _stackOrder = new();
    private BattleEngine _engine;

    public void Clear()
    {
        foreach (var s in _stacks.Values) s.Root.QueueFree();
        _stacks.Clear();
        _stackOrder.Clear();
    }

    /// <summary>把场上召唤物按（阵营 + 名字）分组，与堆叠卡做增量同步（建新 / 删空 / 归位 / 刷新）。</summary>
    public void Sync(BattleEngine engine, float areaX)
    {
        _engine = engine;
        if (_engine == null) return;

        var groups = new Dictionary<string, List<BattleUnit>>();
        Collect(_engine.A, false, groups);
        Collect(_engine.B, true, groups);

        // ① 消失了（全灭）的堆叠 → 释放（引擎已把尸体移除，这里只看存活）
        for (int i = _stackOrder.Count - 1; i >= 0; i--)
        {
            string k = _stackOrder[i];
            if (groups.ContainsKey(k)) continue;
            _stacks[k].Root.QueueFree();
            _stacks.Remove(k);
            _stackOrder.RemoveAt(i);
        }

        // ② 新出现的类型 → 建卡（按名字首次出场顺序排在后面）
        foreach (var kv in groups)
        {
            if (_stacks.ContainsKey(kv.Key)) continue;
            string name = kv.Value[0].Name;
            BuildOne(kv.Key, name, kv.Key.StartsWith("R|"), areaX);
        }

        // ③ 归位 + 刷新
        for (int i = 0; i < _stackOrder.Count; i++)
        {
            var st = _stacks[_stackOrder[i]];
            st.Root.Position = new Vector2(st.Pos.X, TopY + i * (StackH + StackGap));
            st.Pos = st.Root.Position;
            Render(st, groups[st.Key]);
        }
    }

    private StackCard BuildOne(string key, string name, bool right, float areaX)
    {
        float x = right ? areaX - SideX - CardW - StackW - StackMargin
                        : SideX + CardW + StackMargin;
        var root = new Control { Position = new Vector2(x, TopY), Size = new Vector2(StackW, StackH) };
        var bg = new ColorRect
        {
            Color = right ? new Color(0.16f, 0.09f, 0.11f) : new Color(0.10f, 0.12f, 0.16f),
            Size = root.Size,
            Position = Vector2.Zero,
        };
        root.AddChild(bg);

        var title = new Label { Position = new Vector2(6, 4), Size = new Vector2(StackW - 12, 22), Text = name };
        title.AddThemeFontSizeOverride("font_size", 15);
        title.AddThemeColorOverride("font_color", new Color(0.72f, 0.88f, 1.0f));
        root.AddChild(title);

        var hpbg = new ColorRect { Color = UISignalColors.HpBack, Position = new Vector2(6, 30), Size = new Vector2(StackW - 12, 20) };
        var hpfill = new ColorRect { Color = UISignalColors.HpGood, Position = hpbg.Position, Size = hpbg.Size };
        root.AddChild(hpbg); root.AddChild(hpfill);

        var hptext = new Label { Position = hpbg.Position, Size = hpbg.Size, HorizontalAlignment = HorizontalAlignment.Center };
        hptext.AddThemeFontSizeOverride("font_size", 13);
        root.AddChild(hptext);

        AddChild(root);
        var st = new StackCard
        {
            Key = key, Name = name, Root = root,
            TitleText = title, HpFill = hpfill, HpText = hptext,
            Pos = new Vector2(x, TopY),
        };
        _stacks[key] = st;
        _stackOrder.Add(key);
        return st;
    }

    private static void Collect(List<BattleUnit> team, bool right, Dictionary<string, List<BattleUnit>> groups)
    {
        foreach (var u in team)
        {
            if (!u.IsSummon || !u.Alive) continue;
            string key = (right ? "R|" : "L|") + u.Name;
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<BattleUnit>();
            list.Add(u);
        }
    }

    /// <summary>刷新一张堆叠卡：数量 + **聚合血条**（Σ当前 / Σ上限）、最快到期的剩余时间。</summary>
    private static void Render(StackCard st, List<BattleUnit> units)
    {
        st.Units.Clear();
        st.Units.AddRange(units);

        double hp = 0, max = 0, ttl = double.MaxValue;
        foreach (var u in units)
        {
            hp += System.Math.Max(0, u.Hp);
            max += u.BaseHp;
            if (u.Ttl < ttl) ttl = u.Ttl;
        }
        double frac = max > 0 ? System.Math.Clamp(hp / max, 0, 1) : 0;
        st.HpFill.Size = new Vector2((float)((StackW - 12) * frac), 16);
        st.HpFill.Color = frac > 0.5 ? new Color(0.25f, 0.85f, 0.35f)
            : frac > 0.25 ? new Color(0.95f, 0.85f, 0.2f) : new Color(0.9f, 0.25f, 0.2f);
        st.HpText.Text = hp.ToString("0") + "/" + max.ToString("0");
        st.TitleText.Text = ttl < 1e8 ? st.Name + " ×" + units.Count + "     " + ttl.ToString("0") + "s" : st.Name + " ×" + units.Count;
    }

    /// <summary>飘字锚点：召唤物所属堆叠卡上方。</summary>
    public bool TryGetAnchor(BattleUnit u, out Vector2 pos)
    {
        if (u != null && u.IsSummon && _engine != null)
        {
            string key = (_engine.B.Contains(u) ? "R|" : "L|") + u.Name;
            if (_stacks.TryGetValue(key, out var st))
            {
                pos = st.Pos + new Vector2(st.Root.Size.X * 0.5f - 60, 18);
                return true;
            }
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
