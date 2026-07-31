namespace DiscordTraceRemover;

internal sealed record CleanupOptions(
    bool DesktopData,
    bool ChromeData,
    bool EdgeData,
    bool FirefoxData,
    bool AndroidData,
    bool IosData,
    bool WindowsIntegration);

internal sealed record CleanupItem(
    string Category,
    string Description,
    string Location,
    CleanupItemType Type,
    string? TargetId = null);

internal enum CleanupItemType
{
    Directory,
    File,
    ChromiumCookies,
    FirefoxCookies,
    FirefoxLegacyStorage,
    FirefoxPermissions,
    AndroidPackage,
    AndroidDirectory,
    IosPackage,
    Registry
}

internal sealed record PreviewResult(IReadOnlyList<CleanupItem> Items);

internal sealed record CleanupResult(int Succeeded, int Failed, int Skipped);

internal sealed class BrowserIsRunningException(string browserName)
    : InvalidOperationException($"{browserName} is open. Close every {browserName} window, then run the cleanup again.");
