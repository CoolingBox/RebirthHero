// 文件：Talent.cs
// 《轮回大侠》M3 天赋树 —— **道行的唯一出口**（GDD §4.1：道行 = 转世折算法币，天赋树 = 唯一 sink）
//
// 拓扑（用户 2026-11-02："从中间开始，4 条主线向 4 处散开，天赋有些可以点多次"）：
//   · 中心「初心」是唯一起点，四支都从它出发
//   · 4 条主线各 6 层，Id 百位 = 分支号、个位 = 深度（101 = 外功第 1 层）
//   · 每支第 1~3 层可重复点（MaxLevel 3~5），第 4~6 层一次性
//   · 前置：第 n 层要求**同支第 n−1 层至少 1 级**（不要求点满 —— 允许浅点铺开）
//
// 为什么前段可重复点：挂机游戏的天赋树要回答"这一世靠什么滚起来"，不是"解锁了哪个按钮"。
//   可重复节点让同一条支线上有"浅点一下"和"死磕到底"两档选择 —— 道行不够时点 1 级也有用，
//   不会出现"差 1 点卡在门口"。
//
// 成本：第 L 级 = CostBase × L（一次性节点 = CostBase）。CostBase 按深度 1/4/16/64/256/1024，
//   单支点满 1515 点、四支 6060 点。[PLACEHOLDER] 依据见 TalentConfig.xlsx 生成脚本注释。
//
// ⚠️ 天赋**跨世永久**（转世不清零）—— 所以本类刻意没有 Reset()。
//   唯一能让等级下降的是将来的"洗点"，那时再补 Revoke 接口。
//
// 纯 C#，无 Godot 依赖 → 无头探针可直接编译验证。

using System;
using System.Collections.Generic;

/// <summary>天赋效果类型（与 TalentConfig.EffectType 一一对应）。</summary>
public enum TalentEff
{
    None = 0,
    /// <summary>全队 ATK +Value%（每级）</summary>
    AtkPct = 1,
    /// <summary>全队 HP +Value%（每级）</summary>
    HpPct = 2,
    /// <summary>全队 DEF +Value%（每级）</summary>
    DefPct = 3,
    /// <summary>全队 SPD +Value%（每级）</summary>
    SpdPct = 4,
    /// <summary>护盾保持率 +Value 点（每级）</summary>
    ShieldKeepPts = 5,
    /// <summary>全队受击权重 +Value（每级；负数 = 更难被打）</summary>
    WeightPts = 6,
    /// <summary>银两产出 +Value%（每级）</summary>
    SilverPct = 7,
    /// <summary>修为产出 +Value%（每级）</summary>
    CultPct = 8,
    /// <summary>装备掉落率 +Value 点（每级，绝对值）</summary>
    DropPts = 9,
    /// <summary>转世道行 +Value%（每级）</summary>
    DaoPct = 10,
    /// <summary>召唤物上限 +Value（一次性）</summary>
    SummonCap = 11,
    /// <summary>该支其余节点的百分比效果 ×(1+Value)（一次性，第 8 层专用）</summary>
    BranchBoost = 12,
    /// <summary>全队**全部伤害** +Value%（每级；BattleUnit.OutDmgMul）</summary>
    DmgPct = 13,
    /// <summary>每名侠客武学槽 +Value（机制，一次性）</summary>
    SkillSlot = 14,
    /// <summary>客栈候选栏位 +Value（机制，一次性）</summary>
    InnSlot = 15,
    /// <summary>客栈刷新费用 −Value%（机制，一次性）</summary>
    InnDiscount = 16,
    /// <summary>大劫打赢的奖励 +Value%（机制，一次性）</summary>
    DajieReward = 17,
    /// <summary>攻/血/防/速 **同时** +Value%（每级；外环「四象」专用）</summary>
    AllAttrPct = 18,
    /// <summary>**装备**提供的属性（HP/ATK/SPD）额外 +Value%（第二棵「兵刃」支）</summary>
    EquipStatPct = 19,
    /// <summary>每世开局的**免费刷新次数** +Value（第二棵「人脉」支）</summary>
    InnFreeRefresh = 20,
    /// <summary>
    /// 大劫间隔 +Value%（负数 = 来得更勤）。
    /// ⚠️ 当前表里**没有节点使用**（原「沉着」已改造成 RarityDrop，见 2026-11-04）——
    /// 保留字段是因为 `DajieConfig.IntervalWith` 需要有地方读乘区，将来做"加速转世"类节点可以直接用。
    /// </summary>
    DajieInterval = 21,
    /// <summary>
    /// **高稀有度装备**的权重放大 +Value%（第二棵「兵刃·神兵 / 阅历·沉着」）。
    /// 按稀有度阶梯生效：第 r 档 ×(1 + boost × (r−1))，白装不变、橙装放大最多。
    /// 只放大本层已有的档位，不会凭空造出这一层本来不掉落的品阶。
    /// </summary>
    RarityDrop = 22,
}

/// <summary>
/// 天赋树的汇总结果 —— 建队（属性）与经济结算（产出）时读这一份，不要各自遍历节点。
/// 乘法字段一律是"1 + 累加"，没点任何天赋时全是 1，等价于"天赋树不存在"。
/// </summary>
public struct TalentBonus
{
    public double AtkMul;
    public double HpMul;
    public double DefMul;
    public double SpdMul;

    /// <summary>护盾保持率的**加点数**（0.05 = +5 点），直接加到 BattleUnit.ShieldKeep 上。</summary>
    public double ShieldKeepAdd;

    /// <summary>受击权重的**加绝对值**（-3 = 权重减 3），直接加到 BattleUnit.BaseW 上。</summary>
    public double WeightAdd;

    public double SilverMul;
    public double CultMul;
    public double DaoMul;

    /// <summary>装备掉落率的**加点数**（0.015 = +1.5%）。</summary>
    public double DropAdd;

    /// <summary>全队**全部伤害**乘区（1 + 累加；对方受伤时由攻击者的 OutDmgMul 生效）。</summary>
    public double DmgMul;

    public int SummonCapAdd;

    /// <summary>每名侠客的武学槽追加数（3 → 4 就是 1）。</summary>
    public int SkillSlotAdd;

    /// <summary>客栈候选栏位追加数。</summary>
    public int InnSlotAdd;

    /// <summary>客栈刷新费用的折扣比例（0.2 = −20%）。</summary>
    public double InnDiscount;

    /// <summary>大劫奖励乘区（1 + 累加；道行/资源/装备件数同享）。</summary>
    public double DajieRewardMul;

    /// <summary>装备属性的乘区（1 + 累加）—— 只放大"装备带来的那部分"，不动基础成长。</summary>
    public double EquipStatMul;

    /// <summary>客栈免费刷新次数追加（整数，第二棵「招牌」）。</summary>
    public int InnFreeAdd;

    /// <summary>大劫间隔乘区（1 + 累加；>1 = 间隔更长，备战更从容）。</summary>
    public double DajieIntervalMul;

    /// <summary>
    /// 高稀有度装备的权重放大比例（0.15 = +15%，按稀有度阶梯生效，见 `EquipDropper.RollRarity`）。
    /// 0 = 不放大（不是 1 —— 这是个"额外加成"，不是乘区）。
    /// </summary>
    public double RarityBoost;

    /// <summary>全 1 的空加成（未灌表 / 一个都没点时用）。</summary>
    public static TalentBonus Identity
    {
        get
        {
            TalentBonus b = new TalentBonus();
            b.AtkMul = 1;
            b.HpMul = 1;
            b.DefMul = 1;
            b.SpdMul = 1;
            b.SilverMul = 1;
            b.CultMul = 1;
            b.DaoMul = 1;
            b.DmgMul = 1;
            b.DajieRewardMul = 1;
            b.EquipStatMul = 1;
            b.DajieIntervalMul = 1;
            return b;
        }
    }
}

public static class Talent
{
    /// <summary>分支数（1 外功 / 2 内功 / 3 身法 / 4 悟性）。</summary>
    public const int BranchCount = 4;

    /// <summary>天赋**树**的数量（1 = 武学树，2 = 江湖树）。</summary>
    public const int TreeCount = 2;

    /// <summary>每支层数（UI 按它算布局；表里改了这里也要改）。</summary>
    public const int DepthCount = 8;

    static readonly List<TalentConfig> _list = new List<TalentConfig>();
    static readonly Dictionary<int, TalentConfig> _byId = new Dictionary<int, TalentConfig>();
    static readonly Dictionary<int, int> _level = new Dictionary<int, int>();

    /// <summary>灌表（幂等）。按 Id 排序，UI 与前置查找都依赖有序。</summary>
    public static void Init(IEnumerable<TalentConfig> all)
    {
        _list.Clear();
        _byId.Clear();
        if (all != null)
        {
            foreach (TalentConfig c in all)
            {
                if (c == null)
                {
                    continue;
                }
                _list.Add(c);
                _byId[c.Id] = c;
            }
        }

        List<TalentConfig> sorted = new List<TalentConfig>(_list);
        sorted.Sort(CompareById);
        _list.Clear();
        foreach (TalentConfig c in sorted)
        {
            _list.Add(c);
        }
    }

    static int CompareById(TalentConfig a, TalentConfig b)
    {
        return a.Id.CompareTo(b.Id);
    }

    /// <summary>表是否已灌入（未灌入时 UI 不该画空树）。</summary>
    public static bool Ready
    {
        get { return _list.Count > 0; }
    }

    /// <summary>全部节点（只读，按 Id 升序）。</summary>
    public static List<TalentConfig> Nodes
    {
        get { return _list; }
    }

    /// <summary>
    /// 某一棵树上的节点（按 Id 升序）。UI 按树画，规则仍按 Id 查。
    /// 阈值取舍：树为空时返回空表，调用方不会画出空白网格。
    /// </summary>
    public static List<TalentConfig> NodesOf(int tree)
    {
        List<TalentConfig> res = new List<TalentConfig>();
        foreach (TalentConfig c in _list)
        {
            if (TreeOf(c) == tree)
            {
                res.Add(c);
            }
        }
        return res;
    }

    /// <summary>节点属于第几棵树（表里没填/填错一律当第 1 棵，老存档不至于失效）。</summary>
    public static int TreeOf(TalentConfig c)
    {
        if (c == null)
        {
            return 1;
        }
        if (c.Tree <= 0)
        {
            return 1;
        }
        return c.Tree;
    }

    /// <summary>
    /// 第 tree 棵树是否已**全部点满**（每个节点都到 MaxLevel）。
    /// 这是下一棵树的解锁条件 —— 用户 2026-11-04："点满第一个天赋树才出现"。
    /// </summary>
    public static bool IsTreeFull(int tree)
    {
        bool any = false;
        foreach (TalentConfig c in _list)
        {
            if (TreeOf(c) != tree)
            {
                continue;
            }
            any = true;
            if (LevelOf(c.Id) < c.MaxLevel)
            {
                return false;
            }
        }
        return any;
    }

    /// <summary>第 tree 棵树是否可见（第 1 棵恒开；第 n 棵要求第 n−1 棵点满）。</summary>
    public static bool IsUnlocked(int tree)
    {
        if (tree <= 1)
        {
            return true;
        }
        return IsTreeFull(tree - 1);
    }

    /// <summary>按 Id 取节点（不存在返回 null）。</summary>
    public static TalentConfig Get(int id)
    {
        TalentConfig c;
        if (_byId.TryGetValue(id, out c))
        {
            return c;
        }
        return null;
    }

    /// <summary>该节点已点几级（未点 = 0）。</summary>
    public static int LevelOf(int id)
    {
        int lv;
        if (_level.TryGetValue(id, out lv))
        {
            return lv;
        }
        return 0;
    }

    /// <summary>是否已点满。</summary>
    public static bool IsMaxed(int id)
    {
        TalentConfig c = Get(id);
        if (c == null)
        {
            return false;
        }
        return LevelOf(id) >= c.MaxLevel;
    }

    /// <summary>
    /// 下一级的成本（道行）。已满级 / 节点不存在 → 0（调用方用 <see cref="IsMaxed"/> 区分）。
    /// <para>
    /// 第 L 级 = `CostBase × CostPow^(L−1)`。第一棵树的 CostPow 全是 1（等价于 `CostBase × L`
    /// 的逐级递增）；第二棵树的末端节点用 CostPow 1.6 —— 那些节点可以点到 99 级，
    /// 线性成本在指数增长的道行面前会变成白送，只有指数成本才是真正的无底洞。
    /// </para>
    /// 返回 double 而不是 int：CostPow 节点的高等级成本会超过 int 上限（约 21 亿）。
    /// </summary>
    public static double CostOf(int id)
    {
        TalentConfig c = Get(id);
        if (c == null)
        {
            return 0;
        }
        int lv = LevelOf(id);
        if (lv >= c.MaxLevel)
        {
            return 0;
        }
        return CostAtLevel(c, lv);
    }

    /// <summary>
    /// 第 lv+1 级的成本（lv = 已点级数）：`CostBase × (lv+1) × CostPow^lv`。
    /// CostPow = 1 时退化成 `CostBase × (lv+1)`（第一棵树的逐级递增）；
    /// CostPow > 1 时还要再乘一个指数（第二棵树的末端无底洞）。
    /// </summary>
    static double CostAtLevel(TalentConfig c, int lv)
    {
        double pow = c.CostPow > 1 ? c.CostPow : 1;
        return c.CostBase * (lv + 1) * Math.Pow(pow, lv);
    }

    /// <summary>
    /// 前置是否满足：ReqA 与 ReqB **都**至少 1 级（ReqB = 0 表示只有一个前置）。
    /// <para>
    /// 为什么改成"表里写 Id"而不是按 Depth 推导：交汇节点要**两个**前置（相邻两支的子路末端），
    /// 推导规则表达不了"与"关系。直接写 Id 也让改图时不用同步改推导逻辑。
    /// </para>
    /// </summary>
    public static bool ReqMet(int id)
    {
        TalentConfig c = Get(id);
        if (c == null)
        {
            return false;
        }
        if (c.ReqA > 0 && LevelOf(c.ReqA) < 1)
        {
            return false;
        }
        if (c.ReqB > 0 && LevelOf(c.ReqB) < 1)
        {
            return false;
        }
        return true;
    }

    /// <summary>道行是否够点下一级（不考虑前置）。</summary>
    public static bool CanAfford(int id, double daoAmount)
    {
        if (IsMaxed(id))
        {
            return false;
        }
        return daoAmount >= CostOf(id);
    }

    /// <summary>能否点：未满级 + 前置满足 + 道行够。</summary>
    public static bool CanLearn(int id, double daoAmount)
    {
        if (IsMaxed(id))
        {
            return false;
        }
        if (!ReqMet(id))
        {
            return false;
        }
        return CanAfford(id, daoAmount);
    }

    /// <summary>
    /// 点一级：扣道行、等级 +1。返回是否成功（失败时状态不变）。
    /// 道行扣减走 <see cref="Dao.TrySpend"/>，天赋树是它唯一的出口。
    /// </summary>
    public static bool TryLearn(int id, Dao dao)
    {
        if (dao == null)
        {
            return false;
        }
        if (!CanLearn(id, dao.Amount))
        {
            return false;
        }

        double cost = CostOf(id);

        // ⚠️ 0 成本的节点（两棵树的**中心**）必须放行：Dao.TrySpend 对 <=0 直接返回 false，
        // 早先这里无条件调用它 → 中心点不动 → 它的前置链（四条主干）全都点不了，
        // 整棵树是死的。这个坑只有真去点第一下才会暴露。
        if (cost > 0 && !dao.TrySpend(cost))
        {
            return false;
        }
        _level[id] = LevelOf(id) + 1;
        return true;
    }

    /// <summary>已投入的总道行（UI 顶部展示"总共投了多少"）。返回 double：末端无底洞会超过 int。</summary>
    public static double TotalSpent()
    {
        double sum = 0;
        foreach (KeyValuePair<int, int> kv in _level)
        {
            TalentConfig c = Get(kv.Key);
            if (c == null)
            {
                continue;
            }
            // 逐级累加而不是用闭式公式：CostPow 节点每级成本都不一样，闭式容易写错
            for (int i = 0; i < kv.Value; i++)
            {
                sum += CostAtLevel(c, i);
            }
        }
        return sum;
    }

    /// <summary>把 boost 累加到"某棵树的某个分支"（键 = 树×10 + 分支）。</summary>
    static void AddBoost(Dictionary<int, double> boost, int tree, int branch, double gain)
    {
        int key = tree * 10 + branch;
        double cur;
        if (boost.TryGetValue(key, out cur))
        {
            boost[key] = cur + gain;
        }
        else
        {
            boost[key] = gain;
        }
    }

    /// <summary>
    /// 汇总当前天赋提供的全部加成。
    /// <para>
    /// 两遍：先算每支的 BranchBoost（第 6 层），再把该支的百分比效果乘上去。
    /// **点数类效果（护盾保持率 / 受击权重 / 掉率 / 召唤上限）不参与 boost** ——
    /// "护盾保持率 +5 点 × 1.3 = 6.5 点"这种数字既难读又难调，百分比加成天然适合叠乘。
    /// </para>
    /// 两棵树的节点都会汇总进来（第 2 棵点不了时它的等级本来就是 0，不影响结果）。
    /// </summary>
    public static TalentBonus Bonus()
    {
        TalentBonus b = TalentBonus.Identity;
        if (_list.Count == 0)
        {
            return b;
        }

        // ① 每支的 boost（默认 1）。**按树隔离**：boost 的键是 `树×10 + 分支`，
        //    否则第 1 棵的「混元」会顺带放大第 2 棵的同号分支 —— 那等于暗中给第二棵树送数值。
        Dictionary<int, double> boost = new Dictionary<int, double>();

        foreach (TalentConfig c in _list)
        {
            if ((TalentEff)c.EffectType != TalentEff.BranchBoost)
            {
                continue;
            }
            int lv = LevelOf(c.Id);
            if (lv <= 0)
            {
                continue;
            }
            if (c.Branch < 1 || c.Branch > BranchCount)
            {
                continue;
            }

            int tree = TreeOf(c);
            double gain = c.Value * lv;
            AddBoost(boost, tree, c.Branch, gain);

            // 交汇节点（ReqB > 0）**同时放大它连接的另一个分支** —— 这就是"交错"的收益：
            // 只走一支的人拿不到交汇，两支都走到的人两支一起变强。
            TalentConfig other = Get(c.ReqB);
            if (other != null && other.Branch != c.Branch
                && other.Branch >= 1 && other.Branch <= BranchCount
                && TreeOf(other) == tree)
            {
                AddBoost(boost, tree, other.Branch, gain);
            }
        }

        // ② 百分比效果：按支累计，再乘该支 boost
        double atk = 0;
        double hp = 0;
        double def = 0;
        double spd = 0;
        double silver = 0;
        double cult = 0;
        double dao = 0;
        double dmg = 0;
        double dajie = 0;
        double equip = 0;
        double dajieInterval = 0;

        foreach (TalentConfig c in _list)
        {
            int lv = LevelOf(c.Id);
            if (lv <= 0)
            {
                continue;
            }

            double gain = c.Value * lv;
            double mul = 1;
            if (c.Branch >= 1 && c.Branch <= BranchCount)
            {
                double v;
                if (boost.TryGetValue(TreeOf(c) * 10 + c.Branch, out v))
                {
                    mul = 1 + v;
                }
            }

            switch ((TalentEff)c.EffectType)
            {
                case TalentEff.AtkPct:
                    atk += gain * mul;
                    break;
                case TalentEff.HpPct:
                    hp += gain * mul;
                    break;
                case TalentEff.DefPct:
                    def += gain * mul;
                    break;
                case TalentEff.SpdPct:
                    spd += gain * mul;
                    break;
                case TalentEff.SilverPct:
                    silver += gain * mul;
                    break;
                case TalentEff.CultPct:
                    cult += gain * mul;
                    break;
                case TalentEff.DaoPct:
                    dao += gain * mul;
                    break;
                case TalentEff.DmgPct:
                    dmg += gain * mul;
                    break;
                case TalentEff.DajieReward:
                    dajie += gain * mul;
                    break;
                case TalentEff.EquipStatPct:
                    equip += gain * mul;
                    break;
                case TalentEff.DajieInterval:
                    dajieInterval += gain * mul;
                    break;
                case TalentEff.AllAttrPct:
                    // 外环「四象」：四种属性同加。不单独存字段 —— 直接并进四条百分比，
                    // 这样建队侧（HeroBuilder）完全不用知道有"四象"这回事。
                    atk += gain * mul;
                    hp += gain * mul;
                    def += gain * mul;
                    spd += gain * mul;
                    break;
                default:
                    break;
            }
        }

        // ③ 点数类与机制类效果：直接相加，不吃 boost（倍率放大"武学槽 +1"没有意义）
        double shieldAdd = 0;
        double weightAdd = 0;
        double dropAdd = 0;
        double innDiscount = 0;
        double rarityBoost = 0;
        int summonAdd = 0;
        int skillSlotAdd = 0;
        int innSlotAdd = 0;
        int innFreeAdd = 0;

        foreach (TalentConfig c in _list)
        {
            int lv = LevelOf(c.Id);
            if (lv <= 0)
            {
                continue;
            }

            switch ((TalentEff)c.EffectType)
            {
                case TalentEff.ShieldKeepPts:
                    shieldAdd += c.Value * lv;
                    break;
                case TalentEff.WeightPts:
                    weightAdd += c.Value * lv;
                    break;
                case TalentEff.DropPts:
                    dropAdd += c.Value * lv;
                    break;
                case TalentEff.SummonCap:
                    summonAdd += (int)Math.Round(c.Value * lv);
                    break;
                case TalentEff.SkillSlot:
                    skillSlotAdd += (int)Math.Round(c.Value * lv);
                    break;
                case TalentEff.InnSlot:
                    innSlotAdd += (int)Math.Round(c.Value * lv);
                    break;
                case TalentEff.InnDiscount:
                    innDiscount += c.Value * lv;
                    break;
                case TalentEff.InnFreeRefresh:
                    innFreeAdd += (int)Math.Round(c.Value * lv);
                    break;
                case TalentEff.RarityDrop:
                    rarityBoost += c.Value * lv;
                    break;
                default:
                    break;
            }
        }

        b.AtkMul = 1 + atk / 100.0;
        b.HpMul = 1 + hp / 100.0;
        b.DefMul = 1 + def / 100.0;
        b.SpdMul = 1 + spd / 100.0;
        b.SilverMul = 1 + silver / 100.0;
        b.CultMul = 1 + cult / 100.0;
        b.DaoMul = 1 + dao / 100.0;
        b.DmgMul = 1 + dmg / 100.0;
        b.DajieRewardMul = 1 + dajie / 100.0;
        b.EquipStatMul = 1 + equip / 100.0;
        b.DajieIntervalMul = 1 + dajieInterval / 100.0;
        b.InnFreeAdd = innFreeAdd;
        b.RarityBoost = rarityBoost / 100.0;
        b.ShieldKeepAdd = shieldAdd / 100.0;
        b.WeightAdd = weightAdd;
        b.DropAdd = dropAdd / 100.0;
        b.SummonCapAdd = summonAdd;
        b.SkillSlotAdd = skillSlotAdd;
        b.InnSlotAdd = innSlotAdd;
        b.InnDiscount = innDiscount / 100.0;
        return b;
    }

    /// <summary>
    /// 洗点：清空全部已点节点，把投入的道行**全额**退回，返回退回点数。
    /// <para>
    /// 为什么全额退：天赋是"局外决策"，点错必须能改 —— 行业共识是"不要把玩家锁死在永久错误选择里"
    /// （skill-tree 设计规范：respec 要么免费带冷却，要么用软货币收费，但必须存在）。
    /// 将来若想收费，把这里改成退 80% 即可，Ui 只认返回值。
    /// </para>
    /// </summary>
    public static double Respec(Dao dao)
    {
        if (dao == null)
        {
            return 0;
        }
        double refund = TotalSpent();
        _level.Clear();
        if (refund > 0)
        {
            dao.Add(refund);
        }
        return refund;
    }

    /// <summary>读档：整份恢复（非法 Id / 超出 MaxLevel 的等级一律丢弃）。</summary>
    public static void Restore(IEnumerable<TalentSave> saves)
    {
        _level.Clear();
        if (saves == null)
        {
            return;
        }

        foreach (TalentSave s in saves)
        {
            if (s == null)
            {
                continue;
            }
            TalentConfig c = Get(s.Id);
            if (c == null)
            {
                continue;
            }

            int lv = s.Level;
            if (lv < 0)
            {
                lv = 0;
            }
            if (lv > c.MaxLevel)
            {
                lv = c.MaxLevel;
            }
            if (lv > 0)
            {
                _level[s.Id] = lv;
            }
        }
    }

    /// <summary>存档快照（只存已点的节点，没点的不落盘）。</summary>
    public static List<TalentSave> Snapshot()
    {
        List<TalentSave> res = new List<TalentSave>();
        foreach (TalentConfig c in _list)
        {
            int lv = LevelOf(c.Id);
            if (lv <= 0)
            {
                continue;
            }
            TalentSave s = new TalentSave();
            s.Id = c.Id;
            s.Level = lv;
            res.Add(s);
        }
        return res;
    }
}

/// <summary>
/// 天赋树的一个已点节点（Id + 等级）。没点过的不入库。
/// 定义在这里而不是 GameSave.cs：它是 Talent 的存档形状，放一起能让 Talent.cs 自包含
/// （将来探针要测天赋规则时，只要 include Talent.cs + TalentConfig.cs，不用连带整份存档模型）。
/// </summary>
public sealed class TalentSave
{
    public int Id;
    public int Level;
}
