# 轮回大侠 · UI 音效规范 v0.1

> 2026-11-17 ｜ 适用范围：全部界面（战斗 HUD、子界面、弹窗）的**非战斗**反馈音
> 代码入口：`Scripts/Game/UI/UiSfx.cs` ｜ 素材目录：`Resources/Audio/SFX/ui_*.wav`

---

## 0. 结论先行

| 项 | 口径 |
|---|---|
| 一套界面音的动词数 | **9 个**：Click / Open / Close / Confirm / Cancel / Deny / Select / Toggle / Upgrade |
| 调用方式 | 只准写 `UiSfx.Xxx()`，**不准再出现 `"ui_xxx"` 字面量** |
| 核心规则 | **有结果的操作必须播 Confirm 或 Deny** —— 借 Click 顶替 = 玩家听不出成功还是失败 |
| 音色家族 | 全部模态合成（木块 / 竹 / 铜铃 / 梆子 / 古筝），峰值统一 0.72 |
| 随机音高 | **不做**。界面音变调会让按钮显得不可靠（与打击音 ±10% 的处理刻意相反） |
| 总线 | `SFX_UI`（纯干声、居中、永不空间化），随「音效」滑杆缩放 |

---

## 1. 九个动词：什么时候用哪个

| 动词 | 素材 / 听感 | 用在哪 | 反例（不要这么用） |
|---|---|---|---|
| `Click` | ui_click · 木鱼轻叩 0.10s | 普通按钮：翻页、居中视图、进入二次确认态 | 提交/购买成功 |
| `Open` | ui_open · 木门开合 0.35s | 子界面打开（由**打开方**播） | 面板内部的小弹层反复播 |
| `Close` | ui_close · 木门收拢 0.25s | 子界面关闭、退出确认（由**面板自己**播） | 单位阵亡导致的自动收起 |
| `Confirm` | ui_confirm · 铜铃 0.70s | 操作成功：招募、穿上、存档、载入、洗点 | 条件不满足 |
| `Cancel` | ui_cancel · 低频「退」0.20s | 玩家主动撤回：卸下、下阵、清空技能槽 | 系统拒绝（那是 Deny） |
| `Deny` | ui_deny · 梆子双击 0.20s | 资源不足、前置未点亮、大劫中锁死、坏档 | 玩家自己按取消 |
| `Select` | ui_select · 竹片 0.05s | 选项切换：页签、倍速档、主题、数字格式、筛选项、选层 | 提交型按钮 |
| `Toggle` | ui_toggle · 木榫「咔哒」0.07s | 勾选框 / 开关 | 普通按钮 |
| `Upgrade` | ui_upgrade · 古筝 C5→G5 0.85s | 变强：装备强化成功 | 任何失败分支 |

**为什么 Deny 不能借 Cancel**：
ui_cancel 是低频、柔软、下行的「退」，语义是"我不玩了，回去"；
资源不足是"你想做的事没做成"，需要一个**更硬、更靠中高频、且带两次击打**的否定音。
两者混用会出现"点不动天赋点，却听见一声温和的返回音"这种错位（改之前正是如此）。

---

## 2. 素材与合成口径

全套 9 个文件：44.1kHz / 单声道 / 16bit。**峰值统一 0.72**（-2.9dBFS），保证任意两个按钮之间没有音量跳变。

| 文件 | 时长 | f0 | 模态比（振动体） | 备注 |
|---|---|---|---|---|
| ui_click | 0.10s | 420Hz | 1 : 2.34 : 3.93（木块） | 原有 |
| ui_select | 0.05s | 760Hz | 1 : 2.72 : 5.05（竹筒） | 2026-11-17 新增，比 click 高一个八度、短一半 |
| ui_toggle | 0.07s | 1080→880Hz | 1 : 2.10 : 3.30（木榫） | 新增，两段瞬态相距 20ms = 搭扣落位 |
| ui_open | 0.35s | 150Hz | 竹木 + 摩擦 | 原有 |
| ui_close | 0.25s | 120Hz | 竹木 + 摩擦 | 原有 |
| ui_confirm | 0.70s | 880Hz | 1 : 1.51 : 2.32 : 3.62 : 4.85（铜铃圆板） | 原有 |
| ui_cancel | 0.20s | 275Hz | 1 : 2.0 : 3.36（木块，下行） | 原有 |
| ui_deny | 0.20s | 400→330Hz | 1 : 2.32 : 3.90（梆子）双击 | 新增，第二击低 70Hz |
| ui_upgrade | 0.85s | C5→G5 | Karplus-Strong 拨弦（宫→徵） | 原有 |

**为什么不用 Kenney `Interface Sounds` 的 error/select/toggle**：
那三个是噪声型电子音（FFT 上 275~400Hz 一片散峰），与已有六个的干净模态峰不是一族；
实测频段分布也对不上（Kenney 的 >8kHz 能量明显，现有六个为 0）。混进去会立刻听出"两套界面"。
新增三个由 `.codebuddy/tools/GenUiSfx.py` 用同一套配方合成，实测频段占比与 ui_click 一致（<1kHz 占 97%）。

---

## 3. 覆盖矩阵（2026-11-17 全量梳理）

| 界面 | 打开 | 关闭 | 成功 | 失败/拒绝 | 选项 | 开关 |
|---|---|---|---|---|---|---|
| 顶部导航 / 子界面 | Open | Close | – | – | – | – |
| 客栈 InnPanel | Open（主面板播） | Close | 招募 / 上阵 Confirm | 刷新银两不足 Deny | – | – |
| 武学 SkillsPanel | Open | Close | 装配 Confirm | – | 选槽 / 换页 Select | – |
| 装备 EquipPanel | Open | Close | 穿 / 强化 Upgrade / 回收 Confirm | 已穿不可回收 Deny | 筛选排序 Select | – |
| 天赋 TalentPanel | Open | Close | 点满 / 洗点 Confirm | 道行不足 / 前置未亮 / 江湖未解锁 Deny | 换树 Select | – |
| 选层 StagePanel | Open | Close | – | 大劫中锁死 Deny | 选层 Select | – |
| 卡册 GalleryPanel | Open | Close | – | – | 选卡 Select | – |
| 战报 StatsPanel | Open（常驻侧栏） | Close（再点一次） | – | – | 视图/排序 Select | – |
| 存档 SavePanel | Open | Close | 保存/备份/导入/载入/删除 Confirm | 各失败分支 Deny | – | 自动保存 Toggle |
| 设置 SettingPanel | Open | Close / 退出 Close | 恢复默认 Confirm | – | 主题/倍速/数字 Select | 自动保存 Toggle |
| 卡牌详情 UnitDetailView | Open | Close（仅玩家点关闭） | – | – | – | – |
| 战斗结算栏 | – | – | 转世二次确认 Confirm | – | 继续推图 / 转世第一下 Click | – |

约定：
- **打开音由打开方播**（它知道是谁调起来的），**关闭音由面板自己播**（它知道自己被关）。
- **系统触发的收尾不播**：单位阵亡导致详情浮层收起、读档导致整屏重建 —— 那不是玩家操作。

---

## 4. 代码侧不变量

1. 素材名只出现在 `UiSfx.cs`；其它文件出现 `"ui_` 字面量即违规。
2. `UiSfx` 不碰音量、总线、并发 —— 那些归 `AudioManager.PlayUISFX`。
3. 界面音优先级 `SfxPriority.Ui = 5`，高于全部战斗音；并发超预算时抢占的是战斗音，不是按钮音。
4. 新增动词时：先定"玩家在什么心理状态下听到它"，再选时长与 f0 —— 不是先找素材。

---

## 5. 验证方法

| 手段 | 能证明什么 |
|---|---|
| `dotnet build` | 调用面全绿（九个方法都有实现） |
| `--headless --path . --quit-after 200` | `WarmupSfx` 不报「音效缺失」，九个文件都能被 `ResourceLoader` 找到 |
| 频段/谱峰脚本 | 新增素材与已有素材同族（<1kHz 占比、模态峰位置） |
| 试听串 `preview/ui_sfx/ui_sfx_set.wav` | 人耳验收：整串按"click → select → toggle → open → close → confirm → cancel → deny → upgrade"排列 |

---

## 6. 已知未做（有需要再加）

- **悬停音（rollover）**：网格型界面（卡册 8×N、天赋树）里悬停音会连成风铃，暂不做。
- **滑杆刻度 tick**：音量滑杆 Step=1，拖一次要响上百下，只在松手时给一次 Click。
- **胜负结算音**：已由 `clear_layer` / `boss_down` / `crit` 等战斗事件音承担，界面层不重复。
- **BGM 切换**：目前只有战斗 BGM 循环，切换场景时没有淡入淡出。

---

## 7. 变更记录

- 2026-11-17：建立本规范。九个动词收口到 `UiSfx`；新增 deny / select / toggle 三个合成音；
  补齐客栈（原来一个音都没有）、战报、卡册、武学、选层、卡牌详情的反馈；
  失败分支（存档/导入/载入/回收/强化/洗点）从"静默"或"误用 Click"改为 Deny。
