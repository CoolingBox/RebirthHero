// 文件：CultivationConfig.cs
// 《轮回大侠》M2.2 经济 —— **击杀即时掉落** 的产出与成长常数
//
// ⚠️ 2026-10-16 起：**数值由 `TuningConfig` 总账表注入**（见 BattleConfig.cs 顶部说明）。
//   本文件保留 public static 字段（= 兜底值 + 热路径零开销读取）。改数值改表，别改这里。
//
// 用户口径（2026-10-15）：
//   · "击杀就可立即获得经验资源" → 掉落挂在【每个击杀】上，不再等整场结束一次性结算
//   · "升级要等待战斗结束，不然中途升级可能属性混乱" → 经验即时累加、**升级只在战斗结束时结算**
//   · "敌方掉落的是修为(就是经验)和银两"
//
// 于是每击杀一个敌人，两样东西立刻进账：
//
// ┌─ 一、修为（= 经验）──────────────────────────────────────────
// │ 掉落： 每个击杀 → 每名参战者各得 KillGainC × GainA^层
// │        （一层 4 个敌人 → 每人合计 2.0 × 1.16^层，与原"整场结算 2.0"完全等价）
// │ 升级： **战斗结束时**才结算 —— 攒满 LevelCost(L) 自动升、可连升
// │ 为什么延迟：战斗中途改等级会让同一场里已建好的单位与新等级脱节（属性混乱）
// └──────────────────────────────────────────────────────────
//
// ┌─ 二、银两（货币）──────────────────────────────────────────
// │ 掉落： 每个击杀 → 整队共享账户 + KillSilverC × SilverA^层
// │        （一层 4 个 → 合计 10 × 1.16^层，与原版等价）
// │ 用途： GDD §4.2 的 sink —— 客栈招募 / 武学商店 / 装备强化 / 境界突破（M2.3 起）
// └──────────────────────────────────────────────────────────
//
// 注意：**即时制下没有"败北折扣"** —— 打不动就杀不掉，收入自然变少；
//   这比原来"固定给 50%"更精确（打赢一半就拿一半，团灭就拿不到）。

public static class CultivationConfig
{
    // ═══ 一、修为（= 经验，击杀即时掉落；升级延迟到战斗结束）═══
    public static double KillGainC = 0.5;  // 每个击杀、每人的修为（×4 个敌人 = 2.0，沿用已验证的节奏）
    public static double GainA = 1.16;     // 与敌方 HP 同底数（框架 §4 规定）
    //   2026-10-16 用户口径"层级高的产出也提高" → 核查后确认**掉落本就在随层涨**（0.5 × 1.16^层），维持 1.16 不改。

    public static double LevelBase = 30;   // 升 L1→L2 所需修为 [PLACEHOLDER]
    public static double LevelD = 1.18;    // 必须略大于 GainA（§4 规定：消费涨得比产出快）
    public static int MaxLevel = 40;       // 等级上限（框架 §11）

    // ═══ 二、银两（货币，击杀即时掉落）═══
    public static double KillSilverC = 2.5; // 每个击杀的银两（×4 个敌人 = 10）
    public static double SilverA = 1.16;    // 与敌方 HP 同底数

    /// <summary>单次击杀、每名参战者获得的修为。</summary>
    public static double GainPerKill(int layer)
        => KillGainC * System.Math.Pow(GainA, System.Math.Max(1, layer));

    /// <summary>单次击杀掉落的银两（整队共享一份）。</summary>
    public static double SilverPerKill(int layer)
        => KillSilverC * System.Math.Pow(SilverA, System.Math.Max(1, layer));

    /// <summary>从 level 升到 level+1 所需修为（框架 §4：d^k，k = 已升次数 = level−1）。</summary>
    public static double LevelCost(int level)
        => level >= MaxLevel
            ? double.PositiveInfinity
            : LevelBase * System.Math.Pow(LevelD, level - 1);
}
