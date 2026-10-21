// 文件：SettingPanel.cs
// 《轮回大侠》设置面板（框架面板：BasePanel 子类，由 UIManager 开关）
//
// 结构分工（与 InnPanel 一致）：
//   · SettingPanel.tscn            —— 静态外壳：TBg / THeading / TCloseBtn / THint / TContent
//   · SettingPanel.cs              —— 本文件：全部逻辑
//   · Generate/SettingPanelBind.cs —— GenUICode 生成：_T* 字段 + OnBind 里 FindChild 绑定
//
// 打开：BattlePanel 点「设置」→ SettingPanel.BindData(onClose) → UIManager.ShowPanel<SettingPanel>()
// 内容：4 套主题（色块取自各主题资源）+ 主音量 / BGM / SFX

using System;
using Godot;
using TinyFramework;

public partial class SettingPanel : BasePanel
{
    // UIManager.ShowPanel<T>() 不带参，依赖用静态上下文在打开前注入
    static Action _ctxOnClose;

    // 主题切换后本面板要重建（按钮高亮、色块、文字色都是写进节点的）
    static SettingPanel _current;

    public static void BindData(Action onClose)
    {
        _ctxOnClose = onClose;
    }

    /// <summary>主题变更后由 BattlePanel 调用：面板正开着才重建。</summary>
    public static void ApplyThemeCurrent()
    {
        if (_current == null)
            return;

        // 面板被销毁后 _current 会指向已释放对象
        if (_current.GameObject == null || !GodotObject.IsInstanceValid(_current.GameObject))
            return;

        _current.ApplyTheme();
    }

    Action _onClose;
    BackdropLayer _backdrop;
    bool _wired;

    public override void OnShow()
    {
        base.OnShow();

        // 注意：不要 override OnBind —— 生成器已在 Bind 文件里实现它来绑定 _T* 字段，
        // 重复定义会编译失败（CS0111）。
        _current = this;

        // .tscn 里挂的是默认主题（供编辑器预览）；运行时必须换成玩家当前选的那套
        UIThemeManager.ApplyTo(GameObject);

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
        HidePanel<SettingPanel>();
        if (_onClose != null) _onClose();
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

    // ================= 绘制 =================

    void Build()
    {
        if (_TContent == null) return;

        foreach (Node c in _TContent.GetChildren()) c.QueueFree();

        if (_THeading != null) _THeading.Text = "设置 · " + UIThemeManager.ThemeName;
        if (_THint != null)
            _THint.Text = "主题即时生效并记入存档；音量拖动结束后自动保存。";

        BuildThemeSection();
        BuildVolumeSection();
    }

    void BuildThemeSection()
    {
        _TContent.AddChild(SectionLabel("外观 · 武侠主题"));

        HBoxContainer row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);

        for (int i = 0; i < UIThemeManager.Count; i++)
        {
            int themeId = i;
            bool current = i == UIThemeManager.Id;

            // 色块直接取该主题资源里的 bg_base / accent
            // —— 预览必须是"资源里真实的色"，不能在代码里另存一份
            Color bg = UIThemeManager.ColorOf(i, UIThemeKey.BgBase);
            Color accent = UIThemeManager.ColorOf(i, UIThemeKey.Accent);

            VBoxContainer col = new VBoxContainer();
            col.AddThemeConstantOverride("separation", 4);

            HBoxContainer swatchRow = new HBoxContainer();
            swatchRow.AddThemeConstantOverride("separation", 0);

            ColorRect swatchBg = new ColorRect();
            swatchBg.CustomMinimumSize = new Vector2(126, 30);
            swatchBg.Color = bg;
            swatchRow.AddChild(swatchBg);

            ColorRect swatchAccent = new ColorRect();
            swatchAccent.CustomMinimumSize = new Vector2(44, 30);
            swatchAccent.Color = accent;
            swatchRow.AddChild(swatchAccent);

            col.AddChild(swatchRow);

            Button btn = new Button();
            btn.Text = current ? ("● " + UIThemeManager.NameOf(i)) : UIThemeManager.NameOf(i);
            btn.CustomMinimumSize = new Vector2(170, 36);
            if (current) btn.AddThemeColorOverride("font_color", UIThemeManager.Accent);
            btn.Pressed += () =>
            {
                UIThemeManager.SetTheme(themeId);
                Build();
            };
            col.AddChild(btn);

            Label desc = new Label();
            desc.Text = UIThemeManager.DescOf(i);
            desc.AddThemeFontSizeOverride("font_size", 20);
            desc.AddThemeColorOverride("font_color", UIThemeManager.TextMuted);
            col.AddChild(desc);

            row.AddChild(col);
        }

        _TContent.AddChild(row);
    }

    void BuildVolumeSection()
    {
        _TContent.AddChild(SectionLabel("声音"));

        SettingData data = SettingManager.Data;
        if (data == null) return;

        AddVolumeRow("总音量", data.Volume, OnMasterVolume);
        AddVolumeRow("背景音乐", data.BGMVolume, OnBgmVolume);
        AddVolumeRow("音效", data.SFXVolume, OnSfxVolume);
    }

    void AddVolumeRow(string label, float value, Action<float> apply)
    {
        HBoxContainer row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);

        Label name = new Label();
        name.Text = label;
        name.CustomMinimumSize = new Vector2(120, 32);
        row.AddChild(name);

        HSlider slider = new HSlider();
        slider.MinValue = 0;
        slider.MaxValue = 100;
        slider.Step = 1;
        slider.Value = value;
        slider.CustomMinimumSize = new Vector2(420, 32);
        row.AddChild(slider);

        Label readout = new Label();
        readout.Text = ((int)value).ToString();
        readout.CustomMinimumSize = new Vector2(56, 32);
        row.AddChild(readout);

        slider.ValueChanged += (double v) =>
        {
            readout.Text = ((int)v).ToString();
            apply((float)v);          // 立即生效（听感反馈），但不写盘
        };

        // 拖动结束才写盘：每像素存一次盘既无意义又伤 IO
        slider.DragEnded += (bool valueChanged) => SaveSetting();

        _TContent.AddChild(row);
    }

    Label SectionLabel(string text)
    {
        Label label = new Label();
        label.Text = text;
        label.AddThemeFontSizeOverride("font_size", 23);
        label.AddThemeColorOverride("font_color", UIThemeManager.Accent);
        return label;
    }

    // ================= 音量写入 =================

    void OnMasterVolume(float v)
    {
        SettingManager.Data.Volume = v;
        AudioManager.SetVolume(v);
    }

    void OnBgmVolume(float v)
    {
        SettingManager.Data.BGMVolume = v;
        AudioManager.SetBGMVolume(v);
    }

    void OnSfxVolume(float v)
    {
        SettingManager.Data.SFXVolume = v;
        AudioManager.SetSFXVolume(v);
    }

    void SaveSetting()
    {
        SettingData data = SettingManager.Data;
        if (data != null)
            SaveManager.Save(data);
    }
}
