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

    /// <summary>数字显示模式:科学表示法(K/M/B/aa 后缀) 或 中文计数法(万/亿/兆/京...)。</summary>
    public NumberFormat NumberFormat = NumberFormat.Scientific;
}