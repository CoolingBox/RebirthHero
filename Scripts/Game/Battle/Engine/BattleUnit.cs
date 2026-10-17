// 文件：BattleUnit.cs
// 《轮回大侠》M1 战斗引擎 —— 单位数据
// 数值全部 [PLACEHOLDER]，M0 已验证规则与手感区间

using System;
using System.Collections.Generic;
#nullable disable

public sealed class BattleUnit
{
    public string Name = "";
    public int Slot;                   // 1-4 侠客位；0 召唤物
    public double BaseHp, Hp, BaseAtk, BaseSpd;

    /// <summary>
    /// 被"携带召唤类武学"锁死、不可用的生命上限（2026-10-17 口径：每带 1 个召唤武学，所属侠客最大生命 −15%，叠加）。
    /// 这部分血**永远回不来**，也不参与承伤/治疗（施法只到 <see cref="BaseHp"/> 为止）。
    /// UI 上以银灰色段显示在血条最右端，直观表达"被召唤占用的那截生命"。
    /// 注：血条满格 = <see cref="BaseHp"/> + <see cref="LockedHp"/>（即"原本该有的最大生命"）。
    /// </summary>
    public double LockedHp;
    public double BaseDef;             // 防御：只减免外功伤害（框架 §2）
    public double Shield;
    public bool IsSummon;
    public double Ttl;                 // 召唤物存活时间（秒）

    /// <summary>召唤来源武学（仅 <see cref="IsSummon"/> 有值）—— 用于"单个召唤武学的同时存活上限"。</summary>
    public BattleSkill SummonFrom;

    /// <summary>召唤者（仅 <see cref="IsSummon"/> 有值）—— 用于统计把召唤物的伤害/承伤归入"召唤者 + 召唤来源武学"。</summary>
    public BattleUnit Summoner;

    /// <summary>
    /// 每次命中固定扣血（&gt;0 时无视伤害量，一次命中只扣这么多）；0 = 按实际伤害。
    /// 飞剑 = 2 层计数体（<c>FixedHp=2 / DmgPerHit=1</c>）。见 <see cref="SummonSpec.DmgPerHit"/>。
    /// </summary>
    public double DmgPerHit;

    /// <summary>
    /// 召唤物气血加成（资质词条"百兽之王"）：召唤物的气血再乘本值。
    /// 只作用于**比例血**型召唤物；<see cref="DmgPerHit"/> 型的计数体（飞剑）不适用。
    /// </summary>
    public double SummonHpMul = 1.0;

    /// <summary>
    /// 受击伤害乘数（资质词条"嗜战如命"的代价面）：&gt;1 = 更脆，结算时乘在**承伤**上。
    /// 与 <see cref="AtkMult"/>（攻击）无关，是"用更脆换更痛"的词条另一半。
    /// </summary>
    public double DmgTakenMul = 1.0;

    public List<BattleSkill> Skills = new();

    /// <summary>
    /// 起手气血：**&lt; 0 = 满血起手**（默认）；**≥ 0 = 按该值起手**（上限为 <see cref="BaseHp"/>）。
    /// 用途：推图"波间延续血量" —— 一场战斗的角色状态由引擎 <c>Clone()</c> 重置，唯一被允许
    /// 带进下一波的就是血量（0 = 该波已阵亡，下一波不复活）。
    /// </summary>
    public double StartHp = -1;

    // —— 运行时状态 ——
    public double Gauge;               // 行动条 0..GaugeMax
    public int Actions, Hits;
    public double AtkMult = 1.0;
    public double PoisonStacks;
    public BattleUnit PoisonFrom;
    public double NextPoisonTick = -1;
    public BattleUnit BurnFrom;
    public double BurnUntil = -1, NextBurnTick = -1;
    public double SilenceUntil = -1;
    public HashSet<BattleSkill> FiredOnce = new();
    public double AccumDmg;            // 累计承伤（反伤用）
    public double BaseW;               // 基础受击权重
    public List<(double amt, double until)> WBuf = new(), SpdBuf = new(), AtkBuf = new();

    public bool Alive => Hp > 0;

    public double EffSpd(double t)
    {
        double f = 1;
        foreach (var b in SpdBuf) if (b.until > t) f += b.amt;
        return Math.Max(10, BaseSpd * f);
    }

    public double EffAtk(double t)
    {
        double f = 1;
        foreach (var b in AtkBuf) if (b.until > t) f += b.amt;
        return BaseAtk * AtkMult * f;
    }

    public double EffW(double t)
    {
        double w = BaseW;
        foreach (var b in WBuf) if (b.until > t) w += b.amt;
        return Math.Max(BattleConfig.WFloor, w);
    }

    public int Counter(SkillSrc s) => s == SkillSrc.OnAction ? Actions : Hits;
    public bool Silenced(double t) => t < SilenceUntil;
}
