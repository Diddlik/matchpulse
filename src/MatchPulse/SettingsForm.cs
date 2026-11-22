using System.Drawing;
using System.Windows.Forms;

namespace MatchPulse;

/// <summary>Edits game language, UI language, webhooks and scripts, and shows the log file.</summary>
sealed class SettingsForm : Form
{
    public SettingsForm(Config config, string logPath, Action<Config> apply)
    {
        string T(string key) => Ui.T(config.UiLanguage, key);
        Text = $"MatchPulse - {T("Settings")}";
        Size = new Size(640, 560);
        StartPosition = FormStartPosition.CenterScreen;

        var gameLanguage = Combo(config.GameLanguage);
        var uiLanguage = Combo(config.UiLanguage);
        var webhooks = Lines(config.Webhooks.Select(u => u.ToString()));
        var scripts = Lines(config.Scripts);
        var save = new Button { Text = T("Save"), AutoSize = true };
        save.Click += (_, _) =>
        {
            try
            {
                apply(config with
                {
                    GameLanguage = (string)gameLanguage.SelectedItem!,
                    UiLanguage = (string)uiLanguage.SelectedItem!,
                    Webhooks = Split(webhooks.Text).Select(line => new Uri(line)).ToList(),
                    Scripts = Split(scripts.Text),
                });
                MessageBox.Show(this, T("Saved"), Text);
            }
            catch (Exception e) when (e is UriFormatException or InvalidDataException or IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(this, e.Message, T("Invalid"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };

        var form = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(8), AutoScroll = true };
        form.Controls.AddRange([
            Label(T("GameLanguage")), gameLanguage, Label(T("UiLanguage")), uiLanguage,
            Label(T("Webhooks")), webhooks, Label(T("Scripts")), scripts, save,
        ]);

        var log = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font(FontFamily.GenericMonospace, 9) };
        var refresh = new Button { Text = T("Refresh"), Dock = DockStyle.Bottom };
        void LoadLog()
        {
            if (!File.Exists(logPath)) return;
            using var reader = new StreamReader(new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
            log.Text = reader.ReadToEnd();
            log.SelectionStart = log.TextLength;
            log.ScrollToCaret();
        }
        refresh.Click += (_, _) => LoadLog();

        var settingsTab = new TabPage(T("Settings"));
        settingsTab.Controls.Add(form);
        var logTab = new TabPage(T("Log"));
        logTab.Controls.Add(log);
        logTab.Controls.Add(refresh);
        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.AddRange([settingsTab, logTab]);
        tabs.SelectedIndexChanged += (_, _) => { if (tabs.SelectedTab == logTab) LoadLog(); };
        Controls.Add(tabs);
    }

    static ComboBox Combo(string selected)
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80 };
        combo.Items.AddRange(Config.Languages);
        combo.SelectedItem = selected;
        return combo;
    }

    static TextBox Lines(IEnumerable<string> lines) =>
        new() { Text = string.Join(Environment.NewLine, lines), Multiline = true, ScrollBars = ScrollBars.Vertical, Width = 580, Height = 100 };

    static Label Label(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(0, 8, 0, 2) };

    static List<string> Split(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
