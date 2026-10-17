// 文件：InnConfig.cs
// 《轮回大侠》M2.3 客栈 —— 全局系数类
//
// ⚠️ 与 BattleConfig/StageConfig/CultivationConfig 同规矩：**这里的数由 `TuningConfig` 总账表注入**
//   （见 Scripts/Game/Config/Adapter/Tuning.cs）。代码里的 `= 值` 只是兜底，出货值以表为准。改数值改表。
//
// 设计依据：《侠客与资质》§二「资质系统（每世客栈刷新）」
//   · 星级 ★1~★5，概率 50/30/12/6/2%；★1=×1.0，每星 +15% 作用于全部属性
//   · 成长率：HP/ATK/SPD 三维**独立**掷 −20% ~ +30%（均匀），"瘸腿天才"是刻意保留的
//   · 资质词条：0/1/2 个，概率 40/45/15%，池见 `QualityConfig` 表
//   · 客栈 4 栏；每世开始自动刷新；世内手动刷新 1 次免费、之后付费递增
//   · 招募费用 = 基准 × 星级系数（★1=1×，★5=5×），**价格扁平不随次数涨**
//
// ⚠️ 与我（助手）的偏离，已标注待你确认：
//   文档写"招募用**修为**"，但修为 = 经验且被自动升级即时吃掉（2026-10-15 你定的双资源口径），
//   故此处招募/刷新一律花**银两**。文档那两处需同步改口径。

public static class InnConfig
{
    // ═══ 资质：星级 ═══
    public static double StarStep = 0.15;       // 每星 +15%（★1 = ×1.0 → ★5 = ×1.6）
    public static double StarW1 = 50;           // ★1 权重(%)
    public static double StarW2 = 30;           // ★2
    public static double StarW3 = 12;           // ★3
    public static double StarW4 = 6;            // ★4
    public static double StarW5 = 2;            // ★5（"客栈刷屏时刻"）

    // ═══ 资质：成长率（三维各自独立、均匀分布）═══
    public static double StatRollMin = -20;     // 下限 −20%
    public static double StatRollMax = 30;      // 上限 +30%

    // ═══ 资质：词条个数权重 ═══
    public static double AffixW0 = 40;          // 0 个
    public static double AffixW1 = 45;          // 1 个
    public static double AffixW2 = 15;          // 2 个

    // ═══ 客栈规则 ═══
    public static int InnSlots = 4;             // 客栈栏位（天赋树"客栈扩建"可 +1）
    public static int InnFreeRefresh = 1;       // 每世免费手动刷新次数
    public static double InnRefreshBase = 20;   // 手动刷新基础价（银两）[我自拟 · PLACEHOLDER]
    public static double InnRefreshGrowth = 2;  // 每次付费刷新的价格递增倍率 [我自拟 · PLACEHOLDER]
    public static double RecruitBase = 30;      // 招募基础价（银两）[我自拟 · PLACEHOLDER]
    public static double StarCostStep = 1.0;    // 星级费阶：费用系数 = 1 + (星−1)×本值（★5 = 5×）

    /// <summary>编队人数上限（与玩家战斗编制 4 人一致）。</summary>
    public static int RosterSize = 4;
}
