// 归档数据类型。移植自 core/types.py（对齐 ucatpaqtypes.pas）。
namespace Catpaq.Core;

/// <summary>归档类型（从文件头魔数识别）。</summary>
public enum ArchiveType
{
    Unknown = 0,      // 无法读取或空文件
    ZpaqPlain = 1,    // magic "7kSt" -> 无加密
    ZpaqAes = 2,      // magic (随机盐) -> 标准 AES-256
    Franzen = 3,      // magic "FRANZEN" + 0x1A -> 仅 Franzen 混淆
    AesFranzen = 4,   // magic "FRENZEN" + 0x1A -> AES + Franzen
}

/// <summary>文件的某个版本（pakkalist 协议中的 -N 项）。</summary>
public sealed class FileVersion
{
    public int Version { get; set; }
    public string DateStr { get; set; } = "";
    public long Size { get; set; }
    public bool IsDeleted { get; set; }
}

/// <summary>唯一文件（名 + 路径）及其版本历史。</summary>
public sealed class ArchiveFileEntry
{
    public string FileName { get; set; } = "";
    public List<FileVersion> Versions { get; } = new();
}

/// <summary>归档的全局版本（一次事务，'|' 开头的行）。</summary>
public sealed class ArchiveVersion
{
    public int Number { get; set; }
    public string DateStr { get; set; } = "";
}

/// <summary>解析 pakkalist 的完整结果。</summary>
public sealed class ArchiveData
{
    public List<ArchiveVersion> GlobalVersions { get; } = new();
    public List<ArchiveFileEntry> Files { get; } = new();
    public int TotalLines { get; set; }
}

public static class ArchiveHelpers
{
    /// <summary>读取文件头魔数识别归档类型。</summary>
    public static ArchiveType DetectArchiveType(string fileName)
    {
        try
        {
            if (string.IsNullOrEmpty(fileName) || !File.Exists(fileName))
                return ArchiveType.Unknown;
            using var fs = File.OpenRead(fileName);
            var head = new byte[16];
            int n = fs.Read(head, 0, head.Length);
            if (n < 8)
                return ArchiveType.Unknown;
            // "7kSt" -> plain
            if (head[0] == (byte)'7' && head[1] == (byte)'k' &&
                head[2] == (byte)'S' && head[3] == (byte)'t')
                return ArchiveType.ZpaqPlain;
            // "FRANZEN" + 0x1A
            if (n >= 8 &&
                head[0] == (byte)'F' && head[1] == (byte)'R' &&
                head[2] == (byte)'A' && head[3] == (byte)'N' &&
                head[4] == (byte)'Z' && head[5] == (byte)'E' &&
                head[6] == (byte)'N' && head[7] == 0x1A)
                return ArchiveType.Franzen;
            // "FRENZEN" + 0x1A
            if (n >= 8 &&
                head[0] == (byte)'F' && head[1] == (byte)'R' &&
                head[2] == (byte)'E' && head[3] == (byte)'N' &&
                head[4] == (byte)'Z' && head[5] == (byte)'E' &&
                head[6] == (byte)'N' && head[7] == 0x1A)
                return ArchiveType.AesFranzen;
            // 其余（随机盐）-> AES
            return ArchiveType.ZpaqAes;
        }
        catch
        {
            return ArchiveType.Unknown;
        }
    }

    public static string ArchiveTypeToStr(ArchiveType t) => t switch
    {
        ArchiveType.ZpaqPlain => "ZPAQ (plain)",
        ArchiveType.ZpaqAes => "ZPAQ (AES-256)",
        ArchiveType.Franzen => "ZPAQ (Franzen)",
        ArchiveType.AesFranzen => "ZPAQ (AES + Franzen)",
        _ => "Unknown",
    };

    /// <summary>人类可读文件大小（对齐 format_file_size）。</summary>
    public static string FormatFileSize(long size)
    {
        if (size < 0)
            return "-";
        if (size < 1024)
            return $"{size} B";
        double d = size;
        string[] units = { "KB", "MB", "GB", "TB", "PB" };
        int i = -1;
        while (d >= 1024 && i < units.Length - 1)
        {
            d /= 1024;
            i++;
        }
        string s = d >= 100 ? d.ToString("0") : d.ToString("0.0");
        return $"{s} {units[i]}";
    }
}
