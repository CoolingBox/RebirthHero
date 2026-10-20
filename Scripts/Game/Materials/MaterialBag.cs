// 文件：MaterialBag.cs
// 《轮回大侠》材料系统 —— 库存（品阶 → 数量）
//
// 只做存取，不做消耗判断；消耗端（装备强化 / 武学升阶）在 M2.5 接入。

using System.Collections.Generic;

public sealed class MaterialBag
{
    readonly Dictionary<int, long> _byTier = new();

    /// <summary>入库（tier 从 1 起；非法输入直接忽略）。</summary>
    public void Add(int tier, int count)
    {
        if (tier <= 0 || count <= 0) return;
        _byTier.TryGetValue(tier, out long cur);
        _byTier[tier] = cur + count;
    }

    /// <summary>某档持有量（未登记返回 0）。</summary>
    public long Get(int tier)
    {
        if (_byTier.TryGetValue(tier, out long v)) return v;
        return 0;
    }

    /// <summary>全部档位合计（面板顶部展示用）。</summary>
    public long Total
    {
        get
        {
            long sum = 0;
            foreach (var kv in _byTier) sum += kv.Value;
            return sum;
        }
    }

    /// <summary>读档：直接写入某档位持有量（非法档位/数量忽略）。</summary>
    public void Restore(int tier, long count)
    {
        if (tier <= 0 || count <= 0) return;
        _byTier[tier] = count;
    }

    /// <summary>重开本世：清空。</summary>
    public void Reset()
    {
        _byTier.Clear();
    }
}
