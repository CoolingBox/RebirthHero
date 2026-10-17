#if TOOLS
using Godot;
using System.Collections.Generic;

/// <summary>
/// UI代码生成器。扫描 Resources/UI 下的 .tscn,生成面板的 C# partial 绑定代码。
/// 等价于原 GDScript 版 UICodeGenerator.gd,改为 C# 实现以便与项目同源、可在编辑器热重载下稳定工作。
/// 由 TinyToolsPlugin 的「UI代码生成」按钮调用。
/// </summary>
public static class UICodeGenerator
{
    private const string UiResPath = "res://Resources/UI/";
    private const string UiCodePath = "res://Scripts/Game/UI/";
    private const string UiCodeBindPath = "res://Scripts/Game/UI/Generate";

    public static void Generate()
    {
        Start();
        List<string> uiResFiles = CollectTscnFiles(UiResPath);
        GenerateUiCode(uiResFiles);
        GenerateUiCodeBind(uiResFiles);
        End();
    }

    private static List<string> CollectTscnFiles(string resDir)
    {
        List<string> files = new List<string>();
        using (DirAccess dir = DirAccess.Open(resDir))
        {
            if (dir == null)
                return files;
            dir.ListDirBegin();
            string fileName = dir.GetNext();
            while (!string.IsNullOrEmpty(fileName))
            {
                if (fileName.EndsWith(".tscn"))
                    files.Add(resDir + fileName);
                fileName = dir.GetNext();
            }
            dir.ListDirEnd();
        }
        return files;
    }

    /// <summary>
    /// 生成面板骨架代码(仅当文件不存在时),与 Bind 组成 partial class。
    /// </summary>
    private static void GenerateUiCode(List<string> uiResFiles)
    {
        foreach (string fullName in uiResFiles)
        {
            string name = fullName.GetFile().GetBaseName();
            GD.PrintRich("[color=Cyan]正在生成UI代码:[/color]" + name);
            string path = ProjectSettings.GlobalizePath(UiCodePath + name + ".cs");
            if (FileAccess.FileExists(path))
            {
                GD.PrintRich("[color=Cyan]" + name + ".cs 文件已存在,跳过[/color]");
                continue;
            }

            List<string> sb = new List<string>();
            sb.Add("using Godot;");
            sb.Add("using TinyFramework;");
            sb.Add("");
            sb.Add("public partial class " + name + ":BasePanel");
            sb.Add("{");
            sb.Add("\tpublic override void OnShow()");
            sb.Add("\t{");
            sb.Add("\t\tbase.OnShow();");
            sb.Add("\t}");
            sb.Add("");
            sb.Add("\tpublic override void OnHide()");
            sb.Add("\t{");
            sb.Add("\t\tbase.OnHide();");
            sb.Add("\t}");
            sb.Add("");
            sb.Add("}");

            WriteFile(path, sb);
        }
    }

    /// <summary>
    /// 生成面板绑定代码:声明以 T 开头的子节点字段,并在 OnBind 中查找赋值。
    /// </summary>
    private static void GenerateUiCodeBind(List<string> uiResFiles)
    {
        foreach (string fullName in uiResFiles)
        {
            string name = fullName.GetFile().GetBaseName();
            GD.PrintRich("[color=Cyan]正在生成UI绑定代码:[/color]" + name);
            string path = ProjectSettings.GlobalizePath(UiCodeBindPath + "/" + name + "Bind.cs");

            PackedScene packedScene = ResourceLoader.Load<PackedScene>(fullName);
            if (packedScene == null)
                continue;
            Node panel = packedScene.Instantiate();
            List<Node> filtered = FilterTNodes(GetAllChildren(panel));

            List<string> sb = new List<string>();
            sb.Add("using Godot;");
            sb.Add("using TinyFramework;");
            sb.Add("");
            sb.Add("public partial class " + name + ":BasePanel");
            sb.Add("{");
            foreach (Node child in filtered)
                sb.Add("\tprivate " + child.GetClass() + " _" + child.Name + ";");
            sb.Add("\tpublic override void OnBind(Control control)");
            sb.Add("\t{");
            sb.Add("\t\tbase.OnBind(control);");
            foreach (Node child in filtered)
                sb.Add("\t\t_" + child.Name + " = GameObject.FindChild(\"" + child.Name + "\") as " + child.GetClass() + ";");
            sb.Add("\t}");
            sb.Add("");
            sb.Add("}");

            WriteFile(path, sb);
            panel.QueueFree();
        }
    }

    private static List<Node> GetAllChildren(Node node)
    {
        List<Node> list = new List<Node>();
        GetAllChildrenRecursive(node, list);
        return list;
    }

    private static void GetAllChildrenRecursive(Node node, List<Node> list)
    {
        foreach (Node child in node.GetChildren())
        {
            list.Add(child);
            GetAllChildrenRecursive(child, list);
        }
    }

    private static List<Node> FilterTNodes(List<Node> nodes)
    {
        List<Node> filtered = new List<Node>();
        foreach (Node child in nodes)
        {
            string childName = child.Name.ToString();
            if (childName.Length > 0 && childName.StartsWith("T"))
                filtered.Add(child);
        }
        return filtered;
    }

    private static void WriteFile(string path, List<string> lines)
    {
        using (FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Write))
        {
            if (file != null)
                file.StoreString(string.Join("\n", lines));
        }
    }

    private static void Start()
    {
        GD.PrintRich("[color=Green]UI代码生成启动[/color]");
        EnsureDir(UiCodePath);
        EnsureDir(UiCodeBindPath);
    }

    private static void EnsureDir(string resDir)
    {
        string dir = resDir.EndsWith("/") ? resDir.Substring(0, resDir.Length - 1) : resDir;
        if (!DirAccess.DirExistsAbsolute(ProjectSettings.GlobalizePath(dir)))
        {
            DirAccess.Open("res://")?.MakeDirRecursive(dir);
        }
    }

    private static void End()
    {
        GD.PrintRich("[color=Green]UI代码生成完毕[/color]");
    }
}
#endif
