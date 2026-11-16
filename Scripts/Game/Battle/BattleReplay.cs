// 文件：BattleReplay.cs
// 《轮回大侠》—— **战斗录像**：开战时一次性算完整场，之后前端只负责播
//
// 为什么要这个（2026-11-13 用户口径："进入战斗时使用种子并算出战斗过程和结果；前端就是播片"）：
//   原来引擎是**实时推进**的 —— 每 0.1s 拍一下 Step(0.05)，前端顺手刷 UI。
//   这带来三个结构问题：
//     ① 结果与帧率/倍速耦合（虽然已用固定步长收口，但仍是"边算边看"）；
//     ② 想做"跳过战斗""离线结算""回放战报"都得另写一套；
//     ③ 前端的任何卡顿都会延后结果，而结果本该在入场那一刻就定了。
//   现在分成两段：
//     · **算**：入场时用种子把整场跑完（无渲染），逐 0.1s 采一帧、逐个事件记一条；
//     · **播**：之后每拍只做"把帧贴回单位 + 到点把事件重新抛一遍"，不跑任何规则。
//
// 为什么敢这么拆：引擎的随机性**只有** `Random Rng`（由种子初始化），没有一处读真实时间。
// 所以"同一场跑完"是确定的，录下来的东西就是这一场唯一的过程。
//
// 为什么用"重抛事件"而不是"直接调 UI"：
//   战报（StatsTracker）订阅的是 `engine.Bus`。把录下的事件按时间重新 Raise 一遍，
//   战报就自然跟着播片一起长数字，不必为播片再写一套统计入口。
//   —— 前提是播片期间**没有任何东西在 Step 引擎**（见 BattleManager.OnBeat）。
//
// 边界：本文件不引用 Godot，也不认识面板；它只认 BattleEngine 与 BattleEventBus。

using System;
using System.Collections.Generic;

/// <summary>录像里的事件类型（与 BattleEventBus 的回调一一对应）。</summary>
public enum ReplayKind
{
    Action = 1,
    Skill = 2,
    Damage = 3,
    Heal = 4,
    Shield = 5,
    Summon = 6,
    Death = 7,
    End = 8,
    Dodge = 9,
    Dot = 10,
}

/// <summary>一条带时间戳的事件。<see cref="T"/> 是**模拟秒**，与真实时间无关。</summary>
public struct ReplayEvent
{
    public double T;
    public ReplayKind Kind;

    /// <summary>主体（行动者 / 攻击者 / 治疗者 / 召唤者 / 死者）。</summary>
    public BattleUnit A;

    /// <summary>客体（目标 / 被召唤出的单位）。</summary>
    public BattleUnit B;

    public double Amt;
    public string Skill;
    public int Hits;
    public bool Combo;

    /// <summary>Dot 专用：true = 火，false = 毒。</summary>
    public bool Burn;

    public int Result;
}

/// <summary>某单位在某一帧的**显示用**状态。
/// 只存前端会读的字段 —— 引擎的其余状态（武学剩余次数、buff 表等）播片时不参与。</summary>
public sealed class UnitSnapshot
{
    public BattleUnit U;
    public double Hp;
    public double Shield;
    public double Gauge;
    public double SilenceUntil;
    public double Ttl;

    public void CaptureFrom(BattleUnit u)
    {
        U = u;
        Hp = u.Hp;
        Shield = u.Shield;
        Gauge = u.Gauge;
        SilenceUntil = u.SilenceUntil;
        Ttl = u.Ttl;
    }

    /// <summary>贴回单位（播片用）。只改显示相关字段，不动武学与计数器 —— 那些播片不需要。</summary>
    public void Apply()
    {
        if (U == null)
        {
            return;
        }
        U.Hp = Hp;
        U.Shield = Shield;
        U.Gauge = Gauge;
        U.SilenceUntil = SilenceUntil;
        U.Ttl = Ttl;
    }
}

/// <summary>一帧：时刻 + 全体单位状态 + 双方**当帧名单**（召唤物会出现/消失，必须逐帧记）。</summary>
public sealed class ReplayFrame
{
    public double T;
    public UnitSnapshot[] Units;
    public BattleUnit[] Ally;
    public BattleUnit[] Foe;
}

/// <summary>一份可播的录像。</summary>
public sealed class BattleReplay
{
    public readonly List<ReplayFrame> Frames = new List<ReplayFrame>();
    public readonly List<ReplayEvent> Events = new List<ReplayEvent>();

    /// <summary>整场时长（模拟秒）。</summary>
    public double Duration;

    /// <summary>0 平局/超时，1 我方胜，2 我方负。</summary>
    public int Result;

    /// <summary>按帧时间找"不晚于 t 的最后一帧"。帧按时间升序，用游标线性推进（播片只往前走）。</summary>
    public int FrameIndexAt(double t, int from)
    {
        int i = from;
        while (i + 1 < Frames.Count && Frames[i + 1].T <= t)
        {
            i++;
        }
        return i;
    }
}

/// <summary>
/// 录制器：挂到引擎事件总线上，一边把整场跑完，一边采帧、记事件。
/// 用法：Attach → Run(fixedStep) → Detach，之后拿 <see cref="Replay"/> 去播。
/// </summary>
public sealed class BattleRecorder
{
    /// <summary>采样间隔（模拟秒）。与战斗节拍一致：播片时本来就是 10Hz 刷画面。</summary>
    public const double SampleDt = 0.10;

    private readonly BattleEngine _engine;
    private readonly BattleReplay _replay = new BattleReplay();

    /// <summary>出现过的一切单位（含已阵亡、已从场上移除的召唤物）。</summary>
    private readonly List<BattleUnit> _seen = new List<BattleUnit>();
    private readonly HashSet<BattleUnit> _seenSet = new HashSet<BattleUnit>();

    public BattleRecorder(BattleEngine engine)
    {
        _engine = engine;
    }

    public BattleReplay Replay
    {
        get { return _replay; }
    }

    // ============ 订阅 / 退订 ============

    public void Attach()
    {
        BattleEventBus bus = _engine.Bus;
        bus.OnAction += OnAction;
        bus.OnSkill += OnSkill;
        bus.OnDamage += OnDamage;
        bus.OnDodge += OnDodge;
        bus.OnDot += OnDot;
        bus.OnHeal += OnHeal;
        bus.OnShield += OnShield;
        bus.OnSummon += OnSummon;
        bus.OnDeath += OnDeath;
        bus.OnEnd += OnEnd;
    }

    public void Detach()
    {
        BattleEventBus bus = _engine.Bus;
        bus.OnAction -= OnAction;
        bus.OnSkill -= OnSkill;
        bus.OnDamage -= OnDamage;
        bus.OnDodge -= OnDodge;
        bus.OnDot -= OnDot;
        bus.OnHeal -= OnHeal;
        bus.OnShield -= OnShield;
        bus.OnSummon -= OnSummon;
        bus.OnDeath -= OnDeath;
        bus.OnEnd -= OnEnd;
    }

    // ============ 跑完整场 ============

    /// <summary>
    /// 把这场跑到结束（**不渲染**），返回录像。调用前必须先 <see cref="Attach"/>。
    /// </summary>
    public BattleReplay Run(double fixedStep)
    {
        if (fixedStep <= 0)
        {
            fixedStep = 0.05;
        }

        Collect(_engine.A);
        Collect(_engine.B);
        Snapshot(0);

        double nextSample = SampleDt;
        // 兜底上限：正常战斗受 BattleConfig.Limit 约束，这里再给一点余量防死循环
        int guard = 0;
        int maxSteps = (int)(BattleConfig.Limit / fixedStep) + 64;

        while (!_engine.IsOver && guard++ < maxSteps)
        {
            _engine.Step(fixedStep);

            if (_engine.T >= nextSample)
            {
                Snapshot(_engine.T);
                nextSample = _engine.T + SampleDt;
            }
        }

        // 收尾帧：结算要用"最终名单 + 最终血量"，不能停在最后一个采样点
        Snapshot(_engine.T);

        _replay.Duration = _engine.T;
        _replay.Result = _engine.Result;
        return _replay;
    }

    void Snapshot(double t)
    {
        ReplayFrame f = new ReplayFrame();
        f.T = t;

        f.Units = new UnitSnapshot[_seen.Count];
        for (int i = 0; i < _seen.Count; i++)
        {
            UnitSnapshot s = new UnitSnapshot();
            s.CaptureFrom(_seen[i]);
            f.Units[i] = s;
        }

        f.Ally = _engine.A.ToArray();
        f.Foe = _engine.B.ToArray();
        _replay.Frames.Add(f);
    }

    void Collect(List<BattleUnit> team)
    {
        for (int i = 0; i < team.Count; i++)
        {
            Collect(team[i]);
        }
    }

    void Collect(BattleUnit u)
    {
        if (u == null || !_seenSet.Add(u))
        {
            return;
        }
        _seen.Add(u);
    }

    // ============ 事件 → 录像 ============

    void OnAction(BattleUnit u, bool combo)
    {
        Push(new ReplayEvent { Kind = ReplayKind.Action, A = u, Combo = combo });
    }

    void OnSkill(BattleUnit u, string name, int hits)
    {
        Push(new ReplayEvent { Kind = ReplayKind.Skill, A = u, Skill = name, Hits = hits });
    }

    void OnDamage(BattleUnit a, BattleUnit t, double amt, string skill)
    {
        Push(new ReplayEvent { Kind = ReplayKind.Damage, A = a, B = t, Amt = amt, Skill = skill });
    }

    void OnDot(BattleUnit a, BattleUnit t, double amt, bool burn)
    {
        Push(new ReplayEvent { Kind = ReplayKind.Dot, A = a, B = t, Amt = amt, Burn = burn });
    }

    void OnDodge(BattleUnit a, BattleUnit t, string skill)
    {
        Push(new ReplayEvent { Kind = ReplayKind.Dodge, A = a, B = t, Skill = skill });
    }

    void OnHeal(BattleUnit a, BattleUnit t, double amt, string skill)
    {
        Push(new ReplayEvent { Kind = ReplayKind.Heal, A = a, B = t, Amt = amt, Skill = skill });
    }

    void OnShield(BattleUnit a, BattleUnit t, double amt)
    {
        Push(new ReplayEvent { Kind = ReplayKind.Shield, A = a, B = t, Amt = amt });
    }

    void OnSummon(BattleUnit owner, BattleUnit unit)
    {
        Collect(unit);   // 召唤物第一次出现，纳入逐帧快照
        Push(new ReplayEvent { Kind = ReplayKind.Summon, A = owner, B = unit });
    }

    void OnDeath(BattleUnit u)
    {
        Push(new ReplayEvent { Kind = ReplayKind.Death, A = u });
    }

    void OnEnd(int r)
    {
        Push(new ReplayEvent { Kind = ReplayKind.End, Result = r });
    }

    void Push(ReplayEvent e)
    {
        e.T = _engine.T;
        _replay.Events.Add(e);
    }
}

/// <summary>
/// 播放器：把录像"贴上"引擎对象并按模拟时间往前推。
/// 它**不跑任何规则** —— 只做两件事：把到点的事件重新抛到 `engine.Bus`（战报与表现层照旧订阅），
/// 以及把当前帧的单位状态贴回去（血条/行动条/召唤物名单）。
/// </summary>
public sealed class BattlePlayback
{
    private readonly BattleReplay _replay;
    private readonly BattleEngine _engine;

    private int _frame;
    private int _eventIdx;

    /// <summary>已经播到的模拟秒数。</summary>
    public double T;

    public bool Done { get; private set; }

    public BattlePlayback(BattleReplay replay, BattleEngine engine)
    {
        _replay = replay;
        _engine = engine;
    }

    public double Duration
    {
        get { return _replay != null ? _replay.Duration : 0; }
    }

    /// <summary>回到开场：贴上第 0 帧（这样建卡用的是"满血开局"，不是算完的残局）。</summary>
    public void SeekToStart()
    {
        T = 0;
        _frame = 0;
        _eventIdx = 0;
        Done = false;

        if (_replay != null && _replay.Frames.Count > 0)
        {
            ApplyFrame(_replay.Frames[0]);
        }
    }

    /// <summary>
    /// 往前推 <paramref name="simDt"/> 模拟秒：派发到点事件 + 贴当前帧。
    /// 到达终点时**先贴最终帧再放完剩余事件** —— 结算（续战人头/胜负）读的是引擎状态，
    /// 顺序反了就会用倒数第二帧去结算。
    /// </summary>
    public void Advance(double simDt)
    {
        if (Done || _replay == null)
        {
            return;
        }

        T += simDt;

        if (T >= _replay.Duration)
        {
            T = _replay.Duration;
            ApplyFrame(_replay.Frames[_replay.Frames.Count - 1]);
            DispatchTo(double.MaxValue);
            Done = true;
            return;
        }

        DispatchTo(T);
        _frame = _replay.FrameIndexAt(T, _frame);
        ApplyFrame(_replay.Frames[_frame]);
    }

    /// <summary>一次播完（用于"跳过"或测试）。</summary>
    public void FinishImmediately()
    {
        if (Done || _replay == null)
        {
            return;
        }

        T = _replay.Duration;
        ApplyFrame(_replay.Frames[_replay.Frames.Count - 1]);
        DispatchTo(double.MaxValue);
        Done = true;
    }

    void ApplyFrame(ReplayFrame f)
    {
        _engine.T = f.T;

        for (int i = 0; i < f.Units.Length; i++)
        {
            f.Units[i].Apply();
        }

        _engine.A.Clear();
        _engine.A.AddRange(f.Ally);
        _engine.B.Clear();
        _engine.B.AddRange(f.Foe);
    }

    void DispatchTo(double t)
    {
        BattleEventBus bus = _engine.Bus;

        while (_eventIdx < _replay.Events.Count && _replay.Events[_eventIdx].T <= t)
        {
            ReplayEvent e = _replay.Events[_eventIdx];
            _eventIdx++;

            switch (e.Kind)
            {
                case ReplayKind.Action:
                    bus.RaiseAction(e.A, e.Combo);
                    break;
                case ReplayKind.Skill:
                    bus.RaiseSkill(e.A, e.Skill, e.Hits);
                    break;
                case ReplayKind.Damage:
                    bus.RaiseDamage(e.A, e.B, e.Amt, e.Skill);
                    break;
                case ReplayKind.Dodge:
                    bus.RaiseDodge(e.A, e.B, e.Skill);
                    break;
                case ReplayKind.Dot:
                    bus.RaiseDot(e.A, e.B, e.Amt, e.Burn);
                    break;
                case ReplayKind.Heal:
                    bus.RaiseHeal(e.A, e.B, e.Amt, e.Skill);
                    break;
                case ReplayKind.Shield:
                    bus.RaiseShield(e.A, e.B, e.Amt);
                    break;
                case ReplayKind.Summon:
                    bus.RaiseSummon(e.A, e.B);
                    break;
                case ReplayKind.Death:
                    bus.RaiseDeath(e.A);
                    break;
                case ReplayKind.End:
                    bus.RaiseEnd(e.Result);
                    break;
            }
        }
    }
}
