# Steam Icon Restorer

Restore missing/incorrect game icons in your Steam installation.

This tool authenticates with Steam, fetches the official client icon for each installed game, and writes `.ico` files into your Steam `steam\games` directory so Windows can show the proper icons again.

Note: On first runs, Windows Defender SmartScreen may warn that the app is from an unknown publisher because the binary is not signed with a commercial code‑signing certificate. You can still run it by choosing “More info” → “Run anyway”.

---

## Features
- Interactive and non‑interactive (CLI) modes
- Two authentication methods:
  - QR Code (recommended)
  - Username/password
- Automatic Steam install path detection on Windows, Linux, and macOS (can be overridden)
- Clear validation and error messages
- Optional verbose output for troubleshooting

---

## Requirements
- .NET 8 runtime if you use the portable build (not required for the Windows single‑file build)
- A Steam installation on the machine where you run the tool

---

## Download
Prebuilt binaries are attached to GitHub Releases for:
- Windows (portable ZIP and single‑file `win-x64`)
- Linux (`linux-x64` tar.gz)
- macOS (`osx-x64` and `osx-arm64` tar.gz)

---

## Usage

### Interactive mode (default when no args)
```
SteamIconRestorer.exe --interactive
```
The app will:
1) Detect the Steam install path (or allow you to input one),
2) Ask for the authentication method (QR vs. username/password), and
3) Run the icon restore process.

### QR code authentication (recommended)
```
SteamIconRestorer.exe --use-qr-code --steam-install-path "C:\\Program Files (x86)\\Steam"
```

### Username/password authentication
```
SteamIconRestorer.exe --username yourUser --password yourPass --steam-install-path "C:\\Program Files (x86)\\Steam"
```

### Verbose diagnostics
```
SteamIconRestorer.exe --use-qr-code -v
```

### Common options
- `--steam-install-path`, `-s`: path to your Steam installation. If omitted, the app tries to auto‑detect.
- `--use-qr-code`, `-q`: use QR code authentication.
- `--username`, `-u` and `--password`, `-p`: provide credentials (only if not using QR).
- `--interactive`, `-i`: force interactive prompts even when arguments are given.
- `--verbose`, `-v`: show full exceptions and extra logs.

---

## Building locally

### Windows
- Portable (requires .NET 8 runtime):
```
dotnet publish -c Release -o publish
```
- Single‑file self‑contained (no runtime required):
```
dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true -p:PublishTrimmed=false --self-contained true -o publish\win-x64
```

### Linux
```
dotnet publish -c Release -r linux-x64 --self-contained false -o publish/linux-x64
```

### macOS
```
# Apple Silicon
dotnet publish -c Release -r osx-arm64 --self-contained false -o publish/osx-arm64

# Intel
dotnet publish -c Release -r osx-x64 --self-contained false -o publish/osx-x64
```

---

## CI/CD (GitHub Actions)
- Pushing a tag like `v1.0.0` triggers builds for Windows, Linux, and macOS.
- Artifacts are archived (ZIP/tar.gz) and uploaded to the corresponding GitHub Release.
- The app banner prints the assembly version derived from the tag.

---

## Notes about SmartScreen / unsigned binaries
This project’s release binaries are not signed with a commercial code‑signing certificate by default. As a result, Windows SmartScreen may display a warning:

- Click “More info” → “Run anyway” to continue.
- If you distribute widely and want to remove that warning, consider signing releases with an OV/EV code‑signing certificate. EV grants immediate SmartScreen reputation; OV gains reputation over time.

---

## Troubleshooting
- Use `-v` to see full exception details.
- Ensure the Steam path is correct and accessible.
- On Linux/macOS, the tool searches common Steam directories under the current user.
- Network issues can prevent fetching icons; retry later or check your connection.
