// 文件：HeroBuilder.cs
// 《轮回大侠》配置接入层 —— HeroConfig(配置表) → BattleUnit(引擎)
// 全局预算（Slot1W/OtherW/Slot1HpBonus）与 1~40 级成长在此统一应用；
// 三只全局 Scale（AtkScale/HpScale/DefScale）已于 2026-10-16 全部归 1（恒等）——
// 即建出来的单位属性 = HeroConfig 表里的值（顶多再乘 1 号位 1.3 气血），没有别的乘数。

using System;
using System.Collections.Generic;

public static class HeroBuilder
{
    /// <summary>
    /// 建一个战斗单位。slot = 1..4（1 号位享受血量加成与 ×3 权重）；level = 1..40（成长曲线见框架 §11）。
    /// skillLookup：按 SkillConfig.Id 取配置（ConfigManager.GetOne 或字典）。
    /// quality：该侠客**当世的资质**（M2.3 客栈；null = 无资质基准版）。
    ///   资质必须在**技能装配之前**应用 —— 因为"气血上限%"类武学的数值是按 BaseHp 折算成绝对量的。
    /// includePending=false 时跳过 Status=待实装 的卡（引擎尚未实现其机制）。
    /// equipOf：该侠客四栏装备的属性合计（M2.5）。**基础数值加算**到成长结果上，
    ///   与 GDD §4.2「③装备乘区」的基础层一致。
    /// tuningOf：该侠客的**套装 + 随机词条**修正（P1）。属性% 在资质之后、召唤代价之前应用；
    ///   武学参数在技能装配完成之后应用（见 <see cref="EquipSets"/>）。
    /// </summary>
    public static BattleUnit Build(HeroConfig hero, int slot, Func<int, SkillConfig> skillLookup, int level = 1,
                                   bool includePending = false,
                                   HeroQuality quality = null, Func<int, QualityConfig> affixLookup = null,
                                   Func<int, int[]> skillIdsOf = null,
                                   Func<int, EquipBonus> equipOf = null,
                                   Func<int, EquipTuning> tuningOf = null)
    {
        if (hero == null) return null;

        // M2.4 武学装配：默认用表里的 SkillIds；面板装配后由 skillIdsOf 提供 3 槽（0 = 空槽）
        int[] ids = skillIdsOf != null ? skillIdsOf(hero.Id) : hero.SkillIds;

        // M2.5 装备：只取一次，避免同一侠客重复遍历四栏
        EquipBonus eq = new EquipBonus();
        if (equipOf != null) eq = equipOf(hero.Id);

        double hpBonus = slot == 1 ? BattleConfig.Slot1HpBonus : 1.0;
        double slotW = slot == 1 ? BattleConfig.Slot1W : BattleConfig.OtherW;
        // BaseW 是"10 基准"的单体基础权重；1 号位按 Slot1W/OtherW 的比例（=3）放大
        double w = hero.BaseW > 0 ? hero.BaseW * (slotW / BattleConfig.OtherW) : slotW;

        // 纯加算成长：基础值(L1) + 每级成长 × (L−1)，三属性各自独立
        // 装备加成同为加算，接在成长结果之后（"生命+120"就是实打实的 120 点上限）
        double hp = HeroGrowth.Hp((double)hero.BaseHP, hero.HpGrowth, level) + eq.Hp;
        double atk = HeroGrowth.Atk((double)hero.BaseATK, hero.AtkGrowth, level) + eq.Atk;
        double def = HeroGrowth.Def((double)hero.BaseDEF, hero.DefGrowth, level);

        var u = new BattleUnit
        {
            Name = hero.Name,
            Slot = slot,
            BaseHp = hp * hpBonus * BattleConfig.HpScale,
            BaseAtk = atk * BattleConfig.AtkScale,
            BaseDef = def * BattleConfig.DefScale,
            BaseSpd = hero.BaseSPD + eq.Spd,  // SPD 不随等级成长，装备是它的主要来源（境界后续接）
            BaseW = w,
            IsSummon = hero.IsSummon,
        };
        u.Hp = u.BaseHp;

        // —— 当世资质（M2.3 客栈）：星级 / 成长率 / 词条 —— 必须在技能装配前应用
        quality?.ApplyTo(u, affixLookup);

        // —— P1 装备：套装 + 随机词条 ——
        // 属性% 必须早于下面的"召唤武学代价"：代价按最终最大生命算，否则血条与锁死段对不上
        EquipTuning tune = tuningOf != null ? tuningOf(hero.Id) : null;
        if (tune != null) EquipSets.ApplyStats(tune, u);

        // —— 被动·属性型（2026-10-22）：与装备属性同一层，必须早于"召唤武学代价" ——
        // 否则最大生命 −15% 会按旧基数算，血条与锁死段对不上。
        if (ids != null)
            foreach (int id in ids)
            {
                if (id <= 0) continue;
                SkillConfig pc = skillLookup?.Invoke(id);
                if (pc == null) continue;
                if (!includePending && pc.Status == SkillStatus.Pending) continue;
                if (SkillConfigMapper.IsPassiveAttribute(pc)) SkillConfigMapper.ApplyPassive(pc, u);
            }

        // —— 召唤武学代价（2026-10-17 用户）：每携带 1 个召唤类武学，最大生命 −15%（叠加）——
        // 锁死部分记入 LockedHp，由血条 UI 以银灰色显示；惩罚基于"已算好的最终最大生命"（含 1 号位加成与资质）。
        int nSummon = CountSummonSkills(ids, skillLookup, includePending);
        if (nSummon > 0 && u.BaseHp > 0)
        {
            double lockPct = Math.Min(0.85, BattleConfig.SummonHpPenaltyPerSkill * nSummon);  // 0.85 = 安全上限，避免极端配置下暴毙/除零
            u.LockedHp = u.BaseHp * lockPct;
            u.BaseHp -= u.LockedHp;
            u.Hp = u.BaseHp;
        }

        u.Skills = new List<BattleSkill>();
        if (ids != null)
            foreach (int id in ids)
            {
                // 0 = 空槽（SkillLoadout.Empty）。空槽是合法的装配状态，不该去查表 ——
                // 查了会命中 GetOne 的"找不到ID为 0"报错，每个空槽刷一条。
                if (id <= 0) continue;

                SkillConfig cfg = skillLookup?.Invoke(id);
                if (cfg == null) continue;
                if (!includePending && cfg.Status == SkillStatus.Pending) continue;
                // 属性型被动的主效果已在上面按"装备属性"那一层应用过，不再生成主技能；
                // 但 T4 身份卡可以带**副效果**（属性 + 机制），那一支仍然要建（2026-10-22）
                if (SkillConfigMapper.IsPassiveAttribute(cfg))
                {
                    BattleSkill only = SkillConfigMapper.ToAltSkill(cfg, u.BaseHp);
                    if (only != null) { ApplyQualityToSkill(only, cfg, quality); u.Skills.Add(only); }
                    continue;
                }
                // 一张卡可能生成 1~2 个 BattleSkill（主效果 + 副效果，见金刚不坏）
                List<BattleSkill> built = SkillConfigMapper.ToBattleSkills(cfg, u.BaseHp);
                for (int k = 0; k < built.Count; k++)
                {
                    ApplyQualityToSkill(built[k], cfg, quality);
                    u.Skills.Add(built[k]);
                }
            }

        // —— P1 装备：武学参数修正（必须等技能全都建好；召唤存在时间等改动只作用于本单位）——
        if (tune != null) EquipSets.ApplySkills(tune, u.Skills);

        return u;
    }

    /// <summary>
    /// 资质词条对**武学数值**的两处修正（在装配期一次算完，引擎只管执行）：
    /// <list type="bullet">
    /// <item>**治疗量加成**（词条"妙手回春"）→ 只作用于 <see cref="SkillEff.Heal"/>；</item>
    /// <item>**武学倍率加成**（词条"剑心通明"）→ 只作用于 <see cref="SkillEff.Dmg"/>，并按**相性**匹配
    /// （<c>SkillConfig.Category</c> vs <c>QualityConfig.Category</c>；词条侧为 0 = 不限相性）。</item>
    /// </list>
    /// 直接乘在 <see cref="BattleSkill.Mult"/> 上 —— 伤害与治疗都走引擎的 <c>AmountOf</c> 结算，
    /// 所以"倍数"与"绝对量"两种语义都能被正确放大，不需要再往引擎里加钩子。
    /// </summary>
    static void ApplyQualityToSkill(BattleSkill sk, SkillConfig cfg, HeroQuality q)
    {
        if (q == null) return;
        if (sk.Eff == SkillEff.Heal && q.HealPct != 0)
            sk.Mult *= 1 + q.HealPct / 100.0;
        if (sk.Eff == SkillEff.Dmg)
        {
            double f = q.MulBonusFor(cfg.Category);
            if (f != 1.0) sk.Mult *= f;
        }
    }

    /// <summary>统计该侠客阵容里"召唤类武学"的数量（<see cref="SkillEffect.Summon"/>）。</summary>
    static int CountSummonSkills(int[] ids, Func<int, SkillConfig> skillLookup, bool includePending)
    {
        if (ids == null) return 0;
        int n = 0;
        foreach (int id in ids)
        {
            // 0 = 空槽，跳过（同上：空槽不查表）
            if (id <= 0) continue;

            SkillConfig cfg = skillLookup?.Invoke(id);
            if (cfg == null) continue;
            if (!includePending && cfg.Status == SkillStatus.Pending) continue;
            if (cfg.EffectType == SkillEffect.Summon) n++;
        }
        return n;
    }

    /// <summary>整队构建：按数组顺序分配 slot 1..N。</summary>
    public static List<BattleUnit> BuildTeam(IEnumerable<HeroConfig> heroes, Func<int, SkillConfig> skillLookup,
                                             int level = 1, bool includePending = false,
                                             Func<int, HeroQuality> qualityOf = null, Func<int, QualityConfig> affixLookup = null,
                                             Func<int, int[]> skillIdsOf = null,
                                             Func<int, EquipBonus> equipOf = null,
                                             Func<int, EquipTuning> tuningOf = null)
    {
        var list = new List<BattleUnit>();
        int slot = 1;
        foreach (HeroConfig h in heroes)
        {
            BattleUnit u = Build(h, slot, skillLookup, level, includePending, qualityOf?.Invoke(h.Id), affixLookup, skillIdsOf, equipOf, tuningOf);
            if (u != null) { list.Add(u); slot++; }
        }
        return list;
    }

    /// <summary>整队构建（**每侠客各自等级**，M2.2 修为经济起用；返回该侠客等级的委托）。</summary>
    public static List<BattleUnit> BuildTeam(IEnumerable<HeroConfig> heroes, Func<int, SkillConfig> skillLookup,
                                             Func<int, int> levelOf, bool includePending = false,
                                             Func<int, HeroQuality> qualityOf = null, Func<int, QualityConfig> affixLookup = null,
                                             Func<int, int[]> skillIdsOf = null,
                                             Func<int, EquipBonus> equipOf = null,
                                             Func<int, EquipTuning> tuningOf = null)
    {
        var list = new List<BattleUnit>();
        int slot = 1;
        foreach (HeroConfig h in heroes)
        {
            int lv = levelOf?.Invoke(h.Id) ?? 1;
            BattleUnit u = Build(h, slot, skillLookup, lv, includePending, qualityOf?.Invoke(h.Id), affixLookup, skillIdsOf, equipOf, tuningOf);
            if (u != null) { list.Add(u); slot++; }
        }
        return list;
    }
}
