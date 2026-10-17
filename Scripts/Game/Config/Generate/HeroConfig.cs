// 自动生成的数据类（字段与 addons/TinyTools/ConfigGenerator/HeroConfig.xlsx 第1行 1:1 对应）。
using TinyFramework;
public class HeroConfig : BaseConfig
{
	/// <summary> 侠客名 </summary>
	public string Name;
	/// <summary> 称号/境界 </summary>
	public string Title;
	/// <summary> 稀有度(枚举数值) </summary>
	public int Rarity;
	/// <summary> L1气血(标准1000×定位×基础随机×速度占模) </summary>
	public BigNum BaseHP;
	/// <summary> L1攻击(标准60×定位×基础随机×速度占模) </summary>
	public BigNum BaseATK;
	/// <summary> L1防御(标准7.5=原30×0.25折表;**直接**参与外功硬减/内功化解，无隐藏换算) </summary>
	public BigNum BaseDEF;
	/// <summary> 基础速度(蓄速;不随等级成长) </summary>
	public int BaseSPD;
	/// <summary> 暴击率0~1 [引擎未实装] </summary>
	public float CritRate;
	/// <summary> 暴击伤害倍率 [引擎未实装] </summary>
	public float CritDmg;
	/// <summary> 关联SkillConfig.Id(用|分隔) </summary>
	public int[] SkillIds;
	/// <summary> 是否召唤物 </summary>
	public bool IsSummon;
	/// <summary> 行动条阈值(0=用全局100) </summary>
	public int BaseThreshold;
	/// <summary> 基础受击权重(10基准;1号位×3) </summary>
	public int BaseW;
	/// <summary> 每级气血成长(绝对值;直接加算 HP(L)=BaseHP+本值×(L−1);标准100) </summary>
	public float HpGrowth;
	/// <summary> 每级攻击成长(绝对值;直接加算 ATK(L)=BaseATK+本值×(L−1);标准5) </summary>
	public float AtkGrowth;
	/// <summary> 每级防御成长(绝对值;直接加算 DEF(L)=BaseDEF+本值×(L−1);标准2) </summary>
	public float DefGrowth;
	/// <summary> 1~40级成长原型(三属性独立成长后按曲线形状事后归类) </summary>
	public GrowthType GrowthType;
}
