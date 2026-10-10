using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SysInfo;

/// <summary>Lookup tables and patterns shared by the Windows and Linux report builders.</summary>
internal static class Lookups
{
    public static readonly Dictionary<int, string> ChassisType = new()
    {
        [1] = "Other",
        [2] = "Unknown",
        [3] = "Desktop",
        [4] = "Low Profile Desktop",
        [5] = "Pizza Box",
        [6] = "Mini Tower",
        [7] = "Tower",
        [8] = "Portable",
        [9] = "Laptop",
        [10] = "Notebook",
        [11] = "Hand Held",
        [12] = "Docking Station",
        [13] = "All in One",
        [14] = "Sub Notebook",
        [15] = "Space-Saving",
        [16] = "Lunch Box",
        [17] = "Main System Chassis",
        [18] = "Expansion Chassis",
        [19] = "SubChassis",
        [20] = "Bus Expansion Chassis",
        [21] = "Peripheral Chassis",
        [22] = "Storage Chassis",
        [23] = "Rack Mount Chassis",
        [24] = "Sealed-Case PC",
        [30] = "Tablet",
        [31] = "Convertible",
        [32] = "Detachable",
        [33] = "IoT Gateway",
        [34] = "Embedded PC",
        [35] = "Mini PC",
        [36] = "Stick PC"
    };

    public static readonly Regex OemJunk = new(
        @"System Product Name|To be filled|Default string|O\.E\.M\.|System manufacturer|^System Version$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public static readonly Regex LaptopRx = new(
        @"Laptop|Notebook|Portable|Convertible|Detachable|Tablet|Sub Notebook",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
}

internal sealed class MarkdownWriter
{
    private static readonly Regex NewLines = new(@"\r?\n", RegexOptions.Compiled);
    private readonly List<string> _lines = new();

    public void Line(string text = "") => _lines.Add(text);
    public void H1(string t) { Line($"# {t}"); Line(); }
    public void H2(string t) { Line(); Line($"## {t}"); Line(); }
    public void H3(string t) { Line($"### {t}"); Line(); }

    public void KV(params (string Key, object? Value)[] pairs)
    {
        Line("| Property | Value |");
        Line("|---|---|");
        foreach (var (key, value) in pairs)
            Line($"| **{key}** | {Esc(value)} |");
        Line();
    }

    public void Table(string[] headers, IEnumerable<object?[]> rows)
    {
        var data = rows.ToList();
        if (data.Count == 0)
        {
            Line("_None detected._");
            Line();
            return;
        }

        Line("| " + string.Join(" | ", headers) + " |");
        Line("|" + string.Join("|", headers.Select(_ => "---")) + "|");
        foreach (var row in data)
            Line("| " + string.Join(" | ", row.Select(Esc)) + " |");
        Line();
    }

    public static string Esc(object? v)
    {
        string s = v switch
        {
            null => "",
            string str => str,
            IEnumerable seq => string.Join(" ", seq.Cast<object?>().Select(x => x?.ToString() ?? "")),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => v.ToString() ?? ""
        };

        s = s.Trim();
        if (string.IsNullOrWhiteSpace(s)) return "-";
        return NewLines.Replace(s.Replace("|", "\\|"), " ");
    }

    public override string ToString() => string.Join(Environment.NewLine, _lines);
}

internal static class Fmt
{
    private static readonly string[] SizeUnits = { "B", "KB", "MB", "GB", "TB", "PB" };
    private static readonly string[] SpeedUnits = { "bps", "Kbps", "Mbps", "Gbps", "Tbps" };

    public static string Size(double? bytes)
    {
        if (bytes is null or <= 0) return "-";
        double b = bytes.Value;
        int i = 0;
        while (b >= 1024 && i < SizeUnits.Length - 1) { b /= 1024; i++; }
        return $"{b.ToString("N2", CultureInfo.InvariantCulture)} {SizeUnits[i]}";
    }

    public static string LinkSpeed(double? bitsPerSecond)
    {
        if (bitsPerSecond is null or <= 0) return "-";
        double v = bitsPerSecond.Value;
        int i = 0;
        while (v >= 1000 && i < SpeedUnits.Length - 1) { v /= 1000; i++; }
        return $"{v.ToString("0.##", CultureInfo.InvariantCulture)} {SpeedUnits[i]}";
    }

    public static string Num(double value, string format) =>
        value.ToString(format, CultureInfo.InvariantCulture);
}
