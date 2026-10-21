// 自动生成的数据类（字段与 addons/TinyTools/ConfigGenerator/EquipSetConfig.xlsx 第1行 1:1 对应）。
// 修改字段请改 Excel 并同步本文件（ConfigManager 以 System.Text.Json + IncludeFields 反序列化，
// JSON 键名 = Excel 列名 = C# 字段名，三者必须一致）。
using TinyFramework;
public class EquipSetConfig : BaseConfig
{
	/// <summary> 套装名 </summary>
	public string Name;
	/// <summary> 设计意图（服务哪条流派，给策划看） </summary>
	public string Desc;
	/// <summary> 2 件效果类型 </summary>
	public SetEffect Effect2;
	/// <summary> 2 件效果数值 </summary>
	public double Value2;
	/// <summary> 4 件效果类型 </summary>
	public SetEffect Effect4;
	/// <summary> 4 件效果数值 </summary>
	public double Value4;
}
