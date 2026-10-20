// 文件：SavePanel.cs
// 《轮回大侠》存档面板（框架面板：BasePanel 子类，由 UIManager 开关）
//
// 结构分工（与 SettingPanel / InnPanel 一致）：
//   · SavePanel.tscn            —— 静态外壳：TBg / THeading / TCloseBtn / THint / TStatus / TContent（含各行）
//   · SavePanel.cs              —— 本文件：全部逻辑
//   · Generate/SavePanelBind.cs —— GenUICode 生成：_T* 字段 + OnBind 里 FindChild 绑定
//
// 四件事（对应需求 1~4）：
//   ① 保存：立即把当前进度写进主档；可开关"每 10 分钟自动保存"（开关存在 SettingData）
//   ② 备份：默认名 = 版本号_日期，可改；桌面版弹出文件对话框选路径（同时留一份进载入列表），
//            移动端沙盒不能浏览路径 → 只改名、存进游戏自己的存档夹
//   ③ 载入：列出主档与全部备份，点一条即覆盖当前进度并重开一场；可删除备份（主档不可删）
//   ④ 启动默认载入主档：由 BattlePanel.LoadProgress 负责（本面板只是让玩家能换档）
//
// 边界：文件读写/序列化走 TinyFramework 的 SaveManager；本面板不碰 JSON、不拼路径。

using System;
using System.Collections.Generic;
using Godot;
using TinyFramework;

public partial class SavePanel : BasePanel
{
    // UIManager.ShowPanel<T>() 不带参，依赖用静态上下文在打开前注入
    static Func<GameSave> _ctxCapture;        // 抓当前进度（给"保存/备份/导出"用）
    static Func<GameSave, bool> _ctxApply;    // 灌回当前进度（给"载入"用），返回 false = 版本不兼容
    static Action _ctxAutoSaveChanged;        // 自动保存开关变了 → 让 BattlePanel 启停计时器
    static Action _ctxOnClose;

    static SavePanel _current;

    public static void BindData(Func<GameSave> capture, Func<GameSave, bool> apply,
                                Action autoSaveChanged, Action onClose)
    {
        _ctxCapture = capture;
        _ctxApply = apply;
        _ctxAutoSaveChanged = autoSaveChanged;
        _ctxOnClose = onClose;
    }

    /// <summary>主题变更后由 BattlePanel 调用：面板正开着才刷新。</summary>
    public static void ApplyThemeCurrent()
    {
        if (_current == null)
            return;

        if (_current.GameObject == null || !GodotObject.IsInstanceValid(_current.GameObject))
            return;

        _current.RefreshList();
    }

    public override void OnClose()
    {
        // ClosePanel 会 QueueFree 根节点；静态引用必须清掉，否则静态入口会摸到已释放对象
        if (_current == this) _current = null;
        base.OnClose();
    }

    bool _wired;
    FileDialog _dialog;        // 桌面版导出/导入用（懒创建）
    bool _dialogIsExport;      // true=导出 / false=导入

    public override void OnShow()
    {
        base.OnShow();

        // 注意：不要 override OnBind —— 生成器已在 Bind 文件里实现它来绑定 _T* 字段，
        // 重复定义会编译失败（CS0111）。
        _current = this;

        // .tscn 里挂的是默认主题（供编辑器预览）；运行时换成玩家当前选的那套
        UIThemeManager.ApplyTo(GameObject);

        if (!_wired)
        {
            if (_TCloseBtn != null) _TCloseBtn.Pressed += OnClosePressed;
            if (_TBtnSaveNow != null) _TBtnSaveNow.Pressed += OnSaveNow;
            if (_TBtnBackup != null) _TBtnBackup.Pressed += OnBackupPressed;
            if (_TBtnImport != null) _TBtnImport.Pressed += OnImportPressed;
            if (_TChkAuto != null) _TChkAuto.Toggled += OnAutoSaveToggled;
            _wired = true;
        }

        bool mobile = SaveBackup.IsMobile;
        if (_TBtnImport != null) _TBtnImport.Visible = !mobile;   // 移动端没有可浏览的路径

        if (_THeading != null) _THeading.Text = "存档 · v" + SaveBackup.Version;

        // SetPressedNoSignal：程序化赋值不该触发 Toggled（否则一打开面板就写一次设置）
        if (_TChkAuto != null) _TChkAuto.SetPressedNoSignal(SettingManager.Data.AutoSave);

        // 默认名按"版本号 + 日期"预填，玩家想改就改
        if (_TNameEdit != null) _TNameEdit.Text = SaveBackup.DefaultName();

        RefreshList();
        SetStatus(mobile
            ? "移动端：备份保存在游戏自己的存档夹里，可改名字，但不能选外部路径。"
            : "桌面版：「备份」会弹出文件对话框选路径（默认名 = 版本号_日期，可改），同时存一份到载入列表。");
    }

    public void ApplyTheme()
    {
        RefreshList();
    }

    void OnClosePressed()
    {
        AudioManager.PlayUISFX("ui_close");
        HidePanel<SavePanel>();
        if (_ctxOnClose != null) _ctxOnClose();
    }

    // ================= ① 保存 =================

    void OnSaveNow()
    {
        GameSave data = Capture();
        if (data == null)
        {
            SetStatus("保存失败：进度还没准备好。");
            return;
        }

        SaveManager.Save(data);   // 主档 = user://GameSave（框架默认文件名 = 类型名）
        AudioManager.PlayEventSFX("ui_confirm");
        SetStatus("已保存主存档 · " + DateTime.Now.ToString("HH:mm:ss"));
        RefreshList();
    }

    void OnAutoSaveToggled(bool pressed)
    {
        SettingData settings = SettingManager.Data;
        settings.AutoSave = pressed;
        SaveManager.Save(settings);

        if (_ctxAutoSaveChanged != null) _ctxAutoSaveChanged();
        SetStatus(pressed
            ? "已开启：每 " + SaveBackup.AutoSaveMinutes + " 分钟自动保存主存档。"
            : "已关闭自动保存：记得手动保存或退出（退出时仍会兜底存一次）。");
    }

    // ================= ② 备份 =================

    void OnBackupPressed()
    {
        GameSave data = Capture();
        if (data == null)
        {
            SetStatus("备份失败：进度还没准备好。");
            return;
        }

        string name = _TNameEdit != null ? _TNameEdit.Text : "";

        // 移动端：沙盒里没有可浏览的路径 → 只改名，存进游戏自己的存档夹
        if (SaveBackup.IsMobile)
        {
            SaveEntry entry = SaveBackup.CreateBackup(data, name);
            if (entry == null)
            {
                SetStatus("备份失败：写文件出错，详见日志。");
                return;
            }

            AudioManager.PlayEventSFX("ui_confirm");
            SetStatus("已备份：" + entry.DisplayName);
            if (_TNameEdit != null) _TNameEdit.Text = SaveBackup.DefaultName();
            RefreshList();
            return;
        }

        // 桌面端：弹出文件对话框选路径（默认名 = 版本号_日期，可改）
        FileDialog dialog = EnsureDialog();
        if (dialog == null) return;

        _dialogIsExport = true;
        dialog.FileMode = FileDialog.FileModeEnum.SaveFile;
        dialog.CurrentFile = SaveBackup.SafeName(name) + ".json";
        dialog.PopupCentered(new Vector2I(980, 640));
    }

    void OnImportPressed()
    {
        if (SaveBackup.IsMobile) return;

        FileDialog dialog = EnsureDialog();
        if (dialog == null) return;

        _dialogIsExport = false;
        dialog.FileMode = FileDialog.FileModeEnum.OpenFile;
        dialog.PopupCentered(new Vector2I(980, 640));
    }

    FileDialog EnsureDialog()
    {
        if (_dialog != null && GodotObject.IsInstanceValid(_dialog)) return _dialog;
        if (GameObject == null) return null;

        _dialog = new FileDialog();
        _dialog.Access = FileDialog.AccessEnum.Filesystem;
        _dialog.UseNativeDialog = true;
        _dialog.Filters = new string[] { "*.json ; 存档文件" };
        _dialog.FileSelected += OnDialogFileSelected;
        GameObject.AddChild(_dialog);
        return _dialog;
    }

    void OnDialogFileSelected(string path)
    {
        if (_dialogIsExport)
        {
            GameSave data = Capture();
            if (data == null) { SetStatus("备份失败：进度还没准备好。"); return; }

            bool ok = SaveBackup.ExportTo(data, path);
            if (!ok)
            {
                SetStatus("备份失败：" + path + "（详见日志）");
                return;
            }

            // 同时留一份在存档夹：否则玩家选了路径，载入列表里却找不到这份备份
            string imported = SaveBackup.ImportFrom(path);
            AudioManager.PlayEventSFX("ui_confirm");
            if (imported == null)
                SetStatus("已备份到：" + path + "（存档夹复制失败，可用「导入存档…」找回）");
            else
                SetStatus("已备份到：" + path + "，并已加入下方载入列表。");

            RefreshList();
            return;
        }

        string importedName = SaveBackup.ImportFrom(path);
        if (importedName == null)
        {
            SetStatus("导入失败：不是有效的存档文件。");
            return;
        }

        SetStatus("已导入：可在下方列表里点「载入」。");
        AudioManager.PlayEventSFX("ui_confirm");
        RefreshList();
    }

    // ================= ③ 载入 =================

    void OnLoadEntry(SaveEntry entry)
    {
        if (entry == null || _ctxApply == null) return;

        GameSave data = SaveBackup.LoadInternal(entry.InternalName);
        if (data == null)
        {
            SetStatus("载入失败：" + entry.DisplayName + " 读不出来（可能是坏档）。");
            return;
        }

        bool ok = _ctxApply(data);
        if (!ok)
        {
            SetStatus("载入失败：这份存档与本版本不兼容（版本 " + data.Version + "）。");
            return;
        }

        AudioManager.PlayEventSFX("ui_confirm");
        HidePanel<SavePanel>();
        if (_ctxOnClose != null) _ctxOnClose();
    }

    void OnDeleteEntry(SaveEntry entry)
    {
        if (entry == null) return;

        bool ok = SaveBackup.Delete(entry.InternalName);
        SetStatus(ok ? ("已删除备份：" + entry.DisplayName) : "删除失败（主存档不可删除）。");
        RefreshList();
    }

    // ================= 列表 =================

    void RefreshList()
    {
        if (_TList == null) return;

        foreach (Node child in _TList.GetChildren()) child.QueueFree();

        List<SaveEntry> entries = SaveBackup.ListAll();
        if (entries.Count == 0)
        {
            Label empty = new Label();
            empty.Text = "（还没有存档）";
            empty.AddThemeFontSizeOverride("font_size", 17);
            empty.AddThemeColorOverride("font_color", UIThemeManager.TextMuted);
            _TList.AddChild(empty);
            return;
        }

        for (int i = 0; i < entries.Count; i++)
            _TList.AddChild(BuildRow(entries[i]));
    }

    Control BuildRow(SaveEntry entry)
    {
        HBoxContainer row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);

        Label name = new Label();
        name.Text = entry.DisplayName;
        name.CustomMinimumSize = new Vector2(340, 38);
        name.AddThemeFontSizeOverride("font_size", 18);
        if (entry.IsMain) name.AddThemeColorOverride("font_color", UIThemeManager.Accent);
        row.AddChild(name);

        Label meta = new Label();
        meta.Text = "第 " + entry.Layer + " 层（最高 " + entry.BestLayer + "） · 银两 "
                    + BigNum.From(entry.Silver).ToString(2) + " · " + entry.TimeText;
        meta.CustomMinimumSize = new Vector2(520, 38);
        meta.AddThemeFontSizeOverride("font_size", 16);
        meta.AddThemeColorOverride("font_color", UIThemeManager.TextMuted);
        row.AddChild(meta);

        Button loadBtn = new Button();
        loadBtn.Text = "载入";
        loadBtn.CustomMinimumSize = new Vector2(110, 38);
        loadBtn.Pressed += () => OnLoadEntry(entry);
        row.AddChild(loadBtn);

        if (!entry.IsMain)
        {
            Button delBtn = new Button();
            delBtn.Text = "删除";
            delBtn.CustomMinimumSize = new Vector2(100, 38);
            delBtn.Pressed += () => OnDeleteEntry(entry);
            row.AddChild(delBtn);
        }

        return row;
    }

    // ================= 内部 =================

    GameSave Capture()
    {
        if (_ctxCapture == null) return null;
        return _ctxCapture();
    }

    void SetStatus(string text)
    {
        if (_TStatus != null) _TStatus.Text = text;
    }
}
