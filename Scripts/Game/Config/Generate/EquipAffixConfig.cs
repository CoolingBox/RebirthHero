// 自动生成的数据类（字段与 addons/TinyTools/ConfigGenerator/EquipAffixConfig.xlsx 第1行 1:1 对应）。
// 修改字段请改 Excel 并同步本文件（ConfigManager 以 System.Text.Json + IncludeFields 反序列化，
// JSON 键名 = Excel 列名 = C# 字段名，三者必须一致）。
using TinyFramework;
public class EquipAffixConfig : BaseConfig
{
	/// <summary> 词条名 </summary>
	public string Name;
	/// <summary> 效果类型 </summary>
	public EquipAffixEffect Effect;
	/// <summary> 效果数值（百分比词条为百分点） </summary>
	public double Value;
	/// <summary> 抽取权重 </summary>
	public int Weight;
}
