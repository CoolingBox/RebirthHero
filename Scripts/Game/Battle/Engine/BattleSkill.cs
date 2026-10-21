// 文件：BattleSkill.cs
// 《轮回大侠》M1 战斗引擎 —— 武学数据
// 数值全部 [PLACEHOLDER]，M0 已验证规则与手感区间

using System;
#nullable disable

public sealed class BattleSkill
{
    public string Name = "";
    public SkillSrc Src;
    public string Cond = "chance";     // count | chance | combo
    public int CondN;                  // count 模式：每 N 次行动/受击
    public double CondP;               // chance 模式概率 / combo 模式概率 / OnHpBelow 阈值(百分比)

    /// <summary>
    /// 开战计数偏移（P1 装备"破军 2 件 / 先手词条"）：条件=次数的武学按
    /// <c>(实计次数 + CounterOffset) % CondN</c> 判定。
    /// **不是"额外行动"** —— 不增加 Actions/Hits、不触发其它武学，只把"第几次能放"往前挪。
    /// </summary>
    public int CounterOffset;
    public SkillEff Eff;

    /// <summary>
    /// 效果数值。**含义由 <see cref="MultOnAtk"/> 决定**：
    /// true → 以施法者攻击为基准的**倍数**（结算时 × EffAtk）；false → **绝对量**
    /// （如"气血上限%"类，配置接入层已按施法者气血折算完毕）。
    /// 权重增减 / 速度与攻击修正 / 反伤比例属于"非基准量"，恒为绝对量。
    /// </summary>
    public double Mult;

    /// <summary>
    /// <see cref="Mult"/> 是否以**施法者攻击**为基准。
    /// 伤害恒为 true；治疗/护盾取决于卡面 <c>CalcBase</c>：ATK倍数 → true，气血% / 固定值 → false。
    /// <para>
    /// 2026-10-16 修：原先**只有伤害**乘了 EffAtk，治疗/护盾直接 <c>+= Mult</c>，
    /// 于是"1.22×攻击"的金钟罩只给出 **1.22 点**护盾 —— 表现为界面上"全队护盾 1"。
    /// 根因是 Mult 一字段承载了两种语义（倍数 / 绝对量），引擎无从分辨。
    /// </para>
    /// </summary>
    public bool MultOnAtk;
    public DamageKind DmgKind = DamageKind.External; // 伤害类型（仅 Eff=Dmg 时生效）
    public SkillTgt Tgt = SkillTgt.Random;
    public bool CannotAttack;          // 行动类：触发时替换该次普攻（数值可更狠）
    public bool Vamp;                  // 伤害附带吸血 50%
    public bool SilenceAlso;           // 伤害附带沉默（独孤九剑）
    public double ReplayP = 0.01;      // 重放概率（全局保底 1%）
    public int ReplayC = 2;            // 重放上限（总命中次数）
    public bool ReplayLocked = true;   // 重放锁定目标（降龙十八掌：单点）
    public string SummonKind;         // 召唤物类型 tank|dps|sub|sword

    /// <summary>召唤物【类型规格】（血攻/速度/权重/存在时间/同时上限）。由配置接入层按 <see cref="SummonKind"/> 装配。</summary>
    public SummonSpec Summon;

    /// <summary>
    /// **该武学**同时存活的召唤物上限。**0 = 跟随类型档位**（<see cref="SummonSpec.Max"/>），
    /// &gt;0 = 该武学的专属收紧/放宽（少数卡"特别能召"或"只许一只"时用）。
    /// 外层还有一道全队上限 <see cref="BattleConfig.SummonCap"/>。
    /// </summary>
    public int SummonMax;

    /// <summary>
    /// **每次触发召唤几只**（1 = 单只；&gt;1 = 一次多只，如御剑术"一次召唤 2 飞剑"）。
    /// 注意与 <see cref="SummonMax"/>（**同时存活上限**）是两件事：本列管"一次召几只"，上限管"最多留几只"。
    /// 多只召唤逐只走同一套上限检查，超限即停。
    /// </summary>
    public int SummonCount = 1;

    /// <summary>
    /// **召唤时号令**：本卡触发召唤后，命令己方所有**同类型**召唤物各立即普攻一次（万剑归宗"剑阵齐射"）。
    /// 注意是"命令"而非"额外行动"——不调用 <c>Act()</c>，不增加行动计数、不触发其自身武学，
    /// 只做一次普攻结算并抛伤害事件（UI 能看到齐射）。同类型 = <see cref="SummonSpec.Kind"/> 相同。
    /// </summary>
    public bool SummonOrder;

    public bool CondMet(BattleUnit u, Random rng, bool comboNow) => Cond switch
    {
        "count" => (u.Counter(Src) + CounterOffset) % CondN == 0,
        "chance" => rng.NextDouble() < CondP,
        "combo" => comboNow && rng.NextDouble() < CondP,
        _ => false,
    };

    public BattleSkill Clone() => new()
    {
        Name = Name, Src = Src, Cond = Cond, CondN = CondN, CondP = CondP, Eff = Eff, Mult = Mult,
        MultOnAtk = MultOnAtk,
        DmgKind = DmgKind,
        Tgt = Tgt, CannotAttack = CannotAttack, Vamp = Vamp, SilenceAlso = SilenceAlso,
        ReplayP = ReplayP, ReplayC = ReplayC, ReplayLocked = ReplayLocked, SummonKind = SummonKind,
        Summon = Summon, SummonMax = SummonMax, SummonOrder = SummonOrder, SummonCount = SummonCount,
        CounterOffset = CounterOffset,
    };
}
