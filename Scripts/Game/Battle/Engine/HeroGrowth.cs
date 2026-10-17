// 文件：HeroGrowth.cs
// 《轮回大侠》M2 战斗引擎 —— 侠客等级成长（1~40 级，纯加算）
// 规则依据：《轮回大侠-战斗数值框架-v0.1》§11
//
// 模型（最朴素的加算，无百分比、无倍率）：
//     属性(L) = 基础值(L1) + 每级成长 × (L − 1)
//
//   基础值(L1) 与 每级成长 都直接写在 HeroConfig 里：
//     BaseHP / HpGrowth、BaseATK / AtkGrowth、BaseDEF / DefGrowth
//   三属性各自独立随机（均不为 0）→ 侧重比 HP:ATK 会随等级漂移，这是刻意保留的成长多样性。
//
// 标准轴（供对照，非运行时依赖）：HP 1000(+100/级) / ATK 60(+5/级) / DEF 7.5(+0.5/级)
// SPD 不随等级成长（行动频率一变，战斗时长与节奏全跟着漂；来源留给装备/境界）

using System;

public static class HeroGrowth
{
    /// <summary>加算展开：基础值 + 每级成长 × (L − 1)。L 自动 clamp 到 1~40。</summary>
    static double At(double baseValue, double perLevel, int level)
    {
        int L = Math.Clamp(level, 1, BattleConfig.HeroLevelMax);
        return baseValue + perLevel * (L - 1);
    }

    public static double Hp(double baseHp, double hpGrowth, int level)     => At(baseHp, hpGrowth, level);
    public static double Atk(double baseAtk, double atkGrowth, int level)  => At(baseAtk, atkGrowth, level);
    public static double Def(double baseDef, double defGrowth, int level)  => At(baseDef, defGrowth, level);
}
