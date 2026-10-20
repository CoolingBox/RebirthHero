// 文件：EquipItem.cs
// 《轮回大侠》M2.5 装备 —— 装备实例与背包（纯 C#，无 Godot 依赖，可独立探针验证）
//
// 分工：
//   · EquipConfig   —— 静态模板（名字/栏位/稀有度/基础属性），来自 EquipConfig.xlsx
//   · EquipItem     —— 运行时实例：模板 Id + 强化等级 + 唯一 Uid
//   · EquipBag      —— 持有列表 + 每侠客 4 栏穿戴
//   · EquipRules    —— 数值规则（强化倍率与费用），单独一个文件
//
// 口径（GDD §4.2 ③装备乘区）：
//   **基础属性是数值、加到英雄基础属性上（加算）**；
//   套装/词条的**百分比**才进"装备乘区"（乘算）——后者属 P1，本轮不实现。
//   所以这里给的是 +ATK / +HP / +SPD 的绝对值，不是百分比。
//
// 穿戴模型：装备只在 Items 里存一份，穿戴记录的是 Uid。
//   换装不销毁旧装（只是该栏指向新的 Uid），卸下同理 —— 避免"换下的装备人间蒸发"。

using System;
using System.Collections.Generic;

/// <summary>一件装备的运行时实例。</summary>
public sealed class EquipItem
{
    public int Uid;        // 唯一实例 id（同一模板可有多件）
    public int ConfigId;   // EquipConfig.Id
    public int Level;      // 强化等级（0 = 未强化）
}

/// <summary>四栏装备给出的属性合计（加算到英雄基础属性）。</summary>
public struct EquipBonus
{
    public double Atk;
    public double Hp;
    public double Spd;
}

public sealed class EquipBag
{
    public const int SlotCount = 4;

    readonly List<EquipItem> _items = new List<EquipItem>();
    readonly Dictionary<int, int[]> _worn = new Dictionary<int, int[]>();
    int _nextUid = 1;

    /// <summary>全部持有（含穿在身上的）。只读用途。</summary>
    public List<EquipItem> Items { get { return _items; } }

    public int Count { get { return _items.Count; } }

    /// <summary>掉落获得一件装备（返回实例，便于界面高亮"刚掉的那件"）。</summary>
    public EquipItem Add(int configId)
    {
        EquipItem item = new EquipItem();
        item.Uid = _nextUid;
        item.ConfigId = configId;
        item.Level = 0;
        _nextUid++;

        _items.Add(item);
        return item;
    }

    public EquipItem Find(int uid)
    {
        for (int i = 0; i < _items.Count; i++)
            if (_items[i].Uid == uid) return _items[i];

        return null;
    }

    /// <summary>该侠客的 4 栏（Uid 数组，0 = 空栏）。返回内部数组，调用方只读。</summary>
    public int[] SlotsOf(int heroId)
    {
        int[] slots;
        if (_worn.TryGetValue(heroId, out slots))
            return slots;

        slots = new int[SlotCount];
        _worn[heroId] = slots;
        return slots;
    }

    /// <summary>
    /// 穿到对应栏位（栏位由模板的 Slot 决定，不用调用方指定）。
    /// 失败：装备不存在 / 模板查不到 / 已被别的侠客穿着。
    /// </summary>
    public bool Equip(int heroId, int uid, Func<int, EquipConfig> lookup)
    {
        EquipItem item = Find(uid);
        if (item == null) return false;

        EquipConfig cfg = lookup != null ? lookup(item.ConfigId) : null;
        if (cfg == null) return false;

        int idx = (int)cfg.Slot - 1;
        if (idx < 0 || idx >= SlotCount) return false;

        // 同一件装备不能同时穿在两个人身上（换人会先卸下，这里兜底防脏数据）
        if (IsWornByOther(heroId, uid)) return false;

        SlotsOf(heroId)[idx] = uid;
        return true;
    }

    /// <summary>卸下某栏（装备留在背包里，不会消失）。</summary>
    public bool Unequip(int heroId, EquipSlot slot)
    {
        int idx = (int)slot - 1;
        if (idx < 0 || idx >= SlotCount) return false;

        int[] slots = SlotsOf(heroId);
        if (slots[idx] == 0) return false;

        slots[idx] = 0;
        return true;
    }

    /// <summary>四栏属性合计（加算到英雄基础属性）。</summary>
    public EquipBonus BonusOf(int heroId, Func<int, EquipConfig> lookup)
    {
        EquipBonus bonus = new EquipBonus();

        if (lookup == null) return bonus;

        int[] slots = SlotsOf(heroId);
        for (int i = 0; i < SlotCount; i++)
        {
            if (slots[i] == 0) continue;

            EquipItem item = Find(slots[i]);
            if (item == null) continue;

            EquipConfig cfg = lookup(item.ConfigId);
            if (cfg == null) continue;

            bonus.Atk += EquipRules.AtkOf(item, cfg);
            bonus.Hp += EquipRules.HpOf(item, cfg);
            bonus.Spd += EquipRules.SpdOf(item, cfg);
        }

        return bonus;
    }

    bool IsWornByOther(int heroId, int uid)
    {
        foreach (KeyValuePair<int, int[]> kv in _worn)
        {
            if (kv.Key == heroId) continue;

            int[] slots = kv.Value;
            for (int i = 0; i < SlotCount; i++)
                if (slots[i] == uid) return true;
        }

        return false;
    }

    /// <summary>转世：全部归零（GDD §3.3 账本 —— 装备不跨世）。</summary>
    public void Reset()
    {
        _items.Clear();
        _worn.Clear();
        _nextUid = 1;
    }
}
