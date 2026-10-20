// 文件：Inn.cs
// 《轮回大侠》M2.3 客栈 —— 状态机（《侠客与资质》§二「客栈规则」）
//
// 规则：
//   · 栏位数 = `InnConfig.InnSlots`（默认 4，天赋树"客栈扩建"可 +1）
//   · 刷新时机：**每世开始自动刷新**；世内手动刷新 **1 次免费**，之后**付费递增**
//   · 招募费用 = `RecruitBase × (1 + (星−1) × StarCostStep)` → ★1=1×、★5=5×，**扁平不随次数涨**
//   · **已招侠客当世锁定、不可退换**（rogue-lite 的"认命"感）
//
// ⚠️ 资源口径：招募/刷新一律花**银两**。原文档写"招募用修为"，但修为 = 经验、被自动升级即时吃掉
//   （2026-10-15 定的双资源口径）—— 2026-10-16 已把《侠客与资质》§二的三处同步改为银两。
//
// ⚠️ 我的一处默认设定（文档未规定，待你确认）：**开局 4 名是无资质基准版**（星级★1/成长率0/无词条），
//   客栈刷出的才是"带手气的版本"——于是"招募同名侠客"= 用资质版替换基准版，机制自洽且开局强度稳定。

using System;
using System.Collections.Generic;

public sealed class Inn
{
    readonly List<HeroConfig> _unlocked; // 当世可招募的侠客池（已解锁）
    readonly List<QualityConfig> _affixPool; // 资质词条池（QualityConfig 表）
    readonly Func<int, QualityConfig> _affixLookup;

    Random _rng;
    int _seed;

    /// <summary>世内已手动刷新次数（含免费的 1 次）。</summary>
    public int RefreshUsed { get; private set; }

    /// <summary>当前栏位（每个栏位 = 一名可招募侠客 + 其资质）。</summary>
    public readonly List<HeroQuality> Slots = new List<HeroQuality>();

    /// <summary>当世已招（锁定）。</summary>
    public readonly List<HeroQuality> Recruited = new List<HeroQuality>();

    /// <summary>编队（HeroId，最多 <see cref="InnConfig.RosterSize"/> 人）。</summary>
    public readonly List<int> Roster = new List<int>();

    public Inn(List<HeroConfig> unlocked, List<QualityConfig> affixPool, Func<int, QualityConfig> affixLookup)
    {
        if (unlocked != null) _unlocked = unlocked;
        else _unlocked = new List<HeroConfig>();
        if (affixPool != null) _affixPool = affixPool;
        else _affixPool = new List<QualityConfig>();
        _affixLookup = affixLookup;
    }

    // ---------- 每世 ----------

    /// <summary>开一世：清空已招/编队/刷新计数，并**自动刷一次**（免费，不计入手动次数）。</summary>
    public void NewLife(int seed, IEnumerable<int> defaultRoster = null)
    {
        _seed = seed;
        _rng = new Random(seed);
        RefreshUsed = 0;
        Slots.Clear();
        Recruited.Clear();
        Roster.Clear();
        RollSlots();

        if (defaultRoster != null)
        {
            foreach (int id in defaultRoster)
            {
                // 初始侠客以"基准版资质"登记进 Recruited —— 这样下阵后能进候补、上阵槽位也能显示其信息
                if (QualityOf(id) == null) Recruited.Add(HeroQuality.Base(id));
                AddToRoster(id);
            }
        }
    }

    /// <summary>
    /// 读档（M2.6）：直接恢复刷新的栏位 / 已招名单 / 编队 / 刷新次数。
    /// **不走随机** —— 存档里存的是掷好的资质，重开一局必须一模一样。
    /// </summary>
    public void Restore(int refreshUsed, List<HeroQuality> slots, List<HeroQuality> recruited, List<int> roster)
    {
        RefreshUsed = refreshUsed > 0 ? refreshUsed : 0;

        Slots.Clear();
        if (slots != null)
            foreach (HeroQuality q in slots)
                if (q != null) Slots.Add(q);

        Recruited.Clear();
        if (recruited != null)
            foreach (HeroQuality q in recruited)
                if (q != null) Recruited.Add(q);

        Roster.Clear();
        if (roster != null)
            foreach (int id in roster)
            {
                if (id <= 0 || Roster.Contains(id)) continue;
                if (Roster.Count >= InnConfig.RosterSize) break;
                Roster.Add(id);
            }
    }

    // ---------- 刷新 ----------

    /// <summary>下一次手动刷新是否免费。</summary>
    public bool NextRefreshIsFree
    {
        get { return RefreshUsed < InnConfig.InnFreeRefresh; }
    }

    /// <summary>下一次手动刷新的价格（银两）；免费时为 0。</summary>
    public double RefreshCost()
    {
        if (NextRefreshIsFree) return 0;
        int paid = RefreshUsed - InnConfig.InnFreeRefresh; // 第 0、1、2… 次付费
        return InnConfig.InnRefreshBase * Math.Pow(InnConfig.InnRefreshGrowth, paid);
    }

    /// <summary>手动刷新栏位（免费次数内不收钱；之后从银两扣，不足则失败）。</summary>
    public bool TryRefresh(Silver silver)
    {
        double cost = RefreshCost();
        if (cost > 0)
        {
            if (silver == null || !silver.TrySpend(cost)) return false;
        }

        RefreshUsed++;
        Slots.Clear();
        RollSlots();
        return true;
    }

    void RollSlots()
    {
        var pool = new List<HeroConfig>(_unlocked);
        int n = Math.Max(0, InnConfig.InnSlots);
        for (int i = 0; i < n && pool.Count > 0; i++)
        {
            int k = _rng.Next(pool.Count);
            HeroConfig h = pool[k];
            pool.RemoveAt(k); // 同一轮栏位不重复
            Slots.Add(HeroQuality.Roll(h.Id, _rng, _affixPool));
        }
    }

    // ---------- 招募 ----------

    /// <summary>招募费用（银两）= 基准 × 星级系数（★1=1×、★5=5×），**扁平不随次数涨**。</summary>
    public double RecruitCost(HeroQuality q)
    {
        if (q == null) return 0;
        return InnConfig.RecruitBase * (1 + (q.Star - 1) * InnConfig.StarCostStep);
    }

    /// <summary>招募：扣银两 → 进"已招"并自动占编队空位（满了则不自动上阵，由玩家调）。</summary>
    public bool TryRecruit(HeroQuality q, Silver silver)
    {
        if (q == null || q.Recruited) return false;
        double cost = RecruitCost(q);
        if (silver == null || !silver.TrySpend(cost)) return false;

        q.Recruited = true;
        // 同名已存在（初始侠客的基准版）→ 用资质版替换（"招募同名 = 升级基准版"）
        HeroQuality existing = QualityOf(q.HeroId);
        if (existing != null)
        {
            int idx = Recruited.IndexOf(existing);
            Recruited[idx] = q;
        }
        else
        {
            Recruited.Add(q);
        }

        AddToRoster(q.HeroId); // 有空位就上阵
        return true;
    }

    /// <summary>该侠客当世是否**真正招募**（排除开局自带的基准版，用于 UI 置灰）。</summary>
    public bool IsRecruited(int heroId)
    {
        foreach (HeroQuality q in Recruited)
        {
            if (q.HeroId == heroId && !q.IsBase) return true;
        }

        return false;
    }

    /// <summary>已招侠客的资质；未招返回 null。</summary>
    public HeroQuality QualityOf(int heroId)
    {
        foreach (HeroQuality q in Recruited)
        {
            if (q.HeroId == heroId) return q;
        }

        return null;
    }

    // ---------- 编队 ----------

    public bool InRoster(int heroId) => Roster.Contains(heroId);

    public bool AddToRoster(int heroId)
    {
        if (Roster.Count >= InnConfig.RosterSize || Roster.Contains(heroId)) return false;
        Roster.Add(heroId);
        return true;
    }

    public bool RemoveFromRoster(int heroId) => Roster.Remove(heroId);

    /// <summary>在编队里把 heroId 前移/后移一位（dir &lt; 0 向前=靠近 1 号位，dir &gt; 0 向后）。越界返回 false。</summary>
    /// <remarks>站位 = 列表顺序：索引 0 → slot 1（1号位，吃 +30% 气血、×3 承伤权重），依此类推。</remarks>
    public bool MoveRoster(int heroId, int dir)
    {
        int i = Roster.IndexOf(heroId);
        if (i < 0) return false;
        int j = i + (dir < 0 ? -1 : 1);
        if (j < 0 || j >= Roster.Count) return false;
        int tmp = Roster[i];
        Roster[i] = Roster[j];
        Roster[j] = tmp;
        return true;
    }

    /// <summary>清空编队（UI 的"重排"用）。</summary>
    public void ClearRoster() => Roster.Clear();
}