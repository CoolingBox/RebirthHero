// 文件：SampleBuilds.cs
// 《轮回大侠》M1 战斗引擎 —— 样板构筑（M0 六套，数值全 [PLACEHOLDER]）
// 用途：M1 表现层直接驱动这几套对战；M2 起由配置表（HeroConfig/SkillConfig）接管

using System.Collections.Generic;
using System.Linq;
#nullable disable

public static class SampleBuilds
{
    // —— 行动类构造 ——
    static BattleSkill ADmg(string n, int everyN, double mult, SkillTgt t = SkillTgt.Random, bool no = false, double rp = 0.01, int rc = 2, bool locked = true)
        => new() { Name = n, Src = SkillSrc.OnAction, Cond = "count", CondN = everyN, Eff = SkillEff.Dmg, Mult = mult, Tgt = t, CannotAttack = no, ReplayP = rp, ReplayC = rc, ReplayLocked = locked };
    static BattleSkill AP(string n, double p, double mult, SkillTgt t = SkillTgt.Random, bool no = false)
        => new() { Name = n, Src = SkillSrc.OnAction, Cond = "chance", CondP = p, Eff = SkillEff.Dmg, Mult = mult, Tgt = t, CannotAttack = no };
    static BattleSkill AHeal(string n, int everyN, double amount, SkillTgt t = SkillTgt.LowestHpAlly)
        => new() { Name = n, Src = SkillSrc.OnAction, Cond = "count", CondN = everyN, Eff = SkillEff.Heal, Mult = amount, Tgt = t };
    static BattleSkill AShield(string n, int everyN, double amount, SkillTgt t = SkillTgt.All)
        => new() { Name = n, Src = SkillSrc.OnAction, Cond = "count", CondN = everyN, Eff = SkillEff.Shield, Mult = amount, Tgt = t };
    static BattleSkill ASum(string n, int everyN, string kind)
        => new() { Name = n, Src = SkillSrc.OnAction, Cond = "count", CondN = everyN, Eff = SkillEff.Summon, Mult = 1, SummonKind = kind };

    // —— 受击类构造 ——
    static BattleSkill HCount(string n, int everyN, SkillEff eff, double mult, SkillTgt t = SkillTgt.Self)
        => new() { Name = n, Src = SkillSrc.OnHit, Cond = "count", CondN = everyN, Eff = eff, Mult = mult, Tgt = t };
    static BattleSkill HP2(string n, double p, SkillEff eff, double mult, SkillTgt t = SkillTgt.Self)
        => new() { Name = n, Src = SkillSrc.OnHit, Cond = "chance", CondP = p, Eff = eff, Mult = mult, Tgt = t };

    // —— 血量阈值（CondP 存百分比，引擎 Clone 时注册） ——
    static BattleSkill Th(string n, double pct, SkillEff eff, double amount, SkillTgt t = SkillTgt.Self)
        => new() { Name = n, Src = SkillSrc.OnHpBelow, Cond = "chance", CondP = pct, Eff = eff, Mult = amount, Tgt = t };

    static BattleUnit U(string name, int slot, double hp, double atk, double spd, params BattleSkill[] skills)
    {
        double bonus = slot == 1 ? BattleConfig.Slot1HpBonus : 1.0;
        double w = slot == 1 ? BattleConfig.Slot1W : BattleConfig.OtherW;
        return new() { Name = name, Slot = slot, BaseHp = hp * bonus * BattleConfig.HpScale, BaseAtk = atk * BattleConfig.AtkScale, BaseSpd = spd, BaseW = w, Skills = skills.ToList() };
    }

    public static List<(string, BattleUnit[])> All()
    {
        BattleSkill[] tankSkills =
        {
            HCount("铁布衫", 2, SkillEff.Shield, 169),
            HP2("狮子吼", 0.15, SkillEff.Weight, 12),
        };
        BattleSkill[] healWushu = { HCount("龟息功", 3, SkillEff.Heal, 120), AHeal("回风拂柳", 3, 45) };

        var b1 = new BattleUnit[]   // 普攻流：高频行动 + 同放追加
        {
            U("铁震山", 1, 1300, 40, 40, tankSkills),
            U("沈孤鸿", 2, 800, 30, 90, AP("追魂夺命剑", 0.30, 1.5, SkillTgt.LowestHpFoe), AP("燃木刀法", 0.35, 0.6)),
            U("刀客", 3, 900, 55, 55, ADmg("一剑封喉", 3, 2.0), AP("摘星手", 0.25, 1.8)),
            U("苏小药", 4, 800, 30, 50, AHeal("回风拂柳", 3, 45), AShield("金钟罩", 4, 45)),
        };
        var b2 = new BattleUnit[]   // 技能流：不能普攻大AoE + 破招沉默
        {
            U("铁震山", 1, 1300, 40, 40, HCount("金刚不坏", 6, SkillEff.Shield, 507), HP2("狮子吼", 0.15, SkillEff.Weight, 12)),
            U("剑仙", 2, 750, 54, 45, ADmg("神剑御雷真诀", 6, 3.5, SkillTgt.All, no: true), AP("天山折梅手", 0.25, 1.8)),
            U("符师", 3, 700, 47, 45, ADmg("暴雨梨花针", 5, 0.9, SkillTgt.All, no: true), new BattleSkill{ Name="独孤九剑", Src=SkillSrc.OnAction, Cond="chance", CondP=0.20, Eff=SkillEff.Dmg, Mult=2.0, Tgt=SkillTgt.HighestW, SilenceAlso=true, DmgKind=DamageKind.TrueDamage }),
            U("菩提", 4, 750, 30, 50, Th("九阳神功", 0.30, SkillEff.Heal, 300), AHeal("回风拂柳", 3, 45)),
        };
        var b3 = new BattleUnit[]   // 召唤流：召唤物 + 叠毒
        {
            U("铁震山", 1, 1300, 40, 40, tankSkills),
            U("召唤师", 2, 800, 48, 50, ASum("撒豆成兵", 4, "tank"), ASum("御兽诀", 3, "dps")),
            U("毒士", 3, 750, 52, 50, new BattleSkill{ Name="千蛛万毒手", Src=SkillSrc.OnAction, Cond="count", CondN=3, Eff=SkillEff.Poison, Mult=1 }, HP2("毒砂掌", 0.25, SkillEff.Dmg, 0.5)),
            U("医者", 4, 800, 30, 50, healWushu),
        };
        var b4 = new BattleUnit[]   // 重放流
        {
            U("铁震山", 1, 1300, 40, 40, tankSkills),
            U("掌门", 2, 850, 75, 55, ADmg("降龙十八掌", 8, 1.0, no: true, rp: 0.70, rc: 18)),  // rp0.70 = 卡面40% + 降龙套20% + 词条10%；M2 接入装备后拆分
            U("鞭客", 3, 800, 58, 60, ADmg("闪电五连鞭", 5, 0.6, rp: 0.80, rc: 5, locked: false)),
            U("辅助", 4, 800, 35, 50, new BattleSkill{ Name="玄冥神掌", Src=SkillSrc.OnAction, Cond="chance", CondP=0.25, Eff=SkillEff.SpdMod, Mult=-0.2 }, new BattleSkill{ Name="天书残卷", Src=SkillSrc.OnAction, Cond="chance", CondP=0.20, Eff=SkillEff.Growth }, AHeal("回风拂柳", 3, 45)),
        };
        var b5 = new BattleUnit[]   // 反伤坦流
        {
            U("主坦", 1, 2000, 35, 35, HCount("斗转星移", 4, SkillEff.ReflectAccum, 0.6), HP2("沾衣十八跌", 0.30, SkillEff.Dmg, 1.0)),
            U("副坦", 2, 1500, 40, 40, HCount("铁布衫", 2, SkillEff.Shield, 150), new BattleSkill{ Name="移山劲", Src=SkillSrc.OnAction, Cond="count", CondN=5, Eff=SkillEff.Shield2Dmg, Mult=1, CannotAttack=true }),
            U("游侠", 3, 750, 50, 50, AP("天山折梅手", 0.25, 1.8), AP("灵犀一指", 0.15, 4.0, no: true)),
            U("医者", 4, 800, 30, 50, Th("护体罡气", 0.40, SkillEff.Shield, 400), AHeal("回风拂柳", 3, 45)),
        };
        var b6 = new BattleUnit[]   // 连击流：全队速度同步 80
        {
            U("剑首", 1, 1300, 40, 80, tankSkills),
            U("剑侍一", 2, 780, 34, 80, new BattleSkill{ Name="辟邪剑法", Src=SkillSrc.OnAction, Cond="combo", CondP=0.25, Eff=SkillEff.Dmg, Mult=1.8 }, AP("燃木刀法", 0.35, 0.6)),
            U("剑侍二", 3, 780, 34, 80, new BattleSkill{ Name="辟邪剑法", Src=SkillSrc.OnAction, Cond="combo", CondP=0.25, Eff=SkillEff.Dmg, Mult=1.8 }, AP("摘星手", 0.25, 1.8)),
            U("剑侍三", 4, 780, 32, 80, HP2("闪身步", 0.30, SkillEff.Dodge, 0), AHeal("回风拂柳", 3, 45)),
        };
        return new()
        {
            ("普攻流·快刀门", b1), ("技能流·大招", b2), ("召唤流", b3),
            ("重放流", b4), ("反伤坦流", b5), ("连击流·剑阵", b6),
        };
    }
}
