// 文件：BattleClock.cs
// 《轮回大侠》表现层 —— **战斗节拍与倒计时的唯一持有者**
//
// 为什么要单独抽出来（2026-11-05）：
//   BattlePanel 原本自己 new 了 5 个 Timer（战斗节拍 / 结算推进 / 自动保存 /
//   二次确认超时 / 大劫心跳），时间状态（大劫累计秒数、上一跳的毫秒基准）
//   也散在面板字段里。面板一边管推图状态机、一边当秒表，读代码时
//   分不清哪些是玩法、哪些只是"到点喊一声"。
//
//   现在面板只订阅回调（Ticked / AdvanceDue / AutoSaveDue / ConfirmExpired / DajieDue），
//   自己不再 new Timer、不再算毫秒差。要调节奏就改本文件，不用翻 1800 行的面板。
//
// 设计边界（重要）：
//   · 本类**不认识任何玩法** —— 它不知道"大劫"是什么，只知道"距上次清零过了几秒，
//     超过 X 秒就喊一声"，X 由一个委托给（面板塞的是 DajieConfig.IntervalWith(天赋乘区)）。
//     所以天赋改间隔不用回来同步常量，问一次就是最新的。
//   · `BasePanel` 是普通类不是 Node，**没有 `_Process` 可重写**（曾写 override 直接 CS0115），
//     所以这里仍然是 Timer。但大劫用**真实毫秒差**累加 —— 挂机按"你离开多久"算，
//     不能按模拟秒（战斗 tick 在大劫期间是停的，用 tick 数迟早会算歪）。
//   · Timer 一律挂在**本节点**下（本节点挂在 BattlePanel 的 GameObject 下），
//     继承面板的 ProcessMode=Always，子界面盖上来也不会被冻结。

using System;
using Godot;

public partial class BattleClock : Node
{
    /// <summary>战斗节拍长度：每 0.1s 一响。面板拿它推进模拟与刷新表现。</summary>
    public const float TickSec = 0.1f;

    /// <summary>结算栏展示多久后自动开下一场（普通战斗）。</summary>
    public const float AdvanceSec = 1.1f;

    /// <summary>同上，但用于"大劫打输 → 转世"—— 玩家得先看清自己是怎么死的。</summary>
    public const float AdvanceSecLonger = 2.0f;

    /// <summary>大劫心跳：0.5s 一跳足够（倒计时按"分:秒"显示），也不至于把心跳塞进战斗节拍。</summary>
    const float DajieHeartbeatSec = 0.5f;

    /// <summary>「转世 / 重开本世」二次确认的等待秒数，超时自动解除。</summary>
    const float ConfirmSec = 3.0f;

    /// <summary>面板没给间隔委托时的兜底（2 分钟），避免心跳空转或除零。</summary>
    const double DajieFallbackSec = 120.0;

    // —— 对外回调（C# 事件，不是 Godot signal：调用方全在同一个程序集里，少一层封送）——
    /// <summary>战斗节拍到点（0.1s 一次）。</summary>
    public event Action Ticked;
    /// <summary>结算展示结束，该开下一场了。</summary>
    public event Action AdvanceDue;
    /// <summary>自动保存时间到。</summary>
    public event Action AutoSaveDue;
    /// <summary>「再点一次确认」的等待窗口超时。</summary>
    public event Action ConfirmExpired;
    /// <summary>大劫到点上门。</summary>
    public event Action DajieDue;

    /// <summary>
    /// 大劫间隔（秒）的取值委托。为 null 或返回非正值时用兜底值。
    /// 用委托而不是缓存常量：天赋「阅历·沉着」会改这个间隔，问一次拿一次最新的。
    /// </summary>
    public Func<double> DajieIntervalProvider;

    Timer _beat;
    Timer _advance;
    Timer _autoSave;
    Timer _confirm;
    Timer _dajie;

    ulong _dajieMarkMs;      // 上一跳的真实毫秒基准（每跳都刷新 → 暂停那几段不会被补算）
    double _dajieElapsed;    // 距上一场大劫累计的真实秒数
    bool _dajiePaused;
    long _beats;             // 已响过的节拍数
    bool _built;

    /// <summary>
    /// 从 Build 起累计的**真实节拍秒数**（= 节拍数 × TickSec）。
    /// 给"等 0.8 秒再截图"这类一次性延时用；单调递增，不会因为战斗重开而回零。
    /// </summary>
    public double Elapsed
    {
        get { return _beats * (double)TickSec; }
    }

    /// <summary>
    /// 距上一场大劫累计的真实秒数。
    /// 可读写：存档要落盘、读档要灌回（读档发生在 Build 之前，所以这里只碰字段，不碰 Timer）。
    /// </summary>
    public double DajieElapsed
    {
        get { return _dajieElapsed; }
        set
        {
            _dajieElapsed = value;
            if (_dajieElapsed < 0)
            {
                _dajieElapsed = 0;
            }
        }
    }

    /// <summary>大劫是否处于"不计时"状态（打大劫中 / 结算展示中）。</summary>
    public bool DajiePaused
    {
        get { return _dajiePaused; }
    }

    /// <summary>
    /// 建全部计时器并接线。**只建一次**（重复调用直接返回），
    /// 因为面板的 InitStatic 可能被重复走到，而重复 AddChild 会留下永远在跑的孤儿 Timer。
    /// </summary>
    /// <param name="autoSaveSec">自动保存间隔（秒）。由面板从配置算出，时钟不认识配置。</param>
    public void Build(double autoSaveSec)
    {
        if (_built)
        {
            return;
        }
        _built = true;

        _beat = new Timer { WaitTime = TickSec, OneShot = false, Autostart = false };
        AddChild(_beat);
        _beat.Timeout += OnBeat;

        _advance = new Timer { WaitTime = AdvanceSec, OneShot = true, Autostart = false };
        AddChild(_advance);
        _advance.Timeout += OnAdvanceDue;

        _confirm = new Timer { WaitTime = ConfirmSec, OneShot = true, Autostart = false };
        AddChild(_confirm);
        _confirm.Timeout += OnConfirmExpired;

        if (autoSaveSec > 0)
        {
            _autoSave = new Timer { WaitTime = autoSaveSec, OneShot = false, Autostart = false };
            AddChild(_autoSave);
            _autoSave.Timeout += OnAutoSaveDue;
        }

        _dajie = new Timer { WaitTime = DajieHeartbeatSec, OneShot = false, Autostart = false };
        AddChild(_dajie);
        _dajie.Timeout += OnDajieHeartbeat;
        _dajieMarkMs = Time.GetTicksMsec();
        _dajie.Start();
    }

    // ============ 战斗节拍 ============

    /// <summary>启动战斗节拍（开战 / 从存档面板回来时）。重复调用无副作用。</summary>
    public void StartBeat()
    {
        if (_beat == null)
        {
            return;
        }
        _beat.Start();
    }

    public void StopBeat()
    {
        if (_beat == null)
        {
            return;
        }
        _beat.Stop();
    }

    // ============ 结算推进（一次性延时） ============

    /// <summary>过 <paramref name="sec"/> 秒后喊 AdvanceDue。每次调用都重设时长，不依赖上一次的残留值。</summary>
    public void StartAdvance(float sec)
    {
        if (_advance == null)
        {
            return;
        }
        _advance.WaitTime = sec;
        _advance.Start();
    }

    public void StopAdvance()
    {
        if (_advance == null)
        {
            return;
        }
        _advance.Stop();
    }

    // ============ 二次确认窗口 ============

    public void StartConfirm()
    {
        if (_confirm == null)
        {
            return;
        }
        _confirm.Start();
    }

    public void StopConfirm()
    {
        if (_confirm == null)
        {
            return;
        }
        _confirm.Stop();
    }

    // ============ 自动保存 ============

    /// <summary>按开关启停自动保存。关闭时只 Stop，不销毁 —— 再打开还是同一个节拍基准。</summary>
    public void SyncAutoSave(bool on)
    {
        if (_autoSave == null)
        {
            return;
        }
        if (on)
        {
            _autoSave.Start();
        }
        else
        {
            _autoSave.Stop();
        }
    }

    // ============ 大劫沙漏 ============

    /// <summary>清零（大劫开始 / 打完 / 转世时）。顺手刷新毫秒基准，否则下一跳会把清零前那段算进来。</summary>
    public void ResetDajie()
    {
        _dajieElapsed = 0;
        _dajieMarkMs = Time.GetTicksMsec();
    }

    /// <summary>
    /// 暂停/恢复大劫计时。暂停期间的心跳把这段 dt 随基准时间一起丢掉，恢复后**不补算**。
    /// 面板在"打大劫中"与"结算展示中"都会置 true —— 用户口径："Boss 战斗不算入时间"。
    /// </summary>
    public void SetDajiePaused(bool paused)
    {
        _dajiePaused = paused;
        _dajieMarkMs = Time.GetTicksMsec();
    }

    // ============ 内部接线 ============

    void OnBeat()
    {
        _beats++;
        Action h = Ticked;
        if (h != null)
        {
            h();
        }
    }

    void OnAdvanceDue()
    {
        Action h = AdvanceDue;
        if (h != null)
        {
            h();
        }
    }

    void OnAutoSaveDue()
    {
        Action h = AutoSaveDue;
        if (h != null)
        {
            h();
        }
    }

    void OnConfirmExpired()
    {
        Action h = ConfirmExpired;
        if (h != null)
        {
            h();
        }
    }

    /// <summary>
    /// 大劫心跳：真实毫秒差累加，到点喊一声。
    /// 喊完**不清零** —— 清不清是面板的事（面板可能因为"层数还没建立"拒绝开场，
    /// 这时保持超阈值才能下一跳再喊）；面板真开打时会调 ResetDajie。
    /// </summary>
    void OnDajieHeartbeat()
    {
        ulong now = Time.GetTicksMsec();
        double dt = (now - _dajieMarkMs) / 1000.0;
        _dajieMarkMs = now;

        if (dt <= 0)
        {
            return;
        }
        if (_dajiePaused)
        {
            return;
        }

        _dajieElapsed += dt;
        if (_dajieElapsed < IntervalNow())
        {
            return;
        }

        Action h = DajieDue;
        if (h != null)
        {
            h();
        }
    }

    double IntervalNow()
    {
        if (DajieIntervalProvider == null)
        {
            return DajieFallbackSec;
        }

        double sec = DajieIntervalProvider();
        if (sec <= 0)
        {
            return DajieFallbackSec;
        }
        return sec;
    }
}
