// 文件：StageConfig.cs
// 《轮回大侠》M2.1 推图 —— 推图节奏常数（依据：战斗数值框架 §3）
//
// ⚠️ 2026-10-16 起：**数值由 `TuningConfig` 总账表注入**（见 BattleConfig.cs 顶部说明）。
//   本文件保留 public static 字段（= 兜底值 + 热路径零开销读取），表的注入与一致性校验在
//   Scripts/Game/Config/Adapter/Tuning.cs。改数值改表，别改这里。
//
// 分工：
//   · 敌方【基础属性与层级成长】在内容表 `EnemyConfig.xlsx`（BaseHp/BaseAtk/BaseDef + HpRate/AtkRate/DefRate）；
//   · 【波数 / 编成 / 回血】这类引擎常数留在本类（现已全部登记进 TuningConfig 总账）。
//
// 层 → 波（用户口径："起始要刷 5 波普通敌人，才会打层主；每波敌人间只恢复 10% 的血量；
//   每 10 层加 1 波" + "普通层的层主是精英"）：
//   · 一个【层】= N 波普通敌人 + 1 波**收尾首领**（N = 5 + (层−1)/10，每 10 层 +1）
//   · 收尾波的编成：**普通层用精英**（口径："普通层的层主是精英"）、**层主层（每 10 层）用层主**
//   · 波与波之间：**阵亡者保持阵亡**（口径），存活者只回 **10% 最大气血**（不是奶满）
//   · 跨层：整队重建（满血、阵亡者归队）—— 层是新的一段，从满状态开始

public static class StageConfig
{
    public static int BossEvery = 10;     // 每 10 层一个层主（收尾波换用层主模板）
    public static int WaveSize = 4;       // 每波 4 个敌方单位（与玩家 4 人对称）
    public static int RetryEvery = 10;    // 挂机模式下每刷多少场自动重试上面那层一次（0 = 只手动）

    // —— 层内波数 ——
    public static int FirstMobWaves = 5;    // 第 1 层的普通波数（"起始要刷 5 波普通敌人才打层主"）
    public static int WaveStepEvery = 10;   // 每多少层普通波数 +1（"每 10 层加 1 波"）

    /// <summary>波间恢复比例（相对**最大气血**）——"每波敌人间只恢复 10% 的血量"。</summary>
    public static double WaveHealRatio = 0.10;

    /// <summary>第 layer 层的**普通波**数量（不含收尾首领波）。</summary>
    public static int MobWaves(int layer)
        => FirstMobWaves + System.Math.Max(0, layer - 1) / WaveStepEvery;

    /// <summary>第 layer 层的**总波数** = 普通波 + 1 波收尾首领。</summary>
    public static int WavesInLayer(int layer) => MobWaves(layer) + 1;
}

public static class StageRules
{
    /// <summary>
    /// 敌方不享受玩家侧的"1 号位机制"（×3 受击权重 + ×1.3 气血）——那是"布阵"这一玩家决策的回报；
    /// 敌方全员等权重、无加成，层主的强度只来自 EnemyConfig 模板的数值列。
    /// </summary>
    public static bool EnemyHasSlot1Bonus = false;
}
