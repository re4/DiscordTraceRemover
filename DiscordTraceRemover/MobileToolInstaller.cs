using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DiscordTraceRemover;

internal static class MobileToolInstaller
{
    private const string AndroidDownloadUrl =
        "https://dl.google.com/android/repository/platform-tools-latest-windows.zip";

    private const string IosReleaseApi =
        "https://api.github.com/repos/L1ghtmann/libimobiledevice/releases/latest";

    private const long MaximumDownloadBytes = 100L * 1024 * 1024;

    internal static string ToolsRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "DiscordTraceRemover",
        "tools");

    internal static string AndroidAdbPath => Path.Combine(ToolsRoot, "android", "platform-tools", "adb.exe");
    internal static string IosToolsRoot => Path.Combine(ToolsRoot, "ios");

    internal static bool HasManagedAndroidTools => File.Exists(AndroidAdbPath);

    internal static bool HasManagedIosTools =>
        File.Exists(Path.Combine(IosToolsRoot, "idevice_id.exe")) &&
        File.Exists(Path.Combine(IosToolsRoot, "ideviceinstaller.exe"));

    internal static async Task InstallAndroidAsync(IProgress<string>? progress = null)
    {
        var operationRoot = CreateOperationDirectory("android");
        var archive = Path.Combine(operationRoot, "platform-tools.zip");
        var extracted = Path.Combine(operationRoot, "extracted");

        try
        {
            progress?.Report("Downloading Android Platform Tools from dl.google.com…");
            await DownloadAsync(
                new Uri(AndroidDownloadUrl),
                archive,
                uri => uri.Scheme == Uri.UriSchemeHttps &&
                       uri.Host.Equals("dl.google.com", StringComparison.OrdinalIgnoreCase));
            Directory.CreateDirectory(extracted);
            ExtractZipSafely(archive, extracted);

            var payload = Path.Combine(extracted, "platform-tools");
            var adb = Path.Combine(payload, "adb.exe");
            if (!File.Exists(adb))
            {
                throw new InvalidOperationException("Google's archive did not contain platform-tools/adb.exe.");
            }

            progress?.Report("Verifying Google's Windows signature on adb.exe…");
            VerifyGoogleSignature(adb);
            RunVersionCheck(adb, ["version"], "ADB");

            var destination = Path.GetDirectoryName(AndroidAdbPath)
                              ?? throw new InvalidOperationException("The Android tools destination is invalid.");
            ReplaceManagedDirectory(payload, destination);
            WriteSourceMetadata(
                Path.Combine(destination, "discord-trace-remover-source.txt"),
                "Android SDK Platform Tools\r\n" +
                $"Source: {AndroidDownloadUrl}\r\n" +
                $"Installed UTC: {DateTimeOffset.UtcNow:O}\r\n" +
                "Verification: Valid Windows Authenticode signature issued to Google LLC\r\n");
            progress?.Report("Android Platform Tools installed privately for this cleaner.");
        }
        finally
        {
            SafeDeleteGeneratedDirectory(operationRoot);
        }
    }

    internal static async Task InstallIosAsync(IProgress<string>? progress = null)
    {
        var operationRoot = CreateOperationDirectory("ios");
        var archive = Path.Combine(operationRoot, "libimobiledevice.tar.xz");
        var extracted = Path.Combine(operationRoot, "extracted");

        try
        {
            progress?.Report("Finding the latest Windows libimobiledevice suite on GitHub…");
            var release = await GetLatestIosReleaseAsync();
            progress?.Report($"Downloading {release.AssetName}…");
            await DownloadAsync(
                release.DownloadUri,
                archive,
                uri => uri.Scheme == Uri.UriSchemeHttps &&
                       (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
                        uri.Host.Equals("release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase)));

            progress?.Report("Verifying GitHub's published SHA-256 digest…");
            VerifySha256(archive, release.Sha256);

            var tar = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "tar.exe");
            if (!File.Exists(tar))
            {
                throw new InvalidOperationException("Windows tar.exe is required to extract the verified iOS tools archive.");
            }

            Directory.CreateDirectory(extracted);
            ValidateTarEntries(tar, archive);
            RunTool(tar, ["-xf", archive, "-C", extracted], 60_000);
            RejectReparsePoints(extracted);

            var installer = Directory
                .EnumerateFiles(extracted, "ideviceinstaller.exe", SearchOption.AllDirectories)
                .FirstOrDefault();
            if (installer is null)
            {
                throw new InvalidOperationException("The verified iOS tools archive did not contain ideviceinstaller.exe.");
            }

            var payload = Path.GetDirectoryName(installer)
                          ?? throw new InvalidOperationException("The iOS tools payload folder is invalid.");
            var deviceId = Path.Combine(payload, "idevice_id.exe");
            if (!File.Exists(deviceId))
            {
                throw new InvalidOperationException("The verified iOS tools archive did not contain idevice_id.exe beside ideviceinstaller.exe.");
            }

            RunVersionCheck(installer, ["--version"], "ideviceinstaller");
            RunVersionCheck(deviceId, ["--version"], "idevice_id");
            ReplaceManagedDirectory(payload, IosToolsRoot);
            WriteSourceMetadata(
                Path.Combine(IosToolsRoot, "discord-trace-remover-source.txt"),
                "Third-party libimobiledevice Windows suite\r\n" +
                $"Release: {release.ReleasePage}\r\n" +
                $"Asset: {release.AssetName}\r\n" +
                $"SHA-256: {release.Sha256}\r\n" +
                $"Installed UTC: {DateTimeOffset.UtcNow:O}\r\n" +
                "Upstream source: https://github.com/libimobiledevice\r\n");
            progress?.Report("Verified iOS device tools installed privately for this cleaner.");
        }
        finally
        {
            SafeDeleteGeneratedDirectory(operationRoot);
        }
    }

    internal static void RunTargetingSelfTest()
    {
        if (!IsSafeArchiveEntry("platform-tools/adb.exe") ||
            IsSafeArchiveEntry("../outside.exe") ||
            IsSafeArchiveEntry("folder/../../outside.exe") ||
            IsSafeArchiveEntry("C:/outside.exe"))
        {
            throw new InvalidOperationException("Mobile tool archive path safety test failed.");
        }

        if (!IsAllowedIosDownload(new Uri(
                "https://github.com/L1ghtmann/libimobiledevice/releases/download/tag/tools.tar.xz")) ||
            IsAllowedIosDownload(new Uri("https://example.com/tools.tar.xz")))
        {
            throw new InvalidOperationException("iOS dependency download allowlist test failed.");
        }

        RunSignaturePathHandoffSelfTest();
    }

    private static async Task DownloadAsync(
        Uri uri,
        string destination,
        Func<Uri, bool> isAllowedFinalUri)
    {
        using var client = CreateHttpClient();
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var finalUri = response.RequestMessage?.RequestUri;
        if (finalUri is null || !isAllowedFinalUri(finalUri))
        {
            throw new InvalidOperationException("The dependency download redirected to an unapproved host.");
        }
        if (response.Content.Headers.ContentLength is > MaximumDownloadBytes)
        {
            throw new InvalidOperationException("The dependency download is larger than the 100 MB safety limit.");
        }

        await using var source = await response.Content.ReadAsStreamAsync();
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var buffer = new byte[81_920];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > MaximumDownloadBytes)
            {
                throw new InvalidOperationException("The dependency download exceeded the 100 MB safety limit.");
            }

            await output.WriteAsync(buffer.AsMemory(0, read));
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(3)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DiscordTraceRemover/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static async Task<IosRelease> GetLatestIosReleaseAsync()
    {
        using var client = CreateHttpClient();
        var json = await client.GetStringAsync(IosReleaseApi);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var releasePage = root.GetProperty("html_url").GetString()
                          ?? throw new InvalidOperationException("GitHub did not return an iOS tools release page.");

        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? string.Empty;
            if (!name.Contains("x86_64", StringComparison.OrdinalIgnoreCase) ||
                !name.Contains("mingw64", StringComparison.OrdinalIgnoreCase) ||
                !name.EndsWith(".tar.xz", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var download = asset.GetProperty("browser_download_url").GetString();
            var digest = asset.TryGetProperty("digest", out var digestElement)
                ? digestElement.GetString()
                : null;
            if (download is null || digest is null || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var uri = new Uri(download);
            if (!IsAllowedIosDownload(uri))
            {
                throw new InvalidOperationException("GitHub returned an iOS tools download outside the approved repository.");
            }

            return new IosRelease(
                name,
                uri,
                digest["sha256:".Length..].ToLowerInvariant(),
                releasePage);
        }

        throw new InvalidOperationException(
            "The latest third-party libimobiledevice release has no x64 Windows archive with a published SHA-256 digest.");
    }

    private static bool IsAllowedIosDownload(Uri uri)
    {
        return uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
               uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
               uri.AbsolutePath.StartsWith(
                   "/L1ghtmann/libimobiledevice/releases/download/",
                   StringComparison.Ordinal);
    }

    private static void ExtractZipSafely(string archive, string destination)
    {
        var destinationRoot = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) +
                              Path.DirectorySeparatorChar;
        using var zip = ZipFile.OpenRead(archive);
        foreach (var entry in zip.Entries)
        {
            if (!IsSafeArchiveEntry(entry.FullName))
            {
                throw new InvalidOperationException($"The dependency archive contains an unsafe path: {entry.FullName}");
            }

            var target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!target.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The dependency archive attempted to extract outside its staging folder.");
            }

            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: false);
        }
    }

    private static void ValidateTarEntries(string tar, string archive)
    {
        var listing = RunTool(tar, ["-tf", archive], 30_000);
        foreach (var entry in listing.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!IsSafeArchiveEntry(entry.Trim()))
            {
                throw new InvalidOperationException($"The iOS tools archive contains an unsafe path: {entry.Trim()}");
            }
        }

        var verboseListing = RunTool(tar, ["-tvf", archive], 30_000);
        foreach (var entry in verboseListing.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (entry[0] is not '-' and not 'd')
            {
                throw new InvalidOperationException(
                    "The iOS tools archive contains a link or unsupported filesystem entry.");
            }
        }
    }

    private static bool IsSafeArchiveEntry(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value) || value.Contains(':'))
        {
            return false;
        }

        var parts = value.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.All(part => part != "..");
    }

    private static void VerifySha256(string path, string expected)
    {
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(actual),
                Encoding.ASCII.GetBytes(expected.ToLowerInvariant())))
        {
            throw new InvalidOperationException("The iOS tools SHA-256 digest did not match GitHub's release metadata.");
        }
    }

    private static void VerifyGoogleSignature(string adb)
    {
        var powershell = FindWindowsPowerShell();

        const string script =
            "$p=$env:DISCORD_TRACE_REMOVER_SIGNATURE_PATH; " +
            "if([string]::IsNullOrWhiteSpace($p)){throw 'The signature path was not supplied.'}; " +
            "$s=Get-AuthenticodeSignature -LiteralPath $p; " +
            "if($s.Status -ne 'Valid' -or $s.SignerCertificate.Subject -notmatch 'O=Google LLC'){exit 9}";
        RunTool(
            powershell,
            ["-NoProfile", "-NonInteractive", "-Command", script],
            30_000,
            new Dictionary<string, string>
            {
                ["DISCORD_TRACE_REMOVER_SIGNATURE_PATH"] = adb
            });
    }

    private static void RunSignaturePathHandoffSelfTest()
    {
        const string marker = "DiscordTraceRemover signature path handoff";
        var result = RunTool(
            FindWindowsPowerShell(),
            [
                "-NoProfile",
                "-NonInteractive",
                "-Command",
                "[Console]::Out.Write($env:DISCORD_TRACE_REMOVER_SIGNATURE_PATH)"
            ],
            30_000,
            new Dictionary<string, string>
            {
                ["DISCORD_TRACE_REMOVER_SIGNATURE_PATH"] = marker
            });
        if (!result.Output.Equals(marker, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("PowerShell signature path handoff test failed.");
        }
    }

    private static string FindWindowsPowerShell()
    {
        var powershell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        if (!File.Exists(powershell))
        {
            throw new InvalidOperationException("Windows PowerShell is required to verify Google's Authenticode signature.");
        }

        return powershell;
    }

    private static void RunVersionCheck(string executable, IReadOnlyList<string> arguments, string name)
    {
        var result = RunTool(executable, arguments, 20_000);
        if (string.IsNullOrWhiteSpace(result.Output) && string.IsNullOrWhiteSpace(result.Error))
        {
            throw new InvalidOperationException($"{name} started but returned no version information.");
        }
    }

    private static ToolResult RunTool(
        string executable,
        IReadOnlyList<string> arguments,
        int timeoutMilliseconds,
        IReadOnlyDictionary<string, string>? environment = null)
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
        if (environment is not null)
        {
            foreach (var variable in environment)
            {
                process.StartInfo.Environment[variable.Key] = variable.Value;
            }
        }

        if (!process.Start())
        {
            throw new InvalidOperationException($"Could not start {Path.GetFileName(executable)}.");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeoutMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{Path.GetFileName(executable)} did not finish before the safety timeout.");
        }

        var result = new ToolResult(
            process.ExitCode,
            outputTask.GetAwaiter().GetResult().Trim(),
            errorTask.GetAwaiter().GetResult().Trim());
        if (result.ExitCode != 0)
        {
            var details = !string.IsNullOrWhiteSpace(result.Error) ? result.Error : result.Output;
            throw new InvalidOperationException(
                $"{Path.GetFileName(executable)} failed verification. {details}".Trim());
        }

        return result;
    }

    private static string CreateOperationDirectory(string platform)
    {
        Directory.CreateDirectory(ToolsRoot);
        var path = Path.Combine(ToolsRoot, $".install-{platform}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void ReplaceManagedDirectory(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var backup = destination + $".backup-{Guid.NewGuid():N}";
        var hadExisting = Directory.Exists(destination);

        if (hadExisting)
        {
            Directory.Move(destination, backup);
        }

        try
        {
            Directory.Move(source, destination);
            if (hadExisting)
            {
                SafeDeleteGeneratedDirectory(backup);
            }
        }
        catch
        {
            if (!Directory.Exists(destination) && Directory.Exists(backup))
            {
                Directory.Move(backup, destination);
            }

            throw;
        }
    }

    private static void RejectReparsePoints(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.TopDirectoryOnly))
            {
                var attributes = File.GetAttributes(entry);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new InvalidOperationException("The dependency archive contains a symbolic link or reparse point.");
                }

                if (attributes.HasFlag(FileAttributes.Directory))
                {
                    pending.Push(entry);
                }
            }
        }
    }

    private static void WriteSourceMetadata(string path, string content)
    {
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void SafeDeleteGeneratedDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        var toolsRoot = Path.GetFullPath(ToolsRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(path);
        if (!target.StartsWith(toolsRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refused to remove a dependency folder outside the cleaner's managed tools root.");
        }

        Directory.Delete(target, recursive: true);
    }

    private sealed record IosRelease(string AssetName, Uri DownloadUri, string Sha256, string ReleasePage);
    private sealed record ToolResult(int ExitCode, string Output, string Error);
}
