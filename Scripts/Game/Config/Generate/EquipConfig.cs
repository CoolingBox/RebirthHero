// 自动生成的数据类（字段与 addons/TinyTools/ConfigGenerator/EquipConfig.xlsx 第1行 1:1 对应）。
// 修改字段请改 Excel 并同步本文件（ConfigManager 以 System.Text.Json + IncludeFields 反序列化，
// JSON 键名 = Excel 列名 = C# 字段名，三者必须一致）。
using TinyFramework;
public class EquipConfig : BaseConfig
{
	/// <summary> 装备名 </summary>
	public string Name;
	/// <summary> 栏位 </summary>
	public EquipSlot Slot;
	/// <summary> 稀有度 </summary>
	public Rarity Rarity;
	/// <summary> 基础攻击加成(加到基础攻击,加算) </summary>
	public double BaseATK;
	/// <summary> 基础生命加成(加到基础生命,加算) </summary>
	public double BaseHP;
	/// <summary> 基础速度加成(加到基础速度,加算) </summary>
	public double BaseSPD;
	/// <summary> 套装ID(0=散件) </summary>
	public int SetId;
	/// <summary> 同稀有度掉落权重 </summary>
	public int DropWeight;
}
