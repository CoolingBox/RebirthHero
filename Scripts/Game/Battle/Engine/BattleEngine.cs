// 文件：BattleEngine.cs
// 《轮回大侠》M1 战斗引擎 —— 模拟核心（纯 C#，无 Godot 依赖）
// 规则依据：《轮回大侠-战斗数值框架-v0.1》§7/§8/§9
// 说明：Run() 为 M0 确定性步进（供无头验证）；Step(dt) 为实时步进（供表现层逐帧驱动）
//       两者复用全部行动逻辑（Act/DealDamage/Fire/Apply），保证"验证用的数学"与"看见的数学"一致。

using System;
using System.Collections.Generic;
using System.Linq;
#nullable disable

/// <summary>单场战斗统计（M0 验证口径，供无头测试复算）</summary>
public sealed class BattleStats
{
    public double Duration;
    public double[] DamageTaken = new double[5];   // 1-4 侠客位承伤
    public int Dodges, Summons, ReplayExtra, MaxChainSeen, Chains5Plus, SummonOrders;
    public double VampHeal;
    public int ComboOps, ComboHits;
}

public sealed class BattleEngine
{
    public List<BattleUnit> A = new(), B = new();
    public double T;
    public BattleUnit LastActor;
    public int ChainDepth;
    public Random Rng = new();
    public BattleStats St = new();
    public BattleEventBus Bus = new();
    public static double GaugeMax => BattleConfig.GaugeMax;   // 转发（BattleConfig 由 TuningConfig 表注入，见 Tuning.cs）

    public bool IsOver => _over;
    public int Result { get; private set; }   // 0 平局/超时，1 A 胜，2 B 胜

    private bool _over;

    /// <summary>
    /// 行动序号：每次 <c>Act()</c> 自增一次。受击计数按它去重 —— 同一序号内，
    /// 一个目标最多只加 1 次 <see cref="BattleUnit.Hits"/>（见 <see cref="BattleUnit.HitActionId"/>）。
    /// 号令齐射（<see cref="OrderSummons"/>）刻意不走 Act，所以整轮齐射算同一次行动。
    /// </summary>
    private long _actSeq;
    private readonly HashSet<BattleUnit> _deadNotified = new();
    private readonly List<BattleUnit> _pendingRemove = new();   // 待移除的召唤物"尸体"（延迟到安全点）
    private readonly Dictionary<BattleSkill, double> _hpThresh = new();

    public void SetTeams(IEnumerable<BattleUnit> a, IEnumerable<BattleUnit> b, int seed)
    {
        Rng = new Random(seed);
        A = a.Select(u => Clone(u)).ToList();
        B = b.Select(u => Clone(u)).ToList();
        T = 0; LastActor = null; ChainDepth = 0; _over = false; Result = 0;
        _actSeq = 0;                 // 行动序号从 0 起（每次 Act 自增，单位侧 -1 = 未计）
        St = new BattleStats();
        _deadNotified.Clear();
        _hpThresh.Clear();
    }

    private BattleUnit Clone(BattleUnit u)
    {
        var c = new BattleUnit
        {
            Name = u.Name, Slot = u.Slot, BaseHp = u.BaseHp, BaseAtk = u.BaseAtk, BaseSpd = u.BaseSpd,
            BaseDef = u.BaseDef, BaseW = u.BaseW, IsSummon = u.IsSummon, Ttl = u.Ttl,
            DmgPerHit = u.DmgPerHit,
            SummonHpMul = u.SummonHpMul, DmgTakenMul = u.DmgTakenMul,   // 装配期属性，跨波延续时要带上
            DotMul = u.DotMul, DotStackBonus = u.DotStackBonus,         // 百淬套：装配期写入，不带上就等于套装没生效
            ShieldKeep = u.ShieldKeep,                                   // 护盾保持率：资质/装备装配期写入
            LockedHp = u.LockedHp,                                       // 召唤惩罚锁死的生命，跨波延续（不变）
        };
        // 起手气血：默认满血；StartHp ≥ 0 时按该值起手（推图波间延续血量；0 = 已阵亡）
        c.Hp = u.StartHp >= 0 ? Math.Min(c.BaseHp, u.StartHp) : c.BaseHp;
        c.Skills = new List<BattleSkill>();
        foreach (var s in u.Skills)
        {
            var k = s.Clone();
            if (k.Src == SkillSrc.OnHpBelow) _hpThresh[k] = k.CondP;
            c.Skills.Add(k);
        }
        return c;
    }

    // ============ 查询辅助 ============
    public bool InA(BattleUnit u) => A.Contains(u);
    public List<BattleUnit> Allies(BattleUnit u) => InA(u) ? A : B;
    public List<BattleUnit> Foes(BattleUnit u) => InA(u) ? B : A;
    public IEnumerable<BattleUnit> AliveOf(List<BattleUnit> team)
    {
        foreach (var u in team) if (u.Alive) yield return u;
    }

    // ============ 确定性 Run()（无头验证用，复刻 M0） ============
    public int Run()
    {
        while (T < BattleConfig.Limit)
        {
            FlushRemovals();   // 清理上一轮行动中阵亡的召唤物（必须在遍历 A/B 之前）
            double dt = BattleConfig.DtCap;
            foreach (var u in A) if (u.Alive && u.Gauge < GaugeMax) dt = Math.Min(dt, (GaugeMax - u.Gauge) / u.EffSpd(T) + 1e-9);
            foreach (var u in B) if (u.Alive && u.Gauge < GaugeMax) dt = Math.Min(dt, (GaugeMax - u.Gauge) / u.EffSpd(T) + 1e-9);
            if (dt < 1e-9) dt = 1e-6;
            Advance(dt);
            FlushRemovals();   // 清理本次 Advance 里 TTL/DoT 到期而死的召唤物

            var ready = new List<BattleUnit>();
            foreach (var u in A) if (u.Alive && u.Gauge >= GaugeMax) ready.Add(u);
            foreach (var u in B) if (u.Alive && u.Gauge >= GaugeMax) ready.Add(u);
            ready.Sort((x, y) => y.EffSpd(T).CompareTo(x.EffSpd(T)));
            foreach (var u in ready)
            {
                if (!u.Alive) continue;
                u.Gauge = 0;
                Act(u);
                if (!AliveOf(A).Any()) { FlushRemovals(); St.Duration = T; _over = true; Result = 2; Bus.RaiseEnd(2); return 2; }
                if (!AliveOf(B).Any()) { FlushRemovals(); St.Duration = T; _over = true; Result = 1; Bus.RaiseEnd(1); return 1; }
                if (T >= BattleConfig.Limit) break;
            }
        }
        FlushRemovals();
        St.Duration = Math.Min(T, BattleConfig.Limit);
        _over = true; Result = 0; Bus.RaiseEnd(0);
        return 0;
    }

    // ============ 实时 Step(dt)（表现层逐帧驱动） ============
    public void Step(double dt)
    {
        if (_over) return;
        FlushRemovals();   // 清理上一帧行动中阵亡的召唤物
        T += dt;
        foreach (var u in A) TickUnit(u, dt);
        foreach (var u in B) TickUnit(u, dt);

        int guard = 0;
        while (guard++ < 256)
        {
            FlushRemovals();   // TTL/DoT 到期而死的召唤物在此落地
            var ready = new List<BattleUnit>();
            foreach (var m in A) if (m.Alive && m.Gauge >= GaugeMax) ready.Add(m);
            foreach (var m in B) if (m.Alive && m.Gauge >= GaugeMax) ready.Add(m);
            if (ready.Count == 0) break;
            ready.Sort((x, y) => y.EffSpd(T).CompareTo(x.EffSpd(T)));
            var u = ready[0];
            u.Gauge -= GaugeMax;   // 保留溢出，维持速度比
            Act(u);
            if (!AliveOf(A).Any()) { FlushRemovals(); St.Duration = T; _over = true; Result = 2; Bus.RaiseEnd(2); return; }
            if (!AliveOf(B).Any()) { FlushRemovals(); St.Duration = T; _over = true; Result = 1; Bus.RaiseEnd(1); return; }
        }
        FlushRemovals();   // 保证 Step 返回后队伍里没有召唤物尸体
        if (T >= BattleConfig.Limit && !_over)
        {
            St.Duration = BattleConfig.Limit;
            _over = true; Result = 0; Bus.RaiseEnd(0);
        }
    }

    // ============ 内核 ============
    void Advance(double dt)
    {
        T += dt;
        foreach (var u in A) TickUnit(u, dt);
        foreach (var u in B) TickUnit(u, dt);
    }

    void TickUnit(BattleUnit u, double dt)
    {
        if (!u.Alive) return;
        if (u.Gauge < GaugeMax) u.Gauge = Math.Min(GaugeMax, u.Gauge + u.EffSpd(T) * dt);
        if (u.IsSummon)
        {
            u.Ttl -= dt;
            if (u.Ttl <= 0) { u.Hp = 0; EmitDeathIfNeeded(u); return; }
        }
        // DoT 系数取**施毒者**的 DotMul（百淬套穿在放毒的人身上，放大的是他造成的伤害）
        while (u.PoisonStacks > 0 && u.NextPoisonTick > 0 && u.NextPoisonTick <= T && u.Alive)
        { DotDamage(u, u.PoisonStacks * BattleConfig.PoisonPerStack * u.PoisonFrom!.EffAtk(T) * u.PoisonFrom!.DotMul); u.NextPoisonTick += BattleConfig.PoisonTick; }
        while (u.BurnUntil > T - 1e-9 && u.NextBurnTick > 0 && u.NextBurnTick <= T && u.Alive)
        { DotDamage(u, BattleConfig.BurnRate * u.BurnFrom!.EffAtk(T) * u.BurnFrom!.DotMul); u.NextBurnTick += BattleConfig.BurnTick; }
    }

    void DotDamage(BattleUnit u, double amount)
    {
        u.Hp -= amount;
        EmitDeathIfNeeded(u);
        if (!u.Alive) { u.Hp = 0; u.PoisonStacks = 0; u.BurnUntil = -1; }
    }

    void Act(BattleUnit u)
    {
        _actSeq++;                   // 新的一次行动：本次行动内目标最多被计 1 次受击

        // 护盾衰减（2026-10-21 用户设定）：**自身开始行动时，护盾 × 护盾保持率**。
        // 保持率是侠客属性（BattleUnit.ShieldKeep，默认 0.5 = 减半），资质/装备/武学都能改写；
        // 这里做唯一的 clamp 点，任何来源都突破不了上限（BattleConfig.ShieldKeepCap）。
        // 位置在行动**开始**、武学结算**之前** —— 本次行动里新获得的护盾是满值，
        // 要留到下一次自身行动才衰减（行动盾只看得到一轮的收益，这是刻意的）。
        // 不足 1 点直接归零：护盾是 double，无限衰减会留下 0.5 这种既显示不出来、
        // 又让血条一直留着金色蒙层的残余。
        if (u.Shield > 0)
        {
            double keep = u.ShieldKeep;
            if (keep < 0) keep = 0;
            if (keep > BattleConfig.ShieldKeepCap) keep = BattleConfig.ShieldKeepCap;
            u.Shield *= keep;
            if (u.Shield < 1) u.Shield = 0;
        }

        u.Actions++;
        bool comboNow = LastActor != null && Allies(u).Contains(LastActor);
        if (u.Skills.Any(s => s.Cond == "combo")) { St.ComboOps++; if (comboNow) St.ComboHits++; }
        LastActor = u;
        Bus.RaiseAction(u, comboNow);

        var trig = new List<BattleSkill>();
        if (!u.Silenced(T))
            foreach (var s in u.Skills)
                if (s.Src == SkillSrc.OnAction && s.CondMet(u, Rng, comboNow)) trig.Add(s);

        bool replace = false;
        foreach (var s in trig) if (s.CannotAttack) { replace = true; break; }
        foreach (var s in trig) Fire(u, s);
        if (!replace)
        {
            var foes = AliveOf(Foes(u)).ToList();
            if (foes.Count > 0)
            {
                var tgt = WeightedPick(foes);
                double amt = DealDamage(u, tgt, u.EffAtk(T));
                Bus.RaiseDamage(u, tgt, amt, null);
            }
        }
    }

    public BattleUnit WeightedPick(List<BattleUnit> units)
    {
        double total = 0; foreach (var u in units) total += u.EffW(T);
        double r = Rng.NextDouble() * total;
        foreach (var u in units) { r -= u.EffW(T); if (r <= 0) return u; }
        return units[^1];
    }

    public double DealDamage(BattleUnit att, BattleUnit tgt, double amount, DamageKind kind = DamageKind.External)
    {
        // 伤害类型减免（框架 §2）：外功硬减 / 内功比例化解 / 真伤无视
        if (kind == DamageKind.External)
        {
            amount = Math.Max(amount * BattleConfig.ExtMinRatio, amount - tgt.BaseDef);
        }
        else if (kind == DamageKind.Internal)
        {
            double ratio = BattleConfig.IntCurveK / (BattleConfig.IntCurveK + tgt.BaseDef);
            if (ratio < BattleConfig.IntMinRatio) ratio = BattleConfig.IntMinRatio;
            amount *= ratio;
        }
        // 承伤修正（资质词条"嗜战如命"的代价面）：>1 = 更脆；放在个体修正链的最前
        if (tgt.DmgTakenMul != 1.0) amount *= tgt.DmgTakenMul;
        // 战斗疲劳（软狂暴）：30s 后承伤每秒 +3%
        if (T > BattleConfig.FatigueStart) amount *= 1 + BattleConfig.FatigueRate * (T - BattleConfig.FatigueStart);
        // 闪避：结算前判定（被沉默不可闪）
        if (!tgt.Silenced(T))
            foreach (var s in tgt.Skills)
                if (s.Src == SkillSrc.OnHit && s.Eff == SkillEff.Dodge && s.CondMet(tgt, Rng, false))
                { St.Dodges++; return 0; }

        // 「命中计数体」（飞剑：2 层 / 每次受击 -1，2026-10-16 口径）：
        //   无视伤害量，一次命中只扣固定值 —— 免疫一发大伤害秒杀，但被高频多次命中克制得极狠。
        //   放在护盾结算之前，使 rem / AccumDmg 等下游口径一致。
        if (tgt.DmgPerHit > 0) amount = Math.Min(amount, tgt.DmgPerHit);

        double rem = amount;
        if (tgt.Shield > 0) { double ab = Math.Min(tgt.Shield, rem); tgt.Shield -= ab; rem -= ab; }
        bool wasAlive = tgt.Alive;
        // 气血**下限夹 0**（2026-10-16 修）：原先无下限，阵亡后 Hp 会变成负数，例如 40−60=−20。
        //   后果不只是显示难看 —— 推图"波间延续血量"把终局 Hp 写进 BattleUnit.StartHp，
        //   而 StartHp 用 **−1 作"未设置"哨兵**、Clone() 以 `StartHp >= 0` 判定：
        //   负的 StartHp 会被误读成"未设置"→ 满血起手 → **阵亡者在下一波复活**。
        tgt.Hp = Math.Max(0, rem <= 0 ? tgt.Hp : tgt.Hp - rem);
        tgt.AccumDmg += amount;

        // 受击计数（2026-10-21 用户口径「一次行动只加一次受击」）：
        //   同一次行动内只有**第一段伤害**推 Hits，其余段只结算伤害与承伤。
        //   同时，受击武学也只在这第一段判定一次 —— 否则 Hits 停在 6、CondN=3 时，
        //   后续每一段都满足 6%3==0，反而会在一次行动里连放 N 次。
        bool countedHit = tgt.HitActionId != _actSeq;
        if (countedHit) { tgt.HitActionId = _actSeq; tgt.Hits++; }
        if (tgt.Slot is >= 1 and <= 4) St.DamageTaken[tgt.Slot] += amount;

        if (countedHit && tgt.Alive && ChainDepth < BattleConfig.ChainMax)
        {
            ChainDepth++;
            if (!tgt.Silenced(T))
                foreach (var s in tgt.Skills.ToArray())
                    if (s.Src == SkillSrc.OnHit && s.Eff != SkillEff.Dodge && s.CondMet(tgt, Rng, false)) Fire(tgt, s);
            ChainDepth--;
        }
        if (tgt.Alive && !tgt.Silenced(T))
            foreach (var s in tgt.Skills)
                if (s.Src == SkillSrc.OnHpBelow && !tgt.FiredOnce.Contains(s) && _hpThresh.TryGetValue(s, out var pct) && tgt.Hp / tgt.BaseHp <= pct)
                { tgt.FiredOnce.Add(s); Fire(tgt, s); }

        if (wasAlive) EmitDeathIfNeeded(tgt);
        return amount;
    }

    public void Fire(BattleUnit u, BattleSkill s)
    {
        Bus.RaiseSkill(u, s.Name);
        List<BattleUnit> locked = null;
        Apply(u, s, ref locked);
        int hits = 1;
        double p = Math.Min(BattleConfig.ReplayPMax, s.ReplayP);
        int cap = Math.Min(BattleConfig.ReplayCMax, s.ReplayC);
        while (hits < cap && Rng.NextDouble() < p)
        {
            St.ReplayExtra++;
            Apply(u, s, ref locked);
            hits++;
        }
        if (hits > St.MaxChainSeen) St.MaxChainSeen = hits;
        if (hits >= 5) St.Chains5Plus++;
    }

    void Apply(BattleUnit u, BattleSkill s, ref List<BattleUnit> locked)
    {
        switch (s.Eff)
        {
            case SkillEff.Dmg:
            {
                List<BattleUnit> targets;
                if (locked != null) targets = locked.Where(x => x.Alive).ToList();
                else { targets = PickTargets(u, s); if (s.ReplayLocked) locked = targets; }
                double dealt = 0;
                bool aoe = s.Tgt == SkillTgt.All;
                foreach (var t in targets) if (t.Alive)
                {
                    double dmg = AmountOf(u, s);
                    if (aoe && t.IsSummon) dmg *= BattleConfig.SummonAoeDr;   // 召唤物群体减伤（框架 §9）
                    double real = DealDamage(u, t, dmg, s.DmgKind);
                    dealt += real;
                    // 补全：技能伤害此前未抛事件（普攻才有）—— 现在带上武学名，飘字与统计都能归因
                    Bus.RaiseDamage(u, t, real, s.Name);
                    if (s.SilenceAlso && t.Alive) t.SilenceUntil = T + 5;
                }
                if (s.Vamp && u.Alive && dealt > 0) { double h = dealt * 0.5; u.Hp = Math.Min(u.BaseHp, u.Hp + h); St.VampHeal += h; Bus.RaiseHeal(u, u, h, null); }
                break;
            }
            case SkillEff.Heal:
            {
                // 治疗量与护盾同口径：卡面写"×攻击"就乘此刻攻击，写"气血%"则已是绝对量（见 AmountOf）
                double heal = AmountOf(u, s);
                foreach (var t in PickAlly(u, s)) if (t.Alive)
                {
                    double before = t.Hp;
                    t.Hp = Math.Min(t.BaseHp, t.Hp + heal);
                    double got = t.Hp - before;
                    if (got > 0) Bus.RaiseHeal(u, t, got, s.Name);   // 带上武学名，便于"按武学统计治疗"
                }
                break;
            }
            case SkillEff.Shield:
            {
                double add = AmountOf(u, s);
                foreach (var t in PickAlly(u, s)) if (t.Alive) { t.Shield += add; Bus.RaiseShield(u, t, add); }
                break;
            }
            case SkillEff.Summon:
                AddSummon(u, s);
                if (s.SummonOrder) OrderSummons(u, s);   // 万剑归宗：召唤即号令己方同类型召唤物齐射
                break;
            case SkillEff.Weight:
                foreach (var t in PickTargets(u, s)) if (t.Alive) t.WBuf.Add((s.Mult, T + 10));
                break;
            case SkillEff.SpdMod:
                foreach (var t in PickTargets(u, s)) if (t.Alive) t.SpdBuf.Add((s.Mult, T + 10));
                break;
            case SkillEff.AtkMod:
                foreach (var t in PickTargets(u, s)) if (t.Alive) t.AtkBuf.Add((s.Mult, T + 10));
                break;
            case SkillEff.Poison:
                foreach (var t in PickTargets(u, s))
                    if (t.Alive)
                    {
                        // 层数上限取**施毒者**的加成（百淬 4 件）
                        t.PoisonStacks = Math.Min(BattleConfig.PoisonMaxStack + u.DotStackBonus, t.PoisonStacks + 1);
                        t.PoisonFrom = u;
                        if (t.NextPoisonTick < 0) t.NextPoisonTick = T + BattleConfig.PoisonTick;
                    }
                break;
            case SkillEff.Burn:
                foreach (var t in PickTargets(u, s))
                    if (t.Alive) { t.BurnFrom = u; t.BurnUntil = T + BattleConfig.BurnDur; if (t.NextBurnTick < 0) t.NextBurnTick = T + BattleConfig.BurnTick; }
                break;
            case SkillEff.Silence:
                foreach (var t in PickTargets(u, s)) if (t.Alive) t.SilenceUntil = T + 5;
                break;
            case SkillEff.Growth:
                u.AtkMult *= 1.03;
                break;
            case SkillEff.Shield2Dmg:
            {
                double d = u.Shield; u.Shield = 0;
                var foes = AliveOf(Foes(u)).ToList();
                if (d > 0 && foes.Count > 0) DealDamage(u, WeightedPick(foes), d, DamageKind.Internal);
                break;
            }
            case SkillEff.ReflectAccum:
            {
                double rd = u.AccumDmg * s.Mult; u.AccumDmg = 0;
                var foes = AliveOf(Foes(u)).ToList();
                if (rd > 0 && foes.Count > 0) DealDamage(u, WeightedPick(foes), rd, DamageKind.Internal);
                break;
            }
            case SkillEff.ShieldKeep:
            {
                // 护盾保持率修正：Mult 已是小数（装配期把"百分点"除 100）。
                // 只加不清零 —— 想表达"变差"就配负数，clamp 由 Act() 统一做。
                foreach (var t in PickAlly(u, s)) if (t.Alive) t.ShieldKeep += s.Mult;
                break;
            }
            case SkillEff.Dodge:
                break;
        }
    }

    /// <summary>
    /// 技能效果的**结算数值**——统一入口，避免"某类效果忘了乘攻击"。
    /// <see cref="BattleSkill.MultOnAtk"/> = true → <c>施法者攻击 × Mult</c>（倍数语义）；
    /// false → 直接用 Mult（绝对量语义，如"气血上限%"已在装配时折算）。
    /// </summary>
    double AmountOf(BattleUnit u, BattleSkill s) => s.MultOnAtk ? u.EffAtk(T) * s.Mult : s.Mult;

    List<BattleUnit> PickTargets(BattleUnit u, BattleSkill s)
    {
        var foes = AliveOf(Foes(u)).ToList();
        switch (s.Tgt)
        {
            case SkillTgt.All: return foes;
            case SkillTgt.HighestW: return foes.Count == 0 ? foes : new List<BattleUnit> { foes.OrderByDescending(x => x.EffW(T)).First() };
            case SkillTgt.LowestHpFoe: return foes.Count == 0 ? foes : new List<BattleUnit> { foes.OrderBy(x => x.Hp).First() };
            default: return foes.Count == 0 ? foes : new List<BattleUnit> { WeightedPick(foes) };
        }
    }

    List<BattleUnit> PickAlly(BattleUnit u, BattleSkill s)
    {
        var allies = AliveOf(Allies(u)).ToList();
        if (allies.Count == 0) return allies;
        return s.Tgt switch
        {
            SkillTgt.Self => new List<BattleUnit> { u },
            SkillTgt.LowestHpAlly => new List<BattleUnit> { allies.OrderBy(x => x.Hp).First() },
            SkillTgt.All => allies,
            _ => new List<BattleUnit> { u }
        };
    }

    void AddSummon(BattleUnit owner, BattleSkill src)
    {
        if (owner == null || !owner.Alive) return;

        // 类型规格：血攻随召唤者，具体档位（含同时上限）来自 SummonConfig 表；表缺失则走引擎兜底
        SummonSpec spec = src?.Summon ?? SummonSpec.Fallback(src?.SummonKind);

        // "一次召唤几只"（2026-10-16 口径：御剑术一次召唤 2 飞剑）——
        // 与 SummonMax（同时存活上限）是两件事；逐只走同一套上限检查，超限即停。
        int times = Math.Max(1, src?.SummonCount ?? 1);
        for (int k = 0; k < times; k++)
            if (!SpawnSummon(owner, src, spec)) break;
    }

    /// <summary>召唤一只。返回 false = 被上限挡住（外层全队上限 / 内层类型或武学上限）。</summary>
    bool SpawnSummon(BattleUnit owner, BattleSkill src, SummonSpec spec)
    {
        var team = Allies(owner);

        // 两道上限（2026-10-16）：
        //   ① 外层 = 全队召唤位（BattleConfig.SummonCap）—— 防多张召唤卡把战场铺满
        //   ② 内层 = **该类型的档位上限**（SummonSpec.Max），武学可用 BattleSkill.SummonMax 覆盖
        //      没有内层时触发最快的卡会"先到先得"独占全部召唤位，其余召唤卡形同不存在
        int live = 0, mine = 0;
        foreach (var x in team)
        {
            if (!x.IsSummon || !x.Alive) continue;
            live++;
            if (src != null && ReferenceEquals(x.SummonFrom, src)) mine++;
        }
        if (live >= BattleConfig.SummonCap) return false;
        int cap = src != null && src.SummonMax > 0 ? src.SummonMax : spec.Max;
        if (cap > 0 && mine >= cap) return false;

        // 气血：FixedHp > 0 = 固定层数（不乘召唤者、不吃全局 HpScale）；否则走比例血
        // 资质词条"百兽之王"（SummonHpMul）只作用于比例血型 —— 计数体（飞剑）的"2 层"是机制不是血池
        double hp = spec.FixedHp > 0
            ? spec.FixedHp
            : Math.Max(1, owner.BaseHp * spec.HpRatio * owner.SummonHpMul);

        var s = new BattleUnit
        {
            Name = spec.Name,
            Slot = 0,
            IsSummon = true,
            Ttl = spec.Ttl,
            BaseW = spec.Weight,
            BaseHp = hp,
            BaseAtk = owner.BaseAtk * spec.AtkRatio,   // 血攻随召唤者（"和武侠相关"）
            BaseSpd = spec.Speed(owner.EffSpd(T)),
            DmgPerHit = spec.DmgPerHit,
        };
        s.Hp = s.BaseHp;
        s.SummonFrom = src;
        s.Summoner = owner;   // 召唤者引用：统计时把召唤物伤害/承伤归入"召唤者 + 召唤来源武学"
        team.Add(s);
        St.Summons++;
        Bus.RaiseSummon(owner, s);
        return true;
    }

    /// <summary>
    /// **召唤物类型键**（用于"同类型"判定）：优先取规格里的 Kind，回退到武学上的 SummonKind。
    /// </summary>
    static string KindOf(BattleSkill s)
        => s?.Summon != null && !string.IsNullOrEmpty(s.Summon.Kind) ? s.Summon.Kind : (s?.SummonKind ?? "");

    static string KindOf(BattleUnit u) => KindOf(u?.SummonFrom);

    /// <summary>
    /// 号令齐射（万剑归宗，2026-10-16 口径："让己方队伍所有飞剑全部攻击"）：
    /// 己方所有**同类型**且存活的召唤物，各立即做一次普攻。
    /// 语义是"命令"而非"额外行动" —— 不走 <c>Act()</c>，不增加行动计数、不触发其自身武学，
    /// 只结算一次普攻并抛伤害事件（UI 能看到齐射）。
    /// </summary>
    void OrderSummons(BattleUnit owner, BattleSkill src)
    {
        string kind = KindOf(src);
        if (string.IsNullOrEmpty(kind)) return;

        int ordered = 0;
        // 快照遍历：DealDamage 可能引发召唤（目标方的受击武学），虽然改的是对方队伍，
        // 但用快照可彻底避免"边遍历边改集合"。
        foreach (var m in Allies(owner).ToArray())
        {
            if (!m.IsSummon || !m.Alive) continue;
            if (KindOf(m) != kind) continue;

            var foes = AliveOf(Foes(m)).ToList();
            if (foes.Count == 0) break;

            var tgt = WeightedPick(foes);
            double amt = DealDamage(m, tgt, m.EffAtk(T));
            Bus.RaiseDamage(m, tgt, amt, null);
            ordered++;
        }
        if (ordered > 0) St.SummonOrders++;
    }

    void EmitDeathIfNeeded(BattleUnit u)    {
        if (!u.Alive && _deadNotified.Add(u))
        {
            Bus.RaiseDeath(u);
            MarkDeadSummon(u);       // 召唤物不留"尸体"（延迟到安全点移除）
        }
    }

    /// <summary>
    /// 登记一个已阵亡的召唤物，等待安全点从队伍里**真正移除**。
    /// 不能在遍历 A/B 的过程中直接改集合（会抛"集合已修改"），故一律延迟。
    /// </summary>
    void MarkDeadSummon(BattleUnit u)
    {
        if (u != null && u.IsSummon && !_pendingRemove.Contains(u)) _pendingRemove.Add(u);
    }

    /// <summary>
    /// 把已阵亡的召唤物移出 A/B。**只能在"没有正在遍历 A/B"的位置调用**（见 Run/Step/Advance 的调用点）。
    /// rationale（用户口径"召唤物死亡不保留尸体"）：索敌/治疗/上限计数虽已过滤 Alive，但尸体仍留在队伍列表里 ——
    /// UI 卡片不消失、召唤卡的行位计算把它算进去、每次 tick 还要白跑一次 TickUnit。
    /// </summary>
    void FlushRemovals()
    {
        if (_pendingRemove.Count == 0) return;
        foreach (var u in _pendingRemove) { A.Remove(u); B.Remove(u); }
        _pendingRemove.Clear();
    }
}
