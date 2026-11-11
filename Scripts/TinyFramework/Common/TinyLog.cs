// 文件：TinyLog.cs
// 作者：急冻雪柜
// 描述：**分级日志** —— 统一的输出入口，按等级过滤 + 按频道归类
// 日期：2026/11/11
//
// 为什么要它（2026-11-11 对照 GameFramework 的 Log 模块）：
//   项目里现在有 100+ 处直接 `GD.Print` / `GD.PrintErr`，问题有三：
//     ① 发布版照样打印，控制台被"每帧一次"的调试输出刷屏，真错误被冲走；
//     ② 同一条错误在三个地方各印一遍，无法判断"这是同一个问题还是三个"；
//     ③ 没有频道，出问题时要通读全部输出才能找到是哪一块在说话。
//   本类只做三件事：**分级**（Debug 发布版不输出）、**频道**（`[Save]` / `[Audio]` 前缀）、
//   **统一格式**。不做文件落盘 —— 放置游戏的日志量用 Godot 自己的输出窗口足够，
//   真需要落盘再加 FileAccess 写入（别为此引一个日志框架）。
//
// 用法：
//   TinyLog.Info("Save", "已写入 GameSave");
//   TinyLog.Warn("Tuning", "表里有 [Slot1W]，代码里找不到同名字段");
//   TinyLog.Error("Res", "加载失败: " + path);
//   TinyLog.EnableDebugChannel("Battle");   // 只开战斗的 Debug，别的照旧静默

using System.Collections.Generic;
using Godot;

namespace TinyFramework;

public static class TinyLog
{
    public enum Level
    {
        Debug = 0,
        Info = 1,
        Warn = 2,
        Error = 3,
    }

    /// <summary>全局最低输出等级。默认 Info（Debug 只在显式打开时输出）。</summary>
    public static Level MinLevel = Level.Info;

    /// <summary>总开关（性能分析时一键静音）。</summary>
    public static bool Enabled = true;

    private static readonly HashSet<string> _debugChannels = new HashSet<string>();

    /// <summary>允许某个频道输出 Debug（其余频道仍按 <see cref="MinLevel"/> 过滤）。</summary>
    public static void EnableDebugChannel(string channel)
    {
        if (string.IsNullOrEmpty(channel))
        {
            return;
        }
        _debugChannels.Add(channel);
    }

    /// <summary>关掉某个频道的 Debug。</summary>
    public static void DisableDebugChannel(string channel)
    {
        if (string.IsNullOrEmpty(channel))
        {
            return;
        }
        _debugChannels.Remove(channel);
    }

    public static void Debug(string message)
    {
        Debug("General", message);
    }

    public static void Debug(string channel, string message)
    {
        if (!Enabled)
        {
            return;
        }
        if (MinLevel > Level.Debug && !_debugChannels.Contains(channel))
        {
            return;
        }
        GD.Print(Format("DEBUG", channel, message));
    }

    public static void Info(string message)
    {
        Info("General", message);
    }

    public static void Info(string channel, string message)
    {
        if (!Enabled || MinLevel > Level.Info)
        {
            return;
        }
        GD.Print(Format("INFO", channel, message));
    }

    public static void Warn(string message)
    {
        Warn("General", message);
    }

    public static void Warn(string channel, string message)
    {
        if (!Enabled || MinLevel > Level.Warn)
        {
            return;
        }
        GD.PushWarning(Format("WARN", channel, message));
    }

    public static void Error(string message)
    {
        Error("General", message);
    }

    public static void Error(string channel, string message)
    {
        if (!Enabled || MinLevel > Level.Error)
        {
            return;
        }
        GD.PushError(Format("ERROR", channel, message));
    }

    private static string Format(string level, string channel, string message)
    {
        if (string.IsNullOrEmpty(channel))
        {
            channel = "General";
        }
        return "[" + level + "][" + channel + "] " + message;
    }
}
