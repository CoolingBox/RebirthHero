using Godot;
using TinyFramework;

public partial class EquipPanel : BasePanel
{
	private Panel _TBg;
	private Label _THeading;
	private Button _TCloseBtn;
	private Label _THint;
	private VBoxContainer _TContent;
	private VBoxContainer _TSlotBox;
	private HBoxContainer _TFilterRow;
	private HBoxContainer _TSortRow;
	private ScrollContainer _TBagScroll;
	private VBoxContainer _TBagList;
	public override void OnBind(Control control)
	{
		base.OnBind(control);
		_TBg = GameObject.FindChild("TBg") as Panel;
		_THeading = GameObject.FindChild("THeading") as Label;
		_TCloseBtn = GameObject.FindChild("TCloseBtn") as Button;
		_THint = GameObject.FindChild("THint") as Label;
		_TContent = GameObject.FindChild("TContent") as VBoxContainer;
		_TSlotBox = GameObject.FindChild("TSlotBox") as VBoxContainer;
		_TFilterRow = GameObject.FindChild("TFilterRow") as HBoxContainer;
		_TSortRow = GameObject.FindChild("TSortRow") as HBoxContainer;
		_TBagScroll = GameObject.FindChild("TBagScroll") as ScrollContainer;
		_TBagList = GameObject.FindChild("TBagList") as VBoxContainer;
	}
}
