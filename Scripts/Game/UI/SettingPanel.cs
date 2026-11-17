// 文件：SettingPanel.cs
// 《轮回大侠》设置面板（框架面板：BasePanel 子类，由 UIManager 开关）
//
// 结构分工（与 InnPanel 一致）：
//   · SettingPanel.tscn            —— 静态外壳：TBg / THeading / TCloseBtn / THint / TContent
//   · SettingPanel.cs              —— 本文件：全部逻辑
//   · Generate/SettingPanelBind.cs —— GenUICode 生成：_T* 字段 + OnBind 里 FindChild 绑定
//
// 打开：MainPanel 点「设置」→ SettingPanel.BindData(onClose) → UIManager.ShowPanel<SettingPanel>()
//
// 分区（自上而下）：
//   ① 外观 —— 8 套武侠主题（色块直接取主题资源）
//   ② 战斗 —— 倍速档位 / 自动保存 / 数字显示格式
//   ③ 声音 —— 总音量 / 背景音乐 / 音效
//   ④ 底部 —— 恢复默认 / 退出游戏（都要二次确认）
//
// 两条约定：
//   · 开关「点了就生效、就写盘」，没有确定/取消 —— 设置项没有可撤销的中间态，
//     多一个确认按钮只会让玩家多按一次。
//   · 跨系统的通知走 TinyEventBus（AutoSaveToggled / BattleSpeedChanged），
//     本面板不需要认识 MainPanel 与存档面板。

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

    /// <summary>主题变更后由 MainPanel 调用：面板正开着才重建。</summary>
    public static void ApplyThemeCurrent()
    {
        if (_current == null)
        {
            return;
        }

        // 面板被销毁后 _current 会指向已释放对象
        if (_current.GameObject == null || !GodotObject.IsInstanceValid(_current.GameObject))
        {
            return;
        }

        _current.ApplyTheme();
    }

    // ================= 布局常量 =================
    // 面板 1220×880，内容区 1172 宽（见 .tscn 的 TContent）。
    // 主题格子固定 4 列：4×264 + 3×16 = 1104，正好留一点右边距；再加主题只是多一行。
    const int SwatchBgWidth = 200;
    const int SwatchAccentWidth = 64;
    const int SwatchHeight = 32;
    const int ThemeItemWidth = SwatchBgWidth + SwatchAccentWidth;
    const int ThemeColumns = 4;

    Action _onClose;
    BackdropLayer _backdrop;
    bool _wired;

    // 二次确认态：恢复默认、退出游戏都不该"点一下就执行"
    bool _resetConfirm;
    bool _quitConfirm;

    Label _status;
    Button _btnReset;
    Button _btnQuit;

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
            if (_TCloseBtn != null)
            {
                _TCloseBtn.Pressed += OnClosePressed;
            }
            _wired = true;
        }

        Build();
    }

    public void ApplyTheme()
    {
        // 根节点的 Theme 也要换：本面板开着时切主题，按钮/滑杆/勾选框的样式
        // 都继承自根节点 —— 只重建内容的话，文字色变了而控件外框还是旧主题的。
        UIThemeManager.ApplyTo(GameObject);
        Build();
    }

    void OnClosePressed()
    {
        UiSfx.Close();
        HidePanel<SettingPanel>();
        if (_onClose != null)
        {
            _onClose();
        }
    }

    /// <summary>背景层只建一次；换主题时由 BackdropLayer 自己刷新，不重建节点。</summary>
    void EnsureBackdrop()
    {
        if (_TBg == null)
        {
            return;
        }

        if (_backdrop != null && GodotObject.IsInstanceValid(_backdrop))
        {
            return;
        }

        _backdrop = new BackdropLayer();
        _TBg.AddChild(_backdrop);
    }

    // ================= 绘制 =================

    void Build()
    {
        if (_TContent == null)
        {
            return;
        }

        ClearContent();

        _resetConfirm = false;
        _quitConfirm = false;
        _status = null;
        _btnReset = null;
        _btnQuit = null;

        if (_THeading != null)
        {
            _THeading.Text = "设置 · " + UIThemeManager.ThemeName;
        }

        if (_THint != null)
        {
            _THint.Text = "改动即时生效并写入设置；音量在松开滑杆时落盘。";
        }

        BuildThemeSection();
        BuildBattleSection();
        BuildVolumeSection();
        BuildFooter();
    }

    /// <summary>清空内容区。先 RemoveChild 再 QueueFree：否则本帧内新旧节点同时挂在
    /// 容器里，重建的瞬间会看到两套内容叠着排一遍（换主题时最明显）。</summary>
    void ClearContent()
    {
        Godot.Collections.Array<Node> old = _TContent.GetChildren();
        for (int i = 0; i < old.Count; i++)
        {
            _TContent.RemoveChild(old[i]);
            old[i].QueueFree();
        }
    }

    // ---------- ① 外观 ----------

    void BuildThemeSection()
    {
        AddSectionHeader("外观 · 武侠主题");

        GridContainer grid = new GridContainer();
        grid.Columns = ThemeColumns;
        grid.AddThemeConstantOverride("h_separation", 16);
        grid.AddThemeConstantOverride("v_separation", 14);

        for (int i = 0; i < UIThemeManager.Count; i++)
        {
            int themeId = i;
            bool current = i == UIThemeManager.Id;

            VBoxContainer item = new VBoxContainer();
            item.AddThemeConstantOverride("separation", 4);

            // 色块直接取该主题资源里的 bg_base / accent
            // —— 预览必须是"资源里真实的色"，不能在代码里另存一份
            HBoxContainer swatch = new HBoxContainer();
            swatch.AddThemeConstantOverride("separation", 0);

            ColorRect swatchBg = new ColorRect();
            swatchBg.CustomMinimumSize = new Vector2(SwatchBgWidth, SwatchHeight);
            swatchBg.Color = UIThemeManager.ColorOf(i, UIThemeKey.BgBase);
            swatch.AddChild(swatchBg);

            ColorRect swatchAccent = new ColorRect();
            swatchAccent.CustomMinimumSize = new Vector2(SwatchAccentWidth, SwatchHeight);
            swatchAccent.Color = UIThemeManager.ColorOf(i, UIThemeKey.Accent);
            swatch.AddChild(swatchAccent);

            item.AddChild(swatch);

            Button btn = new Button();
            btn.Text = current ? ("● " + UIThemeManager.NameOf(i)) : UIThemeManager.NameOf(i);
            btn.CustomMinimumSize = new Vector2(ThemeItemWidth, 36);
            if (current)
            {
                btn.AddThemeColorOverride("font_color", UIThemeManager.Accent);
            }
            btn.Pressed += () => OnPickTheme(themeId);
            item.AddChild(btn);

            grid.AddChild(item);
        }

        _TContent.AddChild(grid);

        // 每格下面各挂一行风味文字太占高（8 套 × 一行 ≈ 100px），
        // 改成只显示当前这套的说明 —— 换主题是即时预览，点一下就知道像不像。
        Label desc = new Label();
        desc.Text = "当前：" + UIThemeManager.ThemeName + " · " + UIThemeManager.ThemeDesc;
        desc.AddThemeFontSizeOverride("font_size", 20);
        desc.AddThemeColorOverride("font_color", UIThemeManager.TextMuted);
        _TContent.AddChild(desc);
    }

    void OnPickTheme(int themeId)
    {
        UiSfx.Select();

        // SetTheme 自己写盘 + 广播；本面板的重建由 MainPanel → ApplyThemeCurrent 走，
        // 这里不再 Build（否则同一次点击会白重建一遍）。
        UIThemeManager.SetTheme(themeId);
    }

    // ---------- ② 战斗 ----------

    void BuildBattleSection()
    {
        AddSectionHeader("战斗");

        SettingData data = SettingManager.Data;

        // ① 倍速：分段按钮，比下拉少两步（挂机游戏里这是高频操作）
        HBoxContainer speedRow = AddOptionRow("战斗倍速");
        double current = CurrentSpeed(data);
        for (int i = 0; i < SettingData.SpeedSteps.Length; i++)
        {
            double step = SettingData.SpeedSteps[i];
            bool on = Math.Abs(step - current) < 0.001;
            Button btn = SegmentButton("×" + step.ToString("0.#"), on, () => OnPickSpeed(step));
            btn.CustomMinimumSize = new Vector2(84, 38);
            speedRow.AddChild(btn);
        }
        speedRow.AddChild(HintLabel("只影响战斗推进速度；倒计时、自动保存与界面动效不受影响。"));

        // ② 自动保存
        HBoxContainer autoRow = AddOptionRow("自动保存");
        CheckButton chk = new CheckButton();
        chk.Text = "每 " + SaveBackup.AutoSaveMinutes + " 分钟存一次主档";
        chk.CustomMinimumSize = new Vector2(300, 38);
        chk.ButtonPressed = data != null && data.AutoSave;
        chk.Toggled += OnAutoSaveToggled;   // 先赋值后接信号：程序化赋值不该触发写盘
        autoRow.AddChild(chk);
        autoRow.AddChild(HintLabel("关掉后只能手动保存；退出游戏时仍会兜底存一次。"));

        // ③ 数字显示格式
        HBoxContainer numRow = AddOptionRow("数字显示");
        NumberFormat mode = data != null ? data.NumberFormat : NumberFormat.Chinese;

        Button btnCn = SegmentButton("中文 · 万/亿", mode == NumberFormat.Chinese,
            () => OnPickNumberFormat(NumberFormat.Chinese));
        btnCn.CustomMinimumSize = new Vector2(150, 38);
        numRow.AddChild(btnCn);

        Button btnSci = SegmentButton("科学 · K/M/B", mode == NumberFormat.Scientific,
            () => OnPickNumberFormat(NumberFormat.Scientific));
        btnSci.CustomMinimumSize = new Vector2(150, 38);
        numRow.AddChild(btnSci);

        numRow.AddChild(HintLabel("影响万以上的大数；关掉本面板后顶栏按新格式刷新。"));
    }

    /// <summary>读设置里的倍速；不在档位表里的值（旧档、手改）一律退回第一档。</summary>
    static double CurrentSpeed(SettingData data)
    {
        if (data == null)
        {
            return SettingData.SpeedSteps[0];
        }

        for (int i = 0; i < SettingData.SpeedSteps.Length; i++)
        {
            if (Math.Abs(SettingData.SpeedSteps[i] - data.BattleSpeed) < 0.001)
            {
                return SettingData.SpeedSteps[i];
            }
        }
        return SettingData.SpeedSteps[0];
    }

    void OnPickSpeed(double speed)
    {
        UiSfx.Select();

        SettingData data = SettingManager.Data;
        if (data != null)
        {
            data.BattleSpeed = (float)speed;
            SaveManager.Save(data);
        }

        // 导航条的「×N」与战斗会话靠这个事件对齐（本面板不认识 MainPanel）
        TinyEventBus.Global.Emit(new BattleSpeedChanged(speed));

        Build();
        SetStatus("战斗倍速 ×" + speed.ToString("0.#") + " 已生效。");
    }

    void OnAutoSaveToggled(bool on)
    {
        UiSfx.Toggle();

        // 写设置 + 落盘 + 广播都在 SaveBackup（存档面板用的是同一个方法）
        SaveBackup.ApplyAutoSave(on);

        SetStatus(on
            ? "自动保存已开启：每 " + SaveBackup.AutoSaveMinutes + " 分钟记一次主档。"
            : "自动保存已关闭：只在你手动保存或退出时记档。");
    }

    void OnPickNumberFormat(NumberFormat mode)
    {
        UiSfx.Select();

        // 立即生效 + 写盘，框架里已经有这个入口，不在这里重写一遍
        SettingManager.SetNumberFormat(mode);

        Build();
        SetStatus(mode == NumberFormat.Chinese
            ? "数字显示：中文计数（万 / 亿 / 兆……）。"
            : "数字显示：科学计数（K / M / B / T……）。");
    }

    // ---------- ③ 声音 ----------

    void BuildVolumeSection()
    {
        AddSectionHeader("声音");

        SettingData data = SettingManager.Data;
        if (data == null)
        {
            return;
        }

        AddVolumeRow("总音量", data.Volume, OnMasterVolume, false, "乘在另外两条之上，0 即静音。");
        AddVolumeRow("背景音乐", data.BGMVolume, OnBgmVolume, false, "只影响背景音乐。");
        AddVolumeRow("音效", data.SFXVolume, OnSfxVolume, true, "松开滑杆会试听一下。");
    }

    void AddVolumeRow(string label, float value, Action<float> apply, bool audition, string hint)
    {
        HBoxContainer row = AddOptionRow(label);

        HSlider slider = new HSlider();
        slider.MinValue = 0;
        slider.MaxValue = 100;
        slider.Step = 1;
        slider.Value = value;
        slider.CustomMinimumSize = new Vector2(560, 38);
        slider.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(slider);

        Label readout = new Label();
        readout.Text = ((int)value).ToString();
        readout.CustomMinimumSize = new Vector2(56, 38);
        readout.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(readout);

        row.AddChild(HintLabel(hint));

        slider.ValueChanged += (double v) =>
        {
            readout.Text = ((int)v).ToString();
            apply((float)v);          // 立即生效（听感反馈），但不写盘
        };

        // 拖动结束才写盘：每像素存一次盘既无意义又伤 IO
        slider.DragEnded += (bool valueChanged) =>
        {
            SaveSetting();

            if (audition)
            {
                // 音效音量靠耳朵判断，数字没意义；UI 音走 SFX_UI 总线，随滑杆一起缩放
                UiSfx.Click();
            }
        };
    }

    // ---------- ④ 底部 ----------

    void BuildFooter()
    {
        // 把按钮压到面板底边：内容天然留白由这个弹性空行吃掉，
        // 以后再加一两个设置行也不会把按钮顶出面板。
        Control spacer = new Control();
        spacer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _TContent.AddChild(spacer);

        HBoxContainer row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);

        _status = new Label();
        // 平时显示版本号：设置页是玩家最可能"想确认自己玩的是哪一版"的地方
        _status.Text = "v" + SaveBackup.Version;
        _status.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _status.VerticalAlignment = VerticalAlignment.Center;
        _status.ClipText = true;   // 提示再长也不许把按钮挤走
        _status.AddThemeFontSizeOverride("font_size", 19);
        _status.AddThemeColorOverride("font_color", UIThemeManager.TextMuted);
        row.AddChild(_status);

        _btnReset = FooterButton("恢复默认", OnResetPressed);
        row.AddChild(_btnReset);

        _btnQuit = FooterButton("退出游戏", OnQuitPressed);
        _btnQuit.Visible = !SaveBackup.IsMobile;   // 移动端退出交给系统返回键
        row.AddChild(_btnQuit);

        _TContent.AddChild(row);
    }

    void OnResetPressed()
    {
        UiSfx.Click();

        if (!_resetConfirm)
        {
            _resetConfirm = true;
            _quitConfirm = false;

            if (_btnReset != null)
            {
                _btnReset.Text = "确认恢复？";
            }
            if (_btnQuit != null)
            {
                _btnQuit.Text = "退出游戏";
            }

            SetStatus("恢复默认会改回：夜行主题、音量 100、1x 倍速、中文数字、自动保存开。再点一次执行。");
            return;
        }

        ApplyDefaults();
    }

    /// <summary>全部设置回到初始值。列表口径与 SettingData 的字段默认值保持一致。</summary>
    void ApplyDefaults()
    {
        SettingData data = SettingManager.Data;
        if (data == null)
        {
            return;
        }

        data.Volume = 100f;
        data.BGMVolume = 100f;
        data.SFXVolume = 100f;
        data.NumberFormat = NumberFormat.Chinese;
        data.AutoSave = true;
        data.BattleSpeed = SettingData.SpeedSteps[0];

        SaveManager.Save(data);

        // 立即套用：三条音量总线 + 大数显示模式
        AudioManager.SetVolume(data.Volume);
        AudioManager.SetBGMVolume(data.BGMVolume);
        AudioManager.SetSFXVolume(data.SFXVolume);
        BigNum.DisplayMode = data.NumberFormat;

        // 通知战斗侧与计时器（它们各自订阅，本面板不直接碰它们）
        TinyEventBus.Global.Emit(new AutoSaveToggled(true));
        TinyEventBus.Global.Emit(new BattleSpeedChanged(data.BattleSpeed));

        // 主题回到初始的夜行：会顺带写盘并广播，本面板由 ApplyThemeCurrent 重建
        UIThemeManager.SetTheme((int)UIThemeId.NightBlade);

        Build();   // 本来就在夜行时 SetTheme 不广播，这里兜底重建
        SetStatus("已恢复默认设置。");
        UiSfx.Confirm();
    }

    void OnQuitPressed()
    {
        UiSfx.Click();

        if (!_quitConfirm)
        {
            _quitConfirm = true;
            _resetConfirm = false;

            if (_btnQuit != null)
            {
                _btnQuit.Text = "确认退出？";
            }
            if (_btnReset != null)
            {
                _btnReset.Text = "恢复默认";
            }

            SetStatus("进度会在关闭时自动保存。再点一次「确认退出」。");
            return;
        }

        UiSfx.Close();

        // 存档不在这里抓：MainPanel 已经挂了 TreeExiting → SaveProgress，
        // 从这里再存一次只会多写一遍同一份档。
        GameObject.GetTree().Quit();
    }

    // ================= 小部件 =================

    void AddSectionHeader(string title)
    {
        VBoxContainer head = new VBoxContainer();
        head.AddThemeConstantOverride("separation", 6);

        Label label = new Label();
        label.Text = title;
        label.AddThemeFontSizeOverride("font_size", 23);
        label.AddThemeColorOverride("font_color", UIThemeManager.Accent);
        head.AddChild(label);

        // 一条细线：设置项一多，光靠间距分不出"这是新的一段"
        ColorRect line = new ColorRect();
        line.Color = UIThemeManager.Separator;
        line.CustomMinimumSize = new Vector2(0, 2);
        head.AddChild(line);

        _TContent.AddChild(head);
    }

    /// <summary>一行设置项：左列固定名字（对齐），右边由调用方往里塞控件。</summary>
    HBoxContainer AddOptionRow(string label)
    {
        HBoxContainer row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        row.CustomMinimumSize = new Vector2(0, 38);

        Label name = new Label();
        name.Text = label;
        name.CustomMinimumSize = new Vector2(132, 38);
        name.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(name);

        _TContent.AddChild(row);
        return row;
    }

    /// <summary>分段按钮：选中态在文字前加 ●，并用主题强调色 —— 不用另外画选中底。</summary>
    Button SegmentButton(string text, bool on, Action pressed)
    {
        Button btn = new Button();
        btn.Text = on ? ("● " + text) : text;
        btn.CustomMinimumSize = new Vector2(96, 38);
        if (on)
        {
            btn.AddThemeColorOverride("font_color", UIThemeManager.Accent);
        }
        btn.Pressed += pressed;
        return btn;
    }

    Label HintLabel(string text)
    {
        Label hint = new Label();
        hint.Text = text;
        hint.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        hint.AddThemeFontSizeOverride("font_size", 19);
        hint.AddThemeColorOverride("font_color", UIThemeManager.TextMuted);
        return hint;
    }

    Button FooterButton(string text, Action pressed)
    {
        Button btn = new Button();
        btn.Text = text;
        btn.CustomMinimumSize = new Vector2(150, 40);
        btn.Pressed += pressed;
        return btn;
    }

    void SetStatus(string text)
    {
        if (_status != null)
        {
            _status.Text = text;
        }
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
        {
            SaveManager.Save(data);
        }
    }
}
