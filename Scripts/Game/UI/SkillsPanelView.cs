// 文件：SkillsPanelView.cs
// 《轮回大侠》M2.4 **武学装配**面板（Godot 节点组件：挂在 BattlePanel.tscn 的 TSkillsPanel 上）
//
// 交互（两次点击，低摩擦 —— 验收信号 A：30 分钟内主动调整过武学）：
//   ① 点上方某个侠客的某个槽位（选中，按钮前出现 ▶）
//   ② 点下方武学池里的武学 → 装进该槽；点「（清空该槽）」→ 清空
//
// 状态自持：装配状态在 BattlePanel 持有的 SkillLoadout 里，本视图只做"选中槽 + 展示 + 转发"。
// 子节点 Bg / Heading / CloseBtn / Hint / Content / Pool / BottomHint 由 _Ready 自取
//   （非 T 名 → 不会被 GenUICode 绑进面板）。

using System;
using System.Collections.Generic;
using Godot;

public partial class SkillsPanelView : Control
{
    static readonly Color Gold = new Color(1.00f, 0.84f, 0.35f);
    static readonly Color Dim = new Color(0.62f, 0.66f, 0.74f);
    static readonly Color Green = new Color(0.45f, 0.95f, 0.55f);

    Label _heading;
    Button _closeBtn;
    Label _hint;
    VBoxContainer _content;
    GridContainer _pool;
    Label _bottomHint;

    SkillLoadout _loadout;
    List<HeroConfig> _roster;
    Func<int, SkillConfig> _skillOf;
    List<SkillConfig> _poolSkills;
    Action _onClose;

    int _selHero = -1;   // -1 = 未选中槽
    int _selSlot = -1;

    public override void _Ready()
    {
        _heading = GetNodeOrNull<Label>("Heading");
        _closeBtn = GetNodeOrNull<Button>("CloseBtn");
        _hint = GetNodeOrNull<Label>("Hint");
        _content = GetNodeOrNull<VBoxContainer>("Content");
        _pool = GetNodeOrNull<GridContainer>("Pool");
        _bottomHint = GetNodeOrNull<Label>("BottomHint");

        if (_closeBtn != null) _closeBtn.Pressed += OnClosePressed;
        Visible = false;
    }

    /// <summary>由 BattlePanel 在打开前注入依赖并重建界面。</summary>
    public void Bind(SkillLoadout loadout, List<HeroConfig> roster,
                     Func<int, SkillConfig> skillOf, List<SkillConfig> poolSkills, Action onClose)
    {
        _loadout = loadout;
        _roster = roster;
        _skillOf = skillOf;
        _poolSkills = poolSkills;
        _onClose = onClose;

        _selHero = -1;
        _selSlot = -1;

        Visible = true;
        Build();
    }

    public void Close()
    {
        Visible = false;
    }

    void OnClosePressed()
    {
        Visible = false;
        if (_onClose != null) _onClose();
    }

    // ================= 绘制 =================
    void Build()
    {
        if (_loadout == null || _roster == null) return;

        bool hasSel = _selHero > 0 && _selSlot >= 0 && _selSlot < SkillLoadout.Slots;

        if (_hint != null)
        {
            if (hasSel)
                _hint.Text = "已选中　" + HeroName(_selHero) + "　第 " + (_selSlot + 1) + " 槽　→　点下方武学装配";
            else
                _hint.Text = "先点上方某个槽位，再点下方武学装配";
        }
        if (_bottomHint != null)
            _bottomHint.Text = "免费更换、随时生效（下一场战斗起）。同一侠客不可重复装配同一武学。";

        BuildHeroRows();
        BuildPool(hasSel);
    }

    void BuildHeroRows()
    {
        if (_content == null) return;
        foreach (Node c in _content.GetChildren()) c.QueueFree();

        foreach (HeroConfig h in _roster)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);

            var nameL = new Label();
            nameL.CustomMinimumSize = new Vector2(180, 44);
            nameL.Text = h.Name;
            nameL.AddThemeFontSizeOverride("font_size", 16);
            nameL.AddThemeColorOverride("font_color", Gold);
            row.AddChild(nameL);

            int[] slots = _loadout.Get(h.Id);
            for (int i = 0; i < SkillLoadout.Slots; i++)
            {
                int heroId = h.Id;
                int slot = i;
                bool selected = _selHero == heroId && _selSlot == slot;
                int skillId = slots[i];

                var btn = new Button();
                string slotTag;
                switch (slot)
                {
                    case 0: slotTag = "①"; break;
                    case 1: slotTag = "②"; break;
                    default: slotTag = "③"; break;
                }
                string nm = skillId > 0 ? SkillName(skillId) : "（空）";
                btn.Text = (selected ? "▶ " : "") + slotTag + " " + nm;
                btn.CustomMinimumSize = new Vector2(320, 44);
                if (selected) btn.AddThemeColorOverride("font_color", Green);
                else if (skillId <= 0) btn.AddThemeColorOverride("font_color", Dim);

                btn.Pressed += () =>
                {
                    _selHero = heroId;
                    _selSlot = slot;
                    Build();
                };
                row.AddChild(btn);
            }

            _content.AddChild(row);
        }
    }

    void BuildPool(bool hasSel)
    {
        if (_pool == null) return;
        foreach (Node c in _pool.GetChildren()) c.QueueFree();

        // 第一格 = 清空当前槽
        var clearBtn = new Button();
        clearBtn.Text = "（清空该槽）";
        clearBtn.CustomMinimumSize = new Vector2(180, 40);
        clearBtn.Disabled = !hasSel;
        clearBtn.Pressed += () =>
        {
            if (_selHero > 0 && _selSlot >= 0)
            {
                _loadout.Clear(_selHero, _selSlot);
                Build();
            }
        };
        _pool.AddChild(clearBtn);

        if (_poolSkills == null) return;

        foreach (SkillConfig s in _poolSkills)
        {
            if (s == null) continue;
            int skillId = s.Id;
            bool equipped = hasSel && _loadout.Has(_selHero, skillId);

            var b = new Button();
            b.Text = s.Name;
            b.CustomMinimumSize = new Vector2(180, 40);
            b.Disabled = !hasSel;
            if (equipped) b.AddThemeColorOverride("font_color", Gold);

            b.Pressed += () =>
            {
                if (_selHero > 0 && _selSlot >= 0)
                {
                    _loadout.Equip(_selHero, _selSlot, skillId);
                    Build();
                }
            };
            _pool.AddChild(b);
        }
    }

    string SkillName(int skillId)
    {
        if (_skillOf == null) return "#" + skillId;
        SkillConfig cfg = _skillOf(skillId);
        if (cfg == null) return "#" + skillId;
        return cfg.Name;
    }

    string HeroName(int heroId)
    {
        if (_roster == null) return "#" + heroId;
        foreach (HeroConfig h in _roster)
            if (h.Id == heroId) return h.Name;
        return "#" + heroId;
    }
}
