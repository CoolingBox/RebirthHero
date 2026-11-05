using Godot;
using TinyFramework;

public partial class MainPanel : BasePanel
{
	private Panel _TNavBar;
	private HBoxContainer _TNavRow;
	private Label _TTitle;
	private Button _TTalentBtn;
	private Button _TGalleryBtn;
	private Button _TInnBtn;
	private Button _TEquipBtn;
	private Button _TSkillBtn;
	private Button _TStageBtn;
	private Button _TStatsBtn;
	private Button _TSaveBtn;
	private Button _TSettingBtn;
	private Label _TExp;
	public override void OnBind(Control control)
	{
		base.OnBind(control);
		_TNavBar = GameObject.FindChild("TNavBar") as Panel;
		_TNavRow = GameObject.FindChild("TNavRow") as HBoxContainer;
		_TTitle = GameObject.FindChild("TTitle") as Label;
		_TTalentBtn = GameObject.FindChild("TTalentBtn") as Button;
		_TGalleryBtn = GameObject.FindChild("TGalleryBtn") as Button;
		_TInnBtn = GameObject.FindChild("TInnBtn") as Button;
		_TEquipBtn = GameObject.FindChild("TEquipBtn") as Button;
		_TSkillBtn = GameObject.FindChild("TSkillBtn") as Button;
		_TStageBtn = GameObject.FindChild("TStageBtn") as Button;
		_TStatsBtn = GameObject.FindChild("TStatsBtn") as Button;
		_TSaveBtn = GameObject.FindChild("TSaveBtn") as Button;
		_TSettingBtn = GameObject.FindChild("TSettingBtn") as Button;
		_TExp = GameObject.FindChild("TExp") as Label;
	}
}
