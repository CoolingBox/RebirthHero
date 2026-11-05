// 文件：DajieRule.cs
// 《轮回大侠》**战斗规则层 · 大劫** —— 定时上门的 Boss 战：什么时候来、来的是谁、赢给什么
//
// 口径（用户 2026-11-01 / 11-02）：
//   · 每 2 分钟一场，**Boss 战不算入时间**（暂停由 BattleClock 管，这里只管节奏参数）
//   · 打赢 → 记一次胜利、下一场更强（`DajieConfig.PowerOf`）、发悬赏
//   · 打输 → 转世（见 RebirthRule）
//
// 为什么"来的是谁"要经过这里而不是直接调 DajieFactory：
//   `DajieFactory.Build` 只负责"表里有就造"，**表没灌入时要回退到当前层的收尾波**——
//   这个兜底是玩法决策（宁可打一场加强版小怪，也不让游戏因为缺表而崩），
//   属于战斗规则，不该散在面板的 if/else 里。
//
// 本文件不引用 Godot（无头探针可直接编译）。

using System;
using System.Collections.Generic;

/// <summary>一场大劫的悬赏清单（已入账）。面板只负责把它翻成结算栏文案。</summary>
public struct DajieReward
{
    /// <summary>这一档是否有奖励入账（false = 表里这一行什么都没配）。</summary>
    public bool Any;
    /// <summary>道行（唯一跨世货币）。</summary>
    public double Dao;
    /// <summary>给每名参战者的修为。</summary>
    public double ExpPerHead;
    /// <summary>整队共享的银两。</summary>
    public double Silver;
    public int MatTier;
    public int MatCount;
    /// <summary>实发装备件数（模板没配 / 池为空时会少发）。</summary>
    public int EquipCount;
    /// <summary>最后一件装备的名字（发 1 件时面板直接报名字）。</summary>
    public string EquipName;
    /// <summary>这场奖励带来的升级（面板当场展示）。</summary>
    public List<Cultivation.LevelUp> LevelUps;
}

public static class DajieRule
{
    /// <summary>
    /// 大劫间隔的天赋乘区（第二棵树「阅历·沉着」）。
    /// 为什么在玩家侧乘而不是改 <c>DajieConfig.DajieInterval</c>：那是全局静态，
    /// 探针与敌方节奏也读它；天赋是玩家独有的加成，只能在玩家侧的调用点乘。
    /// </summary>
    public static double IntervalMul()
    {
        double mul = Talent.Bonus().DajieIntervalMul;
        if (mul <= 0)
        {
            return 1;
        }
        return mul;
    }

    /// <summary>实际间隔（秒，含天赋乘区与 30 秒下限）。</summary>
    public static double Interval()
    {
        return DajieConfig.IntervalWith(IntervalMul());
    }

    /// <summary>距下一场还有多少秒（顶部倒计时用）。</summary>
    public static double Remain(double elapsed)
    {
        return DajieConfig.RemainWith(elapsed, IntervalMul());
    }

    /// <summary>
    /// 造这一场的大劫对手。
    /// <para>
    /// 优先走独立表（18 档、属性写死绝对值）；表没灌入时**回退到当前层的收尾波**并按
    /// <c>PowerOf(wins)</c> 加强 —— 宁可打一场"加强版小怪"，也不让大劫因为缺表而静默不发生。
    /// </para>
    /// <returns>敌方阵容；null = 连回退波次都造不出来（面板应跳过这一场，别开空战）。</returns>
    /// <param name="waveBuilder">回退用的波次构建器（面板传 <c>BuildEnemyWave</c>）。</param>
    public static List<BattleUnit> BuildFoes(int layer, int wins, List<SkillConfig> skillPool,
                                             Func<int, int, bool, List<BattleUnit>> waveBuilder)
    {
        // ⚠️ skillPool 必须是**全池**：小怪池按 EffectType ≤ 14 过滤，会让 Boss 的
        //    按盾加伤（26）/ 护盾保持率（19）被静默跳过，Boss 变成只会普攻的沙包。
        BattleUnit boss = DajieFactory.Build(layer, wins, skillPool);
        if (boss != null)
        {
            List<BattleUnit> foes = new List<BattleUnit>();
            foes.Add(boss);
            return foes;
        }

        if (waveBuilder == null)
        {
            return null;
        }

        List<BattleUnit> wave = waveBuilder(layer, 1, true);
        if (wave == null || wave.Count == 0)
        {
            return null;
        }

        double mul = DajieConfig.PowerOf(wins);
        foreach (BattleUnit f in wave)
        {
            f.BaseHp *= mul;
            f.Hp = f.BaseHp;
            f.BaseAtk *= mul;
            f.BaseDef *= mul;
        }
        return wave;
    }

    /// <summary>
    /// 打赢大劫 → 发悬赏（道行 + 修为 + 银两 + 材料 + 装备），并结算这一场的升级。
    /// <para>
    /// 核心是 <c>RewardKill</c> 这个"**等效击杀数**"：资源类奖励都按它 × 当前层的单杀产出换算。
    /// 为什么不给每档写死绝对值：普通产出本身是 2.5 × 1.16^层 的指数，写死的奖励在第 30 层
    /// 就是零头（玩家还不如去刷两场小怪）。用"这一场相当于多少杀"表示，一个数字撑起三样资源，
    /// 且永远按结算时的层换算 —— 不通胀也不贬值。
    /// </para>
    /// <para>
    /// 天赋「天机」的 <c>DajieRewardMul</c> 在这一处统一放大（道行 / 资源 / 装备件数同享），
    /// 不散到三个字段各乘一次 —— 否则以后加奖励项一定会漏。
    /// </para>
    /// </summary>
    public static DajieReward Grant(BattleRuleContext ctx, DajieBossConfig stage, int layer)
    {
        DajieReward r = new DajieReward();
        r.LevelUps = new List<Cultivation.LevelUp>();
        if (!ctx.Ready || stage == null)
        {
            return r;
        }
        if (layer < 1)
        {
            layer = 1;
        }

        TalentBonus tb = Talent.Bonus();
        double mul = tb.DajieRewardMul > 0 ? tb.DajieRewardMul : 1;

        // ① 道行：唯一跨世资产，转世不清零
        if (stage.RewardDao > 0)
        {
            r.Dao = stage.RewardDao * mul;
            ctx.Dao.Add(r.Dao);
            r.Any = true;
        }

        int baseKills = stage.RewardKill > 0 ? stage.RewardKill : 0;
        int kills = (int)Math.Round(baseKills * mul);
        if (kills > 0)
        {
            // ② 修为：每名参战者各一份，与普通击杀同一口径（进池不升级）
            //    天赋「苦修」的乘区照样要吃 —— 悬赏只是"等效击杀数"，产出加成不例外
            r.ExpPerHead = CultivationConfig.GainPerKill(layer) * kills * tb.CultMul;
            ctx.Cult.AddExp(ctx.RosterIds(), r.ExpPerHead);

            // ③ 银两：整队共享一份账户（吃天赋「经营」）
            r.Silver = CultivationConfig.SilverPerKill(layer) * kills * tb.SilverMul;
            ctx.Money.Add(r.Silver);

            // ④ 材料：品阶跟当前层段走，数量 = 等效击杀数 × 单杀掉落
            r.MatTier = MaterialCatalog.TierOf(layer);
            r.MatCount = MaterialConfig.MatMobDrop * kills;
            ctx.Materials.Add(r.MatTier, r.MatCount);
            r.Any = true;
        }

        // ⑤ 装备：普通怪只有概率，大劫**必掉** —— 这是"打赢"最直接的实物反馈。
        //    件数用 ceil 而不是 round：+25% 加在 1 件上时 round 会回到 1（等于没加），
        //    ceil 才会多给一件 —— 玩家点完「天机」要能立刻看到区别。
        int equipCount = (int)Math.Ceiling(stage.RewardEquip * mul);
        if (equipCount > 0)
        {
            for (int i = 0; i < equipCount; i++)
            {
                EquipConfig tpl = EquipDropper.Roll(layer, ctx.Rng, ctx.EquipPool, tb.RarityBoost);
                if (tpl == null)
                {
                    continue;
                }

                List<int> affixes = EquipDropper.RollAffixes(tpl.Rarity, ctx.Rng, ctx.AffixPool);
                ctx.Equips.Add(tpl.Id, affixes);
                r.EquipCount++;
                r.EquipName = tpl.Name;
            }
            if (r.EquipCount > 0)
            {
                r.Any = true;
            }
        }

        // 修为是即时进池、升级仍要"一场结束才结算"（中途升级会让已建好的单位与新等级脱节）。
        // 大劫这一场到此就结束了，所以在发完奖后立刻结算一次，让玩家当场看到升级。
        r.LevelUps = ctx.Cult.SettleLevelUps(ctx.RosterIds());
        return r;
    }
}
