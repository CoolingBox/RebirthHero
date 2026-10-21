// 自动生成的数据类（字段与 addons/TinyTools/ConfigGenerator/SkillConfig.xlsx 第1行 1:1 对应）。
// 修改字段请改 Excel 并同步本文件（ConfigManager 以 System.Text.Json + IncludeFields 反序列化，
// JSON 键名 = Excel 列名 = C# 字段名，三者必须一致）。
using TinyFramework;
public class SkillConfig : BaseConfig
{
	/// <summary> 武学名 </summary>
	public string Name;
	/// <summary> 描述 </summary>
	public string Desc;
	/// <summary> 相性 </summary>
	public Category Category;
	/// <summary> 触发时机 </summary>
	public TriggerType TriggerTime;
	/// <summary> 触发条件类型 </summary>
	public CondType CondType;
	/// <summary> 需求次数(条件=次数) </summary>
	public int CondN;
	/// <summary> 触发概率%(条件=概率/连击) </summary>
	public int CondP;
	/// <summary> 血量阈值%(触发时机=血量阈值) </summary>
	public int HpThreshold;
	/// <summary> 伤害类型(外功吃防御减免/内功无视防御) </summary>
	public DamageType DamageType;
	/// <summary> 效果词 </summary>
	public SkillEffect EffectType;
	/// <summary> 数值基准 </summary>
	public CalcBase CalcBase;
	/// <summary> 数值:倍数×100/百分比/固定值(权重类为权重增量,10基准) </summary>
	public double CalcValue;
	/// <summary> 索敌规则 </summary>
	public SkillTarget TargetType;
	/// <summary> 不能普攻(替换该次普攻) </summary>
	public bool CannotAttack;
	/// <summary> 重放概率% (0=用全局保底1%) </summary>
	public int ReplayChance;
	/// <summary> 重放上限=总命中次数 (0=用全局保底2) </summary>
	public int ReplayLimit;
	/// <summary> 重放锁定索敌 </summary>
	public bool ReplayLock;
	/// <summary> 每场释放次数上限(0=不限) </summary>
	public int Limit;
	/// <summary> 召唤物类型 </summary>
	public string SummonKind;
	/// <summary> 该武学同时存活上限(0=跟随召唤物类型档位 SummonConfig.SummonMax;>0=本卡专属覆盖) </summary>
	public int SummonMax;
	/// <summary> 召唤时号令己方【同类型】召唤物各立即普攻一次 </summary>
	public bool SummonOrder;
	/// <summary> 每次触发召唤几只(1=单只;>1=一次多只,仍受同时上限约束) </summary>
	public int SummonCount;
	/// <summary> 吸血 </summary>
	public bool Vamp;
	/// <summary> 附带沉默5s </summary>
	public bool SilenceAlso;
	/// <summary> 实装状态(待实装=引擎尚未实现该机制,构建时应过滤) </summary>
	public SkillStatus Status;
	/// <summary> 武学品阶(1入门/2精良/3上乘/4绝学;敌方按层限制可用上限 TierCap=1+(层-1)/10,最高4=不限制) </summary>
	public int Tier;
	/// <summary> 副效果词(0/缺省=无)。一张卡的第二档，触发时机与 CondType 沿用主行 </summary>
	public SkillEffect AltEffect;
	/// <summary> 副效果数值基准 </summary>
	public CalcBase AltCalcBase;
	/// <summary> 副效果数值(与 CalcValue 同口径) </summary>
	public double AltCalcValue;
	/// <summary> 副效果需求次数(条件=1次数) </summary>
	public int AltCondN;
	/// <summary> 副效果每场释放次数上限(0=不限) </summary>
	public int AltLimit;
}
