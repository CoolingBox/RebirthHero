// 文件：Tuning.cs
// 《轮回大侠》配置接入层 —— **全局系数总账**的运行时入口
//
// 数据源：addons/TinyTools/ConfigGenerator/TuningConfig.xlsx → Resources/Config/TuningConfig.json
//
// 为什么要有这个类（2026-10-16 用户口径："各种系数归纳为一个config表"）：
//   原先系数散在三个纯代码常量类里（BattleConfig / StageConfig / CultivationConfig），
//   每个数"是谁定的、用在哪、有没有依据"全靠代码注释，一旦被追问就得靠回忆（HpScale=1.7 就是这么出事的）。
//   现在改为：
//     · **表 = 唯一事实来源**，且带 Group / Source（用户指定/用户口径/文档规定/我自拟）/ Note 三列，可审计；
//     · 代码里的 public static 字段 = **兜底默认值**（表缺失时引擎仍能跑）+ 热路径直接读（不进字典查找）；
//     · 本类负责把表**注入**这些字段，并做**双向一致性校验** —— 表和代码必须严格一一对应。
//
// 为什么用反射而不是手写 switch：61 个系数手写 switch 就是 61 个 case，且每加一个系数必然漏改；
//   反射按字段名注入 + 下面的双向校验，"表里多一个"或"代码里多一个"都会立刻报错。
//
// 本文件**不引用 Godot**（诊断走 Console.Error），因为无头探针工程会直接编译它。

using System;
using System.Collections.Generic;
using System.Reflection;

public static class Tuning
{
    /// <summary>会被注入的静态常量类（新增一个常量类就加进这里）。</summary>
    static readonly Type[] Targets =
    {
        typeof(BattleConfig),
        typeof(StageConfig),
        typeof(StageRules),
        typeof(CultivationConfig),
        typeof(InnConfig),
        typeof(MaterialConfig),
    };

    static bool _loaded;

    /// <summary>表是否已注入（GameRoot / 探针各调一次 Load）。</summary>
    public static bool Loaded => _loaded;

    /// <summary>校验诊断 —— **为空表示表与代码完全一致**。</summary>
    public static readonly List<string> Errors = new();

    /// <summary>与代码兜底默认值不同的项（非错误，但值得看一眼，说明有人改了表没同步兜底）。</summary>
    public static readonly List<string> Drifts = new();

    /// <summary>把总账表注入各常量类的 public static 字段。可重复调用（幂等）。</summary>
    public static void Load(List<TuningConfig> rows)
    {
        if (_loaded) return;
        _loaded = true;
        Errors.Clear();
        Drifts.Clear();

        var fields = new Dictionary<string, FieldInfo>();
        foreach (Type t in Targets)
            foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.Static))
                fields[f.Name] = f;

        var applied = new HashSet<string>();
        int runtimeRows = 0;

        foreach (TuningConfig r in rows ?? new List<TuningConfig>())
        {
            if (!r.Runtime) continue;
            runtimeRows++;

            if (!fields.TryGetValue(r.Key, out FieldInfo fi))
            {
                Errors.Add($"表里有 [{r.Key}]（Runtime=true），但代码里找不到同名 public static 字段");
                continue;
            }

            object v = ToFieldType(fi.FieldType, r.Value);
            if (v == null)
            {
                Errors.Add($"[{r.Key}] 类型不匹配：代码字段是 {fi.FieldType.Name}，表里是 {r.Value}");
                continue;
            }

            object old = fi.GetValue(null);
            if (!Equals(old, v)) Drifts.Add($"[{r.Key}] 代码兜底 {old} → 表值 {v}");
            fi.SetValue(null, v);
            applied.Add(r.Key);
        }

        // 反向校验：代码里有、表里没有 → 说明总账漏登记
        foreach (KeyValuePair<string, FieldInfo> kv in fields)
            if (!applied.Contains(kv.Key))
                Errors.Add($"代码里有 {kv.Value.DeclaringType?.Name}.{kv.Key}，但表里没登记（应补一行 Runtime=true）");

        Console.WriteLine($"[Tuning] 已从 TuningConfig 注入 {applied.Count} 项（表内 Runtime 行 {runtimeRows}）");
        foreach (string d in Drifts) Console.WriteLine($"[Tuning] 兜底已更新: {d}");
        foreach (string e in Errors) Console.Error.WriteLine($"[Tuning] ✗ {e}");
    }

    static object ToFieldType(Type t, double v)
    {
        if (t == typeof(double)) return v;
        if (t == typeof(float)) return (float)v;
        if (t == typeof(int)) return (int)Math.Round(v);
        if (t == typeof(long)) return (long)Math.Round(v);
        if (t == typeof(bool)) return Math.Abs(v) > 0.5;
        return null;
    }
}
