// 自动生成的数据类（字段与 addons/TinyTools/ConfigGenerator/TalentConfig.xlsx 第1行 1:1 对应）。
// 修改字段请改 Excel 并同步本文件（ConfigManager 以 System.Text.Json + IncludeFields 反序列化，
// JSON 键名 = Excel 列名 = C# 字段名，三者必须一致）。
//
// ⚠️ 本表是**手工维护**的 POCO（GenConfig 只出 JSON，不出 POCO）。
//
// 口径（用户 2026-11-02 三次演进）：
//   ① "从中间开始，4 条主线向 4 处散开，天赋有些可以点多次"
//   ② "UI 可拖动" + "不要只限定属性，可以多设计经济/武学解锁"
//   ③ "只有 4 主线没有分支" → 主干 3 层后**分叉成两条子路**，相邻两支的子路末端**汇到交汇节点**
//
// Id 编码：Branch(1位) + Path(1位) + Depth(1位)
//   101 = 外功主干第 1 层 / 111 = 外功左子路第 1 层 / 190 = 外功↔内功交汇
//   第 2 棵树用 2xxx：2000 = 中心「入世」，2101~2104 = 兵刃支 1~4 层（其余支类推）
//
// 两棵树（用户 2026-11-04："全新的第二天赋树，点满第一个天赋树才出现，可以切换查看"）：
//   第 1 棵「武学」= 自身的攻/守/速/财（十字分叉 + 交汇 + 外环）
//   第 2 棵「江湖」= 身外之物（兵刃/人脉/声望/阅历，同心圆），前置链更长、成本断层 ×32 起、
//     末端是可点到 99 级的无底洞（CostPow 1.6）—— 它是道行在后期唯一的去处。
//
// 前置用 ReqA / ReqB 直接写节点 Id（不再靠 Depth 推导）—— 交汇节点要**两个**前置，
// 这是"交错"成立的前提：必须相邻两支都走到，才点得动那个交汇放大器。
using TinyFramework;
public class TalentConfig : BaseConfig
{
	/// <summary> 第几棵树：1=武学树（自身） 2=江湖树（装备/客栈/经济/大劫）；点满第 1 棵才出现第 2 棵 </summary>
	public int Tree;
	/// <summary> 分支 1外功(上) 2内功(右) 3身法(下) 4悟性(左)；交汇节点写起始支（第 2 棵：1兵刃 2人脉 3声望 4阅历） </summary>
	public int Branch;
	/// <summary> 路径：0=主干 1=左子路 2=右子路 9=交汇（同时决定 UI 角度） </summary>
	public int Path;
	/// <summary> 深度：主干 1~3，子路 1~2，交汇固定 1（同时决定 UI 半径） </summary>
	public int Depth;
	/// <summary> 节点名（最多 2 字：UI 节点只有 60px 宽） </summary>
	public string Name;
	/// <summary> 效果类型（见 TalentEff 枚举） </summary>
	public int EffectType;
	/// <summary> 每级效果值（百分比填数字本身） </summary>
	public double Value;
	/// <summary> 可点层数（&gt;1 = 可重复点） </summary>
	public int MaxLevel;
	/// <summary> 第 1 级成本（第 L 级 = CostBase × L × CostPow^(L−1)） </summary>
	public int CostBase;
	/// <summary> 逐级成本倍率（1 = 线性；&gt;1 = 指数，末端无底洞用 1.6） </summary>
	public double CostPow;
	/// <summary> 前置节点 Id（0 = 无） </summary>
	public int ReqA;
	/// <summary> 第二前置节点 Id（交汇用，0 = 无）—— 两个都要至少 1 级 </summary>
	public int ReqB;
	/// <summary> 一句话说明 </summary>
	public string Desc;
}
