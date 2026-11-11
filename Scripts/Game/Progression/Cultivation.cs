// 文件：Cultivation.cs
// 《轮回大侠》M2.2 修为（= 经验）—— 每侠客的修为池 + 等级
//
// 两条口径（2026-10-15 用户）：
//   · "击杀就可立即获得经验资源" → 每次击杀立刻把修为记进池子（AddExp）
//   · "升级要等待战斗结束，不然中途升级可能属性混乱" → **升级只在战斗结束时结算**（SettleLevelUps）
//     战斗中途改等级会让本场已建好的单位与新等级脱节 —— 所以"记经验"与"升级"必须分开。
//
// 等级是【每侠客各自】的（框架 §11 的个体成长曲线 k/b/g 各不相同）：
// 同一场战斗里 4 人拿同样的修为，但各自的 Base/Growth 不同，L1→L40 的涨幅并不一样。
//
// 2026-11-06：修为池与累计改 **BigNum**。产出 1.16^层 是纯指数，跑过 1e308 后
// double 变 Infinity —— 池子变 Infinity 会让"升级判定"永远成立（白送满级），
// 变成 NaN 则会永久卡住升级。两者都是坏结局，所以这里也必须用大数。

using System;
using System.Collections.Generic;
using System.Linq;
using TinyFramework;

public sealed class Cultivation
{
    readonly Dictionary<int, int> _level = new Dictionary<int, int>();       // heroId -> 等级（缺省 1）
    readonly Dictionary<int, BigNum> _pool = new Dictionary<int, BigNum>();  // heroId -> 当前级内的修为池

    /// <summary>一次升级事件（UI 提示与探针统计用）。</summary>
    public readonly struct LevelUp
    {
        public readonly int HeroId;
        public readonly int From;
        public readonly int To;
        public LevelUp(int id, int from, int to) { HeroId = id; From = from; To = to; }
        public int Gained => To - From;
    }

    /// <summary>本世累计掉落的修为（全队合计，含已消耗在升级上的）。</summary>
    public BigNum TotalGained { get; private set; } = BigNum.Zero;

    /// <summary>本场累计掉落（用于战斗中实时显示，战斗开始时清零）。</summary>
    public BigNum BattleGained { get; private set; } = BigNum.Zero;

    public int LevelOf(int heroId)
    {
        int lv;
        if (_level.TryGetValue(heroId, out lv))
        {
            return lv;
        }
        return 1;
    }

    public BigNum PoolOf(int heroId)
    {
        BigNum e;
        if (_pool.TryGetValue(heroId, out e))
        {
            return e;
        }
        return BigNum.Zero;
    }

    /// <summary>
    /// 该侠客当前级内的修为进度（0~1；满级恒为 1）——给 UI 画进度用。
    /// 注意：战斗中途**不刷新**（升级未结算），故进度可能显示为 >100%，UI 侧自行 clamp。
    /// </summary>
    public double ProgressOf(int heroId)
    {
        int lv = LevelOf(heroId);
        if (lv >= CultivationConfig.MaxLevel)
        {
            return 1.0;
        }

        BigNum need = CultivationConfig.LevelCost(lv);
        if (need <= BigNum.Zero)
        {
            return 0;
        }
        return (double)(PoolOf(heroId) / need);
    }

    /// <summary>整队总等级（成长进度的聚合指标）。</summary>
    public int TotalLevels(IEnumerable<int> heroIds)
    {
        return heroIds.Sum(LevelOf);
    }

    /// <summary>战斗开始：清空"本场掉落"计数。</summary>
    public void BeginBattle()
    {
        BattleGained = BigNum.Zero;
    }

    /// <summary>
    /// **击杀即时掉落**：给一组侠客各记一份修为进池子（不升级）。
    /// 战斗中途可以随便调；等级不会动，故不会属性混乱。
    /// </summary>
    public BigNum AddExp(IEnumerable<int> heroIds, BigNum perHead)
    {
        if (perHead <= BigNum.Zero)
        {
            return BigNum.Zero;
        }

        int n = 0;
        foreach (int id in heroIds)
        {
            _pool[id] = PoolOf(id) + perHead;
            TotalGained += perHead;
            BattleGained += perHead;
            n++;
        }
        return n > 0 ? perHead * n : BigNum.Zero;
    }

    /// <summary>某个敌人被击杀时调用：按层给每名参战者一份修为。</summary>
    public BigNum GainForKill(IEnumerable<int> heroIds, int layer)
    {
        return AddExp(heroIds, CultivationConfig.GainPerKill(layer));
    }

    /// <summary>
    /// **战斗结束时的升级结算**：把所有攒够修为的侠客一次升到位（可连升）。
    /// 返回本场发生升级的人（无升级则空表）。
    /// </summary>
    public List<LevelUp> SettleLevelUps(IEnumerable<int> heroIds)
    {
        var ups = new List<LevelUp>();
        foreach (int id in heroIds)
        {
            int lv = LevelOf(id);
            BigNum e = PoolOf(id);

            int from = lv;
            while (lv < CultivationConfig.MaxLevel)
            {
                BigNum need = CultivationConfig.LevelCost(lv);
                if (e < need)
                {
                    break;
                }
                e -= need;
                lv++;
            }
            if (lv >= CultivationConfig.MaxLevel)
            {
                e = BigNum.Zero;   // 满级后不再累积
            }

            _level[id] = lv;
            _pool[id] = e;
            if (lv > from)
            {
                ups.Add(new LevelUp(id, from, lv));
            }
        }
        return ups;
    }

    /// <summary>
    /// 读档：直接写入某侠客的等级与级内修为池（**不结算升级** —— 存档保存的就是已结算状态）。
    /// 非法值一律夹到合法区间，避免脏存档把等级写成 0 或负数。
    /// </summary>
    public void Restore(int heroId, int level, BigNum pool)
    {
        if (heroId <= 0)
        {
            return;
        }

        int lv = level;
        if (lv < 1)
        {
            lv = 1;
        }
        if (lv > CultivationConfig.MaxLevel)
        {
            lv = CultivationConfig.MaxLevel;
        }

        BigNum p = pool;
        if (p < BigNum.Zero)
        {
            p = BigNum.Zero;
        }

        _level[heroId] = lv;
        _pool[heroId] = p;
    }

    public void Reset()
    {
        _level.Clear();
        _pool.Clear();
        TotalGained = BigNum.Zero;
        BattleGained = BigNum.Zero;
    }
}
