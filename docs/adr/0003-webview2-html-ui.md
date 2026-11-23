# 3. Main window as HTML in WebView2

The main window is a static HTML/JS page (`src/MatchPulse/ui/`) hosted in WebView2 inside the WinForms app, talking to the host only through JSON messages (`ui/README.md`). It replaces the WinForms settings form.

We chose it because the UI is designed outside the code (Claude Design produced the HTML template), and a page can be iterated in a browser against mock data without building the app. The cost is a dependency on the Edge WebView2 Runtime (part of Windows 11) and a message contract that host and page must keep in step; the contract is covered by `UiHubTests`, not by tests of the page itself.

Considered: native WinForms (no runtime dependency, but every design change is hand-written C#) and WPF/WinUI (a rewrite of the tray app for no gain in reaching the design).
