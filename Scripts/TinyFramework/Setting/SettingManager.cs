// 文件：SettingManager.cs
// 作者：急冻雪柜
// 描述：
// 日期：2025/11/20 16:36

namespace TinyFramework;

public class SettingManager:Singleton<SettingManager>
{
    private static SettingData _data;

    // 延迟初始化:首次访问时才加载存档,避免在静态初始化阶段就生成一个无意义的 Id
    public static SettingData Data => _data ??= SaveManager.Load<SettingData>();

    public override void Init()
    {
        base.Init();
        _ = Data;                          // 触发加载
        BigNum.DisplayMode = Data.NumberFormat; // 应用数字显示模式
    }

    /// <summary>运行时切换数字显示模式(科学/中文),立即生效并写回存档。</summary>
    public static void SetNumberFormat(NumberFormat mode)
    {
        Data.NumberFormat = mode;
        BigNum.DisplayMode = mode;
        SaveManager.Save(Data);
    }
}