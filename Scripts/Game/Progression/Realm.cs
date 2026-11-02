// 文件：Realm.cs
// 《轮回大侠》M3 境界 —— 此生的纵向乘区
// 武侠名头八档：初入江湖 → 三流身手 → 二流好手 → 一流高手 → 绝顶高手 → 一代宗师 → 武林神话 → 天下第一
// （用户 2026-11-01："境界用武侠风的，不用仙侠风的"——炼气/筑基/金丹那套是修真，不是江湖）
//
// 口径（GDD §4.2 五乘区之②「境界乘区 / 此生纵向 / 转世归零」）：
//   · 每境 ×10，玩家 **HP / ATK / DEF 同时乘**本值 —— 一跳一个数量级，玩家能感知
//   · 与敌方 1.16^层 配合，每境约对应 40~50 层的推进空间
//   · **突破门槛 = 银两 + 已达最高层数**，两者都满足才能破
//   · 转世时**归零**（Realm.Reset）—— 这是"此生纵向"的定义，也是转世的"失去"部分
//
// 为什么门槛不用"修为"：《境界与天赋树 v0.1》写的是"修为 5 千"，但 GDD §4.1 定死
//   「修为 = 经验，攒满自动升级，不留存」，它没有余额可扣。GDD 的银两 sink 列表里
//   本来就列了"突破境界"，所以门槛落在银两上（文档冲突以 GDD 为准）。

using System;
using System.Collections.Generic;

public sealed class Realm
{
    static readonly List<RealmConfig> _table = new List<RealmConfig>();
    static int _id = 1;

    /// <summary>灌入境界表（按 Id 升序）。幂等，可重复调用。</summary>
    public static void Init(IEnumerable<RealmConfig> all)
    {
        _table.Clear();
        if (all == null)
        {
            return;
        }
        foreach (RealmConfig c in all)
        {
            if (c == null)
            {
                continue;
            }
            _table.Add(c);
        }
        _table.Sort(SortById);
        if (Find(_id) == null && _table.Count > 0)
        {
            _id = _table[0].Id;
        }
    }

    static int SortById(RealmConfig a, RealmConfig b)
    {
        return a.Id.CompareTo(b.Id);
    }

    static RealmConfig Find(int id)
    {
        foreach (RealmConfig c in _table)
        {
            if (c.Id == id)
            {
                return c;
            }
        }
        return null;
    }

    /// <summary>表里最大的境界（= 满境）。</summary>
    public static RealmConfig Last
    {
        get
        {
            if (_table.Count == 0)
            {
                return null;
            }
            return _table[_table.Count - 1];
        }
    }

    /// <summary>已灌入的境界数（0 = 未 Init，乘区一律按 1 处理）。</summary>
    public static int Count
    {
        get { return _table.Count; }
    }

    /// <summary>当前境界 Id（1 = 初入江湖）。</summary>
    public static int Id
    {
        get { return _id; }
    }

    /// <summary>当前境界行（未 Init 时返回 null）。</summary>
    public static RealmConfig Current
    {
        get { return Find(_id); }
    }

    /// <summary>
    /// 当前**境界乘区**。未 Init / 找不到行时返回 1 —— 宁可少乘也不要让战斗崩掉。
    /// </summary>
    public static double Mult
    {
        get
        {
            RealmConfig cur = Current;
            if (cur == null || cur.Mult <= 0)
            {
                return 1;
            }
            return cur.Mult;
        }
    }

    /// <summary>下一境界行；已满境返回 null。</summary>
    public static RealmConfig Next
    {
        get
        {
            for (int i = 0; i < _table.Count; i++)
            {
                if (_table[i].Id > _id)
                {
                    return _table[i];
                }
            }
            return null;
        }
    }

    /// <summary>是否已满境。</summary>
    public static bool IsMax
    {
        get { return Next == null; }
    }

    /// <summary>还差多少层才够门槛（够了返回 0）。</summary>
    public static int LayerGap(int bestLayer)
    {
        RealmConfig nx = Next;
        if (nx == null)
        {
            return 0;
        }
        int gap = nx.ReqLayer - bestLayer;
        return gap > 0 ? gap : 0;
    }

    /// <summary>还差多少银两才够门槛（够了返回 0）。</summary>
    public static double SilverGap(Silver silver)
    {
        RealmConfig nx = Next;
        if (nx == null || silver == null)
        {
            return 0;
        }
        double gap = nx.ReqSilver - silver.Amount;
        return gap > 0 ? gap : 0;
    }

    /// <summary>
    /// 能否突破：有下一境、层数够、银两够。三道闸任一不满足都返回 false。
    /// </summary>
    public static bool CanBreak(Silver silver, int bestLayer)
    {
        RealmConfig nx = Next;
        if (nx == null || silver == null)
        {
            return false;
        }
        if (bestLayer < nx.ReqLayer)
        {
            return false;
        }
        return silver.Amount >= nx.ReqSilver;
    }

    /// <summary>
    /// 突破：扣银两、进一境。返回 false = 条件不满足（**不扣钱**）。
    /// 扣费放在最后一步，避免"钱扣了但没进位"的半截状态。
    /// </summary>
    public static bool TryBreak(Silver silver, int bestLayer)
    {
        RealmConfig nx = Next;
        if (nx == null || silver == null)
        {
            return false;
        }
        if (bestLayer < nx.ReqLayer)
        {
            return false;
        }
        if (!silver.TrySpend(nx.ReqSilver))
        {
            return false;
        }
        _id = nx.Id;
        return true;
    }

    /// <summary>读档：直接写回境界 Id（非法值夹到表内，找不到就退回第一境）。</summary>
    public static void Restore(int id)
    {
        if (Find(id) != null)
        {
            _id = id;
            return;
        }
        if (_table.Count > 0)
        {
            _id = _table[0].Id;
        }
    }

    /// <summary>转世：境界归零。</summary>
    public static void Reset()
    {
        if (_table.Count > 0)
        {
            _id = _table[0].Id;
        }
        else
        {
            _id = 1;
        }
    }
}
