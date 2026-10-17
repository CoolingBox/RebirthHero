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

using System;
using System.Collections.Generic;
using System.Linq;

public sealed class Cultivation
{
    readonly Dictionary<int, int> _level = new();    // heroId -> 等级（缺省 1）
    readonly Dictionary<int, double> _pool = new();  // heroId -> 当前级内的修为池

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
    public double TotalGained { get; private set; }

    /// <summary>本场累计掉落（用于战斗中实时显示，战斗开始时清零）。</summary>
    public double BattleGained { get; private set; }

    public int LevelOf(int heroId) => _level.TryGetValue(heroId, out int l) ? l : 1;
    public double PoolOf(int heroId) => _pool.TryGetValue(heroId, out double e) ? e : 0;

    /// <summary>该侠客当前级内的修为进度（0~1；满级恒为 1）——给 UI 画进度用。
    /// 注意：战斗中途**不刷新**（升级未结算），故进度可能显示为 >100%，UI 侧自行 clamp。</summary>
    public double ProgressOf(int heroId)
    {
        int l = LevelOf(heroId);
        if (l >= CultivationConfig.MaxLevel) return 1.0;
        double need = CultivationConfig.LevelCost(l);
        return need <= 0 ? 0 : PoolOf(heroId) / need;
    }

    /// <summary>整队总等级（成长进度的聚合指标）。</summary>
    public int TotalLevels(IEnumerable<int> heroIds) => heroIds.Sum(LevelOf);

    /// <summary>战斗开始：清空"本场掉落"计数。</summary>
    public void BeginBattle() => BattleGained = 0;

    /// <summary>
    /// **击杀即时掉落**：给一组侠客各记一份修为进池子（不升级）。
    /// 战斗中途可以随便调；等级不会动，故不会属性混乱。
    /// </summary>
    public double AddExp(IEnumerable<int> heroIds, double perHead)
    {
        if (perHead <= 0) return 0;
        int n = 0;
        foreach (int id in heroIds)
        {
            _pool[id] = PoolOf(id) + perHead;
            TotalGained += perHead;
            BattleGained += perHead;
            n++;
        }
        return n > 0 ? perHead * n : 0;
    }

    /// <summary>某个敌人被击杀时调用：按层给每名参战者一份修为。</summary>
    public double GainForKill(IEnumerable<int> heroIds, int layer)
        => AddExp(heroIds, CultivationConfig.GainPerKill(layer));

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
            double e = PoolOf(id);

            int from = lv;
            while (lv < CultivationConfig.MaxLevel)
            {
                double need = CultivationConfig.LevelCost(lv);
                if (e < need) break;
                e -= need;
                lv++;
            }
            if (lv >= CultivationConfig.MaxLevel) e = 0;   // 满级后不再累积

            _level[id] = lv;
            _pool[id] = e;
            if (lv > from) ups.Add(new LevelUp(id, from, lv));
        }
        return ups;
    }

    public void Reset()
    {
        _level.Clear();
        _pool.Clear();
        TotalGained = 0;
        BattleGained = 0;
    }
}
