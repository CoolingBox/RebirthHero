// 文件：StatsTracker.cs
// 《轮回大侠》M2.x 战斗统计 —— 按侠客 + 按武学的实时明细聚合
//
// 设计口径（用户需求"实时统计侠客与武学的伤害/治疗/行动次数/承伤/触发次数，每层刷新"）：
//   · 只统计**玩家方**（A 队）；敌方不计入（"侠客与武学"= 我方视角）。
//   · 订阅 BattleEventBus，按"侠客名"与"(侠客名|武学)"两类 key 聚合。
//   · **召唤物归属**（2026-10-17 修正）：召唤物的伤害/承伤不再丢弃，归入
//     "召唤者（Summoner）+ 召唤来源武学（SummonFrom.Name）"——御兽诀 这类召唤武学
//     会累计其召唤物的输出与承伤，避免"实战输出被低估"。
//   · 仅侠客本体计"行动次数 / 触发次数"；召唤物的攻击不重复计入（避免与召唤技能触发数双算）。
//   · 生命周期：随 BattlePanel 长期存在；进入新一层时调用 Reset() 清零（"每层刷新"）。
//     —— 层内多波（血量延续）累计叠加，不重置；只有 Layer 变化才清。
//   · 不碰引擎数学：本类只读事件，绝不反向写入 BattleUnit / BattleEngine。

using System;
using System.Collections.Generic;

public sealed class StatsTracker
{
    // 排序 / 排列用的指标维度
    public enum StatMetric { Damage, Heal, Actions, Taken, Triggers }

    // —— 侠客合计 ——
    public sealed class HeroStat
    {
        public string Name = "";
        public int Slot;
        public double Damage;   // 造成伤害（含其召唤物）
        public double Heal;     // 治疗量
        public int Actions;     // 行动次数
        public double Taken;    // 承伤（含其召唤物）
        public int Triggers;    // 旗下武学触发总次数
    }

    // —— 武学明细（按 侠客|武学 区分，同名武学在不同侠客身上分开计）——
    public sealed class SkillStat
    {
        public string Hero = "";
        public string Skill = "";
        public int Triggers;    // 触发次数
        public double Damage;   // 该武学造成伤害（含其召唤物的伤害）
        public double Heal;     // 该武学治疗量
        public double Taken;     // 该武学承伤（召唤武学 = 其召唤物承伤）
    }

    private readonly Dictionary<string, HeroStat> _heroes = new();
    private readonly List<string> _heroOrder = new();     // 首见顺序（显示时再按 Slot 排）
    private readonly Dictionary<string, SkillStat> _skills = new();
    private readonly List<string> _skillOrder = new();
    private Func<BattleUnit, bool> _isPlayerHero;

    public IReadOnlyList<string> HeroOrder => _heroOrder;
    public IReadOnlyList<string> SkillOrder => _skillOrder;
    public bool TryHero(string name, out HeroStat s) => _heroes.TryGetValue(name, out s);
    public bool TrySkill(string key, out SkillStat s) => _skills.TryGetValue(key, out s);
    public static string SkillKey(string hero, string skill) => hero + "|" + skill;

    /// <summary>挂到某场引擎的事件总线上。每场（含层内续战的新引擎）都重新挂一次。</summary>
    public void Attach(BattleEngine eng)
    {
        _isPlayerHero = u => eng.InA(u) && !u.IsSummon;
        eng.Bus.OnAction += OnAction;
        eng.Bus.OnSkill += OnSkill;
        eng.Bus.OnDamage += OnDamage;
        eng.Bus.OnHeal += OnHeal;
    }

    /// <summary>进入新一层时调用：清空所有聚合（"每层刷新"）。</summary>
    public void Reset()
    {
        _heroes.Clear();
        _heroOrder.Clear();
        _skills.Clear();
        _skillOrder.Clear();
    }

    // 归账到哪个侠客：玩家侠客→自身；玩家召唤物→其召唤者；其余→null（不统计）
    BattleUnit ResolveHero(BattleUnit u)
    {
        if (u == null) return null;
        if (u.IsSummon && u.Summoner != null) return u.Summoner;
        if (_isPlayerHero != null && _isPlayerHero(u)) return u;
        return null;
    }

    // 归账到哪个武学名：玩家召唤物的伤害/承伤强制归入其召唤来源武学；其余用事件自带的 skill 名
    string ResolveSkill(BattleUnit u, string evtSkill)
    {
        if (u != null && u.IsSummon && u.SummonFrom != null) return u.SummonFrom.Name;
        return evtSkill;
    }

    void OnAction(BattleUnit u, bool combo)
    {
        var h = ResolveHero(u);
        if (h == null) return;
        Hero(h).Actions++;
    }

    void OnSkill(BattleUnit u, string name)
    {
        var h = ResolveHero(u);
        if (h == null || string.IsNullOrEmpty(name)) return;
        Hero(h).Triggers++;
        Skill(h, name).Triggers++;
    }

    void OnDamage(BattleUnit a, BattleUnit t, double amt, string skill)
    {
        if (amt <= 0) return;
        var ha = ResolveHero(a);
        if (ha != null)
        {
            Hero(ha).Damage += amt;
            string sk = ResolveSkill(a, skill);
            if (!string.IsNullOrEmpty(sk)) Skill(ha, sk).Damage += amt;
        }
        var ht = ResolveHero(t);
        if (ht != null)
        {
            Hero(ht).Taken += amt;
            // 召唤物承伤也归入其召唤来源武学
            if (t != null && t.IsSummon && t.SummonFrom != null)
                Skill(ht, t.SummonFrom.Name).Taken += amt;
        }
    }

    void OnHeal(BattleUnit a, BattleUnit t, double amt, string skill)
    {
        if (amt <= 0) return;
        var ha = ResolveHero(a);
        if (ha != null)
        {
            Hero(ha).Heal += amt;
            string sk = ResolveSkill(a, skill);
            if (!string.IsNullOrEmpty(sk)) Skill(ha, sk).Heal += amt;
        }
    }

    HeroStat Hero(BattleUnit u)
    {
        if (!_heroes.TryGetValue(u.Name, out var s))
        {
            s = new HeroStat { Name = u.Name, Slot = u.Slot };
            _heroes[u.Name] = s;
            _heroOrder.Add(u.Name);
        }
        else if (u.Slot > 0 && s.Slot == 0)
        {
            s.Slot = u.Slot;
        }
        return s;
    }

    SkillStat Skill(BattleUnit hero, string skill)
    {
        string key = SkillKey(hero.Name, skill);
        if (!_skills.TryGetValue(key, out var s))
        {
            s = new SkillStat { Hero = hero.Name, Skill = skill };
            _skills[key] = s;
            _skillOrder.Add(key);
        }
        return s;
    }

    // —— 预设（表现层建行时按阵容占位，确保零数据侠客 / 武学也显示）——
    public HeroStat EnsureHero(BattleUnit u)
    {
        if (!_heroes.TryGetValue(u.Name, out var s))
        {
            s = new HeroStat { Name = u.Name, Slot = u.Slot };
            _heroes[u.Name] = s;
            _heroOrder.Add(u.Name);
        }
        else if (u.Slot > 0 && s.Slot == 0)
        {
            s.Slot = u.Slot;
        }
        return s;
    }

    public SkillStat EnsureSkill(BattleUnit hero, string skill)
    {
        string key = SkillKey(hero.Name, skill);
        if (!_skills.TryGetValue(key, out var s))
        {
            s = new SkillStat { Hero = hero.Name, Skill = skill };
            _skills[key] = s;
            _skillOrder.Add(key);
        }
        return s;
    }

    // —— 枚举（供表现层排序）——
    public List<HeroStat> HeroList()
    {
        var list = new List<HeroStat>(_heroes.Values);
        list.Sort(CompareHeroSlot);
        return list;
    }

    public List<SkillStat> SkillList()
    {
        return new List<SkillStat>(_skills.Values);
    }

    private static int CompareHeroSlot(HeroStat x, HeroStat y)
    {
        int c = x.Slot.CompareTo(y.Slot);
        if (c != 0) return c;
        return string.Compare(x.Name, y.Name);
    }

    // —— 指标取值（排序用）——
    public static double MetricOf(HeroStat h, StatMetric m)
    {
        switch (m)
        {
            case StatMetric.Damage: return h.Damage;
            case StatMetric.Heal: return h.Heal;
            case StatMetric.Actions: return h.Actions;
            case StatMetric.Taken: return h.Taken;
            default: return h.Triggers;
        }
    }

    public static double MetricOf(SkillStat s, StatMetric m)
    {
        switch (m)
        {
            case StatMetric.Damage: return s.Damage;
            case StatMetric.Heal: return s.Heal;
            case StatMetric.Actions: return s.Triggers;   // 武学无独立"行动"概念，行动次数≈触发次数
            case StatMetric.Taken: return s.Taken;         // 召唤武学 = 其召唤物承伤
            default: return s.Triggers;
        }
    }
}
