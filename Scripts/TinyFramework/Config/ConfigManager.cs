// 文件：ConfigManager.cs
// 作者：急冻雪柜
// 描述：配置管理器
// 日期：2025/12/26 5:25

using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace TinyFramework;

public class ConfigManager : Singleton<ConfigManager>
{
    private static readonly Dictionary<string, object> _configDict = new Dictionary<string, object>();
    // 按类型名缓存 id -> 配置 索引,使 GetOne 由 O(n) 线性查找变为 O(1)
    private static readonly Dictionary<string, object> _configIndex = new Dictionary<string, object>();

    // IncludeFields=true:配置类(BaseConfig/SkillConfig)使用 public 字段,需开启字段序列化以兼容原 Newtonsoft 行为
    private static readonly JsonSerializerOptions ConfigOptions = new()
    {
        IncludeFields = true,
        Converters = { new BigNumJsonConverter() }, // 大数序列化为紧凑科学串,读取兼容数字字面量
    };

    public static T GetOne<T>(int id) where T : BaseConfig
    {
        string name = typeof(T).Name;

        // 首次访问时从已缓存的配置列表构建 id 索引(O(1) 查询)
        if (!_configIndex.ContainsKey(name))
        {
            List<T> all = GetAll<T>();
            if (all == null)
                return null;
            Dictionary<int, T> idIndex = new Dictionary<int, T>(all.Count);
            foreach (T cfg in all)
                idIndex[cfg.Id] = cfg;
            _configIndex[name] = idIndex;
        }

        var index = (Dictionary<int, T>)_configIndex[name];
        if (index.TryGetValue(id, out T config))
            return config;

        GD.PrintErr($"[ConfigManager] 找不到ID为 {id} 的配置项: {name}");
        return null;
    }


    public static List<T> GetAll<T>() where T : BaseConfig
    {
        string name = typeof(T).Name;

        if (_configDict.TryGetValue(name, out object configs))
        {
            return configs as List<T>;
        }

        string fullName = PathDefine.ConfigPath + name + ".json";
        if (!FileAccess.FileExists(fullName))
        {
            GD.PrintErr($"[ConfigManager] 配置文件不存在: {fullName}");
            return null;
        }

        using (FileAccess fileAccess = FileAccess.Open(fullName, FileAccess.ModeFlags.Read))
        {
            configs = JsonSerializer.Deserialize<List<T>>(fileAccess.GetAsText(), ConfigOptions);
            GD.PrintRich($"[color=Green]已加载配置: {name} (共 {(configs as List<T>).Count} 条)[/color]");
            _configDict.Add(name, configs);
        }

        return configs as List<T>;
    }
}