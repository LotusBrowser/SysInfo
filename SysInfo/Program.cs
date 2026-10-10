using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;

namespace SysInfo;

internal static class Program
{
    private const string ShareBaseUrl = "https://share.browselotus.net";

    private static async Task<int> Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }

        var flags = new HashSet<string>(args.Select(a => a.Trim().ToLowerInvariant()));
        if (flags.Overlaps(new[] { "-h", "--help", "/?", "-?", "/h" }))
        {
            PrintHelp();
            return 0;
        }
        bool shareOnline = flags.Overlaps(new[] { "--share-online", "--shareonline", "-shareonline", "--share", "-s", "/shareonline" });

        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Say("Lotus SysInfo currently supports Windows and Linux only.", ConsoleColor.Red);
            return 1;
        }

        Console.WriteLine();
        Say("  Lotus - SysInfo", ConsoleColor.Magenta);
        Say("  ---------------", ConsoleColor.DarkMagenta);
        Say("Collecting system information...", ConsoleColor.Cyan);

        bool isAdmin = Environment.IsPrivilegedProcess; // Windows: elevated, Linux/Unix: euid == 0
        string rawMarkdown = OperatingSystem.IsWindows() ? BuildWindows(isAdmin) : BuildLinux(isAdmin);
        string markdown = Protect(rawMarkdown);
        if (markdown != rawMarkdown)
            Say("Privacy filter removed personal identifiers from the report.", ConsoleColor.DarkYellow);

        string fileName = $"SysInfo_{DateTime.Now:yyyyMMdd_HHmmss}.md";
        string outputPath = "";
        bool savedLocal = false;
        foreach (string dir in CandidateOutputDirectories())
        {
            string candidate = Path.Combine(dir, fileName);
            try
            {
                await File.WriteAllTextAsync(candidate, markdown, new UTF8Encoding(false));
                outputPath = candidate;
                savedLocal = true;
                Console.WriteLine();
                Say($"Report saved to: {outputPath}", ConsoleColor.Green);
                break;
            }
            catch (Exception ex)
            {
                Say($"Could not save report in {dir}: {ex.Message}", ConsoleColor.DarkYellow);
            }
        }
        if (!savedLocal)
            Say("Failed to save the report to disk.", ConsoleColor.Red);

        string? shareLink = null;
        if (shareOnline)
        {
            string uuid = Guid.NewGuid().ToString();
            Say("Uploading report...", ConsoleColor.Cyan);
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{ShareBaseUrl}/api/sysinfo")
                {
                    Content = JsonContent.Create(new { uuid, markdown })
                };
                string? token = Environment.GetEnvironmentVariable("SYSINFO_TOKEN");
                if (!string.IsNullOrEmpty(token))
                    request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");

                using var response = await http.SendAsync(request);
                response.EnsureSuccessStatusCode();

                shareLink = $"{ShareBaseUrl}/sysinfo?uuid={uuid}";
                string copied = TryCopyToClipboard(shareLink) ? " (copied to clipboard)" : "";

                Say($"Shareable link{copied}:", ConsoleColor.Green);
                Say($"  {shareLink}", ConsoleColor.White);
                Say("Anyone with this link can view the report. It contains hardware specs only (no serial numbers, names, or network info).", ConsoleColor.Yellow);
            }
            catch (HttpRequestException ex) when (ex.StatusCode is not null)
            {
                Say($"Upload failed: HTTP {(int)ex.StatusCode}", ConsoleColor.Red);
            }
            catch (Exception ex)
            {
                Say($"Upload failed: {ex.Message}", ConsoleColor.Red);
            }
        }

        if (!isAdmin)
        {
            Say(OperatingSystem.IsWindows()
                ? "Tip: run as Administrator for fuller TPM / Secure Boot data."
                : "Tip: run with sudo for RAM slot details (dmidecode) and drive health (smartctl).",
                ConsoleColor.Yellow);
        }

        // As root on Linux (sudo) there is usually no usable desktop session to open anything in.
        bool canOpen = OperatingSystem.IsWindows() || !isAdmin;
        if (canOpen)
        {
            if (shareLink is not null) OpenShell(shareLink);
            else if (savedLocal) OpenShell(outputPath);
        }

        return 0;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Lotus - SysInfo: share your exact system specs with friends, easily. (Windows & Linux)");
        Console.WriteLine();
        Console.WriteLine("Usage: LotusSysInfo [--share-online]");
        Console.WriteLine();
        Console.WriteLine("  --share-online   Also upload the report to share.browselotus.net and print a link");
        Console.WriteLine("                   anyone can open (the link contains a random UUID).");
        Console.WriteLine("  -h, --help       Show this help.");
        Console.WriteLine();
        Console.WriteLine("Environment: SYSINFO_TOKEN (optional) is sent as a Bearer token on upload.");
    }

    private static void Say(string text, ConsoleColor color)
    {
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ForegroundColor = previous;
    }

    [SupportedOSPlatform("windows")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string BuildWindows(bool isAdmin) => WindowsReport.Build(isAdmin);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string BuildLinux(bool isPrivileged) => LinuxReport.Build(isPrivileged);

    private static IEnumerable<string> CandidateOutputDirectories()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var dirs = new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory(),
            Path.GetTempPath()
        };
        foreach (string? d in dirs)
        {
            if (string.IsNullOrEmpty(d)) continue;
            string full = Path.GetFullPath(d);
            if (seen.Add(full)) yield return full;
        }
    }

    // Values that identify the user/machine and must never reach the report.
    // Generic names carry no personal information and would mangle legitimate text
    // (e.g. a host called "ubuntu" turning "Ubuntu 24.04" into "[removed] 24.04"), so they are kept.
    private static readonly HashSet<string> GenericNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "root", "localhost", "user", "admin", "administrator",
        "ubuntu", "debian", "fedora", "archlinux", "raspberrypi", "nixos"
    };

    private static string Protect(string text)
    {
        var raw = new List<string?>
        {
            Environment.GetEnvironmentVariable("COMPUTERNAME"),
            Environment.GetEnvironmentVariable("USERNAME"),
            Environment.GetEnvironmentVariable("USERDOMAIN"),
            Environment.GetEnvironmentVariable("USERPROFILE"),
            Environment.MachineName,
            Environment.UserName
        };

        if (OperatingSystem.IsLinux())
        {
            string? sudoUser = Environment.GetEnvironmentVariable("SUDO_USER");
            raw.Add(Environment.GetEnvironmentVariable("HOME"));
            raw.Add(Environment.GetEnvironmentVariable("USER"));
            raw.Add(Environment.GetEnvironmentVariable("LOGNAME"));
            raw.Add(Environment.GetEnvironmentVariable("HOSTNAME"));
            raw.Add(sudoUser);
            if (!string.IsNullOrEmpty(sudoUser)) raw.Add($"/home/{sudoUser}");
            try { raw.Add(File.ReadAllText("/etc/hostname").Trim()); } catch { }
        }

        // Whole paths (profile / home) and their last segment (the user folder name).
        var values = new List<string>();
        foreach (string? v in raw)
        {
            if (string.IsNullOrEmpty(v)) continue;
            values.Add(v);
            string leaf = Path.GetFileName(v.TrimEnd('\\', '/'));
            if (leaf.Length > 0 && leaf != v) values.Add(leaf);
        }

        var privateValues = values
            .Where(v => v.Length >= 3 && !GenericNames.Contains(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(v => v.Length); // longest first so "/home/bob" goes before "bob"

        foreach (var value in privateValues)
        {
            text = Regex.Replace(text, @"(?<!\w)" + Regex.Escape(value) + @"(?!\w)", "[removed]", RegexOptions.IgnoreCase);
        }
        return Regex.Replace(text, @"\b(?:[0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}\b", "[removed]");
    }

    private static bool TryCopyToClipboard(string text)
    {
        if (OperatingSystem.IsWindows())
            return TryClipboardTool("clip.exe");

        // Linux: Wayland, then X11, then WSL's clip.exe interop.
        return TryClipboardTool("wl-copy")
            || TryClipboardTool("xclip", "-selection", "clipboard")
            || TryClipboardTool("xsel", "--clipboard", "--input")
            || TryClipboardTool("clip.exe");

        bool TryClipboardTool(string tool, params string[] toolArgs)
        {
            try
            {
                var psi = new ProcessStartInfo(tool)
                {
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                foreach (string a in toolArgs) psi.ArgumentList.Add(a);
                using var p = Process.Start(psi);
                if (p is null) return false;
                p.StandardInput.Write(text);
                p.StandardInput.Close();
                p.WaitForExit(5000);
                return p.HasExited && p.ExitCode == 0;
            }
            catch { return false; } // tool not installed / no display
        }
    }

    private static void OpenShell(string target)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                return;
            }

            // Linux: only try when a graphical session exists, and keep xdg-open's chatter off the terminal.
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")) &&
                string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
                return;

            var psi = new ProcessStartInfo("xdg-open")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add(target);
            Process.Start(psi)?.Dispose();
        }
        catch { }
    }
}
