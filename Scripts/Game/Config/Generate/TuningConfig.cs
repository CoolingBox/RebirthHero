// 自动生成的数据类（字段与 addons/TinyTools/ConfigGenerator/TuningConfig.xlsx 第1行 1:1 对应）。
using TinyFramework;
public class TuningConfig : BaseConfig
{
	/// <summary> 系数名(与 C# public static 字段同名;Runtime=true 时必须能对上) </summary>
	public string Key;
	/// <summary> 值 </summary>
	public double Value;
	/// <summary> 分组 </summary>
	public string Group;
	/// <summary> 引擎是否读取(False=仅登记) </summary>
	public bool Runtime;
	/// <summary> 来源(审计用: 用户指定/用户口径/文档规定/我自拟) </summary>
	public string Source;
	/// <summary> 作用与备注 </summary>
	public string Note;
}
