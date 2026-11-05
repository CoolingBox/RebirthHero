// 文件：DropRule.cs
// 《轮回大侠》**战斗规则层 · 掉落** —— 击杀产出与清层奖励的**唯一口径**
//
// 口径来源（GDD §3.2 + 用户 2026-10-15）：
//   · "击杀就可立即获得经验资源" —— 修为与银两**即时进账**，不等战斗结束
//   · 修为只累进池子，**升级在结算时统一做**（`Cultivation.SettleLevelUps`）——
//     战斗中途改等级会让已经建好的单位与新等级脱节
//   · 材料是确定性的（每杀必得、按层段定品阶），装备是概率的（偶发、随层变好）：
//     两种节奏并存才不会让掉落变成纯打卡
//
// 为什么这些乘区在**玩家侧**乘而不是改全局常量：
//   `CultivationConfig` / `EquipDropper` 的常量敌人也走；天赋只属于玩家，
//   只能在玩家侧的调用点乘（`TalentBonus` 的四个乘区都在这里落地）。
//
// 本文件不引用 Godot（无头探针可直接编译）。

using System;
using System.Collections.Generic;

/// <summary>一次击杀的产出（已入账）。面板拿它拼飘字，不参与任何计算。</summary>
public struct KillDrop
{
    /// <summary>本场击杀给**每名**参战者的修为。</summary>
    public double ExpPerHead;
    /// <summary>整队共享一份的银两。</summary>
    public double Silver;
    /// <summary>材料品阶（跟层段走）。</summary>
    public int MatTier;
    /// <summary>材料数量。</summary>
    public int MatCount;
    /// <summary>掉出来的装备模板（null = 这次没掉）。</summary>
    public EquipConfig Equip;
    /// <summary>该装备掷出的随机词条 Id（可能为空）。</summary>
    public List<int> Affixes;
}

public static class DropRule
{
    /// <summary>
    /// 击杀一个敌方单位 → 四本账一次性入账。
    /// 调用方只需要保证"死的是敌人、不是召唤物"（召唤物不掉落，由面板提前 return）。
    /// </summary>
    public static KillDrop GrantKill(BattleRuleContext ctx, int layer)
    {
        KillDrop drop = new KillDrop();
        if (!ctx.Ready)
        {
            return drop;
        }
        if (layer < 1)
        {
            layer = 1;
        }

        // 天赋的经济乘区（2026-11-02）：一次取出，四处复用，避免每个字段各查一次 Bonus()
        TalentBonus tb = Talent.Bonus();

        // ① 修为：每名参战者各一份，只累进池子、不升级
        drop.ExpPerHead = CultivationConfig.GainPerKill(layer) * tb.CultMul;
        ctx.Cult.AddExp(ctx.RosterIds(), drop.ExpPerHead);

        // ② 银两：整队一份账户
        drop.Silver = ctx.Money.GainForKill(layer, tb.SilverMul);

        // ③ 材料：确定性掉落，品阶按层段
        drop.MatTier = MaterialCatalog.TierOf(layer);
        drop.MatCount = MaterialConfig.MatMobDrop;
        ctx.Materials.Add(drop.MatTier, drop.MatCount);

        // ④ 装备：概率掉落 + 稀有度掷词条（同件不出同类效果，见 EquipDropper）
        if (EquipDropper.ShouldDrop(ctx.Rng, tb.DropAdd))
        {
            EquipConfig tpl = EquipDropper.Roll(layer, ctx.Rng, ctx.EquipPool, tb.RarityBoost);
            if (tpl != null)
            {
                List<int> affixes = EquipDropper.RollAffixes(tpl.Rarity, ctx.Rng, ctx.AffixPool);
                ctx.Equips.Add(tpl.Id, affixes);
                drop.Equip = tpl;
                drop.Affixes = affixes;
            }
        }

        return drop;
    }

    /// <summary>
    /// 打穿一整层（收尾波获胜）的额外材料 —— 一次清层只发一次。
    /// 与击杀掉落分开计价：击杀奖励"多杀多得"，清层奖励"翻页的仪式感"。
    /// </summary>
    /// <returns>发出去的件数（0 = 状态机未就位）。</returns>
    public static int GrantClearBonus(BattleRuleContext ctx, int layer)
    {
        if (!ctx.Ready)
        {
            return 0;
        }
        if (layer < 1)
        {
            layer = 1;
        }

        int tier = MaterialCatalog.TierOf(layer);
        int count = MaterialConfig.MatClearBonus;
        ctx.Materials.Add(tier, count);
        return count;
    }
}
