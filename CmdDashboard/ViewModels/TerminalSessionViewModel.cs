using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using CmdDashboard.Models;
using CmdDashboard.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CmdDashboard.ViewModels;

public partial class TerminalSessionViewModel : ObservableObject, IDisposable
{
    private TerminalProcessHost? _processHost;
    private readonly StringBuilder _buffer = new();
    private readonly SynchronizationContext _syncContext;
    private readonly List<string> _history = new();

    private int _historyIndex = -1;
    private string? _historyDraft;
    private bool _isRecalling;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _output = string.Empty;

    [ObservableProperty]
    private string _pendingInput = string.Empty;

    [ObservableProperty]
    private bool _isRunning = true;

    [ObservableProperty]
    private bool _isInteractive = true;

    public IRelayCommand SendInputCommand { get; }
    public IRelayCommand ClearOutputCommand { get; }
    public IRelayCommand StopCommand { get; }
    public IRelayCommand RestartCommand { get; }

    public string? WorkingDirectory { get; }
    public string? StartupCommand { get; }
    public bool RunAsAdministrator { get; }

    private TerminalSessionViewModel(string title, string? workingDirectory, string? startCommand, bool runAsAdministrator, string? initialOutput, bool runStartupCommands, bool interactive)
    {
        _syncContext = SynchronizationContext.Current ?? new SynchronizationContext();
        _title = title;
        WorkingDirectory = workingDirectory;
        StartupCommand = startCommand;
        RunAsAdministrator = runAsAdministrator;
        SendInputCommand = new RelayCommand(SendInput, CanSendInput);
        ClearOutputCommand = new RelayCommand(ClearOutput, CanClearOutput);
        StopCommand = new RelayCommand(StopSession, CanStopSession);
        RestartCommand = new RelayCommand(RestartSession, CanRestartSession);

        _processHost = null;
        if (interactive)
        {
            InitializeProcessHost(runStartupCommands);
        }

        if (!string.IsNullOrEmpty(initialOutput))
        {
            _buffer.Append(initialOutput);
            Output = _buffer.ToString();
        }

        if (!interactive)
        {
            IsRunning = false;
        }

        IsInteractive = interactive;
    }

    public static TerminalSessionViewModel CreateInteractive(string title, string? workingDirectory = null, string? startCommand = null, bool runAsAdministrator = false)
    {
        return new TerminalSessionViewModel(title, workingDirectory, startCommand, runAsAdministrator, initialOutput: null, runStartupCommands: true, interactive: true);
    }

    public static TerminalSessionViewModel Restore(TerminalSessionSnapshot snapshot, bool interactive = true)
    {
        return new TerminalSessionViewModel(
            snapshot.Title,
            snapshot.WorkingDirectory,
            snapshot.StartupCommand,
            snapshot.RunAsAdministrator,
            snapshot.Output,
            runStartupCommands: false,
            interactive: interactive);
    }

    public TerminalSessionSnapshot Capture()
    {
        return new TerminalSessionSnapshot(Title, WorkingDirectory, StartupCommand, RunAsAdministrator, Output);
    }

    private bool CanSendInput() => IsInteractive && IsRunning && !string.IsNullOrWhiteSpace(PendingInput);

    private bool CanClearOutput() => IsInteractive;

    private bool CanStopSession() => IsInteractive && IsRunning;

    private bool CanRestartSession() => IsInteractive && !IsRunning;

    partial void OnPendingInputChanged(string value)
    {
        SendInputCommand.NotifyCanExecuteChanged();

        if (_isRecalling)
        {
            _isRecalling = false;
            return;
        }

        _historyIndex = -1;
        _historyDraft = value;
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
        _processHost?.Send(command);
    }

    private void SendInput()
    {
        if (!IsInteractive || !IsRunning)
        {
            return;
        }

        var command = PendingInput.TrimEnd();
        PendingInput = string.Empty;
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        _buffer.AppendLine($"> {command}");
        Output = _buffer.ToString();
        SendRaw(command);

        _history.Add(command);
        ResetHistoryTraversal();
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
        if (!IsInteractive)
        {
            return;
        }

        _buffer.Clear();
        Output = string.Empty;
    }

    private void StopSession()
    {
        if (!IsInteractive)
        {
            return;
        }

        _processHost?.Stop();
        if (IsRunning)
        {
            IsRunning = false;
        }
    }

    private void RestartSession()
    {
        if (!CanRestartSession())
        {
            return;
        }

        DisposeProcessHost();
        InitializeProcessHost(runStartupCommands: true);

        _buffer.AppendLine($"Process restarted at {DateTime.Now:T}.");
        Output = _buffer.ToString();
        ResetHistoryTraversal();
    }

    private void InitializeProcessHost(bool runStartupCommands)
    {
        DisposeProcessHost();

        _processHost = TerminalProcessHost.Start(WorkingDirectory, RunAsAdministrator);
        _processHost.OutputReceived += AppendOutput;
        _processHost.Exited += OnExited;

        if (!IsRunning)
        {
            IsRunning = true;
        }

        StopCommand.NotifyCanExecuteChanged();
        RestartCommand.NotifyCanExecuteChanged();

        if (runStartupCommands)
        {
            InitializeStartupCommands(StartupCommand);
        }
    }

    private void DisposeProcessHost()
    {
        if (_processHost is null)
        {
            return;
        }

        _processHost.OutputReceived -= AppendOutput;
        _processHost.Exited -= OnExited;
        _processHost.Dispose();
        _processHost = null;
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
            RestartCommand.NotifyCanExecuteChanged();
        }, null);
    }

    partial void OnIsRunningChanged(bool value)
    {
        StopCommand.NotifyCanExecuteChanged();
        RestartCommand.NotifyCanExecuteChanged();
        SendInputCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        DisposeProcessHost();
    }

    public bool RecallPrevious()
    {
        if (_history.Count == 0)
        {
            return false;
        }

        if (_historyIndex == -1)
        {
            _historyIndex = _history.Count;
        }

        if (_historyIndex > 0)
        {
            _historyIndex--;
        }

        if (_historyIndex >= _history.Count)
        {
            _historyIndex = _history.Count - 1;
        }

        if (_historyIndex < 0)
        {
            _historyIndex = 0;
        }

        SetPendingInputFromHistory(_history[_historyIndex]);
        return true;
    }

    public bool RecallNext()
    {
        if (_historyIndex == -1)
        {
            if (_historyDraft is null)
            {
                return false;
            }

            SetPendingInputFromHistory(_historyDraft);
            _historyDraft = null;
            return true;
        }

        if (_history.Count == 0)
        {
            return false;
        }

        _historyIndex++;

        if (_historyIndex >= _history.Count)
        {
            SetPendingInputFromHistory(_historyDraft ?? string.Empty);
            _historyDraft = null;
            _historyIndex = -1;
            return true;
        }

        SetPendingInputFromHistory(_history[_historyIndex]);
        return true;
    }

    private void SetPendingInputFromHistory(string command)
    {
        _isRecalling = true;
        PendingInput = command;
    }

    private void ResetHistoryTraversal()
    {
    _historyIndex = -1;
    _historyDraft = null;
    }

    partial void OnIsInteractiveChanged(bool value)
    {
        SendInputCommand.NotifyCanExecuteChanged();
        ClearOutputCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        RestartCommand.NotifyCanExecuteChanged();
    }
}
