using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SysInfo;

/// <summary>
/// Small, exception-free helpers for reading /proc, /sys and running external tools.
/// Every method returns null / empty instead of throwing so a missing file or tool just
/// results in "-" in the report.
/// </summary>
internal static class Lx
{
    private static readonly Regex Placeholder = new(
        @"^(Not Specified|Unknown|Undefined|\[Empty\]|N/A|None|0x0+|Default string|To Be Filled By O\.E\.M\.)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Reads a small text file (works for /proc and /sys). Null when missing, unreadable or empty.</summary>
    public static string? Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            string s = File.ReadAllText(path).Trim('\0', ' ', '\n', '\r', '\t');
            return s.Length == 0 ? null : s;
        }
        catch { return null; }
    }

    public static long? ReadLong(string path) =>
        long.TryParse(Read(path), NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : null;

    /// <summary>Null for empty text and for firmware placeholder strings ("Unknown", "To Be Filled By O.E.M." ...).</summary>
    public static string? Tidy(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        return Placeholder.IsMatch(s) ? null : s;
    }

    /// <summary>Sub-directories (symlinks to directories included), ordinal-sorted. Empty when the path is missing.</summary>
    public static string[] Dirs(string path, string pattern = "*")
    {
        try
        {
            if (!Directory.Exists(path)) return Array.Empty<string>();
            var dirs = Directory.GetDirectories(path, pattern);
            Array.Sort(dirs, StringComparer.Ordinal);
            return dirs;
        }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>Fully resolves symlinks; returns the input path (as a full path) if it is not a link.</summary>
    public static string? RealPath(string path)
    {
        try
        {
            var info = new DirectoryInfo(path);
            return (info.ResolveLinkTarget(true) ?? info).FullName;
        }
        catch { return null; }
    }

    /// <summary>File name of the target a symlink points to (e.g. .../driver -> "i915"), or null if the link is absent.</summary>
    public static string? LinkName(string linkPath)
    {
        try
        {
            if (!Directory.Exists(linkPath) && !File.Exists(linkPath)) return null;
            string? real = RealPath(linkPath);
            return real is null ? null : Path.GetFileName(real.TrimEnd('/'));
        }
        catch { return null; }
    }

    /// <summary>"Key: value" lines (meminfo, lscpu, one cpuinfo block, dmidecode block). First occurrence of a key wins.</summary>
    public static Dictionary<string, string> Colon(string? text)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(text)) return d;
        foreach (string raw in text.Split('\n'))
        {
            int i = raw.IndexOf(':');
            if (i <= 0) continue;
            string key = raw[..i].Trim();
            if (key.Length == 0 || d.ContainsKey(key)) continue;
            d[key] = raw[(i + 1)..].Trim();
        }
        return d;
    }

    /// <summary>KEY=value lines as used by /etc/os-release.</summary>
    public static Dictionary<string, string> OsRelease()
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        string? text = Read("/etc/os-release") ?? Read("/usr/lib/os-release");
        if (text is null) return d;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            int i = line.IndexOf('=');
            if (i <= 0) continue;
            d[line[..i]] = line[(i + 1)..].Trim().Trim('"', '\'');
        }
        return d;
    }

    /// <summary>/proc/cpuinfo split into one dictionary per logical processor.</summary>
    public static List<Dictionary<string, string>> CpuInfoBlocks()
    {
        var list = new List<Dictionary<string, string>>();
        string? text = Read("/proc/cpuinfo");
        if (text is null) return list;
        foreach (string block in Regex.Split(text.Replace("\r", ""), @"\n\s*\n"))
        {
            var d = Colon(block);
            if (d.Count > 0) list.Add(d);
        }
        return list;
    }

    /// <summary>Parses "32K", "12288K", "16 GB", "512 kB", "16384 MB" into bytes.</summary>
    public static double? ParseSize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var m = Regex.Match(s, @"^\s*(\d+(?:\.\d+)?)\s*([KMGTP])?i?B?\s*$", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        double v = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        int pow = m.Groups[2].Success ? "KMGTP".IndexOf(char.ToUpperInvariant(m.Groups[2].Value[0])) + 1 : 0;
        return v * Math.Pow(1024, pow);
    }

    /// <summary>
    /// Runs an external tool and returns its stdout. Returns null when the tool is not installed,
    /// times out, or (when <paramref name="requireSuccess"/>) exits non-zero.
    /// Tools in sbin are found even when sbin is not on PATH (typical for non-root users).
    /// Output is forced to the C locale so it can be parsed reliably.
    /// </summary>
    public static string? Run(string tool, string[] args, int timeoutMs = 5000, bool requireSuccess = true)
    {
        foreach (string exe in Candidates(tool))
        {
            try
            {
                var psi = new ProcessStartInfo(exe)
                {
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                foreach (string a in args) psi.ArgumentList.Add(a);
                psi.Environment["LC_ALL"] = "C";
                psi.Environment["LANG"] = "C";

                using var p = Process.Start(psi);
                if (p is null) continue;
                p.StandardInput.Close();
                var stdout = p.StandardOutput.ReadToEndAsync();
                var stderr = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(true); } catch { }
                    return null;
                }
                p.WaitForExit(); // make sure redirected streams are flushed
                string output = stdout.GetAwaiter().GetResult();
                _ = stderr.GetAwaiter().GetResult();
                if (requireSuccess && p.ExitCode != 0) return null;
                return output;
            }
            catch (Win32Exception) { /* not found here, try next location */ }
            catch { return null; }
        }
        return null;
    }

    private static IEnumerable<string> Candidates(string tool)
    {
        yield return tool;
        if (!tool.Contains('/'))
        {
            yield return "/usr/sbin/" + tool;
            yield return "/sbin/" + tool;
        }
    }
}
