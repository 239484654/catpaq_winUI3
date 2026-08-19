// PakkaListParser: 从临时文件流式解析 pakkalist。
// 移植自 core/zpaq_bridge.py 的 parse_pakkalist_from_file（对齐 Pascal ParsePakkaListFromFile）。
using System.Text;

namespace Catpaq.Core;

public static class PakkaListParser
{
    /// <summary>从磁盘临时文件流式解析。逐行读取以支持百万行级归档。</summary>
    public static ArchiveData ParseFromFile(string tempFile)
    {
        var result = new ArchiveData();
        var files = new List<ArchiveFileEntry>();
        string lastFileName = "";

        if (!File.Exists(tempFile))
            return result;

        var pending = new Queue<string>();

        string? GetNextValidLine(StreamReader fh)
        {
            // 先消费 pending
            while (pending.Count > 0)
            {
                var outLine = pending.Dequeue();
                if (outLine.Trim() == "") continue;
                if (outLine.StartsWith("$$$NULL-W")) continue;
                return outLine;
            }
            // 再从文件读
            while (true)
            {
                var raw = fh.ReadLine();
                if (raw == null) return null;
                var outLine = raw.TrimEnd('\r');
                if (outLine.Trim() == "") continue;
                if (outLine.StartsWith("$$$NULL-W")) continue;
                if (outLine.Length > 0 && outLine[0] == '!') continue;
                if (outLine.StartsWith("@SPK@")) continue;
                if (outLine.Length >= 6 && outLine[..2] == "W " && outLine[5] == '%') continue;
                // 过滤时间戳行 "DD/MM/YYYY ... W ..."
                if (outLine.Length > 20 && outLine[2] == '/' && outLine[5] == '/'
                    && outLine.IndexOf(" W ", StringComparison.Ordinal) > 10)
                    continue;
                return outLine;
            }
        }

        try
        {
            using var fh = new StreamReader(tempFile, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var currentLine = GetNextValidLine(fh);
            while (currentLine is not null)
            {
                if (currentLine.EndsWith('\r'))
                    currentLine = currentLine[..^1];

                if (currentLine.Length > 0 && currentLine[0] == '|')
                {
                    var dateStr = currentLine[1..].Trim();
                    var firstToken = dateStr.Split(' ')[0];
                    result.GlobalVersions.Add(new ArchiveVersion
                    {
                        Number = ToInt(firstToken),
                        DateStr = dateStr,
                    });
                    currentLine = GetNextValidLine(fh);
                    continue;
                }

                if (currentLine.Length > 0 && currentLine[0] == '+')
                {
                    result.TotalLines = ToInt(currentLine[1..]);
                    currentLine = GetNextValidLine(fh);
                    continue;
                }

                if (currentLine.Length > 0 && currentLine[0] == '-')
                {
                    int verNum = ToInt(currentLine[1..]);
                    var dateLine = GetNextValidLine(fh);
                    if (dateLine is null) break;
                    bool isDeleted = dateLine.Trim() == "D";
                    if (!isDeleted && dateLine.Trim().Length > 30)
                    {
                        currentLine = GetNextValidLine(fh);
                        continue;
                    }
                    var sizeLine = GetNextValidLine(fh);
                    if (sizeLine is null) break;
                    var sizeClean = sizeLine.Trim().Replace(".", "");
                    long sizeVal = ToInt64(sizeClean);
                    if (sizeClean != "0" && sizeVal == 0 && sizeClean.Length > 0)
                    {
                        currentLine = GetNextValidLine(fh);
                        continue;
                    }
                    var nameLine = GetNextValidLine(fh);
                    if (nameLine is null) break;
                    if (nameLine == "?")
                        nameLine = lastFileName != "" ? lastFileName : "UNKNOWN_FILE_ERROR";
                    else
                        lastFileName = nameLine;

                    ArchiveFileEntry entry;
                    if (files.Count > 0 && files[^1].FileName == nameLine)
                        entry = files[^1];
                    else
                    {
                        entry = new ArchiveFileEntry { FileName = nameLine };
                        files.Add(entry);
                    }
                    entry.Versions.Add(new FileVersion
                    {
                        Version = verNum,
                        IsDeleted = isDeleted,
                        Size = sizeVal,
                        DateStr = isDeleted ? "DELETED" : dateLine,
                    });
                    currentLine = GetNextValidLine(fh);
                    continue;
                }

                // Scan/遥测行：静默忽略
                if (currentLine.Length >= 5 && currentLine[..5] == "Scan ")
                {
                    currentLine = GetNextValidLine(fh);
                    continue;
                }
                if (currentLine.Length > 1 && currentLine[0] == '@'
                    && currentLine.IndexOf('@', 1) > 1)
                {
                    currentLine = GetNextValidLine(fh);
                    continue;
                }
                currentLine = GetNextValidLine(fh);
            }
        }
        catch (IOException)
        {
            return result;
        }

        foreach (var f in files)
            result.Files.Add(f);
        return result;
    }

    private static int ToInt(string s, int def = 0)
    {
        s = s.Trim();
        return int.TryParse(s, out var v) ? v : def;
    }

    private static long ToInt64(string s, long def = 0)
    {
        s = s.Trim();
        return long.TryParse(s, out var v) ? v : def;
    }
}

/// <summary>
/// 流式解析 pakkalist：zpaqfranz 边输出边喂入 FeedLine（后台线程），
/// 进程结束时数据已就绪，省去 temp 文件全量写入 + 全量解析（对齐"由专门程序提交"的思路）。
/// 状态机与 ParseFromFile 的行格式/过滤规则完全一致。
/// </summary>
public sealed class PakkaListStreamParser
{
    private enum Stage { Start, ExpectDate, ExpectSize, ExpectName }

    private readonly ArchiveData _result = new();
    private readonly List<ArchiveFileEntry> _files = new();
    private string _lastFileName = "";
    private Stage _stage = Stage.Start;
    private int _curVer;
    private string _curDate = "";
    private long _curSize;

    /// <summary>喂入一行（后台线程调用；与 ParseFromFile 的过滤/解析规则一致）。</summary>
    public void FeedLine(string raw)
    {
        var line = raw.TrimEnd('\r');
        if (line.Trim() == "") return;
        if (line.StartsWith("$$$NULL-W", StringComparison.Ordinal)) return;
        if (line.Length > 0 && line[0] == '!') return;
        if (line.StartsWith("@SPK@", StringComparison.Ordinal)) return;
        if (line.Length >= 6 && line[..2] == "W " && line[5] == '%') return;
        // 过滤时间戳行 "DD/MM/YYYY ... W ..."
        if (line.Length > 20 && line[2] == '/' && line[5] == '/'
            && line.IndexOf(" W ", StringComparison.Ordinal) > 10) return;
        if (line.Length >= 5 && line[..5] == "Scan ") return;

        switch (_stage)
        {
            case Stage.Start:
                if (line.Length > 0 && line[0] == '|')
                {
                    var dateStr = line[1..].Trim();
                    var firstToken = dateStr.Split(' ')[0];
                    _result.GlobalVersions.Add(new ArchiveVersion { Number = ToInt(firstToken), DateStr = dateStr });
                }
                else if (line.Length > 0 && line[0] == '+')
                {
                    _result.TotalLines = ToInt(line[1..]);
                }
                else if (line.Length > 0 && line[0] == '-')
                {
                    _curVer = ToInt(line[1..]);
                    _stage = Stage.ExpectDate;
                }
                break;

            case Stage.ExpectDate:
                var dateLine = line.Trim();
                if (dateLine == "D")
                {
                    _curDate = "DELETED";
                    _stage = Stage.ExpectSize;
                }
                else if (dateLine.Length > 30)
                {
                    _stage = Stage.Start; // 异常长行：跳过该版本
                }
                else
                {
                    _curDate = dateLine;
                    _stage = Stage.ExpectSize;
                }
                break;

            case Stage.ExpectSize:
                var sizeClean = line.Trim().Replace(".", "");
                var sizeVal = ToInt64(sizeClean);
                if (sizeClean != "0" && sizeVal == 0 && sizeClean.Length > 0)
                {
                    _stage = Stage.Start; // 无效大小：跳过该版本
                }
                else
                {
                    _curSize = sizeVal;
                    _stage = Stage.ExpectName;
                }
                break;

            case Stage.ExpectName:
                var nameLine = line;
                if (nameLine == "?")
                    nameLine = _lastFileName != "" ? _lastFileName : "UNKNOWN_FILE_ERROR";
                else
                    _lastFileName = nameLine;

                ArchiveFileEntry entry;
                if (_files.Count > 0 && _files[^1].FileName == nameLine)
                    entry = _files[^1];
                else
                {
                    entry = new ArchiveFileEntry { FileName = nameLine };
                    _files.Add(entry);
                }
                entry.Versions.Add(new FileVersion
                {
                    Version = _curVer,
                    IsDeleted = _curDate == "DELETED",
                    Size = _curSize,
                    DateStr = _curDate,
                });
                _stage = Stage.Start;
                break;
        }
    }

    /// <summary>取完整解析结果（第一层文件列表完整）。</summary>
    public ArchiveData Result
    {
        get
        {
            _result.Files.Clear();
            _result.Files.AddRange(_files);
            return _result;
        }
    }

    private static int ToInt(string s, int def = 0)
    {
        s = s.Trim();
        return int.TryParse(s, out var v) ? v : def;
    }

    private static long ToInt64(string s, long def = 0)
    {
        s = s.Trim();
        return long.TryParse(s, out var v) ? v : def;
    }
}
