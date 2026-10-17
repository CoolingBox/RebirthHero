// 文件：SkillConfigMapper.cs
// 《轮回大侠》配置接入层 —— SkillConfig(配置表) → BattleSkill(引擎)
// 依赖方向：Config 层 → Engine 层（引擎不反向依赖配置表；枚举各自独立，见 ConfigEnums.cs 的映射说明）

using System;

/// <summary>把 Excel 生成的 SkillConfig 映射为引擎可执行的 BattleSkill。</summary>
public static class SkillConfigMapper
{
    /// <summary>
    /// 转换一条武学配置。
    /// ownerBaseHp：把"气血上限%"类效果折算成绝对值（引擎的 Heal/Shield 用绝对量）。
    /// 返回 null = 引擎未实装该效果词（复制/污染/光环/蓄力 = SkillEffect 15~18）。
    /// </summary>
    public static BattleSkill ToBattleSkill(SkillConfig cfg, double ownerBaseHp)
    {
        if (cfg == null) return null;
        if ((int)cfg.EffectType > 14) return null;

        var s = new BattleSkill
        {
            Name = cfg.Name,
            Src = ToSrc(cfg.TriggerTime),
            Eff = (SkillEff)((int)cfg.EffectType - 1),   // 与 BattleEnums.SkillEff 同序（值-1）
            Tgt = ToTgt(cfg.TargetType),
            CannotAttack = cfg.CannotAttack,
            Vamp = cfg.Vamp,
            SilenceAlso = cfg.SilenceAlso,
            SummonKind = string.IsNullOrEmpty(cfg.SummonKind) ? null : cfg.SummonKind,
            // 召唤物类型档位（血攻/速度/权重/存在时间/同时上限）来自 SummonConfig 表；
            // 表缺失时引擎侧 SummonSpec.Fallback 兜底。
            Summon = SummonCatalog.ToSpec(SummonCatalog.Get(cfg.SummonKind)),
            SummonMax = cfg.SummonMax,   // 武学级覆盖（0 = 跟随类型档位）
            SummonOrder = cfg.SummonOrder,
            SummonCount = Math.Max(1, cfg.SummonCount),   // 一次召唤几只（配置缺省/0 → 1 只）
            ReplayP = cfg.ReplayChance > 0 ? cfg.ReplayChance / 100.0 : 0.01, // 0 → 全局保底 1%
            ReplayC = cfg.ReplayLimit > 0 ? cfg.ReplayLimit : 2,               // 0 → 全局保底 2
            ReplayLocked = cfg.ReplayLock,
            DmgKind = ToDmgKind(cfg.DamageType),
        };

        // —— 触发条件 ——
        if (cfg.TriggerTime == TriggerType.OnHpBelow)
        {
            s.Cond = "chance";                       // 引擎用 Cond="chance" + CondP 承载血量阈值
            s.CondP = cfg.HpThreshold / 100.0;
        }
        else
        {
            s.Cond = cfg.CondType switch
            {
                CondType.Chance => "chance",
                CondType.Combo => "combo",
                _ => "count",
            };
            s.CondN = Math.Max(1, cfg.CondN);
            s.CondP = cfg.CondP / 100.0;
        }

        s.Mult = ResolveMult(cfg, ownerBaseHp);
        // Mult 的**基准**（2026-10-16 补）：伤害恒以施法者攻击为基准；
        // 治疗/护盾看卡面 CalcBase —— ATK倍数 → 以攻击为基准（结算时 × EffAtk），
        // 气血% / 固定值 → 上面 ResolveMult 已折算成绝对量。见 BattleSkill.MultOnAtk。
        s.MultOnAtk = cfg.EffectType switch
        {
            SkillEffect.Dmg => true,
            SkillEffect.Heal or SkillEffect.Shield => cfg.CalcBase == CalcBase.AtkMultiple,
            _ => false,
        };
        return s;
    }

    static SkillSrc ToSrc(TriggerType t) => t switch
    {
        TriggerType.OnHit => SkillSrc.OnHit,
        TriggerType.OnHpBelow => SkillSrc.OnHpBelow,
        _ => SkillSrc.OnAction,        // OnStart(光环) 引擎未实装（上层按 Status 过滤）
    };

    static DamageKind ToDmgKind(DamageType t) => t switch
    {
        DamageType.Internal => DamageKind.Internal,
        DamageType.TrueDamage => DamageKind.TrueDamage,
        _ => DamageKind.External,      // None（无伤害）与 External 同路，无伤害时不影响
    };

    static SkillTgt ToTgt(SkillTarget t) => t switch
    {
        SkillTarget.Self => SkillTgt.Self,
        SkillTarget.LowestHpFoe => SkillTgt.LowestHpFoe,
        SkillTarget.LowestHpAlly => SkillTgt.LowestHpAlly,
        SkillTarget.HighestWeight => SkillTgt.HighestW,
        SkillTarget.All => SkillTgt.All,
        SkillTarget.Attacker => SkillTgt.Random,   // 引擎暂无"攻击者"索敌，退化处理
        _ => SkillTgt.Random,
    };

    /// <summary>
    /// 解析效果数值。**注意返回值的含义有两种**：
    /// ① 倍数（伤害、"×攻击"型治疗/护盾）—— 由 <c>BattleSkill.MultOnAtk = true</c> 标记，引擎结算时再乘施法者攻击；
    /// ② 绝对量（"气血上限%"已在此按 ownerBaseHp 折算；权重/修正/反伤比例本身就是绝对量）—— MultOnAtk = false。
    /// 引擎不得假设 Mult 一定是绝对量（2026-10-16 的"全队护盾 1"就是踩了这个）。
    /// </summary>
    static double ResolveMult(SkillConfig c, double ownerBaseHp)
    {
        double ByBase() => c.CalcBase switch
        {
            CalcBase.SelfMaxHpPercent => ownerBaseHp * c.CalcValue / 100.0,
            CalcBase.Fixed => c.CalcValue,
            _ => c.CalcValue / 100.0,          // AtkMultiple：倍数×100
        };

        return c.EffectType switch
        {
            SkillEffect.Dmg or SkillEffect.Heal or SkillEffect.Shield => ByBase(),
            SkillEffect.Weight => c.CalcValue,          // 权重增量（10 基准）
            SkillEffect.SpdMod => c.CalcValue / 100.0,  // ±%
            SkillEffect.AtkMod => c.CalcValue / 100.0,  // ±%
            SkillEffect.Burn => c.CalcValue / 100.0,    // 每秒倍率
            SkillEffect.ReflectAccum => c.CalcValue / 100.0,
            SkillEffect.Shield2Dmg => 1.0,
            _ => 0,                                     // Poison/Silence/Growth/Dodge/Summon 用引擎默认
        };
    }
}
