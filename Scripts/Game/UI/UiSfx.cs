// 文件：UiSfx.cs
// 《轮回大侠》界面音效语义表
//
// 为什么要有这一层：
//   AudioManager.PlayUISFX(name) 只认文件名，调用处散着 "ui_click" 这种字面量 ——
//   "我要表达什么"（点了提交 / 钱不够）和"用哪个文件"混在一起。结果就是：
//     · 换素材要全文搜字符串；
//     · 资源不足也播 ui_click，听着像操作成功（改之前的天赋页就是这样）。
//   这里把动词钉死，素材名只出现在本文件；调用处读起来是 UiSfx.Deny()，语义自解释。
//
// 不做什么：不碰音量、总线、并发预算 —— 那些归 AudioManager（PlayUISFX 已经是
//   "居中 + 干声 + 不随机音高 + SFX_UI 总线"）。本类只做"动词 → 素材"。
//
// 素材口径：全部 Resources/Audio/SFX/ui_*.wav，44.1kHz 单声道，峰值统一 0.72
//   （新增三个由 .codebuddy/tools/GenUiSfx.py 生成，与原有六个响度对齐）。

using TinyFramework;

public static class UiSfx
{
    // ===== 素材名（只在这里出现）=====
    const string ClipClick = "ui_click";        // 0.10s 中低"嗒"
    const string ClipOpen = "ui_open";          // 0.35s 上扬
    const string ClipClose = "ui_close";        // 0.25s 低频"收"
    const string ClipConfirm = "ui_confirm";    // 0.70s 明亮确认
    const string ClipCancel = "ui_cancel";      // 0.20s 低频"退"
    const string ClipUpgrade = "ui_upgrade";    // 0.85s 上扬琶音
    const string ClipDeny = "ui_deny";          // 0.10s 中高频否定"嗡"
    const string ClipSelect = "ui_select";      // 0.04s 短"嗒"
    const string ClipToggle = "ui_toggle";      // 0.07s 机械"咔哒"

    /// <summary>普通按钮：没有副作用的一次点击（翻页、切换选中、次要动作）。
    /// 有结果的操作用 Confirm / Deny，别用这个。</summary>
    public static void Click()
    {
        AudioManager.PlayUISFX(ClipClick);
    }

    /// <summary>子界面打开。由**打开方**播（面板自己不知道是谁调它起来的）。</summary>
    public static void Open()
    {
        AudioManager.PlayUISFX(ClipOpen);
    }

    /// <summary>子界面关闭 / 退出确认。由**面板自己**播（它知道自己被关了）。</summary>
    public static void Close()
    {
        AudioManager.PlayUISFX(ClipClose);
    }

    /// <summary>操作成功：招募到人、穿上装备、存了档、点满了天赋。</summary>
    public static void Confirm()
    {
        AudioManager.PlayUISFX(ClipConfirm);
    }

    /// <summary>撤销：取消选择、下阵、放弃二次确认、返回上一层。</summary>
    public static void Cancel()
    {
        AudioManager.PlayUISFX(ClipCancel);
    }

    /// <summary>条件不满足：道行/银两不足、等级未解锁、装备穿着中不能回收。
    /// 和 Cancel 的区别是"不是玩家主动退回，而是系统拒绝了这次操作"，
    /// 所以音色要用中高频的否定音，不能是柔软的"退"。</summary>
    public static void Deny()
    {
        AudioManager.PlayUISFX(ClipDeny);
    }

    /// <summary>选项切换：页签、倍速档位、主题、数字格式、列表项。
    /// 比 Click 更短更硬，连续切页签不会糊成一片。</summary>
    public static void Select()
    {
        AudioManager.PlayUISFX(ClipSelect);
    }

    /// <summary>勾选框 / 开关。</summary>
    public static void Toggle()
    {
        AudioManager.PlayUISFX(ClipToggle);
    }

    /// <summary>变强了：强化成功、升级成功。比 Confirm 多一段上扬，值得多播 0.15s。</summary>
    public static void Upgrade()
    {
        AudioManager.PlayUISFX(ClipUpgrade);
    }
}
