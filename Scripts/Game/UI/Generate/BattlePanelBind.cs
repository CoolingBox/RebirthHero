using Godot;
using TinyFramework;

public partial class BattlePanel : BasePanel
{
	private Panel _TBg;
	private Control _TCards;
	private Control _TStacks;
	private Control _TFxLayer;
	private Panel _TNavBar;
	private HBoxContainer _TNavRow;
	private Label _TTitle;
	private Button _TInnBtn;
	private Button _TEquipBtn;
	private Button _TSkillBtn;
	private Button _TStageBtn;
	private Button _TStatsBtn;
	private Button _TSaveBtn;
	private Button _TSettingBtn;
	private Label _TExp;
	private Label _TResult;
	private Button _TPushBtn;
	private Button _TNextBtn;
	public override void OnBind(Control control)
	{
		base.OnBind(control);
		_TBg = GameObject.FindChild("TBg") as Panel;
		_TCards = GameObject.FindChild("TCards") as Control;
		_TStacks = GameObject.FindChild("TStacks") as Control;
		_TFxLayer = GameObject.FindChild("TFxLayer") as Control;
		_TNavBar = GameObject.FindChild("TNavBar") as Panel;
		_TNavRow = GameObject.FindChild("TNavRow") as HBoxContainer;
		_TTitle = GameObject.FindChild("TTitle") as Label;
		_TInnBtn = GameObject.FindChild("TInnBtn") as Button;
		_TEquipBtn = GameObject.FindChild("TEquipBtn") as Button;
		_TSkillBtn = GameObject.FindChild("TSkillBtn") as Button;
		_TStageBtn = GameObject.FindChild("TStageBtn") as Button;
		_TStatsBtn = GameObject.FindChild("TStatsBtn") as Button;
		_TSaveBtn = GameObject.FindChild("TSaveBtn") as Button;
		_TSettingBtn = GameObject.FindChild("TSettingBtn") as Button;
		_TExp = GameObject.FindChild("TExp") as Label;
		_TResult = GameObject.FindChild("TResult") as Label;
		_TPushBtn = GameObject.FindChild("TPushBtn") as Button;
		_TNextBtn = GameObject.FindChild("TNextBtn") as Button;
	}
}
