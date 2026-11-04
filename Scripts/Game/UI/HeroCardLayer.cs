// 文件：HeroCardLayer.cs
// 《轮回大侠》表现层 —— **侠客单位卡**层（Godot 节点：Control 子类，挂在 BattlePanel.tscn 的 TCards 上）
//
// 2026-11-04 改版：卡牌从 500×176 横条换成 **7:9 竖版 4 层视差卡**（BattleParallaxCard），
// 布局从"左右各一列"换成"敌上一排 / 我下一排，各自一字排开"。
//
// 本类只做三件事，**不含任何绘制细节**：
//   ① 按阵营与位次算坐标并建卡；
//   ② 把引擎数据转给每张卡刷新；
//   ③ 给飘字/音频提供锚点。
// 卡内部（分层视差、血条拖尾、信息带）全在 BattleParallaxCard 里，改视觉不用碰这里。
//
// 为什么不再自己画血条：旧实现把血条、锁死段、护盾金层、行动条全铺在 500 宽的横条上，
// 换成 300 宽的竖卡后这些件必须重排；让每张卡自己持有自己的节点，
// 刷新时不需要"按索引找回第 3 个 ColorRect"，也不会出现"卡换了布局但血条还在旧坐标"。

using System.Collections.Generic;
using Godot;

public partial class HeroCardLayer : Control
{
    readonly Dictionary<BattleUnit, BattleParallaxCard> _cards = new Dictionary<BattleUnit, BattleParallaxCard>();

    public void Clear()
    {
        foreach (KeyValuePair<BattleUnit, BattleParallaxCard> kv in _cards)
        {
            if (kv.Value != null && IsInstanceValid(kv.Value))
            {
                kv.Value.QueueFree();
            }
        }
        _cards.Clear();
    }

    /// <summary>
    /// A 队（我方，下一排）/ B 队（敌方，上一排）各按位次从左到右建卡；召唤物跳过（归 SummonStackLayer）。
    /// areaX 用视口宽，行宽居中 —— 换分辨率时两排始终在中间。
    /// </summary>
    public void Build(List<BattleUnit> a, List<BattleUnit> b, float areaX)
    {
        float x0 = BattleLayout.RowX(areaX);
        float step = BattleLayout.CardW + BattleLayout.CardGap;

        for (int i = 0; i < BattleLayout.RowCount; i++)
        {
            if (i < a.Count && !a[i].IsSummon)
            {
                BuildOne(a[i], x0 + i * step, BattleLayout.AllyRowY, false);
            }
            if (i < b.Count && !b[i].IsSummon)
            {
                BuildOne(b[i], x0 + i * step, BattleLayout.EnemyRowY, true);
            }
        }
    }

    void BuildOne(BattleUnit u, float x, float y, bool enemy)
    {
        BattleParallaxCard card = new BattleParallaxCard();
        card.Position = new Vector2(x, y);
        card.Size = new Vector2(BattleLayout.CardW, BattleLayout.CardH);
        AddChild(card);

        // Setup 必须在 AddChild 之后：卡面节点是在 _Ready 里建的
        card.Setup(u, enemy, BattleArt.KeyOf(u.Name));
        _cards[u] = card;
    }

    /// <summary>把引擎数据推给每张卡（10Hz，由面板的 Tick 调用）。</summary>
    public void Refresh(BattleEngine engine)
    {
        foreach (KeyValuePair<BattleUnit, BattleParallaxCard> kv in _cards)
        {
            kv.Value.Refresh(engine);
        }
    }

    /// <summary>飘字锚点：卡面上部（人物头顶一带）。</summary>
    public bool TryGetAnchor(BattleUnit u, out Vector2 pos)
    {
        BattleParallaxCard c;
        if (_cards.TryGetValue(u, out c))
        {
            pos = c.Position + new Vector2(BattleLayout.CardW * 0.5f - 60f, 40f);
            return true;
        }

        pos = Vector2.Zero;
        return false;
    }

    /// <summary>同上，但换算到**世界坐标**（供音频定位用；飘字仍用局部坐标）。</summary>
    public bool TryGetWorldAnchor(BattleUnit u, out Vector2 world)
    {
        if (!TryGetAnchor(u, out Vector2 local))
        {
            world = Vector2.Zero;
            return false;
        }

        world = GetGlobalTransform() * local;
        return true;
    }
}
