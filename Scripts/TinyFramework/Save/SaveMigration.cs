// 文件：SaveMigration.cs
// 作者：急冻雪柜
// 描述：**存档版本迁移** —— 让老存档能升级到新结构，而不是"版本不认识就整份丢弃"
// 日期：2026/11/11
//
// 为什么要它（2026-11-11 对照 STS2 的 `Core/Saves/Migrations`）：
//   现在的 `SaveManager.Load` 遇到不认识的版本只能整份作废（GameSave 顶部注释写的策略）。
//   对放置游戏来说这是最差的结局：玩家挂了三天，一次字段改名就清零。
//   STS2 用 `IMigration<T>` 一条链把 v1→v2→v3 逐级迁过去；这里取它的内核，但**不绑定强类型**——
//   迁移直接改 JSON 树，这样不必为每个历史版本保留一份 DTO 类。
//
// 设计边界：
//   · 迁移**只改 JSON**，不碰业务逻辑；加字段、改名、改默认值都在这里做。
//   · 迁移必须**幂等且单向**：`FromVersion = N` 表示"把 vN 的数据变成 v(N+1)"。
//   · 链断了（缺 v2→v3）就停在能迁到的那一版，并在日志里报出来 —— 不假装成功。
//   · ⚠️ **契约：迁移链的终点必须等于业务侧的 `CurrentVersion`**。
//     注册 v1→v2 之后，记得把 `GameSave.CurrentVersion` 一起改成 2；
//     否则 `GameProgress.Apply` 会因为"版本不符"把整份存档丢掉 —— 迁移做完白做。
//     （2026-11-11 实测踩过：探针把 v1 迁到 v2、CurrentVersion 仍为 1，存档当场作废。）
//
// 用法（在游戏侧注册，框架不管什么时候注册，但必须在第一次读档之前）：
//   SaveMigrationRegistry.Register("GameSave", new GameSaveV1ToV2());
//
//   sealed class GameSaveV1ToV2 : SaveMigration
//   {
//       public override int FromVersion { get { return 1; } }
//       public override void Apply(JsonObject save)
//       {
//           save["NewField"] = 0;   // 补默认值 / 改名 / 搬字段
//       }
//   }

using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace TinyFramework;

/// <summary>一次版本跃迁：`FromVersion` → `FromVersion + 1`。</summary>
public abstract class SaveMigration
{
    /// <summary>源版本号。迁移完成后存档的 Version 会变成 `FromVersion + 1`。</summary>
    public abstract int FromVersion { get; }

    /// <summary>就地修改 JSON。只处理"这一个版本差"，不要试图一次迁到最新。</summary>
    public abstract void Apply(JsonObject save);
}

/// <summary>迁移注册表（按存档名分组，名字 = SaveManager 的文件名）。</summary>
public static class SaveMigrationRegistry
{
    private static readonly Dictionary<string, List<SaveMigration>> _table =
        new Dictionary<string, List<SaveMigration>>();

    /// <summary>登记一条迁移。重复登记同一个 FromVersion 会覆盖旧的（热重载友好）。</summary>
    public static void Register(string saveName, SaveMigration migration)
    {
        if (string.IsNullOrEmpty(saveName) || migration == null)
        {
            return;
        }

        List<SaveMigration> list;
        if (!_table.TryGetValue(saveName, out list))
        {
            list = new List<SaveMigration>();
            _table[saveName] = list;
        }

        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].FromVersion == migration.FromVersion)
            {
                list[i] = migration;
                return;
            }
        }
        list.Add(migration);
    }

    /// <summary>这个存档登记过迁移吗（没有就跳过整条解析，省一次 JSON 往返）。</summary>
    public static bool HasMigration(string saveName)
    {
        List<SaveMigration> list;
        if (!_table.TryGetValue(saveName, out list))
        {
            return false;
        }
        return list.Count > 0;
    }

    /// <summary>读当前版本号（没有 Version 字段就当 0）。</summary>
    public static int ReadVersion(JsonObject save)
    {
        if (save == null)
        {
            return 0;
        }

        JsonNode node;
        if (!save.TryGetPropertyValue("Version", out node) || node == null)
        {
            return 0;
        }

        JsonValue value = node.AsValue();
        int version;
        if (value.TryGetValue<int>(out version))
        {
            return version;
        }
        return 0;
    }

    /// <summary>
    /// 按版本号依次执行迁移，返回迁移后的版本号。
    /// 缺一环就停在那里（不做半套迁移），由调用方决定是继续用还是报错。
    /// </summary>
    public static int Migrate(string saveName, JsonObject save)
    {
        int version = ReadVersion(save);
        if (save == null)
        {
            return version;
        }

        List<SaveMigration> list;
        if (!_table.TryGetValue(saveName, out list) || list.Count == 0)
        {
            return version;
        }

        list.Sort(CompareByVersion);

        int index = 0;
        while (index < list.Count)
        {
            SaveMigration step = list[index];
            if (step.FromVersion < version)
            {
                index++;          // 这一跳早就跨过去了
                continue;
            }
            if (step.FromVersion > version)
            {
                // 链断了：比如只有 v1→v2 与 v3→v4，而存档是 v2
                TinyLog.Warn("Save", saveName + " 迁移链断裂：当前 v" + version
                                     + "，缺 v" + version + " 的迁移，停在这里");
                break;
            }

            step.Apply(save);
            version++;
            index++;
        }

        save["Version"] = version;
        return version;
    }

    private static int CompareByVersion(SaveMigration a, SaveMigration b)
    {
        return a.FromVersion.CompareTo(b.FromVersion);
    }

    /// <summary>清空注册表（切换账号 / 测试用）。</summary>
    public static void Clear()
    {
        _table.Clear();
    }
}
