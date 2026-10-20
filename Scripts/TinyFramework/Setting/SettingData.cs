// 文件：SettingData.cs
// 作者：急冻雪柜
// 描述：
// 日期：2025/11/20 16:37

namespace TinyFramework;

public class SettingData:SaveData
{
    public float Volume = 100;
    public float BGMVolume = 100;
    public float SFXVolume = 100;

    /// <summary>UI 主题：0=水墨 / 1=朱漆 / 2=青瓷 / 3=夜行（见 UIThemeManager）。
    /// 默认夜行：与原有深色视觉连续，升级瞬间不会"突然变脸"；水墨是浅色，留给主动选择。</summary>
    public int ThemeId = 3;

    /// <summary>数字显示模式:科学表示法(K/M/B/aa 后缀) 或 中文计数法(万/亿/兆/京...)。
    /// 默认中文:《轮回大侠》是武侠语境,UI 上蹦出 "15.0K" 会出戏;中文单位同时覆盖到 1载(1e44)。</summary>
    public NumberFormat NumberFormat = NumberFormat.Chinese;

    /// <summary>自动保存主档（间隔见 SaveBackup.AutoSaveMinutes，默认 10 分钟）。
    /// 默认开启：放置游戏没有"手动存档"的习惯动作，玩家直接关窗口丢进度是致命的。</summary>
    public bool AutoSave = true;
}