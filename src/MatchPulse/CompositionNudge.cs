using System.Drawing;
using System.Windows.Forms;

namespace MatchPulse;

/// <summary>
/// A 1×1 pixel, nearly invisible, click-through window kept on top of the game's monitor. While it overlaps the game,
/// Windows has to compose the game into the desktop again instead of flipping it straight to the screen (independent
/// flip or a hardware overlay plane), so screen capture sees the game again. Same idea as ForceComposedFlip; it touches
/// only its own window, never the game. Costs the game at most a frame of composition. Runs on its own thread.
/// </summary>
public sealed class CompositionNudge : IDisposable
{
    readonly Thread _thread;
    Nudge? _form;

    public CompositionNudge(Rectangle monitor)
    {
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            _form = new Nudge(monitor.Location);
            _form.Shown += (_, _) => ready.Set();
            Application.Run(_form);
        }) { IsBackground = true, Name = "Composition nudge" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Wait(TimeSpan.FromSeconds(2));
    }

    public void Dispose()
    {
        try { _form?.BeginInvoke(_form.Close); } catch (InvalidOperationException) { }
        _thread.Join(TimeSpan.FromSeconds(2));
    }

    sealed class Nudge : Form
    {
        const int Layered = 0x80000, Transparent = 0x20, ToolWindow = 0x80, OnTop = 0x8, NoActivate = 0x8000000;
        readonly System.Windows.Forms.Timer _onTop = new() { Interval = 500 };

        public Nudge(Point at)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            BackColor = Color.Black;
            Bounds = new Rectangle(at, new Size(1, 1));
            Opacity = 1 / 255.0; // fully transparent windows are skipped by Windows' composition
            TopMost = true;
            // Borderless fullscreen games can sit above ordinary topmost windows: claim the top again and again.
            _onTop.Tick += (_, _) => SetWindowPos(Handle, -1 /* HWND_TOPMOST */, 0, 0, 0, 0, 0x1 | 0x2 | 0x10 /* NoSize, NoMove, NoActivate */);
            _onTop.Start();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var p = base.CreateParams;
                p.ExStyle |= Layered | Transparent | ToolWindow | OnTop | NoActivate;
                return p;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _onTop.Dispose();
            base.Dispose(disposing);
        }
    }
}
