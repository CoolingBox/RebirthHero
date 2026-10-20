// 文件：InnPanel.cs
// 《轮回大侠》M2.3 客栈 —— **独立窗口**（框架面板：BasePanel 子类，由 UIManager 开关）
//
// 设计分工（与框架 UICodeGenerator 流程一致）：
//   · InnPanel.tscn        —— 静态外壳节点（名字以 T 开头，供生成器绑定）：TBg / TTitle / TSilver /
//                            THint / TRefreshBtn / TCloseBtn / TSlotRow / TFormationBox / TBottomHint
//                            位置·尺寸·锚点·字号·配色·间距·初始可见性全部在此文件定义（人工可在编辑器直接调）
//   · InnPanel.cs          —— 本文件：所有逻辑（InitStatic 仅一次性接线、刷新、事件、辅助）。已存在故生成器跳过
//   · Generate/InnPanelBind.cs —— 由 GenUICode.py 生成：声明 _T* 字段并在 OnBind 里 FindChild 绑定
//
// 打开方式：BattlePanel 点「客栈」→ InnPanel.BindData(...) 注入依赖 → UIManager.ShowPanel<InnPanel>()
//           同时暂停战斗（BattlePanel 停 _timer / _advanceTimer）。关闭 → 回调恢复战斗。
//
// 界面分三块：
//   ① 候选栏位（4 个，TSlotRow 内动态生成）：侠客 + 资质（★／三维成长率／词条）+ 招募按钮
//   ② 上阵阵容（4 个槽位，TFormationBox 内动态生成）：1 号位 = 前排（吸仇恨 +30% 气血），可 ◀▶ 调位 / 下阵
//   ③ 候补区（TFormationBox 内动态生成）：已招未上阵，点「上阵」进空槽
//
// 刷新策略：Refresh() **整体重建动态容器**（TSlotRow / TFormationBox）。客栈是低频界面，
// 重建比增量同步简单，也避免资质对象跨场复用带来的残留显示。站位 / 上下阵变更在**下一场战斗**生效。

using System;
using System.Collections.Generic;
using Godot;
using TinyFramework;

public partial class InnPanel : BasePanel
{
    // —— 配色（动态内容用；静态外壳的配色已迁到 InnPanel.tscn）——
    static readonly Color Gold    = new Color(1.00f, 0.84f, 0.35f);
    static readonly Color Dim     = new Color(0.62f, 0.66f, 0.74f);
    static readonly Color Green   = new Color(0.45f, 0.95f, 0.55f);

    private BackdropLayer _backdrop;   // 全屏山水背景（懒创建）

    // —— 外部注入的依赖（UIManager.ShowPanel<T>() 不带参，故用静态上下文在打开前注入）——
    static Inn _ctxInn;
    static Silver _ctxSilver;
    static Func<int, HeroConfig> _ctxHeroOf;
    static Func<int, QualityConfig> _ctxAffixOf;
    static Action _ctxOnClose;
    static Action _ctxOnRosterChanged;

    public static void BindData(Inn inn, Silver silver,
                                Func<int, HeroConfig> heroOf, Func<int, QualityConfig> affixOf,
                                Action onClose, Action onRosterChanged)
    {
        _ctxInn = inn;
        _ctxSilver = silver;
        _ctxHeroOf = heroOf;
        _ctxAffixOf = affixOf;
        _ctxOnClose = onClose;
        _ctxOnRosterChanged = onRosterChanged;
    }

    // —— 实例状态 ——
    Inn _inn;
    Silver _silver;
    Func<int, HeroConfig> _heroOf;
    Func<int, QualityConfig> _affixOf;
    Action _onClose;
    Action _onRosterChanged;
    bool _wired;

    public override void OnShow()
    {
        base.OnShow();

        // 消费注入的依赖（每次显示都刷新引用，安全且无额外开销）
        _inn = _ctxInn;
        _silver = _ctxSilver;
        _heroOf = _ctxHeroOf;
        _affixOf = _ctxAffixOf;
        _onClose = _ctxOnClose;
        _onRosterChanged = _ctxOnRosterChanged;

        if (!_wired)
        {
            InitStatic();
            if (_TRefreshBtn != null) _TRefreshBtn.Pressed += OnRefreshClicked;
            if (_TCloseBtn != null) _TCloseBtn.Pressed += OnCloseClicked;
            _wired = true;
        }
        Refresh();
    }

    public override void OnHide()
    {
        base.OnHide();
    }

    // ================= 静态外壳：仅一次性接线，定位交给 .tscn =================
    void InitStatic()
    {
        // 根面板撑满画布：UIManager 是普通 Node（非 Control），子节点锚点才能相对它解析。
        // 其余静态组件（背景/标题/信息栏/按钮/容器/底部提示）的位置·尺寸·字号·配色·间距
        // 全部写在 InnPanel.tscn，便于在编辑器里直接看、直接拖。
        var area = GameObject.GetViewportRect().Size;
        if (area.X < 2 || area.Y < 2) area = new Vector2(1920, 1080);   // 设计分辨率兜底
        GameObject.Size = area;
        GameObject.Position = Vector2.Zero;
        GameObject.ProcessMode = Node.ProcessModeEnum.Always;
    }

    // ================= 绘制 =================
    public void Refresh()
    {
        if (_inn == null) return;
        float x0 = 60f;
        float gap = 24f;
        float w = GameObject.Size.X;                          // 设计分辨率宽（1920），来自 .tscn 撑满的根面板
        float cardW = (w - x0 * 2f - gap * 3f) / 4f;          // 4 张候选卡按设计宽均分 ≈ 432
        float cardH = 320f;
        float rowW = w - x0 * 2f;

        if (_TSilver != null) _TSilver.Text = "银两 " + Fmt(CurrentSilver());

        if (_THint != null)
            _THint.Text = "已刷新 " + _inn.RefreshUsed + " 次　·　栏位 " + _inn.Slots.Count + "/" + InnConfig.InnSlots
                        + "　·　资质 = 星级(HP/ATK，不含速度) + 三维成长率 + 词条";

        if (_TRefreshBtn != null)
        {
            _TRefreshBtn.Text = _inn.NextRefreshIsFree ? "刷新（免费）" : "刷新（" + Fmt(_inn.RefreshCost()) + " 银两）";
            _TRefreshBtn.Disabled = !_inn.NextRefreshIsFree && CurrentSilver() < _inn.RefreshCost();
        }

        // —— 候选栏位（动态重建）——
        if (_TSlotRow != null)
        {
            foreach (Node c in _TSlotRow.GetChildren()) c.QueueFree();
            foreach (HeroQuality q in _inn.Slots) _TSlotRow.AddChild(MakeSlotCard(q, cardW, cardH));
        }

        // —— 编队区（动态重建）——
        if (_TFormationBox != null)
        {
            foreach (Node c in _TFormationBox.GetChildren()) c.QueueFree();
            AddLabel(_TFormationBox, Vector2.Zero, new Vector2(rowW, 24),
                     "上阵阵容 " + _inn.Roster.Count + "/" + InnConfig.RosterSize + "　（①1号位=前排·吸仇恨·+30%气血；" +
                     "◀▶ 调站位，下一场生效）", 16, Gold);

            for (int i = 0; i < InnConfig.RosterSize; i++)
                _TFormationBox.AddChild(MakeFormationRow(i, rowW));

            string benchHint = _inn.Roster.Count >= InnConfig.RosterSize ? "（已满 —— 先「下阵」腾位）" : "";
            AddLabel(_TFormationBox, Vector2.Zero, new Vector2(rowW, 22), "候补 · 未上阵 " + benchHint, 15, Gold);

            // 候补 = 已招但未上阵（显式循环，不写 LINQ）
            var bench = new List<HeroQuality>();
            foreach (HeroQuality q in _inn.Recruited)
            {
                if (!_inn.InRoster(q.HeroId)) bench.Add(q);
            }
            bench.Sort(CompareBench);   // 星级降序 → 英雄 Id 升序

            var grid = new GridContainer();
            grid.Columns = 6;
            grid.AddThemeConstantOverride("h_separation", 8);
            grid.AddThemeConstantOverride("v_separation", 6);
            _TFormationBox.AddChild(grid);
            if (bench.Count == 0)
                AddLabel(grid, Vector2.Zero, new Vector2(1000, 26), "（暂无候补 —— 去上面招人）", 14, Dim);
            else
                foreach (HeroQuality q in bench) grid.AddChild(MakeBenchButton(q));
        }

        if (_TBottomHint != null)
            _TBottomHint.Text = "上下阵 / 站位变更在**下一场战斗**生效；已招侠客当世锁定（不可退换）。";

        UIThemeManager.ApplyTo(GameObject);

        if (_TBg != null)
            EnsureBackdrop();
    }

    /// <summary>背景层只建一次；换主题时由 BackdropLayer 自己刷新，不重建节点。</summary>
    void EnsureBackdrop()
    {
        if (_TBg == null)
            return;

        if (_backdrop != null && GodotObject.IsInstanceValid(_backdrop))
            return;

        _backdrop = new BackdropLayer();
        _TBg.AddChild(_backdrop);
    }

    void OnRefreshClicked()
    {
        if (_inn.TryRefresh(_silver))
        {
            if (_onRosterChanged != null) _onRosterChanged();
            Refresh();
        }
    }

    void OnCloseClicked()
    {
        if (_onClose != null) _onClose();
        HidePanel<InnPanel>();   // BasePanel 保护方法 → UIManager.HidePanel
    }

    /// <summary>一个候选栏位卡：名字 + ★ + 三维成长率 + 词条 + 招募按钮。</summary>
    Control MakeSlotCard(HeroQuality q, float cw, float ch)
    {
        var card = new Control();
        card.CustomMinimumSize = new Vector2(cw, ch);
        var slotBg = new ColorRect();
        slotBg.Color = new Color(0.11f, 0.13f, 0.18f);
        slotBg.Size = new Vector2(cw, ch);
        card.AddChild(slotBg);

        var vb = new VBoxContainer();
        vb.Position = new Vector2(16, 14);
        vb.Size = new Vector2(cw - 32f, ch - 28f);
        vb.AddThemeConstantOverride("separation", 4);
        card.AddChild(vb);

        string nm = HeroName(q.HeroId);

        var star = new HBoxContainer();
        star.AddThemeConstantOverride("separation", 8);
        var nameL = new Label();
        nameL.Text = nm;
        nameL.AddThemeFontSizeOverride("font_size", 22);
        nameL.AddThemeColorOverride("font_color", Colors.White);
        star.AddChild(nameL);
        var starL = new Label();
        starL.Text = q.StarText;
        starL.AddThemeFontSizeOverride("font_size", 18);
        starL.AddThemeColorOverride("font_color", Gold);
        star.AddChild(starL);
        vb.AddChild(star);

        Color starMulColor = q.StarMul > 1.001 ? Green : Dim;
        vb.AddChild(Line("星级乘数 ×" + q.StarMul.ToString("0.00") + "（气血/攻击）", 15, starMulColor));

        vb.AddChild(Line(
            "成长率　气血 " + Signed(q.HpPct) + "%　攻击 " + Signed(q.AtkPct) + "%　速度 " + Signed(q.SpdPct) + "%",
            15, Dim));

        string affix;
        if (q.AffixIds.Count == 0)
        {
            affix = "词条　—";
        }
        else
        {
            var parts = new List<string>();
            foreach (int id in q.AffixIds) parts.Add(AffixName(id));
            affix = "词条　" + string.Join(" · ", parts);
        }
        Color affixColor = q.AffixIds.Count > 0 ? Gold : Dim;
        vb.AddChild(Line(affix, 15, affixColor));

        double cost = _inn.RecruitCost(q);
        string btnText = q.Recruited ? "已招募" : "招募（" + Fmt(cost) + " 银两）";
        double cur = CurrentSilver();
        bool btnDisabled = q.Recruited || cur < cost;
        var btn = new Button();
        btn.Text = btnText;
        btn.Size = new Vector2(cw - 32f, 44);
        btn.Disabled = btnDisabled;
        btn.Pressed += () =>
        {
            if (_inn.TryRecruit(q, _silver))
            {
                if (_onRosterChanged != null) _onRosterChanged();
                Refresh();
            }
        };
        vb.AddChild(btn);
        return card;
    }

    /// <summary>上阵阵容里的一个槽位行：槽位徽标 + 英雄信息 + ◀▶ 调位 / 下阵。</summary>
    Control MakeFormationRow(int idx, float rowW)
    {
        int slot = idx + 1;
        bool isFront = slot == 1;
        bool hasHero = idx < _inn.Roster.Count;

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);

        // —— 槽位徽标 ——
        var badge = new Label();
        badge.CustomMinimumSize = new Vector2(140, 64);
        string badgeFront = isFront ? "前排·吸仇恨" : "后排";
        badge.Text = SlotTag(slot) + " " + slot + "号位\n" + badgeFront;
        badge.AddThemeFontSizeOverride("font_size", 15);
        badge.AddThemeColorOverride("font_color", isFront ? Gold : Dim);
        row.AddChild(badge);

        // —— 英雄信息 ——
        var info = new VBoxContainer();
        info.AddThemeConstantOverride("separation", 2);
        float infoW = rowW - 140f - 240f - 24f;   // 140 徽标 + 240 按钮 + 24 两道间隔
        info.CustomMinimumSize = new Vector2(infoW, 64);
        if (hasHero)
        {
            int hid = _inn.Roster[idx];
            string nm = HeroName(hid);
            HeroQuality q = _inn.QualityOf(hid);
            bool isBase = q == null || q.IsBase;

            string nameText;
            if (isBase) nameText = nm + "（基准）";
            else nameText = nm + "　" + q.StarText;
            var nameL = new Label();
            nameL.Text = nameText;
            nameL.AddThemeFontSizeOverride("font_size", 18);
            nameL.AddThemeColorOverride("font_color", Colors.White);
            info.AddChild(nameL);

            string qline;
            if (isBase)
            {
                qline = "（无资质基准版）";
            }
            else
            {
                qline = "×" + q.StarMul.ToString("0.00") + "　成长 气血" + Signed(q.HpPct) + "% 攻" + Signed(q.AtkPct) + "% 速" + Signed(q.SpdPct) + "%";
                if (q.AffixIds.Count > 0)
                {
                    var parts = new List<string>();
                    foreach (int id in q.AffixIds) parts.Add(AffixName(id));
                    qline = qline + "　" + string.Join("·", parts);
                }
            }
            info.AddChild(Line(qline, 13, Dim));
        }
        else
        {
            var empty = new Label();
            empty.Text = "— 空位（从候补上阵）—";
            empty.AddThemeFontSizeOverride("font_size", 17);
            empty.AddThemeColorOverride("font_color", Dim);
            info.AddChild(empty);
        }
        row.AddChild(info);

        // —— 操作按钮 ——
        var btns = new HBoxContainer();
        btns.AddThemeConstantOverride("separation", 8);
        btns.CustomMinimumSize = new Vector2(240, 64);

        bool canUp = hasHero && idx > 0;
        bool canDown = hasHero && idx < _inn.Roster.Count - 1;

        var up = new Button();
        up.Text = "◀前";
        up.Disabled = !canUp;
        up.CustomMinimumSize = new Vector2(64, 40);
        up.Pressed += () =>
        {
            if (hasHero && _inn.MoveRoster(_inn.Roster[idx], -1))
            {
                if (_onRosterChanged != null) _onRosterChanged();
                Refresh();
            }
        };
        var down = new Button();
        down.Text = "后▶";
        down.Disabled = !canDown;
        down.CustomMinimumSize = new Vector2(64, 40);
        down.Pressed += () =>
        {
            if (hasHero && _inn.MoveRoster(_inn.Roster[idx], +1))
            {
                if (_onRosterChanged != null) _onRosterChanged();
                Refresh();
            }
        };
        var off = new Button();
        off.Text = "下阵";
        off.Disabled = !hasHero;
        off.CustomMinimumSize = new Vector2(72, 40);
        off.Pressed += () =>
        {
            if (hasHero)
            {
                _inn.RemoveFromRoster(_inn.Roster[idx]);
                if (_onRosterChanged != null) _onRosterChanged();
                Refresh();
            }
        };
        btns.AddChild(up); btns.AddChild(down); btns.AddChild(off);
        row.AddChild(btns);

        return row;
    }

    /// <summary>候补区的一个「上阵」按钮（已招但未上阵）。</summary>
    Button MakeBenchButton(HeroQuality q)
    {
        string nm = HeroName(q.HeroId);
        bool full = _inn.Roster.Count >= InnConfig.RosterSize;
        var btn = new Button();
        btn.Text = "上阵 " + nm + " " + q.StarText;
        btn.CustomMinimumSize = new Vector2(200, 44);
        btn.Disabled = full;
        if (!full) btn.AddThemeColorOverride("font_color", Green);
        btn.Pressed += () =>
        {
            if (_inn.AddToRoster(q.HeroId))
            {
                if (_onRosterChanged != null) _onRosterChanged();
                Refresh();
            }
        };
        return btn;
    }

    // ================= 小工具 =================

    double CurrentSilver()
    {
        if (_silver != null) return _silver.Amount;
        return 0;
    }

    string HeroName(int id)
    {
        if (_heroOf == null) return "#" + id;
        HeroConfig h = _heroOf(id);
        if (h == null) return "#" + id;
        return h.Name;
    }

    string AffixName(int id)
    {
        if (_affixOf == null) return "#" + id;
        QualityConfig a = _affixOf(id);
        if (a == null) return "#" + id;
        return a.Name;
    }

    static string SlotTag(int slot)
    {
        switch (slot)
        {
            case 1: return "①";
            case 2: return "②";
            case 3: return "③";
            case 4: return "④";
            default: return "[" + slot + "]";
        }
    }

    static int CompareBench(HeroQuality a, HeroQuality b)
    {
        if (a.Star != b.Star) return b.Star - a.Star;   // 星级降序
        return a.HeroId - b.HeroId;                       // 英雄 Id 升序
    }

    static string Signed(double v)
    {
        if (v >= 0) return "+" + v.ToString("0.#");
        return v.ToString("0.#");
    }

    static string Fmt(double v)
    {
        if (v >= 1e8) return (v / 1e8).ToString("0.##") + "亿";
        if (v >= 1e4) return (v / 1e4).ToString("0.##") + "万";
        return v.ToString("0.#");
    }

    static Label Line(string text, int size, Color color)
    {
        var l = new Label();
        l.Text = text;
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        return l;
    }

    static void AddLabel(Control parent, Vector2 pos, Vector2 size, string text, int fontSize, Color color)
    {
        var l = new Label();
        l.Text = text;
        l.Position = pos;
        l.Size = size;
        l.HorizontalAlignment = HorizontalAlignment.Left;
        l.AddThemeFontSizeOverride("font_size", fontSize);
        l.AddThemeColorOverride("font_color", color);
        parent.AddChild(l);
    }
}
