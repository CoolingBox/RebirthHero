// 文件：UiUtil.cs
// 《轮回大侠》表现层 —— UI 共享格式化工具（无状态静态类）
// 抽出原因：大数简写被 BattlePanel（信息栏 / 掉落飘字 / 结算）与 StatsView（战报）共用，
//           放在任一侧都会造成两两耦合，故独立成工具。

public static class UiUtil
{
    /// <summary>大数简写（中文单位）。</summary>
    public static string Fmt(double v)
    {
        if (v >= 1e12) return (v / 1e12).ToString("0.##") + "兆";
        if (v >= 1e8) return (v / 1e8).ToString("0.##") + "亿";
        if (v >= 1e4) return (v / 1e4).ToString("0.##") + "万";
        return v.ToString("N0");
    }
}
