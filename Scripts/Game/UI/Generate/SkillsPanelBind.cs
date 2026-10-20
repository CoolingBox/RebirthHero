using Godot;
using TinyFramework;

public partial class SkillsPanel : BasePanel
{
	private Panel _TBg;
	private Label _THeading;
	private Button _TCloseBtn;
	private Label _THint;
	private VBoxContainer _TContent;
	private GridContainer _TPool;
	private Label _TBottomHint;
	public override void OnBind(Control control)
	{
		base.OnBind(control);
		_TBg = GameObject.FindChild("TBg") as Panel;
		_THeading = GameObject.FindChild("THeading") as Label;
		_TCloseBtn = GameObject.FindChild("TCloseBtn") as Button;
		_THint = GameObject.FindChild("THint") as Label;
		_TContent = GameObject.FindChild("TContent") as VBoxContainer;
		_TPool = GameObject.FindChild("TPool") as GridContainer;
		_TBottomHint = GameObject.FindChild("TBottomHint") as Label;
	}
}
