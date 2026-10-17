// 自动生成的数据类（字段与 addons/TinyTools/ConfigGenerator/EnemyConfig.xlsx 第1行 1:1 对应）。
using TinyFramework;
public class EnemyConfig : BaseConfig
{
	/// <summary> 敌名 </summary>
	public string Name;
	/// <summary> 类型 </summary>
	public EnemyKind Kind;
	/// <summary> 第1层基础气血(标准174=150×1.16) </summary>
	public float BaseHp;
	/// <summary> 第1层基础攻击(标准26.9=24×1.12) </summary>
	public float BaseAtk;
	/// <summary> 第1层基础防御(标准16.5=15×1.10) </summary>
	public float BaseDef;
	/// <summary> 每层气血成长倍率(标准1.16) </summary>
	public float HpRate;
	/// <summary> 每层攻击成长倍率(标准1.12) </summary>
	public float AtkRate;
	/// <summary> 每层防御成长倍率(标准1.10) </summary>
	public float DefRate;
	/// <summary> 基础速度(蓄速;不随层成长) </summary>
	public float Spd;
	/// <summary> 基础受击权重(10基准) </summary>
	public int BaseW;
	/// <summary> 携带武学数基数(小怪用;精英/层主由 Skills 固定组决定) </summary>
	public int SkillCount;
	/// <summary> 每多少层+1个武学(小怪用;0=不增) </summary>
	public int SkillTier;
	/// <summary> 最早出现层(档位;抽取取 MinLayer 最大的一档) </summary>
	public int MinLayer;
	/// <summary> 固定武学ID(用|分隔;精英/层主必填,小怪留空=按层随机抽) </summary>
	public int[] Skills;
	/// <summary> 定位备注 </summary>
	public string Desc;
}
