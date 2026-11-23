using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MatchPulse;

/// <summary>
/// A short notice at the top of the screen, above all windows: it never takes the focus, lets clicks through and fades
/// out by itself. Shown on the screen under the mouse pointer (the game's screen while playing). Windows shows no other
/// window above a game in exclusive fullscreen; borderless and windowed modes work.
/// </summary>
sealed class Overlay : Form
{
    const int WsExTopmost = 0x8, WsExTransparent = 0x20, WsExToolWindow = 0x80, WsExNoActivate = 0x08000000;
    static Overlay? _current;
    readonly string _text;
    readonly Color _accent;
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 30 };
    int _ticks;

    Overlay(string text, Color accent)
    {
        _text = text;
        _accent = accent;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(22, 26, 32);
        Font = new Font("Segoe UI Semibold", 15f);
        DoubleBuffered = true;
        var textSize = TextRenderer.MeasureText(text, Font);
        Size = new Size(textSize.Width + 76, 56);
        var screen = Screen.FromPoint(Cursor.Position).Bounds;
        Location = new Point(screen.Left + (screen.Width - Width) / 2, screen.Top + screen.Height / 12);
        Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, Width + 1, Height + 1, 18, 18));
        Opacity = 0.94;
        _timer.Tick += (_, _) => Fade();
    }

    /// <summary>Shows the notice, replacing one still on screen.</summary>
    public static void Show(string text, Color accent)
    {
        _current?.Close();
        _current = new Overlay(text, accent);
        _current.Show();
        _current._timer.Start();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var p = base.CreateParams;
            p.ExStyle |= WsExTopmost | WsExTransparent | WsExToolWindow | WsExNoActivate;
            return p;
        }
    }

    // Visible for about 1.5 s, then fades out over about 0.4 s.
    void Fade()
    {
        if (++_ticks < 50) return;
        Opacity -= 0.07;
        if (Opacity > 0.05) return;
        _timer.Stop();
        Close();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var border = new Pen(Color.FromArgb(70, _accent), 2))
            g.DrawPath(border, Rounded(new Rectangle(1, 1, Width - 3, Height - 3), 9));
        using (var dot = new SolidBrush(_accent))
            g.FillEllipse(dot, 22, Height / 2 - 7, 14, 14);
        TextRenderer.DrawText(g, _text, Font, new Rectangle(48, 0, Width - 56, Height), Color.FromArgb(230, 237, 243),
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
    }

    static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            if (_current == this) _current = null;
        }
        base.Dispose(disposing);
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
}
