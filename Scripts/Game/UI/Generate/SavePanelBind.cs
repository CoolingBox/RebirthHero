using Godot;
using TinyFramework;

public partial class SavePanel : BasePanel
{
	private Panel _TBg;
	private Label _THeading;
	private Button _TCloseBtn;
	private Label _THint;
	private Label _TStatus;
	private VBoxContainer _TContent;
	private Label _TSecMain;
	private HBoxContainer _TRowMain;
	private CheckButton _TChkAuto;
	private Label _TNoteAuto;
	private Button _TBtnSaveNow;
	private Label _TSecBackup;
	private HBoxContainer _TRowBackup;
	private Label _TLabelName;
	private LineEdit _TNameEdit;
	private Button _TBtnBackup;
	private Button _TBtnImport;
	private Label _TSecLoad;
	private ScrollContainer _TScroll;
	private VBoxContainer _TList;
	public override void OnBind(Control control)
	{
		base.OnBind(control);
		_TBg = GameObject.FindChild("TBg") as Panel;
		_THeading = GameObject.FindChild("THeading") as Label;
		_TCloseBtn = GameObject.FindChild("TCloseBtn") as Button;
		_THint = GameObject.FindChild("THint") as Label;
		_TStatus = GameObject.FindChild("TStatus") as Label;
		_TContent = GameObject.FindChild("TContent") as VBoxContainer;
		_TSecMain = GameObject.FindChild("TSecMain") as Label;
		_TRowMain = GameObject.FindChild("TRowMain") as HBoxContainer;
		_TChkAuto = GameObject.FindChild("TChkAuto") as CheckButton;
		_TNoteAuto = GameObject.FindChild("TNoteAuto") as Label;
		_TBtnSaveNow = GameObject.FindChild("TBtnSaveNow") as Button;
		_TSecBackup = GameObject.FindChild("TSecBackup") as Label;
		_TRowBackup = GameObject.FindChild("TRowBackup") as HBoxContainer;
		_TLabelName = GameObject.FindChild("TLabelName") as Label;
		_TNameEdit = GameObject.FindChild("TNameEdit") as LineEdit;
		_TBtnBackup = GameObject.FindChild("TBtnBackup") as Button;
		_TBtnImport = GameObject.FindChild("TBtnImport") as Button;
		_TSecLoad = GameObject.FindChild("TSecLoad") as Label;
		_TScroll = GameObject.FindChild("TScroll") as ScrollContainer;
		_TList = GameObject.FindChild("TList") as VBoxContainer;
	}
}
