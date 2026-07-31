# Discord Trace Remover

A Windows .NET utility that removes old Discord desktop data, Windows registrations, Discord-only browser data, and optional Android or iOS app data before a clean reinstall.

The app uses a Discord-inspired dark GUI with individual cleanup cards for the desktop app, Chrome, Edge, Firefox, Android through ADB, iOS/iPadOS through libimobiledevice, and Windows registrations. **Preview cleanup** is non-destructive; **Clean for reinstall** always asks for confirmation.

## What it removes

- Discord, Discord Canary, Discord PTB, and Discord Development application folders
- Per-user and machine-wide installation folders under AppData, ProgramData, Program Files, and LocalAppData Programs
- Discord caches, logs, settings, crash dumps, updater remnants, and temporary folders
- Discord Microsoft Store application data, when present
- User and public desktop, Start menu, taskbar, and recent-item shortcuts
- Current-user and all-user 32/64-bit registry entries for startup, uninstall, App Paths, URL protocols, application registration, firewall access, and Discord settings
- Cookies and Discord-owned site storage from every detected Chrome and Edge profile
- Cookies, site permissions, legacy web storage, and Discord-owned origin storage from every detected Firefox profile
- The official `com.discord` Android package and its app-owned `Android/data`, `Android/media`, and `Android/obb` folders through ADB
- The official `com.hammerandchisel.discord` iOS app through libimobiledevice; iOS removes the app's private sandbox during uninstall

After cleanup, the program scans the selected desktop and registry locations again. Any item that remains is shown as an error instead of reporting a successful clean.

It does **not** remove browser history, bookmarks, downloads, saved passwords, or data belonging to other websites.

Browser databases are edited through an isolated working copy and atomically replaced only if the browser stayed closed and the original database did not change. Non-Discord cookies remain untouched.

If ESET Enhanced data protection is enabled, ESET may block any unlisted utility from reading or writing Chrome and Edge profiles even after the browsers close. Add `DiscordTraceRemover.exe` under **Advanced setup (F5) > Protections > Browser protection > Browser protection allowlist**, then retry. The cleaner does not disable or stop security software automatically.

Android cleanup does not remove the phone's Downloads folder or unrelated packages. The Android card is off by default.
The device serial detected during confirmation is locked into the cleanup targets; changing connected devices before deletion causes the operation to stop.

If ADB is missing, the GUI can download Google's current Windows Platform Tools archive after showing the SDK license notice. The extracted `adb.exe` must have a valid Google LLC Authenticode signature. Tools are installed privately under `%ProgramFiles%\DiscordTraceRemover\tools`; system PATH is not changed.

iOS cleanup is off by default and requires the independent, third-party `idevice_id` and `ideviceinstaller` tools from libimobiledevice. It does not inspect or change Photos, Files, iCloud, device backups, or unrelated apps. The connected device must be unlocked and must trust the computer.

If the iOS tools are missing, the GUI can download the latest x64 Windows suite from `L1ghtmann/libimobiledevice` after an explicit third-party software warning. The archive URL is restricted to that GitHub repository, its SHA-256 digest must match GitHub's release metadata, and archive links are rejected. Apple's Windows device driver may still require the Apple Devices app.

## Use

1. Save browser work first. After confirmation, the app closes selected Chrome, Edge, and Firefox processes before cleanup.
2. For Android cleanup, enable USB debugging, connect and authorize exactly one device, then select the Android card. The GUI offers to install ADB if needed.
3. For iOS cleanup, connect and trust exactly one unlocked iPhone or iPad, then select the iOS card. The GUI offers to install the third-party tools if needed.
4. Open `DiscordTraceRemover.exe` and approve the Windows administrator prompt.
5. Select the areas to clean.
6. Choose **Preview** to review the detected targets.
7. Choose **Clean for reinstall** and confirm.

Administrator access is required so machine-wide folders, firewall entries, shortcuts, and 32/64-bit registrations can be removed. Cleanup is restricted to official Discord product names and paths; third-party Discord mods and unrelated applications are not targeted.

## Build

```powershell
dotnet build -c Release
```

Publish a self-contained single executable (the user does not need to install .NET separately):

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o release
```

## Command line

Preview without changing anything:

```powershell
DiscordTraceRemover.exe --preview
```

Run cleanup after explicit confirmation:

```powershell
DiscordTraceRemover.exe --clean --yes
```

Add `--desktop-only`, `--chrome-only`, `--edge-only`, `--firefox-only`, `--android-only`, or `--ios-only` to limit the command-line operation. Add `--android` or `--ios` to include either mobile platform in the normal combined cleanup.
