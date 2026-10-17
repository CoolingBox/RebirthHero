// 文件：StatsPanelView.cs
// 《轮回大侠》表现层 —— **战报统计面板**组件（Godot 节点：Control 子类，挂在 BattlePanel.tscn 的 TStatsPanel 上）
// 职责：两种排列（按侠客 / 按武学）× 五指标排序（伤害/治疗/行动/承伤/触发），每层刷新。
// 状态自持：StatsTracker / 行表 / 排列·排序 / 当前层全部私有；面板只经 BeginBattle / Refresh / Toggle / CycleView / CycleSort / InvalidateLayer 调用。
// 子节点（Header / ViewBtn / SortBtn / Body）由本组件在 _Ready 自行抓取并接线，外部不关心其内部结构。

using System;
using System.Collections.Generic;
using Godot;

public partial class StatsPanelView : Control
{
    private readonly StatsTracker _stats = new();
    private readonly Dictionary<string, Label> _rows = new();

    private Label _header;
    private Button _viewBtn;
    private Button _sortBtn;
    private VBoxContainer _body;

    private List<BattleUnit> _team;
    private int _layer;
    private int _statLayer = -1;                                  // 当前统计对应的层；变化时清零
    private int _view = 0;                                        // 0 = 按侠客排列，1 = 按武学排列
    private StatsTracker.StatMetric _metric = StatsTracker.StatMetric.Damage;  // 排序子项

    public override void _Ready()
    {
        _header = GetNodeOrNull<Label>("Header");
        _viewBtn = GetNodeOrNull<Button>("ViewBtn");
        _sortBtn = GetNodeOrNull<Button>("SortBtn");
        _body = GetNodeOrNull<VBoxContainer>("Body");
        if (_viewBtn != null) _viewBtn.Pressed += CycleView;
        if (_sortBtn != null) _sortBtn.Pressed += CycleSort;
    }

    /// <summary>开战：挂事件总线 + 进新层则清零 + 建行（"每层刷新"，层内续战累计）。</summary>
    public void BeginBattle(BattleEngine engine, List<BattleUnit> team, int layer)
    {
        _stats.Attach(engine);
        if (layer != _statLayer) { _stats.Reset(); _statLayer = layer; }
        _team = team;
        _layer = layer;
        UpdateButtons();
        Build();
    }

    /// <summary>重开本世：强制下次开战清零统计。</summary>
    public void InvalidateLayer() { _statLayer = -1; }

    /// <summary>切换战报面板的显隐。仅可见时才刷新数值（省开销）。</summary>
    public void Toggle()
    {
        Visible = !Visible;
        if (Visible) Refresh();
    }

    public void CycleView()
    {
        _view = _view == 0 ? 1 : 0;
        UpdateButtons();
        Build();
    }

    public void CycleSort()
    {
        // 伤害 → 治疗 → 行动 → 承伤 → 触发 → 伤害（循环）
        _metric = (StatsTracker.StatMetric)(((int)_metric + 1) % 5);
        UpdateButtons();
        Build();
    }

    /// <summary>把聚合数据刷进战报行。面板隐藏时直接跳过。</summary>
    public void Refresh()
    {
        if (!Visible) return;
        UpdateHeader();
        foreach (var kv in _rows)
        {
            string key = kv.Key;
            Label lbl = kv.Value;
            if (key.IndexOf('|') >= 0)
            {
                if (_stats.TrySkill(key, out var sk))
                {
                    string head = _view == 1 ? ("· " + sk.Hero + "·" + sk.Skill) : ("　· " + sk.Skill);
                    string text = head + "：触发 " + sk.Triggers + "　伤 " + UiUtil.Fmt(sk.Damage) + "　疗 " + UiUtil.Fmt(sk.Heal);
                    if (sk.Taken > 0) text += "　承 " + UiUtil.Fmt(sk.Taken);   // 召唤武学：其召唤物承伤
                    lbl.Text = text;
                }
            }
            else
            {
                if (_stats.TryHero(key, out var h))
                    lbl.Text = h.Name + "　伤 " + UiUtil.Fmt(h.Damage) + "　疗 " + UiUtil.Fmt(h.Heal) + "　行动 " + h.Actions + "　承伤 " + UiUtil.Fmt(h.Taken);
            }
        }
    }

    /// <summary>按当前排列模式（按侠客 / 按武学）与排序子项重建战报行。每场开战、切换模式 / 指标时调用。</summary>
    private void Build()
    {
        foreach (var lbl in _rows.Values) lbl.QueueFree();
        _rows.Clear();
        if (_body == null || _team == null) return;

        // 玩家方侠客（与阵容一致，按 Slot 排序；即使暂无数据也占位显示）
        var heroes = new List<BattleUnit>();
        foreach (var u in _team)
            if (!u.IsSummon && u.Slot >= 1) heroes.Add(u);
        heroes.Sort(CompareSlot);

        if (_view == 0)
        {
            // —— 按侠客排列：侠客为父行，旗下武学为子行（均按指标降序）——
            foreach (var h in heroes)
            {
                _stats.EnsureHero(h);
                var hl = NewStatLabel(15, new Color(1f, 0.86f, 0.42f));
                _body.AddChild(hl);
                _rows[h.Name] = hl;

                var sk = new List<StatsTracker.SkillStat>();
                foreach (var s in h.Skills)
                    sk.Add(_stats.EnsureSkill(h, s.Name));
                sk.Sort(SkillDesc);
                foreach (var ss in sk)
                {
                    var sl = NewStatLabel(13, new Color(0.72f, 0.74f, 0.86f));
                    _body.AddChild(sl);
                    _rows[StatsTracker.SkillKey(ss.Hero, ss.Skill)] = sl;
                }
            }
        }
        else
        {
            // —— 按武学排列：每条 (侠客|武学) 独立成行，按指标降序 ——
            var sk = new List<StatsTracker.SkillStat>();
            foreach (var h in heroes)
                foreach (var s in h.Skills)
                    sk.Add(_stats.EnsureSkill(h, s.Name));
            sk.Sort(SkillDesc);
            foreach (var ss in sk)
            {
                var sl = NewStatLabel(13, new Color(0.72f, 0.74f, 0.86f));
                _body.AddChild(sl);
                _rows[StatsTracker.SkillKey(ss.Hero, ss.Skill)] = sl;
            }
        }
        Refresh();
    }

    private int SkillDesc(StatsTracker.SkillStat a, StatsTracker.SkillStat b)
    {
        return StatsTracker.MetricOf(b, _metric).CompareTo(StatsTracker.MetricOf(a, _metric));
    }

    private void UpdateButtons()
    {
        if (_viewBtn != null) _viewBtn.Text = _view == 0 ? "按侠客" : "按武学";
        if (_sortBtn != null) _sortBtn.Text = "序：" + MetricName(_metric);
        UpdateHeader();
    }

    private void UpdateHeader()
    {
        if (_header == null) return;
        _header.Text = "第 " + _layer + " 层 · 战报（本层实时 · 每层刷新）";
    }

    private static string MetricName(StatsTracker.StatMetric m)
    {
        switch (m)
        {
            case StatsTracker.StatMetric.Damage: return "伤害";
            case StatsTracker.StatMetric.Heal: return "治疗";
            case StatsTracker.StatMetric.Actions: return "行动";
            case StatsTracker.StatMetric.Taken: return "承伤";
            default: return "触发";
        }
    }

    private static Label NewStatLabel(int fontSize, Color color)
    {
        var l = new Label { HorizontalAlignment = HorizontalAlignment.Left };
        l.AddThemeFontSizeOverride("font_size", fontSize);
        l.AddThemeColorOverride("font_color", color);
        return l;
    }

    private static int CompareSlot(BattleUnit x, BattleUnit y)
    {
        return x.Slot.CompareTo(y.Slot);
    }
}
