// Copyright (c) Files Community. Licensed under the MIT License.
// 移植 Files 4.2.3 的 FolderHelpers（用 System.IO 简化实现 Win32 FindFirstFile 枚举）。
using System.IO;

namespace Catpaq.Core;

public readonly record struct SubfolderEntry(string Path, string Name, bool HasSubfolders, bool IsHidden);

public static class FolderHelpers
{
	public static List<SubfolderEntry> EnumerateSubfolders(string path, bool showHidden, bool showProtected, bool showDot, int limit = 1000)
	{
		var results = new List<SubfolderEntry>();
		try
		{
			var dirInfo = new DirectoryInfo(path);
			foreach (var d in dirInfo.EnumerateDirectories())
			{
				var isHidden = (d.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden;
				var isSystem = (d.Attributes & FileAttributes.System) == FileAttributes.System;

				if (!showDot && d.Name.StartsWith('.'))
					continue;
				if (isHidden && !showHidden)
					continue;
				if (isHidden && isSystem && !showProtected)
					continue;

				results.Add(new SubfolderEntry(d.FullName, d.Name, HasSubfolders(d.FullName), isHidden));

				if (results.Count == limit)
					break;
			}
		}
		catch
		{
			// 无权限/不存在时返回已枚举的部分
		}

		results.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
		return results;
	}

	public static bool HasSubfolders(string path)
	{
		try
		{
			return new DirectoryInfo(path).EnumerateDirectories().Any();
		}
		catch
		{
			return false;
		}
	}
}
