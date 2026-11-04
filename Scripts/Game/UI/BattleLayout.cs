// 文件：BattleLayout.cs
// 《轮回大侠》战斗界面布局常量 —— **单一真值**
//
// 为什么单独抽出来：
//   HeroCardLayer 与 SummonStackLayer 都要用同一组卡尺寸（堆叠卡要贴着卡列外侧排），
//   两处写死意味着改一次卡宽要记得改两个文件，漏改就是"堆叠卡压在侠客卡上"或"飘在半空"。
//
// 尺寸推导（1920×1080 视口）：
//   上方导航 0~72、战况行 78~114、结算栏 940 起、底部按钮 1024 起。
//   可用竖直区间 120 ~ 928（808px）：
//     敌方一排 386 + 中间 36 + 我方一排 386 = 808 ✓
//   竖版卡是 7:9，所以宽 = 386 × 7/9 ≈ 300。
//   一排 4 张：4×300 + 3×26 = 1278，居中后左右各余 321px —— 正好给召唤物堆叠卡留出侧边栏。
//
// 为什么是"两排各一字排开"而不是 8 张排成一排：
//   8 张竖卡并排时每张只有 216px 宽，血条里的数字、名字、武学名全部读不清。
//   分排后每张 300×386，字能看清，且敌上我下本身就把"谁打谁"讲明白了。

public static class BattleLayout
{
    // —— 侠客卡（7:9 竖版视差卡）——
    public const float CardW = 300f;
    public const float CardH = 386f;
    public const float CardGap = 26f;

    /// <summary>每排的卡数（4 位侠客）。</summary>
    public const int RowCount = 4;

    // —— 两排的顶边 ——
    public const float EnemyRowY = 120f;
    public const float AllyRowY = 542f;

    // —— 卡内件（BattleParallaxCard 内部用，这里只保留对外需要共享的两个）——
    public const float CardPadX = 8f;
    public const float HpBarH = 18f;
    public const float GaugeH = 6f;

    // —— 召唤物堆叠卡（贴屏幕左右边距，按阵营分列）——
    public const float StackW = 240f;
    public const float StackH = 64f;
    public const float StackGap = 8f;
    public const float StackMargin = 28f;

    /// <summary>一排卡的总宽（4 张 + 3 个缝）。</summary>
    public static float RowWidth
    {
        get { return CardW * RowCount + CardGap * (RowCount - 1); }
    }

    /// <summary>一排卡的起始 X（水平居中）。</summary>
    public static float RowX(float areaX)
    {
        return (areaX - RowWidth) * 0.5f;
    }
}
