// 文件：SaveBackup.cs
// 《轮回大侠》M2.6 —— **存档文件管理**（备份 / 导出 / 导入 / 列表 / 删除）
//
// 边界（2026-10-20 用户纠正过一条：不要重复实现框架能力）：
//   · 序列化与落盘/读盘   → 仍然走 `TinyFramework.SaveManager.Save / Load<T>`
//   · 框架管不到的才在这里：目录列举、改名、复制到任意路径、按时间排序
//     （`SaveManager` 只有单文件 Save/Load，没有 List/Rename/Copy —— 这不是重复实现）
//   · 主档（自动保存）固定名字 = `SaveManager` 默认的 `typeof(GameSave).Name`，即 user://GameSave
//   · 备份统一放 user://Backups/，**移动端只改名、不选路径**（沙盒里没有可写的浏览位置）
//
// 命名默认 "v版本号_年月日_时分"：玩家在文件管理器里也能一眼认出是哪一版、什么时候存的。

using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using TinyFramework;

/// <summary>一条存档（列表展示 + 载入用）。</summary>
public sealed class SaveEntry
{
    /// <summary>交给 SaveManager 的名字：主档 = "GameSave"，备份 = "Backups/xxx.json"。</summary>
    public string InternalName;
    /// <summary>显示名：主档给固定文案，备份给文件名（去扩展名）。</summary>
    public string DisplayName;
    public int Layer;
    public int BestLayer;
    public double Silver;
    public string TimeText;
    public bool IsMain;
}

public static class SaveBackup
{
    /// <summary>自动保存间隔（分钟）。存档设置面板只给"开/关"，间隔是设计口径。</summary>
    public const int AutoSaveMinutes = 10;

    /// <summary>主档名字 = SaveManager 默认文件名（写成常量便于列表/删除时识别）。</summary>
    public const string MainName = "GameSave";

    const string BackupFolder = "Backups";

    /// <summary>项目版本号（project.godot 的 application/config/version），用于备份默认名。</summary>
    public static string Version
    {
        get
        {
            Variant v = ProjectSettings.GetSetting("application/config/version", "0.0.0");
            string s = v.AsString();
            return string.IsNullOrEmpty(s) ? "0.0.0" : s;
        }
    }

    /// <summary>移动端：没有文件系统浏览 -> 只能改名，不提供"选择路径"。</summary>
    public static bool IsMobile
    {
        get { return OS.HasFeature("mobile"); }
    }

    /// <summary>备份目录（user://Backups）。用框架的 PathDefine 常量，不自己拼 user://。</summary>
    public static string BackupsDir
    {
        get { return Path.Combine(PathDefine.UserPath, BackupFolder); }
    }

    /// <summary>备份默认名：v0.7.0_20261020_1630。</summary>
    public static string DefaultName()
    {
        return "v" + Version + "_" + DateTime.Now.ToString("yyyyMMdd_HHmm");
    }

    // ============ 备份 ============

    /// <summary>把当前进度另存为一条备份，返回条目（失败返回 null）。</summary>
    public static SaveEntry CreateBackup(GameSave data, string displayName)
    {
        if (data == null) return null;

        EnsureDir();
        string internalName = NextFreeInternalName(displayName);
        SaveManager.Save(data, internalName);   // 序列化/写盘仍归框架
        return Describe(internalName, false);
    }

    /// <summary>
    /// 导出到任意路径（桌面版用）。实现：框架先写一份临时档 → 复制字节到目标 → 删临时档。
    /// 这样 JSON 的序列化口径只有框架一处，不会出现"导出的档读不回来"。
    /// </summary>
    public static bool ExportTo(GameSave data, string absolutePath)
    {
        if (data == null || string.IsNullOrEmpty(absolutePath)) return false;

        const string tempName = "ExportTemp";
        SaveManager.Save(data, tempName);

        string temp = PathOf(tempName);
        try
        {
            string dir = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.Copy(temp, absolutePath, true);
            return true;
        }
        catch (Exception e)
        {
            GD.Print("[Save] 导出失败：" + absolutePath + " → " + e.Message);
            return false;
        }
        finally
        {
            TryDelete(temp);
        }
    }

    /// <summary>从任意路径导入：复制进备份夹（成为一条可见备份），返回内部名；失败返回 null。</summary>
    public static string ImportFrom(string absolutePath)
    {
        if (string.IsNullOrEmpty(absolutePath) || !File.Exists(absolutePath)) return null;

        EnsureDir();
        string stem = Path.GetFileNameWithoutExtension(absolutePath);
        string internalName = NextFreeInternalName(stem);
        string target = PathOf(internalName);

        try
        {
            File.Copy(absolutePath, target, false);
        }
        catch (Exception e)
        {
            GD.Print("[Save] 导入失败：" + absolutePath + " → " + e.Message);
            return null;
        }

        // 复制完必须能被反序列化才算成功 —— 否则列表里会多一条点不开的坏档
        GameSave probe = LoadInternal(internalName);
        if (probe == null || !probe.Written)
        {
            TryDelete(target);
            return null;
        }

        return internalName;
    }

    // ============ 读 ============

    /// <summary>读一份存档（主档或备份）。文件不存在时框架返回 new T()（Written=false），这里按失败处理。</summary>
    public static GameSave LoadInternal(string internalName)
    {
        if (string.IsNullOrEmpty(internalName)) return null;

        try
        {
            GameSave data = SaveManager.Load<GameSave>(internalName);
            if (data == null || !data.Written) return null;
            return data;
        }
        catch (Exception e)
        {
            GD.Print("[Save] 读取失败：" + internalName + " → " + e.Message);
            return null;
        }
    }

    /// <summary>列出主档 + 全部备份（备份按修改时间倒序，最近的在最上面）。</summary>
    public static List<SaveEntry> ListAll()
    {
        List<SaveEntry> list = new List<SaveEntry>();

        SaveEntry main = Describe(MainName, true);
        if (main != null) list.Add(main);

        if (Directory.Exists(BackupsDir))
        {
            List<string> files = new List<string>();
            string[] found = Directory.GetFiles(BackupsDir, "*.json");
            for (int i = 0; i < found.Length; i++) files.Add(found[i]);

            files.Sort(CompareByWriteTimeDesc);

            for (int i = 0; i < files.Count; i++)
            {
                string internalName = BackupFolder + "/" + Path.GetFileName(files[i]);
                SaveEntry e = Describe(internalName, false);
                if (e != null) list.Add(e);   // 坏档直接跳过，不让它卡住整个列表
            }
        }

        return list;
    }

    static int CompareByWriteTimeDesc(string a, string b)
    {
        DateTime ta = File.GetLastWriteTime(a);
        DateTime tb = File.GetLastWriteTime(b);
        if (ta > tb) return -1;
        if (ta < tb) return 1;
        return 0;
    }

    // ============ 删 ============

    /// <summary>删除一条备份。主档不给删（要清档走「重开本世」）。</summary>
    public static bool Delete(string internalName)
    {
        if (string.IsNullOrEmpty(internalName) || internalName == MainName) return false;

        string path = PathOf(internalName);
        if (!File.Exists(path)) return false;

        TryDelete(path);
        return !File.Exists(path);
    }

    // ============ 内部 ============

    static SaveEntry Describe(string internalName, bool isMain)
    {
        GameSave data = LoadInternal(internalName);
        if (data == null) return null;

        SaveEntry e = new SaveEntry();
        e.InternalName = internalName;
        e.IsMain = isMain;
        e.DisplayName = isMain ? "主存档（自动保存）" : Path.GetFileNameWithoutExtension(internalName);
        e.Layer = data.Layer;
        e.BestLayer = data.BestLayer;
        e.Silver = data.SilverAmount;
        e.TimeText = data.UpdateTime.ToString("MM-dd HH:mm");
        return e;
    }

    static void EnsureDir()
    {
        if (!Directory.Exists(BackupsDir))
            Directory.CreateDirectory(BackupsDir);
    }

    /// <summary>把玩家输入的名字变成安全文件名，并保证不与已有备份撞名（撞了追加 _2/_3）。</summary>
    static string NextFreeInternalName(string rawName)
    {
        string stem = SafeName(rawName);
        string file = stem + ".json";
        int n = 2;
        while (File.Exists(Path.Combine(BackupsDir, file)))
        {
            file = stem + "_" + n + ".json";
            n++;
        }
        return BackupFolder + "/" + file;
    }

    /// <summary>把玩家输入的名字变成安全文件名（对话框默认名与存档夹文件名共用同一条规则）。</summary>
    public static string SafeName(string rawName)
    {
        string name = rawName;
        if (string.IsNullOrEmpty(name) || name.Trim().Length == 0) name = DefaultName();
        name = name.Trim();

        char[] bad = { '\\', '/', ':', '*', '?', '"', '<', '>', '|' };
        for (int i = 0; i < bad.Length; i++) name = name.Replace(bad[i], '_');

        if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            name = name.Substring(0, name.Length - 5);

        if (name.Length > 60) name = name.Substring(0, 60);
        if (name.Length == 0) name = DefaultName();

        return name;
    }

    static string PathOf(string internalName)
    {
        string rel = internalName.Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(PathDefine.UserPath, rel);
    }

    static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception e)
        {
            GD.Print("[Save] 删除失败：" + path + " → " + e.Message);
        }
    }
}
