using System;
using System.Text;
using System.Threading;
using CmdDashboard.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CmdDashboard.ViewModels;

public partial class TerminalSessionViewModel : ObservableObject, IDisposable
{
    private readonly TerminalProcessHost _processHost;
    private readonly StringBuilder _buffer = new();
    private readonly SynchronizationContext _syncContext;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _output = string.Empty;

    [ObservableProperty]
    private string _pendingInput = string.Empty;

    [ObservableProperty]
    private bool _isRunning = true;

    public IRelayCommand SendInputCommand { get; }
    public IRelayCommand ClearOutputCommand { get; }
    public IRelayCommand StopCommand { get; }

    private TerminalSessionViewModel(string title, string? workingDirectory, string? startCommand)
    {
        _syncContext = SynchronizationContext.Current ?? new SynchronizationContext();
        _title = title;
        _processHost = TerminalProcessHost.Start(workingDirectory);
        _processHost.OutputReceived += AppendOutput;
        _processHost.Exited += OnExited;

        SendInputCommand = new RelayCommand(SendInput, CanSendInput);
        ClearOutputCommand = new RelayCommand(ClearOutput);
        StopCommand = new RelayCommand(StopSession, () => IsRunning);

        InitializeStartupCommands(startCommand);
    }

    public static TerminalSessionViewModel CreateInteractive(string title, string? workingDirectory = null, string? startCommand = null)
    {
        return new TerminalSessionViewModel(title, workingDirectory, startCommand);
    }

    private bool CanSendInput() => !string.IsNullOrWhiteSpace(PendingInput);

    partial void OnPendingInputChanged(string value)
    {
        SendInputCommand.NotifyCanExecuteChanged();
    }

    private void AppendOutput(string text)
    {
        _syncContext.Post(_ =>
        {
            _buffer.Append(text);
            Output = _buffer.ToString();
        }, null);
    }

    private void SendRaw(string command)
    {
        _processHost.Send(command);
    }

    private void SendInput()
    {
        var command = PendingInput.TrimEnd();
        PendingInput = string.Empty;
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        _buffer.AppendLine($"> {command}");
        Output = _buffer.ToString();
        SendRaw(command);
    }

    private void InitializeStartupCommands(string? startCommand)
    {
        if (string.IsNullOrWhiteSpace(startCommand))
        {
            return;
        }

        var commands = startCommand
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var command in commands)
        {
            _buffer.AppendLine($"> {command}");
            Output = _buffer.ToString();
            SendRaw(command);
        }
    }

    private void ClearOutput()
    {
        _buffer.Clear();
        Output = string.Empty;
    }

    private void StopSession()
    {
        _processHost.Stop();
        if (IsRunning)
        {
            IsRunning = false;
        }
    }

    private void OnExited(int exitCode)
    {
        _syncContext.Post(_ =>
        {
            if (IsRunning)
            {
                IsRunning = false;
                _buffer.AppendLine($"Process exited with code {exitCode}.");
                Output = _buffer.ToString();
            }
            StopCommand.NotifyCanExecuteChanged();
        }, null);
    }

    partial void OnIsRunningChanged(bool value)
    {
        StopCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        _processHost.OutputReceived -= AppendOutput;
        _processHost.Exited -= OnExited;
        _processHost.Dispose();
    }
}
