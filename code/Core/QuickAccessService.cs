// Copyright (c) Files Community. Licensed under the MIT License.
// 系统快速访问（frequent places）读取。
// 照抄 Files 4.2.3：
//   - QuickAccessService.GetPinnedFoldersAsync
//   - Win32Helper.GetShellFolderAsync（删掉控制面板特判等用不到部分）
//   - ShellFolderExtensions.GetShellFileItem / GetShellItemFromPathOrPIDL（删掉库/回收站映射、ShellLink 分支）
//   - ShellFileItem 数据模型
//   - STATask（STA 线程执行 + OleInitialize，照抄 Files 的稳健性）
//   - SafetyExtensions.IgnoreExceptions（属性读取安全检查）
using System.IO;
using Vanara.PInvoke;
using Vanara.Windows.Shell;

namespace Catpaq.Core;

/// <summary>快速访问中的一个钉住文件夹。</summary>
public sealed class PinnedFolder
{
    public required string Name { get; init; }
    public required string Path { get; init; }
}

/// <summary>Shell 枚举结果项（照抄 Files 的 ShellFileItem，仅保留用得到的字段）。</summary>
public sealed class ShellFileItem
{
    public bool IsFolder { get; set; }
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public Dictionary<string, object?> Properties { get; set; } = new();
}

public static class QuickAccessService
{
    // frequent places 壳文件夹（含快速访问钉住的文件夹）。
    // 注意：不用 quick access 本体（::{679f85cb-...}），因它包含 recent files（Files 源码注释）。
    private readonly static string guid = "::{3936e9e4-d92c-4eee-a85a-bc16d5ea0819}";

    /// <summary>
    /// 枚举系统快速访问中的钉住文件夹。
    /// 照抄 Files：GetPinnedFoldersAsync → GetShellFolderAsync(guid, false, true, 0, int.MaxValue, "System.Home.IsPinned")
    /// → 过滤 IsFolder；调用方再过滤 System.Home.IsPinned（Files 在 PinnedFoldersManager.UpdateItemsWithExplorerAsync 里做）。
    /// 失败返回空列表，不抛异常。
    /// </summary>
    public static async Task<IReadOnlyList<PinnedFolder>> GetPinnedFoldersAsync()
    {
        var items = (await GetShellFolderAsync(guid, false, true, 0, int.MaxValue, "System.Home.IsPinned")).Enumerate;
        return items
            .Where(link => link.IsFolder)
            .Where(link => (bool?)link.Properties["System.Home.IsPinned"] ?? false)  // 照抄 PinnedFoldersManager 的过滤
            .Where(link => !string.IsNullOrEmpty(link.FilePath))
            .Select(link => new PinnedFolder
            {
                Name = !string.IsNullOrEmpty(link.FileName) ? link.FileName : Path.GetFileName(link.FilePath.TrimEnd('\\')),
                Path = link.FilePath,
            })
            .ToList();
    }

    /// <summary>照抄 Files 的 Win32Helper.GetShellFolderAsync（删除控制面板特判/ShellLink 分支等用不到部分）。</summary>
    public static async Task<(ShellFileItem? Folder, List<ShellFileItem> Enumerate)> GetShellFolderAsync(
        string path, bool getFolder, bool getEnumerate, int from, int count, params string[] properties)
    {
        if (path.StartsWith("::{", StringComparison.Ordinal))
            path = $"shell:{path}";

        return await StatTask.Run(() =>
        {
            var flc = new List<ShellFileItem>();
            ShellFileItem? folder = null;

            try
            {
                using var shellFolder = GetShellItemFromPathOrPIDL(path) as ShellFolder;
                if (shellFolder is null)
                    return (folder, flc);

                if (getFolder)
                    folder = GetShellFileItem(shellFolder);

                if (getEnumerate)
                {
                    foreach (var folderItem in shellFolder.Skip(from).Take(count))
                    {
                        try
                        {
                            var shellFileItem = GetShellFileItem(folderItem);
                            if (shellFileItem is null)
                                continue;

                            foreach (var prop in properties)
                                shellFileItem.Properties[prop] = IgnoreExceptions(() => folderItem.Properties[prop]);

                            flc.Add(shellFileItem);
                        }
                        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
                        {
                            // Files 注释：项正在被删除时会发生，跳过
                        }
                        finally
                        {
                            folderItem.Dispose();
                        }
                    }
                }
            }
            catch
            {
                // Files 同样静默吞掉
            }

            return (folder, flc);
        });
    }

    // 照抄 Files 的 ShellFolderExtensions.GetShellItemFromPathOrPIDL（我们的场景不涉及 \\SHELL\ PIDL 编码，直接打开）
    private static ShellItem GetShellItemFromPathOrPIDL(string pathOrPIDL) => ShellItem.Open(pathOrPIDL);

    // 照抄 Files 的 ShellFolderExtensions.GetShellFileItem（删除库/回收站映射、ShellLink、时间/大小等用不到字段）
    private static ShellFileItem? GetShellFileItem(ShellItem folderItem)
    {
        if (folderItem is null)
            return null;

        // Files 注释：不要用 folderItem 的 Attributes 属性，部分 shell 文件夹未实现。
        // Zip 归档也是 shell 文件夹，用 STREAM 属性区分真实文件夹。
        bool isFolder = folderItem.IsFolder && folderItem.IShellItem?.GetAttributes(Shell32.SFGAO.SFGAO_STREAM) is 0;
        var parsingPath = folderItem.GetDisplayName(ShellItemDisplayString.DesktopAbsoluteParsing);
        parsingPath ??= folderItem.FileSystemPath;

        var fileName = IgnoreExceptions(() => folderItem.Properties.TryGetValue<string>(Ole32.PROPERTYKEY.System.ItemNameDisplay, out var v) ? v : null);
        fileName ??= Path.GetFileName(folderItem.Name);  // 原始文件名
        fileName ??= folderItem.GetDisplayName(ShellItemDisplayString.ParentRelativeParsing);
        fileName ??= parsingPath;

        // 导航用真实文件系统路径优先，其次是 shell 解析路径
        string filePath = folderItem.FileSystemPath ?? parsingPath ?? "";
        fileName ??= string.IsNullOrEmpty(filePath) ? folderItem.Name : filePath;
        fileName ??= "";

        return new ShellFileItem
        {
            IsFolder = isFolder,
            FilePath = filePath,
            FileName = fileName,
        };
    }

    private static T? IgnoreExceptions<T>(Func<T?> action)
    {
        try { return action(); }
        catch { return default; }
    }
}

// 照抄 Files 的 STATask（剥掉 ILogger 依赖）：Shell COM 枚举必须在 STA 线程上进行。
// 注意：Files 用的是 CsWin32 的 PInvoke.OleInitialize()（失败返回 HRESULT 不抛）；
// Vanara 的 Ole32.OleInitialize() 失败会抛异常，故放在 try 内并用 TrySetResult 保证任务必完成。
internal static class StatTask
{
    public static Task<T> Run<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>();

        var thread = new Thread(() =>
        {
            try
            {
                Ole32.OleInitialize();
                try
                {
                    tcs.TrySetResult(func());
                }
                catch
                {
                    tcs.TrySetResult(default!);
                }
                finally
                {
                    Ole32.OleUninitialize();
                }
            }
            catch
            {
                tcs.TrySetResult(default!);
            }
        })
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return tcs.Task;
    }
}
