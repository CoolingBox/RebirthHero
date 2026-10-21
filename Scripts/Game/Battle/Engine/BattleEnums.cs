// 文件：BattleEnums.cs
// 《轮回大侠》M1 战斗引擎 —— 枚举定义（纯 C#，无 Godot 依赖）
// 规则依据：《轮回大侠-战斗数值框架-v0.1》§7/§8/§9

/// <summary>武学触发时机</summary>
public enum SkillSrc
{
    OnAction,    // 行动时（默认与普攻同放）
    OnHit,       // 受击时
    OnHpBelow,   // 血量低于阈值时（一次性）
}

/// <summary>武学效果词</summary>
public enum SkillEff
{
    Dmg,         // 伤害
    Heal,        // 治疗
    Shield,      // 护盾
    Summon,      // 召唤
    Weight,      // 受击权重修正（嘲讽/隐身）
    SpdMod,      // 速度修正（减速）
    AtkMod,      // 攻击修正（减攻）
    Poison,      // 叠毒（DoT，不吃武学倍率）
    Burn,        // 灼烧（DoT，不吃武学倍率）
    Silence,     // 沉默
    Growth,      // 成长（当局叠乘攻击）
    Shield2Dmg,  // 护盾转伤害
    Dodge,       // 闪避（结算前判定，不触发后续）
    ReflectAccum,// 累计伤害反伤
    ShieldKeep,  // 护盾保持率修正（友方）：+Mult 个百分点，本场累计，上限由 Act() 统一兜
}

/// <summary>伤害类型（三种减免口径；公式见数值框架 §2）</summary>
public enum DamageKind
{
    External,     // 外功：硬减（基础 − 防御），对小伤害惩罚重、对大伤害更划算
    Internal,     // 内功：比例化解（×(Kd/(Kd+DEF))），收益递减、对小伤害更划算
    TrueDamage,   // 真伤：无视一切减伤（稀缺，用于克制高防 Boss）
}

// 注：成长"原型"（早熟/均衡/晚成/技巧）已从引擎枚举中移除 —— 它现在是 HeroConfig.GrowthType 上的
//     【事后分类标签】（先按随机偏差生成曲线、再按形状归类），引擎不参与判定，
//     数值只由 HeroConfig 的 HpGrowth / AtkGrowth / DefGrowth 三个字段驱动。

/// <summary>目标规则</summary>
public enum SkillTgt
{
    Random,        // 随机单体（普攻按权重）
    LowestHpAlly,  // 血最少友方
    LowestHpFoe,   // 血最少敌方
    HighestW,      // 受击权重最高敌方
    All,           // 全体敌方
    Self,          // 自身
}
