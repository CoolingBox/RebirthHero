using Godot;
using TinyFramework;

public partial class BattlePanel : BasePanel
{
	private Panel _TBg;
	private Control _TCards;
	private Control _TStacks;
	private Control _TFxLayer;
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
		_TResult = GameObject.FindChild("TResult") as Label;
		_TPushBtn = GameObject.FindChild("TPushBtn") as Button;
		_TNextBtn = GameObject.FindChild("TNextBtn") as Button;
	}
}
