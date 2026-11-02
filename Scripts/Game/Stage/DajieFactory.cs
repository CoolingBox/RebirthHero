// 文件：DajieFactory.cs
// 《轮回大侠》M3 大劫 —— 独立 Boss 表的构建器
//
// 口径（用户 2026-11-01："大劫独立 Boss 表" + "大劫Boss设计多点"）：
//   · 大劫**不再复用当前层的收尾波**，而是从 DajieBossConfig 表里选档
//   · **18 档**，按境界阶梯排（初入江湖 → 三流 → 二流 → 一流 → 绝顶）；
//     档位按"本世已打赢几场"切（MinWins），换档时名字/属性/技能一起换 —— 玩家能感到"这次来的是另一个人"
//   · 属性**写死绝对值**（用户口径）：不随推图层成长，也不乘 k^wins
//
// 为什么要 18 档而不是 6 档：6 档时打赢最后一档后 `Pick` 永远返回同一行，
//   玩家要么反复刷同一个 Boss、要么被打不动 —— 中间没有"再往上走一步"的空间。
//   18 档把整条境界阶梯铺开，每一世能爬到哪一档，就是这一世构筑强度的读数。
//
// ⚠️ **传入的 skillPool 必须是全池**（已实装的全部武学），不是小怪池：
//   小怪池按 `EffectType <= 14` 过滤（见 EnemyFactory.BuildSkillPool），
//   Boss 配招里的按盾加伤（26）/ 护盾保持率（19）/ 斩杀（25）会被静默跳过，
//   结果 Boss 只剩普攻。曾踩过：黑道魁首（韦陀献杵 234）和无名剑客（绝禅 195）双双空手。
//
// 为什么只造**一个**单位：大劫是"仇家上门"，不是"一伙人围剿"。
// 一个高属性 Boss 让战斗聚焦（玩家能清楚看到自己在打谁），也和"层主 + 随从"的推图编成区分开。
//
// 本文件不引用 Godot（无头探针可直接编译）。

using System;
using System.Collections.Generic;

public static class DajieFactory
{
    static readonly List<DajieBossConfig> _table = new List<DajieBossConfig>();

    /// <summary>灌入大劫表（按 MinWins 升序）。幂等。</summary>
    public static void Init(IEnumerable<DajieBossConfig> all)
    {
        _table.Clear();
        if (all == null)
        {
            return;
        }
        foreach (DajieBossConfig c in all)
        {
            if (c == null)
            {
                continue;
            }
            _table.Add(c);
        }
        _table.Sort(SortByMinWins);
    }

    static int SortByMinWins(DajieBossConfig a, DajieBossConfig b)
    {
        return a.MinWins.CompareTo(b.MinWins);
    }

    /// <summary>表是否已灌入（未灌入时 BattlePanel 回退到收尾波，不让战斗崩掉）。</summary>
    public static bool Ready
    {
        get { return _table.Count > 0; }
    }

    /// <summary>本世已胜 wins 场时该出哪一档（MinWins ≤ wins 中最大的一档）。</summary>
    public static DajieBossConfig Pick(int wins)
    {
        DajieBossConfig hit = null;
        foreach (DajieBossConfig c in _table)
        {
            if (c.MinWins <= wins)
            {
                hit = c;
            }
        }
        return hit;
    }

    /// <summary>
    /// 造这一场的大劫 Boss。**属性就是表里的绝对值** —— 不随推图层成长，也不乘 k^wins。
    /// 用户 2026-11-01："Boss 写死绝对值，可提前打，而且打赢有奖励"：
    ///   · 写死 = 大劫变成"一道固定的关"，玩家变强就能反复赢它、反复拿奖励
    ///   · 代价是迟早被打穿，所以表尾（第 18 档：1170 万血 / 21 万攻）是这一世的终点
    /// layer 只用于日志与将来的分层表，不参与属性计算。
    /// </summary>
    public static BattleUnit Build(int layer, int wins, List<SkillConfig> skillPool)
    {
        if (!Ready)
        {
            return null;
        }

        DajieBossConfig t = Pick(wins);
        if (t == null)
        {
            return null;
        }

        var u = new BattleUnit
        {
            Name = t.Name,
            Slot = 1,
            BaseHp = t.BaseHp * BattleConfig.HpScale,
            BaseAtk = t.BaseAtk * BattleConfig.AtkScale,
            BaseDef = t.BaseDef * BattleConfig.DefScale,
            BaseSpd = t.Spd,
            BaseW = t.BaseW > 0 ? t.BaseW : BattleConfig.OtherW,
            IsSummon = false,
        };
        u.Hp = u.BaseHp;
        u.Skills = new List<BattleSkill>();

        List<SkillConfig> configs = IdsToConfigs(t.Skills, skillPool);

        // 被动·属性型：与玩家/推图敌人同一层处理，必须早于战斗技能构建
        foreach (SkillConfig pc in configs)
        {
            if (SkillConfigMapper.IsPassiveAttribute(pc))
            {
                SkillConfigMapper.ApplyPassive(pc, u);
            }
        }

        foreach (SkillConfig cfg in configs)
        {
            if (SkillConfigMapper.IsPassiveAttribute(cfg))
            {
                BattleSkill only = SkillConfigMapper.ToAltSkill(cfg, u.BaseHp);
                if (only != null)
                {
                    u.Skills.Add(only);
                }
                continue;
            }
            List<BattleSkill> built = SkillConfigMapper.ToBattleSkills(cfg, u.BaseHp);
            for (int k = 0; k < built.Count; k++)
            {
                u.Skills.Add(built[k]);
            }
        }
        return u;
    }

    /// <summary>固定技能组：按 Id 取（池已过滤；取不到就跳过该槽，方便把待实装卡先写进表）。</summary>
    static List<SkillConfig> IdsToConfigs(int[] ids, List<SkillConfig> pool)
    {
        var res = new List<SkillConfig>();
        if (ids == null || pool == null)
        {
            return res;
        }
        foreach (int id in ids)
        {
            foreach (SkillConfig s in pool)
            {
                if (s != null && s.Id == id)
                {
                    res.Add(s);
                    break;
                }
            }
        }
        return res;
    }
}
