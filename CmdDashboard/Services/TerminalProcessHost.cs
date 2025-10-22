using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CmdDashboard.Services;

public sealed class TerminalProcessHost : IDisposable
{
    private readonly Process _process;
    private readonly Task _outputPumpTask;
    private readonly Task _errorPumpTask;
    private readonly CancellationTokenSource _cts = new();
    private readonly StringBuilder _buffer = new();
    private readonly object _sync = new();

    public event Action<string>? OutputReceived;
    public event Action<int>? Exited;

    private TerminalProcessHost(Process process)
    {
        _process = process;
        _outputPumpTask = Task.Run(() => PumpAsync(process.StandardOutput, _cts.Token));
        _errorPumpTask = Task.Run(() => PumpAsync(process.StandardError, _cts.Token));
        _process.EnableRaisingEvents = true;
        _process.Exited += (_, _) => Exited?.Invoke(_process.ExitCode);
    }

    public static TerminalProcessHost Start(string? workingDirectory = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe",
            Arguments = "/K",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                : workingDirectory
        };
        startInfo.EnvironmentVariables["PROMPT"] = "$P$G$_";

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start command prompt process.");
        }

        process.StandardInput.WriteLine();
        process.StandardInput.Flush();

        return new TerminalProcessHost(process);
    }

    private async Task PumpAsync(StreamReader reader, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                lock (_sync)
                {
                    _buffer.AppendLine(line);
                }

                OutputReceived?.Invoke(line + Environment.NewLine);
            }
        }
        catch (OperationCanceledException)
        {
            // ignored
        }
    }

    public void Send(string command)
    {
        if (_process.HasExited)
        {
            return;
        }

        _process.StandardInput.WriteLine(command);
        _process.StandardInput.Flush();
    }

    public void Stop()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.StandardInput.WriteLine("exit");
                _process.StandardInput.Flush();
                if (!_process.WaitForExit(1000))
                {
                    _process.Kill(true);
                }
            }
        }
        catch
        {
            // ignored
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        Stop();
        try
        {
            Task.WaitAll(new[] { _outputPumpTask, _errorPumpTask }, TimeSpan.FromSeconds(1));
        }
        catch
        {
            // ignored
        }

        _process.Dispose();
    }
}
