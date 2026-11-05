using Godot;
using System;
using TinyFramework;

public partial class GameRoot : SingletonNode<GameRoot>
{
    public override void _EnterTree()
    {
        base._EnterTree();   // 设置 Instance,并在运行时自动 Init()
        if (Engine.IsEditorHint())
            return;

        // 场景树管理器:进入树时由 SingletonNode 自动 Init,此处只需 Create()
        UIManager.Create();
        AudioManager.Create();

        // 纯 C# 单例无场景树生命周期,需显式初始化一次
        SaveManager.Instance.Init();
        SettingManager.Instance.Init();

        // UI 主题：必须早于任何面板创建（读存档里的 ThemeId，并准备字体）
        UIThemeManager.Init();

        // 全局系数总账：把 TuningConfig 表注入 BattleConfig / StageConfig / StageRules / CultivationConfig。
        // 必须在任何建队 / 开战之前调用（表缺失时引擎退回代码兜底值，诊断见 Tuning.Errors）。
        Tuning.Load(ConfigManager.GetAll<TuningConfig>());

        // 入口：战斗显示面板（全屏底层，含背景与卡牌）→ 主面板（顶部 HUD 与全局状态）。
        // 顺序即层级：后者 AddChild 更晚，画在上层。主面板创建 BattleManager 时反向接上显示面板。
        UIManager.ShowPanel<BattlePanel>();
        UIManager.ShowPanel<MainPanel>();

        //UIManager.ShowPanel<StartPanel>();
    }
}
