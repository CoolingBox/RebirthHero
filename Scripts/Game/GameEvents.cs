// 文件：GameEvents.cs
// 《轮回大侠》**应用级事件**（走 `TinyEventBus.Global`）
//
// 放什么：**跨系统的通知** —— "某个设置变了""存档写完了""语言换了"。
//   这类事件的订阅方不确定、数量会变（今天只有主面板关心，明天可能音效系统也关心），
//   用事件总线比逐个传回调参数更省事。
//
// 不放什么：**战斗内事件**（伤害/治疗/护盾/死亡）。那些走引擎的 `BattleEventBus`，
//   因为它们是高频、带业务参数、且必须能被无头探针驱动 —— 塞进全局总线会让
//   探针也必须初始化 UI 层。
//
// 事件一律用 readonly struct（派发零装箱）。

/// <summary>战斗倍速被改动（导航条「×N」与设置面板都可能改）。订阅方负责把新速度套给战斗。</summary>
public readonly struct BattleSpeedChanged
{
    /// <summary>切换后的倍速。</summary>
    public readonly double Speed;

    public BattleSpeedChanged(double speed)
    {
        Speed = speed;
    }
}

/// <summary>设置里的「自动保存」开关被切换。订阅方负责启停自己的计时器。</summary>
public readonly struct AutoSaveToggled
{
    /// <summary>切换后的状态。</summary>
    public readonly bool On;

    public AutoSaveToggled(bool on)
    {
        On = on;
    }
}
