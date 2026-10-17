// 文件：SaveManager.cs
// 作者：急冻雪柜
// 描述：
// 日期：2025/11/20 16:18


using System.IO;
using System.Text.Json;
using Godot;

namespace TinyFramework;

public class SaveManager : Singleton<SaveManager>
{
    // IncludeFields=true:数据模型(SaveData/SettingData)使用 public 字段而非属性,需开启字段序列化以兼容原 Newtonsoft 行为
    private static readonly JsonSerializerOptions SaveOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        IncludeFields = true,
        Converters = { new BigNumJsonConverter() }, // 大数序列化为紧凑科学串,读取兼容数字字面量
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
        GD.PrintRich($"[color=Green]已保存数据:[/color] {name}");
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
        T data = JsonSerializer.Deserialize<T>(json, SaveOptions);
        return data;
    }
}