// 文件：GameSave.cs
// 《轮回大侠》M2.6 —— **存档数据模型**（`SaveData` 子类；落盘/读盘由 TinyFramework 的 `SaveManager` 负责）
//
// 分工（与 `SettingData` + `SettingManager` 同一套范式）：
//   · 本文件        数据形状（扁平 DTO，可人肉读改）
//   · GameProgress  抓/灌：状态机 ↔ GameSave（框架管不了这一层）
//   · SaveManager   `Save(data)` / `Load<T>()`：user:// 路径、JSON、字段序列化、缺文件返回 new T()
//   · 文件名         `SaveManager` 默认取 `typeof(T).Name` = user://GameSave，无需显式传名字
//
// 账本口径（GDD §3.3）：
//   · **跨世**（重开本世不清）：武学装配（玩家的构筑选择）、设置（SettingData 单独一份）
//   · **本世**（重开本世清空，但关游戏不丢）：推图层数 / 修为等级 / 银两 / 材料 / 装备 / 客栈
//
// 设计说明：
//   · 全部用 public 字段 —— SaveManager 的 JsonSerializerOptions 开了 IncludeFields
//   · 嵌套结构一律是「扁平 DTO」，不做对象图引用：存档要能被人肉看懂、能手工修
//   · Version 用于将来迁移：读到不认识的版本就整份丢弃，宁可从第 1 层重开，也不要读半份脏数据
//
// ⚠️ 战斗过程本身不存（引擎状态、波内血量、飘字）：存档粒度 = **一场战斗的结算点**。
//   读档后从"存档记录的层 + 波"重新开打本波，这是放置游戏可接受的粒度。

using System.Collections.Generic;
using TinyFramework;

/// <summary>一份存档（文件名固定 GameSave）。</summary>
public sealed class GameSave : SaveData
{
    public const int CurrentVersion = 1;

    /// <summary>存档格式版本；结构变动时 +1，读档侧按版本决定迁移或丢弃。</summary>
    public int Version = CurrentVersion;

    /// <summary>
    /// "这份数据是真的抓出来的"标记 —— **不能省**。
    /// `SaveManager.Load&lt;T&gt;` 在文件不存在时返回 `new T()`（各字段默认值），与"存过盘但内容为空"不可区分；
    /// 若不判别就直接灌，会把初始 4 人编队和已掷好的客栈栏位清空。
    /// 判据放在数据里（而不是 File.Exists 探文件），这样不绕过框架、也不重复框架的路径逻辑。
    /// </summary>
    public bool Written;

    // ---------- 推图 ----------
    public int Layer = 1;
    public int Wave = 1;
    public int BestLayer = 1;
    public bool AutoPush = true;

    // ---------- 银两 ----------
    public double SilverAmount;
    public double SilverTotal;

    // ---------- 本世资产 ----------
    public List<HeroExpSave> Heroes = new List<HeroExpSave>();
    public List<MaterialSave> Materials = new List<MaterialSave>();
    public List<EquipItemSave> Equips = new List<EquipItemSave>();
    public List<WornSave> Worn = new List<WornSave>();

    // ---------- 跨世资产（武学装配）----------
    public List<LoadoutSave> Loadouts = new List<LoadoutSave>();

    // ---------- 客栈（本世）----------
    public int InnRefreshUsed;
    public List<QualitySave> InnSlots = new List<QualitySave>();
    public List<QualitySave> InnRecruited = new List<QualitySave>();
    public List<int> Roster = new List<int>();
}

/// <summary>某侠客的修为：等级 + 当前级内池子。</summary>
public sealed class HeroExpSave
{
    public int HeroId;
    public int Level = 1;
    public double Pool;
}

/// <summary>某品阶材料的持有量。</summary>
public sealed class MaterialSave
{
    public int Tier;
    public long Count;
}

/// <summary>一件装备实例（模板 Id + 强化等级 + 唯一 Uid）。</summary>
public sealed class EquipItemSave
{
    public int Uid;
    public int ConfigId;
    public int Level;
}

/// <summary>某侠客四栏穿的是哪几件（Uid，0 = 空栏）。</summary>
public sealed class WornSave
{
    public int HeroId;
    public int[] Uids = new int[EquipBag.SlotCount];
}

/// <summary>某侠客的三个武学槽（0 = 空槽）。</summary>
public sealed class LoadoutSave
{
    public int HeroId;
    public int[] Slots = new int[SkillLoadout.Slots];
}

/// <summary>一份资质（客栈栏位 / 已招名单）。派生加成（HealPct/MulBonus）不入库，装配时按词条重算。</summary>
public sealed class QualitySave
{
    public int HeroId;
    public int Star = 1;
    public double HpPct;
    public double AtkPct;
    public double SpdPct;
    public List<int> AffixIds = new List<int>();
    public bool Recruited;
    public bool IsBase;
}
