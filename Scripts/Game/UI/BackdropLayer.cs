// 文件：BackdropLayer.cs
// 《轮回大侠》全屏背景层 —— 武侠底图 + 主题调色 + 暗角
//
// 为什么不做纯色背景：
//   纯色背景会让 UI 像"贴在色卡上"，缺少空间感。加一层山水底图后，
//   面板是"压在画上"的，画面立刻有纵深。
//
// 三层结构（自下而上）：
//   1. 底图   —— 生成的武侠山水（浅色主题用宣纸山水，暗色主题共用夜雨江湖）
//   2. 调色   —— 叠一层主题色（朱漆偏红、青瓷偏青、夜行偏墨蓝），让同一张图变四种气候
//   3. 暗角   —— 径向渐变，四周压暗、中心留亮，把视觉焦点收到中间的战斗区
//
// 参数从哪来：全在 .tres（Wuxia 类型下的 backdrop_tint / backdrop_shade）。
//   背景色调也属于配色，按项目约定不写在 C# 里 —— 改氛围请改资源，不要改代码。
//
// 为什么暗色主题共用一张图：
//   8 套里 5 套是暗色，若各生成一张，体积翻五倍而差异主要靠叠色就能做到。
//   一张夜雨底图 + 多种叠色，效果差别已经足够明显，也便于后期统一改图。

using Godot;

public partial class BackdropLayer : Node2D
{
    const string BgInkPath = "res://Resources/UI/Background/bg_ink_light.png";
    const string BgNightPath = "res://Resources/UI/Background/bg_night_dark.png";

    static Texture2D _bgInk;
    static Texture2D _bgNight;

    TextureRect _image;
    ColorRect _tint;
    TextureRect _vignette;

    public override void _Ready()
    {
        // Node2D 没有 MouseFilter，默认不接收 GUI 输入，天然不会挡住上级按钮。
        BuildLayers();
        ApplyTheme();

        GetTree().Root.SizeChanged += OnViewportResized;
        UIThemeManager.Changed += OnThemeChanged;
        OnViewportResized();
    }

    public override void _ExitTree()
    {
        // 静态事件与 Window 信号都要退订，否则主题切换 / 窗口变化会调到已释放的节点上
        if (GetTree() != null && GetTree().Root != null)
        {
            GetTree().Root.SizeChanged -= OnViewportResized;
        }
        UIThemeManager.Changed -= OnThemeChanged;
    }

    /// <summary>
    /// Node2D 没有 anchors，三层背景必须显式按 viewport 尺寸铺满。
    /// 项目是固定 1920×1080 + canvas_items，这里主要防窗口尺寸变化后背景留在旧尺寸。
    /// </summary>
    void OnViewportResized()
    {
        Vector2 s = GetViewportRect().Size;
        if (_image != null)
        {
            _image.Position = Vector2.Zero;
            _image.Size = s;
        }
        if (_tint != null)
        {
            _tint.Position = Vector2.Zero;
            _tint.Size = s;
        }
        if (_vignette != null)
        {
            _vignette.Position = Vector2.Zero;
            _vignette.Size = s;
        }
    }

    /// <summary>按当前主题刷新底图、叠色与暗角。</summary>
    public void ApplyTheme()
    {
        int id = UIThemeManager.Id;

        if (_image != null)
        {
            _image.Texture = LoadBackdrop(id);
        }

        if (_tint != null)
        {
            _tint.Color = UIThemeManager.ColorOf(id, UIThemeKey.BackdropTint);
        }

        if (_vignette != null)
        {
            _vignette.Modulate = UIThemeManager.ColorOf(id, UIThemeKey.BackdropShade);
        }
    }

    void OnThemeChanged(int themeId)
    {
        ApplyTheme();
    }

    void BuildLayers()
    {
        // 2026-11-16 迁到 Node2D：根节点没有 anchors，子节点挂在 Node2D 父链下
        // 也没法靠 FullRect 自动铺满。尺寸统一由 OnViewportResized() 显式设置。
        // 子节点本身仍是 Control，MouseFilter=Ignore 照常有效，不会抢上级按钮的点击。
        _image = new TextureRect();
        _image.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        _image.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
        _image.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_image);

        _tint = new ColorRect();
        _tint.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_tint);

        _vignette = new TextureRect();
        _vignette.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        _vignette.StretchMode = TextureRect.StretchModeEnum.Scale;
        _vignette.Texture = BuildVignetteTexture();
        _vignette.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_vignette);
    }

    /// <summary>径向渐变：中心透明、四角不透明。乘上主题暗角色即得暗角。</summary>
    static GradientTexture2D BuildVignetteTexture()
    {
        Gradient gradient = new Gradient();
        gradient.SetColor(0, new Color(1, 1, 1, 0));
        gradient.SetColor(1, new Color(1, 1, 1, 1));

        GradientTexture2D texture = new GradientTexture2D();
        texture.Gradient = gradient;
        texture.Fill = GradientTexture2D.FillEnum.Radial;
        texture.FillFrom = new Vector2(0.5f, 0.5f);
        texture.FillTo = new Vector2(1.0f, 1.0f);   // 半径到角落，四角最暗
        texture.Width = 256;
        texture.Height = 256;

        return texture;
    }

    static Texture2D LoadBackdrop(int themeId)
    {
        // 明暗由主题元数据声明，不写死 id —— 新增浅色主题不必回来改这里
        if (UIThemeManager.IsLightOf(themeId))
        {
            if (_bgInk == null)
            {
                _bgInk = ResourceLoader.Load<Texture2D>(BgInkPath, "");
            }
            return _bgInk;
        }

        if (_bgNight == null)
        {
            _bgNight = ResourceLoader.Load<Texture2D>(BgNightPath, "");
        }

        return _bgNight;
    }
}
