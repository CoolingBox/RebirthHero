// 文件：SummonSpec.cs
// 《轮回大侠》战斗引擎 —— 召唤物【类型规格】（纯 C#，无 Godot 依赖）
//
// 口径（2026-10-16 用户）："召唤物类型分档，召唤物的血攻和武侠相关，限 1 的血攻多点"
//   · **血攻随召唤者**：气血 = 召唤者最大气血 × HpRatio，攻击 = 召唤者攻击 × AtkRatio
//     （所以召唤物的绝对强度自动跟随召唤者的等级/装备，不需要单独给成长曲线）
//   · **类型分档**：每种类型一套 HpRatio / AtkRatio / 速度 / 权重 / 存在时间 / **同时上限**
//   · **限 1 的补更多**：上限越低的类型，单体血攻给得越高（见 SummonConfig 表）
//
// 数据来源：`SummonConfig.xlsx` → JSON → `SummonConfig` 生成类 → 配置接入层
// （`SummonCatalog`）→ 本类。引擎不反向依赖配置表，故这里保留一份 **Fallback**
// 作为"表缺失/类型未登记"的兜底（= 2026-10-16 之前的硬编码值）。

using System;

/// <summary>召唤物类型规格（一种类型的全部参数）。</summary>
public sealed class SummonSpec
{
    public string Kind = "";            // 类型键（tank/dps/sub/sword…），与 SkillConfig.SummonKind 对应
    public string Name = "召唤物";       // 显示名

    /// <summary>气血 = 召唤者最大气血 × 本值（"血攻和武侠相关"）。</summary>
    public double HpRatio = 0.25;
    /// <summary>
    /// **固定气血**：&gt;0 时不用 <see cref="HpRatio"/>，也不乘全局 HpScale（它是"层数"而不是血池）。
    /// 0 = 走比例血。
    /// </summary>
    public double FixedHp = 0;
    /// <summary>
    /// **每次命中固定扣血**：&gt;0 时**无视伤害量**，一次命中只扣这么多；0 = 按实际伤害。
    /// 与 <see cref="FixedHp"/> 配合可做"**命中计数体**"（飞剑：2 层、挨一次掉 1 层）——
    /// 免疫一发大伤害秒杀，但被高频多次命中（AoE / 连击 / 多段）克制得极狠。
    /// </summary>
    public double DmgPerHit = 0;
    /// <summary>攻击 = 召唤者攻击 × 本值。</summary>
    public double AtkRatio = 0.5;

    /// <summary>速度下限；实际速度 = max(SpdFloor, 召唤者速度 × SpdRatio)。</summary>
    public double SpdFloor = 60;
    /// <summary>速度跟随召唤者的倍率（0 = 不吃召唤者速度）。</summary>
    public double SpdRatio = 0;

    public double Weight = 10;          // 基础受击权重（10 基准）
    public double Ttl = 20;             // 存在时间（秒）

    /// <summary>
    /// **该类型同时存活的召唤物上限**（0 = 不单独限制，只受全队 <see cref="BattleConfig.SummonCap"/> 约束）。
    /// 武学侧还可以用 <see cref="BattleSkill.SummonMax"/> 覆盖（0 = 跟随类型）。
    /// </summary>
    public int Max = 0;

    public double Speed(double ownerSpd) => Math.Max(SpdFloor, ownerSpd * SpdRatio);

    /// <summary>表缺失或类型未登记时的兜底（= 2026-10-16 之前的引擎硬编码值，Max=0 即"不限"）。</summary>
    public static SummonSpec Fallback(string kind) => new()
    {
        Kind = kind ?? "",
        Name = kind switch { "tank" => "刀盾傀儡", "dps" => "灵蛇", "sub" => "纸人", _ => "飞剑" },
        HpRatio = kind switch { "tank" => 0.40, "dps" => 0.25, "sub" => 0.25, _ => 0.20 },
        AtkRatio = kind switch { "tank" => 0.30, "dps" => 0.60, "sub" => 0.10, _ => 0.80 },
        SpdFloor = kind switch { "sword" => 0, _ => 60 },
        SpdRatio = kind switch { "dps" => 1.2, "sword" => 1.5, _ => 0 },
        Weight = kind switch { "tank" => 20, "sub" => 25, _ => 10 },
        Ttl = BattleConfig.SummonTtl,
        Max = 0,
    };
}
