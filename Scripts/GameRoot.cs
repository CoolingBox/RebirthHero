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

        // 全局 2D 辉光环境：引擎原生多尺度 Glow（Godot 版的 bloom 金字塔）。
        // 只注册到 viewport，位置无关，但要早于首帧绘制。
        SetupGlowEnvironment();

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

    /// <summary>
    /// 全局 2D Glow 环境（2026-11-16 跨引擎调研后启用，见桌面调研报告 §1）。
    ///
    /// Godot 的 glow 就是标准 bloom 管线：HDR 阈值提取 → glow levels 多尺度
    /// 降采样金字塔 → 上采样累加 → 在 tonemap 之前按 Screen 合成回原图。
    /// 与 Unity URP / Unreal 的 Bloom 同构，比自研单 pass 描边更贴近物理散射。
    ///
    /// 选择性靠 HDR 而不是 layer mask：卡面 / UI / 背景颜色都 &lt;= 1，
    /// 阈值 1.0 下不会发光；只有 BattleFxGlow 注入过 emissive 的特效像素
    /// 会超过阈值。先验探针实测过：HDR 块发光、普通块与卡面人物不发光。
    ///
    /// 参数来自 6 组实机扫描（preview/scan_envglow_grid.png）的第 0 / 2 组：
    /// 强度 0.70、只开前 4 级。扫描用实心 HDR 方块，实战是稀疏像素特效，
    /// 能量少得多，所以比最克制的第 0 组略往上调一档；5~7 级一律不开，
    /// 否则就是 UE 文档里说的整屏光污染。
    /// 关闭：注释掉 _EnterTree 里的 SetupGlowEnvironment() 调用即可。
    /// </summary>
    void SetupGlowEnvironment()
    {
        Godot.Environment env = new Godot.Environment();
        env.BackgroundMode = Godot.Environment.BGMode.Canvas;
        env.BackgroundCanvasMaxLayer = 10;
        env.GlowEnabled = true;
        env.GlowHdrThreshold = 1.0f;
        env.GlowHdrScale = 1.0f;
        env.GlowIntensity = 0.70f;
        env.GlowStrength = 1.0f;
        env.GlowBloom = 0.0f;
        env.GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Screen;
        // 开前 4 级：1~3 给紧贴特效的亮晕，4 给一点外溢；
        // 5~7 是全屏级，开了会把整片画面染亮（扫描 #3 已验证），一律关闭。
        for (int i = 0; i < 7; i++)
        {
            env.SetGlowLevel(i, i < 4 ? 1.0f : 0.0f);
        }

        WorldEnvironment we = new WorldEnvironment();
        we.Name = "FxGlowEnvironment";
        we.Environment = env;
        AddChild(we);
    }
}
