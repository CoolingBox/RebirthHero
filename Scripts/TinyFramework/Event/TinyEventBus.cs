// 文件：TinyEventBus.cs
// 作者：急冻雪柜
// 描述：**类型安全事件总线** —— 跨系统的"广播/订阅"，替代互相持有引用与静态回调
// 日期：2026/11/11
//
// 为什么要它（2026-11-11 对照 GameFramework 的 EventManager、QFramework 的 TypeEventSystem）：
//   项目里跨系统通信有两条老路：
//     ① A 直接持有 B 的引用（面板 ↔ 面板），后面每加一个监听者就要改一次构造函数；
//     ② 静态 Action 字段（`public static event Action OnXxx`），谁都能清、无法追溯。
//   本类是第三条：以**事件类型**为键，订阅方与派发方互不认识。
//
// 与 `BattleEventBus` 的分工（重要，别混用）：
//   · `BattleEventBus` 是**战斗引擎内部**的事件流（伤害/治疗/护盾/死亡），引擎层与表现层经它解耦；
//     它是高频、强类型、带业务参数的，且必须能被探针驱动。
//   · `TinyEventBus` 是**系统之间**的通知（"存档已写入""主题已切换""语言已换"），低频、无业务耦合。
//   一个事件如果"只有战斗内的东西关心"，放 BattleEventBus；"跨模块关心"，放这里。
//
// 用法：
//   TinyEventBus.Global.On<SaveWritten>(e => Refresh());
//   TinyEventBus.Global.Emit(new SaveWritten { Name = "GameSave" });
//   TinyEventBus.Global.Off<SaveWritten>(handler);   // 面板关闭时退订，避免持有已释放的面板

using System;
using System.Collections.Generic;

namespace TinyFramework;

/// <summary>
/// 类型安全事件总线。以事件类型为键，**同类型可以注册多个订阅者**（按注册顺序调用）。
/// 事件建议用 <c>readonly struct</c>，`Emit` 时零装箱。
/// </summary>
public sealed class TinyEventBus
{
    /// <summary>全局总线。绝大多数跨系统通知走这里；需要隔离的子系统可以自己 new 一个。</summary>
    public static readonly TinyEventBus Global = new TinyEventBus();

    private readonly Dictionary<Type, Delegate> _handlers = new Dictionary<Type, Delegate>();

    /// <summary>订阅。同一个 handler 重复订阅会收到多次（与 C# event 语义一致）。</summary>
    public void On<T>(Action<T> handler)
    {
        if (handler == null)
        {
            return;
        }

        Type key = typeof(T);
        Delegate existing;
        if (_handlers.TryGetValue(key, out existing))
        {
            _handlers[key] = Delegate.Combine(existing, handler);
            return;
        }
        _handlers[key] = handler;
    }

    /// <summary>退订。面板/节点关闭时必须调，否则总线下会攒一堆已释放对象。</summary>
    public void Off<T>(Action<T> handler)
    {
        if (handler == null)
        {
            return;
        }

        Type key = typeof(T);
        Delegate existing;
        if (!_handlers.TryGetValue(key, out existing))
        {
            return;
        }

        Delegate left = Delegate.Remove(existing, handler);
        if (left == null)
        {
            _handlers.Remove(key);
            return;
        }
        _handlers[key] = left;
    }

    /// <summary>
    /// 派发。订阅者在回调里退订是安全的 —— 这里取的是委托快照，
    /// 不会因为集合被改动而抛"迭代时修改"。
    /// </summary>
    public void Emit<T>(T evt)
    {
        Delegate existing;
        if (!_handlers.TryGetValue(typeof(T), out existing))
        {
            return;
        }

        Action<T> callback = existing as Action<T>;
        if (callback == null)
        {
            return;
        }
        callback(evt);
    }

    /// <summary>退订某一种事件的全部订阅者。</summary>
    public void Clear<T>()
    {
        _handlers.Remove(typeof(T));
    }

    /// <summary>清空全部（切场景 / 回主菜单时调，避免上个场景的残留订阅）。</summary>
    public void Clear()
    {
        _handlers.Clear();
    }

    /// <summary>当前已注册的事件种类数（调试用）。</summary>
    public int EventKindCount
    {
        get { return _handlers.Count; }
    }
}
