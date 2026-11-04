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
    /// <summary>默认可用槽数（天赋「藏经」在它之上做加法）。</summary>
    public const int BaseSlots = 3;

    /// <summary>
    /// **数组硬上限**：内部数组一律按它开长度，天赋只改"可用槽数"。
    /// 为什么分开：天赋可能在游戏中途把槽数从 3 加到 4，如果数组长度也跟着变，
    /// 就要重建每名侠客的数组、还要迁存档 —— 多了一整类 bug。长度固定 5、只挪"可见窗口"最简单。
    /// </summary>
    public const int MaxSlots = 5;

    /// <summary>当前可用槽数（默认 3；天赋「藏经」+1 → 4）。<see cref="MaxSlots"/> 封顶。</summary>
    public static int Slots = BaseSlots;

    public const int Empty = 0;

    readonly Dictionary<int, int[]> _byHero = new();

    /// <summary>按 HeroConfig.SkillIds 初始化（不足补 0，超出截断到当前可用槽数）。</summary>
    public void Init(IEnumerable<HeroConfig> heroes)
    {
        _byHero.Clear();
        if (heroes == null)
        {
            return;
        }
        foreach (HeroConfig h in heroes)
        {
            if (h == null)
            {
                continue;
            }
            _byHero[h.Id] = BuildDefault(h.SkillIds);
        }
    }

    static int[] BuildDefault(int[] ids)
    {
        var arr = new int[MaxSlots];
        if (ids == null)
        {
            return arr;
        }
        int n = ids.Length < Slots ? ids.Length : Slots;
        for (int i = 0; i < n; i++)
        {
            arr[i] = ids[i];
        }
        return arr;
    }

    /// <summary>取该侠客的整段槽位（长度恒为 <see cref="MaxSlots"/>，调用方只读前 <see cref="Slots"/> 个）。</summary>
    public int[] Get(int heroId)
    {
        if (_byHero.TryGetValue(heroId, out int[] arr))
        {
            return arr;
        }
        return new int[MaxSlots];
    }

    /// <summary>
    /// 把武学装到指定槽。若该武学已在此侠客的别的槽 → 先移除（同侠客不重复）。
    /// slot 越界（超过当前可用槽数）/ skillId 非法 → false。
    /// </summary>
    public bool Equip(int heroId, int slot, int skillId)
    {
        if (slot < 0 || slot >= Slots || slot >= MaxSlots || skillId <= 0)
        {
            return false;
        }

        if (!_byHero.TryGetValue(heroId, out int[] arr))
        {
            arr = new int[MaxSlots];
            _byHero[heroId] = arr;
        }
        for (int i = 0; i < MaxSlots; i++)
        {
            if (arr[i] == skillId)
            {
                arr[i] = Empty;
            }
        }

        arr[slot] = skillId;
        return true;
    }

    /// <summary>清空指定槽。</summary>
    public bool Clear(int heroId, int slot)
    {
        if (slot < 0 || slot >= Slots || slot >= MaxSlots)
        {
            return false;
        }
        if (!_byHero.TryGetValue(heroId, out int[] arr))
        {
            return false;
        }
        arr[slot] = Empty;
        return true;
    }

    /// <summary>读档：整槽恢复（不足补 0，非法 id 归空槽）。长度按 MaxSlots 对齐，旧档短数组也吃得下。</summary>
    public void Restore(int heroId, int[] ids)
    {
        if (heroId <= 0)
        {
            return;
        }

        int[] arr = new int[MaxSlots];
        if (ids != null)
        {
            int n = ids.Length < MaxSlots ? ids.Length : MaxSlots;
            for (int i = 0; i < n; i++)
            {
                arr[i] = ids[i] > 0 ? ids[i] : Empty;
            }
        }

        _byHero[heroId] = arr;
    }

    /// <summary>该侠客是否已装某武学。</summary>
    public bool Has(int heroId, int skillId)
    {
        if (!_byHero.TryGetValue(heroId, out int[] arr))
        {
            return false;
        }
        for (int i = 0; i < MaxSlots; i++)
        {
            if (arr[i] == skillId)
            {
                return true;
            }
        }
        return false;
    }
}
