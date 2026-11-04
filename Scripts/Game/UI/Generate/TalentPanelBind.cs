using Godot;
using TinyFramework;

public partial class TalentPanel : BasePanel
{
	private Panel _TBg;
	private Label _TTitle;
	private Button _TTree1Btn;
	private Button _TTree2Btn;
	private Button _TCenterBtn;
	private Button _TResetBtn;
	private Button _TCloseBtn;
	private Control _TView;
	private Control _TTree;
	private Label _TInfo;
	private Panel _TTip;
	private Label _TTipLabel;
	private Label _THint;
	public override void OnBind(Control control)
	{
		base.OnBind(control);
		_TBg = GameObject.FindChild("TBg") as Panel;
		_TTitle = GameObject.FindChild("TTitle") as Label;
		_TTree1Btn = GameObject.FindChild("TTree1Btn") as Button;
		_TTree2Btn = GameObject.FindChild("TTree2Btn") as Button;
		_TCenterBtn = GameObject.FindChild("TCenterBtn") as Button;
		_TResetBtn = GameObject.FindChild("TResetBtn") as Button;
		_TCloseBtn = GameObject.FindChild("TCloseBtn") as Button;
		_TView = GameObject.FindChild("TView") as Control;
		// ⚠️ FindChild 的 pattern 是**节点名通配符**，不是路径 —— 写 "TView/TTree" 永远找不到。
		//    必须用名字 + recursive=true。这也是"天赋树看不见"的根因：_TTree 为 null 时
		//    BuildTree 直接 return，画布停在原点，节点全在视口外。
		_TTree = GameObject.FindChild("TTree", true, false) as Control;
		_TInfo = GameObject.FindChild("TInfo") as Label;
		_TTip = GameObject.FindChild("TTip") as Panel;
		_TTipLabel = GameObject.FindChild("TTipLabel") as Label;
		_THint = GameObject.FindChild("THint") as Label;
	}
}
