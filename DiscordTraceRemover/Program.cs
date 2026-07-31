namespace DiscordTraceRemover;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var renderIndex = Array.FindIndex(args, a => a.Equals("--render-ui", StringComparison.OrdinalIgnoreCase));
        if (renderIndex >= 0)
        {
            var outputPath = renderIndex + 1 < args.Length
                ? Path.GetFullPath(args[renderIndex + 1])
                : Path.Combine(Path.GetTempPath(), "DiscordTraceRemover-ui.png");
            return RenderUi(outputPath);
        }

        if (args.Any(a => a.Equals("--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            return RunSelfTest();
        }

        if (args.Any(a => a.Equals("--preview", StringComparison.OrdinalIgnoreCase)))
        {
            return RunPreview(args);
        }

        if (args.Any(a => a.Equals("--clean", StringComparison.OrdinalIgnoreCase)))
        {
            return RunCommandLineCleanup(args);
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }

    private static CleanupOptions ParseOptions(string[] args)
    {
        var onlyDesktop = args.Any(a => a.Equals("--desktop-only", StringComparison.OrdinalIgnoreCase));
        var onlyChrome = args.Any(a => a.Equals("--chrome-only", StringComparison.OrdinalIgnoreCase));
        var onlyEdge = args.Any(a => a.Equals("--edge-only", StringComparison.OrdinalIgnoreCase));
        var onlyFirefox = args.Any(a => a.Equals("--firefox-only", StringComparison.OrdinalIgnoreCase));
        var onlyAndroid = args.Any(a => a.Equals("--android-only", StringComparison.OrdinalIgnoreCase));
        var onlyIos = args.Any(a => a.Equals("--ios-only", StringComparison.OrdinalIgnoreCase));
        var includeAndroid = onlyAndroid || args.Any(a => a.Equals("--android", StringComparison.OrdinalIgnoreCase));
        var includeIos = onlyIos || args.Any(a => a.Equals("--ios", StringComparison.OrdinalIgnoreCase));
        var browserOnly = onlyChrome || onlyEdge || onlyFirefox;
        var scopedOnly = browserOnly || onlyAndroid || onlyIos;

        return new CleanupOptions(
            DesktopData: !scopedOnly,
            ChromeData: !onlyDesktop && (!browserOnly || onlyChrome),
            EdgeData: !onlyDesktop && (!browserOnly || onlyEdge),
            FirefoxData: !onlyDesktop && (!browserOnly || onlyFirefox),
            AndroidData: includeAndroid,
            IosData: includeIos,
            WindowsIntegration: !scopedOnly);
    }

    private static int RunPreview(string[] args)
    {
        try
        {
            var options = ParseOptions(args);
            var result = CleanupEngine.Preview(options);

            Console.WriteLine("Discord Trace Remover - preview (nothing was changed)");
            Console.WriteLine();
            foreach (var item in result.Items)
            {
                Console.WriteLine($"[{item.Category}] {item.Description}");
                Console.WriteLine($"  {item.Location}");
            }

            Console.WriteLine();
            Console.WriteLine(result.Items.Count == 0
                ? "No Discord data was found in the selected areas."
                : $"Found {result.Items.Count} cleanup target(s).");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int RunCommandLineCleanup(string[] args)
    {
        if (!args.Any(a => a.Equals("--yes", StringComparison.OrdinalIgnoreCase)))
        {
            Console.Error.WriteLine("Cleanup was not started. Add --yes to confirm permanent deletion.");
            return 2;
        }

        try
        {
            var options = ParseOptions(args);
            var result = CleanupEngine.Clean(options, Console.WriteLine);
            Console.WriteLine();
            Console.WriteLine($"Finished: {result.Succeeded} removed, {result.Failed} failed, {result.Skipped} skipped.");
            return result.Failed == 0 ? 0 : 1;
        }
        catch (BrowserIsRunningException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 3;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int RunSelfTest()
    {
        var database = Path.Combine(Path.GetTempPath(), $"discord-trace-remover-test-{Guid.NewGuid():N}.db");
        try
        {
            CleanupEngine.RunTargetingSelfTest();
            AdbClient.RunTargetingSelfTest();
            IosDeviceClient.RunTargetingSelfTest();
            MobileToolInstaller.RunTargetingSelfTest();
            NativeSqlite.Execute(
                database,
                """
                CREATE TABLE cookies (host_key TEXT NOT NULL);
                CREATE TABLE moz_cookies (host TEXT NOT NULL);
                CREATE TABLE webappsstore2 (originKey TEXT NOT NULL);
                CREATE TABLE moz_perms (origin TEXT NOT NULL);
                INSERT INTO cookies(host_key) VALUES ('discord.com'), ('.discord.gg'), ('example.com');
                INSERT INTO moz_cookies(host) VALUES ('.discord.com'), ('discord.gg'), ('.example.com');
                INSERT INTO webappsstore2(originKey) VALUES ('moc.drocsid.:https:443'), ('moc.elpmaxe.:https:443');
                INSERT INTO moz_perms(origin) VALUES ('https://discord.com'), ('https://example.com');
                """);
            File.WriteAllBytes(database + "-journal", []);

            if (!CleanupEngine.DeleteChromiumDiscordCookies(database, null) ||
                CleanupEngine.DeleteChromiumDiscordCookies(database, null) ||
                !CleanupEngine.DeleteFirefoxDiscordCookies(database, null) ||
                CleanupEngine.DeleteFirefoxDiscordCookies(database, null) ||
                !CleanupEngine.DeleteFirefoxLegacyStorage(database, null) ||
                CleanupEngine.DeleteFirefoxLegacyStorage(database, null) ||
                !CleanupEngine.DeleteFirefoxPermissions(database, null) ||
                CleanupEngine.DeleteFirefoxPermissions(database, null))
            {
                Console.Error.WriteLine("Browser database cleanup test failed.");
                return 1;
            }

            Console.WriteLine("Self-test passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Self-test failed: {ex.Message}");
            return 1;
        }
        finally
        {
            try
            {
                File.Delete(database);
                File.Delete(database + "-journal");
                File.Delete(database + "-wal");
                File.Delete(database + "-shm");
            }
            catch
            {
                // Best-effort cleanup of the temporary test database.
            }
        }
    }

    private static int RenderUi(string outputPath)
    {
        try
        {
            ApplicationConfiguration.Initialize();
            using var form = new MainForm();
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-32_000, -32_000);
            form.Show();
            Application.DoEvents();
            form.PerformLayout();
            using var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.ClientSize));
            bitmap.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
            form.Hide();
            Console.WriteLine(outputPath);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"UI render failed: {ex.Message}");
            return 1;
        }
    }
}
