# Lotus SysInfo

Lotus SysInfo is a lightweight Windows console utility that collects detailed system information and generates a clean, shareable Markdown report.  
It is designed to help you quickly share your exact hardware and OS specifications with others-without exposing personal identifiers.

## Features

- Collects comprehensive hardware and software details:
  - CPU, RAM (modules, slots, speeds), storage (physical disks, volumes), GPU, displays
  - Motherboard, BIOS/UEFI, operating system, network adapters
  - Battery, audio devices, peripherals, USB devices
  - Platform security (Secure Boot, TPM)
- Automatically applies a **privacy filter** to remove:
  - Computer name, username, domain, user profile path
  - MAC addresses
- Saves the report as a Markdown file locally.
- Optionally uploads the report to a public share service and returns a short link.
- Copies the share link to the clipboard and opens it in your default browser.
- Works without Administrator rights, but running as Administrator provides fuller TPM and Secure Boot data.

## Requirements

- Windows 10 or later (the tool relies on WMI, the registry, and Windows-specific APIs).
- No installation required-just run the executable.

## Usage

Run `SysInfo.exe` (or the compiled executable name) from a command prompt or by double-clicking.

By default, the program will:
1. Collect system information.
2. Apply the privacy filter.
3. Save a Markdown report in the same folder as the executable, named `SysInfo_YYYYMMDD_HHMMSS.md`.
4. Open the report in your default Markdown viewer or text editor.

### Command Line Arguments

| Argument | Aliases | Description |
|----------|---------|-------------|
| `--share-online` | `--shareonline`, `-shareonline`, `--share`, `-s`, `/shareonline` | Also uploads the report to `share.browselotus.net` and prints a shareable link. The link is copied to the clipboard and opened in your browser. |
| `-h`, `--help` | `/?`, `-?`, `/h` | Displays a help message and exits. |

## Examples

**Generate a local report only:**
```
SysInfo.exe
```

**Generate a report and get a shareable link:**
```
SysInfo.exe --share-online
```

**Show help:**
```
SysInfo.exe --help
```

## Output

- **Local report:** A Markdown file named `SysInfo_<timestamp>.md` saved next to the executable.
- **Online Share:** If `--share-online` is used, the program prints a URL in the format:
  ```
  https://share.browselotus.net/sysinfo?uuid=<random-uuid>
  ```
  Anyone with this link can view the report. The report contains hardware specifications only; personal identifiers are removed by the privacy filter.

## Privacy & Security

- The privacy filter removes the following from the report:
  - Computer name
  - Username
  - User domain
  - User profile path (and its folder name)
  - MAC addresses
- Shared reports do **not** include serial numbers, personal names, or network information.
- The online share service stores the report under a random UUID. The link is unlisted but publicly accessible to anyone who has it.

## Notes

- Running the tool as Administrator is recommended for the most complete TPM and Secure Boot information. If not elevated, a tip is displayed at the end.
- The report is generated in Markdown format, which can be easily viewed in any text editor, Markdown viewer, or pasted into a chat/Discord/forum that supports Markdown.
