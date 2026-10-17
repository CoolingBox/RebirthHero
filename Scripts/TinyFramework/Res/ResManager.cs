// 文件：ResManager.cs
// 作者：急冻雪柜
// 描述：
// 日期：2025/12/17 14:36

using System.Threading.Tasks;
using Godot;

namespace TinyFramework;

public class ResManager : Singleton<ResManager>
{
    /// <summary>
    /// 加载资源。不在用户层做静态缓存,避免静态字段持有 GodotObject 引用导致编辑器热重载时程序集无法卸载(issue 78513)。
    /// ResourceLoader 内部已按路径缓存资源,重复加载无额外开销。
    /// </summary>
    /// <typeparam name="T">资源类型</typeparam>
    /// <param name="path">资源路径</param>
    /// <returns>加载的资源</returns>
    public static T Load<T>(string path) where T : GodotObject
    {
        var res = ResourceLoader.Load(path);
        if (res == null)
        {
            GD.PrintErr($"[ResManager] 加载资源失败: {path}");
            return null;
        }

        return res as T;
    }

    /// <summary>
    /// 异步加载资源(后台线程加载,不阻塞主线程)。需运行时 GameRoot 已就绪以获取 SceneTree 轮询进度。
    /// </summary>
    public static async Task<T> LoadAsync<T>(string path) where T : GodotObject
    {
        if (GameRoot.Instance == null)
        {
            GD.PrintErr($"[ResManager] 异步加载失败(尚未运行时): {path}");
            return null;
        }
        if (!ResourceLoader.Exists(path))
        {
            GD.PrintErr($"[ResManager] 资源不存在: {path}");
            return null;
        }

        if (ResourceLoader.LoadThreadedRequest(path) != Error.Ok)
        {
            GD.PrintErr($"[ResManager] 异步加载请求失败: {path}");
            return null;
        }

        var tree = GameRoot.Instance.GetTree();
        while (true)
        {
            var status = ResourceLoader.LoadThreadedGetStatus(path);
            if (status == ResourceLoader.ThreadLoadStatus.Loaded)
                return ResourceLoader.LoadThreadedGet(path) as T;
            if (status == ResourceLoader.ThreadLoadStatus.Failed)
            {
                GD.PrintErr($"[ResManager] 异步加载失败: {path}");
                return null;
            }
            await tree.ToSignal(tree, "process_frame");
        }
    }
}