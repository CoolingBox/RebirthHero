// 文件：BasePanel.cs
// 作者：急冻雪柜
// 描述：UI面板基类
// 日期：2025/11/15 1:56

using Godot;

namespace TinyFramework;



public partial class BasePanel
{
    public Control GameObject { get; private set; }

    public virtual void OnBind(Control control)
    {
        GameObject = control;
        
    }
    public virtual void OnShow()
    {
        GameObject.Show();
    }
    
    public virtual void OnHide()
    {
        GameObject.Hide();
    }
    
    public virtual void OnClose()
    {
        
    }
    
    protected void ShowPanel<T>(string panelName=null) where T : BasePanel, new()
    {
        UIManager.ShowPanel<T>(panelName);
    }
    
    protected void HidePanel<T>(string panelName=null) where T : BasePanel
    {
        UIManager.HidePanel<T>(panelName);
    }
    protected void ClosePanel<T>(string panelName=null) where T : BasePanel
    {
        UIManager.ClosePanel<T>(panelName);
    }
}