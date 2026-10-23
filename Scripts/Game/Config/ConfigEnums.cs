// 配置枚举定义（被各配置表类型行引用的枚举名；SkillConfig / HeroConfig / 未来 ForgeConfig 等共用）。
// 生成器 ConfigGenerator 把类型行的值原样当 C# 类型名写出(public SkillEffect EffectType;),
// 但不会自动生成枚举体 —— 因此这些枚举必须手动定义, 否则对应 .cs 无法编译。
// 重要: 枚举成员必须显式赋 int 值, 且【严格以对应 Excel 第4行(补充说明)的图例为准】,
//        生成器不校验枚举合法性, 成员值错会导致代码中读到错误的枚举。
//
// 各表第4行图例(权威)：
//   SkillConfig（2026-10-15 按《武学卡清单 v0.7》重建，v2 加 DamageType/Status；2026-10-16 加 SummonMax）:
//     Category    : 1剑/2刀/3拳/4掌/5腿/6鞭/7暗器/8轻功/9医/10毒/11奇门/12禅/13兽
//     TriggerType : 1行动/2受击/3血量阈值/4开局(光环)
//     CondType    : 0无/1次数/2概率/3连击
//     DamageType  : 0无/1外功/2内功/3真伤   （三种减免口径，公式见数值框架 §2）
//     SkillEffect : 1伤害/2治疗/3护盾/4召唤/5权重修正/6速度修正/7攻击修正/8叠毒/9灼烧/10沉默/11成长/12护盾转伤害/13闪避/14累计反伤/15复制/16污染/17光环/18蓄力
//                   （15~18 为引擎未实装机制，配置里对应卡 Status=待实装）
//     CalcBase    : 1自身攻击力倍数/2自身最大生命%/3固定值
//     SkillTarget : 0自身/1随机(权重)/2敌方最低血/3己方最低血/4敌方最高权重/5全体/6攻击者
//     SkillStatus : 1已实装/2待实装
//     Tier        : 1入门/2精良/3上乘/4绝学（武学品阶，2026-10-15 新增）。
//                   敌方抽卡按层限制品阶：TierCap = min(4, 1+(层-1)/10) —— 层 1~10 只出 1、
//                   11~20 ≤2、21~30 ≤3、31+ 全开（= 不限制）；与"敌方武学数每 10 层 +1"同节奏
//     SummonMax   : **该武学**同时存活的召唤物上限。**0 = 跟随召唤物类型档位**
//                   （SummonConfig.SummonMax）；>0 = 本卡专属覆盖。外层还有全队上限
//                   BattleConfig.SummonCap=4。2026-10-16 口径"单个召唤物武学限制最大数量" →
//                   当轮落在本列；随后"召唤物类型分档"把真正的档位移到 SummonConfig，
//                   本列退化为可选覆盖（例：万剑归宗 = 1，御剑术 = 2）。
//     SummonOrder : **召唤时号令**（2026-10-16 口径"让己方队伍所有飞剑全部攻击"）——
//                   本卡触发召唤后，命令己方所有【同类型】召唤物各立即普攻一次。
//                   语义是"命令"不是"额外行动"：不走 Act()、不加行动计数、不触发其自身武学。
//                   当前只有万剑归宗 = True（"召唤1飞剑 + 号令全队飞剑齐射"）。
//     SummonCount : **每次触发召唤几只**（0/1 = 单只；>1 = 一次多只）。与 SummonMax 是两件事：
//                   本列管"一次召几只"，SummonMax 管"最多同时留几只"。
//                   2026-10-16 口径"御剑术是召唤2飞剑(不是上限)" → 御剑术 SummonCount=2、
//                   SummonMax=0（跟随剑类档位）。多只召唤逐只走同一套上限检查，超限即停。
//   SummonConfig: **召唤物【类型档位】表**（2026-10-16 新增，口径"召唤物类型分档，召唤物的血攻和武侠相关，
//                   限 1 的血攻多点"）。4 行 = tank/dps/sub/sword：
//                   · HpRatio/AtkRatio = 气血/攻击 相对**召唤者**的倍率（"血攻和武侠相关"→
//                     召唤物强度自动跟随召唤者等级装备，不需要单独成长曲线）
//                   · **FixedHp/DmgPerHit = "命中计数体"**（2026-10-16 口径"飞剑血量固定为2,每次受击-1"）：
//                     FixedHp>0 时气血用固定值（不乘召唤者、不吃 HpScale）；DmgPerHit>0 时**无视伤害量**，
//                     一次命中只扣该值。飞剑 = 2 层计数体 → 免疫一发大伤害秒杀，怕高频多次命中（AoE/连击/多段）
//                   · SpdFloor/SpdRatio → 速度 = max(SpdFloor, 召唤者速度×SpdRatio)
//                   · BaseW=基础受击权重；Ttl=存在时间(秒)
//                   · SummonMax = **该类型同时存活上限**（tank 1 / dps 2 / sub 3 / sword 2）
//                   分档原则：总量守恒（单卡总血量 0.40~0.80）＋ 上限越低单体越厚（"限1的补更多"）
//                   ＋ 权重与血量分工（tank 靠血厚活着，sub 靠权重吸伤害然后死掉）。
//   EnemyConfig : Kind(1小怪/2精英/3层主)。**模板自带属性与成长**（2026-10-15 v5.0）：
//                   BaseHp/BaseAtk/BaseDef = 第1层基础属性；HpRate/AtkRate/DefRate = 每层成长倍率
//                   → 属性(层) = Base × Rate^(层−1)（不再有"全局曲线 × 模板倍率"两层）；
//                   Spd=速度(不随层成长)；BaseW=基础受击权重(10基准,敌方无 1 号位加成)；
//                   SkillCount=武学数基数、SkillTier=每多少层+1个武学(**仅小怪用**)；
//                   MinLayer=最早出现层【档位】(抽取取 MinLayer ≤ 当前层中最大的一档)；
//                   Skills=**固定技能组**(精英/层主必填；小怪留空 = 从"已实装全池"按层随机抽，受品阶上限约束)。
//                   编成：普通层 = 1 精英 + 3 小怪；层主层 = 1 层主 + 1 精英 + 2 小怪。
//   HeroConfig  : Rarity(1普通/2精良/3史诗/4传说/5神话)；BaseW=基础受击权重(10基准;1号位×3)；
//                 HpGrowth/AtkGrowth/DefGrowth = 三属性**各自独立**的【每级成长绝对值】(直接加算,均不为0；
//                   标准 100 / 5 / 2；属性(L) = Base + 本值×(L−1))；
//                 GrowthType(1早熟/2均衡/3晚成/4技巧) = 【事后分类标签】(先按基础随机 b±40% + 三条独立成长随机
//                   g±20% 长出曲线，再按 E=sqrt(g_hp×g_atk) 的形状归类)，引擎不参与判定，
//                   数值只由三个 Growth 字段驱动；速度占模也只在生成期体现(不入字段)。模型见数值框架 §11。
//
// 引擎消费映射（M2 落地，见 BattleEnums）：
//   SkillEffect 1~14 → SkillEff：逐项对应（值-1），顺序与 BattleEnums.SkillEff 完全一致；15~18 待引擎扩充。
//   SkillTarget → SkillTgt：语义对应但编号不同，用 switch（Self→Self, Random→Random,
//     LowestHpFoe→LowestHpFoe, LowestHpAlly→LowestHpAlly, HighestWeight→HighestW, All→All, Attacker→暂以随机代）。
//   TriggerType → SkillSrc：1→OnAction, 2→OnHit, 3→OnHpBelow, 4→开战光环（引擎未实装）。
//   CondType → BattleSkill.Cond 字符串：1→"count", 2→"chance", 3→"combo"。
//   DamageType → 伤害公式分支（外功硬减 / 内功比例化解 / 真伤无视减伤；无伤害类忽略）。
//   目标阵营由 EffectType 决定（伤害/权重/减速/减攻/DoT/沉默→敌方；治疗/护盾→己方），故无 TargetTeam 列。
//   Status=待实装 的卡：引擎尚未实现其机制，构建 AI 构筑/装配池时应过滤。

public enum Category
{
    Sword = 1,     // 剑
    Saber = 2,     // 刀
    Fist = 3,      // 拳
    Palm = 4,      // 掌
    Leg = 5,       // 腿
    Whip = 6,      // 鞭
    Hidden = 7,    // 暗器
    Lightness = 8, // 轻功
    Medicine = 9,  // 医
    Poison = 10,   // 毒
    Qimen = 11,    // 奇门
    Zen = 12,      // 禅
    Beast = 13,    // 兽
}

public enum TriggerType
{
    OnAction = 1,     // 行动
    OnHit = 2,        // 受击
    OnHpBelow = 3,    // 血量阈值
    OnStart = 4,      // 开局(光环)：开战触发一次、持续整场
    Passive = 5,      // 被动（常驻）：无条件、装配即生效 —— 2026-10-22
}

public enum CondType
{
    None = 0,    // 无
    Count = 1,   // 次数
    Chance = 2,  // 概率
    Combo = 3,   // 连击
}

public enum DamageType
{
    None = 0,        // 无(不造成伤害)
    External = 1,    // 外功(硬减：基础−DEF)
    Internal = 2,    // 内功(比例化解：×(1−DEF/(DEF+Kd)))
    TrueDamage = 3,  // 真伤(无视减伤)
}

public enum SkillEffect
{
    Dmg = 1,           // 伤害
    Heal = 2,          // 治疗
    Shield = 3,        // 护盾
    Summon = 4,        // 召唤
    Weight = 5,        // 权重修正
    SpdMod = 6,        // 速度修正
    AtkMod = 7,        // 攻击修正
    Poison = 8,        // 叠毒
    Burn = 9,          // 灼烧
    Silence = 10,      // 沉默
    Growth = 11,       // 成长
    Shield2Dmg = 12,   // 护盾转伤害
    Dodge = 13,        // 闪避
    ReflectAccum = 14, // 累计反伤
    Copy = 15,         // 复制      [待实装]
    Corrupt = 16,      // 污染      [待实装]
    Aura = 17,         // 光环      [待实装]
    Charge = 18,       // 蓄力      [待实装]
    ShieldKeep = 19,   // 护盾保持率 +CalcValue 个百分点（友方，本场累计）—— 2026-10-21 新增
    Hot = 20,          // 持续治疗（HoT）：每 2s 一跳，可叠加 —— 2026-10-22 新增
    CounterUp = 21,    // 开局充能：给己方**次数型**武学的当前计数 +CalcValue（= CounterOffset）—— 2026-10-22
    MaxHpPct = 22,     // 气血上限 +CalcValue%（被动，装配期生效）
    DmgTakenPct = 23,  // 受击伤害 +CalcValue%（被动，装配期生效；负数 = 减伤）
    PoisonBurst = 24,  // 引爆毒层：伤害 = 攻击 × CalcValue% × 目标毒层数，结算后清空毒层 —— 2026-10-22
    MissingHp = 25,    // 斩杀：伤害 = 目标**已损失气血** × CalcValue% —— 2026-10-22
    ShieldScale = 26,  // 按盾加伤：伤害 = 自身当前护盾 × CalcValue%，**不消耗护盾** —— 2026-10-22
    SelfHpLoss = 27,   // 自损气血：扣自身**气血上限** × CalcValue%（不吃护盾/减伤、不计受击）—— 2026-10-23
    Expose = 28,       // 破绽：目标每层破绽让**下一次受击**伤害 ×(1+BreakBonus)，挨一刀消耗一层 —— 2026-10-24
}

public enum CalcBase
{
    AtkMultiple = 1,      // 自身攻击力倍数
    SelfMaxHpPercent = 2, // 自身最大生命%
    Fixed = 3,            // 固定值
}

public enum SkillTarget
{
    Self = 0,          // 自身
    Random = 1,        // 随机(权重)
    LowestHpFoe = 2,   // 敌方最低血
    LowestHpAlly = 3,  // 己方最低血
    HighestWeight = 4, // 敌方最高权重
    All = 5,           // 全体
    Attacker = 6,      // 攻击者
}

public enum SkillStatus
{
    Implemented = 1, // 已实装
    Pending = 2,     // 待实装
}

// 资质词条效果（《侠客与资质》§二"资质词条"；池见 QualityConfig 表）
//   1~4 **直接落到 BattleUnit 的既有字段**（已实装）；5~8 需要引擎新钩子（**待实装**，构建时跳过）。
public enum AffixEffect
{
    None = 0,
    AtkPct = 1,        // 攻击 +Value1%
    HpPct = 2,         // 气血 +Value1%
    SpdPct = 3,        // 速度 +Value1%
    Weight = 4,        // 受击权重 +Value1（10 基准的**绝对值**增量，可负）
    MulPct = 5,        // 武学倍率 +Value1%（按 School 匹配流派；School 空 = 全部武学）—— 装配期乘进武学倍率
    HealPct = 6,       // 治疗量 +Value1%（装配期乘进治疗类武学）
    SummonHpPct = 7,   // 召唤物气血 +Value1%（比例血型召唤物；计数体飞剑不吃）
    DmgTakenPct = 8,   // 受击伤害 +Value1%（代价面：>0 = 更脆，用更脆换更痛）
    ShieldKeepPct = 9, // 护盾保持率 +Value1 个百分点（行动时护盾衰减得更少）
}

// 成长原型 = 随机曲线的【事后归类标签】（判定规则见数值框架 §11）。
// 引擎不读它，只读 HeroConfig 的 HpGrowth / AtkGrowth / DefGrowth（三属性各自的每级成长绝对值）。
//   b = 基础随机（HP/ATK/DEF 共用，±40%，决定 L1 强弱）
//   g = 成长随机（三属性各自独立，±20%，决定涨幅强弱）；E = sqrt(g_hp × g_atk)
public enum GrowthType
{
    Early = 1,     // 早熟：基础偏高、涨幅不足（E ≤ 0.97）→ L1 强、满级平
    Balanced = 2,  // 均衡：涨幅居中（E 介于 0.97~1.03）
    Late = 3,      // 晚成：涨幅充足（E ≥ 1.03）→ L1 弱、满级强
    Utility = 4,   // 技巧：基础低(b ≤ 0.95) + 涨幅不足(E ≤ 0.97) → 属性全线偏低，靠独特武学补偿
}

public enum Rarity
{
    Common = 1,   // 普通
    Fine = 2,     // 精良
    Epic = 3,     // 史诗
    Legend = 4,   // 传说
    Myth = 5,     // 神话
}

// 敌方单位类型（EnemyConfig.Kind）。
//   层主层由 StageConfig.BossEvery 判定；该层取 Kind=Boss 的模板，其余层取 Kind=Mob。
//   Elite 为"精英小怪"预留位——当前表未使用（每层 4 人中的强化位，留给后续内容量产）。
public enum EnemyKind
{
    Mob = 1,    // 小怪
    Elite = 2,  // 精英
    Boss = 3,   // 层主
}

// 装备栏位（EquipConfig.Slot）。四栏刻意不同构：武器偏攻、护甲偏血、饰品均衡、靴子偏速。
//   rationale：若四栏同构（都给一点攻血），配装就退化成"哪件数值大穿哪件"，没有决策。
// 稀有度**复用 Rarity 五档**（1白/2绿/3蓝/4紫/5橙），不另立枚举 —— 档位数与语义层级一致，
//   多一个同构枚举只会增加映射成本。
public enum EquipSlot
{
    Weapon = 1,   // 武器
    Armor = 2,    // 护甲
    Trinket = 3,  // 饰品
    Boots = 4,    // 靴子
}

// ================= 套装与装备词条（P1，2026-10-20） =================

/// <summary>
/// 套装效果类型（EquipSetConfig.Effect2 / Effect4）。
/// **纪律：只做参数修正**（改武学既有参数或既有结算系数），不新开乘区。
/// 依据《装备与套装 v0.2》§二 + 数值框架 §9。
/// </summary>
public enum SetEffect
{
    None = 0,
    ActionCondDown = 1,   // 行动类武学需求次数 −Value（下限 1）
    ActionProcUp = 2,     // 行动类武学触发概率 +Value 个百分点
    ReplayUp = 3,         // 重放概率 +Value 个百分点
    CounterUp = 4,        // 开战时武学当前计数 +Value（引擎 CounterOffset）
    CannotAtkMult = 5,    // "不能普攻"武学倍率 +Value%（走武学倍率，不开新乘区）
    HitCondDown = 6,      // 受击类武学需求次数 −Value（下限 1）
    ShieldUp = 7,         // 护盾效果 +Value%
    SummonTtl = 8,        // 召唤物存在时间 +Value 秒
    SummonCondDown = 9,   // 召唤类武学需求次数 −Value（下限 1）
    DotUp = 10,           // DoT（叠毒/灼烧）伤害 +Value%
    DotStackUp = 11,      // 叠毒层数上限 +Value
    ShieldKeepUp = 12,    // 护盾保持率 +Value 个百分点（行动时护盾衰减得更少）
}

/// <summary>
/// 装备随机词条效果类型（EquipAffixConfig.Effect）。
/// 与资质词条（<see cref="AffixEffect"/>）分开：那套作用于"人"，这套只作用于"装备带来的修正"，
/// 数值预算与封顶规则完全不同（见 EquipTuning）。
/// </summary>
public enum EquipAffixEffect
{
    None = 0,
    AtkPct = 1,           // 攻击 +Value%
    HpPct = 2,            // 气血 +Value%
    SpdPct = 3,           // 速度 +Value%
    ProcAction = 4,       // 行动类武学触发概率 +Value 个百分点
    ProcHit = 5,          // 受击类武学触发概率 +Value 个百分点
    CondDownAction = 6,   // 行动类武学需求次数 −Value（下限 1）
    ReplayPct = 7,        // 重放概率 +Value 个百分点（同类合计封顶 +10）
    ReplayLimit = 8,      // 重放上限 +Value（同类合计封顶 +4）
    ShieldPct = 9,        // 护盾效果 +Value%
    WeightDelta = 10,     // 受击权重 +Value（10 基准的绝对值增量，可负）
    CounterUp = 11,       // 开战时武学当前计数 +Value
    ShieldKeepPct = 12,   // 护盾保持率 +Value 个百分点（同类合计封顶 +20）
}

