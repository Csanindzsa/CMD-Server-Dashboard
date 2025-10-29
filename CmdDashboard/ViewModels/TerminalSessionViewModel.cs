using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
    private bool _isAutoCompleting;
    private AutoCompleteSession? _autoCompleteSession;

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

        if (_isAutoCompleting)
        {
            _isAutoCompleting = false;
            return;
        }

        _historyIndex = -1;
        _historyDraft = value;
        ResetAutoCompleteSession();
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

        ResetAutoCompleteSession();

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
        ResetAutoCompleteSession();
    }

    private void ResetHistoryTraversal()
    {
        _historyIndex = -1;
        _historyDraft = null;
        ResetAutoCompleteSession();
    }

    public bool TryInterrupt()
    {
        if (!IsInteractive)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(PendingInput))
        {
            PendingInput = string.Empty;
        }

        _buffer.AppendLine("^C");
        _buffer.AppendLine("Restarting session...");
        Output = _buffer.ToString();

        try
        {
            InitializeProcessHost(runStartupCommands: true);
        }
        catch (Exception ex)
        {
            _buffer.AppendLine($"Restart failed: {ex.Message}");
            Output = _buffer.ToString();
            return false;
        }

        _buffer.AppendLine($"Process restarted at {DateTime.Now:T}.");
        Output = _buffer.ToString();
        ResetHistoryTraversal();
        return true;
    }

    public bool TryAutoComplete(bool reverse, string currentText, int caretIndex, out int newCaretIndex)
    {
        newCaretIndex = caretIndex;

        if (!IsInteractive)
        {
            return false;
        }

        if (caretIndex < 0 || caretIndex > currentText.Length)
        {
            return false;
        }

        var session = _autoCompleteSession;
        if (session is null || !session.CanReuse(currentText, caretIndex))
        {
            session = CreateAutoCompleteSession(currentText, caretIndex);
            if (session is null)
            {
                _autoCompleteSession = null;
                return false;
            }

            _autoCompleteSession = session;
        }

        if (session.Candidates.Count == 0)
        {
            _autoCompleteSession = null;
            return false;
        }

        var count = session.Candidates.Count;
        if (session.CurrentIndex < 0)
        {
            session.CurrentIndex = reverse ? count - 1 : 0;
        }
        else
        {
            session.CurrentIndex = (session.CurrentIndex + (reverse ? -1 : 1) + count) % count;
        }

        var (completedText, caret) = session.ApplyCandidate(session.CurrentIndex);

        _isAutoCompleting = true;
        PendingInput = completedText;
        if (_isAutoCompleting)
        {
            _isAutoCompleting = false;
        }
        newCaretIndex = caret;
        return true;
    }

    partial void OnIsInteractiveChanged(bool value)
    {
        SendInputCommand.NotifyCanExecuteChanged();
        ClearOutputCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        RestartCommand.NotifyCanExecuteChanged();
    }

    private void ResetAutoCompleteSession()
    {
        _autoCompleteSession = null;
    }

    private AutoCompleteSession? CreateAutoCompleteSession(string text, int caretIndex)
    {
        var lineStart = FindLineStart(text, caretIndex);
        var tokenStart = FindTokenStart(text, caretIndex);

        var linePrefix = text.Substring(lineStart, caretIndex - lineStart);
        var tokenPrefix = text.Substring(tokenStart, caretIndex - tokenStart);

        var candidates = GatherAutoCompleteCandidates(text, caretIndex, lineStart, linePrefix, tokenStart, tokenPrefix);
        if (candidates.Count == 0)
        {
            return null;
        }

        return new AutoCompleteSession(text, caretIndex, candidates);
    }

    private List<AutoCompleteCandidate> GatherAutoCompleteCandidates(
        string text,
        int caretIndex,
        int lineStart,
        string linePrefix,
        int tokenStart,
        string tokenPrefix)
    {
        var candidates = new List<AutoCompleteCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var context = ExtractCommandContext(linePrefix);
        var directoriesOnly = ShouldRestrictToDirectories(context);
        var subcommandCandidates = BuildSubcommandCandidates(context, tokenPrefix, tokenStart, caretIndex);
        var fileCandidates = BuildFileSystemCandidates(tokenPrefix, tokenStart, caretIndex, directoriesOnly);
        var commandCandidates = BuildCommandCandidates(tokenPrefix, tokenStart, caretIndex);
        var historyCandidates = BuildHistoryCandidates(linePrefix, lineStart, caretIndex);

        var looksLikePath = LooksLikePathToken(tokenPrefix);

        void AddRange(IEnumerable<AutoCompleteCandidate> source)
        {
            foreach (var candidate in source)
            {
                var key = FormCandidateKey(candidate);
                if (seen.Add(key))
                {
                    candidates.Add(candidate);
                }
            }
        }

        if (looksLikePath)
        {
            AddRange(subcommandCandidates);
            AddRange(fileCandidates);
            AddRange(historyCandidates);
            AddRange(commandCandidates);
        }
        else
        {
            AddRange(subcommandCandidates);
            AddRange(historyCandidates);
            AddRange(commandCandidates);
            AddRange(fileCandidates);
        }

        return candidates;
    }

    private static string FormCandidateKey(AutoCompleteCandidate candidate)
    {
        return $"{candidate.Start}:{candidate.Length}:{candidate.Replacement}";
    }

    private IEnumerable<AutoCompleteCandidate> BuildHistoryCandidates(string linePrefix, int lineStart, int caretIndex)
    {
        if (string.IsNullOrWhiteSpace(linePrefix))
        {
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _history.AsEnumerable().Reverse())
        {
            if (!entry.StartsWith(linePrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!seen.Add(entry))
            {
                continue;
            }

            yield return new AutoCompleteCandidate(lineStart, caretIndex - lineStart, entry);
        }
    }

    private IEnumerable<AutoCompleteCandidate> BuildCommandCandidates(string tokenPrefix, int tokenStart, int caretIndex)
    {
        var prefix = tokenPrefix.TrimStart('"');
        if (string.IsNullOrEmpty(prefix))
        {
            yield break;
        }

        if (LooksLikePathToken(prefix))
        {
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in EnumerateCommandNames(prefix))
        {
            if (!seen.Add(name))
            {
                continue;
            }

            yield return new AutoCompleteCandidate(tokenStart, caretIndex - tokenStart, name);
        }
    }

    private IEnumerable<string> EnumerateCommandNames(string prefix)
    {
        foreach (var builtin in BuiltInCommands)
        {
            if (builtin.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                yield return builtin;
            }
        }

        foreach (var name in EnumerateLocalExecutables(prefix))
        {
            yield return name;
        }

        foreach (var name in GlobalExecutableNames.Value)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                yield return name;
            }
        }
    }

    private IEnumerable<string> EnumerateLocalExecutables(string prefix)
    {
        var workingDirectory = ResolveWorkingDirectory();
        if (string.IsNullOrEmpty(workingDirectory) || !Directory.Exists(workingDirectory))
        {
            yield break;
        }

        var extensions = ExecutableExtensions.Value;
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(workingDirectory);
        }
        catch
        {
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var extension = Path.GetExtension(file);
            if (!extensions.Contains(extension))
            {
                continue;
            }

            var name = Path.GetFileNameWithoutExtension(file);
            if (!seen.Add(name))
            {
                continue;
            }

            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                yield return name;
            }
        }
    }

    private IEnumerable<AutoCompleteCandidate> BuildFileSystemCandidates(string tokenPrefix, int tokenStart, int caretIndex, bool directoriesOnly)
    {
        var prefix = tokenPrefix;
        var hasLeadingQuote = false;
        if (prefix.StartsWith("\"", StringComparison.Ordinal))
        {
            hasLeadingQuote = true;
            prefix = prefix[1..];
        }

    var normalizedPrefix = prefix.Replace("/", "\\");

        var typedDirectoryPart = string.Empty;
        var normalizedDirectoryPart = string.Empty;
        var searchTerm = prefix;

        var lastSeparator = FindLastPathSeparator(prefix);
        if (lastSeparator >= 0)
        {
            typedDirectoryPart = prefix[..(lastSeparator + 1)];
            normalizedDirectoryPart = normalizedPrefix[..(lastSeparator + 1)];
            searchTerm = prefix[(lastSeparator + 1)..];
        }
        else
        {
            searchTerm = prefix;
        }

        var resolvedBase = ResolveWorkingDirectory();
        string? probeDirectory;
        if (!string.IsNullOrEmpty(normalizedDirectoryPart))
        {
            probeDirectory = TryResolveDirectory(resolvedBase, normalizedDirectoryPart);
        }
        else
        {
            probeDirectory = resolvedBase;
        }

        if (string.IsNullOrEmpty(probeDirectory) || !Directory.Exists(probeDirectory))
        {
            yield break;
        }

        var preferredSeparator = DeterminePreferredSeparator(tokenPrefix);
        var directoryTokenPrefix = NormalizeSeparators(typedDirectoryPart, preferredSeparator);
        var leading = hasLeadingQuote ? "\"" : string.Empty;

    foreach (var entry in EnumerateFileSystemEntries(probeDirectory, searchTerm))
        {
            if (directoriesOnly && !entry.IsDirectory)
            {
                continue;
            }

            var baseToken = directoryTokenPrefix;
            if (!string.IsNullOrEmpty(baseToken) && baseToken[^1] != preferredSeparator)
            {
                baseToken += preferredSeparator;
            }

            var replacement = leading + baseToken + entry.Name;
            if (entry.IsDirectory)
            {
                replacement += preferredSeparator;
            }

            yield return new AutoCompleteCandidate(tokenStart, caretIndex - tokenStart, replacement);
        }
    }

    private static IEnumerable<(string Name, bool IsDirectory)> EnumerateFileSystemEntries(string directory, string searchTerm)
    {
        var comparison = StringComparison.OrdinalIgnoreCase;

        bool Matches(string candidate)
        {
            return string.IsNullOrEmpty(searchTerm) || candidate.StartsWith(searchTerm, comparison);
        }

        IEnumerable<string> directories;
        try
        {
            directories = Directory.EnumerateDirectories(directory);
        }
        catch
        {
            directories = Array.Empty<string>();
        }

        foreach (var dir in directories)
        {
            var name = Path.GetFileName(dir);
            if (Matches(name))
            {
                yield return (name, true);
            }
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(directory);
        }
        catch
        {
            files = Array.Empty<string>();
        }

        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            if (Matches(name))
            {
                yield return (name, false);
            }
        }
    }

    private string ResolveWorkingDirectory()
    {
        if (!string.IsNullOrWhiteSpace(WorkingDirectory) && Directory.Exists(WorkingDirectory))
        {
            return WorkingDirectory;
        }

        try
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(profile) && Directory.Exists(profile))
            {
                return profile;
            }
        }
        catch
        {
            // ignored
        }

        return Environment.CurrentDirectory;
    }

    private static string? TryResolveDirectory(string baseDirectory, string relativeOrAbsolute)
    {
        try
        {
            if (Path.IsPathRooted(relativeOrAbsolute))
            {
                return Path.GetFullPath(relativeOrAbsolute);
            }

            return Path.GetFullPath(Path.Combine(baseDirectory, relativeOrAbsolute));
        }
        catch
        {
            return null;
        }
    }

    private static int FindLineStart(string text, int caretIndex)
    {
        if (caretIndex <= 0 || text.Length == 0)
        {
            return 0;
        }

        var idx = caretIndex - 1;
        while (idx >= 0)
        {
            var ch = text[idx];
            if (ch == '\n')
            {
                return idx + 1;
            }

            if (ch == '\r')
            {
                return idx + 1;
            }

            idx--;
        }

        return 0;
    }

    private static int FindTokenStart(string text, int caretIndex)
    {
        if (caretIndex <= 0)
        {
            return 0;
        }

        var index = caretIndex;
        while (index > 0)
        {
            var ch = text[index - 1];
            if (char.IsWhiteSpace(ch) || ch == '(' || ch == ')' || ch == ';')
            {
                break;
            }

            index--;
        }

        return index;
    }

    private static int FindLastPathSeparator(string text)
    {
        var lastBackslash = text.LastIndexOf('\\');
        var lastForwardSlash = text.LastIndexOf('/');
        return Math.Max(lastBackslash, lastForwardSlash);
    }

    private static bool LooksLikePathToken(string tokenPrefix)
    {
        if (string.IsNullOrEmpty(tokenPrefix))
        {
            return false;
        }

        var prefix = tokenPrefix.TrimStart('"');
        return prefix.Contains('\\') || prefix.Contains('/') || prefix.Contains(':') || prefix.StartsWith("..", StringComparison.Ordinal) || prefix.StartsWith(".", StringComparison.Ordinal);
    }

    private IEnumerable<AutoCompleteCandidate> BuildSubcommandCandidates(CommandContext context, string tokenPrefix, int tokenStart, int caretIndex)
    {
        if (context.Command is null)
        {
            yield break;
        }

        var isTypingCommand = context.Tokens.Count <= 1 && !context.LastTokenCompleted;
        if (isTypingCommand)
        {
            yield break;
        }

        if (!CommandSubcommands.Value.TryGetValue(context.Command, out var subcommands) || subcommands.Count == 0)
        {
            yield break;
        }

        var prefix = tokenPrefix.TrimStart('"');

        foreach (var subcommand in subcommands)
        {
            if (!string.IsNullOrEmpty(prefix) && !subcommand.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return new AutoCompleteCandidate(tokenStart, caretIndex - tokenStart, subcommand);
        }
    }

    private static CommandContext ExtractCommandContext(string linePrefix)
    {
        if (string.IsNullOrEmpty(linePrefix))
        {
            return CommandContext.Empty;
        }

        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var ch in linePrefix)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(ch);
            }
        }

        var lastTokenCompleted = !inQuotes && linePrefix.Length > 0 && char.IsWhiteSpace(linePrefix[^1]) && current.Length == 0;

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
            lastTokenCompleted = false;
        }

        var command = tokens.Count > 0 ? tokens[0] : null;
        return new CommandContext(command, tokens, lastTokenCompleted);
    }

    private static bool ShouldRestrictToDirectories(CommandContext context)
    {
        if (context.Command is null)
        {
            return false;
        }

        if (context.Tokens.Count <= 1 && !context.LastTokenCompleted)
        {
            return false;
        }

        return DirectoryOnlyCommands.Contains(context.Command);
    }

    private static char DeterminePreferredSeparator(string tokenPrefix)
    {
        return tokenPrefix.Contains('/') ? '/' : '\\';
    }

    private static string NormalizeSeparators(string text, char separator)
    {
        var separatorText = separator.ToString();
        return text.Replace("\\", separatorText).Replace("/", separatorText);
    }

    private static readonly string[] BuiltInCommands =
    {
        "assoc", "break", "call", "cd", "chcp", "chdir", "cls", "color", "copy", "date", "del",
        "dir", "echo", "endlocal", "erase", "exit", "for", "ftype", "goto", "if", "md", "mkdir",
        "mklink", "move", "path", "pause", "popd", "prompt", "pushd", "rd", "rem", "ren", "rename",
        "rmdir", "set", "setlocal", "shift", "start", "time", "title", "type", "ver", "verify", "vol"
    };

    private static readonly HashSet<string> DirectoryOnlyCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "cd",
        "chdir",
        "pushd"
    };

    private static readonly string[] CommandsWithDiscoverableSubcommands =
    {
        "net"
    };

    private static readonly IReadOnlyList<string> NetKnownSubcommands = new[]
    {
        "accounts",
        "computer",
        "config",
        "continue",
        "file",
        "group",
        "help",
        "helpmsg",
        "localgroup",
        "pause",
        "session",
        "share",
        "start",
        "statistics",
        "stop",
        "time",
        "use",
        "user",
        "view"
    };

    private static readonly string[] DefaultExecutableExtensions = { ".exe", ".bat", ".cmd", ".com" };

    private static readonly Lazy<HashSet<string>> ExecutableExtensions = new(
        () => LoadExecutableExtensions(),
        System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<IReadOnlyList<string>> GlobalExecutableNames = new(
        () => LoadGlobalExecutables(),
        System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<IReadOnlyList<string>> HelpCommandNames = new(
        () => LoadHelpCommands(),
        System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<Dictionary<string, IReadOnlyList<string>>> CommandSubcommands = new(
        () => LoadCommandSubcommands(),
        System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

    private static HashSet<string> LoadExecutableExtensions()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pathext = Environment.GetEnvironmentVariable("PATHEXT");
        if (!string.IsNullOrWhiteSpace(pathext))
        {
            foreach (var ext in pathext.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = ext.Trim();
                if (string.IsNullOrEmpty(trimmed))
                {
                    continue;
                }

                if (!trimmed.StartsWith(".", StringComparison.Ordinal))
                {
                    trimmed = "." + trimmed;
                }

                result.Add(trimmed);
            }
        }

        foreach (var fallback in DefaultExecutableExtensions)
        {
            result.Add(fallback);
        }

        return result;
    }

    private static IReadOnlyList<string> LoadGlobalExecutables()
    {
        var commands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var extensions = ExecutableExtensions.Value;
        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathVariable))
        {
            return Array.Empty<string>();
        }

        foreach (var segment in pathVariable.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var directory = segment.Trim();
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                continue;
            }

            try
            {
                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    var extension = Path.GetExtension(file);
                    if (!extensions.Contains(extension))
                    {
                        continue;
                    }

                    var name = Path.GetFileNameWithoutExtension(file);
                    if (!string.IsNullOrEmpty(name))
                    {
                        commands.Add(name);
                    }
                }
            }
            catch
            {
                // ignored
            }
        }

        return commands.OrderBy(static c => c, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<string> LoadHelpCommands()
    {
        var output = RunCommandAndCaptureOutput("help");
        if (string.IsNullOrWhiteSpace(output))
        {
            return Array.Empty<string>();
        }

        var commands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            if (string.IsNullOrEmpty(trimmed))
            {
                continue;
            }

            var firstSpace = trimmed.IndexOf(' ');
            var command = firstSpace > 0 ? trimmed[..firstSpace] : trimmed;
            if (command.Any(static c => char.IsLetterOrDigit(c)))
            {
                commands.Add(command.ToLowerInvariant());
            }
        }

        if (commands.Count == 0)
        {
            return Array.Empty<string>();
        }

        return commands.OrderBy(static c => c, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static Dictionary<string, IReadOnlyList<string>> LoadCommandSubcommands()
    {
        var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var command in CommandsWithDiscoverableSubcommands)
        {
            var output = RunCommandAndCaptureOutput(command, "/?");
            if (string.IsNullOrWhiteSpace(output))
            {
                output = string.Empty;
            }

            IReadOnlyList<string> subcommands = Array.Empty<string>();

            if (command.Equals("net", StringComparison.OrdinalIgnoreCase))
            {
                var parsed = ParseNetSubcommands(output);
                if (parsed.Count == 0)
                {
                    subcommands = NetKnownSubcommands;
                }
                else
                {
                    var merged = new HashSet<string>(parsed, StringComparer.OrdinalIgnoreCase);
                    foreach (var fallback in NetKnownSubcommands)
                    {
                        merged.Add(fallback);
                    }

                    subcommands = merged.OrderBy(static s => s, StringComparer.OrdinalIgnoreCase).ToArray();
                }
            }

            if (subcommands.Count > 0)
            {
                map[command] = subcommands;
            }
        }

        return map;
    }

    private static IReadOnlyList<string> ParseNetSubcommands(string helpOutput)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in Regex.Matches(helpOutput, "\\[(.*?)\\]", RegexOptions.Singleline))
        {
            var content = match.Groups[1].Value;
            var tokens = content.Split(new[] { '|', '\r', '\n', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in tokens)
            {
                var cleaned = token.Trim();
                if (string.IsNullOrEmpty(cleaned))
                {
                    continue;
                }

                if (cleaned.Equals("NET", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (Regex.IsMatch(cleaned, "^[A-Z][A-Z0-9-]*$", RegexOptions.IgnoreCase))
                {
                    names.Add(cleaned.ToLowerInvariant());
                }
            }
        }

        if (names.Count == 0)
        {
            var lines = helpOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var fallbackMatch = Regex.Match(line, "^\\s*([A-Z][A-Z0-9-]+)\\b", RegexOptions.IgnoreCase);
                if (!fallbackMatch.Success)
                {
                    continue;
                }

                names.Add(fallbackMatch.Groups[1].Value.ToLowerInvariant());
            }
        }

        if (names.Count == 0)
        {
            return Array.Empty<string>();
        }

        return names.OrderBy(static n => n, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string? RunCommandAndCaptureOutput(string command, string? arguments = null)
    {
        try
        {
            var interpreter = Environment.GetEnvironmentVariable("COMSPEC");
            if (string.IsNullOrWhiteSpace(interpreter))
            {
                interpreter = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            }

            if (string.IsNullOrWhiteSpace(interpreter) || !File.Exists(interpreter))
            {
                return null;
            }

            var psi = new ProcessStartInfo
            {
                FileName = interpreter,
                Arguments = $"/c {command}{(string.IsNullOrEmpty(arguments) ? string.Empty : " " + arguments)}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(2000);
            return output;
        }
        catch
        {
            return null;
        }
    }

    private readonly struct CommandContext
    {
        public static readonly CommandContext Empty = new(null, Array.Empty<string>(), false);

        public CommandContext(string? command, IReadOnlyList<string> tokens, bool lastTokenCompleted)
        {
            Command = command;
            Tokens = tokens;
            LastTokenCompleted = lastTokenCompleted;
        }

        public string? Command { get; }
        public IReadOnlyList<string> Tokens { get; }
        public bool LastTokenCompleted { get; }
    }

    private readonly struct AutoCompleteCandidate
    {
        public AutoCompleteCandidate(int start, int length, string replacement)
        {
            Start = start;
            Length = length;
            Replacement = replacement;
        }

        public int Start { get; }
        public int Length { get; }
        public string Replacement { get; }
    }

    private sealed class AutoCompleteSession
    {
        public AutoCompleteSession(string baseText, int baseCaretIndex, IReadOnlyList<AutoCompleteCandidate> candidates)
        {
            BaseText = baseText;
            BaseCaretIndex = baseCaretIndex;
            Candidates = candidates;
            CurrentIndex = -1;
            LastText = baseText;
            LastCaretIndex = baseCaretIndex;
        }

        public string BaseText { get; }
        public int BaseCaretIndex { get; }
        public IReadOnlyList<AutoCompleteCandidate> Candidates { get; }
        public int CurrentIndex { get; set; }
        public string LastText { get; private set; }
        public int LastCaretIndex { get; private set; }

        public bool CanReuse(string text, int caretIndex)
        {
            if (CurrentIndex == -1)
            {
                return text == BaseText && caretIndex == BaseCaretIndex;
            }

            return text == LastText && caretIndex == LastCaretIndex;
        }

        public (string Text, int CaretIndex) ApplyCandidate(int index)
        {
            var candidate = Candidates[index];
            var before = BaseText[..candidate.Start];
            var after = BaseText[(candidate.Start + candidate.Length)..];
            var text = string.Concat(before, candidate.Replacement, after);
            var caret = candidate.Start + candidate.Replacement.Length;

            LastText = text;
            LastCaretIndex = caret;
            return (text, caret);
        }
    }
}
