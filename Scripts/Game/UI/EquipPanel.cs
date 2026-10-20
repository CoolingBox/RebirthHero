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
    static Action _ctxOnClose;

    static EquipPanel _current;

    public static void BindData(EquipBag bag, List<EquipConfig> pool, List<HeroConfig> roster,
                                Silver silver, Action onClose)
    {
        _ctxBag = bag;
        _ctxPool = pool;
        _ctxRoster = roster;
        _ctxSilver = silver;
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
                          + "　（银两 " + BigNum.From(_silver != null ? _silver.Amount : 0).ToString(2) + "）";
        }

        BuildHeroRow();
        BuildSlots(hero);
        BuildBag(hero);
    }

    /// <summary>侠客切换：每个侠客各有独立四栏，必须先选人。</summary>
    void BuildHeroRow()
    {
        if (_TSlotBox == null) return;

        ClearChildren(_TSlotBox);

        HBoxContainer row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);

        Label tag = new Label();
        tag.Text = "侠客";
        tag.CustomMinimumSize = new Vector2(70, 36);
        tag.AddThemeColorOverride("font_color", UIThemeManager.TextMuted);
        row.AddChild(tag);

        for (int i = 0; i < RosterCount(); i++)
        {
            int idx = i;
            bool selected = i == _selHero;

            Button btn = new Button();
            btn.Text = selected ? ("● " + _roster[i].Name) : _roster[i].Name;
            btn.CustomMinimumSize = new Vector2(150, 36);
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
            tag.CustomMinimumSize = new Vector2(70, 40);
            tag.AddThemeColorOverride("font_color", UIThemeManager.TextMuted);
            row.AddChild(tag);

            Label content = new Label();
            content.CustomMinimumSize = new Vector2(700, 40);

            if (uid == 0)
            {
                content.Text = "（空栏）";
                content.AddThemeColorOverride("font_color", UIThemeManager.TextMuted);
            }
            else
            {
                EquipItem item = _bag.Find(uid);
                EquipConfig cfg = item != null ? CfgOf(item.ConfigId) : null;
                if (cfg == null)
                {
                    content.Text = "（数据缺失）";
                    content.AddThemeColorOverride("font_color", UISignalColors.Lose);
                }
                else
                {
                    content.Text = Describe(cfg, item);
                    content.AddThemeColorOverride("font_color", UISignalColors.RarityOf(cfg.Rarity));
                }
            }
            row.AddChild(content);

            if (uid != 0)
            {
                Button off = new Button();
                off.Text = "卸下";
                off.CustomMinimumSize = new Vector2(100, 40);
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

    /// <summary>行囊：全部持有装备，点「穿上」进对应栏。已穿着的标出在谁身上。</summary>
    void BuildBag(HeroConfig hero)
    {
        if (_TBagList == null) return;

        ClearChildren(_TBagList);

        Label title = new Label();
        title.Text = "行囊（" + _bag.Count + " 件）";
        title.AddThemeFontSizeOverride("font_size", 18);
        title.AddThemeColorOverride("font_color", UIThemeManager.Accent);
        _TBagList.AddChild(title);

        if (_bag.Count == 0)
        {
            Label empty = new Label();
            empty.Text = "（还没有装备。打怪有概率掉落。）";
            empty.AddThemeColorOverride("font_color", UIThemeManager.TextMuted);
            _TBagList.AddChild(empty);
            return;
        }

        for (int i = 0; i < _bag.Items.Count; i++)
        {
            EquipItem item = _bag.Items[i];
            EquipConfig cfg = CfgOf(item.ConfigId);
            if (cfg == null) continue;

            HBoxContainer row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);

            Label name = new Label();
            name.Text = Describe(cfg, item);
            name.CustomMinimumSize = new Vector2(560, 36);
            name.AddThemeColorOverride("font_color", UISignalColors.RarityOf(cfg.Rarity));
            row.AddChild(name);

            int wornBy = WornBy(item.Uid);
            Label state = new Label();
            state.CustomMinimumSize = new Vector2(240, 36);
            if (wornBy == hero.Id) state.Text = "（" + hero.Name + " 已穿）";
            else if (wornBy > 0) state.Text = "（在 " + HeroNameOf(wornBy) + " 身上）";
            else state.Text = "";
            state.AddThemeColorOverride("font_color", UIThemeManager.TextMuted);
            row.AddChild(state);

            int uid = item.Uid;
            Button wear = new Button();
            wear.Text = "穿上";
            wear.CustomMinimumSize = new Vector2(90, 36);
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
            plus.CustomMinimumSize = new Vector2(150, 36);
            plus.Disabled = cost < 0 || wornBy != hero.Id;
            plus.Pressed += () =>
            {
                AudioManager.PlayUISFX("ui_upgrade");
                if (EquipRules.TryEnhance(item, _silver)) Build();
            };
            row.AddChild(plus);

            _TBagList.AddChild(row);
        }
    }

    // ================= 辅助 =================

    string Describe(EquipConfig cfg, EquipItem item)
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

        return text;
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
