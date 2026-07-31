using System.Diagnostics;
using System.Text;

namespace DiscordTraceRemover;

internal sealed record AdbCleanupTarget(
    string Description,
    string DisplayLocation,
    CleanupItemType Type,
    string TargetId);

internal static class AdbClient
{
    private sealed record AdbDevice(string Serial, string DisplayName);
    private sealed record CommandResult(int ExitCode, string Output, string Error);

    private const string OfficialDiscordPackage = "com.discord";

    internal static bool IsAvailable()
    {
        try
        {
            _ = FindAdb();
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    internal static IReadOnlyList<AdbCleanupTarget> Discover()
    {
        var adb = FindAdb();
        var device = GetSingleDevice(adb);
        var targets = new List<AdbCleanupTarget>();

        if (IsPackageInstalled(adb, device.Serial, OfficialDiscordPackage))
        {
            targets.Add(new AdbCleanupTarget(
                "Official Discord Android app and private data",
                $"{device.DisplayName} ({device.Serial}) - {OfficialDiscordPackage}",
                CleanupItemType.AndroidPackage,
                EncodeTarget(device.Serial, OfficialDiscordPackage)));
        }

        foreach (var path in GetApprovedExternalPaths())
        {
            if (!RemoteDirectoryExists(adb, device.Serial, path))
            {
                continue;
            }

            targets.Add(new AdbCleanupTarget(
                "Discord Android app-owned storage",
                $"{device.DisplayName} ({device.Serial}) - {path}",
                CleanupItemType.AndroidDirectory,
                EncodeTarget(device.Serial, path)));
        }

        return targets;
    }

    internal static bool UninstallPackage(string targetId, Action<string>? report)
    {
        var (serial, packageName) = DecodeTarget(targetId);
        if (!packageName.Equals(OfficialDiscordPackage, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("ADB cleanup refused an unapproved Android package name.");
        }

        var adb = FindAdb();
        EnsureSelectedDevice(adb, serial);
        if (!IsPackageInstalled(adb, serial, packageName))
        {
            return false;
        }

        Run(adb, ["-s", serial, "shell", "am", "force-stop", packageName], allowFailure: true);
        var uninstall = Run(adb, ["-s", serial, "uninstall", packageName], allowFailure: true);
        if (uninstall.ExitCode != 0 || !uninstall.Output.Contains("Success", StringComparison.OrdinalIgnoreCase))
        {
            var perUser = Run(
                adb,
                ["-s", serial, "shell", "pm", "uninstall", "--user", "0", packageName],
                allowFailure: true);
            if (perUser.ExitCode != 0 || !perUser.Output.Contains("Success", StringComparison.OrdinalIgnoreCase))
            {
                var details = FirstUsefulMessage(uninstall, perUser);
                throw new InvalidOperationException($"Android could not uninstall {packageName}. {details}");
            }
        }

        report?.Invoke($"Uninstalled {packageName} from Android device {serial}.");
        return true;
    }

    internal static bool DeleteExternalDirectory(string targetId, Action<string>? report)
    {
        var (serial, path) = DecodeTarget(targetId);
        if (!GetApprovedExternalPaths().Contains(path, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("ADB cleanup refused an unapproved Android storage path.");
        }

        var adb = FindAdb();
        EnsureSelectedDevice(adb, serial);
        if (!RemoteDirectoryExists(adb, serial, path))
        {
            return false;
        }

        var deletion = Run(adb, ["-s", serial, "shell", "rm", "-rf", path], allowFailure: true);
        if (deletion.ExitCode != 0 || RemoteDirectoryExists(adb, serial, path))
        {
            var details = FirstUsefulMessage(deletion);
            throw new InvalidOperationException($"Android could not remove {path}. {details}");
        }

        report?.Invoke($"Removed Android Discord storage: {path}");
        return true;
    }

    internal static void RunTargetingSelfTest()
    {
        const string serial = "emulator-5554";
        var packageTarget = EncodeTarget(serial, OfficialDiscordPackage);
        var decodedPackage = DecodeTarget(packageTarget);
        if (decodedPackage.Serial != serial || decodedPackage.Value != OfficialDiscordPackage)
        {
            throw new InvalidOperationException("ADB package target encoding test failed.");
        }

        var path = GetApprovedExternalPaths()[0];
        var pathTarget = EncodeTarget(serial, path);
        var decodedPath = DecodeTarget(pathTarget);
        if (decodedPath.Serial != serial || decodedPath.Value != path)
        {
            throw new InvalidOperationException("ADB directory target encoding test failed.");
        }
    }

    private static string FindAdb()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = new List<string>
        {
            MobileToolInstaller.AndroidAdbPath,
            Path.Combine(AppContext.BaseDirectory, "platform-tools", "adb.exe"),
            Path.Combine(AppContext.BaseDirectory, "adb.exe"),
            Path.Combine(local, "Android", "Sdk", "platform-tools", "adb.exe")
        };

        foreach (var variable in new[] { "ANDROID_SDK_ROOT", "ANDROID_HOME" })
        {
            var sdkRoot = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(sdkRoot))
            {
                candidates.Add(Path.Combine(sdkRoot, "platform-tools", "adb.exe"));
            }
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathValue))
        {
            foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    candidates.Add(Path.Combine(directory.Trim(), "adb.exe"));
                }
                catch
                {
                    // Ignore malformed PATH entries and continue through known locations.
                }
            }
        }

        var adb = candidates
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(File.Exists);

        return adb ?? throw new InvalidOperationException(
            "Android Platform Tools (adb.exe) was not found. Install Google's Platform Tools, " +
            "add adb to PATH, or place the platform-tools folder next to this cleaner.");
    }

    private static AdbDevice GetSingleDevice(string adb)
    {
        var result = Run(adb, ["devices", "-l"]);
        var devices = new List<(string Serial, string State, string Model)>();

        foreach (var rawLine in result.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase) || line.StartsWith('*'))
            {
                continue;
            }

            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            var modelPart = parts.FirstOrDefault(part => part.StartsWith("model:", StringComparison.OrdinalIgnoreCase));
            var model = modelPart is null ? "Android device" : modelPart["model:".Length..].Replace('_', ' ');
            devices.Add((parts[0], parts[1], model));
        }

        var authorized = devices.Where(device => device.State.Equals("device", StringComparison.OrdinalIgnoreCase)).ToList();
        if (authorized.Count == 0)
        {
            if (devices.Any(device => device.State.Equals("unauthorized", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    "The Android device has not authorized this computer. Unlock the phone and accept the USB debugging prompt.");
            }

            if (devices.Any(device => device.State.Equals("offline", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    "The Android device is offline. Reconnect it and confirm USB debugging is enabled.");
            }

            throw new InvalidOperationException(
                "No authorized Android device was found. Connect one device and enable USB debugging in Developer options.");
        }

        if (authorized.Count > 1)
        {
            throw new InvalidOperationException(
                "More than one Android device is connected. Disconnect the devices you do not want to clean, then preview again.");
        }

        var selected = authorized[0];
        return new AdbDevice(selected.Serial, selected.Model);
    }

    private static void EnsureSelectedDevice(string adb, string expectedSerial)
    {
        var device = GetSingleDevice(adb);
        if (!device.Serial.Equals(expectedSerial, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The connected Android device changed after Preview. Preview again before cleaning.");
        }
    }

    private static bool IsPackageInstalled(string adb, string serial, string packageName)
    {
        var result = Run(
            adb,
            ["-s", serial, "shell", "pm", "list", "packages", packageName],
            allowFailure: true);
        return result.ExitCode == 0 && result.Output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Any(line => line.Equals($"package:{packageName}", StringComparison.Ordinal));
    }

    private static bool RemoteDirectoryExists(string adb, string serial, string path)
    {
        var result = Run(adb, ["-s", serial, "shell", "test", "-d", path], allowFailure: true);
        return result.ExitCode == 0;
    }

    private static string[] GetApprovedExternalPaths()
    {
        return
        [
            $"/sdcard/Android/data/{OfficialDiscordPackage}",
            $"/sdcard/Android/media/{OfficialDiscordPackage}",
            $"/sdcard/Android/obb/{OfficialDiscordPackage}"
        ];
    }

    private static string EncodeTarget(string serial, string value)
    {
        return $"{Convert.ToBase64String(Encoding.UTF8.GetBytes(serial))}." +
               Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    }

    private static (string Serial, string Value) DecodeTarget(string targetId)
    {
        var separator = targetId.IndexOf('.');
        if (separator <= 0 || separator == targetId.Length - 1)
        {
            throw new InvalidOperationException("The ADB cleanup target is invalid.");
        }

        try
        {
            var serial = Encoding.UTF8.GetString(Convert.FromBase64String(targetId[..separator]));
            var value = Encoding.UTF8.GetString(Convert.FromBase64String(targetId[(separator + 1)..]));
            if (string.IsNullOrWhiteSpace(serial) || string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException("The ADB cleanup target is empty.");
            }

            return (serial, value);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("The ADB cleanup target is invalid.", ex);
        }
    }

    private static CommandResult Run(string adb, IReadOnlyList<string> arguments, bool allowFailure = false)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = adb,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        if (!process.Start())
        {
            throw new InvalidOperationException("ADB could not be started.");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(20_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("ADB did not respond within 20 seconds.");
        }

        var output = outputTask.GetAwaiter().GetResult().Trim();
        var error = errorTask.GetAwaiter().GetResult().Trim();
        var result = new CommandResult(process.ExitCode, output, error);
        if (!allowFailure && result.ExitCode != 0)
        {
            throw new InvalidOperationException($"ADB failed: {FirstUsefulMessage(result)}");
        }

        return result;
    }

    private static string FirstUsefulMessage(params CommandResult[] results)
    {
        return results
                   .SelectMany(result => new[] { result.Error, result.Output })
                   .FirstOrDefault(message => !string.IsNullOrWhiteSpace(message))
               ?? "No additional details were returned.";
    }
}
