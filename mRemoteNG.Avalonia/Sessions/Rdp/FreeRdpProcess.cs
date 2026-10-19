using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace mRemoteNG.Avalonia.Sessions.Rdp;

/// <summary>How an RDP session's program ended.</summary>
/// <param name="ExitCode">xfreerdp's exit code.</param>
/// <param name="LastError">The last error xfreerdp logged, if any.</param>
public sealed record RdpExit(int ExitCode, string? LastError);

/// <summary>A running RDP session program; an interface so the session tab can be tested without xfreerdp.</summary>
public interface IRdpProcess : IDisposable
{
    /// <summary>Raised once, on a worker thread, when the program has ended.</summary>
    event EventHandler<RdpExit>? Exited;

    /// <summary>Asks the program to end the session; it is killed if it does not.</summary>
    void Stop();
}

/// <summary>xfreerdp, given its arguments on standard input.</summary>
internal sealed class FreeRdpProcess : IRdpProcess
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(3);
    private const int SignalTerminate = 15;

    private readonly Process _process;
    private readonly Lock _lock = new();
    private string? _lastError;

    private FreeRdpProcess(Process process)
    {
        _process = process;
    }

    public event EventHandler<RdpExit>? Exited;

    /// <exception cref="SessionException">xfreerdp could not be started.</exception>
    public static FreeRdpProcess Start(FreeRdpPlan plan)
    {
        ProcessStartInfo info = new(plan.Program, ["/args-from:stdin"])
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };

        Process process;
        try
        {
            process = Process.Start(info) ?? throw new InvalidOperationException(plan.Program);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new SessionException(UiText.CouldNotStart(Path.GetFileName(plan.Program), ex.Message));
        }

        FreeRdpProcess session = new(process);
        process.OutputDataReceived += (_, e) => session.OnOutput(e.Data);
        process.ErrorDataReceived += (_, e) => session.OnOutput(e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            // One argument per line; the end of the input ends them.
            foreach (string argument in plan.Arguments)
                process.StandardInput.Write(argument + "\n");
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // xfreerdp ended at once; its exit code tells why.
        }

        _ = session.WaitAsync();
        return session;
    }

    public void Stop()
    {
        try
        {
            if (_process.HasExited)
                return;
            // xfreerdp ends the session cleanly on SIGTERM; one that hangs is killed, without blocking the caller.
            _ = kill(_process.Id, SignalTerminate);
            _ = Task.Run(() =>
            {
                try
                {
                    if (!_process.WaitForExit(StopTimeout))
                        _process.Kill();
                }
                catch (InvalidOperationException)
                {
                }
            });
        }
        catch (InvalidOperationException)
        {
            // It has already ended.
        }
    }

    // The process object is disposed once the program has ended (WaitAsync).
    public void Dispose() => Stop();

    private async Task WaitAsync()
    {
        try
        {
            await _process.WaitForExitAsync().ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        string? lastError;
        lock (_lock)
            lastError = _lastError;
        int exitCode = _process.ExitCode;
        _process.Dispose();
        Exited?.Invoke(this, new RdpExit(exitCode, lastError));
    }

    // FreeRDP logs as "[time] [pid:tid] [ERROR][com.freerdp.core] - message"; the last error says what went wrong.
    private void OnOutput(string? line)
    {
        if (line is null || !line.Contains("[ERROR]", StringComparison.Ordinal))
            return;
        int text = line.IndexOf(" - ", StringComparison.Ordinal);
        lock (_lock)
            _lastError = text >= 0 ? line[(text + 3)..].Trim() : line.Trim();
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int kill(int pid, int signal);
}
