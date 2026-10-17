// 文件：TinyExtension.cs
// 作者：急冻雪柜
// 描述：
// 日期：2025/11/15 3:26

using System.Collections.Generic;
using Godot;

namespace TinyFramework;

public static class TinyExtension
{
    /// <summary>
    /// 获取所有子孙节点(迭代实现,避免深树递归导致的栈/分配开销)
    /// </summary>
    public static List<Node> GetAllChildren(this Node node)
    {
        List<Node> list = new List<Node>();
        if (node == null)
            return list;

        var stack = new Stack<Node>();
        foreach (Node child in node.GetChildren())
            stack.Push(child);

        while (stack.Count > 0)
        {
            Node current = stack.Pop();
            list.Add(current);
            foreach (Node child in current.GetChildren())
                stack.Push(child);
        }

        return list;
    }
    
}