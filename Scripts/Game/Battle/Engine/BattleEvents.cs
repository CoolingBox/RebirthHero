// 文件：BattleEvents.cs
// 《轮回大侠》M1 战斗引擎 —— 事件总线（表现层订阅）
// 引擎不引用任何 Godot 类型，表现层通过本总线消费瞬时事件

using System;

/// <summary>
/// 战斗事件总线。引擎在每个模拟动作发生时 raise 对应事件，
/// 表现层（BattlePanel）订阅后做飘字 / 高亮 / 死亡淡出等演出。
/// 所有事件在引擎 Step / Run 的调用线程内同步触发。
/// </summary>
public sealed class BattleEventBus
{
    // 单位行动（comboNow = 上一行动者为友方，连击条件成立）
    public event Action<BattleUnit, bool> OnAction;
    // 武学触发（含普攻：skillName=null）
    public event Action<BattleUnit, string> OnSkill;
    // 伤害（attacker 当前恒非 null；skillName=null=普攻）
    public event Action<BattleUnit, BattleUnit, double, string> OnDamage;
    // 治疗（skillName=null=吸血/其它非武学治疗）
    public event Action<BattleUnit, BattleUnit, double, string> OnHeal;
    // 护盾
    public event Action<BattleUnit, BattleUnit, double> OnShield;
    // 召唤（owner 召唤者，unit 新单位实例）
    public event Action<BattleUnit, BattleUnit> OnSummon;
    // 死亡
    public event Action<BattleUnit> OnDeath;
    // 战斗结束（0=平局/超时，1=A 胜，2=B 胜）
    public event Action<int> OnEnd;

    public void RaiseAction(BattleUnit u, bool combo) => OnAction?.Invoke(u, combo);
    public void RaiseSkill(BattleUnit u, string name) => OnSkill?.Invoke(u, name);
    public void RaiseDamage(BattleUnit a, BattleUnit t, double amt, string skill) => OnDamage?.Invoke(a, t, amt, skill);
    public void RaiseHeal(BattleUnit a, BattleUnit t, double amt, string skill = null) => OnHeal?.Invoke(a, t, amt, skill);
    public void RaiseShield(BattleUnit a, BattleUnit t, double amt) => OnShield?.Invoke(a, t, amt);
    public void RaiseSummon(BattleUnit owner, BattleUnit unit) => OnSummon?.Invoke(owner, unit);
    public void RaiseDeath(BattleUnit u) => OnDeath?.Invoke(u);
    public void RaiseEnd(int r) => OnEnd?.Invoke(r);
}
