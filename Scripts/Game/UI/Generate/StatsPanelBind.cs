using Godot;
using TinyFramework;

public partial class StatsPanel : BasePanel
{
	private Panel _TBg;
	private Label _THeader;
	private Button _TViewBtn;
	private Button _TSortBtn;
	private VBoxContainer _TBody;
	public override void OnBind(Control control)
	{
		base.OnBind(control);
		_TBg = GameObject.FindChild("TBg") as Panel;
		_THeader = GameObject.FindChild("THeader") as Label;
		_TViewBtn = GameObject.FindChild("TViewBtn") as Button;
		_TSortBtn = GameObject.FindChild("TSortBtn") as Button;
		_TBody = GameObject.FindChild("TBody") as VBoxContainer;
	}
}
