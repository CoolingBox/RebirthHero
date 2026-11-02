// 自动生成的数据类（字段与 addons/TinyTools/ConfigGenerator/DajieBossConfig.xlsx 第1行 1:1 对应）。
// 修改字段请改 Excel 并同步本文件（ConfigManager 以 System.Text.Json + IncludeFields 反序列化，
// JSON 键名 = Excel 列名 = C# 字段名，三者必须一致）。
//
// ⚠️ 本表是**手工维护**的 POCO（GenConfig 只出 JSON，不出 POCO）。
//
// 口径（用户 2026-11-01）：大劫走独立表，**18 档**，按境界阶梯排（初入江湖 → 三流 → 二流 → 一流 → 绝顶）。
//   · **属性写死绝对值**（不随推图层成长、不乘 k^wins）
//   · 打赢 → 拿 RewardDao 道行 + 局内资源（RewardKill / RewardEquip），继续这一世（下一场换档）
//   · 打输 → 转世
//   · 玩家可以「提前迎战」主动挑战当前档位
// 后果：固定属性迟早被打穿，所以表尾（第 18 档：1170 万血 / 21 万攻）是这一世的终点。
//
// 曲线（2026-11-02 用户连说两次"成长有点高" + "都降"，整条一起压）：
//   1~6 档 血 ×2.0 / 攻 ×1.45 / 防 ×1.4（900 → 2.88 万血）；
//   7~18 档 血 ×1.65 / 攻 ×1.75 / 防 ×1.6（2.88 万 → 1170 万血）。
//   初版是前段 ×2.5 + 后段 ×2.2 三连，末端冲到 11.3 亿血 —— 比玩家实际能达到的量级高太多，
//   "打不过"的断层会横跨 3~4 档，玩家只会看到"连续几档都差得离谱"。现在末端降到 1/96。
//   **攻的成长率刻意高于血**（血攻比 113 → 56）：写死绝对值永远追不上玩家的境界乘区（×10000），
//   所以真正的威胁是"血量打不完 → 超时判负"（`T >= BattleConfig.Limit` → 判负），
//   攻击只负责让超时来得更快。
//   跨度依据：玩家一世成长 ≈ 等级 ×4.9 × 境界（破 1~2 档 ×10~100）≈ ×50~500，
//   按这条曲线一世能跨 9~13 档 —— 18 档约两世走完。
//
// 为什么不是 6 档：6 档时打赢最后一档后 Pick 永远返回同一行 —— 玩家要么反复刷同一个 Boss、
//   要么被打不动，中间没有"再往上走一步"的空间。18 档把整条阶梯铺开，每一世能爬到哪一档，
//   就是这一世构筑强度的读数。
// ⚠️ 配招必须用**全池**（BattlePanel 传 _skillPool，不是 _enemyPool）：小怪池按 `EffectType <= 14`
//   过滤，Boss 的按盾加伤(26)/护盾保持率(19)/斩杀(25) 会被静默跳过，Boss 只剩普攻。
//
// ⚠️ 局内资源为什么用「等效击杀数」而不是写死银子数（用户 2026-11-01："打赢不单有道行，
//   要有局内资源"）：普通产出本身是 2.5 × 1.16^层 的指数（CultivationConfig），
//   奖励写死绝对值在第 30 层就是零头。用"这一场相当于多少次击杀"表示，一个数字撑起三样资源，
//   且永远按**结算时的层**换算 —— 不会通胀也不会贬值。
using TinyFramework;
public class DajieBossConfig : BaseConfig
{
	/// <summary> 仇家名号（武侠语域，不用仙侠） </summary>
	public string Name;
	/// <summary> 本世已打赢多少场后换这一档(0=第1场就用) </summary>
	public int MinWins;
	/// <summary> 基础气血（**绝对值，不随层成长**） </summary>
	public float BaseHp;
	/// <summary> 基础攻击（绝对值） </summary>
	public float BaseAtk;
	/// <summary> 基础防御（绝对值） </summary>
	public float BaseDef;
	/// <summary> 速度(比玩家 55 快 → 先手压制) </summary>
	public float Spd;
	/// <summary> 基础受击权重(单体目标，实际影响很小) </summary>
	public int BaseW;
	/// <summary> 打赢这一档给多少道行 </summary>
	public double RewardDao;
	/// <summary> 打赢相当于多少次普通击杀的**局内资源**（修为/银两/材料都按它换算） </summary>
	public int RewardKill;
	/// <summary> 打赢必掉几件装备（0=不掉） </summary>
	public int RewardEquip;
	/// <summary> 固定武学组(用|分隔的 SkillConfig.Id；空=只会普攻) </summary>
	public int[] Skills;
	/// <summary> 定位备注 </summary>
	public string Desc;
}
