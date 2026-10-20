// 文件：GameProgress.cs
// 《轮回大侠》M2.6 存档 —— **游戏进度与存档数据的搬运工**（不是存档系统本身）
//
// 存档系统归 TinyFramework：`SaveManager.Save / Load<T>` 负责路径(user://)、JSON、字段序列化、缺文件时返回 new T()。
// 本文件只做两件框架管不到的事：
//   · Capture —— 把散落在各状态机里的进度**抓**成一份 `GameSave`（SaveData 子类）
//   · Apply   —— 把 `GameSave` **灌**回各状态机
// 换句话说：**落盘/读盘由调用方直接调 SaveManager**（与 SettingManager 的写法一致），
// 这里不出现 Save / Load / HasSave 这类与框架重名的入口，也不做 File.Exists 之类的文件层探测。
//
// 为什么不能直接把状态机本身存成 SaveData：
//   `Cultivation / Silver / EquipBag / Inn / StageRun` 是状态机（私有字段 + 只读属性，还要维持内部一致性），
//   把它们改成 SaveData 会让引擎跟存档格式绑死。所以走"抓/灌"这层薄适配，格式只在 GameSave 里。

using System.Collections.Generic;
using Godot;

public static class GameProgress
{
    // ============ 抓（存盘前调用，结果交给 SaveManager）============

    /// <summary>把当前所有进度打包成存档数据。heroIds = 参与修为分配的那批侠客（开局阵容）。</summary>
    public static GameSave Capture(StageRun run, Cultivation cult, Silver silver, MaterialBag materials,
                                   EquipBag equips, SkillLoadout loadout, Inn inn, List<int> heroIds)
    {
        GameSave save = new GameSave();
        if (run == null || cult == null || silver == null) return save;

        save.Written = true;   // 标记"这份数据是抓出来的"，读档侧据此区分真存档与框架返回的空对象

        // —— 推图 ——
        save.Layer = run.Layer;
        save.Wave = run.Wave;
        save.BestLayer = run.BestLayer;
        save.AutoPush = run.AutoPush;

        // —— 银两（本场掉落不存，跨进程无意义）——
        save.SilverAmount = silver.Amount;
        save.SilverTotal = silver.TotalGained;

        // —— 修为 ——
        if (heroIds != null)
            foreach (int id in heroIds)
            {
                HeroExpSave e = new HeroExpSave();
                e.HeroId = id;
                e.Level = cult.LevelOf(id);
                e.Pool = cult.PoolOf(id);
                save.Heroes.Add(e);
            }

        // —— 材料（按档位遍历，只存有量的）——
        if (materials != null)
            for (int tier = 1; tier <= MaterialCatalog.Count; tier++)
            {
                long n = materials.Get(tier);
                if (n <= 0) continue;
                MaterialSave m = new MaterialSave();
                m.Tier = tier;
                m.Count = n;
                save.Materials.Add(m);
            }

        // —— 装备：实例 + 穿戴关系 ——
        if (equips != null)
        {
            foreach (EquipItem item in equips.Items)
            {
                EquipItemSave s = new EquipItemSave();
                s.Uid = item.Uid;
                s.ConfigId = item.ConfigId;
                s.Level = item.Level;
                save.Equips.Add(s);
            }

            foreach (int heroId in equips.WornHeroIds())
            {
                int[] slots = equips.SlotsOf(heroId);
                WornSave w = new WornSave();
                w.HeroId = heroId;
                for (int i = 0; i < EquipBag.SlotCount; i++)
                    w.Uids[i] = slots[i];
                save.Worn.Add(w);
            }
        }

        // —— 武学装配（跨世资产）——
        if (loadout != null && heroIds != null)
            foreach (int id in heroIds)
            {
                int[] slots = loadout.Get(id);
                LoadoutSave s = new LoadoutSave();
                s.HeroId = id;
                for (int i = 0; i < SkillLoadout.Slots; i++)
                    s.Slots[i] = i < slots.Length ? slots[i] : SkillLoadout.Empty;
                save.Loadouts.Add(s);
            }

        // —— 客栈 ——
        if (inn != null)
        {
            save.InnRefreshUsed = inn.RefreshUsed;
            foreach (HeroQuality q in inn.Slots) save.InnSlots.Add(ToQualitySave(q));
            foreach (HeroQuality q in inn.Recruited) save.InnRecruited.Add(ToQualitySave(q));
            foreach (int id in inn.Roster) save.Roster.Add(id);
        }

        return save;
    }

    static QualitySave ToQualitySave(HeroQuality q)
    {
        QualitySave s = new QualitySave();
        if (q == null) return s;

        s.HeroId = q.HeroId;
        s.Star = q.Star;
        s.HpPct = q.HpPct;
        s.AtkPct = q.AtkPct;
        s.SpdPct = q.SpdPct;
        s.Recruited = q.Recruited;
        s.IsBase = q.IsBase;
        foreach (int id in q.AffixIds) s.AffixIds.Add(id);
        return s;
    }

    // ============ 灌（读到存档后调用）============

    /// <summary>
    /// 把存档灌回各状态机。返回 false = 从没存过盘 / 版本不认识（调用方保持新一局状态）。
    /// **必须在各状态机初始化之后、开第一场战斗之前调用。**
    ///
    /// 注意：`SaveManager.Load<T>` 在文件不存在时返回 `new T()`（各字段是默认值），
    /// 所以不能靠它判断"有没有存档"，也不能直接灌 —— 直接灌会把初始 4 人编队和已掷好的客栈栏位清空。
    /// 判据用 `GameSave.Written`（类内自带，属于数据语义，不需要去探文件系统）。
    /// </summary>
    public static bool Apply(GameSave save, StageRun run, Cultivation cult, Silver silver, MaterialBag materials,
                             EquipBag equips, SkillLoadout loadout, Inn inn)
    {
        if (save == null || !save.Written) return false;
        if (run == null || cult == null || silver == null) return false;

        if (save.Version != GameSave.CurrentVersion)
        {
            // 不认识的版本：整份丢弃。读半份脏数据比从第 1 层重开更糟（症状会散落在各处）
            GD.Print("[Save] 存档版本 " + save.Version + " 与当前 " + GameSave.CurrentVersion
                     + " 不符，已忽略（从新一局开始）");
            return false;
        }

        run.Restore(save.Layer, save.Wave, save.BestLayer, save.AutoPush);
        silver.Restore(save.SilverAmount, save.SilverTotal);

        foreach (HeroExpSave e in save.Heroes)
            cult.Restore(e.HeroId, e.Level, e.Pool);

        if (materials != null)
            foreach (MaterialSave m in save.Materials)
                materials.Restore(m.Tier, m.Count);

        if (equips != null)
        {
            foreach (EquipItemSave s in save.Equips)
                equips.RestoreItem(s.Uid, s.ConfigId, s.Level);

            // 穿戴必须在实例全部恢复之后 —— RestoreWorn 会校验 Uid 是否存在
            foreach (WornSave w in save.Worn)
                equips.RestoreWorn(w.HeroId, w.Uids);
        }

        if (loadout != null)
            foreach (LoadoutSave s in save.Loadouts)
                loadout.Restore(s.HeroId, s.Slots);

        if (inn != null)
        {
            List<HeroQuality> slots = new List<HeroQuality>();
            foreach (QualitySave s in save.InnSlots) slots.Add(FromQualitySave(s));

            List<HeroQuality> recruited = new List<HeroQuality>();
            foreach (QualitySave s in save.InnRecruited) recruited.Add(FromQualitySave(s));

            inn.Restore(save.InnRefreshUsed, slots, recruited, save.Roster);
        }

        GD.Print("[Save] 已读取：第 " + save.Layer + " 层 第 " + save.Wave + " 波"
                 + " · 最高 " + save.BestLayer + " 层 · 银两 " + save.SilverAmount
                 + " · 装备 " + save.Equips.Count + " 件 · 已招 " + save.InnRecruited.Count + " 人");
        return true;
    }

    static HeroQuality FromQualitySave(QualitySave s)
    {
        if (s == null) return null;

        return HeroQuality.Restore(s.HeroId, s.Star, s.HpPct, s.AtkPct, s.SpdPct,
                                   s.AffixIds, s.Recruited, s.IsBase);
    }
}
