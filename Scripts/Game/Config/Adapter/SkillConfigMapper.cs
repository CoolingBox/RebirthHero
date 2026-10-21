// 文件：SkillConfigMapper.cs
// 《轮回大侠》配置接入层 —— SkillConfig(配置表) → BattleSkill(引擎)
// 依赖方向：Config 层 → Engine 层（引擎不反向依赖配置表；枚举各自独立，见 ConfigEnums.cs 的映射说明）

using System;
using System.Collections.Generic;

/// <summary>把 Excel 生成的 SkillConfig 映射为引擎可执行的 BattleSkill。</summary>
public static class SkillConfigMapper
{
    /// <summary>
    /// 转换一条武学配置，返回**该卡在这一场里会生成的技能列表**。
    /// <para>
    /// 通常只有 1 个；带副效果（AltEffect &gt; 0）的卡会生成 2 个 —— 例如金刚不坏：
    /// 「受击每次 1% 气血护盾」+「受击每 9 次 24% 气血护盾（每场 1 次）」。
    /// 两个 BattleSkill 各自独立判定条件与次数上限，但**在玩家眼里仍是同一张卡**，
    /// 所以装配槽位、品质加成、装备修正都按主效果这一张算。
    /// </para>
    /// ownerBaseHp：把"气血上限%"类效果折算成绝对值（引擎的 Heal/Shield 用绝对量）。
    /// 返回空列表 = 引擎未实装该效果词（复制/污染/光环/蓄力 = SkillEffect 15~18）。
    /// </summary>
    public static List<BattleSkill> ToBattleSkills(SkillConfig cfg, double ownerBaseHp)
    {
        List<BattleSkill> res = new List<BattleSkill>();
        if (cfg == null) return res;

        SkillEff eff;
        if (ToEff(cfg.EffectType, out eff))
        {
            BattleSkill main = Build(cfg, eff, cfg.CalcBase, cfg.CalcValue, cfg.CondN, cfg.Limit, ownerBaseHp, false);
            if (main != null) res.Add(main);
        }

        // 副效果：条件沿用主行的触发时机/CondType，次数与数值各自独立。
        // 只支持"同类第二档"，不支持第二个召唤（一张卡不能召两种东西）。
        SkillEff altEff;
        if ((int)cfg.AltEffect > 0 && ToEff(cfg.AltEffect, out altEff) && altEff != SkillEff.Summon)
        {
            BattleSkill alt = Build(cfg, altEff, cfg.AltCalcBase, cfg.AltCalcValue, cfg.AltCondN, cfg.AltLimit, ownerBaseHp, true);
            if (alt != null) res.Add(alt);
        }

        return res;
    }

    /// <summary>
    /// 建一个技能实例。isAlt = true 时：不吃"不能普攻"（那是主效果决定本次行动是否取消普攻）、
    /// 不重放（否则 24% 那档会被重放成 48%+）、不继承召唤/吸血/附带沉默。
    /// </summary>
    static BattleSkill Build(SkillConfig cfg, SkillEff eff, CalcBase calcBase, double calcValue,
                             int condN, int maxFires, double ownerBaseHp, bool isAlt)
    {
        var s = new BattleSkill
        {
            Name = cfg.Name,
            Src = ToSrc(cfg.TriggerTime),
            Eff = eff,
            Tgt = ToTgt(cfg.TargetType),
            DmgKind = ToDmgKind(cfg.DamageType),
            MaxFires = maxFires,
        };

        if (isAlt)
        {
            s.ReplayP = 0;          // 0 = 不重放（Fire 里 while 条件不成立）
            s.ReplayC = 1;
            s.ReplayLocked = true;
        }
        else
        {
            s.CannotAttack = cfg.CannotAttack;
            s.Vamp = cfg.Vamp;
            s.SilenceAlso = cfg.SilenceAlso;
            s.SummonKind = string.IsNullOrEmpty(cfg.SummonKind) ? null : cfg.SummonKind;
            // 召唤物类型档位（血攻/速度/权重/存在时间/同时上限）来自 SummonConfig 表；
            // 表缺失时引擎侧 SummonSpec.Fallback 兜底。
            s.Summon = SummonCatalog.ToSpec(SummonCatalog.Get(cfg.SummonKind));
            s.SummonMax = cfg.SummonMax;   // 武学级覆盖（0 = 跟随类型档位）
            s.SummonOrder = cfg.SummonOrder;
            s.SummonCount = Math.Max(1, cfg.SummonCount);   // 一次召唤几只（配置缺省/0 → 1 只）
            s.ReplayP = cfg.ReplayChance > 0 ? cfg.ReplayChance / 100.0 : 0.01; // 0 → 全局保底 1%
            s.ReplayC = cfg.ReplayLimit > 0 ? cfg.ReplayLimit : 2;               // 0 → 全局保底 2
            s.ReplayLocked = cfg.ReplayLock;
        }

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
            s.CondN = Math.Max(1, condN);
            s.CondP = cfg.CondP / 100.0;
        }

        s.Mult = ResolveMult(eff, calcBase, calcValue, ownerBaseHp);
        // Mult 的**基准**（2026-10-16 补）：伤害恒以施法者攻击为基准；
        // 治疗/护盾看卡面 CalcBase —— ATK倍数 → 以攻击为基准（结算时 × EffAtk），
        // 气血% / 固定值 → 上面 ResolveMult 已折算成绝对量。见 BattleSkill.MultOnAtk。
        s.MultOnAtk = MultOnAtkOf(eff, calcBase);
        return s;
    }

    /// <summary>
    /// 配置 SkillEffect → 引擎 SkillEff。
    /// <para>
    /// 1~14 与 <see cref="SkillEff"/> 同序（值 −1）；**15 之后不再同序**，必须逐条显式列出 ——
    /// 2026-10-21 加 ShieldKeep 时踩过这个坑：若继续用"值 −1"算索引，
    /// 待实装的 Copy=15 会算到 14，正好撞上新追加的 ShieldKeep。
    /// </para>
    /// 返回 false = 引擎未实装该效果词（复制/污染/光环/蓄力）。
    /// </summary>
    static bool ToEff(SkillEffect e, out SkillEff s)
    {
        int v = (int)e;
        if (v >= 1 && v <= 14)
        {
            s = (SkillEff)(v - 1);
            return true;
        }

        switch (e)
        {
            case SkillEffect.ShieldKeep: s = SkillEff.ShieldKeep; return true;
            default: s = SkillEff.Dmg; return false;
        }
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
    /// <see cref="BattleSkill.MultOnAtk"/>：伤害恒以施法者攻击为基准；治疗/护盾只有"×攻击"才乘；
    /// 其余（权重/修正/DoT/反伤比例/护盾保持率）一律是绝对量。
    /// </summary>
    static bool MultOnAtkOf(SkillEff eff, CalcBase calcBase)
    {
        if (eff == SkillEff.Dmg) return true;
        if (eff == SkillEff.Heal || eff == SkillEff.Shield) return calcBase == CalcBase.AtkMultiple;
        return false;
    }

    /// <summary>
    /// 解析效果数值。**注意返回值的含义有两种**：
    /// ① 倍数（伤害、"×攻击"型治疗/护盾）—— 由 <c>BattleSkill.MultOnAtk = true</c> 标记，引擎结算时再乘施法者攻击；
    /// ② 绝对量（"气血上限%"已在此按 ownerBaseHp 折算；权重/修正/反伤比例本身就是绝对量）—— MultOnAtk = false。
    /// 引擎不得假设 Mult 一定是绝对量（2026-10-16 的"全队护盾 1"就是踩了这个）。
    /// </summary>
    static double ResolveMult(SkillEff eff, CalcBase calcBase, double calcValue, double ownerBaseHp)
    {
        // 按基准折算：气血% → 绝对量；AtkMultiple → 倍数（值÷100）；Fixed → 原值
        double byBase = calcValue;
        if (calcBase == CalcBase.SelfMaxHpPercent) byBase = ownerBaseHp * calcValue / 100.0;
        else if (calcBase == CalcBase.AtkMultiple) byBase = calcValue / 100.0;

        if (eff == SkillEff.Dmg || eff == SkillEff.Heal || eff == SkillEff.Shield) return byBase;

        if (eff == SkillEff.Weight) return calcValue;          // 权重增量（10 基准）
        if (eff == SkillEff.SpdMod) return calcValue / 100.0;  // ±%
        if (eff == SkillEff.AtkMod) return calcValue / 100.0;  // ±%
        if (eff == SkillEff.Burn) return calcValue / 100.0;    // 每秒倍率
        if (eff == SkillEff.ReflectAccum) return calcValue / 100.0;
        if (eff == SkillEff.ShieldKeep) return calcValue / 100.0;   // 百分点 → 小数（15 → 0.15）
        if (eff == SkillEff.Shield2Dmg) return 1.0;
        return 0;                                                 // Poison/Silence/Growth/Dodge/Summon 用引擎默认
    }
}
