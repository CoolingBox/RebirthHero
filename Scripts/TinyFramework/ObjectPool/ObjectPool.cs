// 文件：ObjectPool.cs
// 作者：急冻雪柜
// 描述：通用对象池(复用 Node,避免运行时频繁 new/Free 造成的 GC 抖动)
// 日期：2026/09/23
//
// 2026-11-11 补强（对照 GameFramework 的 ObjectPool / STS2 的 NodePool）：
//   · `IPoolable` 生命周期回调 —— 复用的对象常常要"重置状态"，否则上一轮的血量、
//     缩放、粒子进度会残留到下一次取出（这类 bug 表现为"新特效第一帧是旧的"）
//   · `Prewarm` 预热 —— 战斗开局集中实例化比战斗中零散实例化更稳（首波不掉帧）
//   · `MaxSize` 上限 —— 池不是越大越好：特效洪峰过去后，池里躺着几百个节点就是纯占内存
// 旧 API（`new ObjectPool<T>(factory)` / `Get` / `Return` / `Clear`）签名与语义都没变。

using System;
using System.Collections.Generic;
using Godot;

namespace TinyFramework;

/// <summary>
/// 可池化节点的生命周期钩子。实现它的类型会在这两个时刻收到回调：
/// `Get`（即将被使用）与 `Return`（刚被压回池）。
/// </summary>
public interface IPoolable
{
    /// <summary>从池中取出后调用 —— 在这里重置血量、计时、可见性等"上一轮残留"。</summary>
    void OnGetFromPool();

    /// <summary>压回池前调用 —— 在这里停掉动画、断开信号，别让池中的对象继续吃 CPU。</summary>
    void OnReturnToPool();
}

/// <summary>
/// 通用对象池。适用于敌人、子弹、特效、飘字等需要反复创建/销毁的 Node。
/// 取出时由 factory 创建，归还时从父节点 detach 并压回池内复用。
/// </summary>
/// <typeparam name="T">池化类型(须为 Node)</typeparam>
public class ObjectPool<T> where T : Node
{
    private readonly Stack<T> _pool = new Stack<T>();
    private readonly Func<T> _factory;

    /// <summary>池内保留上限。超出后归还会直接 QueueFree 而不是入池（默认不限）。</summary>
    public int MaxSize = int.MaxValue;

    public ObjectPool(Func<T> factory)
    {
        _factory = factory;
    }

    /// <summary>池内当前可用实例数（调试 / 性能面板用）。</summary>
    public int FreeCount
    {
        get { return _pool.Count; }
    }

    /// <summary>
    /// 预热：提前实例化 <paramref name="count"/> 个放进池里。
    /// 建议在战斗/关卡开始前调用 —— 把实例化开销压到加载期，而不是第一波敌人出现时。
    /// </summary>
    public void Prewarm(int count)
    {
        if (count <= 0)
        {
            return;
        }

        for (int i = 0; i < count; i++)
        {
            if (_pool.Count >= MaxSize)
            {
                break;
            }
            _pool.Push(_factory());
        }
    }

    /// <summary>取出一个实例；池空则通过 factory 新建。会触发 <see cref="IPoolable.OnGetFromPool"/>。</summary>
    public T Get()
    {
        T item;
        if (_pool.Count > 0)
        {
            item = _pool.Pop();
        }
        else
        {
            item = _factory();
        }

        IPoolable poolable = item as IPoolable;
        if (poolable != null)
        {
            poolable.OnGetFromPool();
        }
        return item;
    }

    /// <summary>
    /// 归还实例（从场景树 detach，保留在池中复用）。
    /// 超过 <see cref="MaxSize"/> 时直接销毁。会触发 <see cref="IPoolable.OnReturnToPool"/>。
    /// </summary>
    public void Return(T item)
    {
        if (item == null || !GodotObject.IsInstanceValid(item))
        {
            return;
        }

        IPoolable poolable = item as IPoolable;
        if (poolable != null)
        {
            poolable.OnReturnToPool();
        }

        Node parent = item.GetParent();
        if (parent != null)
        {
            parent.RemoveChild(item);
        }

        if (_pool.Count >= MaxSize)
        {
            item.QueueFree();
            return;
        }
        _pool.Push(item);
    }

    /// <summary>释放池中全部实例（切换场景/退出时调用）。</summary>
    public void Clear()
    {
        foreach (T item in _pool)
        {
            if (GodotObject.IsInstanceValid(item))
            {
                item.QueueFree();
            }
        }
        _pool.Clear();
    }
}
