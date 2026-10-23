// 文件：EnemyFactory.cs
// 《轮回大侠》M2.1 推图 —— 波次生成器（**表驱动**）
//
// 数据源：EnemyConfig.xlsx → Resources/Config/EnemyConfig.json → EnemyConfig 生成类。
// **模板自带属性与成长**（2026-10-15 用户口径："敌方也要设定基础属性，层级成长"）：
//   属性(层) = Base × Rate^(层 − 1)     ← BaseHp/BaseAtk/BaseDef + HpRate/AtkRate/DefRate
//   每个模板有自己的基础与成长曲线，不再靠"全局曲线 × 模板倍率"两层相乘。
//
// 编成（2026-10-15 v5.2 用户口径："起始要刷 4 波普通敌人才打层主（原 5 波，2026-10-17 全体 −1）" + "普通层的层主是精英"）：
//   一个【层】= N 波**纯小怪**普通波 + 1 波**收尾首领**（N 见 StageConfig.MobWaves）
//   收尾波： 普通层 = **1 精英 + 3 小怪**      层主层（每 10 层）= **1 层主 + 1 精英 + 2 小怪**
//   （v5.0 的"每层都有精英"依然成立——精英只是从"首发"挪到了"收尾"）
//
// 武学：
//   · 精英 / 层主 —— **技能组固定**（EnemyConfig.Skills 列写死武学 Id，不随层变化）
//   · 小怪 —— 数量 = SkillCount + (层−1)/SkillTier；从【已实装全池】按层**品阶上限**随机抽
//     （TierCap(层) = min(4, 1+(层−1)/10)，层 31+ 全开）
//
// 抽取规则（确定性，无隐藏随机）：
//   ① 候选 = Kind 匹配 且 MinLayer ≤ 当前层 → 取 MinLayer 最大的那一档（"当前档位"）；
//   ② 档内按 Id 定序，用整数哈希派生的确定性索引【不重复】抽取；候选不足满员时循环补。

using System;
using System.Collections.Generic;
using System.Linq;

public static class EnemyFactory
{
    /// <summary>是否层主层。</summary>
    public static bool IsBossLayer(int layer) => layer > 0 && layer % StageConfig.BossEvery == 0;

    /// <summary>
    /// 敌方武学池 = 全部【已实装】武学，不限定流派。只保留两条硬过滤：
    ///   ① Status=已实装（待实装 = 引擎未实现其机制）；
    ///   ② 效果词 ≤14（与 SkillConfigMapper 的过滤口径一致）。
    /// 按 Id 定序 → 抽取可复现。
    /// </summary>
    public static List<SkillConfig> BuildSkillPool(List<SkillConfig> all)
        => (all ?? new List<SkillConfig>())
            .Where(s => s.Status == SkillStatus.Implemented && (int)s.EffectType <= 14)
            .OrderBy(s => s.Id)
            .ToList();

    /// <summary>
    /// 本层的【武学品阶上限】（用户口径："为武学评级，低层搭载弱一点的武学，高层不限制"）：
    /// `1 + (层−1)/10`，最高 4 —— 层 1~10 只出品阶 1、11~20 ≤2、21~30 ≤3、**31+ 全开（= 不限制）**。
    /// 只作用于【小怪】的随机抽卡；精英/层主用固定技能组，不走这里。
    /// </summary>
    public static int TierCap(int layer) => Math.Min(4, 1 + Math.Max(0, layer - 1) / 10);

    /// <summary>本层小怪实际可抽的武学池（按 TierCap 过滤；过滤后为空则回退全池）。</summary>
    public static List<SkillConfig> PoolForLayer(List<SkillConfig> pool, int layer)
    {
        int cap = TierCap(layer);
        var limited = (pool ?? new List<SkillConfig>()).Where(s => s.Tier <= cap).ToList();
        return limited.Count > 0 ? limited : (pool ?? new List<SkillConfig>());
    }

    /// <summary>模板在 layer 层的属性（就是 EnemyConfig 表里的值；三只全局 Scale 已于 2026-10-16 归 1，乘法为恒等）。</summary>
    public static (double hp, double atk, double def) Stats(EnemyConfig t, int layer)
    {
        if (t == null) return (0, 0, 0);
        double n = Math.Max(0, layer - 1);
        return (t.BaseHp * Math.Pow(t.HpRate, n),
                t.BaseAtk * Math.Pow(t.AtkRate, n),
                t.BaseDef * Math.Pow(t.DefRate, n));
    }

    /// <summary>小怪的携带武学数 = SkillCount(基数) + (layer−1)/SkillTier。</summary>
    public static int SkillCountOf(EnemyConfig t, int layer)
    {
        if (t == null) return 0;
        int n = Math.Max(0, t.SkillCount);
        if (t.SkillTier > 0) n += Math.Max(0, layer - 1) / t.SkillTier;
        return n;
    }

    /// <summary>本层某类型的"档位"（= 候选里最大的 MinLayer）；无候选返回 0。</summary>
    public static int TierOf(int layer, List<EnemyConfig> templates, EnemyKind kind)
    {
        var cand = (templates ?? new List<EnemyConfig>())
            .Where(t => t.Kind == kind && t.MinLayer <= layer).ToList();
        return cand.Count == 0 ? 0 : cand.Max(t => t.MinLayer);
    }

    /// <summary>
    /// 生成第 layer 层第 <paramref name="waveIndex"/> 波敌人（1 起，共 <see cref="StageConfig.WavesInLayer"/> 波）。
    /// 编成（v5.2 用户口径："起始要刷 4 波普通敌人才打层主（原 5 波，2026-10-17 全体 −1）" + "普通层的层主是精英"）：
    ///   · **普通波**（waveIndex &lt; 总波数）—— 纯 <see cref="StageConfig.WaveSize"/> 个小怪，无首领
    ///   · **收尾波**（waveIndex == 总波数）—— 普通层 = 1 精英 + 3 小怪；层主层 = 1 层主 + 1 精英 + 2 小怪
    /// </summary>
    public static List<BattleUnit> BuildWave(int layer, int waveIndex, bool isFinisher,
                                            List<EnemyConfig> templates, List<SkillConfig> skillPool)
    {
        var list = new List<BattleUnit>();
        if (templates == null || templates.Count == 0) return list;

        var picked = new List<EnemyConfig>();
        if (isFinisher)
        {
            // ① 层主（仅层主层，固定占第 1 位）
            if (IsBossLayer(layer)) picked.AddRange(PickTier(layer, templates, EnemyKind.Boss, 1, waveIndex));
            // ② 精英（普通层的收尾首领 —— 口径："普通层的层主是精英"）
            picked.AddRange(PickTier(layer, templates, EnemyKind.Elite, 1, waveIndex));
        }
        // ③ 余位补小怪（普通波全是小怪）
        int need = StageConfig.WaveSize - picked.Count;
        if (need > 0) picked.AddRange(PickTier(layer, templates, EnemyKind.Mob, need, waveIndex));

        // 小怪按层限制品阶；精英/层主用固定组，**不受品阶限制**（组里写什么就是什么）
        var poolAtLayer = PoolForLayer(skillPool, layer);

        for (int i = 0; i < picked.Count; i++)
            list.Add(Make(picked[i], layer, i + 1, poolAtLayer, skillPool));

        return list;
    }

    /// <summary>取某类型【当前档位】（MinLayer ≤ layer 中最大的一档）的模板 n 个；档内不重复。</summary>
    public static List<EnemyConfig> PickTier(int layer, List<EnemyConfig> templates, EnemyKind kind, int need, int salt = 0)
    {
        var res = new List<EnemyConfig>();
        if (templates == null || need <= 0) return res;

        var cand = templates.Where(t => t.Kind == kind && t.MinLayer <= layer).ToList();
        if (cand.Count == 0) return res;

        int top = cand.Max(t => t.MinLayer);
        var tier = cand.Where(t => t.MinLayer == top).OrderBy(t => t.Id).ToList();

        // salt = 波序号 → 同一层的各普通波取到不同面貌（否则整层小怪一模一样）
        foreach (int idx in PickIndices(tier.Count, need, Mix(Mix(layer, salt), (int)kind * 131)))
            res.Add(tier[idx]);
        for (int k = 0; res.Count < need; k++) res.Add(tier[k % tier.Count]);
        return res;
    }

    static BattleUnit Make(EnemyConfig t, int layer, int slot, List<SkillConfig> pool, List<SkillConfig> fullPool)
    {
        var (hp, atk, def) = Stats(t, layer);
        var u = new BattleUnit
        {
            Name = t.Name,
            Slot = slot,
            BaseHp = hp * BattleConfig.HpScale,
            BaseAtk = atk * BattleConfig.AtkScale,
            BaseDef = def * BattleConfig.DefScale,
            BaseSpd = t.Spd,
            BaseW = t.BaseW > 0 ? t.BaseW : BattleConfig.OtherW,   // 敌方无 1 号位偏置（见 StageRules）
            IsSummon = false,
        };
        u.Hp = u.BaseHp;
        u.Skills = new List<BattleSkill>();

        // 精英/层主：Skills 固定组（走全池，不受品阶限制）；小怪：按层随机抽（pool 已按品阶过滤）
        List<SkillConfig> configs = (t.Skills != null && t.Skills.Length > 0)
            ? IdsToConfigs(t.Skills, fullPool)
            : PickFromPool(pool, SkillCountOf(t, layer), layer, slot);

        // 被动·属性型：与玩家同一层处理，必须在生成战斗技能之前
        foreach (SkillConfig pc in configs)
            if (SkillConfigMapper.IsPassiveAttribute(pc)) SkillConfigMapper.ApplyPassive(pc, u);

        foreach (SkillConfig cfg in configs)
        {
            // 属性型被动的主效果已在上面应用过；副效果（属性 + 机制的身份卡）仍要建
            if (SkillConfigMapper.IsPassiveAttribute(cfg))
            {
                BattleSkill only = SkillConfigMapper.ToAltSkill(cfg, u.BaseHp);
                if (only != null) u.Skills.Add(only);
                continue;
            }
            // 一张卡可能生成 1~2 个 BattleSkill（主效果 + 副效果）
            List<BattleSkill> built = SkillConfigMapper.ToBattleSkills(cfg, u.BaseHp);
            for (int k = 0; k < built.Count; k++) u.Skills.Add(built[k]);
        }
        return u;
    }

    /// <summary>固定技能组：按 Id 取（池已过滤；取不到则跳过该槽，便于把待实装卡先写进表）。</summary>
    static List<SkillConfig> IdsToConfigs(int[] ids, List<SkillConfig> pool)
    {
        var res = new List<SkillConfig>();
        if (ids == null || pool == null) return res;
        foreach (int id in ids)
        {
            SkillConfig hit = pool.FirstOrDefault(s => s.Id == id);
            if (hit != null) res.Add(hit);
        }
        return res;
    }

    static List<SkillConfig> PickFromPool(List<SkillConfig> pool, int count, int layer, int slot)
    {
        var res = new List<SkillConfig>();
        if (pool == null || pool.Count == 0 || count <= 0) return res;
        foreach (int idx in PickIndices(pool.Count, count, Mix(layer, slot)))
            res.Add(pool[idx]);
        return res;
    }

    // ---------- 确定性抽取（同 seed 永远同一组，便于复现与对比调参）----------

    /// <summary>从 [0,size) 确定性抽 count 个【互不相同】的下标。</summary>
    static List<int> PickIndices(int size, int count, int seed)
    {
        var res = new List<int>();
        if (size <= 0 || count <= 0) return res;

        int n = Math.Min(count, size);
        int start = Mix(seed, 17) % size;
        int step = 1 + Mix(seed, 91) % Math.Max(1, size - 1);
        int guard = 0;
        while (Gcd(step, size) != 1 && guard++ < size) step++;   // 与 size 互质 → 遍历全池且天然不重复

        var used = new HashSet<int>();
        int idx = start;
        guard = 0;
        while (res.Count < n && guard++ < size * 4)
        {
            if (used.Add(idx)) res.Add(idx);
            idx = (idx + step) % size;
        }
        return res;
    }

    static int Gcd(int a, int b)
    {
        while (b != 0) { int t = a % b; a = b; b = t; }
        return Math.Max(1, a);
    }

    /// <summary>整数哈希混合（确定性；不用 string.GetHashCode —— 它带进程随机化种子）。</summary>
    static int Mix(int a, int b)
    {
        unchecked
        {
            uint h = (uint)a * 0x9E3779B1u ^ (uint)b * 0x85EBCA6Bu;
            h ^= h >> 15;
            h *= 0xC2B2AE35u;
            h ^= h >> 13;
            return (int)(h & 0x7FFFFFFF);
        }
    }
}
