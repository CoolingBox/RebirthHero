using Godot;
using TinyFramework;

public partial class GalleryPanel : BasePanel
{
	private Label _TTitle;
	private Button _TCloseBtn;
	private Control _TCardHolder;
	private GridContainer _TGrid;
	private Label _TInfo;
	private Label _THint;

	public override void OnBind(Control control)
	{
		base.OnBind(control);
		// ⚠️ FindChild 的 pattern 是**节点名**，不是路径（写 "A/B" 永远找不到）
		_TTitle = GameObject.FindChild("TTitle") as Label;
		_TCloseBtn = GameObject.FindChild("TCloseBtn") as Button;
		_TCardHolder = GameObject.FindChild("TCardHolder") as Control;
		_TGrid = GameObject.FindChild("TGrid") as GridContainer;
		_TInfo = GameObject.FindChild("TInfo") as Label;
		_THint = GameObject.FindChild("THint") as Label;
	}
}
