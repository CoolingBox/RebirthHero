// 自动生成的数据类（字段与 addons/TinyTools/ConfigGenerator/QualityConfig.xlsx 第1行 1:1 对应）。
using TinyFramework;
public class QualityConfig : BaseConfig
{
	/// <summary> 词条名 </summary>
	public string Name;
	/// <summary> 效果描述(数值改了必须同步这里) </summary>
	public string Desc;
	/// <summary> 效果1枚举(0=无) </summary>
	public AffixEffect Effect1;
	/// <summary> 效果1数值 </summary>
	public float Value1;
	/// <summary> 效果2枚举(0=无) </summary>
	public AffixEffect Effect2;
	/// <summary> 效果2数值 </summary>
	public float Value2;
	/// <summary> 1已实装/2待实装 </summary>
	public SkillStatus Status;
	/// <summary> 适用相性(仅 MulPct 用：匹配 SkillConfig.Category；0=不限相性/全部武学) </summary>
	public Category Category;
}
