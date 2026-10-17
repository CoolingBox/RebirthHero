#if TOOLS
using System;
using Godot;

/// <summary>
/// TinyTools 工具集入口(EditorPlugin)。在右侧 Dock 提供一个 EditorDock 面板,内含「UI代码生成」与「配置生成」两个按钮,
/// 分别调用 UICodeGenerator.Generate / ConfigGenerator.Generate。
/// </summary>
[Tool]
public partial class TinyToolsPlugin : EditorPlugin
{
    // 关键:用 WeakReference 持有 GodotObject,避免两类热重载问题:
    // 1) Godot 的 ScriptSerializationGenerator 会序列化实例字段中的 GodotObject 引用,热重载时对已释放对象 GetPtr 抛 ObjectDisposedException;
    //    WeakReference 既非 GodotObject 也非 [Export],不会被序列化。
    // 2) 若用 static 字段持有 GodotObject,会让旧 AssemblyLoadContext 无法卸载(issue 78513),导致插件在改代码后失效、必须重启编辑器;
    //    WeakReference 不根引用旧程序集,旧插件实例被释放后即可正常卸载。
    private WeakReference<EditorDock> _dockRef;
    private WeakReference<Button> _uiRef;
    private WeakReference<Button> _configRef;
    private bool _cleaned;

    public override void _EnterTree()
    {
        _cleaned = false;
        var dock = new EditorDock();
        dock.Name = "TinyToolsDock";
        dock.Title = "TinyTools";
        dock.DefaultSlot = EditorDock.DockSlot.RightBl;

        var panel = new VBoxContainer();
        panel.AddChild(new Label { Text = "TinyTools 工具集" });

        var uiButton = new Button { Text = "UI代码生成" };
        var configButton = new Button { Text = "配置生成" };

        panel.AddChild(uiButton);
        panel.AddChild(configButton);

        dock.AddChild(panel);
        AddDock(dock); 

        uiButton.Pressed += UICodeGenerator.Generate;
        configButton.Pressed += ConfigGenerator.Generate;

        _dockRef = new WeakReference<EditorDock>(dock);
        _uiRef = new WeakReference<Button>(uiButton);
        _configRef = new WeakReference<Button>(configButton);
    }

    public override void _ExitTree() => Cleanup();

    // 热重载时 Godot 会 Dispose 旧脚本实例。此处断开信号并移除 Dock,确保旧程序集能被卸载。
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Cleanup();
        base.Dispose(disposing);
    }

    private void Cleanup()
    {
        if (_cleaned)
            return;
        _cleaned = true;

        Disconnect(_uiRef, UICodeGenerator.Generate);
        Disconnect(_configRef, ConfigGenerator.Generate);

        if (_dockRef != null && _dockRef.TryGetTarget(out var dock) && GodotObject.IsInstanceValid(dock))
        {
            try { RemoveDock(dock); }
            catch (ObjectDisposedException) { }
            // 必须用 Free() 而非 QueueFree():QueueFree 是延迟释放,要等下一帧才真正销毁原生节点,
            // 而 Godot 的 AssemblyLoadContext 卸载发生在同一轮重载内,延迟释放来不及执行,
            // Dock 的原生节点及其子按钮的 GCHandle / Pressed 信号仍指向旧程序集,导致旧 ALC 无法卸载(issue 78513)。
            // Free() 立即销毁 Dock 整棵子树,在卸载前同步释放所有 GCHandle 与信号连接。
            try { dock.Free(); }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }
        _dockRef = null;
        _uiRef = null;
        _configRef = null;
    }

    // 仅在原生对象仍然存活时断开信号,否则跳过(已释放对象的 Disconnect 会抛 ObjectDisposedException)。
    private static void Disconnect(WeakReference<Button> refBtn, Action handler)
    {
        if (refBtn != null && refBtn.TryGetTarget(out var btn) && GodotObject.IsInstanceValid(btn))
        {
            try { btn.Pressed -= handler; }
            catch (ObjectDisposedException) { }
        }
    }
}
#endif
