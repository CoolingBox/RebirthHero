// 文件：SummonCatalog.cs
// 《轮回大侠》配置接入层 —— 召唤物【类型档位】查表
//
// 作用：把 `SummonConfig.xlsx`（类型键 → 类型规格）变成一个按 Kind 查的目录，
//       供 `SkillConfigMapper` 把 `SkillConfig.SummonKind` 解析成引擎的 `SummonSpec`。
//
// 依赖方向：本文件**不引用 Godot / ConfigManager**（这样无头探针能直接编译），
//   由调用方灌入数据：游戏侧 `BattlePanel.Init`（OnShow 首次触发）用 `ConfigManager.GetAll<SummonConfig>()`，
//   探针直接反序列化 JSON。未灌入时 `Get` 返回 null → 引擎用 `SummonSpec.Fallback` 兜底。

using System;
using System.Collections.Generic;

public static class SummonCatalog
{
    static Dictionary<string, SummonConfig> _byKind;

    /// <summary>灌入召唤物类型表（幂等，可重复调用）。</summary>
    public static void Init(IEnumerable<SummonConfig> all)
    {
        var map = new Dictionary<string, SummonConfig>(StringComparer.Ordinal);
        if (all != null)
            foreach (var c in all)
                if (c != null && !string.IsNullOrEmpty(c.Kind)) map[c.Kind] = c;
        _byKind = map;
    }

    /// <summary>已灌入的类型数（0 = 尚未 Init，召唤物会走引擎兜底规格）。</summary>
    public static int Count => _byKind?.Count ?? 0;

    /// <summary>按类型键取配置；未登记/未灌入返回 null（调用方回退到引擎兜底规格）。</summary>
    public static SummonConfig Get(string kind)
        => string.IsNullOrEmpty(kind) || _byKind == null || !_byKind.TryGetValue(kind, out var hit) ? null : hit;

    /// <summary>把配置行转成引擎规格；cfg 为 null 时返回 null（调用方走兜底）。</summary>
    public static SummonSpec ToSpec(SummonConfig cfg)
    {
        if (cfg == null) return null;
        return new SummonSpec
        {
            Kind = cfg.Kind,
            Name = string.IsNullOrEmpty(cfg.Name) ? "召唤物" : cfg.Name,
            HpRatio = cfg.HpRatio,
            FixedHp = cfg.FixedHp,
            DmgPerHit = cfg.DmgPerHit,
            AtkRatio = cfg.AtkRatio,
            SpdFloor = cfg.SpdFloor,
            SpdRatio = cfg.SpdRatio,
            Weight = cfg.BaseW > 0 ? cfg.BaseW : BattleConfig.OtherW,
            Ttl = cfg.Ttl > 0 ? cfg.Ttl : BattleConfig.SummonTtl,
            Max = cfg.SummonMax,
        };
    }
}
