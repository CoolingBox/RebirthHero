using Godot;
using TinyFramework;

public partial class BattlePanel : BasePanel
{
	private ColorRect _TBg;
	private Control _TCards;
	private Control _TStacks;
	private Control _TFxLayer;
	private Label _TTitle;
	private Label _TExp;
	private Label _TResult;
	private Button _TPushBtn;
	private Button _TNextBtn;
	private Button _TInnBtn;
	private Button _TStatsBtn;
	private Control _TStatsPanel;
	public override void OnBind(Control control)
	{
		base.OnBind(control);
		_TBg = GameObject.FindChild("TBg") as ColorRect;
		_TCards = GameObject.FindChild("TCards") as Control;
		_TStacks = GameObject.FindChild("TStacks") as Control;
		_TFxLayer = GameObject.FindChild("TFxLayer") as Control;
		_TTitle = GameObject.FindChild("TTitle") as Label;
		_TExp = GameObject.FindChild("TExp") as Label;
		_TResult = GameObject.FindChild("TResult") as Label;
		_TPushBtn = GameObject.FindChild("TPushBtn") as Button;
		_TNextBtn = GameObject.FindChild("TNextBtn") as Button;
		_TInnBtn = GameObject.FindChild("TInnBtn") as Button;
		_TStatsBtn = GameObject.FindChild("TStatsBtn") as Button;
		_TStatsPanel = GameObject.FindChild("TStatsPanel") as Control;
	}
}
