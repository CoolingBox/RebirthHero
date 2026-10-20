// 文件：StagePanel.cs
// 《轮回大侠》选关面板（框架面板：BasePanel 子类，由 UIManager 开关）
//
// 职责：按"章节（每 MatTierEveryLayer 层）"列出层段 → 显示该段材料品阶 → 点层锁定挂机。
//
// 结构分工（与 InnPanel 一致）：
//   · StagePanel.tscn            —— 静态外壳：TBg / THeading / TCloseBtn / THint / TContent / TBottomHint
//   · StagePanel.cs              —— 本文件：全部逻辑
//   · Generate/StagePanelBind.cs —— GenUICode 生成
//
// 打开：BattlePanel 点「选关」→ StagePanel.BindData(run, bag, onGo, onClose) → UIManager.ShowPanel<StagePanel>()

using System;
using System.Collections.Generic;
using Godot;
using TinyFramework;

public partial class StagePanel : BasePanel
{
    // UIManager.ShowPanel<T>() 不带参，依赖用静态上下文在打开前注入
    static StageRun _ctxRun;
    static MaterialBag _ctxBag;
    static Action<int> _ctxOnGo;
    static Action _ctxOnClose;

    static StagePanel _current;

    public static void BindData(StageRun run, MaterialBag bag, Action<int> onGo, Action onClose)
    {
        _ctxRun = run;
        _ctxBag = bag;
        _ctxOnGo = onGo;
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

    StageRun _run;
    MaterialBag _bag;
    Action<int> _onGo;
    Action _onClose;
    BackdropLayer _backdrop;
    bool _wired;

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

        _run = _ctxRun;
        _bag = _ctxBag;
        _onGo = _ctxOnGo;
        _onClose = _ctxOnClose;

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
        Build();
    }

    void OnClosePressed()
    {
        AudioManager.PlayUISFX("ui_close");
        HidePanel<StagePanel>();
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
        if (_run == null) return;

        if (_THint != null)
            _THint.Text = "当前 第 " + _run.Layer + " 层　·　最高 第 " + _run.BestLayer + " 层　·　持有材料：" + DescribeBag();

        if (_TBottomHint != null)
            _TBottomHint.Text = "点某层 → 进入锁层挂机（只刷该层）。想继续往上冲 → 关掉面板点「继续推图」。";

        if (_TContent == null) return;
        foreach (Node c in _TContent.GetChildren()) c.QueueFree();

        int step = MaterialConfig.MatTierEveryLayer > 0 ? MaterialConfig.MatTierEveryLayer : 10;
        int best = _run.BestLayer;

        for (int tier = 1; tier <= MaterialCatalog.Count; tier++)
        {
            int lo = (tier - 1) * step + 1;
            if (lo > best) break;                 // 整章未解锁 → 不显示
            int hi = lo + step - 1;
            if (hi > best) hi = best;

            _TContent.AddChild(MakeChapterRow(tier, lo, hi));
        }
    }

    /// <summary>一章一行：品阶标签 + 该段的层按钮。</summary>
    Control MakeChapterRow(int tier, int lo, int hi)
    {
        HBoxContainer row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);

        Label tag = new Label();
        tag.CustomMinimumSize = new Vector2(300, 40);
        tag.Text = tier + " 档 · " + MaterialCatalog.NameOf(tier) + "（" + lo + "~" + hi + " 层）";
        tag.AddThemeFontSizeOverride("font_size", 18);
        tag.AddThemeColorOverride("font_color", UIThemeManager.Accent);
        row.AddChild(tag);

        for (int layer = lo; layer <= hi; layer++)
        {
            int target = layer;        // 每轮新建局部变量，闭包各自捕获（避免 for 变量共享）
            Button btn = new Button();
            btn.Text = target.ToString();
            btn.CustomMinimumSize = new Vector2(56, 40);
            if (target == _run.Layer) btn.AddThemeColorOverride("font_color", UISignalColors.Win);
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

        List<string> parts = new List<string>();
        for (int t = 1; t <= MaterialCatalog.Count; t++)
        {
            long n = _bag.Get(t);
            if (n > 0) parts.Add(MaterialCatalog.NameOf(t) + " " + n);
        }

        if (parts.Count == 0) return "（暂无）";
        return string.Join("　", parts);
    }
}
