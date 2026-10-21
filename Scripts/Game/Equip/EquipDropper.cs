// 文件：EquipDropper.cs
// 《轮回大侠》M2.5 装备掉落（纯 C#，可探针验证）
//
// 两层随机：先定稀有度（按层分档权重），再在同稀有度里定具体一件（4 栏等权）。
//   为什么分两层而不是"给每件装备一个权重直接抽"：
//   稀有度是玩家感知的最强信号（"出紫了"），必须能被单独调；混在一个权重表里，
//   改一级装备的掉率会顺带改变整套稀有度分布，调参时无法归因。
//
// 全部 [PLACEHOLDER · 需实机 playtest]：
//   验证路径 —— 记录玩家每 10 层的装备获取量与稀有度分布；
//   若"换装频率 < 每 2 层一次"，说明掉率偏低或套装成型太慢，先调 DropChance。

using System;
using System.Collections.Generic;

public static class EquipDropper
{
    /// <summary>每次击杀的掉落概率。[PLACEHOLDER] 8% → 平均 12.5 杀一件。</summary>
    public const double DropChance = 0.08;

    /// <summary>
    /// 稀有度权重表（行 = 层段，列 = 白/绿/蓝/紫/橙）。
    /// 随层推进，高档权重上升、白装下降 —— 让"刷更高层"同时是"更容易出好装备"。
    /// 31 层以后沿用最后一档（不再无限放宽，否则后期橙装贬值）。
    /// </summary>
    static readonly int[][] RarityWeights =
    {
        new int[] { 70, 25,  5, 0, 0 },   // 1~10 层
        new int[] { 45, 35, 17, 3, 0 },   // 11~20 层
        new int[] { 25, 35, 28, 10, 2 },  // 21~30 层
        new int[] { 10, 30, 35, 20, 5 },  // 31 层以上
    };

    /// <summary>按层取稀有度权重行。</summary>
    public static int[] WeightsOf(int layer)
    {
        if (layer <= 10) return RarityWeights[0];
        if (layer <= 20) return RarityWeights[1];
        if (layer <= 30) return RarityWeights[2];
        return RarityWeights[3];
    }

    /// <summary>该层会掉什么稀有度（1~5）。</summary>
    public static Rarity RollRarity(int layer, Random rng)
    {
        if (rng == null) rng = new Random();

        int[] w = WeightsOf(layer);
        int total = 0;
        for (int i = 0; i < w.Length; i++) total += w[i];

        if (total <= 0) return Rarity.Common;

        int roll = rng.Next(total);
        int acc = 0;
        for (int i = 0; i < w.Length; i++)
        {
            acc += w[i];
            if (roll < acc) return (Rarity)(i + 1);
        }

        return Rarity.Common;
    }

    /// <summary>
    /// 抽一件装备。pool = 全部装备模板（EquipConfig）。
    /// 该稀有度没有模板时逐档下探（避免高层出了橙装却因为没配橙装而空手）。
    /// 返回 null 表示池里完全没有可用模板。
    /// </summary>
    public static EquipConfig Roll(int layer, Random rng, List<EquipConfig> pool)
    {
        if (pool == null || pool.Count == 0) return null;
        if (rng == null) rng = new Random();

        Rarity rarity = RollRarity(layer, rng);

        for (int r = (int)rarity; r >= 1; r--)
        {
            List<EquipConfig> candidates = new List<EquipConfig>();
            for (int i = 0; i < pool.Count; i++)
            {
                EquipConfig c = pool[i];
                if (c != null && (int)c.Rarity == r) candidates.Add(c);
            }

            if (candidates.Count == 0) continue;

            int total = 0;
            for (int i = 0; i < candidates.Count; i++)
                total += candidates[i].DropWeight > 0 ? candidates[i].DropWeight : 1;

            int roll = rng.Next(total);
            int acc = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                int wt = candidates[i].DropWeight > 0 ? candidates[i].DropWeight : 1;
                acc += wt;
                if (roll < acc) return candidates[i];
            }

            return candidates[candidates.Count - 1];
        }

        return null;
    }

    /// <summary>是否掉落（每次击杀调一次）。</summary>
    public static bool ShouldDrop(Random rng)
    {
        if (rng == null) rng = new Random();
        return rng.NextDouble() < DropChance;
    }

    /// <summary>
    /// 稀有度 → 随机词条条数（下标 = Rarity）。[PLACEHOLDER]
    /// 白装 0 条（纯过渡），绿 1 / 蓝 2 / 紫 3 / 橙 3。
    /// rationale：橙装与紫装同为 3 条，差别落在**基础数值**（64 vs 32）而不是词条数量 ——
    /// 让"刷橙"是数值提升，而不是把词条上限再抬一档（否则紫装直接被淘汰）。
    /// </summary>
    static readonly int[] AffixCountByRarity = { 0, 0, 1, 2, 3, 3 };

    /// <summary>
    /// 掷一件装备的随机词条（P1）。同一件不出现**同类效果**（避免两条"锋利"叠满上限），
    /// 但不同类可以共存（例：锋利 + 连绵）。
    /// </summary>
    public static List<int> RollAffixes(Rarity rarity, Random rng, List<EquipAffixConfig> pool)
    {
        List<int> ids = new List<int>();
        if (pool == null || pool.Count == 0) return ids;
        if (rng == null) rng = new Random();

        int idx = (int)rarity;
        if (idx < 0) idx = 0;
        if (idx >= AffixCountByRarity.Length) idx = AffixCountByRarity.Length - 1;
        int want = AffixCountByRarity[idx];

        List<EquipAffixConfig> cand = new List<EquipAffixConfig>();
        for (int i = 0; i < pool.Count; i++)
        {
            EquipAffixConfig c = pool[i];
            if (c != null && c.Weight > 0) cand.Add(c);
        }

        for (int n = 0; n < want && cand.Count > 0; n++)
        {
            int total = 0;
            for (int i = 0; i < cand.Count; i++) total += cand[i].Weight;
            if (total <= 0) break;

            int roll = rng.Next(total);
            int acc = 0;
            int pick = 0;
            for (int i = 0; i < cand.Count; i++)
            {
                acc += cand[i].Weight;
                if (roll < acc) { pick = i; break; }
            }

            EquipAffixEffect picked = cand[pick].Effect;
            ids.Add(cand[pick].Id);

            // 同类效果出过就整体移除，下一跳换类别
            for (int i = cand.Count - 1; i >= 0; i--)
                if (cand[i].Effect == picked) cand.RemoveAt(i);
        }

        return ids;
    }
}
