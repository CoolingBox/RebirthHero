// 文件：SkillsPanel.cs
// 《轮回大侠》M2.4 武学装配面板（框架面板：BasePanel 子类，由 UIManager 开关）
//
// 交互（两次点击，低摩擦 —— 验收信号 A：30 分钟内主动调整过武学）：
//   ① 点上方某个侠客的某个槽位（选中，按钮前出现 ▶）
//   ② 点下方武学池里的武学 → 装进该槽；点「（清空该槽）」→ 清空
//
// 结构分工（与 InnPanel 一致）：
//   · SkillsPanel.tscn            —— 静态外壳：TBg / THeading / TCloseBtn / THint / TContent / TPool / TBottomHint
//   · SkillsPanel.cs              —— 本文件
//   · Generate/SkillsPanelBind.cs —— GenUICode 生成
//
// 状态自持：装配状态在 BattlePanel 持有的 SkillLoadout 里，本面板只做"选中槽 + 展示 + 转发"。

using System;
using System.Collections.Generic;
using Godot;
using TinyFramework;

public partial class SkillsPanel : BasePanel
{
    // 不缓存成 static readonly：否则换主题后这里永远是旧色
    static Color Gold { get { return UIThemeManager.Accent; } }
    static Color Dim { get { return UIThemeManager.TextMuted; } }
    static Color Green { get { return UISignalColors.Win; } }

    // UIManager.ShowPanel<T>() 不带参，依赖用静态上下文在打开前注入
    static SkillLoadout _ctxLoadout;
    static List<HeroConfig> _ctxRoster;
    static Func<int, SkillConfig> _ctxSkillOf;
    static List<SkillConfig> _ctxPoolSkills;
    static Action _ctxOnClose;

    static SkillsPanel _current;

    public static void BindData(SkillLoadout loadout, List<HeroConfig> roster,
                                Func<int, SkillConfig> skillOf, List<SkillConfig> poolSkills,
                                Action onClose)
    {
        _ctxLoadout = loadout;
        _ctxRoster = roster;
        _ctxSkillOf = skillOf;
        _ctxPoolSkills = poolSkills;
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

    SkillLoadout _loadout;
    List<HeroConfig> _roster;
    Func<int, SkillConfig> _skillOf;
    List<SkillConfig> _poolSkills;
    Action _onClose;

    BackdropLayer _backdrop;
    bool _wired;

    int _selHero = -1;   // -1 = 未选中槽
    int _selSlot = -1;

    public override void OnClose()
    {
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

        _loadout = _ctxLoadout;
        _roster = _ctxRoster;
        _skillOf = _ctxSkillOf;
        _poolSkills = _ctxPoolSkills;
        _onClose = _ctxOnClose;

        // 每次打开都从"未选中"开始：跨次残留选中会让下一次装配误触
        _selHero = -1;
        _selSlot = -1;

        EnsureBackdrop();

        if (!_wired)
        {
            if (_TCloseBtn != null) _TCloseBtn.Pressed += OnClosePressed;
            _wired = true;
        }

        Build();
    }

    public void ApplyTheme()
    {
        if (_loadout != null) Build();
    }

    void OnClosePressed()
    {
        AudioManager.PlayUISFX("ui_close");
        HidePanel<SkillsPanel>();
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
        if (_loadout == null || _roster == null) return;

        bool hasSel = _selHero > 0 && _selSlot >= 0 && _selSlot < SkillLoadout.Slots;

        if (_THint != null)
        {
            if (hasSel)
                _THint.Text = "已选中　" + HeroName(_selHero) + "　第 " + (_selSlot + 1) + " 槽　→　点下方武学装配";
            else
                _THint.Text = "先点上方某个槽位，再点下方武学装配";
        }

        if (_TBottomHint != null)
            _TBottomHint.Text = "免费更换、随时生效（下一场战斗起）。同一侠客不可重复装配同一武学。";

        BuildHeroRows();
        BuildPool(hasSel);
    }

    void BuildHeroRows()
    {
        if (_TContent == null) return;
        foreach (Node c in _TContent.GetChildren()) c.QueueFree();

        foreach (HeroConfig h in _roster)
        {
            HBoxContainer row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);

            Label nameL = new Label();
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

                Button btn = new Button();
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

            _TContent.AddChild(row);
        }
    }

    void BuildPool(bool hasSel)
    {
        if (_TPool == null) return;
        foreach (Node c in _TPool.GetChildren()) c.QueueFree();

        // 第一格 = 清空当前槽
        Button clearBtn = new Button();
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
        _TPool.AddChild(clearBtn);

        if (_poolSkills == null) return;

        foreach (SkillConfig s in _poolSkills)
        {
            if (s == null) continue;
            int skillId = s.Id;
            bool equipped = hasSel && _loadout.Has(_selHero, skillId);

            Button b = new Button();
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
            _TPool.AddChild(b);
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
