using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SysInfo;

[SupportedOSPlatform("windows")]
internal static class WindowsReport
{
    private static readonly Dictionary<int, string> MemType = new()
    {
        [0] = "Unknown",
        [20] = "DDR",
        [21] = "DDR2",
        [22] = "DDR2 FB-DIMM",
        [24] = "DDR3",
        [26] = "DDR4",
        [27] = "LPDDR",
        [28] = "LPDDR2",
        [29] = "LPDDR3",
        [30] = "LPDDR4",
        [34] = "DDR5",
        [35] = "LPDDR5"
    };
    private static readonly Dictionary<int, string> FormFactor = new()
    {
        [0] = "Unknown",
        [8] = "DIMM",
        [12] = "SODIMM",
        [13] = "SRIMM",
        [15] = "FB-DIMM",
        [16] = "DIE"
    };
    private static readonly Dictionary<int, string> CpuArch = new()
    {
        [0] = "x86",
        [1] = "MIPS",
        [2] = "Alpha",
        [3] = "PowerPC",
        [5] = "ARM",
        [6] = "Itanium",
        [9] = "x64",
        [12] = "ARM64"
    };
    private static readonly Dictionary<int, string> ChassisType = Lookups.ChassisType;
    private static readonly Dictionary<int, string> BatStatus = new()
    {
        [1] = "Discharging",
        [2] = "On AC power",
        [3] = "Fully charged",
        [4] = "Low",
        [5] = "Critical",
        [6] = "Charging",
        [7] = "Charging (High)",
        [8] = "Charging (Low)",
        [9] = "Charging (Critical)",
        [10] = "Undefined",
        [11] = "Partially charged"
    };
    private static readonly Dictionary<int, string> BatChemistry = new()
    {
        [1] = "Other",
        [2] = "Unknown",
        [3] = "Lead Acid",
        [4] = "Nickel Cadmium",
        [5] = "Nickel Metal Hydride",
        [6] = "Lithium-ion",
        [7] = "Zinc air",
        [8] = "Lithium Polymer"
    };
    private static readonly Dictionary<int, string> PcType = new()
    {
        [0] = "Unspecified",
        [1] = "Desktop",
        [2] = "Mobile / Laptop",
        [3] = "Workstation",
        [4] = "Enterprise Server",
        [5] = "SOHO Server",
        [6] = "Appliance PC",
        [7] = "Performance Server",
        [8] = "Slate",
        [9] = "Maximum"
    };
    private static readonly Dictionary<int, string> DiskMediaType = new()
    {
        [0] = "Unspecified",
        [3] = "HDD",
        [4] = "SSD",
        [5] = "SCM"
    };
    private static readonly Dictionary<int, string> DiskBusType = new()
    {
        [0] = "Unknown",
        [1] = "SCSI",
        [2] = "ATAPI",
        [3] = "ATA",
        [4] = "1394",
        [5] = "SSA",
        [6] = "Fibre Channel",
        [7] = "USB",
        [8] = "RAID",
        [9] = "iSCSI",
        [10] = "SAS",
        [11] = "SATA",
        [12] = "SD",
        [13] = "MMC",
        [14] = "Virtual",
        [15] = "File Backed Virtual",
        [16] = "Storage Spaces",
        [17] = "NVMe",
        [18] = "SCM",
        [19] = "UFS"
    };
    private static readonly Dictionary<int, string> DiskHealth = new()
    {
        [0] = "Healthy",
        [1] = "Warning",
        [2] = "Unhealthy",
        [5] = "Unknown"
    };
    private static readonly Dictionary<int, string> DiskOpStatus = new()
    {
        [0] = "Unknown",
        [2] = "OK",
        [3] = "Degraded",
        [4] = "Stressed",
        [5] = "Predictive Failure",
        [6] = "Error",
        [10] = "Stopped",
        [11] = "In Service",
        [13] = "Lost Communication",
        [53250] = "Transient Error"
    };
    private static readonly Dictionary<int, string> NetStatus = new()
    {
        [1] = "Up",
        [2] = "Disconnected",
        [3] = "Testing",
        [4] = "Unknown",
        [5] = "Dormant",
        [6] = "Not Present",
        [7] = "Lower Layer Down"
    };

    private static readonly Regex OemJunk = Lookups.OemJunk;
    private static readonly Regex LaptopRx = Lookups.LaptopRx;

    private static string? L(Dictionary<int, string> map, int? key) =>
        map.TryGetValue(key ?? 0, out var v) ? v : null;

    private static string Tf(bool? b) => b is null ? "-" : b.Value ? "True" : "False";


    public static string Build(bool isAdmin)
    {
        var md = new MarkdownWriter();

        var cs = Wmi.First("Win32_ComputerSystem");
        var csp = Wmi.First("Win32_ComputerSystemProduct");
        var bios = Wmi.First("Win32_BIOS");
        var board = Wmi.First("Win32_BaseBoard");
        var chassis = Wmi.First("Win32_SystemEnclosure");
        var os = Wmi.First("Win32_OperatingSystem");
        var cpus = Wmi.Query("Win32_Processor");
        var ramMods = Wmi.Query("Win32_PhysicalMemory");
        var ramArr = Wmi.First("Win32_PhysicalMemoryArray");

        double cpuCores = cpus.Sum(c => c.Dbl("NumberOfCores") ?? 0);
        double cpuThreads = cpus.Sum(c => c.Dbl("NumberOfLogicalProcessors") ?? 0);
        double totalRam = ramMods.Sum(m => m.Dbl("Capacity") ?? 0);
        if (totalRam <= 0) totalRam = cs.Dbl("TotalPhysicalMemory") ?? 0;

        string chassisDesc = string.Join(", ",
            chassis.IntArray("ChassisTypes").Select(t => ChassisType.TryGetValue(t, out var n) ? n : $"Code {t}"));
        bool isLaptop = LaptopRx.IsMatch(chassisDesc);

        int memDevices = (int)(ramArr.Dbl("MemoryDevices") ?? 0);
        bool haveRamArr = ramArr is not null;
        double ramMaxBytes = (ramArr.Dbl("MaxCapacity") ?? 0) * 1024;

        string? titleMake = cs.Str("Manufacturer");
        string? titleModel = cs.Str("Model");
        if (string.IsNullOrEmpty(titleModel) || OemJunk.IsMatch(titleModel))
        {
            titleMake = board.Str("Manufacturer");
            titleModel = board.Str("Product");
        }
        string titleText = $"{titleMake} {titleModel}".Trim();
        md.H1(titleText.Length > 0 ? $"System Information: {titleText}" : "System Information");
        md.Line($"_Generated by **Lotus SysInfo** on **{DateTime.Now:yyyy-MM-dd HH:mm:ss}** | .NET {Environment.Version} | Elevated: **{(isAdmin ? "True" : "False")}**_");
        md.Line();

        md.H2("Quick Summary");
        string modelName = !string.IsNullOrEmpty(cs.Str("Model")) ? cs.Str("Model")! : csp.Str("Name") ?? "";
        string uptime = "-";
        if (os.Date("LastBootUpTime") is { } boot)
        {
            var u = DateTime.Now - boot;
            uptime = $"{u.Days}d {u.Hours}h {u.Minutes}m";
        }
        md.KV(
            ("Manufacturer", cs.Str("Manufacturer")),
            ("Model", modelName),
            ("Product Family", cs.Str("SystemFamily")),
            ("System Type", isLaptop ? $"Laptop ({chassisDesc})" : chassisDesc),
            ("CPU Model", string.Join(" + ", cpus.Select(c => c.Str("Name")?.Trim()))),
            ("CPU Sockets", cpus.Count),
            ("CPU Cores (physical)", cpuCores),
            ("CPU Threads (logical)", cpuThreads),
            ("RAM Total", Fmt.Size(totalRam)),
            ("RAM Module Count", ramMods.Count),
            ("RAM Slots Total", haveRamArr ? memDevices : "-"),
            ("RAM Slots Free", haveRamArr ? memDevices - ramMods.Count : "-"),
            ("RAM Max Supported", haveRamArr ? Fmt.Size(ramMaxBytes) : "-"),
            ("Operating System", $"{os.Str("Caption")} {os.Str("OSArchitecture")} (Build {os.Str("BuildNumber")})"),
            ("BIOS Version", $"{bios.Str("SMBIOSBIOSVersion")} ({bios.Str("Manufacturer")})"),
            ("Uptime", uptime));

        md.H2("Computer / System");
        md.KV(
            ("Manufacturer", cs.Str("Manufacturer")),
            ("Model", cs.Str("Model")),
            ("System Family", cs.Str("SystemFamily")),
            ("SKU / Part Number", cs.Str("SystemSKUNumber")),
            ("Product Name", csp.Str("Name")),
            ("Product Version", csp.Str("Version")),
            ("PC Type (power profile)", L(PcType, cs.Int("PCSystemType"))),
            ("Chassis Type", chassisDesc),
            ("Chassis Manufacturer", chassis.Str("Manufacturer")),
            ("Hypervisor Present", Tf(cs.Bool("HypervisorPresent"))),
            ("Thermal State", cs.Str("ThermalState")),
            ("Boot-up State", cs.Str("BootupState")));

        md.H2("BIOS / UEFI Firmware");
        string fwType = WindowsPlatform.GetFirmwareMode();
        string ec = "-";
        if (bios.Int("EmbeddedControllerMajorVersion") is { } ecMajor && ecMajor != 255)
            ec = $"{ecMajor}.{bios.Int("EmbeddedControllerMinorVersion")}";
        md.KV(
            ("Vendor", bios.Str("Manufacturer")),
            ("Version", bios.Str("SMBIOSBIOSVersion")),
            ("Release Date", bios.Date("ReleaseDate")?.ToString("yyyy-MM-dd") ?? "-"),
            ("SMBIOS Version", $"{bios.Str("SMBIOSMajorVersion")}.{bios.Str("SMBIOSMinorVersion")}"),
            ("Embedded Controller", ec),
            ("Firmware Mode", fwType));

        md.H2("Motherboard");
        md.KV(
            ("Manufacturer", board.Str("Manufacturer")),
            ("Product", board.Str("Product")),
            ("Version", board.Str("Version")));

        md.H2("Operating System");
        string? displayVer = null, ubr = null;
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            displayVer = key?.GetValue("DisplayVersion")?.ToString();
            ubr = key?.GetValue("UBR")?.ToString();
        }
        catch { }
        string pageFile = string.Join("; ", Wmi.Query("Win32_PageFileUsage")
            .Select(p => $"{p.Str("Name")} ({p.Str("AllocatedBaseSize")} MB)"));
        md.KV(
            ("Name", os.Str("Caption")),
            ("Edition Version", displayVer),
            ("Version", os.Str("Version")),
            ("Build", $"{os.Str("BuildNumber")}.{ubr}"),
            ("Architecture", os.Str("OSArchitecture")),
            ("System Directory", os.Str("SystemDirectory")),
            ("Boot Device", os.Str("BootDevice")),
            ("Virtual Memory (Total)", Fmt.Size((os.Dbl("TotalVirtualMemorySize") ?? 0) * 1024)),
            ("Virtual Memory (Free)", Fmt.Size((os.Dbl("FreeVirtualMemory") ?? 0) * 1024)),
            ("Page File", pageFile));

        md.H2("Processor (CPU)");
        int ci = 0;
        foreach (var c in cpus)
        {
            ci++;
            if (cpus.Count > 1) md.H3($"CPU {ci}");
            double l2 = c.Dbl("L2CacheSize") ?? 0, l3 = c.Dbl("L3CacheSize") ?? 0;
            md.KV(
                ("Model", c.Str("Name")?.Trim()),
                ("Manufacturer", c.Str("Manufacturer")),
                ("Description", c.Str("Description")),
                ("Architecture", L(CpuArch, c.Int("Architecture"))),
                ("Physical Cores", c.Str("NumberOfCores")),
                ("Logical Processors (Threads)", c.Str("NumberOfLogicalProcessors")),
                ("Base Clock", $"{Fmt.Num((c.Dbl("MaxClockSpeed") ?? 0) / 1000, "N2")} GHz"),
                ("Current Clock", $"{Fmt.Num((c.Dbl("CurrentClockSpeed") ?? 0) / 1000, "N2")} GHz"),
                ("External Bus Clock", $"{c.Str("ExtClock")} MHz"),
                ("L2 Cache", l2 > 0 ? $"{l2} KB" : "-"),
                ("L3 Cache", l3 > 0 ? $"{l3} KB" : "-"),
                ("Socket", c.Str("SocketDesignation")),
                ("Virtualization Enabled in Firmware", Tf(c.Bool("VirtualizationFirmwareEnabled"))),
                ("SLAT (Second Level Address Translation)", Tf(c.Bool("SecondLevelAddressTranslationExtensions"))),
                ("Current Load", $"{c.Str("LoadPercentage")} %"),
                ("Status", c.Str("Status")));
        }

        md.H2("Memory (RAM)");
        double freePhys = (os.Dbl("FreePhysicalMemory") ?? 0) * 1024;
        double visiblePhys = (os.Dbl("TotalVisibleMemorySize") ?? 0) * 1024;
        md.KV(
            ("Total Installed", Fmt.Size(totalRam)),
            ("Module Count", ramMods.Count),
            ("Total Slots", haveRamArr ? memDevices : "-"),
            ("Empty Slots", haveRamArr ? memDevices - ramMods.Count : "-"),
            ("Max Supported", haveRamArr ? Fmt.Size(ramMaxBytes) : "-"),
            ("Available Now", Fmt.Size(freePhys)),
            ("In Use Now", Fmt.Size(visiblePhys - freePhys)));

        md.H3("Installed Modules");
        var ramRows = ramMods
            .OrderBy(m => m.Str("BankLabel") ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Str("DeviceLocator") ?? "", StringComparer.OrdinalIgnoreCase)
            .Select(m =>
            {
                double speed = m.Dbl("Speed") ?? 0;
                double cfg = m.Dbl("ConfiguredClockSpeed") ?? 0;
                double speedCfg = cfg > 0 ? cfg : speed;
                double volt = m.Dbl("ConfiguredVoltage") ?? 0;
                int typeCode = m.Int("SMBIOSMemoryType") ?? 0;
                return new object?[]
                {
                    m.Str("DeviceLocator"),
                    m.Str("BankLabel"),
                    Fmt.Size(m.Dbl("Capacity")),
                    MemType.TryGetValue(typeCode, out var t) ? t : $"Code {typeCode}",
                    L(FormFactor, m.Int("FormFactor")),
                    speed > 0 ? $"{speed} MT/s" : "-",
                    speedCfg > 0 ? $"{speedCfg} MT/s" : "-",
                    volt > 0 ? $"{Fmt.Num(volt / 1000, "N2")} V" : "-",
                    m.Str("Manufacturer"),
                    m.Str("PartNumber")
                };
            });
        md.Table(new[] { "Slot", "Bank", "Capacity", "Type", "Form Factor", "Rated Speed", "Running Speed", "Voltage (cfg)", "Manufacturer", "Part Number" }, ramRows);

        md.H2("Storage");
        md.H3("Physical Disks");
        var physDisks = Wmi.Query("MSFT_PhysicalDisk", @"root\Microsoft\Windows\Storage");
        if (physDisks.Count > 0)
        {
            var rows = physDisks
                .OrderBy(d => d.Dbl("DeviceId") ?? double.MaxValue)
                .ThenBy(d => d.Str("DeviceId") ?? "", StringComparer.OrdinalIgnoreCase)
                .Select(d => new object?[]
                {
                    d.Str("DeviceId"),
                    d.Str("FriendlyName"),
                    L(DiskMediaType, d.Int("MediaType")),
                    L(DiskBusType, d.Int("BusType")),
                    Fmt.Size(d.Dbl("Size")),
                    L(DiskHealth, d.Int("HealthStatus")),
                    d.IntArray("OperationalStatus").Select(s => DiskOpStatus.TryGetValue(s, out var n) ? n : $"Code {s}").ToList(),
                    d.Str("FirmwareVersion")
                });
            md.Table(new[] { "ID", "Model", "Type", "Bus", "Size", "Health", "Status", "Firmware" }, rows);
        }
        else
        {
            var rows = Wmi.Query("Win32_DiskDrive").Select(d => new object?[]
            {
                d.Str("Model"), d.Str("InterfaceType"), Fmt.Size(d.Dbl("Size")), d.Str("Partitions"), d.Str("FirmwareRevision")
            });
            md.Table(new[] { "Model", "Interface", "Size", "Partitions", "Firmware" }, rows);
        }

        md.H3("Logical Volumes");
        var volumes = Wmi.Query("Win32_LogicalDisk")
            .Where(d => d.Int("DriveType") is 2 or 3 or 4)
            .Select(d =>
            {
                double? size = d.Dbl("Size");
                double? free = d.Dbl("FreeSpace");
                double used = (size ?? 0) - (free ?? 0);
                return new object?[]
                {
                    d.Str("DeviceID"),
                    d.Int("DriveType") switch { 2 => "Removable", 3 => "Local", 4 => "Network", _ => "-" },
                    d.Str("FileSystem"),
                    Fmt.Size(size),
                    Fmt.Size(used),
                    Fmt.Size(free),
                    size is > 0 ? $"{Fmt.Num(used / size.Value * 100, "N1")} %" : "-"
                };
            });
        md.Table(new[] { "Drive", "Type", "File System", "Size", "Used", "Free", "% Used" }, volumes);

        md.H2("Graphics (GPU)");
        var gpuRows = Wmi.Query("Win32_VideoController").Select(g =>
        {
            int w = g.Int("CurrentHorizontalResolution") ?? 0;
            int h = g.Int("CurrentVerticalResolution") ?? 0;
            int hz = g.Int("CurrentRefreshRate") ?? 0;
            return new object?[]
            {
                g.Str("Name"),
                g.Str("AdapterCompatibility"),
                g.Str("DriverVersion"),
                g.Date("DriverDate")?.ToString("yyyy-MM-dd") ?? "-",
                Fmt.Size(g.Dbl("AdapterRAM")),
                w > 0 ? $"{w} x {h}" : "-",
                hz > 0 ? $"{hz} Hz" : "-",
                g.Str("Status")
            };
        });
        md.Table(new[] { "Name", "Vendor", "Driver Version", "Driver Date", "Dedicated VRAM*", "Resolution", "Refresh Rate", "Status" }, gpuRows);
        md.Line("_*Windows reports VRAM as a 32-bit value, so cards with more than 4 GB may show incorrectly._");
        md.Line();

        md.H2("Displays");
        var monitorRows = Wmi.Query("WmiMonitorID", @"root\wmi").Select(m => new object?[]
        {
            DecodeChars(m.Get("ManufacturerName")),
            DecodeChars(m.Get("UserFriendlyName")),
            DecodeChars(m.Get("ProductCodeID")),
            $"{m.Str("WeekOfManufacture")} / {m.Str("YearOfManufacture")}"
        });
        md.Table(new[] { "Manufacturer", "Model", "Product Code", "Made (Week/Year)" }, monitorRows);

        md.H2("Network Adapters");
        var adapters = Wmi.Query("MSFT_NetAdapter", @"root\StandardCimv2");
        if (adapters.Count > 0)
        {
            var rows = adapters
                .OrderBy(a => a.Str("InterfaceDescription") ?? "", StringComparer.OrdinalIgnoreCase)
                .Select(a => new object?[]
                {
                    a.Str("InterfaceDescription"),
                    L(NetStatus, a.Int("InterfaceOperationalStatus")),
                    Fmt.LinkSpeed(a.Dbl("ReceiveLinkSpeed")),
                    a.Str("DriverVersionString"),
                    Tf(a.Bool("Virtual"))
                });
            md.Table(new[] { "Adapter", "Status", "Link Speed", "Driver", "Virtual" }, rows);
        }
        else
        {
            md.Line("_None detected._");
            md.Line();
        }

        md.H2("Battery & Power");
        var batts = Wmi.Query("Win32_Battery");
        if (batts.Count > 0)
        {
            double? design = Wmi.First("BatteryStaticData", @"root\wmi").Dbl("DesignedCapacity");
            double? full = Wmi.First("BatteryFullChargedCapacity", @"root\wmi").Dbl("FullChargedCapacity");
            foreach (var b in batts)
            {
                double run = b.Dbl("EstimatedRunTime") ?? 0;
                md.KV(
                    ("Name", b.Str("Name")),
                    ("Chemistry", L(BatChemistry, b.Int("Chemistry")) ?? "-"),
                    ("Status", L(BatStatus, b.Int("BatteryStatus"))),
                    ("Charge Remaining", $"{b.Str("EstimatedChargeRemaining")} %"),
                    ("Estimated Runtime", run > 0 && run < 71582788 ? $"{run} min" : "-"),
                    ("Design Capacity", design is > 0 ? $"{design} mWh" : "-"),
                    ("Full Charge Capacity", full is > 0 ? $"{full} mWh" : "-"),
                    ("Battery Health", design is > 0 && full is > 0 ? $"{Fmt.Num(full.Value / design.Value * 100, "N1")} %" : "-"));
            }
        }
        else
        {
            md.Line("_No battery detected (likely a desktop)._");
            md.Line();
        }
        md.KV(("Active Power Plan", WindowsPlatform.GetActivePowerPlan()));

        md.H2("Audio Devices");
        md.Table(new[] { "Name", "Manufacturer", "Status" },
            Wmi.Query("Win32_SoundDevice").Select(s => new object?[] { s.Str("Name"), s.Str("Manufacturer"), s.Str("Status") }));

        md.H2("Peripherals");
        md.H3("Keyboards & Pointing Devices");
        var inputRows = Wmi.Query("Win32_Keyboard").Select(k => new object?[] { "Keyboard", k.Str("Name") })
            .Concat(Wmi.Query("Win32_PointingDevice").Select(p => new object?[] { "Pointing", p.Str("Name") }));
        md.Table(new[] { "Type", "Name" }, inputRows);

        md.H3("Connected USB Devices");
        var usbRows = Wmi.Query("Win32_PnPEntity", Wmi.CimV2,
                "PNPClass = 'USB' OR PNPClass = 'HIDClass' OR PNPClass = 'Camera' OR PNPClass = 'Image'")
            .Where(d => !string.IsNullOrWhiteSpace(d.Str("Name")))
            .OrderBy(d => d.Str("PNPClass") ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.Str("Name") ?? "", StringComparer.OrdinalIgnoreCase)
            .Select(d => new object?[] { d.Str("PNPClass"), d.Str("Name"), d.Str("Status") });
        md.Table(new[] { "Class", "Device", "Status" }, usbRows);

        md.H2("Platform Security");
        var tpm = Wmi.First("Win32_Tpm", @"root\cimv2\Security\MicrosoftTpm");
        bool? tpmEnabled = tpm is null ? null : WindowsPlatform.InvokeBool(tpm, "IsEnabled", "IsEnabled");
        bool? tpmActivated = tpm is null ? null : WindowsPlatform.InvokeBool(tpm, "IsActivated", "IsActivated");
        string tpmReady = tpm is null ? "-" : Tf(tpmEnabled == true && tpmActivated == true);
        string tpmManufacturer = tpm is null ? "-" : $"{tpm.Str("ManufacturerIdTxt")} ({tpm.Str("ManufacturerVersion")})";
        md.KV(
            ("Secure Boot", WindowsPlatform.GetSecureBootState(fwType)),
            ("TPM Present", tpm is not null ? "True" : "Unknown / needs admin"),
            ("TPM Ready", tpmReady),
            ("TPM Spec Version", tpm is not null ? tpm.Str("SpecVersion") : "-"),
            ("TPM Manufacturer", tpmManufacturer));

        md.Line("---");
        md.Line("_End of report · Lotus SysInfo_");

        return md.ToString();
    }

    private static string? DecodeChars(object? arr)
    {
        if (arr is not Array a) return null;
        var sb = new StringBuilder();
        foreach (var x in a)
        {
            int c;
            try { c = Convert.ToInt32(x); } catch { continue; }
            if (c != 0) sb.Append((char)c);
        }
        string s = sb.ToString().Trim();
        return s.Length == 0 ? null : s;
    }
}

[SupportedOSPlatform("windows")]
internal static class WindowsPlatform
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFirmwareType(out uint firmwareType);

    public static string GetFirmwareMode()
    {
        try
        {
            if (GetFirmwareType(out uint t))
                return t switch { 1 => "Legacy BIOS", 2 => "UEFI", _ => "Unknown" };
        }
        catch { }
        return "Unknown";
    }

    public static string GetSecureBootState(string firmwareMode)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
            if (key?.GetValue("UEFISecureBootEnabled") is int v)
                return v == 1 ? "True" : "False";
        }
        catch { }
        return firmwareMode == "Legacy BIOS"
            ? "Not supported (Legacy BIOS)"
            : "Unknown / not supported / needs admin";
    }

    public static string? GetActivePowerPlan()
    {
        try
        {
            var psi = new ProcessStartInfo("powercfg", "/getactivescheme")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            string output = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(5000);

            var m = Regex.Match(output, @"\(([^()]+)\)\s*$");
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }
        catch { return null; }
    }

    public static bool? InvokeBool(ManagementObject obj, string method, string outParam)
    {
        try
        {
            var result = obj.InvokeMethod(method, null, null);
            return result?[outParam] is bool b ? b : null;
        }
        catch { return null; }
    }
}

[SupportedOSPlatform("windows")]
internal static class Wmi
{
    public const string CimV2 = @"root\cimv2";

    public static List<ManagementObject> Query(string wmiClass, string ns = CimV2, string? where = null)
    {
        var results = new List<ManagementObject>();
        try
        {
            string query = $"SELECT * FROM {wmiClass}" + (where is null ? "" : $" WHERE {where}");
            using var searcher = new ManagementObjectSearcher(ns, query);
            foreach (ManagementBaseObject o in searcher.Get())
                results.Add((ManagementObject)o);
        }
        catch
        {

        }
        return results;
    }

    public static ManagementObject? First(string wmiClass, string ns = CimV2, string? where = null) =>
        Query(wmiClass, ns, where).FirstOrDefault();

    public static object? Get(this ManagementBaseObject? o, string name)
    {
        if (o is null) return null;
        try { return o[name]; } catch { return null; }
    }

    public static string? Str(this ManagementBaseObject? o, string name) => o.Get(name)?.ToString();

    public static double? Dbl(this ManagementBaseObject? o, string name)
    {
        var v = o.Get(name);
        if (v is null) return null;
        try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); } catch { return null; }
    }

    public static int? Int(this ManagementBaseObject? o, string name)
    {
        var v = o.Get(name);
        if (v is null) return null;
        try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); } catch { return null; }
    }

    public static bool? Bool(this ManagementBaseObject? o, string name)
    {
        var v = o.Get(name);
        if (v is null) return null;
        try { return Convert.ToBoolean(v, CultureInfo.InvariantCulture); } catch { return null; }
    }

    public static DateTime? Date(this ManagementBaseObject? o, string name)
    {
        var v = o.Get(name);
        if (v is null) return null;
        try { return v is string s ? ManagementDateTimeConverter.ToDateTime(s) : Convert.ToDateTime(v, CultureInfo.InvariantCulture); }
        catch { return null; }
    }

    public static List<int> IntArray(this ManagementBaseObject? o, string name)
    {
        var result = new List<int>();
        if (o.Get(name) is Array arr)
        {
            foreach (var x in arr)
            {
                try { result.Add(Convert.ToInt32(x, CultureInfo.InvariantCulture)); } catch { }
            }
        }
        return result;
    }
}
