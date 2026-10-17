// 自动生成的数据类（字段与 addons/TinyTools/ConfigGenerator/SummonConfig.xlsx 第1行 1:1 对应）。
// 修改字段请改 Excel 并同步本文件（ConfigManager 以 System.Text.Json + IncludeFields 反序列化，
// JSON 键名 = Excel 列名 = C# 字段名，三者必须一致）。
using TinyFramework;
public class SummonConfig : BaseConfig
{
	/// <summary> 类型键(与 SkillConfig.SummonKind 对应) </summary>
	public string Kind;
	/// <summary> 召唤物名 </summary>
	public string Name;
	/// <summary> 气血=召唤者最大气血×本值 </summary>
	public float HpRatio;
	/// <summary> 固定气血(>0时不用 HpRatio,也不乘全局 HpScale;0=用 HpRatio) </summary>
	public float FixedHp;
	/// <summary> 每次命中固定扣血(>0 时无视伤害量;0=按实际伤害) </summary>
	public float DmgPerHit;
	/// <summary> 攻击=召唤者攻击×本值 </summary>
	public float AtkRatio;
	/// <summary> 速度下限 </summary>
	public float SpdFloor;
	/// <summary> 速度=召唤者速度×本值(与下限取大;0=不吃召唤者速度) </summary>
	public float SpdRatio;
	/// <summary> 基础受击权重(10基准) </summary>
	public int BaseW;
	/// <summary> 存在时间(秒) </summary>
	public float Ttl;
	/// <summary> 该类型同时存活上限(0=不限,只受全队 SummonCap 约束) </summary>
	public int SummonMax;
	/// <summary> 定位备注 </summary>
	public string Desc;
}
