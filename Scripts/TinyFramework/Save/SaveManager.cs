// 文件：SaveManager.cs
// 作者：急冻雪柜
// 描述：存档读写（JSON 落盘 + 版本迁移入口）
// 日期：2025/11/20 16:18
//
// 2026-11-11 补强：
//   · 读档前先跑 `SaveMigrationRegistry` 的迁移链 —— 老存档升级而不是整份作废
//   · 迁移只作用于**内存中的 JSON**，不立即回写：读档失败时原档还在，不会把唯一一份进度写坏
//   · 日志走 `TinyLog`，发布版可整体静音

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;

namespace TinyFramework;

public class SaveManager : Singleton<SaveManager>
{
    // IncludeFields=true:数据模型(SaveData/SettingData)使用 public 字段而非属性
    // 需开启字段序列化以兼容原 Newtonsoft 行为
    private static readonly JsonSerializerOptions SaveOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        IncludeFields = true,
        Converters = { new BigNumJsonConverter() }, // 大数序列化为紧凑科学串，读取兼容数字字面量
    };

    public static void Save<T>(T data, string name = null) where T : SaveData
    {
        if (name == null)
        {
            name = typeof(T).Name;
        }

        data.Update();
        string json = JsonSerializer.Serialize(data, SaveOptions);
        string path = Path.Combine(PathDefine.UserPath, name);
        File.WriteAllText(path, json);
        TinyLog.Info("Save", "已写入 " + name);
    }

    public static T Load<T>(string name = null) where T : SaveData,new()
    {
        if (name == null)
        {
            name = typeof(T).Name;
        }

        string path = Path.Combine(PathDefine.UserPath, name);
        if (!File.Exists(path))
        {
            return new T();
        }

        string json = File.ReadAllText(path);

        // 迁移失败**不阻断读档**：退回原始 JSON 继续解析，最大限度保住玩家进度
        try
        {
            json = ApplyMigrations(name, json);
        }
        catch (Exception e)
        {
            TinyLog.Error("Save", name + " 迁移失败，按原数据读取：" + e.Message);
        }

        T data = JsonSerializer.Deserialize<T>(json, SaveOptions);
        return data;
    }

    /// <summary>
    /// 把盘上的 JSON 按版本迁移到最新结构，返回新的 JSON 文本。
    /// 没有登记迁移、或版本已经最新时原样返回（不产生一次多余的序列化）。
    /// </summary>
    private static string ApplyMigrations(string name, string json)
    {
        if (!SaveMigrationRegistry.HasMigration(name))
        {
            return json;
        }

        JsonNode root = JsonNode.Parse(json);
        JsonObject obj = root as JsonObject;
        if (obj == null)
        {
            return json;
        }

        int before = SaveMigrationRegistry.ReadVersion(obj);
        int after = SaveMigrationRegistry.Migrate(name, obj);
        if (after == before)
        {
            return json;
        }

        TinyLog.Info("Save", name + " 存档已迁移 v" + before + " → v" + after);
        return obj.ToJsonString(SaveOptions);
    }
}
