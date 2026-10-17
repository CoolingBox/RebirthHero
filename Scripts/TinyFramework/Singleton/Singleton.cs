// 文件：Singleton.cs
// 作者：急冻雪柜
// 描述：单例基类
// 日期：2025/11/15 1:46

using Godot;

namespace TinyFramework;

/// <summary>
/// 普通单例
/// </summary>
public class Singleton<T> where T : new()
{
    private static T _instance;

    public static T Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = new T();
            }

            return _instance;
        }
    }

    /// <summary>
    /// 清空静态实例。此为纯 C# 单例,不持有 GodotObject,不影响 78513;用于需要重新初始化的场景。
    /// </summary>
    public static void Reset()
    {
        _instance = default;
    }

    public virtual void Init()
    {
        GD.PrintRich($"[color=Cyan]{this.GetType().Name} 启动[/color]");
    }

    public virtual void UnInit()
    {
        GD.PrintRich($"[color=Cyan]{this.GetType().Name} 关闭[/color]");
    }
}

/// <summary>
/// 单例节点
/// </summary>
public partial class SingletonNode<T> : Node where T :Node, new()
{
    public static T Instance;

    public override void _EnterTree()
    {
        base._EnterTree();
        Instance = this as T;
        // 运行时进入场景树即自动初始化,调用方只需 Create() 即可,无需再手动 Init()
        if (!Engine.IsEditorHint())
            Init();
    }

    public override void _ExitTree()
    {
        UnInit();
        // 释放静态引用,避免在编辑器热重载程序集时旧 AssemblyLoadContext 因被静态字段持有而无法卸载(issue 78513)
        Instance = null;
        base._ExitTree();
    }

    // 热重载程序集时,单例节点不会退出树(_ExitTree 不会触发),但 Godot 会 Dispose 旧脚本实例。
    // 在此清空静态引用,确保旧 AssemblyLoadContext 能够卸载(issue 78513)。
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Instance = null;
        }
        base.Dispose(disposing);
    }

    public virtual void Init()
    {
        GD.PrintRich($"[color=Cyan]{this.GetType().Name} 启动[/color]");
    }

    public virtual void UnInit()
    {
        GD.PrintRich($"[color=Cyan]{this.GetType().Name} 关闭[/color]");
    }

    /// <summary>
    /// 创建单例节点,并添加到GameRoot节点
    /// </summary>
    public static T Create()
    {
        T instance = new T();
        instance.Name = typeof(T).Name;
        GameRoot.Instance.AddChild(instance);
        return instance;
    }
}