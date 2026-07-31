using System.Runtime.InteropServices;

namespace DiscordTraceRemover;

internal sealed class MainForm : Form
{
    private const int WindowCaption = 0xA1;
    private const int LeftButtonDown = 0x2;

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint window, int message, int parameter, int data);

    private readonly DiscordOptionCard _desktopData = new(
        "Discord desktop app and data",
        "Installations, cache, logs, settings, temporary data, and crash reports");

    private readonly DiscordOptionCard _chromeData = new(
        "Google Chrome",
        "Discord cookies and Discord-owned site storage only");

    private readonly DiscordOptionCard _edgeData = new(
        "Microsoft Edge",
        "Discord cookies and Discord-owned site storage only");

    private readonly DiscordOptionCard _firefoxData = new(
        "Mozilla Firefox",
        "Discord cookies, permissions, and Discord-owned site storage only");

    private readonly DiscordOptionCard _androidData = new(
        "Android device (ADB)",
        "Uninstalls com.discord and removes app-owned storage; ADB installs on demand");

    private readonly DiscordOptionCard _iosData = new(
        "iPhone or iPad (libimobiledevice)",
        "Uninstalls Discord and its private sandbox; verified tools install on demand");

    private readonly DiscordOptionCard _windowsIntegration = new(
        "Windows shortcuts and registrations",
        "Startup, uninstall, App Paths, protocol, firewall, and shortcut registrations");

    private readonly DiscordButton _previewButton = new()
    {
        Text = "Preview cleanup",
        Size = new Size(136, 40),
        BackColor = Color.FromArgb(78, 80, 88),
        FlatAppearance = { MouseOverBackColor = Color.FromArgb(90, 93, 101) }
    };

    private readonly DiscordButton _cleanButton = new()
    {
        Text = "Clean for reinstall",
        Size = new Size(156, 40),
        BackColor = DiscordTheme.Blurple,
        FlatAppearance = { MouseOverBackColor = DiscordTheme.BlurpleHover }
    };

    private readonly RichTextBox _results = new()
    {
        ReadOnly = true,
        BorderStyle = BorderStyle.None,
        Dock = DockStyle.Fill,
        BackColor = DiscordTheme.Input,
        ForeColor = DiscordTheme.MutedText,
        Font = new Font("Cascadia Mono", 9F),
        DetectUrls = false,
        Margin = new Padding(0)
    };

    private readonly Label _status = new()
    {
        Text = "READY",
        AutoSize = true,
        ForeColor = DiscordTheme.Green,
        Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold),
        Padding = new Padding(0, 12, 12, 0)
    };

    internal MainForm()
    {
        Text = "Discord Trace Remover";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 650);
        ClientSize = new Size(1040, 720);
        Font = new Font("Segoe UI", 9.5F);
        ForeColor = DiscordTheme.Text;
        BackColor = DiscordTheme.WindowBorder;
        FormBorderStyle = FormBorderStyle.None;
        Padding = new Padding(1);
        AutoScaleMode = AutoScaleMode.Dpi;

        _desktopData.Checked = true;
        _chromeData.Checked = true;
        _edgeData.Checked = true;
        _firefoxData.Checked = true;
        _androidData.Checked = false;
        _iosData.Checked = false;
        _windowsIntegration.Checked = true;

        var window = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = DiscordTheme.Main
        };
        window.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        window.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        window.Controls.Add(CreateTitleBar(), 0, 0);
        window.Controls.Add(CreateAppShell(), 0, 1);
        Controls.Add(window);

        AcceptButton = _cleanButton;
        _previewButton.Click += async (_, _) => await PreviewCleanupAsync();
        _cleanButton.Click += async (_, _) => await CleanAsync();
    }

    private Control CreateTitleBar()
    {
        var titleBar = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = DiscordTheme.TitleBar,
            Margin = new Padding(0)
        };
        titleBar.MouseDown += DragWindow;

        var title = new Label
        {
            Text = "Discord Trace Remover",
            AutoSize = true,
            ForeColor = DiscordTheme.MutedText,
            Font = new Font("Segoe UI", 8.5F),
            Location = new Point(12, 8)
        };
        title.MouseDown += DragWindow;

        var close = CreateWindowButton("×", DiscordTheme.Red, (_, _) => Close());
        close.Dock = DockStyle.Right;
        var minimize = CreateWindowButton("—", Color.FromArgb(78, 80, 88), (_, _) => WindowState = FormWindowState.Minimized);
        minimize.Dock = DockStyle.Right;

        titleBar.Controls.Add(title);
        titleBar.Controls.Add(minimize);
        titleBar.Controls.Add(close);
        return titleBar;
    }

    private static Button CreateWindowButton(string text, Color hoverColor, EventHandler onClick)
    {
        var button = new Button
        {
            Text = text,
            Width = 46,
            Dock = DockStyle.Right,
            FlatStyle = FlatStyle.Flat,
            BackColor = DiscordTheme.TitleBar,
            ForeColor = DiscordTheme.MutedText,
            Font = new Font("Segoe UI", 10F),
            TabStop = false,
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = hoverColor;
        button.Click += onClick;
        return button;
    }

    private Control CreateAppShell()
    {
        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 224));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.Controls.Add(CreateServerRail(), 0, 0);
        shell.Controls.Add(CreateSidebar(), 1, 0);
        shell.Controls.Add(CreateMainContent(), 2, 0);
        return shell;
    }

    private Control CreateServerRail()
    {
        var rail = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = DiscordTheme.ServerRail,
            Margin = new Padding(0)
        };

        var activePill = new Panel
        {
            BackColor = Color.White,
            Size = new Size(4, 40),
            Location = new Point(0, 18)
        };

        var brand = new DiscordButton
        {
            Text = "D",
            Size = new Size(48, 48),
            Location = new Point(12, 14),
            BackColor = DiscordTheme.Blurple,
            Font = new Font("Segoe UI Semibold", 16F, FontStyle.Bold),
            CornerRadius = 16,
            TabStop = false
        };
        brand.FlatAppearance.MouseOverBackColor = DiscordTheme.BlurpleHover;

        var separator = new Panel
        {
            BackColor = DiscordTheme.Divider,
            Size = new Size(32, 2),
            Location = new Point(20, 74)
        };

        var shield = new DiscordButton
        {
            Text = "✓",
            Size = new Size(48, 48),
            Location = new Point(12, 88),
            BackColor = DiscordTheme.Card,
            ForeColor = DiscordTheme.Green,
            Font = new Font("Segoe UI Semibold", 15F, FontStyle.Bold),
            CornerRadius = 24,
            TabStop = false
        };
        shield.FlatAppearance.MouseOverBackColor = DiscordTheme.CardHover;

        rail.Controls.Add(activePill);
        rail.Controls.Add(brand);
        rail.Controls.Add(separator);
        rail.Controls.Add(shield);
        return rail;
    }

    private Control CreateSidebar()
    {
        var sidebar = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = DiscordTheme.Sidebar,
            Margin = new Padding(0)
        };

        var workspaceTitle = new Label
        {
            Text = "TRACE REMOVER",
            Dock = DockStyle.Top,
            Height = 48,
            Padding = new Padding(16, 16, 0, 0),
            ForeColor = DiscordTheme.Text,
            Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold)
        };

        var separator = new Panel
        {
            Dock = DockStyle.Top,
            Height = 1,
            BackColor = Color.FromArgb(35, 36, 40)
        };

        var category = new Label
        {
            Text = "CLEANUP CHANNELS",
            Dock = DockStyle.Top,
            Height = 32,
            Padding = new Padding(12, 12, 0, 0),
            ForeColor = DiscordTheme.FaintText,
            Font = new Font("Segoe UI Semibold", 7.75F, FontStyle.Bold)
        };

        var selectedChannel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 38,
            BackColor = Color.FromArgb(64, 66, 73),
            Padding = new Padding(10, 8, 0, 0),
            Margin = new Padding(8, 0, 8, 0)
        };
        selectedChannel.Controls.Add(new Label
        {
            Text = "#  clean-reinstall",
            Dock = DockStyle.Fill,
            ForeColor = DiscordTheme.Text,
            Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold)
        });

        var privacyChannel = new Label
        {
            Text = "#  browser-privacy",
            Dock = DockStyle.Top,
            Height = 38,
            Padding = new Padding(18, 10, 0, 0),
            ForeColor = DiscordTheme.MutedText
        };

        var userPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 58,
            BackColor = Color.FromArgb(35, 36, 40)
        };
        userPanel.Controls.Add(new Label
        {
            Text = "✓",
            TextAlign = ContentAlignment.MiddleCenter,
            Size = new Size(34, 34),
            Location = new Point(10, 12),
            BackColor = DiscordTheme.Green,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold)
        });
        userPanel.Controls.Add(new Label
        {
            Text = "Privacy-safe\r\nDiscord-only targets",
            AutoSize = true,
            Location = new Point(52, 12),
            ForeColor = DiscordTheme.Text,
            Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold)
        });

        sidebar.Controls.Add(userPanel);
        sidebar.Controls.Add(privacyChannel);
        sidebar.Controls.Add(selectedChannel);
        sidebar.Controls.Add(category);
        sidebar.Controls.Add(separator);
        sidebar.Controls.Add(workspaceTitle);
        return sidebar;
    }

    private Control CreateMainContent()
    {
        var main = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = DiscordTheme.Main,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(28, 22, 28, 22),
            Margin = new Padding(0)
        };
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 338));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        main.Controls.Add(CreateHeader(), 0, 0);
        main.Controls.Add(CreateOptions(), 0, 1);
        main.Controls.Add(CreateNotice(), 0, 2);
        main.Controls.Add(CreateActionBar(), 0, 3);
        main.Controls.Add(CreateResultsHeader(), 0, 4);
        main.Controls.Add(CreateResultsPanel(), 0, 5);
        return main;
    }

    private static Control CreateHeader()
    {
        var header = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
        header.Controls.Add(new Label
        {
            Text = "Discord Trace Remover",
            AutoSize = true,
            Location = new Point(0, 0),
            ForeColor = DiscordTheme.Text,
            Font = new Font("Segoe UI Semibold", 18F, FontStyle.Bold)
        });
        header.Controls.Add(new Label
        {
            Text = "Choose what to remove before a fresh desktop reinstall.",
            AutoSize = true,
            Location = new Point(2, 36),
            ForeColor = DiscordTheme.MutedText,
            Font = new Font("Segoe UI", 9.5F)
        });
        return header;
    }

    private Control CreateOptions()
    {
        var options = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };

        options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (var i = 0; i < 4; i++)
        {
            options.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        }

        foreach (var leftCard in new[] { _desktopData, _chromeData, _firefoxData })
        {
            leftCard.Margin = new Padding(0, 0, 5, 8);
        }

        foreach (var rightCard in new[] { _windowsIntegration, _edgeData, _androidData })
        {
            rightCard.Margin = new Padding(5, 0, 0, 8);
        }

        options.Controls.Add(_desktopData, 0, 0);
        options.Controls.Add(_windowsIntegration, 1, 0);
        options.Controls.Add(_chromeData, 0, 1);
        options.Controls.Add(_edgeData, 1, 1);
        options.Controls.Add(_firefoxData, 0, 2);
        options.Controls.Add(_androidData, 1, 2);
        _iosData.Margin = new Padding(0, 0, 0, 8);
        options.Controls.Add(_iosData, 0, 3);
        options.SetColumnSpan(_iosData, 2);
        return options;
    }

    private static Control CreateNotice()
    {
        var notice = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(56, 58, 64),
            Margin = new Padding(0, 3, 0, 7),
            Padding = new Padding(12, 10, 12, 8)
        };
        notice.Controls.Add(new Label
        {
            Text = "ⓘ  Close selected browsers. Mobile tools install on demand. Connect exactly one authorized or trusted USB device.",
            Dock = DockStyle.Fill,
            ForeColor = DiscordTheme.MutedText,
            Font = new Font("Segoe UI", 8.75F),
            TextAlign = ContentAlignment.MiddleLeft
        });
        return notice;
    }

    private Control CreateActionBar()
    {
        var bar = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
        _cleanButton.Location = new Point(0, 5);
        _previewButton.Location = new Point(166, 5);
        _status.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _status.Location = new Point(Math.Max(0, bar.Width - _status.Width), 8);
        bar.Resize += (_, _) => _status.Left = Math.Max(0, bar.ClientSize.Width - _status.Width);
        bar.Controls.Add(_cleanButton);
        bar.Controls.Add(_previewButton);
        bar.Controls.Add(_status);
        return bar;
    }

    private static Control CreateResultsHeader()
    {
        var header = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
        header.Controls.Add(new Label
        {
            Text = "CLEANUP LOG",
            AutoSize = true,
            Location = new Point(0, 7),
            ForeColor = DiscordTheme.FaintText,
            Font = new Font("Segoe UI Semibold", 7.75F, FontStyle.Bold)
        });
        return header;
    }

    private Control CreateResultsPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = DiscordTheme.Input,
            Padding = new Padding(12),
            Margin = new Padding(0)
        };
        panel.Controls.Add(_results);
        return panel;
    }

    private void DragWindow(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        ReleaseCapture();
        SendMessage(Handle, WindowCaption, LeftButtonDown, 0);
    }

    private CleanupOptions GetOptions()
    {
        return new CleanupOptions(
            _desktopData.Checked,
            _chromeData.Checked,
            _edgeData.Checked,
            _firefoxData.Checked,
            _androidData.Checked,
            _iosData.Checked,
            _windowsIntegration.Checked);
    }

    private bool EnsureSelection()
    {
        if (_desktopData.Checked || _chromeData.Checked || _edgeData.Checked ||
            _firefoxData.Checked || _androidData.Checked || _iosData.Checked ||
            _windowsIntegration.Checked)
        {
            return true;
        }

        ShowMessage("Choose at least one cleanup card first.", "Nothing selected", MessageBoxIcon.Information);
        return false;
    }

    private async Task PreviewCleanupAsync()
    {
        if (!EnsureSelection())
        {
            return;
        }

        var options = GetOptions();
        if (!await EnsureMobileToolsAsync(options))
        {
            return;
        }

        try
        {
            var preview = CleanupEngine.Preview(options);
            _results.Clear();
            if (preview.Items.Count == 0)
            {
                AppendResult("No Discord data was found in the selected areas.");
            }
            else
            {
                foreach (var group in preview.Items.GroupBy(item => item.Category))
                {
                    AppendHeading(group.Key.ToUpperInvariant());
                    foreach (var item in group)
                    {
                        AppendResult($"  • {item.Description}");
                        AppendMuted($"    {item.Location}");
                    }

                    AppendResult(string.Empty);
                }
            }

            SetStatus($"PREVIEW: {preview.Items.Count} TARGET(S)", DiscordTheme.Blurple);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private async Task CleanAsync()
    {
        if (!EnsureSelection())
        {
            return;
        }

        var options = GetOptions();
        if (!await EnsureMobileToolsAsync(options))
        {
            return;
        }

        PreviewResult preview;
        try
        {
            preview = CleanupEngine.Preview(options);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            return;
        }

        if (preview.Items.Count == 0)
        {
            _results.Text = "No Discord data was found in the selected areas.";
            SetStatus("ALREADY CLEAN", DiscordTheme.Green);
            return;
        }

        var mobileWarning = string.Empty;
        if (options.AndroidData)
        {
            mobileWarning += "\n\nAndroid cleanup will uninstall the official Discord app from the connected device.";
        }

        if (options.IosData)
        {
            mobileWarning += "\n\niOS cleanup will uninstall Discord from the connected iPhone or iPad.";
        }
        var confirmation = MessageBox.Show(
            this,
            $"Permanently remove {preview.Items.Count} detected Discord cleanup target(s)?\n\n" +
            "Discord will be closed. Close each selected browser before continuing. This cannot be undone." +
            mobileWarning,
            "Confirm Discord cleanup",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (confirmation != DialogResult.Yes)
        {
            return;
        }

        SetBusy(true);
        _results.Clear();
        IProgress<string> progress = new Progress<string>(AppendResult);

        try
        {
            var result = await Task.Run(() => CleanupEngine.Clean(options, progress.Report, preview.Items));
            AppendResult(string.Empty);
            AppendHeading($"FINISHED  •  {result.Succeeded} REMOVED  •  {result.Failed} FAILED  •  {result.Skipped} CLEAR");
            SetStatus(result.Failed == 0 ? "READY TO REINSTALL" : "CHECK LOG", result.Failed == 0 ? DiscordTheme.Green : DiscordTheme.Red);

            ShowMessage(
                result.Failed == 0
                    ? "Cleanup is complete. Discord is ready to reinstall."
                    : "Some items could not be removed. Review the cleanup log for details.",
                result.Failed == 0 ? "Ready to reinstall" : "Cleanup finished",
                result.Failed == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (BrowserIsRunningException ex)
        {
            ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            ShowError($"Cleanup stopped: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task<bool> EnsureMobileToolsAsync(CleanupOptions options)
    {
        if (options.AndroidData && !AdbClient.IsAvailable())
        {
            var consent = MessageBox.Show(
                this,
                "Android cleanup needs Google's Android SDK Platform Tools.\n\n" +
                "The cleaner will download the official, Google-signed Windows archive and install it only " +
                "inside its protected DiscordTraceRemover tools folder. System PATH will not be changed.\n\n" +
                "Continuing means you accept Google's Android SDK License shown at:\n" +
                "https://developer.android.com/tools/releases/platform-tools\n\nDownload and install now?",
                "Install Android Platform Tools",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button2);
            if (consent != DialogResult.Yes)
            {
                return false;
            }

            if (!await InstallMobileToolAsync(
                    "INSTALLING ANDROID TOOLS…",
                    progress => MobileToolInstaller.InstallAndroidAsync(progress)))
            {
                return false;
            }
        }

        if (options.IosData && !IosDeviceClient.IsAvailable())
        {
            var consent = MessageBox.Show(
                this,
                "iOS cleanup needs idevice_id and ideviceinstaller. Upstream does not provide an Apple-signed " +
                "Windows installer.\n\nThe cleaner can download a third-party GitHub Actions Windows build from " +
                "L1ghtmann/libimobiledevice, verify GitHub's published SHA-256 digest, and install it only " +
                "inside its protected DiscordTraceRemover tools folder. System PATH will not be changed.\n\n" +
                "These tools are independent and are not approved by Apple. The Apple Devices app may still " +
                "be needed for the Windows device driver. Download and install the tools now?",
                "Install third-party iOS tools",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (consent != DialogResult.Yes)
            {
                return false;
            }

            if (!await InstallMobileToolAsync(
                    "INSTALLING iOS TOOLS…",
                    progress => MobileToolInstaller.InstallIosAsync(progress)))
            {
                return false;
            }
        }

        return true;
    }

    private async Task<bool> InstallMobileToolAsync(
        string status,
        Func<IProgress<string>, Task> install)
    {
        SetBusy(true);
        _cleanButton.Text = "Installing…";
        SetStatus(status, DiscordTheme.Blurple);
        _results.Clear();
        IProgress<string> progress = new Progress<string>(AppendResult);

        try
        {
            await install(progress);
            AppendHeading("MOBILE TOOLS READY");
            SetStatus("TOOLS READY", DiscordTheme.Green);
            return true;
        }
        catch (Exception ex)
        {
            ShowError($"Tool installation failed: {ex.Message}");
            return false;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _previewButton.Enabled = !busy;
        _cleanButton.Enabled = !busy;
        _desktopData.Enabled = !busy;
        _chromeData.Enabled = !busy;
        _edgeData.Enabled = !busy;
        _firefoxData.Enabled = !busy;
        _androidData.Enabled = !busy;
        _iosData.Enabled = !busy;
        _windowsIntegration.Enabled = !busy;
        _cleanButton.Text = busy ? "Cleaning…" : "Clean for reinstall";
        UseWaitCursor = busy;
        if (busy)
        {
            SetStatus("CLEANING…", DiscordTheme.Blurple);
        }
    }

    private void SetStatus(string text, Color color)
    {
        _status.Text = text;
        _status.ForeColor = color;
        if (_status.Parent is not null)
        {
            _status.Left = Math.Max(0, _status.Parent.ClientSize.Width - _status.Width);
        }
    }

    private void AppendHeading(string line)
    {
        AppendColored(line + Environment.NewLine, DiscordTheme.Text, bold: true);
    }

    private void AppendMuted(string line)
    {
        AppendColored(line + Environment.NewLine, DiscordTheme.FaintText, bold: false);
    }

    private void AppendResult(string line)
    {
        AppendColored(line + Environment.NewLine, DiscordTheme.MutedText, bold: false);
    }

    private void AppendColored(string text, Color color, bool bold)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => AppendColored(text, color, bold));
            return;
        }

        _results.SelectionStart = _results.TextLength;
        _results.SelectionLength = 0;
        _results.SelectionColor = color;
        using var selectionFont = new Font(_results.Font, bold ? FontStyle.Bold : FontStyle.Regular);
        _results.SelectionFont = selectionFont;
        _results.AppendText(text);
        _results.SelectionColor = _results.ForeColor;
        _results.ScrollToCaret();
    }

    private void ShowError(string message)
    {
        SetStatus("ACTION NEEDED", DiscordTheme.Red);
        AppendResult(message);
        ShowMessage(message, "Discord Trace Remover", MessageBoxIcon.Warning);
    }

    private void ShowMessage(string message, string title, MessageBoxIcon icon)
    {
        MessageBox.Show(this, message, title, MessageBoxButtons.OK, icon);
    }
}
