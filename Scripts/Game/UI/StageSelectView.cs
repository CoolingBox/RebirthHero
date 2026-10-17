// 文件：StageSelectView.cs
// 《轮回大侠》**选关面板**（Godot 节点组件：挂在 BattlePanel.tscn 的 TStagePanel 上）
// 职责：按"章节（每 MatTierEveryLayer 层）"列出层段 → 显示该段材料品阶 → 点层锁定挂机。
// 状态自持：StageRun / MaterialBag 引用 + 回调，由 BattlePanel 经 Bind 注入。
// 子节点 Bg / Title / CloseBtn / Hint / Content / BottomHint 由 _Ready 自取
//   （非 T 名 → 不会被 GenUICode 绑进 BattlePanel，保持面板只认这个组件）。

using System;
using System.Collections.Generic;
using Godot;

public partial class StageSelectView : Control
{
    static readonly Color Gold = new Color(1.00f, 0.84f, 0.35f);
    static readonly Color Dim = new Color(0.62f, 0.66f, 0.74f);
    static readonly Color Green = new Color(0.45f, 0.95f, 0.55f);

    Label _title;
    Button _closeBtn;
    Label _hint;
    VBoxContainer _content;
    Label _bottomHint;

    StageRun _run;
    MaterialBag _bag;
    Action<int> _onGo;
    Action _onClose;

    public override void _Ready()
    {
        _title = GetNodeOrNull<Label>("Heading");
        _closeBtn = GetNodeOrNull<Button>("CloseBtn");
        _hint = GetNodeOrNull<Label>("Hint");
        _content = GetNodeOrNull<VBoxContainer>("Content");
        _bottomHint = GetNodeOrNull<Label>("BottomHint");

        if (_closeBtn != null) _closeBtn.Pressed += OnClosePressed;
        Visible = false;
    }

    /// <summary>由 BattlePanel 在打开前注入依赖并重建列表。</summary>
    public void Bind(StageRun run, MaterialBag bag, Action<int> onGo, Action onClose)
    {
        _run = run;
        _bag = bag;
        _onGo = onGo;
        _onClose = onClose;
        Visible = true;
        Build();
    }

    public void Open()
    {
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
        if (_run == null) return;

        if (_hint != null)
            _hint.Text = "当前 第 " + _run.Layer + " 层　·　最高 第 " + _run.BestLayer + " 层　·　持有材料：" + DescribeBag();

        if (_bottomHint != null)
            _bottomHint.Text = "点某层 → 进入锁层挂机（只刷该层）。想继续往上冲 → 关掉面板点「继续推图」。";

        if (_content == null) return;
        foreach (Node c in _content.GetChildren()) c.QueueFree();

        int step = MaterialConfig.MatTierEveryLayer > 0 ? MaterialConfig.MatTierEveryLayer : 10;
        int best = _run.BestLayer;

        for (int tier = 1; tier <= MaterialCatalog.Count; tier++)
        {
            int lo = (tier - 1) * step + 1;
            if (lo > best) break;                 // 整章未解锁 → 不显示
            int hi = lo + step - 1;
            if (hi > best) hi = best;

            _content.AddChild(MakeChapterRow(tier, lo, hi));
        }
    }

    /// <summary>一章一行：品阶标签 + 该段的层按钮。</summary>
    Control MakeChapterRow(int tier, int lo, int hi)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);

        var tag = new Label();
        tag.CustomMinimumSize = new Vector2(300, 40);
        tag.Text = tier + " 档 · " + MaterialCatalog.NameOf(tier) + "（" + lo + "~" + hi + " 层）";
        tag.AddThemeFontSizeOverride("font_size", 15);
        tag.AddThemeColorOverride("font_color", Gold);
        row.AddChild(tag);

        for (int layer = lo; layer <= hi; layer++)
        {
            int target = layer;        // 每轮新建局部变量，闭包各自捕获（避免 for 变量共享）
            var btn = new Button();
            btn.Text = target.ToString();
            btn.CustomMinimumSize = new Vector2(56, 40);
            if (target == _run.Layer) btn.AddThemeColorOverride("font_color", Green);
            btn.Pressed += () => OnLayerPressed(target);
            row.AddChild(btn);
        }
        return row;
    }

    void OnLayerPressed(int layer)
    {
        if (_onGo != null) _onGo(layer);
    }

    string DescribeBag()
    {
        if (_bag == null) return "—";
        var parts = new List<string>();
        for (int t = 1; t <= MaterialCatalog.Count; t++)
        {
            long n = _bag.Get(t);
            if (n > 0) parts.Add(MaterialCatalog.NameOf(t) + " " + n);
        }
        if (parts.Count == 0) return "（暂无）";
        return string.Join("　", parts);
    }
}
