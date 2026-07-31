using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace DiscordTraceRemover;

internal static class CleanupEngine
{
    private sealed record RegistryScope(
        RegistryHive Hive,
        RegistryView View,
        string Prefix,
        string DisplayName);

    private static readonly RegistryScope[] RegistryScopes =
    [
        new(RegistryHive.CurrentUser, RegistryView.Registry64, "HKCU64", "current user (64-bit)"),
        new(RegistryHive.CurrentUser, RegistryView.Registry32, "HKCU32", "current user (32-bit)"),
        new(RegistryHive.LocalMachine, RegistryView.Registry64, "HKLM64", "all users (64-bit)"),
        new(RegistryHive.LocalMachine, RegistryView.Registry32, "HKLM32", "all users (32-bit)")
    ];

    private static readonly string[] DiscordHostTokens =
    [
        "discord.com",
        "discord.gg",
        "discordapp.com",
        "discordapp.net",
        "discord.media",
        "discordcdn.com"
    ];

    private static readonly string[] DiscordProcessNames =
    [
        "Discord",
        "DiscordCanary",
        "DiscordPTB",
        "DiscordDevelopment"
    ];

    private static readonly string[] DiscordFolderNames =
    [
        "Discord",
        "discord",
        "DiscordCanary",
        "discordcanary",
        "DiscordPTB",
        "discordptb",
        "DiscordDevelopment",
        "discorddevelopment"
    ];

    private static readonly string[] DiscordExecutableNames =
    [
        "Discord.exe",
        "DiscordCanary.exe",
        "DiscordPTB.exe",
        "DiscordDevelopment.exe"
    ];

    private static readonly string[] DiscordRegistrationKeys =
    [
        @"Software\Discord",
        @"Software\Discord Inc.",
        @"Software\Classes\discord",
        @"Software\Classes\discord-canary",
        @"Software\Classes\discord-ptb",
        @"Software\Classes\discordcanary",
        @"Software\Classes\discordptb",
        @"Software\Classes\Applications\Discord.exe",
        @"Software\Classes\Applications\DiscordCanary.exe",
        @"Software\Classes\Applications\DiscordPTB.exe",
        @"Software\Classes\Applications\DiscordDevelopment.exe",
        @"Software\Clients\StartMenuInternet\Discord",
        @"Software\Clients\StartMenuInternet\DiscordCanary",
        @"Software\Clients\StartMenuInternet\DiscordPTB",
        @"Software\Microsoft\Windows\CurrentVersion\App Paths\Discord.exe",
        @"Software\Microsoft\Windows\CurrentVersion\App Paths\DiscordCanary.exe",
        @"Software\Microsoft\Windows\CurrentVersion\App Paths\DiscordPTB.exe",
        @"Software\Microsoft\Windows\CurrentVersion\App Paths\DiscordDevelopment.exe"
    ];

    private static readonly string[] DiscordRegistryValueContainers =
    [
        @"Software\Microsoft\Windows\CurrentVersion\Run",
        @"Software\Microsoft\Windows\CurrentVersion\RunOnce",
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run",
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32",
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder",
        @"Software\RegisteredApplications"
    ];

    internal static PreviewResult Preview(CleanupOptions options)
    {
        return new PreviewResult(Discover(options));
    }

    internal static void RunTargetingSelfTest()
    {
        if (!IsOfficialDiscordProduct("Discord PTB") ||
            !IsOfficialDiscordProduct("DiscordCanary") ||
            IsOfficialDiscordProduct("BetterDiscord") ||
            IsOfficialDiscordProduct("Discord Bot Manager"))
        {
            throw new InvalidOperationException("Discord product targeting test failed.");
        }

        if (!IsOfficialDiscordRegistryValue(
                "Discord",
                @"C:\Users\Test\AppData\Local\Discord\Update.exe --processStart Discord.exe") ||
            IsOfficialDiscordRegistryValue("Browser", "https://discord.com"))
        {
            throw new InvalidOperationException("Registry targeting test failed.");
        }

        if (!IsPathWithin(@"C:\Users\Test\AppData\Local\Discord\Update.exe", @"C:\Users\Test\AppData\Local\Discord") ||
            IsPathWithin(@"C:\Users\Test\AppData\Local\DiscordBackup\Update.exe", @"C:\Users\Test\AppData\Local\Discord"))
        {
            throw new InvalidOperationException("Folder boundary targeting test failed.");
        }
    }

    internal static CleanupResult Clean(
        CleanupOptions options,
        Action<string>? report = null,
        IReadOnlyList<CleanupItem>? confirmedTargets = null)
    {
        var targets = confirmedTargets ?? Discover(options);

        if (options.ChromeData && targets.Any(item => item.Category.Equals("Chrome", StringComparison.OrdinalIgnoreCase)))
        {
            StopBrowser("chrome", "Google Chrome", report);
        }

        if (options.EdgeData && targets.Any(item => item.Category.Equals("Edge", StringComparison.OrdinalIgnoreCase)))
        {
            StopBrowser("msedge", "Microsoft Edge", report);
        }

        if (options.FirefoxData && targets.Any(item => item.Category.Equals("Firefox", StringComparison.OrdinalIgnoreCase)))
        {
            StopBrowser("firefox", "Mozilla Firefox", report);
        }

        if (options.DesktopData)
        {
            StopDiscord(report);
        }

        if (options.DesktopData && options.WindowsIntegration)
        {
            RunDiscordUninstallers(report);
            StopDiscord(report);
        }

        var succeeded = 0;
        var failed = 0;
        var skipped = 0;
        var failedLocations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in targets)
        {
            try
            {
                var changed = item.Type switch
                {
                    CleanupItemType.Directory => DeleteDirectory(item.Location),
                    CleanupItemType.File => DeleteFile(item.Location),
                    CleanupItemType.ChromiumCookies => DeleteChromiumDiscordCookies(item.Location, report),
                    CleanupItemType.FirefoxCookies => DeleteFirefoxDiscordCookies(item.Location, report),
                    CleanupItemType.FirefoxLegacyStorage => DeleteFirefoxLegacyStorage(item.Location, report),
                    CleanupItemType.FirefoxPermissions => DeleteFirefoxPermissions(item.Location, report),
                    CleanupItemType.AndroidPackage => AdbClient.UninstallPackage(RequireTargetId(item), report),
                    CleanupItemType.AndroidDirectory => AdbClient.DeleteExternalDirectory(RequireTargetId(item), report),
                    CleanupItemType.IosPackage => IosDeviceClient.UninstallDiscord(RequireTargetId(item), report),
                    CleanupItemType.Registry => DeleteRegistryEntry(item.Location),
                    _ => false
                };

                if (changed)
                {
                    succeeded++;
                    report?.Invoke($"Removed: {item.Description}");
                }
                else
                {
                    skipped++;
                    report?.Invoke($"Already clear: {item.Description}");
                }
            }
            catch (Exception ex)
            {
                failed++;
                failedLocations.Add(item.Location);
                report?.Invoke($"Could not remove {item.Description}: {ex.Message}");
            }
        }

        if (options.DesktopData || options.WindowsIntegration)
        {
            var verificationOptions = new CleanupOptions(
                DesktopData: options.DesktopData,
                ChromeData: false,
                EdgeData: false,
                FirefoxData: false,
                AndroidData: false,
                IosData: false,
                WindowsIntegration: options.WindowsIntegration);
            var remaining = Discover(verificationOptions);

            if (remaining.Count == 0)
            {
                report?.Invoke("Verified: no selected Discord desktop folders or registry registrations remain.");
            }
            else
            {
                report?.Invoke($"Verification found {remaining.Count} desktop item(s) still present:");
                foreach (var item in remaining)
                {
                    if (failedLocations.Add(item.Location))
                    {
                        failed++;
                    }

                    report?.Invoke($"  Still present: {item.Description} - {item.Location}");
                }
            }
        }

        if (options.AndroidData)
        {
            var verificationOptions = new CleanupOptions(
                DesktopData: false,
                ChromeData: false,
                EdgeData: false,
                FirefoxData: false,
                AndroidData: true,
                IosData: false,
                WindowsIntegration: false);
            var remaining = Discover(verificationOptions);
            if (remaining.Count == 0)
            {
                report?.Invoke("Verified: no official Discord Android package or app-owned storage remains.");
            }
            else
            {
                report?.Invoke($"Verification found {remaining.Count} Android item(s) still present:");
                foreach (var item in remaining)
                {
                    if (failedLocations.Add(item.Location))
                    {
                        failed++;
                    }

                    report?.Invoke($"  Still present: {item.Description} - {item.Location}");
                }
            }
        }

        if (options.IosData)
        {
            var verificationOptions = new CleanupOptions(
                DesktopData: false,
                ChromeData: false,
                EdgeData: false,
                FirefoxData: false,
                AndroidData: false,
                IosData: true,
                WindowsIntegration: false);
            var remaining = Discover(verificationOptions);
            if (remaining.Count == 0)
            {
                report?.Invoke("Verified: the official Discord iOS app is no longer installed.");
            }
            else
            {
                report?.Invoke($"Verification found {remaining.Count} iOS item(s) still present:");
                foreach (var item in remaining)
                {
                    if (failedLocations.Add(item.Location))
                    {
                        failed++;
                    }

                    report?.Invoke($"  Still present: {item.Description} - {item.Location}");
                }
            }
        }

        return new CleanupResult(succeeded, failed, skipped);
    }

    private static List<CleanupItem> Discover(CleanupOptions options)
    {
        var items = new List<CleanupItem>();

        if (options.DesktopData)
        {
            DiscoverDesktopData(items);
        }

        if (options.ChromeData)
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            DiscoverChromiumBrowserData(
                items,
                "Chrome",
                Path.Combine(local, "Google", "Chrome", "User Data"));
        }

        if (options.EdgeData)
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            DiscoverChromiumBrowserData(
                items,
                "Edge",
                Path.Combine(local, "Microsoft", "Edge", "User Data"));
        }

        if (options.FirefoxData)
        {
            DiscoverFirefoxData(items);
        }

        if (options.AndroidData)
        {
            foreach (var target in AdbClient.Discover())
            {
                items.Add(new CleanupItem(
                    "Android (ADB)",
                    target.Description,
                    target.DisplayLocation,
                    target.Type,
                    target.TargetId));
            }
        }

        if (options.IosData)
        {
            foreach (var target in IosDeviceClient.Discover())
            {
                items.Add(new CleanupItem(
                    "iOS device",
                    target.Description,
                    target.DisplayLocation,
                    target.Type,
                    target.TargetId));
            }
        }

        if (options.WindowsIntegration)
        {
            DiscoverWindowsIntegration(items);
        }

        return items
            .DistinctBy(item => $"{item.Type}|{item.Location}", StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item.Category)
            .ThenBy(item => item.Location)
            .ToList();
    }

    private static string RequireTargetId(CleanupItem item)
    {
        return item.TargetId ?? throw new InvalidOperationException("The cleanup action is missing its protected target identifier.");
    }

    private static void DiscoverDesktopData(List<CleanupItem> items)
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var commonData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        foreach (var root in new[] { roaming, local, commonData })
        {
            foreach (var folderName in DiscordFolderNames)
            {
                AddDirectoryIfPresent(items, "Desktop app", "Discord application files and data", Path.Combine(root, folderName));
            }
        }

        foreach (var programsRoot in new[]
                 {
                     Path.Combine(local, "Programs"),
                     programFiles,
                     programFilesX86
                 }.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var folderName in DiscordFolderNames)
            {
                AddDirectoryIfPresent(items, "Desktop app", "Discord installation folder", Path.Combine(programsRoot, folderName));
                AddDirectoryIfPresent(
                    items,
                    "Desktop app",
                    "Discord installation folder",
                    Path.Combine(programsRoot, "Discord Inc", folderName));
            }
        }

        var packages = Path.Combine(local, "Packages");
        if (Directory.Exists(packages))
        {
            foreach (var directory in SafeEnumerateDirectories(packages))
            {
                var name = Path.GetFileName(directory);
                if (name.StartsWith("DiscordInc.Discord_", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("Discord.Discord_", StringComparison.OrdinalIgnoreCase))
                {
                    AddDirectoryIfPresent(items, "Desktop app", "Discord Microsoft Store data", directory);
                }
            }
        }

        var crashDumps = Path.Combine(local, "CrashDumps");
        AddMatchingFiles(items, "Desktop app", "Discord crash report", crashDumps, "Discord*.dmp");

        var temp = Path.GetTempPath();
        foreach (var folderName in DiscordFolderNames.Append("Discord Crashes"))
        {
            AddDirectoryIfPresent(items, "Desktop app", "Discord temporary data", Path.Combine(temp, folderName));
        }

        AddMatchingFiles(items, "Desktop app", "Discord temporary crash file", temp, "Discord*.dmp");

        var squirrelTemp = Path.Combine(local, "SquirrelTemp");
        if (Directory.Exists(squirrelTemp))
        {
            foreach (var path in SafeEnumerateDirectories(squirrelTemp)
                         .Where(path => IsDiscordNamedFileSystemEntry(Path.GetFileName(path))))
            {
                AddDirectoryIfPresent(items, "Desktop app", "Discord updater temporary data", path);
            }

            foreach (var path in SafeEnumerateFiles(squirrelTemp)
                         .Where(path => IsDiscordNamedFileSystemEntry(Path.GetFileName(path))))
            {
                items.Add(new CleanupItem("Desktop app", "Discord updater temporary file", path, CleanupItemType.File));
            }
        }
    }

    private static void DiscoverChromiumBrowserData(
        List<CleanupItem> items,
        string browserName,
        string userData)
    {
        if (!Directory.Exists(userData))
        {
            return;
        }

        foreach (var profile in SafeEnumerateDirectories(userData))
        {
            var profileName = Path.GetFileName(profile);
            var cookies = new[]
            {
                Path.Combine(profile, "Network", "Cookies"),
                Path.Combine(profile, "Cookies")
            };

            foreach (var cookieDatabase in cookies.Where(File.Exists))
            {
                items.Add(new CleanupItem(
                    browserName,
                    $"Discord cookies in {browserName} profile \"{profileName}\"",
                    cookieDatabase,
                    CleanupItemType.ChromiumCookies));
            }

            foreach (var container in new[]
                     {
                         Path.Combine(profile, "IndexedDB"),
                         Path.Combine(profile, "Storage", "default")
                     })
            {
                if (!Directory.Exists(container))
                {
                    continue;
                }

                foreach (var directory in SafeEnumerateDirectories(container))
                {
                    if (ContainsDiscordHost(Path.GetFileName(directory)))
                    {
                        AddDirectoryIfPresent(
                            items,
                            browserName,
                            $"Discord site storage in {browserName} profile \"{profileName}\"",
                            directory);
                    }
                }
            }
        }
    }

    private static void DiscoverFirefoxData(List<CleanupItem> items)
    {
        foreach (var profile in GetFirefoxProfiles())
        {
            var profileName = Path.GetFileName(profile);
            var cookies = Path.Combine(profile, "cookies.sqlite");
            if (File.Exists(cookies))
            {
                items.Add(new CleanupItem(
                    "Firefox",
                    $"Discord cookies in Firefox profile \"{profileName}\"",
                    cookies,
                    CleanupItemType.FirefoxCookies));
            }

            var legacyStorage = Path.Combine(profile, "webappsstore.sqlite");
            if (File.Exists(legacyStorage))
            {
                items.Add(new CleanupItem(
                    "Firefox",
                    $"Discord legacy site storage in Firefox profile \"{profileName}\"",
                    legacyStorage,
                    CleanupItemType.FirefoxLegacyStorage));
            }

            var permissions = Path.Combine(profile, "permissions.sqlite");
            if (File.Exists(permissions))
            {
                items.Add(new CleanupItem(
                    "Firefox",
                    $"Discord site permissions in Firefox profile \"{profileName}\"",
                    permissions,
                    CleanupItemType.FirefoxPermissions));
            }

            var storageRoot = Path.Combine(profile, "storage");
            foreach (var storageType in new[] { "default", "temporary", "permanent" })
            {
                var container = Path.Combine(storageRoot, storageType);
                foreach (var directory in SafeEnumerateDirectories(container))
                {
                    if (ContainsDiscordHost(Path.GetFileName(directory)))
                    {
                        AddDirectoryIfPresent(
                            items,
                            "Firefox",
                            $"Discord site storage in Firefox profile \"{profileName}\"",
                            directory);
                    }
                }
            }
        }
    }

    private static IEnumerable<string> GetFirefoxProfiles()
    {
        var firefoxRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Mozilla",
            "Firefox");
        var profiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var profile in SafeEnumerateDirectories(Path.Combine(firefoxRoot, "Profiles")))
        {
            profiles.Add(Path.GetFullPath(profile));
        }

        var profilesIni = Path.Combine(firefoxRoot, "profiles.ini");
        if (File.Exists(profilesIni))
        {
            try
            {
                foreach (var line in File.ReadLines(profilesIni))
                {
                    var trimmed = line.Trim();
                    var separator = trimmed.IndexOf('=');
                    if (separator <= 0)
                    {
                        continue;
                    }

                    var key = trimmed[..separator].Trim();
                    if (!key.Equals("Path", StringComparison.OrdinalIgnoreCase) &&
                        !key.Equals("Default", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var value = trimmed[(separator + 1)..].Trim().Replace('/', Path.DirectorySeparatorChar);
                    if (string.IsNullOrWhiteSpace(value) || value is "0" or "1")
                    {
                        continue;
                    }

                    var candidate = Path.IsPathRooted(value)
                        ? value
                        : Path.Combine(firefoxRoot, value);
                    if (Directory.Exists(candidate))
                    {
                        profiles.Add(Path.GetFullPath(candidate));
                    }
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                // Standard Firefox profiles already discovered above remain available.
            }
        }

        return profiles;
    }

    private static void DiscoverWindowsIntegration(List<CleanupItem> items)
    {
        DiscoverShortcuts(items);
        DiscoverRegistry(items);
    }

    private static void DiscoverShortcuts(List<CleanupItem> items)
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var userPrograms = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        var commonPrograms = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var commonDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        var recent = Environment.GetFolderPath(Environment.SpecialFolder.Recent);

        foreach (var programs in new[] { userPrograms, commonPrograms })
        {
            if (string.IsNullOrWhiteSpace(programs))
            {
                continue;
            }

            AddDirectoryIfPresent(items, "Windows", "Discord Start menu folder", Path.Combine(programs, "Discord Inc"));
            AddDirectoryIfPresent(items, "Windows", "Discord Start menu folder", Path.Combine(programs, "Discord"));
            AddMatchingFiles(items, "Windows", "Discord Start menu shortcut", programs, "Discord*.lnk");
        }

        var taskbar = Path.Combine(
            roaming,
            "Microsoft",
            "Internet Explorer",
            "Quick Launch",
            "User Pinned",
            "TaskBar");

        AddMatchingFiles(items, "Windows", "Discord taskbar shortcut", taskbar, "Discord*.lnk");
        AddMatchingFiles(items, "Windows", "Discord desktop shortcut", desktop, "Discord*.lnk");
        AddMatchingFiles(items, "Windows", "Discord public desktop shortcut", commonDesktop, "Discord*.lnk");
        AddMatchingFiles(items, "Windows", "Discord recent-item shortcut", recent, "Discord*.lnk");
    }

    private static void DiscoverRegistry(List<CleanupItem> items)
    {
        foreach (var scope in RegistryScopes)
        {
            using var root = OpenRegistryRoot(scope, writable: false);
            if (root is null)
            {
                continue;
            }

            foreach (var keyPath in DiscordRegistrationKeys)
            {
                AddRegistryKeyIfPresent(
                    items,
                    scope,
                    root,
                    "Discord application registration",
                    keyPath);
            }

            DiscoverUninstallRegistryKeys(items, scope, root);

            foreach (var keyPath in DiscordRegistryValueContainers)
            {
                AddMatchingRegistryValues(
                    items,
                    scope,
                    root,
                    keyPath,
                    keyPath.Contains("Run", StringComparison.OrdinalIgnoreCase)
                        ? "Discord startup registration"
                        : "Discord Windows registration");
            }
        }

        var machine64 = RegistryScopes.Single(scope =>
            scope.Hive == RegistryHive.LocalMachine && scope.View == RegistryView.Registry64);
        using var machineRoot = OpenRegistryRoot(machine64, writable: false);
        if (machineRoot is not null)
        {
            AddMatchingRegistryValues(
                items,
                machine64,
                machineRoot,
                @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallRules",
                "Discord firewall registration");
        }
    }

    private static void DiscoverUninstallRegistryKeys(
        List<CleanupItem> items,
        RegistryScope scope,
        RegistryKey root)
    {
        const string uninstallPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
        using var uninstall = TryOpenSubKey(root, uninstallPath, writable: false);
        if (uninstall is null)
        {
            return;
        }

        foreach (var subKeyName in SafeGetSubKeyNames(uninstall))
        {
            try
            {
                using var appKey = uninstall.OpenSubKey(subKeyName, writable: false);
                var displayName = appKey?.GetValue("DisplayName") as string;
                if (!IsOfficialDiscordProduct(displayName) && !IsOfficialDiscordProduct(subKeyName))
                {
                    continue;
                }

                items.Add(new CleanupItem(
                    "Registry",
                    $"Discord uninstall registration ({scope.DisplayName})",
                    $"{scope.Prefix}\\{uninstallPath}\\{subKeyName}",
                    CleanupItemType.Registry));
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                // Continue scanning other uninstall registrations.
            }
        }
    }

    private static void AddRegistryKeyIfPresent(
        List<CleanupItem> items,
        RegistryScope scope,
        RegistryKey root,
        string description,
        string subKey)
    {
        using var key = TryOpenSubKey(root, subKey, writable: false);
        if (key is null)
        {
            return;
        }

        items.Add(new CleanupItem(
            "Registry",
            $"{description} ({scope.DisplayName})",
            $"{scope.Prefix}\\{subKey}",
            CleanupItemType.Registry));
    }

    private static void AddMatchingRegistryValues(
        List<CleanupItem> items,
        RegistryScope scope,
        RegistryKey root,
        string subKey,
        string description)
    {
        using var key = TryOpenSubKey(root, subKey, writable: false);
        if (key is null)
        {
            return;
        }

        foreach (var valueName in SafeGetValueNames(key))
        {
            try
            {
                var value = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (!IsOfficialDiscordRegistryValue(valueName, value))
                {
                    continue;
                }

                items.Add(new CleanupItem(
                    "Registry",
                    $"{description} ({scope.DisplayName})",
                    $"{scope.Prefix}\\{subKey}|{valueName}",
                    CleanupItemType.Registry));
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                // Continue scanning other values.
            }
        }
    }

    private static bool DeleteRegistryEntry(string location)
    {
        var slash = location.IndexOf('\\');
        if (slash <= 0)
        {
            throw new InvalidOperationException("The registry cleanup target is invalid.");
        }

        var prefix = location[..slash];
        var scope = RegistryScopes.SingleOrDefault(candidate =>
            candidate.Prefix.Equals(prefix, StringComparison.OrdinalIgnoreCase));
        if (scope is null)
        {
            throw new InvalidOperationException("The registry cleanup target is outside the approved registry scopes.");
        }

        var target = location[(slash + 1)..];
        var valueSeparator = target.IndexOf('|');
        using var root = OpenRegistryRoot(scope, writable: true)
                         ?? throw new UnauthorizedAccessException($"Could not open {scope.DisplayName} registry for cleanup.");

        if (valueSeparator >= 0)
        {
            var keyPath = target[..valueSeparator];
            var valueName = target[(valueSeparator + 1)..];
            using var key = TryOpenSubKey(root, keyPath, writable: true);
            if (key is null || !SafeGetValueNames(key).Contains(valueName, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            key.DeleteValue(valueName, throwOnMissingValue: false);
            return true;
        }

        using (var existing = TryOpenSubKey(root, target, writable: false))
        {
            if (existing is null)
            {
                return false;
            }
        }

        root.DeleteSubKeyTree(target, throwOnMissingSubKey: false);
        return true;
    }

    private static RegistryKey? OpenRegistryRoot(RegistryScope scope, bool writable)
    {
        try
        {
            var root = RegistryKey.OpenBaseKey(scope.Hive, scope.View);
            if (!writable)
            {
                return root;
            }

            return root;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }

    private static RegistryKey? TryOpenSubKey(RegistryKey root, string path, bool writable)
    {
        try
        {
            return root.OpenSubKey(path, writable);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return null;
        }
    }

    private static string[] SafeGetSubKeyNames(RegistryKey key)
    {
        try
        {
            return key.GetSubKeyNames();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return [];
        }
    }

    private static string[] SafeGetValueNames(RegistryKey key)
    {
        try
        {
            return key.GetValueNames();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return [];
        }
    }

    private static bool IsOfficialDiscordRegistryValue(string valueName, object? value)
    {
        if (IsOfficialDiscordProduct(valueName) || DiscordExecutableNames.Contains(valueName, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        if (value is not string text)
        {
            return false;
        }

        if (IsOfficialDiscordProduct(text))
        {
            return true;
        }

        var containsExecutable = DiscordExecutableNames.Any(executable =>
            text.Contains(executable, StringComparison.OrdinalIgnoreCase));
        var containsOfficialPath = DiscordFolderNames.Any(folder =>
            text.Contains($"\\{folder}\\", StringComparison.OrdinalIgnoreCase));

        return containsExecutable && containsOfficialPath;
    }

    private static bool IsOfficialDiscordProduct(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = new string(value.Where(char.IsLetterOrDigit).ToArray());
        return normalized.Equals("Discord", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("DiscordCanary", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("DiscordPTB", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("DiscordDevelopment", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool DeleteChromiumDiscordCookies(string databasePath, Action<string>? report)
    {
        if (!File.Exists(databasePath))
        {
            return false;
        }

        const string sql = """
            BEGIN IMMEDIATE;
            DELETE FROM cookies
            WHERE lower(host_key) = 'discord.com'
               OR lower(host_key) LIKE '%.discord.com'
               OR lower(host_key) = 'discord.gg'
               OR lower(host_key) LIKE '%.discord.gg'
               OR lower(host_key) = 'discordapp.com'
               OR lower(host_key) LIKE '%.discordapp.com'
               OR lower(host_key) = 'discordapp.net'
               OR lower(host_key) LIKE '%.discordapp.net'
               OR lower(host_key) = 'discord.media'
               OR lower(host_key) LIKE '%.discord.media'
               OR lower(host_key) = 'discordcdn.com'
               OR lower(host_key) LIKE '%.discordcdn.com';
            COMMIT;
            """;

        var rows = ExecuteBrowserDatabaseCleanup(databasePath, sql);
        report?.Invoke($"  Removed {rows} Discord cookie record(s).");
        return rows > 0;
    }

    internal static bool DeleteFirefoxDiscordCookies(string databasePath, Action<string>? report)
    {
        if (!File.Exists(databasePath))
        {
            return false;
        }

        const string sql = """
            BEGIN IMMEDIATE;
            DELETE FROM moz_cookies
            WHERE lower(host) = 'discord.com'
               OR lower(host) LIKE '%.discord.com'
               OR lower(host) = 'discord.gg'
               OR lower(host) LIKE '%.discord.gg'
               OR lower(host) = 'discordapp.com'
               OR lower(host) LIKE '%.discordapp.com'
               OR lower(host) = 'discordapp.net'
               OR lower(host) LIKE '%.discordapp.net'
               OR lower(host) = 'discord.media'
               OR lower(host) LIKE '%.discord.media'
               OR lower(host) = 'discordcdn.com'
               OR lower(host) LIKE '%.discordcdn.com';
            COMMIT;
            """;

        var rows = ExecuteBrowserDatabaseCleanup(databasePath, sql);
        report?.Invoke($"  Removed {rows} Firefox Discord cookie record(s).");
        return rows > 0;
    }

    internal static bool DeleteFirefoxLegacyStorage(string databasePath, Action<string>? report)
    {
        if (!File.Exists(databasePath))
        {
            return false;
        }

        const string sql = """
            BEGIN IMMEDIATE;
            DELETE FROM webappsstore2
            WHERE lower(originKey) LIKE '%moc.drocsid%'
               OR lower(originKey) LIKE '%gg.drocsid%'
               OR lower(originKey) LIKE '%moc.ppadrocsid%'
               OR lower(originKey) LIKE '%ten.ppadrocsid%'
               OR lower(originKey) LIKE '%aidem.drocsid%'
               OR lower(originKey) LIKE '%moc.ndcdrocsid%';
            COMMIT;
            """;

        var rows = ExecuteBrowserDatabaseCleanup(databasePath, sql);
        report?.Invoke($"  Removed {rows} Firefox Discord legacy storage record(s).");
        return rows > 0;
    }

    internal static bool DeleteFirefoxPermissions(string databasePath, Action<string>? report)
    {
        if (!File.Exists(databasePath))
        {
            return false;
        }

        const string sql = """
            BEGIN IMMEDIATE;
            DELETE FROM moz_perms
            WHERE lower(origin) = 'https://discord.com'
               OR lower(origin) LIKE 'https://%.discord.com%'
               OR lower(origin) = 'https://discord.gg'
               OR lower(origin) LIKE 'https://%.discord.gg%'
               OR lower(origin) = 'https://discordapp.com'
               OR lower(origin) LIKE 'https://%.discordapp.com%'
               OR lower(origin) = 'https://discordapp.net'
               OR lower(origin) LIKE 'https://%.discordapp.net%'
               OR lower(origin) = 'https://discord.media'
               OR lower(origin) LIKE 'https://%.discord.media%'
               OR lower(origin) = 'https://discordcdn.com'
               OR lower(origin) LIKE 'https://%.discordcdn.com%';
            COMMIT;
            """;

        var rows = ExecuteBrowserDatabaseCleanup(databasePath, sql);
        report?.Invoke($"  Removed {rows} Firefox Discord permission record(s).");
        return rows > 0;
    }

    private static int ExecuteBrowserDatabaseCleanup(string databasePath, string sql)
    {
        EnsureBrowserForDatabaseIsClosed(databasePath);
        EnsureNoPendingSqliteJournal(databasePath);

        var stagingParent = Path.Combine(Path.GetTempPath(), "DiscordTraceRemover", "browser-databases");
        var databaseRoot = Path.GetPathRoot(Path.GetFullPath(databasePath));
        var stagingRoot = Path.GetPathRoot(Path.GetFullPath(stagingParent));
        if (!string.Equals(databaseRoot, stagingRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The browser database and temporary folder are on different drives, so an atomic update is not possible.");
        }

        Directory.CreateDirectory(stagingParent);
        var operationDirectory = Path.Combine(stagingParent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(operationDirectory);
        var workingDatabase = Path.Combine(operationDirectory, "browser.sqlite");

        try
        {
            var originalHash = HashFile(databasePath);
            File.Copy(databasePath, workingDatabase, overwrite: false);
            if (!CryptographicOperations.FixedTimeEquals(originalHash, HashFile(workingDatabase)))
            {
                throw new IOException("The browser database changed while its safe working copy was being created.");
            }

            var changes = NativeSqlite.Execute(workingDatabase, sql);
            EnsureNoPendingSqliteJournal(workingDatabase);

            EnsureBrowserForDatabaseIsClosed(databasePath);
            EnsureNoPendingSqliteJournal(databasePath);
            if (!CryptographicOperations.FixedTimeEquals(originalHash, HashFile(databasePath)))
            {
                throw new IOException(
                    "The browser database changed during cleanup. Keep the browser closed and try again.");
            }

            File.Replace(workingDatabase, databasePath, null, ignoreMetadataErrors: true);
            return changes;
        }
        catch (UnauthorizedAccessException ex)
        {
            throw CreateBrowserProtectionException(ex);
        }
        finally
        {
            SafeDeleteBrowserStaging(operationDirectory, stagingParent);
        }
    }

    private static UnauthorizedAccessException CreateBrowserProtectionException(UnauthorizedAccessException inner)
    {
        var executable = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "DiscordTraceRemover.exe");
        if (IsEsetSecurityInstalled())
        {
            return new UnauthorizedAccessException(
                "ESET Enhanced data protection blocked the browser profile. In ESET, open Advanced setup (F5) > " +
                "Protections > Browser protection > Browser protection allowlist, add this cleaner, then retry. " +
                $"Cleaner path: {executable}",
                inner);
        }

        return new UnauthorizedAccessException(
            "Security software blocked the browser profile. Add this cleaner to its browser-protection allowlist, " +
            $"then retry. Cleaner path: {executable}",
            inner);
    }

    private static bool IsEsetSecurityInstalled()
    {
        foreach (var programFiles in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                 }.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(Path.Combine(programFiles, "ESET", "ESET Security", "ekrn.exe")) ||
                Directory.Exists(Path.Combine(programFiles, "ESET", "ESET Security")))
            {
                return true;
            }
        }

        return false;
    }

    private static void EnsureBrowserForDatabaseIsClosed(string databasePath)
    {
        var fullPath = Path.GetFullPath(databasePath);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var chromeRoot = Path.Combine(local, "Google", "Chrome", "User Data");
        var edgeRoot = Path.Combine(local, "Microsoft", "Edge", "User Data");
        var firefoxRoot = Path.Combine(roaming, "Mozilla", "Firefox");

        if (IsPathWithin(fullPath, chromeRoot) && IsProcessRunning("chrome"))
        {
            throw new BrowserIsRunningException("Google Chrome");
        }

        if (IsPathWithin(fullPath, edgeRoot) && IsProcessRunning("msedge"))
        {
            throw new BrowserIsRunningException("Microsoft Edge");
        }

        if (IsPathWithin(fullPath, firefoxRoot) && IsProcessRunning("firefox"))
        {
            throw new BrowserIsRunningException("Mozilla Firefox");
        }
    }

    private static void EnsureNoPendingSqliteJournal(string databasePath)
    {
        foreach (var suffix in new[] { "-wal", "-journal" })
        {
            var sidecar = databasePath + suffix;
            if (File.Exists(sidecar) && new FileInfo(sidecar).Length > 0)
            {
                throw new IOException(
                    "The browser database still has pending changes. Keep the browser closed for a few seconds and try again.");
            }
        }
    }

    private static byte[] HashFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return SHA256.HashData(stream);
    }

    private static void SafeDeleteBrowserStaging(string operationDirectory, string stagingParent)
    {
        if (!Directory.Exists(operationDirectory))
        {
            return;
        }

        var approvedRoot = Path.GetFullPath(stagingParent).TrimEnd(Path.DirectorySeparatorChar) +
                           Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(operationDirectory);
        if (!target.StartsWith(approvedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refused to remove a browser working folder outside the approved temporary root.");
        }

        Directory.Delete(target, recursive: true);
    }

    private static bool IsProcessRunning(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static void StopBrowser(string processName, string displayName, Action<string>? report)
    {
        var initialProcesses = Process.GetProcessesByName(processName);
        if (initialProcesses.Length == 0)
        {
            return;
        }

        try
        {
            foreach (var process in initialProcesses)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.CloseMainWindow();
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // The process exited while it was being inspected.
                }
            }

            Thread.Sleep(1_200);
        }
        finally
        {
            foreach (var process in initialProcesses)
            {
                process.Dispose();
            }
        }

        var forced = false;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var remaining = Process.GetProcessesByName(processName);
            if (remaining.Length == 0)
            {
                break;
            }

            forced = true;
            try
            {
                foreach (var process in remaining)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill(entireProcessTree: true);
                        }
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                    {
                        // A final process check below determines whether cleanup can continue safely.
                    }
                }

                foreach (var process in remaining)
                {
                    try
                    {
                        process.WaitForExit(2_000);
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                    {
                        // A final process check below determines whether cleanup can continue safely.
                    }
                }
            }
            finally
            {
                foreach (var process in remaining)
                {
                    process.Dispose();
                }
            }

            Thread.Sleep(200);
        }

        if (IsProcessRunning(processName))
        {
            throw new BrowserIsRunningException(displayName);
        }

        report?.Invoke(forced ? $"Force-closed {displayName}." : $"Closed {displayName}.");
    }

    private static void StopDiscord(Action<string>? report)
    {
        var candidates = new Dictionary<int, Process>();
        foreach (var processName in DiscordProcessNames)
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                if (!candidates.TryAdd(process.Id, process))
                {
                    process.Dispose();
                }
            }
        }

        foreach (var process in Process.GetProcessesByName("Update"))
        {
            try
            {
                var executable = process.MainModule?.FileName;
                if (executable is not null && IsOfficialDiscordInstallPath(executable))
                {
                    if (!candidates.TryAdd(process.Id, process))
                    {
                        process.Dispose();
                    }
                }
                else
                {
                    process.Dispose();
                }
            }
            catch
            {
                process.Dispose();
            }
        }

        foreach (var process in candidates.Values)
        {
            try
            {
                var name = process.ProcessName;
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5_000);
                report?.Invoke($"Closed {name}.");
            }
            catch (Exception ex)
            {
                report?.Invoke($"Could not close Discord process: {ex.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static void RunDiscordUninstallers(Action<string>? report)
    {
        foreach (var installRoot in GetOfficialInstallRoots())
        {
            var update = Path.Combine(installRoot, "Update.exe");
            if (!File.Exists(update))
            {
                continue;
            }

            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = update,
                    Arguments = "--uninstall -s",
                    WorkingDirectory = installRoot,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (process is null)
                {
                    continue;
                }

                if (!process.WaitForExit(20_000))
                {
                    report?.Invoke($"Discord uninstaller is still running for {installRoot}; continuing cleanup.");
                }
                else
                {
                    report?.Invoke($"Ran Discord's uninstaller in {installRoot}.");
                }
            }
            catch (Exception ex)
            {
                report?.Invoke($"Could not run Discord's uninstaller in {installRoot}: {ex.Message}");
            }
        }
    }

    private static IEnumerable<string> GetOfficialInstallRoots()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        foreach (var root in new[]
                 {
                     local,
                     Path.Combine(local, "Programs"),
                     programFiles,
                     programFilesX86
                 }.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var folder in DiscordFolderNames.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                yield return Path.Combine(root, folder);
                yield return Path.Combine(root, "Discord Inc", folder);
            }
        }
    }

    private static bool IsOfficialDiscordInstallPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return GetOfficialInstallRoots().Any(root => IsPathWithin(fullPath, root));
    }

    private static bool IsPathWithin(string path, string parent)
    {
        var fullParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.StartsWith(fullParent, StringComparison.OrdinalIgnoreCase);
    }

    private static bool DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return false;
        }

        var rootAttributes = File.GetAttributes(path);
        if (rootAttributes.HasFlag(FileAttributes.ReparsePoint))
        {
            Directory.Delete(path, recursive: false);
            return true;
        }

        ClearReadOnlyAttributes(path);
        Directory.Delete(path, recursive: true);
        return true;
    }

    private static bool DeleteFile(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        File.SetAttributes(path, FileAttributes.Normal);
        File.Delete(path);
        return true;
    }

    private static void ClearReadOnlyAttributes(string root)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        foreach (var file in Directory.EnumerateFiles(root, "*", options))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        foreach (var directory in Directory.EnumerateDirectories(root, "*", options))
        {
            var attributes = File.GetAttributes(directory);
            File.SetAttributes(directory, attributes & ~FileAttributes.ReadOnly);
        }
    }

    private static bool ContainsDiscordHost(string value)
    {
        return DiscordHostTokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsDiscordNamedFileSystemEntry(string value)
    {
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(value);
        return DiscordFolderNames.Any(product =>
            nameWithoutExtension.Equals(product, StringComparison.OrdinalIgnoreCase) ||
            nameWithoutExtension.StartsWith(product + "-", StringComparison.OrdinalIgnoreCase) ||
            nameWithoutExtension.StartsWith(product + "_", StringComparison.OrdinalIgnoreCase));
    }

    private static void AddDirectoryIfPresent(
        List<CleanupItem> items,
        string category,
        string description,
        string path)
    {
        if (Directory.Exists(path))
        {
            items.Add(new CleanupItem(category, description, path, CleanupItemType.Directory));
        }
    }

    private static void AddMatchingFiles(
        List<CleanupItem> items,
        string category,
        string description,
        string parent,
        string pattern)
    {
        if (!Directory.Exists(parent))
        {
            return;
        }

        foreach (var path in SafeEnumerateFiles(parent, pattern))
        {
            items.Add(new CleanupItem(category, description, path, CleanupItemType.File));
        }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string parent, string pattern = "*")
    {
        try
        {
            return Directory.EnumerateDirectories(parent, pattern, SearchOption.TopDirectoryOnly).ToArray();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return [];
        }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string parent, string pattern = "*")
    {
        try
        {
            return Directory.EnumerateFiles(parent, pattern, SearchOption.TopDirectoryOnly).ToArray();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return [];
        }
    }
}
