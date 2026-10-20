// 文件：EquipRules.cs
// 《轮回大侠》M2.5 装备数值规则（强化倍率 / 强化费用）
//
// 为什么用银两而不是修为：
//   开发任务拆解 2.5 写的是"修为强化"，但双资源口径（2026-10-15 定稿）里
//   **修为 = 经验，攒满自动升级、不留存**，它不是货币，没有余额可供消费。
//   GDD §4.2 的通胀检查也写明"**银两**的最大 sink 是客栈招募与装备洗炼"。
//   所以强化消耗走银两，与"修为=经验"的口径一致。
//
// 数值全部 [PLACEHOLDER · 需实机 playtest]：
//   验证路径 —— 记录玩家从 L1 到套装成型期间，银两用于"招募 / 强化"的比例；
//   若强化长期 <20%，说明费用偏高或收益偏低，调这两只旋钮即可。
//
// **设备旋钮只有两个**（其余都是推导值）：
//   LevelBonusPerLevel = 每级强化的属性增幅
//   CostBase / CostGrowth = 强化费用曲线的起点与公比

using System;

public static class EquipRules
{
    /// <summary>强化等级上限。[PLACEHOLDER] 10 级 = 2 倍基础属性（1 + 10×0.10）。</summary>
    public const int MaxLevel = 10;

    /// <summary>每强化等级给基础属性 +10%。[PLACEHOLDER] 线性加算，不做边际递减 ——
    /// 强化是"打磨已有装备"，不是"抽更强的装备"，线性更好预期。</summary>
    public const double LevelBonusPerLevel = 0.10;

    /// <summary>0 级 → 1 级的费用。[PLACEHOLDER]</summary>
    public const double CostBase = 100.0;

    /// <summary>每级费用公比。[PLACEHOLDER] 1.8 → 满 10 级单件累计约 6400 银两。</summary>
    public const double CostGrowth = 1.8;

    public static double AtkOf(EquipItem item, EquipConfig cfg)
    {
        if (item == null || cfg == null) return 0;
        return cfg.BaseATK * Multiplier(item.Level);
    }

    public static double HpOf(EquipItem item, EquipConfig cfg)
    {
        if (item == null || cfg == null) return 0;
        return cfg.BaseHP * Multiplier(item.Level);
    }

    public static double SpdOf(EquipItem item, EquipConfig cfg)
    {
        if (item == null || cfg == null) return 0;
        return cfg.BaseSPD * Multiplier(item.Level);
    }

    public static double Multiplier(int level)
    {
        if (level <= 0) return 1.0;
        if (level > MaxLevel) level = MaxLevel;
        return 1.0 + level * LevelBonusPerLevel;
    }

    /// <summary>从当前等级升到下一级的费用；已满级返回 -1。</summary>
    public static long EnhanceCost(int currentLevel)
    {
        if (currentLevel < 0) return -1;
        if (currentLevel >= MaxLevel) return -1;

        return (long)(CostBase * Math.Pow(CostGrowth, currentLevel));
    }

    /// <summary>花银两强化 +1 级。失败（满级 / 银两不足）返回 false 且不扣钱。</summary>
    public static bool TryEnhance(EquipItem item, Silver silver)
    {
        if (item == null || silver == null) return false;

        long cost = EnhanceCost(item.Level);
        if (cost < 0) return false;

        // 先扣钱再升级：TrySpend 内部已做余额判断，扣失败则等级不变
        if (!silver.TrySpend(cost)) return false;

        item.Level++;
        return true;
    }

    /// <summary>稀有度显示名（白/绿/蓝/紫/橙）。装备复用 Rarity 五档。</summary>
    public static string RarityName(Rarity rarity)
    {
        switch (rarity)
        {
            case Rarity.Common: return "白";
            case Rarity.Fine: return "绿";
            case Rarity.Epic: return "蓝";
            case Rarity.Legend: return "紫";
            case Rarity.Myth: return "橙";
            default: return "?";
        }
    }

    /// <summary>栏位显示名。</summary>
    public static string SlotName(EquipSlot slot)
    {
        switch (slot)
        {
            case EquipSlot.Weapon: return "武器";
            case EquipSlot.Armor: return "护甲";
            case EquipSlot.Trinket: return "饰品";
            case EquipSlot.Boots: return "靴子";
            default: return "?";
        }
    }
}
