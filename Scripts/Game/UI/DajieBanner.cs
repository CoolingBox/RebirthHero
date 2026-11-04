// 文件：DajieBanner.cs
// 《轮回大侠》顶部横幅 —— 大劫上门 / 操作被拒时，在屏幕最上方闪一行提示。
//
// 为什么要一个独立的 CanvasLayer，而不是往某个面板里塞 Label：
//   提示必须能盖在**任何**子界面（客栈 / 武学 / 装备 / 天赋 / 选关…）上面，
//   而那些面板都是 UIManager 下各自 new 出来的 Control，谁压谁只看打开顺序。
//   用 layer = 100 的 CanvasLayer 挂在场景根上，永远在默认 canvas(0) 之上，与顺序无关。
//
// 为什么闪完就消失，而不是常驻：
//   主界面本来就有「大劫上门（第 N 场）」标题，常驻横幅等于把它再喊一遍还挡画面。
//   闪约 3.4 秒是"提醒"，提醒到位就把画面还给玩家 —— 玩家此刻最需要看到的是战斗本身。
//
// 为什么单例复用而不是每次新建：
//   大劫每 2 分钟一次，一局下来要建几十个节点；复用同一个 Label 只改文字与动画，
//   也避免同一时刻两条横幅叠在一起。

using Godot;

public static class DajieBanner
{
    /// <summary>默认 canvas 是 0，所有面板都在它下面；100 保证压在所有面板之上。</summary>
    const int BannerLayer = 100;

    static CanvasLayer _layer;
    static ColorRect _bg;
    static Label _label;
    static Tween _tw;

    /// <summary>
    /// 闪一行提示（约 4 秒后自动消失）。连闪两次只会重置同一条，不会叠出两条。
    /// </summary>
    public static void Flash(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (!Ensure())
        {
            return;
        }

        _label.Text = text;
        _label.Visible = true;
        _bg.Visible = true;
        _label.Modulate = new Color(1f, 1f, 1f, 1f);

        if (_tw != null && _tw.IsValid())
        {
            _tw.Kill();
        }

        // 亮 → 暗 算一次，共闪 4 次（4 × 0.85s ≈ 3.4s）。
        // 每一轮都回到 1.0（而不是停在暗处），否则 Finished 之后 Label 会留着半透明状态。
        // 底条不跟着闪：底色闪会像屏幕故障，只有文字闪才是"提醒"。
        _tw = _layer.CreateTween();
        _tw.SetLoops(4);
        _tw.TweenProperty(_label, "modulate:a", 0.12f, 0.35f);
        _tw.TweenProperty(_label, "modulate:a", 1.0f, 0.50f);
        _tw.Finished += OnBlinkFinished;
    }

    static void OnBlinkFinished()
    {
        if (_label == null || !GodotObject.IsInstanceValid(_label))
        {
            return;
        }

        _label.Visible = false;
        _label.Modulate = new Color(1f, 1f, 1f, 1f);

        if (_bg != null && GodotObject.IsInstanceValid(_bg))
        {
            _bg.Visible = false;
        }
    }

    /// <summary>建一次节点并缓存。返回 false = 拿不到场景树（极早期调用），本次静默跳过。</summary>
    static bool Ensure()
    {
        if (_layer != null && GodotObject.IsInstanceValid(_layer))
        {
            return true;
        }

        SceneTree tree = Engine.GetMainLoop() as SceneTree;
        if (tree == null || tree.Root == null)
        {
            return false;
        }

        _layer = new CanvasLayer();
        _layer.Name = "DajieBanner";
        _layer.Layer = BannerLayer;
        tree.Root.AddChild(_layer);

        // 底条：压在顶部导航条上时，纯文字会和按钮糊在一起。一条半透明黑带能让文字
        // 在任何背景（主界面导航、子界面标题栏）上都读得清；它不跟着闪 —— 底色闪像屏幕故障。
        _bg = new ColorRect();
        _bg.Name = "Bg";
        _bg.Visible = false;
        _bg.Color = new Color(0.04f, 0.03f, 0.02f, 0.82f);
        _bg.MouseFilter = Control.MouseFilterEnum.Ignore;
        _bg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
        _bg.OffsetTop = 0f;
        _bg.OffsetBottom = 68f;

        _label = new Label();
        _label.Name = "Text";
        _label.Visible = false;
        _label.HorizontalAlignment = HorizontalAlignment.Center;
        _label.VerticalAlignment = VerticalAlignment.Center;
        _label.MouseFilter = Control.MouseFilterEnum.Ignore;   // 提示不该吃掉任何点击

        // 贴屏幕顶边的一条：TopWide 会把宽度拉满，再给上下留出条高
        _label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
        _label.OffsetTop = 8f;
        _label.OffsetBottom = 64f;

        _label.AddThemeFontSizeOverride("font_size", 30);
        _label.AddThemeColorOverride("font_color", new Color(1f, 0.86f, 0.42f));
        _label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.92f));
        _label.AddThemeConstantOverride("outline_size", 8);

        // 中文字形来自主题字体 —— 漏了这一句会显示成一排方框
        UIThemeManager.ApplyTo(_label);

        _layer.AddChild(_bg);
        _layer.AddChild(_label);
        return true;
    }
}
