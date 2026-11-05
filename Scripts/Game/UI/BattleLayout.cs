// 文件：BattleLayout.cs
// 《轮回大侠》战斗界面布局常量 —— **单一真值**
//
// 为什么单独抽出来：
//   HeroCardLayer 与 SummonStackLayer 都要用同一组卡尺寸（堆叠卡要贴着卡列外侧排），
//   两处写死意味着改一次卡宽要记得改两个文件，漏改就是"堆叠卡压在侠客卡上"或"飘在半空"。
//
// ============================================================
// 站位：1 前 3 后 · 左右对战（2026-11-05 用户："31vs13左右对战" + "都是1前3后"）
//
//   我方（左）                        中间战场                        敌方（右）
//                     [前①]                                  [前①]
//      [后②]                                                 [后②]
//      [后③]                                                 [后③]
//      [后④]                                                 [后④]
//
// **双方阵型完全一样**：1 号位独占前排（靠中线、中间行），2~4 号位在后排一列。
// 「31 vs 13」不是两种阵型，而是**镜像之后读出来的东西不一样**：
//   我方从左往右读 = 外列 3 张 + 内列 1 张 =「3 1」；
//   敌方从左往右读 = 内列 1 张 + 外列 3 张 =「1 3」。
//
// ⚠️ 左右对战的物理代价（必须先认这一条）：
//   屏幕是 16:9。8 张 7:9 竖卡横着排，每张能分的宽度只有约 200px ——
//   卡高只能到 261，比上下对战时的 252×324 再小 20%。要更大的卡只剩两条路：
//     · 每方 2×2（卡能回到 300×386），但阵型就不是 1 前 3 后了；
//     · 把每方 4 张缩到 3 张。
//
// 网格：每方 2 列 × 3 行。前排 = 靠中线的内列中间格；后排 = 外列三格。
//   3 行：3×261 + 2×12 = 807 ≤ 808（120~928）✓
//   两方 2 列：2×203 + 26 = 432，两侧各留 378 给召唤物，中间 300 当战场。
//
// 📏 卡的实际显示尺寸还会被**受击权重**微调（见 HeroCardLayer.ScaleOf）：权重高的卡略大。
//    权重藏得太深（1 号位 30 / 其余 10），玩家看不到"谁更容易挨打"，尺寸是唯一可读的线索。
//    标称 4 人队：前排 ×1.08 / 后排 ×0.967。卡是**中心对齐**格位的，所以放大不往上顶行距；
//    前排与后排横向隔 ColGap，纵向放大也不会互相压。
// ============================================================

using Godot;

public static class BattleLayout
{
    // —— 侠客卡（7:9 竖版视差卡）——
    public const float CardW = 203f;
    public const float CardH = 261f;

    /// <summary>同阵营两列之间的横向间隔 —— 前后两列的视觉缝隙，太窄就糊成一列。</summary>
    public const float ColGap = 26f;

    /// <summary>每队最多几张卡（4 位侠客）。</summary>
    public const int RowCount = 4;

    /// <summary>同一列相邻卡的纵向间隔。</summary>
    public const float RowGap = 12f;

    // —— 三行基准线（都是卡的**顶边**）——
    public const float RowTopY = 120f;

    public static float RowY(int row)
    {
        return RowTopY + row * (CardH + RowGap);
    }

    // —— 四列的左边 X ——
    // 我方：外列（后）在左，内列（前）在右 —— 靠中线的一列是前排。
    // 敌方镜像：内列（前）在左，外列（后）在右。
    // 两侧留 378 给召唤物堆叠卡；两军内列相距 300 —— 再宽就"各站各的"，不像交战。
    public const float AllyBackX = 378f;
    public const float AllyFrontX = AllyBackX + CardW + ColGap;
    /// <summary>敌方内列左边界 —— 由"右侧留白与我方一致"反推，保证画面左右对称。</summary>
    public const float EnemyFrontX = 1920f - AllyBackX - CardW * 2 - ColGap;
    public const float EnemyBackX = EnemyFrontX + CardW + ColGap;

    // —— 卡内件（BattleParallaxCard 内部用，这里只保留对外需要共享的几个）——
    public const float CardPadX = 8f;
    public const float HpBarH = 14f;
    public const float GaugeH = 5f;

    // —— 召唤物堆叠卡（贴屏幕左右边距）——
    public const float StackW = 240f;
    public const float StackH = 64f;
    public const float StackGap = 8f;
    public const float StackMargin = 28f;

    /// <summary>
    /// 某位侠客的卡位（格位左上角）。index = 队内顺序（0-based），不是 Slot。
    /// **双方都是 1 前 3 后**：index 0 独占前排中间行，index 1~3 在后排三行。
    /// 因为左右镜像，画面上左边读作「3 张 | 1 张」、右边读作「1 张 | 3 张」——这就是 31vs13。
    /// </summary>
    public static Vector2 PosOf(bool enemy, int index)
    {
        if (index <= 0)
        {
            return new Vector2(enemy ? EnemyFrontX : AllyFrontX, RowY(1));
        }
        return new Vector2(enemy ? EnemyBackX : AllyBackX, RowY(index - 1));
    }
}
