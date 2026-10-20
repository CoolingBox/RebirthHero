// 文件：BattleLayout.cs
// 《轮回大侠》战斗界面布局常量 —— **单一真值**
//
// 为什么单独抽出来：
//   HeroCardLayer 与 SummonStackLayer 原来各自硬编码了同一组数字
//   （CardW 460 / SideX 120 / TopY 140 —— 堆叠卡的定位要贴着侠客卡的右缘，
//   所以它必须知道 CardW）。两处写死意味着改一次卡宽要记得改两个文件，
//   漏改就是"堆叠卡压在侠客卡上"或"飘在半空"。
//   现在只在这里改。
//
// 尺寸推导（1920×1080 视口）：
//   4 张卡竖排：TopY 140 + 4×176 + 3×26 = 922 < 1080 ✓
//   左右各一列：左 120~620，右 1300~1800，中间留 680px 给飘字与未来的战场特效 ✓

using Godot;

public static class BattleLayout
{
    // —— 侠客卡 ——
    public const float CardW = 500f;
    public const float CardH = 176f;
    public const float CardGap = 26f;

    // —— 公共定位 ——
    public const float TopY = 140f;
    public const float SideX = 120f;

    // —— 卡内件（HeroCardLayer 的 BuildOne 与 Refresh 必须用同一组，否则血条与文字错位）——
    public const float CardPadX = 8f;
    public const float HpBarH = 20f;
    public const float GaugeH = 12f;

    // —— 召唤物堆叠卡（贴在侠客卡内侧）——
    public const float StackW = 240f;
    public const float StackH = 64f;
    public const float StackGap = 8f;
    public const float StackMargin = 14f;
}
