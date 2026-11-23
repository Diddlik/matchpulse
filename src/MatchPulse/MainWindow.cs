using System.Drawing;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MatchPulse;

/// <summary>
/// The main window: the HTML UI in <c>ui/</c> inside WebView2, connected to the <see cref="UiHub"/>.
/// Diagnostic snapshots and kill pictures are served from <see cref="UiHub.ImageHost"/>. A passive window (the live
/// monitor on a second screen) never takes the focus when it opens.
/// </summary>
sealed class MainWindow : Form
{
    const string AppHost = "app.matchpulse";
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    readonly UiHub _hub;
    readonly WebView2 _view = new() { Dock = DockStyle.Fill };
    readonly bool _passive;
    string? _startPage;
    bool _ready;

    public MainWindow(UiHub hub, Icon icon, string? startPage = null, bool passive = false)
    {
        _hub = hub;
        _startPage = startPage;
        _passive = passive;
        Text = passive ? "MatchPulse – Live" : "MatchPulse";
        Icon = icon;
        ClientSize = passive ? new Size(1100, 760) : new Size(1240, 820);
        MinimumSize = new Size(760, 520);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(0x16, 0x18, 0x1c);
        Controls.Add(_view);
        if (passive) StartPosition = FormStartPosition.WindowsDefaultLocation;
        Load += async (_, _) => await StartAsync();
        FormClosed += (_, _) => _hub.Post -= OnPost;
    }

    protected override bool ShowWithoutActivation => _passive;

    /// <summary>Shows a page, e.g. after a click on a problem notification.</summary>
    public void Navigate(string page)
    {
        if (_ready) Send("navigate", new JsonObject { ["page"] = page });
        else _startPage = page;
    }

    async Task StartAsync()
    {
        try
        {
            var userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MatchPulse", "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(null, userData);
            await _view.EnsureCoreWebView2Async(environment);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show(this, "Microsoft Edge WebView2 Runtime is missing: https://developer.microsoft.com/microsoft-edge/webview2/", "MatchPulse", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
            return;
        }

        var core = _view.CoreWebView2;
        core.Settings.AreDevToolsEnabled = System.Diagnostics.Debugger.IsAttached;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        Directory.CreateDirectory(_hub.DataFolder);
        core.SetVirtualHostNameToFolderMapping(AppHost, Path.Combine(AppContext.BaseDirectory, "ui"), CoreWebView2HostResourceAccessKind.DenyCors);
        core.SetVirtualHostNameToFolderMapping(UiHub.ImageHost, _hub.DataFolder, CoreWebView2HostResourceAccessKind.DenyCors);
        // The UI never leaves its own pages.
        core.NavigationStarting += (_, e) => e.Cancel = !e.Uri.StartsWith($"https://{AppHost}/", StringComparison.OrdinalIgnoreCase);
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.WebMessageReceived += (_, e) =>
        {
            var json = e.WebMessageAsJson;
            if ((string?)JsonNode.Parse(json)?["type"] == "ready") _ready = true;
            Task.Run(() => _hub.Receive(json));
        };
        _hub.Post += OnPost;
        core.Navigate($"https://{AppHost}/index.html" + (_startPage is null ? "" : $"?page={Uri.EscapeDataString(_startPage)}"));
    }

    void OnPost(string type, JsonNode? payload) => Send(type, payload);

    void Send(string type, JsonNode? payload)
    {
        var json = new JsonObject { ["type"] = type, ["payload"] = payload?.DeepClone() }.ToJsonString(Json);
        if (IsDisposed) return;
        try
        {
            BeginInvoke(() => { if (!IsDisposed && _view.CoreWebView2 is { } core) core.PostWebMessageAsJson(json); });
        }
        catch (InvalidOperationException) { } // window closing
    }
}
