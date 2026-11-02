// 自动生成的数据类（字段与 addons/TinyTools/ConfigGenerator/RealmConfig.xlsx 第1行 1:1 对应）。
// 修改字段请改 Excel 并同步本文件（ConfigManager 以 System.Text.Json + IncludeFields 反序列化，
// JSON 键名 = Excel 列名 = C# 字段名，三者必须一致）。
//
// ⚠️ 本表是**手工维护**的 POCO（GenConfig 只出 JSON，不出 POCO）。
//
// 口径说明（2026-11-01）：突破门槛用 **银两 + 层数**，不是《境界与天赋树 v0.1》里写的"修为"——
// GDD §4.1 定死了「修为 = 经验，攒满自动升级，不留存」，它不能当货币用。
// GDD 的银两 sink 列表里本来就列了"突破境界"，所以门槛落在银两上。
using TinyFramework;
public class RealmConfig : BaseConfig
{
	/// <summary> 境界名 </summary>
	public string Name;
	/// <summary> 境界乘区：玩家 HP/ATK/DEF 同时 ×本值(初入江湖=1) </summary>
	public double Mult;
	/// <summary> 突破消耗银两(0=开局自带) </summary>
	public double ReqSilver;
	/// <summary> 突破需已达的最高层数 </summary>
	public int ReqLayer;
	/// <summary> 解锁项(本期只做数值乘区,解锁待实装) </summary>
	public string Unlock;
	/// <summary> 设计备注 </summary>
	public string Desc;
}
