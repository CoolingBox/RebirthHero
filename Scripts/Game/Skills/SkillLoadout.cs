// 文件：SkillLoadout.cs
// 《轮回大侠》M2.4 武学装配 —— 每个侠客 **3 个武学槽** 的装配状态
//
// 口径（GDD §151："每个侠客 3 个武学槽，武学从通用池自由装配"）：
//   · 3 槽；0 = 空槽
//   · 初始 = HeroConfig.SkillIds 的默认武学（现有数据每人 2 个 → 天然留 1 个空槽给玩家填）
//   · 免费、随时可换 —— 验收信号 A 要求"30 分钟内主动调整过武学"，任何成本都会抑制尝试
//   · 同一侠客不可重复装同一武学（否则同一技能双倍触发，破坏强度基线）
//   · 池 = 全部 Status=Implemented 的武学；**不按 Tier 限制**（TierCap 只约束敌方，见 SkillConfig.Tier 注释）
//
// 纯 C#，无 Godot 依赖 → 无头探针可直接编译验证。

using System.Collections.Generic;

public sealed class SkillLoadout
{
    public const int Slots = 3;
    public const int Empty = 0;

    readonly Dictionary<int, int[]> _byHero = new();

    /// <summary>按 HeroConfig.SkillIds 初始化（不足 3 个补 0，超出截断）。</summary>
    public void Init(IEnumerable<HeroConfig> heroes)
    {
        _byHero.Clear();
        if (heroes == null) return;
        foreach (HeroConfig h in heroes)
        {
            if (h == null) continue;
            _byHero[h.Id] = BuildDefault(h.SkillIds);
        }
    }

    static int[] BuildDefault(int[] ids)
    {
        var arr = new int[Slots];
        if (ids == null) return arr;
        int n = ids.Length < Slots ? ids.Length : Slots;
        for (int i = 0; i < n; i++) arr[i] = ids[i];
        return arr;
    }

    /// <summary>取该侠客的 3 槽（返回内部数组，调用方只读）。未登记返回全 0。</summary>
    public int[] Get(int heroId)
    {
        if (_byHero.TryGetValue(heroId, out int[] arr)) return arr;
        return new int[Slots];
    }

    /// <summary>
    /// 把武学装到指定槽。若该武学已在此侠客的别的槽 → 先移除（同侠客不重复）。
    /// slot 越界 / skillId 非法 → false。
    /// </summary>
    public bool Equip(int heroId, int slot, int skillId)
    {
        if (slot < 0 || slot >= Slots || skillId <= 0) return false;

        if (!_byHero.TryGetValue(heroId, out int[] arr))
        {
            arr = new int[Slots];
            _byHero[heroId] = arr;
        }
        for (int i = 0; i < Slots; i++)
            if (arr[i] == skillId) arr[i] = Empty;

        arr[slot] = skillId;
        return true;
    }

    /// <summary>清空指定槽。</summary>
    public bool Clear(int heroId, int slot)
    {
        if (slot < 0 || slot >= Slots) return false;
        if (!_byHero.TryGetValue(heroId, out int[] arr)) return false;
        arr[slot] = Empty;
        return true;
    }

    /// <summary>该侠客是否已装某武学。</summary>
    public bool Has(int heroId, int skillId)
    {
        if (!_byHero.TryGetValue(heroId, out int[] arr)) return false;
        for (int i = 0; i < Slots; i++)
            if (arr[i] == skillId) return true;
        return false;
    }
}
