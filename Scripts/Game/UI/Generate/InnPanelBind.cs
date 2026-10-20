using Godot;
using TinyFramework;

public partial class InnPanel : BasePanel
{
	private Panel _TBg;
	private Label _TTitle;
	private Label _TSilver;
	private Label _THint;
	private Button _TRefreshBtn;
	private Button _TCloseBtn;
	private HBoxContainer _TSlotRow;
	private VBoxContainer _TFormationBox;
	private Label _TBottomHint;
	public override void OnBind(Control control)
	{
		base.OnBind(control);
		_TBg = GameObject.FindChild("TBg") as Panel;
		_TTitle = GameObject.FindChild("TTitle") as Label;
		_TSilver = GameObject.FindChild("TSilver") as Label;
		_THint = GameObject.FindChild("THint") as Label;
		_TRefreshBtn = GameObject.FindChild("TRefreshBtn") as Button;
		_TCloseBtn = GameObject.FindChild("TCloseBtn") as Button;
		_TSlotRow = GameObject.FindChild("TSlotRow") as HBoxContainer;
		_TFormationBox = GameObject.FindChild("TFormationBox") as VBoxContainer;
		_TBottomHint = GameObject.FindChild("TBottomHint") as Label;
	}
}
