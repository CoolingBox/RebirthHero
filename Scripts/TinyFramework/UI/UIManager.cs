// 文件：UIManager.cs
// 作者：急冻雪柜
// 描述：
// 日期：2025/11/15 1:56

using System.Collections.Generic;
using System.IO;
using Godot;

namespace TinyFramework;

public partial class UIManager : SingletonNode<UIManager>
{
    private readonly Dictionary<string, BasePanel> _panelDict = new Dictionary<string, BasePanel>();
    

    public override void Init()
    {
        base.Init();
        _panelDict.Clear();
    }

    public static void ShowPanel<T>(string panelName = null) where T : BasePanel, new()
    {
        if (panelName == null)
        {
            // 如果没有指定面板名称，则使用类名称
            panelName = typeof(T).Name;
        }

        if (Instance._panelDict.TryGetValue(panelName, out BasePanel panel))
        {
            // 已经存在，直接显示
            panel.OnShow();
            return;
        }

        string path = Path.Combine(PathDefine.UIPath, typeof(T).Name + ".tscn");


        PackedScene scene = ResManager.Load<PackedScene>(path);
        if (scene == null)
        {
            GD.PrintErr($"面板文件加载失败: {path}");
            return;
        }

        Control gameObject = scene.Instantiate<Control>();
        panel = new T();

        panel.OnBind(gameObject);
        // 添加到字典中并显示
        Instance._panelDict.Add(panelName, panel);
        Instance.AddChild(panel.GameObject);
        panel?.OnShow();
    }

    public static void HidePanel<T>(string panelName = null) where T : BasePanel
    {
        if (panelName == null)
        {
            // 如果没有指定面板名称，则使用类名称
            panelName = typeof(T).Name;
        }

        if (Instance._panelDict.TryGetValue(panelName, out BasePanel panel))
        {
            panel.OnHide();
        }
    }

    public static void ClosePanel<T>(string panelName = null) where T : BasePanel
    {
        if (panelName == null)
        {
            // 如果没有指定面板名称，则使用类名称
            panelName = typeof(T).Name;
        }

        if (Instance._panelDict.TryGetValue(panelName, out BasePanel panel))
        {
            panel.OnHide();
            panel.OnClose();
            Instance._panelDict.Remove(panelName);
            panel.GameObject.QueueFree();
        }
    }

}