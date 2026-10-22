// 文件：EquipPanel.cs
// 《轮回大侠》M2.5 装备面板（框架面板：BasePanel 子类，由 UIManager 开关）
//
// 结构分工（与 InnPanel / SettingPanel 一致）：
//   · EquipPanel.tscn            —— 静态外壳：TBg / THeading / TCloseBtn / THint / TContent
//                                  + TSlotBox（栏位区）/ TBagScroll + TBagList（行囊）
//   · EquipPanel.cs              —— 本文件
//   · Generate/EquipPanelBind.cs —— GenUICode 生成
//
// 交互：先选侠客 → 看他的四栏 → 点行囊里的装备穿上；栏内可卸下、可花银两强化。
//
// **装备在下一场战斗生效**（与武学装配同一口径）：不在战斗中途重算属性，
//   否则玩家能靠"打到一半换装"绕过战力门槛，难度曲线失去意义。

using System;
using System.Collections.Generic;
using Godot;
using TinyFramework;

public partial class EquipPanel : BasePanel
{
    // UIManager.ShowPanel<T>() 不带参，依赖用静态上下文在打开前注入
    static EquipBag _ctxBag;
    static List<EquipConfig> _ctxPool;
    static List<HeroConfig> _ctxRoster;
    static Silver _ctxSilver;
    static List<EquipSetConfig> _ctxSetPool;
    static List<EquipAffixConfig> _ctxAffixPool;
    static Action _ctxOnClose;

    static EquipPanel _current;

    public static void BindData(EquipBag bag, List<EquipConfig> pool, List<HeroConfig> roster,
                                Silver silver, List<EquipSetConfig> setPool, List<EquipAffixConfig> affixPool,
                                Action onClose)
    {
        _ctxBag = bag;
        _ctxPool = pool;
        _ctxRoster = roster;
        _ctxSilver = silver;
        _ctxSetPool = setPool;
        _ctxAffixPool = affixPool;
        _ctxOnClose = onClose;
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

    EquipBag _bag;
    List<EquipConfig> _pool;
    List<HeroConfig> _roster;
    Silver _silver;
    List<EquipSetConfig> _setPool;
    List<EquipAffixConfig> _affixPool;
    Action _onClose;

    BackdropLayer _backdrop;
    bool _wired;

    int _selHero;   // 当前查看第几个侠客（索引，不是 Id）

    public override void OnShow()
    {
        base.OnShow();

        _current = this;

        // .tscn 里挂的是默认主题（供编辑器预览）；运行时必须换成玩家当前选的那套
        UIThemeManager.ApplyTo(GameObject);

        _bag = _ctxBag;
        _pool = _ctxPool;
        _roster = _ctxRoster;
        _silver = _ctxSilver;
        _setPool = _ctxSetPool;
        _affixPool = _ctxAffixPool;
        _onClose = _ctxOnClose;

        if (_selHero >= RosterCount()) _selHero = 0;

        // 弹窗不放山水图：同一张 1920×1080 的图裁进小窗口会有"截图放大"感。
        // 让背后的战斗场景透过半透明的 BgPanel 底自然显出来（见主题资源 bgpanel 的 alpha）。
        // 方法保留：将来若某个弹窗需要独立全屏底，取消注释即可。
        // EnsureBackdrop();

        if (!_wired)
        {
            if (_TCloseBtn != null) _TCloseBtn.Pressed += OnClosePressed;
            _wired = true;
        }

        Build();
    }

    public void ApplyTheme()
    {
        // 底色由 Theme 的 "BgPanel" 变体提供（TBg 是 Panel 不是 ColorRect，没有 Color 属性）。
        // 这里只需重建动态内容，让文字/按钮重新取当前主题的色。
        Build();
    }

    void OnClosePressed()
    {
        AudioManager.PlayUISFX("ui_close");
        HidePanel<EquipPanel>();
        if (_onClose != null) _onClose();
    }

    void EnsureBackdrop()
    {
        if (_TBg == null)
            return;

        if (_backdrop != null && GodotObject.IsInstanceValid(_backdrop))
            return;

        _backdrop = new BackdropLayer();
        _TBg.AddChild(_backdrop);
    }

    // ================= 绘制 =================

    void Build()
    {
        if (_bag == null || _roster == null) return;

        int count = RosterCount();
        if (count == 0)
        {
            if (_THint != null) _THint.Text = "阵容为空，先去客栈招人。";
            ClearChildren(_TSlotBox);
            ClearChildren(_TBagList);
            return;
        }

        HeroConfig hero = _roster[_selHero];

        if (_THeading != null)
            _THeading.Text = "装备 · " + hero.Name + "的行囊";

        if (_THint != null)
        {
            EquipBonus bonus = _bag.BonusOf(hero.Id, CfgOf);
            _THint.Text = "换装与强化在**下一场战斗**生效　·　当前加成："
                          + "攻击 +" + bonus.Atk.ToString("0") + "　生命 +" + bonus.Hp.ToString("0")
                          + "　速度 +" + bonus.Spd.ToString("0")
                          + "　（银两 " + BigNum.From(_silver != null ? _silver.Amount : 0).ToString(2) + "）"
                          + "　｜　" + SetSummary(hero.Id);
        }

        BuildHeroRow();
        BuildSlots(hero);
        BuildFilterSort();
        BuildBag(hero);
    }

    /// <summary>侠客切换：每个侠客各有独立四栏，必须先选人。</summary>
    void BuildHeroRow()
    {
        if (_TSlotBox == null) return;

        ClearChildren(_TSlotBox);

        HBoxContainer row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);

        Label tag = new Label();
        tag.Text = "侠客";
        tag.CustomMinimumSize = new Vector2(91, RowH - 12);
        tag.AddThemeFontSizeOverride("font_size", FontMain);
        tag.AddThemeColorOverride("font_color", UIThemeManager.TextMuted);
        row.AddChild(tag);

        for (int i = 0; i < RosterCount(); i++)
        {
            int idx = i;
            bool selected = i == _selHero;

            Button btn = new Button();
            btn.Text = selected ? ("● " + _roster[i].Name) : _roster[i].Name;
            btn.CustomMinimumSize = new Vector2(180, RowH - 12);
            btn.AddThemeFontSizeOverride("font_size", FontBtn);
            if (selected) btn.AddThemeColorOverride("font_color", UIThemeManager.Accent);
            btn.Pressed += () =>
            {
                _selHero = idx;
                Build();
            };
            row.AddChild(btn);
        }

        _TSlotBox.AddChild(row);
    }

    // ================= 尺寸口径（2026-10-21 用户：字体 ×1.3 / 图标 ×1.5）=================
    // 原值：图标 40、行高 40/36、正文 17、按钮 15。这里集中一处，避免各处硬编码漂移。
    const int IconSize = 60;      // 40 × 1.5
    const int RowH = 60;          // 跟随图标
    const int FontMain = 22;      // 17 × 1.3
    const int FontSub = 19;       // 副信息（词条 / 已穿状态）
    const int FontBtn = 22;       // 按钮：与正文同级（2026-10-22 用户按钮字偏小）
    const int BtnW = 130;         // 四栏「卸下」按钮宽

    /// <summary>四栏：显示当前穿戴 + 卸下按钮。</summary>
    void BuildSlots(HeroConfig hero)
    {
        if (_TSlotBox == null) return;

        int[] slots = _bag.SlotsOf(hero.Id);

        for (int s = 0; s < EquipBag.SlotCount; s++)
        {
            EquipSlot slotEnum = (EquipSlot)(s + 1);
            int uid = slots[s];

            HBoxContainer row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);

            Label tag = new Label();
            tag.Text = EquipRules.SlotName(slotEnum);
            tag.CustomMinimumSize = new Vector2(91, RowH);
            tag.AddThemeFontSizeOverride("font_size", FontMain);
            tag.AddThemeColorOverride("font_color", UIThemeManager.TextMuted);
            row.AddChild(tag);

            // 图标：空栏也给同尺寸占位，保证四行左缘与行高完全对齐
            EquipConfig slotCfg = null;
            EquipItem slotItem = uid != 0 ? _bag.Find(uid) : null;
            if (slotItem != null) slotCfg = CfgOf(slotItem.ConfigId);
            if (slotCfg != null)
            {
                row.AddChild(EquipIcon.BuildThumb(slotCfg, IconSize));
                row.AddChild(EquipIcon.BuildRarityBar(slotCfg.Rarity, IconSize));
            }
            else
            {
                Control ph = new Control();
                ph.CustomMinimumSize = new Vector2(IconSize, IconSize);
                row.AddChild(ph);
            }

            row.AddChild(BuildInfoColumn(slotCfg, slotItem, uid != 0, 1300));

            if (uid != 0)
            {
                Button off = new Button();
                off.Text = "卸下";
                off.CustomMinimumSize = new Vector2(BtnW, RowH - 4);
                off.AddThemeFontSizeOverride("font_size", FontBtn);
                EquipSlot captured = slotEnum;
                off.Pressed += () =>
                {
                    AudioManager.PlayUISFX("ui_click");
                    _bag.Unequip(hero.Id, captured);
                    Build();
                };
                row.AddChild(off);
            }

            _TSlotBox.AddChild(row);
        }
    }

    /// <summary>
    /// 两行信息列：第一行「稀有度+名字+强化+属性+套装」，第二行词条。
    /// 为什么要拆两行：22px 下"[紫] 降龙袍 +3 〔降龙套〕 命+441 词条：浑厚(命+3%)…"已接近 1000px，
    /// 单行必然溢出并盖住右侧按钮。
    /// </summary>
    Control BuildInfoColumn(EquipConfig cfg, EquipItem item, bool hasItem, int width)
    {
        VBoxContainer col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 0);
        col.CustomMinimumSize = new Vector2(width, RowH);
        col.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;

        Label head = new Label();
        head.AddThemeFontSizeOverride("font_size", FontMain);
        head.CustomMinimumSize = new Vector2(width, FontMain + 8);

        if (!hasItem)
        {
            head.Text = "（空栏）";
            head.AddThemeColorOverride("font_color", UIThemeManager.TextMuted);
            col.AddChild(head);
            return col;
        }

        if (cfg == null)
        {
            head.Text = "（数据缺失）";
            head.AddThemeColorOverride("font_color", UISignalColors.Lose);
            col.AddChild(head);
            return col;
        }

        head.Text = DescribeHead(cfg, item);
        head.AddThemeColorOverride("font_color", UISignalColors.RarityOf(cfg.Rarity));
        col.AddChild(head);

        string affix = DescribeAffix(item);
        if (affix.Length > 0)
        {
            Label sub = new Label();
            sub.Text = affix;
            sub.AddThemeFontSizeOverride("font_size", FontSub);
            sub.AddThemeColorOverride("font_color", UIThemeManager.TextSecondary);
            sub.CustomMinimumSize = new Vector2(width, FontSub + 8);
            col.AddChild(sub);
        }

        return col;
    }

    /// <summary>行囊：全部持有装备，点「穿上」进对应栏。已穿着的标出在谁身上。</summary>
    void BuildBag(HeroConfig hero)
    {
        if (_TBagList == null) return;

        ClearChildren(_TBagList);

        List<EquipItem> view = CollectBagView();

        Label title = new Label();
        title.Text = "行囊（显示 " + view.Count + " / 共 " + _bag.Count + " 件）";
        title.AddThemeFontSizeOverride("font_size", 23);
        title.AddThemeColorOverride("font_color", UIThemeManager.Accent);
        _TBagList.AddChild(title);

        if (view.Count == 0)
        {
            Label empty = new Label();
            empty.Text = _bag.Count == 0 ? "（还没有装备。打怪有概率掉落。）" : "（当前筛选下没有装备）";
            empty.AddThemeFontSizeOverride("font_size", 20);
            empty.AddThemeColorOverride("font_color", UIThemeManager.TextMuted);
            _TBagList.AddChild(empty);
            return;
        }

        for (int i = 0; i < view.Count; i++)
        {
            EquipItem item = view[i];
            EquipConfig cfg = CfgOf(item.ConfigId);
            if (cfg == null) continue;

            HBoxContainer row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);

            row.AddChild(EquipIcon.BuildThumb(cfg, IconSize));
            row.AddChild(EquipIcon.BuildRarityBar(cfg.Rarity, IconSize));
            row.AddChild(BuildInfoColumn(cfg, item, true, 850));

            int wornBy = WornBy(item.Uid);
            Label state = new Label();
            state.CustomMinimumSize = new Vector2(200, RowH);
            state.AddThemeFontSizeOverride("font_size", FontSub);
            if (wornBy == hero.Id) state.Text = "（" + hero.Name + " 已穿）";
            else if (wornBy > 0) state.Text = "（在 " + HeroNameOf(wornBy) + " 身上）";
            else state.Text = "";
            state.AddThemeColorOverride("font_color", UIThemeManager.TextMuted);
            row.AddChild(state);

            int uid = item.Uid;
            Button wear = new Button();
            wear.Text = "穿上";
            wear.CustomMinimumSize = new Vector2(110, RowH - 4);
            wear.AddThemeFontSizeOverride("font_size", FontBtn);
            wear.Disabled = wornBy == hero.Id;
            wear.Pressed += () =>
            {
                AudioManager.PlayUISFX("ui_click");
                if (_bag.Equip(hero.Id, uid, CfgOf)) Build();
            };
            row.AddChild(wear);

            // 强化只对"穿在身上"的做，避免玩家在行囊里给几十件都强化一遍
            Button plus = new Button();
            long cost = EquipRules.EnhanceCost(item.Level);
            if (cost < 0) plus.Text = "满级";
            else plus.Text = "强化 " + cost;
            plus.CustomMinimumSize = new Vector2(170, RowH - 4);
            plus.AddThemeFontSizeOverride("font_size", FontBtn);
            plus.Disabled = cost < 0 || wornBy != hero.Id;
            plus.Pressed += () =>
            {
                AudioManager.PlayUISFX("ui_upgrade");
                if (EquipRules.TryEnhance(item, _silver)) Build();
            };
            row.AddChild(plus);

            // 回收：价格直接写在按钮上（玩家不用点开才知道能换多少）
            Button recycle = new Button();
            recycle.Text = "回收 +" + EquipRules.RecycleValue(item, cfg);
            recycle.CustomMinimumSize = new Vector2(150, RowH - 4);
            recycle.AddThemeFontSizeOverride("font_size", FontBtn);
            recycle.Disabled = wornBy != 0;
            recycle.TooltipText = wornBy != 0 ? "穿着中的装备不能回收，先卸下" : "回收换银两";
            recycle.Pressed += () =>
            {
                AudioManager.PlayUISFX("ui_click");
                EquipItem target = _bag.Find(uid);
                EquipConfig targetCfg = target != null ? CfgOf(target.ConfigId) : null;
                if (targetCfg == null) return;

                long value = EquipRules.RecycleValue(target, targetCfg);
                if (!_bag.Remove(uid)) return;
                if (_silver != null) _silver.Add(value);
                Build();
            };
            row.AddChild(recycle);

            _TBagList.AddChild(row);
        }
    }

    // ================= 筛选 / 排序 / 回收（2026-10-21）=================
    // 筛选维度只给两个：**栏位**（我要配这个位）+ **状态**（我要腾位/凑套）。
    // 不做"稀有度筛选"——行囊本该按稀有度排序看，筛稀有度只会让玩家忘记低档件的存在。
    int _filterSlot;      // 0=全部，1..4 = EquipSlot
    int _filterKind;      // 0=全部 1=套装 2=散件 3=未穿
    int _sortMode;        // 0=稀有度↓ 1=强化↓ 2=最新↓
    bool _batchConfirm;   // 一键回收的二次确认态

    void BuildFilterSort()
    {
        if (_TFilterRow != null)
        {
            ClearChildren(_TFilterRow);
            _TFilterRow.AddChild(TextTag("栏位"));
            _TFilterRow.AddChild(Chip("全部", _filterSlot == 0, () => { _filterSlot = 0; Build(); }));
            for (int s = 1; s <= EquipBag.SlotCount; s++)
            {
                int captured = s;
                _TFilterRow.AddChild(Chip(EquipRules.SlotName((EquipSlot)s), _filterSlot == s,
                                          () => { _filterSlot = captured; Build(); }));
            }

            _TFilterRow.AddChild(Spacer(16));
            _TFilterRow.AddChild(TextTag("状态"));
            _TFilterRow.AddChild(Chip("全部", _filterKind == 0, () => { _filterKind = 0; Build(); }));
            _TFilterRow.AddChild(Chip("套装", _filterKind == 1, () => { _filterKind = 1; Build(); }));
            _TFilterRow.AddChild(Chip("散件", _filterKind == 2, () => { _filterKind = 2; Build(); }));
            _TFilterRow.AddChild(Chip("未穿", _filterKind == 3, () => { _filterKind = 3; Build(); }));
        }

        if (_TSortRow != null)
        {
            ClearChildren(_TSortRow);
            _TSortRow.AddChild(TextTag("排序"));
            _TSortRow.AddChild(Chip("稀有度↓", _sortMode == 0, () => { _sortMode = 0; Build(); }));
            _TSortRow.AddChild(Chip("强化↓", _sortMode == 1, () => { _sortMode = 1; Build(); }));
            _TSortRow.AddChild(Chip("最新↓", _sortMode == 2, () => { _sortMode = 2; Build(); }));

            Control gap = new Control();
            gap.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            _TSortRow.AddChild(gap);

            int n = 0;
            long sum = 0;
            CollectRecyclable(2, ref n, ref sum);   // 白/绿未穿
            if (n > 0)
            {
                Button batch = new Button();
                batch.Text = _batchConfirm
                    ? ("确认回收 " + n + " 件？+" + sum + " 银两")
                    : ("一键回收白/绿未穿（" + n + " 件 +" + sum + "）");
                batch.CustomMinimumSize = new Vector2(360, RowH - 8);
                batch.AddThemeFontSizeOverride("font_size", FontBtn);
                if (_batchConfirm) batch.AddThemeColorOverride("font_color", UISignalColors.Lose);
                batch.Pressed += OnBatchRecycle;
                _TSortRow.AddChild(batch);
            }
        }
    }

    void OnBatchRecycle()
    {
        if (!_batchConfirm)
        {
            // 第一次点只切到确认态：批量删道具必须有一步"你确定"的停顿
            AudioManager.PlayUISFX("ui_click");
            _batchConfirm = true;
            Build();
            return;
        }

        _batchConfirm = false;
        List<int> uids = new List<int>();
        for (int i = 0; i < _bag.Items.Count; i++)
        {
            EquipItem it = _bag.Items[i];
            EquipConfig cfg = CfgOf(it.ConfigId);
            if (cfg == null) continue;
            if ((int)cfg.Rarity > 2) continue;
            if (_bag.WornBy(it.Uid) != 0) continue;
            uids.Add(it.Uid);
        }

        long total = 0;
        for (int i = 0; i < uids.Count; i++)
        {
            EquipItem it = _bag.Find(uids[i]);
            EquipConfig cfg = it != null ? CfgOf(it.ConfigId) : null;
            if (cfg == null) continue;
            total += EquipRules.RecycleValue(it, cfg);
            _bag.Remove(uids[i]);
        }

        if (total > 0 && _silver != null) _silver.Add(total);
        AudioManager.PlayEventSFX("ui_confirm");
        Build();
    }

    /// <summary>收集符合条件、且可以回收（未穿 + 稀有度不超过 maxRarity）的件数与总价。</summary>
    void CollectRecyclable(int maxRarity, ref int count, ref long sum)
    {
        for (int i = 0; i < _bag.Items.Count; i++)
        {
            EquipItem it = _bag.Items[i];
            EquipConfig cfg = CfgOf(it.ConfigId);
            if (cfg == null) continue;
            if ((int)cfg.Rarity > maxRarity) continue;
            if (_bag.WornBy(it.Uid) != 0) continue;
            count++;
            sum += EquipRules.RecycleValue(it, cfg);
        }
    }

    /// <summary>当前视野内的行囊列表（已按筛选与排序处理）。</summary>
    List<EquipItem> CollectBagView()
    {
        List<EquipItem> list = new List<EquipItem>();

        for (int i = 0; i < _bag.Items.Count; i++)
        {
            EquipItem it = _bag.Items[i];
            EquipConfig cfg = CfgOf(it.ConfigId);
            if (cfg == null) continue;

            if (_filterSlot != 0 && (int)cfg.Slot != _filterSlot) continue;

            if (_filterKind == 1 && cfg.SetId <= 0) continue;
            if (_filterKind == 2 && cfg.SetId > 0) continue;
            if (_filterKind == 3 && _bag.WornBy(it.Uid) != 0) continue;

            list.Add(it);
        }

        list.Sort(CompareForView);
        return list;
    }

    int CompareForView(EquipItem a, EquipItem b)
    {
        EquipConfig ca = CfgOf(a.ConfigId);
        EquipConfig cb = CfgOf(b.ConfigId);
        int ra = ca != null ? (int)ca.Rarity : 0;
        int rb = cb != null ? (int)cb.Rarity : 0;

        if (_sortMode == 1)
        {
            if (a.Level != b.Level) return b.Level.CompareTo(a.Level);
            if (ra != rb) return rb.CompareTo(ra);
            return b.Uid.CompareTo(a.Uid);
        }

        if (_sortMode == 2) return b.Uid.CompareTo(a.Uid);

        // 默认：稀有度降序 → 强化降序 → 新获得优先
        if (ra != rb) return rb.CompareTo(ra);
        if (a.Level != b.Level) return b.Level.CompareTo(a.Level);
        return b.Uid.CompareTo(a.Uid);
    }

    Label TextTag(string text)
    {
        Label l = new Label();
        l.Text = text;
        l.CustomMinimumSize = new Vector2(56, RowH - 14);
        l.AddThemeFontSizeOverride("font_size", FontSub);
        l.AddThemeColorOverride("font_color", UIThemeManager.TextMuted);
        l.VerticalAlignment = VerticalAlignment.Center;
        return l;
    }

    Control Spacer(int w)
    {
        Control c = new Control();
        c.CustomMinimumSize = new Vector2(w, 1);
        return c;
    }

    Button Chip(string text, bool active, Action onClick)
    {
        Button b = new Button();
        b.Text = text;
        b.CustomMinimumSize = new Vector2(0, RowH - 14);
        b.AddThemeFontSizeOverride("font_size", FontBtn);
        if (active) b.AddThemeColorOverride("font_color", UIThemeManager.Accent);
        b.Pressed += () =>
        {
            AudioManager.PlayUISFX("ui_click");
            onClick();
        };
        return b;
    }

    // ================= 辅助 =================

    /// <summary>第一行：稀有度 + 名字 + 强化等级 + 基础属性 + 套装归属。</summary>
    string DescribeHead(EquipConfig cfg, EquipItem item)
    {
        string text = "[" + EquipRules.RarityName(cfg.Rarity) + "] " + cfg.Name;
        if (item != null && item.Level > 0) text += " +" + item.Level;

        double atk = EquipRules.AtkOf(item, cfg);
        double hp = EquipRules.HpOf(item, cfg);
        double spd = EquipRules.SpdOf(item, cfg);

        text += "　";
        if (atk > 0) text += "攻+" + atk.ToString("0") + " ";
        if (hp > 0) text += "命+" + hp.ToString("0") + " ";
        if (spd > 0) text += "速+" + spd.ToString("0");

        // 套装归属（绿及以上才可能有）
        if (cfg.SetId > 0)
        {
            EquipSetConfig sc = SetOf(cfg.SetId);
            if (sc != null) text += "　〔" + sc.Name + "套〕";
        }

        return text;
    }

    /// <summary>第二行：随机词条（掉落时掷定，不随强化变化）。无词条返回空串。</summary>
    string DescribeAffix(EquipItem item)
    {
        if (item == null || item.AffixIds.Count == 0) return "";

        string text = "词条：";
        for (int i = 0; i < item.AffixIds.Count; i++)
        {
            EquipAffixConfig a = AffixOf(item.AffixIds[i]);
            if (a == null) continue;
            if (i > 0) text += "、";
            text += a.Name + "(" + AffixText(a) + ")";
        }

        return text;
    }

    /// <summary>词条效果的可读文本（数值来自配置，不在这里写死）。</summary>
    string AffixText(EquipAffixConfig a)
    {
        switch (a.Effect)
        {
            case EquipAffixEffect.AtkPct: return "攻+" + a.Value.ToString("0.#") + "%";
            case EquipAffixEffect.HpPct: return "命+" + a.Value.ToString("0.#") + "%";
            case EquipAffixEffect.SpdPct: return "速+" + a.Value.ToString("0.#") + "%";
            case EquipAffixEffect.ProcAction: return "行动触发+" + a.Value.ToString("0.#") + "%";
            case EquipAffixEffect.ProcHit: return "受击触发+" + a.Value.ToString("0.#") + "%";
            case EquipAffixEffect.CondDownAction: return "行动需求-" + a.Value.ToString("0.#");
            case EquipAffixEffect.ReplayPct: return "重放+" + a.Value.ToString("0.#") + "%";
            case EquipAffixEffect.ReplayLimit: return "重放上限+" + a.Value.ToString("0.#");
            case EquipAffixEffect.ShieldPct: return "护盾+" + a.Value.ToString("0.#") + "%";
            case EquipAffixEffect.WeightDelta: return "受击权重" + a.Value.ToString("0.#");
            case EquipAffixEffect.CounterUp: return "开战计数+" + a.Value.ToString("0.#");
            default: return a.Value.ToString("0.#");
        }
    }

    int WornBy(int uid)
    {
        if (_roster == null) return 0;

        for (int i = 0; i < _roster.Count; i++)
        {
            int[] slots = _bag.SlotsOf(_roster[i].Id);
            for (int s = 0; s < EquipBag.SlotCount; s++)
                if (slots[s] == uid) return _roster[i].Id;
        }

        return 0;
    }

    string HeroNameOf(int heroId)
    {
        if (_roster == null) return "#" + heroId;

        for (int i = 0; i < _roster.Count; i++)
            if (_roster[i].Id == heroId) return _roster[i].Name;

        return "#" + heroId;
    }

    int RosterCount()
    {
        return _roster != null ? _roster.Count : 0;
    }

    EquipConfig CfgOf(int configId)
    {
        if (_pool == null) return null;

        for (int i = 0; i < _pool.Count; i++)
            if (_pool[i].Id == configId) return _pool[i];

        return null;
    }

    EquipSetConfig SetOf(int setId)
    {
        if (_setPool == null) return null;

        for (int i = 0; i < _setPool.Count; i++)
            if (_setPool[i].Id == setId) return _setPool[i];

        return null;
    }

    EquipAffixConfig AffixOf(int affixId)
    {
        if (_affixPool == null) return null;

        for (int i = 0; i < _affixPool.Count; i++)
            if (_affixPool[i].Id == affixId) return _affixPool[i];

        return null;
    }

    /// <summary>套装状态摘要：已成型写「4件」、只凑到 2 件写「2件」、单件不显示。</summary>
    string SetSummary(int heroId)
    {
        EquipTuning tuning = EquipSets.Resolve(_bag, heroId, CfgOf, AffixOf, SetOf);
        if (tuning.Sets.Count == 0) return "未成套（4 栏同套可激活 4 件效果）";

        string text = "";
        for (int i = 0; i < tuning.Sets.Count; i++)
        {
            SetState st = tuning.Sets[i];
            if (st.Pieces < 2) continue;          // 单件没有效果，不占位置
            if (text.Length > 0) text += "　";

            text += st.Name + " " + st.Pieces + "/4";
            if (st.Active4) text += "（2+4 件全开）";
            else if (st.Active2) text += "（2 件生效）";
        }

        return text.Length > 0 ? text : "散件搭配（凑同套 2/4 件可解锁套装效果）";
    }

    static void ClearChildren(Node parent)
    {
        if (parent == null) return;

        foreach (Node c in parent.GetChildren())
        {
            parent.RemoveChild(c);
            c.QueueFree();
        }
    }
}
