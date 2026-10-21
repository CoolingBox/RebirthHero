// 文件：EquipTuning.cs
// 《轮回大侠》P1 装备加成 —— **套装 + 随机词条 → 一组参数修正**（纯 C#，可探针验证）
//
// 纪律（《装备与套装 v0.2》§二 + 数值框架 §9）：
//   **只做参数修正，不开新乘区。** 落到引擎的只有三样东西：
//     ① 基础属性百分比的乘算（装备乘区内部，和资质/等级同一条线）
//     ② 武学既有参数（CondN / CondP / ReplayP / ReplayC / Mult / 召唤存在时间）
//     ③ 引擎已有的两个"参数位"（CounterOffset 初始计数、DotMul/DotStackBonus DoT 系数）
//   加新乘区是被砍掉的羁绊系统的做法，这里不做。
//
// 预算（为什么是这些数）：
//   · 装备侧总增量封顶 ~30%：套装 20（降龙两档）+ 散件词条 10（重放），其余词条按类别各自封顶
//   · 散件词条只承担 1/3 的长尾增量，主体必须是**可预期的套装收集目标** —— 否则随机词条喧宾夺主，
//     玩家的目标感从"凑齐 4 件"退化成"刷到好词条"，这是刷子游戏和构筑游戏的分界线
//   · 套装件的基础数值刻意 ×0.92（见 EquipConfig.xlsx 备注）：数值略低换套装效果，
//     让"不凑套的散件"在中期仍有存在意义（即战力 vs 成长潜力）

using System;
using System.Collections.Generic;

/// <summary>已激活/未激活的一套（UI 展示用）。</summary>
public struct SetState
{
    public int SetId;
    public string Name;
    public int Pieces;
    public bool Active2;
    public bool Active4;
}

/// <summary>一个侠客身上"装备侧"的全部修正（套装 + 词条合计，已封顶）。</summary>
public sealed class EquipTuning
{
    public double AtkPct;
    public double HpPct;
    public double SpdPct;

    public double ProcAction;        // 行动类触发概率（百分点）
    public double ProcHit;           // 受击类触发概率（百分点）

    public int CondDownAction;       // 行动类需求次数 −N
    public int CondDownHit;          // 受击类需求次数 −N
    public int CondDownSummon;       // 召唤类需求次数 −N

    public double ReplayPct;         // 重放概率（百分点）
    public int ReplayLimit;          // 重放上限 +N

    public double ShieldPct;         // 护盾效果 +%
    public double ShieldKeepPct;     // 护盾保持率 + 百分点（行动时衰减得更少）
    public double CannotAtkPct;      // 不能普攻武学倍率 +%
    public int CounterUp;            // 开战计数 +N
    public double DotPct;            // DoT 伤害 +%
    public int DotStackUp;           // 叠毒层数上限 +N
    public double SummonTtl;         // 召唤物存在时间 +秒
    public double WeightDelta;       // 受击权重增量

    public readonly List<SetState> Sets = new List<SetState>();

    public bool HasAny;

    /// <summary>该侠客四栏里属于某套装的件数（UI 用，不参与计算）。</summary>
    public int PiecesOf(int setId)
    {
        for (int i = 0; i < Sets.Count; i++)
            if (Sets[i].SetId == setId) return Sets[i].Pieces;
        return 0;
    }
}

public static class EquipSets
{
    // ---------- 封顶（散件词条侧；套装不吃这些上限，它是固定的收集目标）----------
    /// <summary>[PLACEHOLDER] 攻/血/速 单项上限。三条 4% 就到顶 = 需要"专精"才吃得满。</summary>
    public const double CapStatPct = 12;
    /// <summary>[PLACEHOLDER] 触发概率上限。超过 6% 会让 CondP 卡整体上移一档。</summary>
    public const double CapProcPct = 6;
    /// <summary>重放概率上限 +10（与套装 20 合计 30，doc 定稿口径）。</summary>
    public const double CapReplayPct = 10;
    /// <summary>重放上限 +4（框架 §9：p≤90%、C≤25）。</summary>
    public const int CapReplayLimit = 4;
    /// <summary>[PLACEHOLDER] 护盾效果上限，避免"高护盾+治疗"双叠变成无敌。</summary>
    public const double CapShieldPct = 30;
    /// <summary>
    /// 护盾保持率上限 +20 点（基准 50 + 20 = 70，离引擎硬上限 85 还留 15 点给武学/资质）。
    /// rationale：保持率是**乘在护盾存量上的复利项**，给满会让护盾变成第二血条；
    /// 装备侧只给到 70，剩下的空间留给"专门做护盾流"的武学与资质。
    /// </summary>
    public const double CapShieldKeepPct = 20;
    /// <summary>开战计数上限 +2（与破军 2 件同量级，再高就等于直接触发一次）。</summary>
    public const int CapCounterUp = 2;
    /// <summary>需求次数最多减 2（引擎侧还有 floor 1 兜底）。</summary>
    public const int CapCondDown = 2;

    /// <summary>
    /// 解析某侠客的装备加成。cfg/affix/set 三个 lookup 允许为 null（缺表时退化为无加成）。
    /// </summary>
    public static EquipTuning Resolve(EquipBag bag, int heroId,
                                      Func<int, EquipConfig> cfgLookup,
                                      Func<int, EquipAffixConfig> affixLookup,
                                      Func<int, EquipSetConfig> setLookup)
    {
        EquipTuning t = new EquipTuning();
        if (bag == null || heroId <= 0) return t;

        int[] slots = bag.SlotsOf(heroId);

        // 词条先落到局部变量，最后统一封顶再加进 t（套装不进封顶）
        double atk = 0, hp = 0, spd = 0, procA = 0, procH = 0;
        double replayP = 0, shield = 0, weight = 0, ttl = 0, shieldKeep = 0;
        int replayC = 0, counter = 0, condA = 0, condH = 0, condSummon = 0;

        Dictionary<int, int> setCount = new Dictionary<int, int>();

        for (int i = 0; i < EquipBag.SlotCount; i++)
        {
            if (slots[i] == 0) continue;

            EquipItem item = bag.Find(slots[i]);
            if (item == null) continue;

            EquipConfig cfg = cfgLookup != null ? cfgLookup(item.ConfigId) : null;
            if (cfg != null && cfg.SetId > 0)
            {
                int n;
                setCount.TryGetValue(cfg.SetId, out n);
                setCount[cfg.SetId] = n + 1;
            }

            // —— 随机词条 ——
            if (item.AffixIds == null) continue;
            foreach (int affixId in item.AffixIds)
            {
                EquipAffixConfig a = affixLookup != null ? affixLookup(affixId) : null;
                if (a == null) continue;

                switch (a.Effect)
                {
                    case EquipAffixEffect.AtkPct: atk += a.Value; break;
                    case EquipAffixEffect.HpPct: hp += a.Value; break;
                    case EquipAffixEffect.SpdPct: spd += a.Value; break;
                    case EquipAffixEffect.ProcAction: procA += a.Value; break;
                    case EquipAffixEffect.ProcHit: procH += a.Value; break;
                    case EquipAffixEffect.CondDownAction: condA += (int)a.Value; break;
                    case EquipAffixEffect.ReplayPct: replayP += a.Value; break;
                    case EquipAffixEffect.ReplayLimit: replayC += (int)a.Value; break;
                    case EquipAffixEffect.ShieldPct: shield += a.Value; break;
                    case EquipAffixEffect.WeightDelta: weight += a.Value; break;
                    case EquipAffixEffect.CounterUp: counter += (int)a.Value; break;
                    case EquipAffixEffect.ShieldKeepPct: shieldKeep += a.Value; break;
                    default: break;
                }
            }
        }

        // —— 套装（2 件 / 4 件各一档，可叠加）——
        foreach (KeyValuePair<int, int> kv in setCount)
        {
            EquipSetConfig sc = setLookup != null ? setLookup(kv.Key) : null;
            if (sc == null) continue;

            SetState st = new SetState();
            st.SetId = kv.Key;
            st.Name = sc.Name;
            st.Pieces = kv.Value;

            if (kv.Value >= 2) { ApplySetEffect(t, sc.Effect2, sc.Value2); st.Active2 = true; }
            if (kv.Value >= 4) { ApplySetEffect(t, sc.Effect4, sc.Value4); st.Active4 = true; }
            t.Sets.Add(st);
        }

        // —— 词条封顶后并入 ——
        t.AtkPct += Clamp(atk, 0, CapStatPct);
        t.HpPct += Clamp(hp, 0, CapStatPct);
        t.SpdPct += Clamp(spd, 0, CapStatPct);
        t.ProcAction += Clamp(procA, 0, CapProcPct);
        t.ProcHit += Clamp(procH, 0, CapProcPct);
        t.ReplayPct += Clamp(replayP, 0, CapReplayPct);
        t.ReplayLimit += ClampInt(replayC, 0, CapReplayLimit);
        t.ShieldPct += Clamp(shield, 0, CapShieldPct);
        t.ShieldKeepPct += Clamp(shieldKeep, 0, CapShieldKeepPct);
        t.WeightDelta += weight;
        t.CounterUp += ClampInt(counter, 0, CapCounterUp);
        t.CondDownAction += ClampInt(condA, 0, CapCondDown);
        t.CondDownHit += ClampInt(condH, 0, CapCondDown);
        t.CondDownSummon += ClampInt(condSummon, 0, CapCondDown);
        t.SummonTtl += ttl;

        t.HasAny = AnyNonZero(t);
        return t;
    }

    static void ApplySetEffect(EquipTuning t, SetEffect effect, double value)
    {
        switch (effect)
        {
            case SetEffect.ActionCondDown: t.CondDownAction += (int)value; break;
            case SetEffect.ActionProcUp: t.ProcAction += value; break;
            case SetEffect.ReplayUp: t.ReplayPct += value; break;
            case SetEffect.CounterUp: t.CounterUp += (int)value; break;
            case SetEffect.CannotAtkMult: t.CannotAtkPct += value; break;
            case SetEffect.HitCondDown: t.CondDownHit += (int)value; break;
            case SetEffect.ShieldUp: t.ShieldPct += value; break;
            case SetEffect.SummonTtl: t.SummonTtl += value; break;
            case SetEffect.SummonCondDown: t.CondDownSummon += (int)value; break;
            case SetEffect.DotUp: t.DotPct += value; break;
            case SetEffect.DotStackUp: t.DotStackUp += (int)value; break;
            case SetEffect.ShieldKeepUp: t.ShieldKeepPct += value; break;
            default: break;
        }
    }

    // ---------- 应用 ----------

    /// <summary>
    /// 属性部分：**必须在召唤武学代价（LockedHp）计算之前**调用，
    /// 否则"最大生命 −15%"会按旧基数算，血条与锁死段会对不上。
    /// </summary>
    public static void ApplyStats(EquipTuning t, BattleUnit u)
    {
        if (t == null || u == null) return;

        if (t.HpPct != 0) { u.BaseHp *= 1 + t.HpPct / 100.0; u.Hp = u.BaseHp; }
        if (t.AtkPct != 0) u.BaseAtk *= 1 + t.AtkPct / 100.0;
        if (t.SpdPct != 0) u.BaseSpd *= 1 + t.SpdPct / 100.0;
        if (t.WeightDelta != 0) u.BaseW = Math.Max(BattleConfig.WFloor, u.BaseW + t.WeightDelta);

        if (t.DotPct != 0) u.DotMul = 1 + t.DotPct / 100.0;
        if (t.DotStackUp != 0) u.DotStackBonus = t.DotStackUp;

        // 护盾保持率：百分点加在 0.5 的基准上（50 + 20 = 70 → 行动后留 70%）。
        // 不在这里 clamp —— 引擎 Act() 有唯一的 clamp 点，避免两处上限不一致。
        if (t.ShieldKeepPct != 0) u.ShieldKeep += t.ShieldKeepPct / 100.0;
    }

    /// <summary>武学参数部分：装配完技能后调用（技能列表必须已经建好）。</summary>
    public static void ApplySkills(EquipTuning t, List<BattleSkill> skills)
    {
        if (t == null || skills == null) return;

        for (int i = 0; i < skills.Count; i++)
            ApplyToSkill(t, skills[i]);
    }

    static void ApplyToSkill(EquipTuning t, BattleSkill sk)
    {
        if (sk == null) return;

        // ① 需求次数（只对"条件=次数"的武学有意义）
        if (sk.Cond == "count" && sk.CondN > 0)
        {
            int down = 0;
            if (sk.Src == SkillSrc.OnAction) down = t.CondDownAction;
            else if (sk.Src == SkillSrc.OnHit) down = t.CondDownHit;
            if (sk.Eff == SkillEff.Summon && t.CondDownSummon > down) down = t.CondDownSummon;

            if (down > 0)
            {
                int n = sk.CondN - down;
                sk.CondN = n < 1 ? 1 : n;          // 下限 1：0 会让 "Counter % 0" 直接崩
            }
        }

        // ② 触发概率（chance / combo 两种都用 CondP）
        if (sk.Cond == "chance" || sk.Cond == "combo")
        {
            double add = sk.Src == SkillSrc.OnHit ? t.ProcHit : t.ProcAction;
            if (add != 0)
            {
                double p = sk.CondP + add / 100.0;
                sk.CondP = p > 0.95 ? 0.95 : p;    // 概率封顶 95%，保留"不一定"的张力
            }
        }

        // ③ 重放
        if (t.ReplayPct != 0)
        {
            double p = sk.ReplayP + t.ReplayPct / 100.0;
            sk.ReplayP = p > 0.90 ? 0.90 : p;      // 框架 §9：p ≤ 90%
        }
        if (t.ReplayLimit != 0)
        {
            int c = sk.ReplayC + t.ReplayLimit;
            sk.ReplayC = c > 25 ? 25 : c;          // 框架 §9：C ≤ 25
        }

        // ④ 护盾效果
        if (sk.Eff == SkillEff.Shield && t.ShieldPct != 0)
            sk.Mult *= 1 + t.ShieldPct / 100.0;

        // ⑤ "不能普攻"武学倍率（破军 4 件）：走武学倍率，不开新乘区
        if (sk.CannotAttack && t.CannotAtkPct != 0)
            sk.Mult *= 1 + t.CannotAtkPct / 100.0;

        // ⑥ 开战计数（破军 2 件 / "先手"词条）：引擎侧参数位，不是"额外行动"
        if (t.CounterUp != 0)
            sk.CounterOffset += t.CounterUp;

        // ⑦ 召唤物存在时间：Summon 是共享的规格对象，**必须克隆再改**，
        //    否则给一个侠客加时长会污染整张表（所有召唤者一起变长）
        if (t.SummonTtl != 0 && sk.Summon != null)
            sk.Summon = sk.Summon.WithTtl(sk.Summon.Ttl + t.SummonTtl);
    }

    // ---------- 工具 ----------

    static double Clamp(double v, double lo, double hi)
    {
        if (v < lo) return lo;
        if (v > hi) return hi;
        return v;
    }

    static int ClampInt(int v, int lo, int hi)
    {
        if (v < lo) return lo;
        if (v > hi) return hi;
        return v;
    }

    static bool AnyNonZero(EquipTuning t)
    {
        return t.AtkPct != 0 || t.HpPct != 0 || t.SpdPct != 0
            || t.ProcAction != 0 || t.ProcHit != 0
            || t.CondDownAction != 0 || t.CondDownHit != 0 || t.CondDownSummon != 0
            || t.ReplayPct != 0 || t.ReplayLimit != 0
            || t.ShieldPct != 0 || t.ShieldKeepPct != 0 || t.CannotAtkPct != 0 || t.CounterUp != 0
            || t.DotPct != 0 || t.DotStackUp != 0 || t.SummonTtl != 0
            || t.WeightDelta != 0;
    }
}
