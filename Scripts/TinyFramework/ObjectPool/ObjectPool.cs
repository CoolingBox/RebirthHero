// 文件：ObjectPool.cs
// 作者：急冻雪柜
// 描述：通用对象池(复用 Node,避免运行时频繁 new/Free 造成的 GC 抖动)
// 日期：2026/09/23

using System;
using System.Collections.Generic;
using Godot;

namespace TinyFramework;

/// <summary>
/// 通用对象池。适用于敌人、子弹、特效等需要反复创建/销毁的 Node。
/// 取出时由 factory 创建,归还时从父节点 detach 并压回池内复用。
/// </summary>
/// <typeparam name="T">池化类型(须为 Node)</typeparam>
public class ObjectPool<T> where T : Node
{
    private readonly Stack<T> _pool = new Stack<T>();
    private readonly Func<T> _factory;

    public ObjectPool(Func<T> factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// 取出一个实例;池空则通过 factory 新建。
    /// </summary>
    public T Get()
    {
        return _pool.Count > 0 ? _pool.Pop() : _factory();
    }

    /// <summary>
    /// 归还实例(从场景树 detach,保留在池中复用)。
    /// </summary>
    public void Return(T item)
    {
        item.GetParent()?.RemoveChild(item);
        _pool.Push(item);
    }

    /// <summary>
    /// 释放池中全部实例(切换场景/退出时调用)。
    /// </summary>
    public void Clear()
    {
        foreach (T item in _pool)
            item.QueueFree();
        _pool.Clear();
    }
}
