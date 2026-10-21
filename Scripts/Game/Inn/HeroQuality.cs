// 文件：HeroQuality.cs
// 《轮回大侠》M2.3 客栈 —— **一名侠客在本世的资质**（《侠客与资质》§二）
//
// 三个随机维度（每世开始，由客栈对每名可招募侠客各掷一次）：
//   ① 星级 ★1~★5（50/30/12/6/2%）—— **全局乘数**，★1=×1.0、每星 +15%
//   ② 成长率 HP/ATK/SPD **三维各自独立** 均匀掷 −20%~+30%（单维可为负 → "瘸腿天才"）
//   ③ 资质词条 0/1/2 个（40/45/15%）—— 池见 `QualityConfig` 表
//
// 所有系数走 `InnConfig`（= TuningConfig 总账表注入），**不在这里写死数值**。

using System;
using System.Collections.Generic;

public sealed class HeroQuality
{
    public int HeroId;
    public int Star = 1;                    // ★1~★5
    public double HpPct;                    // 成长率（%）
    public double AtkPct;
    public double SpdPct;
    public readonly List<int> AffixIds = new List<int>();   // 词条（QualityConfig.Id）
    public bool Recruited;                  // 当世已招募（锁定，不可退换）
    public bool IsBase;                     // 基准版：开局自带的初始侠客（非客栈招募，资质★1无词条）

    // —— 词条派生的加成（在 ApplyTo 里按词条算出，供装配期/引擎使用）——
    /// <summary>治疗量加成（词条"妙手回春"，%）：装配技能时乘进**治疗类**武学的数值。</summary>
    public double HealPct;
    /// <summary>武学倍率加成（词条"剑心通明"）列表：每条记录(适用相性, 百分比)。</summary>
    public readonly List<MulEntry> MulBonus = new List<MulEntry>();

    /// <summary>武学倍率加成的一项：Cat=适用相性（0=不限），Pct=百分比。</summary>
    public struct MulEntry
    {
        public Category Cat;
        public double Pct;
    }

    /// <summary>星级全局乘数：★1=×1.0，每星 +<see cref="InnConfig.StarStep"/>。
    /// **不作用于 SPD**（2026-10-16 口径"高星行动频率去掉"）—— 速度会被行动频率二次放大，不该叠在一个 2% 概率的乘数上。</summary>
    public double StarMul
    {
        get { return 1 + (Star - 1) * InnConfig.StarStep; }
    }

    public string StarText
    {
        get { return new string('★', Math.Clamp(Star, 1, 5)); }
    }

    /// <summary>基准版资质（开局初始侠客用：★1 / 无成长率 / 无词条 / IsBase=true）。</summary>
    public static HeroQuality Base(int heroId)
    {
        var q = new HeroQuality();
        q.HeroId = heroId;
        q.Star = 1;
        q.IsBase = true;
        return q;
    }

    /// <summary>
    /// 读档（M2.6）：按存档里的值原样重建一份资质。
    /// 只恢复"掷骰结果"（星级/三维成长率/词条/是否已招），派生加成（HealPct/MulBonus）在 ApplyTo 时按词条重算。
    /// </summary>
    public static HeroQuality Restore(int heroId, int star, double hpPct, double atkPct, double spdPct,
                                      IEnumerable<int> affixIds, bool recruited, bool isBase)
    {
        HeroQuality q = new HeroQuality();
        q.HeroId = heroId;
        q.Star = Math.Clamp(star, 1, 5);
        q.HpPct = hpPct;
        q.AtkPct = atkPct;
        q.SpdPct = spdPct;
        q.Recruited = recruited;
        q.IsBase = isBase;

        if (affixIds != null)
            foreach (int id in affixIds)
                if (id > 0) q.AffixIds.Add(id);

        return q;
    }

    /// <summary>掷一份资质（确定性：传入 rng，便于复现/做分布验证）。</summary>
    public static HeroQuality Roll(int heroId, Random rng, IReadOnlyList<QualityConfig> affixPool)
    {
        var q = new HeroQuality();
        q.HeroId = heroId;
        q.Star = RollStar(rng);
        q.HpPct = RollStat(rng);
        q.AtkPct = RollStat(rng);
        q.SpdPct = RollStat(rng);

        int need = RollAffixCount(rng);
        if (affixPool != null && affixPool.Count > 0 && need > 0)
        {
            var cand = new List<int>();
            foreach (QualityConfig c in affixPool) cand.Add(c.Id);   // 全池（含待实装；待实装者由 UI 标注）
            for (int i = 0; i < need && cand.Count > 0; i++)
            {
                int k = rng.Next(cand.Count);
                q.AffixIds.Add(cand[k]);
                cand.RemoveAt(k);                                     // 不重复
            }
        }
        return q;
    }

    static int RollStar(Random rng)
    {
        double total = InnConfig.StarW1 + InnConfig.StarW2 + InnConfig.StarW3 + InnConfig.StarW4 + InnConfig.StarW5;
        double r = rng.NextDouble() * total;
        if ((r -= InnConfig.StarW1) < 0) return 1;
        if ((r -= InnConfig.StarW2) < 0) return 2;
        if ((r -= InnConfig.StarW3) < 0) return 3;
        if ((r -= InnConfig.StarW4) < 0) return 4;
        return 5;
    }

    static double RollStat(Random rng)
    {
        double v = InnConfig.StatRollMin + rng.NextDouble() * (InnConfig.StatRollMax - InnConfig.StatRollMin);
        return Math.Round(v, 1);
    }

    static int RollAffixCount(Random rng)
    {
        double total = InnConfig.AffixW0 + InnConfig.AffixW1 + InnConfig.AffixW2;
        double r = rng.NextDouble() * total;
        if ((r -= InnConfig.AffixW0) < 0) return 0;
        if ((r -= InnConfig.AffixW1) < 0) return 1;
        return 2;
    }

    /// <summary>
    /// 把资质作用到一个已建好的战斗单位上（**在 HeroBuilder 建完基准属性之后、装配技能之前**调用）。
    /// 星级乘数作用于 HP/ATK（**不含 SPD**）；成长率三维各自作用；词条按 <see cref="ApplyEffect"/> 落到对应位置。
    /// </summary>
    public void ApplyTo(BattleUnit u, Func<int, QualityConfig> affixLookup)
    {
        if (u == null) return;

        u.BaseHp *= StarMul * (1 + HpPct / 100.0);
        u.BaseAtk *= StarMul * (1 + AtkPct / 100.0);
        u.BaseSpd *= 1 + SpdPct / 100.0;          // ← 不吃星级（"高星行动频率去掉"）
        u.Hp = u.BaseHp;

        // 派生加成每次重算（资质对象跨场复用，累积会翻倍）
        HealPct = 0;
        MulBonus.Clear();

        if (affixLookup == null) return;
        foreach (int id in AffixIds)
        {
            QualityConfig a = affixLookup(id);
            if (a == null) continue;
            ApplyEffect(u, a, a.Effect1, a.Value1);
            ApplyEffect(u, a, a.Effect2, a.Value2);
        }
    }

    /// <summary>
    /// 词条效果落地。**四类一律已实装**：
    /// 直接改字段（攻/血/速/权重）／登记派生加成（武学倍率、治疗量，由 HeroBuilder 装配时乘）／
    /// 写引擎钩子字段（召唤物气血 <c>SummonHpMul</c>、承伤 <c>DmgTakenMul</c>）。
    /// </summary>
    void ApplyEffect(BattleUnit u, QualityConfig a, AffixEffect e, double v)
    {
        if (e == AffixEffect.None) return;
        switch (e)
        {
            case AffixEffect.AtkPct:
                u.BaseAtk *= 1 + v / 100.0;
                break;
            case AffixEffect.HpPct:
                u.BaseHp *= 1 + v / 100.0;
                u.Hp = u.BaseHp;
                break;
            case AffixEffect.SpdPct:
                u.BaseSpd *= 1 + v / 100.0;
                break;
            case AffixEffect.Weight:
                u.BaseW = Math.Max(BattleConfig.WFloor, u.BaseW + v);
                break;
            case AffixEffect.MulPct:
            {
                var me = new MulEntry();
                me.Cat = a.Category;   // 装配期乘武学倍率（按相性匹配）
                me.Pct = v;
                MulBonus.Add(me);
                break;
            }
            case AffixEffect.HealPct:
                HealPct += v;          // 装配期乘治疗量
                break;
            case AffixEffect.SummonHpPct:
                u.SummonHpMul *= 1 + v / 100.0;   // 引擎 SpawnSummon
                break;
            case AffixEffect.DmgTakenPct:
                u.DmgTakenMul *= 1 + v / 100.0;   // 引擎 DealDamage
                break;
            case AffixEffect.ShieldKeepPct:
                u.ShieldKeep += v / 100.0;        // 百分点加在 0.5 基准上；clamp 在引擎 Act()
                break;
            default:
                break;
        }
    }

    /// <summary>该词条的武学倍率加成是否适用于这张武学（词条相性为 0 = 不限，适用于全部武学）。</summary>
    public double MulBonusFor(Category skillCat)
    {
        double f = 1.0;
        foreach (MulEntry e in MulBonus)
        {
            if (e.Cat == 0 || e.Cat == skillCat) f *= 1 + e.Pct / 100.0;
        }
        return f;
    }
}
