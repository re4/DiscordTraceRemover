using System.Drawing.Drawing2D;

namespace DiscordTraceRemover;

internal static class DiscordTheme
{
    internal static readonly Color WindowBorder = Color.FromArgb(17, 18, 20);
    internal static readonly Color TitleBar = Color.FromArgb(30, 31, 34);
    internal static readonly Color ServerRail = Color.FromArgb(30, 31, 34);
    internal static readonly Color Sidebar = Color.FromArgb(43, 45, 49);
    internal static readonly Color Main = Color.FromArgb(49, 51, 56);
    internal static readonly Color Card = Color.FromArgb(43, 45, 49);
    internal static readonly Color CardHover = Color.FromArgb(53, 55, 60);
    internal static readonly Color CardChecked = Color.FromArgb(57, 59, 65);
    internal static readonly Color Input = Color.FromArgb(30, 31, 34);
    internal static readonly Color Blurple = Color.FromArgb(88, 101, 242);
    internal static readonly Color BlurpleHover = Color.FromArgb(71, 82, 196);
    internal static readonly Color Green = Color.FromArgb(35, 165, 90);
    internal static readonly Color Red = Color.FromArgb(218, 55, 60);
    internal static readonly Color Text = Color.FromArgb(242, 243, 245);
    internal static readonly Color MutedText = Color.FromArgb(181, 186, 193);
    internal static readonly Color FaintText = Color.FromArgb(148, 155, 164);
    internal static readonly Color Divider = Color.FromArgb(63, 65, 71);

    internal static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var diameter = Math.Max(2, radius * 2);
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class DiscordOptionCard : Control
{
    private bool _checked;
    private bool _hovered;

    internal string Title { get; }
    internal string Description { get; }

    internal bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value)
            {
                return;
            }

            _checked = value;
            Invalidate();
        }
    }

    internal DiscordOptionCard(string title, string description)
    {
        Title = title;
        Description = description;
        Height = 66;
        Dock = DockStyle.Fill;
        Margin = new Padding(0, 0, 0, 8);
        Cursor = Cursors.Hand;
        TabStop = true;
        DoubleBuffered = true;
        AccessibleName = title;
        AccessibleDescription = description;
        AccessibleRole = AccessibleRole.CheckButton;
    }

    protected override void OnClick(EventArgs e)
    {
        if (Enabled)
        {
            Checked = !Checked;
        }

        base.OnClick(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Enabled && e.KeyCode is Keys.Space or Keys.Enter)
        {
            Checked = !Checked;
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        Invalidate();
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        Invalidate();
        base.OnLostFocus(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        using var cardPath = DiscordTheme.RoundedRectangle(bounds, 8);
        var background = !Enabled
            ? DiscordTheme.Card
            : Checked
                ? DiscordTheme.CardChecked
                : _hovered
                    ? DiscordTheme.CardHover
                    : DiscordTheme.Card;
        using (var backgroundBrush = new SolidBrush(background))
        {
            e.Graphics.FillPath(backgroundBrush, cardPath);
        }

        if (Focused)
        {
            using var focusPen = new Pen(DiscordTheme.Blurple, 1.5F);
            e.Graphics.DrawPath(focusPen, cardPath);
        }

        var checkBounds = new Rectangle(17, (Height - 20) / 2, 20, 20);
        using var checkPath = DiscordTheme.RoundedRectangle(checkBounds, 5);
        using var checkBrush = new SolidBrush(Checked ? DiscordTheme.Blurple : Color.FromArgb(64, 66, 73));
        e.Graphics.FillPath(checkBrush, checkPath);

        if (Checked)
        {
            using var checkPen = new Pen(Color.White, 2F)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            e.Graphics.DrawLines(checkPen, new Point[]
            {
                new Point(checkBounds.Left + 5, checkBounds.Top + 10),
                new Point(checkBounds.Left + 9, checkBounds.Top + 14),
                new Point(checkBounds.Left + 16, checkBounds.Top + 6)
            });
        }

        var titleColor = Enabled ? DiscordTheme.Text : DiscordTheme.FaintText;
        var descriptionColor = Enabled ? DiscordTheme.MutedText : DiscordTheme.FaintText;
        using var titleBrush = new SolidBrush(titleColor);
        using var descriptionBrush = new SolidBrush(descriptionColor);
        using var titleFont = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
        using var descriptionFont = new Font("Segoe UI", 8.75F);
        e.Graphics.DrawString(Title, titleFont, titleBrush, 51, 11);
        using var descriptionFormat = new StringFormat
        {
            Trimming = StringTrimming.EllipsisCharacter
        };
        var descriptionBounds = new RectangleF(51, 34, Math.Max(1, Width - 64), Math.Max(1, Height - 39));
        e.Graphics.DrawString(Description, descriptionFont, descriptionBrush, descriptionBounds, descriptionFormat);
    }
}

internal sealed class DiscordButton : Button
{
    internal int CornerRadius { get; set; } = 4;

    internal DiscordButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        ForeColor = DiscordTheme.Text;
        Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold);
        Cursor = Cursors.Hand;
        UseVisualStyleBackColor = false;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        using var path = DiscordTheme.RoundedRectangle(new Rectangle(0, 0, Width, Height), CornerRadius);
        Region = new Region(path);
    }
}
