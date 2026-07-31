using System.Diagnostics;
using System.Text;

namespace DiscordTraceRemover;

internal sealed record IosCleanupTarget(
    string Description,
    string DisplayLocation,
    CleanupItemType Type,
    string TargetId);

internal static class IosDeviceClient
{
    private sealed record IosTools(string DeviceId, string Installer);
    private sealed record CommandResult(int ExitCode, string Output, string Error);

    private const string OfficialDiscordBundleId = "com.hammerandchisel.discord";

    internal static bool IsAvailable()
    {
        try
        {
            _ = FindTools();
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    internal static IReadOnlyList<IosCleanupTarget> Discover()
    {
        var tools = FindTools();
        var udid = GetSingleDevice(tools.DeviceId);
        if (!IsDiscordInstalled(tools.Installer, udid))
        {
            return [];
        }

        return
        [
            new IosCleanupTarget(
                "Official Discord iOS app and sandbox data",
                $"iOS device ({ShortDeviceId(udid)}) - {OfficialDiscordBundleId}",
                CleanupItemType.IosPackage,
                EncodeTarget(udid, OfficialDiscordBundleId))
        ];
    }

    internal static bool UninstallDiscord(string targetId, Action<string>? report)
    {
        var (udid, bundleId) = DecodeTarget(targetId);
        if (!bundleId.Equals(OfficialDiscordBundleId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("iOS cleanup refused an unapproved bundle identifier.");
        }

        var tools = FindTools();
        var connectedUdid = GetSingleDevice(tools.DeviceId);
        if (!connectedUdid.Equals(udid, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The connected iOS device changed after Preview. Preview again before cleaning.");
        }

        if (!IsDiscordInstalled(tools.Installer, udid))
        {
            return false;
        }

        var result = Run(
            tools.Installer,
            ["--udid", udid, "uninstall", bundleId],
            allowFailure: true,
            timeoutMilliseconds: 60_000);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"iOS could not uninstall Discord. {FirstUsefulMessage(result)}");
        }

        if (IsDiscordInstalled(tools.Installer, udid))
        {
            throw new InvalidOperationException("iOS still reports Discord as installed after the uninstall command.");
        }

        report?.Invoke($"Uninstalled {bundleId} from iOS device {ShortDeviceId(udid)}.");
        return true;
    }

    internal static void RunTargetingSelfTest()
    {
        const string udid = "00008110-001234560123401E";
        var target = EncodeTarget(udid, OfficialDiscordBundleId);
        var decoded = DecodeTarget(target);
        if (decoded.Udid != udid || decoded.BundleId != OfficialDiscordBundleId)
        {
            throw new InvalidOperationException("iOS target encoding test failed.");
        }

        if (ShortDeviceId(udid).Contains(udid, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("iOS device identifier masking test failed.");
        }

        if (!IsOfficialDiscordBundleLine($"{OfficialDiscordBundleId}, 300, Discord") ||
            IsOfficialDiscordBundleLine($"{OfficialDiscordBundleId}.local, 300, Discord Local"))
        {
            throw new InvalidOperationException("iOS bundle identifier matching test failed.");
        }
    }

    private static IosTools FindTools()
    {
        var deviceId = FindTool("idevice_id.exe");
        var installer = FindTool("ideviceinstaller.exe");
        if (deviceId is null || installer is null)
        {
            throw new InvalidOperationException(
                "iOS support requires idevice_id.exe and ideviceinstaller.exe from libimobiledevice. " +
                "Add them to PATH or place the complete libimobiledevice tools folder next to this cleaner.");
        }

        return new IosTools(deviceId, installer);
    }

    private static string? FindTool(string fileName)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var candidates = new List<string>
        {
            Path.Combine(MobileToolInstaller.IosToolsRoot, fileName),
            Path.Combine(AppContext.BaseDirectory, fileName),
            Path.Combine(AppContext.BaseDirectory, "libimobiledevice", fileName),
            Path.Combine(AppContext.BaseDirectory, "libimobiledevice", "bin", fileName),
            Path.Combine(local, "libimobiledevice", fileName),
            Path.Combine(local, "libimobiledevice", "bin", fileName)
        };

        foreach (var root in new[] { programFiles, programFilesX86 }.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            candidates.Add(Path.Combine(root, "libimobiledevice", fileName));
            candidates.Add(Path.Combine(root, "libimobiledevice", "bin", fileName));
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathValue))
        {
            foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    candidates.Add(Path.Combine(directory.Trim(), fileName));
                }
                catch
                {
                    // Ignore malformed PATH entries.
                }
            }
        }

        return candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(File.Exists);
    }

    private static string GetSingleDevice(string deviceIdTool)
    {
        var result = Run(deviceIdTool, ["--list"], allowFailure: true);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"The iOS device service could not list devices. {FirstUsefulMessage(result)}");
        }

        var devices = result.Output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (devices.Count == 0)
        {
            throw new InvalidOperationException(
                "No trusted iOS device was found. Connect and unlock one iPhone or iPad, then tap Trust when prompted.");
        }

        if (devices.Count > 1)
        {
            throw new InvalidOperationException(
                "More than one iOS device is connected. Disconnect devices you do not want to clean, then preview again.");
        }

        return devices[0];
    }

    private static bool IsDiscordInstalled(string installer, string udid)
    {
        var result = Run(installer, ["--udid", udid, "list"], allowFailure: true, timeoutMilliseconds: 45_000);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                "The iOS app service could not read installed apps. Unlock the device, confirm Trust, " +
                $"and verify Apple device drivers are installed. {FirstUsefulMessage(result)}");
        }

        return result.Output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Any(IsOfficialDiscordBundleLine);
    }

    private static bool IsOfficialDiscordBundleLine(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Equals(OfficialDiscordBundleId, StringComparison.Ordinal))
        {
            return true;
        }

        if (!trimmed.StartsWith(OfficialDiscordBundleId, StringComparison.Ordinal) ||
            trimmed.Length == OfficialDiscordBundleId.Length)
        {
            return false;
        }

        return trimmed[OfficialDiscordBundleId.Length] is ',' or ' ' or '\t';
    }

    private static string EncodeTarget(string udid, string bundleId)
    {
        return $"{Convert.ToBase64String(Encoding.UTF8.GetBytes(udid))}." +
               Convert.ToBase64String(Encoding.UTF8.GetBytes(bundleId));
    }

    private static (string Udid, string BundleId) DecodeTarget(string targetId)
    {
        var separator = targetId.IndexOf('.');
        if (separator <= 0 || separator == targetId.Length - 1)
        {
            throw new InvalidOperationException("The iOS cleanup target is invalid.");
        }

        try
        {
            var udid = Encoding.UTF8.GetString(Convert.FromBase64String(targetId[..separator]));
            var bundleId = Encoding.UTF8.GetString(Convert.FromBase64String(targetId[(separator + 1)..]));
            if (string.IsNullOrWhiteSpace(udid) || string.IsNullOrWhiteSpace(bundleId))
            {
                throw new InvalidOperationException("The iOS cleanup target is empty.");
            }

            return (udid, bundleId);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("The iOS cleanup target is invalid.", ex);
        }
    }

    private static string ShortDeviceId(string udid)
    {
        if (udid.Length <= 8)
        {
            return "••••";
        }

        return $"••••{udid[^6..]}";
    }

    private static CommandResult Run(
        string executable,
        IReadOnlyList<string> arguments,
        bool allowFailure = false,
        int timeoutMilliseconds = 25_000)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory,
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
            throw new InvalidOperationException("The iOS device tool could not be started.");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeoutMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("The iOS device tool did not respond before the safety timeout.");
        }

        var result = new CommandResult(
            process.ExitCode,
            outputTask.GetAwaiter().GetResult().Trim(),
            errorTask.GetAwaiter().GetResult().Trim());
        if (!allowFailure && result.ExitCode != 0)
        {
            throw new InvalidOperationException($"The iOS device tool failed: {FirstUsefulMessage(result)}");
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
