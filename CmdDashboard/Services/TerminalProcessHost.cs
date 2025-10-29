using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CmdDashboard;

namespace CmdDashboard.Services;

public sealed class TerminalProcessHost : IDisposable
{
    static TerminalProcessHost()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        EnsureHiddenConsole();
    }

    private static Encoding ResolveTerminalEncoding()
    {
        try
        {
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }
        catch (NotSupportedException)
        {
            try
            {
                return Console.OutputEncoding;
            }
            catch
            {
                return Encoding.UTF8;
            }
        }
    }

    private readonly Process _process;
    private readonly Task _outputPumpTask;
    private readonly Task _errorPumpTask;
    private readonly CancellationTokenSource _cts = new();
    private readonly StringBuilder _buffer = new();
    private readonly object _sync = new();
    private readonly object _signalLock = new();

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

    public static TerminalProcessHost Start(string? workingDirectory = null, bool runAsAdministrator = false)
    {
        if (runAsAdministrator && !App.IsRunningAsAdministrator)
        {
            throw new InvalidOperationException("Administrator privileges are required to create an elevated terminal.");
        }

        var interpreterPath = Environment.GetEnvironmentVariable("COMSPEC");
        if (string.IsNullOrWhiteSpace(interpreterPath) || !interpreterPath.EndsWith("cmd.exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(interpreterPath))
        {
            interpreterPath = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        }
        var oemEncoding = ResolveTerminalEncoding();

        var startInfo = new ProcessStartInfo
        {
            FileName = interpreterPath,
            Arguments = "/K",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = false,
            StandardInputEncoding = oemEncoding,
            StandardOutputEncoding = oemEncoding,
            StandardErrorEncoding = oemEncoding,
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

    public bool TrySendCtrlC()
    {
        if (_process.HasExited)
        {
            return false;
        }

        if (SendConsoleControlEvent())
        {
            return true;
        }

        return TryWriteControlC();
    }

    private bool TryWriteControlC()
    {
        try
        {
            if (_process.HasExited)
            {
                return false;
            }

            _process.StandardInput.Write('\u0003');
            _process.StandardInput.Flush();
            return true;
        }
        catch
        {
            return false;
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

    private bool SendConsoleControlEvent()
    {
        lock (_signalLock)
        {
            if (_process.HasExited)
            {
                return false;
            }

            var handlerInstalled = false;
            try
            {
                handlerInstalled = NativeMethods.SetConsoleCtrlHandler(NativeMethods.IgnoreCtrlDelegate, true);
                if (!handlerInstalled)
                {
                    return false;
                }

                if (!NativeMethods.GenerateConsoleCtrlEvent(NativeMethods.CTRL_C_EVENT, 0))
                {
                    return false;
                }

                Thread.Sleep(150);

                return true;
            }
            finally
            {
                if (handlerInstalled)
                {
                    NativeMethods.SetConsoleCtrlHandler(NativeMethods.IgnoreCtrlDelegate, false);
                }
            }
        }
    }

    private static void EnsureHiddenConsole()
    {
        try
        {
            var consoleWindow = NativeMethods.GetConsoleWindow();
            var created = false;
            if (consoleWindow == IntPtr.Zero)
            {
                if (!NativeMethods.AllocConsole())
                {
                    return;
                }

                consoleWindow = NativeMethods.GetConsoleWindow();
                created = true;
            }

            if (created && consoleWindow != IntPtr.Zero)
            {
                NativeMethods.ShowWindow(consoleWindow, NativeMethods.SW_HIDE);
            }
        }
        catch
        {
            // intentionally ignored - managing the console is best-effort only
        }
    }


    private static class NativeMethods
    {
        internal const uint CTRL_C_EVENT = 0;
        internal const int SW_HIDE = 0;

        internal static readonly ConsoleCtrlDelegate IgnoreCtrlDelegate = _ => true;

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate handler, bool add);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr GetConsoleWindow();

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool AllocConsole();

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        internal delegate bool ConsoleCtrlDelegate(uint ctrlType);
    }
}
