// 文件：StatsPanel.cs
// 《轮回大侠》战报面板（框架面板：BasePanel 子类，由 UIManager 开关）
//
// 与其它面板的差别：它是**常驻侧栏**，不是模态窗口。
//   · 默认隐藏，点「战报」切换显隐（HidePanel 不销毁，下次 ShowPanel 复用实例）
//   · 每 tick 由 BattlePanel 调 RefreshCurrent()，仅可见时才真正刷数字
//   · 它浮在战斗画面上，所以不加 BackdropLayer（全屏底图会挡住战斗）
//
// 结构分工（与 InnPanel 一致）：
//   · StatsPanel.tscn            —— 静态外壳：TBg / THeader / TViewBtn / TSortBtn / TBody
//   · StatsPanel.cs              —— 本文件
//   · Generate/StatsPanelBind.cs —— GenUICode 生成

using System;
using System.Collections.Generic;
using Godot;
using TinyFramework;

public partial class StatsPanel : BasePanel
{
    // UIManager.ShowPanel<T>() 不带参，依赖用静态上下文在打开前注入
    static BattleEngine _ctxEngine;
    static List<BattleUnit> _ctxTeam;
    static int _ctxLayer;

    static StatsPanel _current;

    /// <summary>开战时注入引擎与阵容；面板未创建时先存着，等首次打开再用。</summary>
    public static void BindBattle(BattleEngine engine, List<BattleUnit> team, int layer)
    {
        _ctxEngine = engine;
        _ctxTeam = team;
        _ctxLayer = layer;

        if (_current != null)
            _current.ApplyBattleContext();
    }

    /// <summary>重开本世：强制下次开战清零统计。</summary>
    public static void InvalidateCurrent()
    {
        _ctxLayer = -1;
        if (_current != null)
            _current.InvalidateLayer();
    }

    /// <summary>切换显隐（「战报」按钮）。首次点击才创建面板。</summary>
    public static void ToggleCurrent()
    {
        if (_current == null || !GodotObject.IsInstanceValid(_current.GameObject))
        {
            UIManager.ShowPanel<StatsPanel>();
            return;
        }

        if (_current.GameObject.Visible)
            UIManager.HidePanel<StatsPanel>();
        else
            UIManager.ShowPanel<StatsPanel>();
    }

    /// <summary>逐帧刷新（面板隐藏时内部直接返回，不做无谓计算）。</summary>
    public static void RefreshCurrent()
    {
        if (_current == null)
            return;

        // ClosePanel 会 QueueFree 根节点；此后 _current 可能指向已释放对象，
        // 而本方法每 tick 被调用 —— 不判有效会抛 ObjectDisposedException。
        if (_current.GameObject == null || !GodotObject.IsInstanceValid(_current.GameObject))
            return;

        _current.RefreshRows();
    }

    /// <summary>主题变更后由 BattlePanel 调用。</summary>
    public static void ApplyThemeCurrent()
    {
        if (_current == null)
            return;

        if (_current.GameObject == null || !GodotObject.IsInstanceValid(_current.GameObject))
            return;

        _current.ApplyTheme();
    }

    // —— 实例状态 ——
    readonly StatsTracker _stats = new StatsTracker();
    readonly Dictionary<string, Label> _rows = new Dictionary<string, Label>();

    List<BattleUnit> _team;
    int _layer;
    int _statLayer = -1;                                  // 当前统计对应的层；变化时清零
    int _view = 0;                                        // 0 = 按侠客排列，1 = 按武学排列
    StatsTracker.StatMetric _metric = StatsTracker.StatMetric.Damage;

    bool _wired;

    public override void OnClose()
    {
        // 面板被销毁 → 断开静态引用，否则静态入口会摸到已释放节点
        _current = null;
        base.OnClose();
    }

    public override void OnShow()
    {
        base.OnShow();

        // 不要 override OnBind（生成器已在 Bind 文件里实现），否则 CS0111
        _current = this;

        // .tscn 里挂的是默认主题（供编辑器预览）；运行时必须换成玩家当前选的那套
        UIThemeManager.ApplyTo(GameObject);

        if (!_wired)
        {
            if (_TViewBtn != null) _TViewBtn.Pressed += CycleView;
            if (_TSortBtn != null) _TSortBtn.Pressed += CycleSort;
            _wired = true;
        }

        ApplyBattleContext();
    }

    public void ApplyTheme()
    {
        Build();
    }

    // ================= 数据 =================

    void ApplyBattleContext()
    {
        if (_ctxEngine == null)
            return;

        _stats.Attach(_ctxEngine);

        if (_ctxLayer != _statLayer)
        {
            _stats.Reset();
            _statLayer = _ctxLayer;
        }

        _team = _ctxTeam;
        _layer = _ctxLayer;

        UpdateButtons();
        Build();
    }

    void InvalidateLayer()
    {
        _statLayer = -1;
    }

    void CycleView()
    {
        _view = _view == 0 ? 1 : 0;
        UpdateButtons();
        Build();
    }

    void CycleSort()
    {
        // 伤害 → 治疗 → 行动 → 承伤 → 触发 → 伤害（循环）
        _metric = (StatsTracker.StatMetric)(((int)_metric + 1) % 5);
        UpdateButtons();
        Build();
    }

    /// <summary>把聚合数据刷进战报行。面板隐藏时直接跳过。</summary>
    void RefreshRows()
    {
        if (GameObject == null || !GameObject.Visible)
            return;

        UpdateHeader();

        foreach (KeyValuePair<string, Label> kv in _rows)
        {
            string key = kv.Key;
            Label lbl = kv.Value;

            if (key.IndexOf('|') >= 0)
            {
                if (_stats.TrySkill(key, out StatsTracker.SkillStat sk))
                {
                    string head = _view == 1 ? ("· " + sk.Hero + "·" + sk.Skill) : ("　· " + sk.Skill);
                    string text = head + "：触发 " + sk.Triggers
                                  + "　伤 " + BigNum.From(sk.Damage).ToString(2)
                                  + "　疗 " + BigNum.From(sk.Heal).ToString(2);
                    if (sk.Taken > 0)
                        text += "　承 " + BigNum.From(sk.Taken).ToString(2);
                    lbl.Text = text;
                }
            }
            else
            {
                if (_stats.TryHero(key, out StatsTracker.HeroStat h))
                    lbl.Text = h.Name + "　伤 " + BigNum.From(h.Damage).ToString(2)
                               + "　疗 " + BigNum.From(h.Heal).ToString(2)
                               + "　行动 " + h.Actions
                               + "　承伤 " + BigNum.From(h.Taken).ToString(2);
            }
        }
    }

    // ================= 绘制 =================

    /// <summary>按当前排列模式与排序子项重建战报行。开战、切换模式/指标时调用。</summary>
    void Build()
    {
        foreach (Label lbl in _rows.Values) lbl.QueueFree();
        _rows.Clear();

        if (_TBody == null || _team == null) return;

        List<BattleUnit> heroes = new List<BattleUnit>();
        foreach (BattleUnit u in _team)
            if (!u.IsSummon && u.Slot >= 1) heroes.Add(u);
        heroes.Sort(CompareSlot);

        Color heroColor = UIThemeManager.Accent;
        Color skillColor = UIThemeManager.TextSecondary;

        if (_view == 0)
        {
            // —— 按侠客排列：侠客为父行，旗下武学为子行（均按指标降序）——
            foreach (BattleUnit h in heroes)
            {
                _stats.EnsureHero(h);
                Label hl = NewStatLabel(15, heroColor);
                _TBody.AddChild(hl);
                _rows[h.Name] = hl;

                List<StatsTracker.SkillStat> sk = new List<StatsTracker.SkillStat>();
                foreach (BattleSkill s in h.Skills)
                    sk.Add(_stats.EnsureSkill(h, s.Name));
                sk.Sort(SkillDesc);

                foreach (StatsTracker.SkillStat ss in sk)
                {
                    Label sl = NewStatLabel(13, skillColor);
                    _TBody.AddChild(sl);
                    _rows[StatsTracker.SkillKey(ss.Hero, ss.Skill)] = sl;
                }
            }
        }
        else
        {
            // —— 按武学排列：每条 (侠客|武学) 独立成行，按指标降序 ——
            List<StatsTracker.SkillStat> sk = new List<StatsTracker.SkillStat>();
            foreach (BattleUnit h in heroes)
                foreach (BattleSkill s in h.Skills)
                    sk.Add(_stats.EnsureSkill(h, s.Name));
            sk.Sort(SkillDesc);

            foreach (StatsTracker.SkillStat ss in sk)
            {
                Label sl = NewStatLabel(13, skillColor);
                _TBody.AddChild(sl);
                _rows[StatsTracker.SkillKey(ss.Hero, ss.Skill)] = sl;
            }
        }

        RefreshRows();
    }

    int SkillDesc(StatsTracker.SkillStat a, StatsTracker.SkillStat b)
    {
        return StatsTracker.MetricOf(b, _metric).CompareTo(StatsTracker.MetricOf(a, _metric));
    }

    void UpdateButtons()
    {
        if (_TViewBtn != null) _TViewBtn.Text = _view == 0 ? "按侠客" : "按武学";
        if (_TSortBtn != null) _TSortBtn.Text = "序：" + MetricName(_metric);
        UpdateHeader();
    }

    void UpdateHeader()
    {
        if (_THeader == null) return;
        _THeader.Text = "第 " + _layer + " 层 · 战报（本层实时 · 每层刷新）";
    }

    static string MetricName(StatsTracker.StatMetric m)
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

    static Label NewStatLabel(int fontSize, Color color)
    {
        Label l = new Label { HorizontalAlignment = HorizontalAlignment.Left };
        l.AddThemeFontSizeOverride("font_size", fontSize);
        l.AddThemeColorOverride("font_color", color);
        return l;
    }

    static int CompareSlot(BattleUnit x, BattleUnit y)
    {
        return x.Slot.CompareTo(y.Slot);
    }
}
