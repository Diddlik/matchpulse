using System.Diagnostics;

namespace MatchPulse;

/// <summary>
/// Starts one command line (through <c>cmd.exe /c</c>) per Game Event and writes the event JSON to its stdin.
/// A non-zero exit code or a run longer than the timeout throws (the process tree is killed) and is logged by the Event Dispatcher.
/// </summary>
public sealed class ScriptSink(string command, TimeSpan timeout) : IEventSink
{
    public async Task SendAsync(GameEvent gameEvent, CancellationToken cancellationToken)
    {
        using var process = Process.Start(new ProcessStartInfo("cmd.exe", "/c " + command)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            CreateNoWindow = true,
        })!;
        try
        {
            await process.StandardInput.WriteAsync(gameEvent.ToJson());
            process.StandardInput.Close();
        }
        catch (IOException) { } // The script exited without reading stdin; its exit code decides.

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{this} did not exit within {timeout.TotalSeconds} s and was killed.");
        }
        if (process.ExitCode != 0) throw new InvalidOperationException($"{this} exited with code {process.ExitCode}.");
    }

    public override string ToString() => $"script {command}";
}
