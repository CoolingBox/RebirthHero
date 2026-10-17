// 文件：MaterialCatalog.cs
// 《轮回大侠》材料系统 —— 品阶目录（名称 + 层→档映射）
//
// 为什么单独一个类：`Tuning` 只认 public static 的数值字段（反射注入 + 双向校验），
//   名称数组是"内容"不是"系数"，放进去会让总账校验报类型不匹配。故名称留在这里。

public static class MaterialCatalog
{
    /// <summary>品阶名（下标 0 = 1 档），武侠风递进。扩档只需往后追加。</summary>
    public static readonly string[] Names = { "凡铁", "精铁", "玄铁", "寒铁", "天外玄铁" };

    /// <summary>档位总数。</summary>
    public static int Count => Names.Length;

    /// <summary>层 → 品阶档（1 起；超出最高档则封顶）。</summary>
    public static int TierOf(int layer)
    {
        int l = layer < 1 ? 1 : layer;
        int step = MaterialConfig.MatTierEveryLayer > 0 ? MaterialConfig.MatTierEveryLayer : 10;
        int tier = (l - 1) / step + 1;
        return Clamp(tier, 1, Names.Length);
    }

    /// <summary>档位 → 名称（越界自动夹取）。</summary>
    public static string NameOf(int tier)
    {
        return Names[Clamp(tier, 1, Names.Length) - 1];
    }

    /// <summary>显示用的"层段"文案，如 "11~20 层"。</summary>
    public static string RangeText(int tier)
    {
        int step = MaterialConfig.MatTierEveryLayer > 0 ? MaterialConfig.MatTierEveryLayer : 10;
        int lo = (Clamp(tier, 1, Names.Length) - 1) * step + 1;
        int hi = lo + step - 1;
        return lo + "~" + hi + " 层";
    }

    static int Clamp(int v, int lo, int hi)
    {
        if (v < lo) return lo;
        if (v > hi) return hi;
        return v;
    }
}
